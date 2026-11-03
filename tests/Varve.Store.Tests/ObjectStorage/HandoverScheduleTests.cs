// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Handover within a store (ADR 0179; #97's gate, #165): three instances
/// over one store, a writer and two standbys, under generated schedules of:
/// - writes sent to any instance, a standby forwarding on the route it last
///   read, which a handover may have made stale;
/// - abdications, for shutdown or storage-error, raced by every standby at
///   once;
/// - probe-triggered takeovers, including false ones against a writer that
///   is alive and stalled;
/// - late and duplicated triggers, naming an old epoch or the same one twice;
/// - <c>server-busy</c> rounds at the writer;
/// - lost answers to the store's creates, a partition's lost responses;
/// - standbys advancing, and superseded writers rejoining as standbys.
///
/// Every schedule shows, from a reader opened afresh at the end:
/// - no acknowledged commit is lost;
/// - positions are contiguous, and each is written once;
/// - the header chain verifies across epochs: the reader opens;
/// - a superseded writer appends nothing after the fence;
/// - no trigger for an old epoch changes the writer;
/// - <c>server-busy</c> never causes a handover;
/// - a write forwarded across a handover commits exactly once, or is refused
///   as <c>writer-moved</c> and is in no position.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class HandoverScheduleTests
{
    // The land gate's count; VARVE_HANDOVER_SCHEDULES raises it nightly (docs/testing.md §8).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_HANDOVER_SCHEDULES"), out int n) ? n : 40;

    private static readonly string[] Names = ["a", "b", "c"];

    private static DatasetOptions Small() => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    [Fact]
    public async Task every_handover_schedule_keeps_the_acknowledged_commits_once_each_in_one_verified_log()
    {
        Gen<(int Kind, int Subject, bool Flag)[]> schedules = Gen.Select(Gen.Int[0, 12], Gen.Int[0, 5], Gen.Bool).Array[1, 60];

        await schedules.SampleAsync(
            RunAsync,
            iter: Schedules,
            print: schedule => string.Join(" ", schedule.Select(step => step.Kind + ":" + step.Subject + (step.Flag ? "+" : "-"))));
    }

    private static async Task RunAsync((int Kind, int Subject, bool Flag)[] schedule)
    {
        MemoryObjectStore store = new();
        int? lose = null;

        // A partition: the answer to the store's create at the countdown is lost after it was applied.
        store.Hook = (operation, _) =>
        {
            if (operation != ObjectOperation.Create || lose is not { } countdown)
            {
                return null;
            }

            if (countdown > 0)
            {
                lose = countdown - 1;
                return null;
            }

            lose = null;
            return () => throw new IOException("injected: the create's answer was lost");
        };

        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        Fleet fleet = await Fleet.StartAsync(objects);
        HashSet<string> acknowledged = [];
        HashSet<string> unknown = [];
        HashSet<string> refused = [];
        int writes = 0;

        try
        {
            foreach ((int kind, int subject, bool flag) in schedule)
            {
                long before = (await fleet.RecordAsync()).Epoch.Value;

                switch (kind)
                {
                    case <= 3:
                    {
                        // A write sent to an instance: the writer commits it; any other forwards on its route.
                        string id = "w" + (writes++).ToString(CultureInfo.InvariantCulture);
                        Instance target = fleet.RouteFrom(fleet.Instances[subject % Names.Length]);

                        switch (await target.WriteAsync(id))
                        {
                            case WriteOutcome.Committed:
                                acknowledged.Add(id);
                                break;
                            case WriteOutcome.Unknown:
                                unknown.Add(id);
                                break;
                            default:
                                // writer-moved: never committed, and the route is read again.
                                refused.Add(id);
                                Fleet.Stale(fleet.Instances[subject % Names.Length]);
                                break;
                        }

                        break;
                    }

                    case 4:
                        // server-busy at the writer: a load signal, never a trigger.
                        await fleet.StandbyRoundAsync();
                        Assert.Equal(before, (await fleet.RecordAsync()).Epoch.Value);
                        break;
                    case 5:
                        await fleet.AbdicateAsync(flag ? ReleaseReason.Shutdown : ReleaseReason.StorageError);
                        break;
                    case 6:
                        // A probe trigger against the writer, which may be alive and only stalled.
                        await fleet.TakeoverAsync(subject, new WriterEpoch(before), concurrently: flag);
                        break;
                    case 7 when before > 1:
                    {
                        // A late trigger for an epoch already gone: a no-op.
                        WriterRecord record = await fleet.RecordAsync();
                        await fleet.TakeoverAsync(subject, new WriterEpoch(before - 1), concurrently: flag);
                        WriterRecord after = await fleet.RecordAsync();
                        Assert.Equal(record.Epoch, after.Epoch);
                        Assert.Equal(record.Holder, after.Holder);
                        break;
                    }

                    case 8:
                        lose = subject;
                        break;
                    case 9:
                        await fleet.StandbyRoundAsync();
                        break;
                    case 10:
                        await fleet.RejoinAsync();
                        break;
                    case 11:
                        // A writer a takeover deposed, still holding its old open, tries to append: the fence refuses it.
                        foreach (Instance stale in fleet.Deposed.Where(instance => instance.Dataset is { IsFollower: false }).ToArray())
                        {
                            string id = "w" + (writes++).ToString(CultureInfo.InvariantCulture);
                            Assert.NotEqual(WriteOutcome.Committed, await stale.WriteAsync(id));
                            refused.Add(id);
                        }

                        break;
                    default:
                        break;
                }
            }

            lose = null;

            // A reader opened afresh: its open verifies the header chain across every epoch.
            await using Dataset reader = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);
            long head = reader.Head.Value;
            Dictionary<string, long> written = [];
            SortedSet<string> previous = [];

            for (long p = 1; p <= head; p++)
            {
                using DatasetView view = await reader.AsOfAsync(new Position(p), T.Ct);
                SortedSet<string> now = T.Terms(view);
                string[] added = [.. now.Except(previous)];

                // Each position is one commit, of one write: contiguous, and each written once.
                string only = Assert.Single(added);
                Assert.Empty(previous.Except(now));
                string id = Id(only);
                Assert.True(written.TryAdd(id, p), id + " is at positions " + (written.TryGetValue(id, out long first) ? first : 0) + " and " + p);
                previous = now;
            }

            Assert.Empty(acknowledged.Except(written.Keys));
            Assert.Empty(refused.Intersect(written.Keys));
            Assert.Empty(written.Keys.Except(acknowledged).Except(unknown));
        }
        finally
        {
            await fleet.DisposeAsync();
        }
    }

    private static string Id(string rendered)
    {
        int start = rendered.IndexOf("example.org/", StringComparison.Ordinal) + "example.org/".Length;
        return rendered[start..rendered.IndexOf('>', start)];
    }

    private enum WriteOutcome
    {
        Committed,
        Refused,
        Unknown,
    }

    /// <summary>One instance: its storage, and the dataset it holds, as the writer or a standby.</summary>
    private sealed class Instance(string name, ObjectHarness objects)
    {
        internal string Name { get; } = name;

        internal Backend? Storage { get; private set; }

        internal Dataset? Dataset { get; private set; }

        internal bool Writes => Dataset is { IsFollower: false } && Storage is { IsSuperseded: false, Epoch: not null };

        // Opened for write and since superseded: it holds its old open until it rejoins.
        internal bool Superseded => Dataset is { IsFollower: false } && Storage is { IsSuperseded: true };

        /// <summary>The route this instance last read, as the epoch and the instance it named; none before the first read.</summary>
        internal Instance? Route { get; set; }

        internal async Task OpenWriterAsync()
        {
            Storage = await objects.OpenAsync(instance: Name);
            Dataset = await T.OpenOrCreate(Storage, Small());
        }

        internal async Task OpenStandbyAsync()
        {
            await CloseAsync();
            Storage = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
            Dataset = await Dataset.OpenFollowerAsync(Storage, Small(), T.Ct);
        }

        internal async Task CloseAsync(ReleaseReason reason = ReleaseReason.Shutdown)
        {
            if (Storage is { } storage)
            {
                storage.ReleaseOnClose = reason;
            }

            if (Dataset is { } dataset)
            {
                await dataset.DisposeAsync();
            }

            if (Storage is { } held)
            {
                await held.DisposeAsync();
            }

            Dataset = null;
            Storage = null;
        }

        internal async Task<WriteOutcome> WriteAsync(string id)
        {
            if (Dataset is not { } dataset)
            {
                return WriteOutcome.Refused;
            }

            try
            {
                CommitResult result = await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri(id), T.Iri("p"), T.Iri("o")), T.Ct);
                return result.Outcome == CommitOutcome.Committed ? WriteOutcome.Committed : WriteOutcome.Refused;
            }
            catch (WriterSupersededException)
            {
                return WriteOutcome.Refused;
            }
            catch (IOException)
            {
                // The batch's outcome is unknown to its client; the commits resource resolves it.
                return WriteOutcome.Unknown;
            }
        }

        // A standby's advance; a failed one leaves it serving what it applied.
        internal async Task AdvanceAsync()
        {
            if (Dataset is { IsFollower: true } dataset)
            {
                try
                {
                    await dataset.AdvanceAsync(T.Ct);
                }
                catch (IOException)
                {
                }
            }
        }

        // A claim in place, guarded by the epoch named. A loser opens again as a standby.
        internal async Task<bool> ClaimAsync(WriterEpoch from, ClaimTrigger trigger)
        {
            if (Dataset is not { IsFollower: true } dataset || Storage is not { } storage)
            {
                return false;
            }

            bool won;

            try
            {
                won = await dataset.TakeWriteAsync(claim => storage.ClaimInPlaceAsync(new WriterInstance(Name), null, TestRandomness.Instance, trigger, from, claim), T.Ct);
            }
            catch (IOException)
            {
                won = false;
            }

            if (!won)
            {
                await OpenStandbyAsync();
            }

            return won;
        }
    }

    /// <summary>The three instances, and what a host does for them: routes, standby rounds, releases, takeovers.</summary>
    private sealed class Fleet : IAsyncDisposable
    {
        private readonly ObjectHarness _objects;

        private Fleet(ObjectHarness objects, Instance[] instances)
        {
            _objects = objects;
            Instances = instances;
        }

        internal Instance[] Instances { get; }

        /// <summary>The writers a takeover deposed while they were alive, until they rejoin.</summary>
        internal HashSet<Instance> Deposed { get; } = [];

        internal static async Task<Fleet> StartAsync(ObjectHarness objects)
        {
            Instance[] instances = [.. Names.Select(name => new Instance(name, objects))];
            await instances[0].OpenWriterAsync();

            foreach (Instance standby in instances[1..])
            {
                await standby.OpenStandbyAsync();
            }

            return new Fleet(objects, instances);
        }

        internal async Task<WriterRecord> RecordAsync() => (await Backend.ReadWriterAsync(_objects.Store, _objects.Prefix, T.Ct))!;

        // Where a write sent to this instance goes: itself if it writes, else the instance its route names.
        internal Instance RouteFrom(Instance from)
        {
            if (from.Writes)
            {
                return from;
            }

            from.Route ??= Instances.FirstOrDefault(instance => instance.Writes) ?? from;
            return from.Route;
        }

        internal static void Stale(Instance from) => from.Route = null;

        // Each standby advances and reads the record; a release its reason lets it race for, every warm one claims.
        internal async Task StandbyRoundAsync()
        {
            foreach (Instance standby in Instances)
            {
                await standby.AdvanceAsync();
            }

            WriterRecord record = await RecordAsync();

            if (record.Released is ReleaseReason.Shutdown or ReleaseReason.StorageError)
            {
                await RaceAsync(record.Epoch, ClaimTrigger.Abdication, Instances.Where(instance => instance.Dataset is { IsFollower: true }));
            }
        }

        // The writer releases its epoch and stands by; the standbys race for it.
        internal async Task AbdicateAsync(ReleaseReason reason)
        {
            if (Instances.FirstOrDefault(instance => instance.Writes) is not { } writer)
            {
                return;
            }

            // It may be a writer a takeover deposed, which does not know yet: its release then fails, and changes nothing.
            await writer.CloseAsync(reason);
            Deposed.Remove(writer);
            await writer.OpenStandbyAsync();
            await StandbyRoundAsync();
        }

        // A takeover request to one standby, or to every one at once (a duplicated trigger).
        internal async Task TakeoverAsync(int subject, WriterEpoch from, bool concurrently)
        {
            IEnumerable<Instance> candidates = concurrently
                ? Instances.Where(instance => instance.Dataset is { IsFollower: true })
                : [Instances[subject % Names.Length]];
            await RaceAsync(from, ClaimTrigger.Probe, candidates);
        }

        // A superseded writer rejoins as a standby; a dataset with no writer at all is claimed by a restart.
        internal async Task RejoinAsync()
        {
            foreach (Instance stale in Instances.Where(instance => instance.Superseded || instance.Dataset is null || (Deposed.Contains(instance) && !instance.Writes)))
            {
                await stale.OpenStandbyAsync();
                Deposed.Remove(stale);
            }

            WriterRecord record = await RecordAsync();

            if (!Instances.Any(instance => instance.Writes) && record.Released is null && Instances.FirstOrDefault(instance => instance.Name == record.Holder.Value) is { } holder)
            {
                // The record's holder crashed with its claim: its restart claims again (ADR 0136).
                await holder.CloseAsync();
                await holder.OpenWriterAsync();
            }
        }

        private async Task RaceAsync(WriterEpoch from, ClaimTrigger trigger, IEnumerable<Instance> candidates)
        {
            Instance[] racing = [.. candidates];
            Instance[] writing = [.. Instances.Where(instance => instance.Writes)];
            bool[] won = await Task.WhenAll(racing.Select(candidate => candidate.ClaimAsync(from, trigger)));

            // The store's conditional write picks at most one.
            Assert.InRange(won.Count(claimed => claimed), 0, 1);

            if (won.Any(claimed => claimed))
            {
                Deposed.UnionWith(writing);
                Deposed.ExceptWith(racing.Where((_, i) => won[i]));
            }

            foreach (Instance instance in Instances)
            {
                instance.Route = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (Instance instance in Instances)
            {
                await instance.CloseAsync();
            }
        }
    }
}
