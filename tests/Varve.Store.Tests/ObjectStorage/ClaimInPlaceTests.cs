// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The claim in place (ADR 0179 point 8; the maintainer's answer on #165):
/// over generated histories, a follower takes the epoch over at any point and
/// writes on, and it answers every read equally to a dataset re-opened from
/// the catalog, as of every position after the claim. The writer it took
/// over from, closed or stalled, appends nothing after the fence.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ClaimInPlaceTests
{
    // The land gate's count; VARVE_CLAIM_IN_PLACE_SCHEDULES raises it nightly (docs/testing.md §8).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_CLAIM_IN_PLACE_SCHEDULES"), out int n) ? n : 40;

    private static DatasetOptions Small() => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    [Fact]
    public async Task a_claim_in_place_and_a_reopen_from_the_catalog_give_equal_datasets_at_every_position_after_it()
    {
        Gen<((int Kind, int Subject, bool Assert)[] Before, (int Kind, int Subject, bool Assert)[] After, bool Graceful)> schedules =
            Gen.Select(Gen.Select(Gen.Int[0, 6], Gen.Int[0, 7], Gen.Bool).Array[0, 30], Gen.Select(Gen.Int[0, 6], Gen.Int[0, 7], Gen.Bool).Array[0, 30], Gen.Bool);

        await schedules.SampleAsync(
            async schedule =>
            {
                await using ObjectHarness objects = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
                Backend written = await objects.OpenAsync(instance: "a");
                Dataset writer = await T.OpenOrCreate(written, Small());
                Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly, instance: "b");
                Dataset follower = await Dataset.OpenFollowerAsync(followed, Small(), T.Ct);

                try
                {
                    foreach ((int kind, int subject, bool assert) in schedule.Before)
                    {
                        await StepAsync(writer, written, kind, subject, assert, follower);
                    }

                    if (schedule.Graceful)
                    {
                        await writer.DisposeAsync();
                    }

                    WriterEpoch from = (await Backend.ReadWriterAsync(objects.Store, objects.Prefix, T.Ct))!.Epoch;
                    long claimedAt = writer.Head.Value;
                    Assert.True(await follower.TakeWriteAsync(ct => followed.ClaimInPlaceAsync(new WriterInstance("b"), null, TestRandomness.Instance, ClaimTrigger.Probe, from, ct), T.Ct));
                    Assert.False(follower.IsFollower);
                    Assert.Equal(claimedAt, follower.Head.Value);
                    Assert.Equal(from.Value + 1, followed.Epoch!.Value.Value);

                    // The writer it took over from appends nothing after the fence.
                    if (!schedule.Graceful)
                    {
                        CommitResult late;

                        try
                        {
                            late = await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("late"), T.Iri("p"), T.Iri("o")), T.Ct);
                        }
                        catch (WriterSupersededException)
                        {
                            late = CommitResult.Unavailable(writer.Head.Value, "superseded");
                        }

                        Assert.NotEqual(CommitOutcome.Committed, late.Outcome);
                    }

                    foreach ((int kind, int subject, bool assert) in schedule.After)
                    {
                        await StepAsync(follower, followed, kind, subject, assert, null);
                    }

                    // A re-open from the catalog, against the dataset claimed in place.
                    await using Dataset reopened = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);
                    Assert.Equal(follower.Head, reopened.Head);

                    for (long p = claimedAt; p <= follower.Head.Value; p++)
                    {
                        using DatasetView expected = await reopened.AsOfAsync(new Position(p), T.Ct);
                        using DatasetView actual = await follower.AsOfAsync(new Position(p), T.Ct);
                        Assert.Equal(T.Terms(expected), T.Terms(actual));
                    }

                    using (DatasetView expected = reopened.Pin())
                    using (DatasetView actual = follower.Pin())
                    {
                        Assert.Equal(T.Terms(expected), T.Terms(actual));
                    }
                }
                finally
                {
                    await follower.DisposeAsync();

                    if (!schedule.Graceful)
                    {
                        await writer.DisposeAsync();
                    }
                }
            },
            iter: Schedules,
            print: schedule => Show(schedule.Item1) + " | " + Show(schedule.Item2) + (schedule.Item3 ? " closed" : " stalled"));
    }

    /// <summary>
    /// The claim in place reaches the write-open's verdict over a log whose
    /// newest segment is consolidated (ADR 0136 decision 2, #181). The
    /// follower wins epoch 2 in the record; before it fences, b claims 3,
    /// commits until segment 0 is sealed and consolidated, and stops before it
    /// opens segment 1. With the seal's deletes pending, the refresh adopts
    /// the seal; with them done, it adopts the consolidated segment, which
    /// holds no chunk, and the claim reads the log's listed tail. Either way
    /// the follower is superseded, as a write-open of epoch 2 is, and fences
    /// nothing after epoch 3's seal.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_claim_in_place_over_a_segment_a_newer_epoch_sealed_and_consolidated_is_superseded_as_a_write_open_is(bool deleted)
    {
        MemoryObjectStore store = new();
        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        await using (Dataset first = await T.OpenOrCreate(await objects.OpenAsync(instance: "first"), Small()))
        {
            await first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly, instance: "f");
        await using Dataset follower = await Dataset.OpenFollowerAsync(followed, Small(), T.Ct);
        WriterEpoch from = (await Backend.ReadWriterAsync(store, objects.Prefix, T.Ct))!.Epoch;

        ObjectName writerRecord = new((objects.Prefix is { } prefix ? prefix.Value : string.Empty) + "writer");
        const string Next = "log/00000001.chunks/00000000";
        const string Consolidated = "log/00000000.seg";
        bool armed = true;
        bool failing = false;
        Dataset? stopped = null;
        store.Hook = (operation, name) =>
        {
            if (failing && operation == ObjectOperation.Create && name.Value.EndsWith(Next, StringComparison.Ordinal))
            {
                throw new System.IO.IOException("The writer of epoch 3 stops before it opens the next segment.");
            }

            if (!armed || operation is not (ObjectOperation.Create or ObjectOperation.Replace) || name != writerRecord)
            {
                return null;
            }

            // After the follower's claim of epoch 2 lands, before its refresh and fence.
            armed = false;
            return () =>
            {
                failing = true;
                stopped = Task.Run(async () =>
                {
                    Backend storage = await objects.OpenAsync(instance: "b");
                    Dataset opened = await Dataset.OpenAsync(storage, Small(), T.Ct);

                    for (int i = 0; await ConditionalWrites.ReadWholeAsync(store, new ObjectName(Prefixed(objects, Consolidated)), T.Ct) is null; i++)
                    {
                        Assert.True(i < 100, "segment 0 was never sealed and consolidated");

                        try
                        {
                            await opened.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 100))), T.Ct);
                        }
                        catch (System.IO.IOException)
                        {
                            // The commit after the seal, whose segment b could not open: b stops here.
                        }
                    }

                    if (deleted)
                    {
                        await storage.MaintainStorageAsync(T.Ct);
                    }

                    return opened;
                }).GetAwaiter().GetResult();
                failing = false;
            };
        };

        bool won;

        try
        {
            won = await follower.TakeWriteAsync(ct => followed.ClaimInPlaceAsync(new WriterInstance("f"), null, TestRandomness.Instance, ClaimTrigger.Probe, from, ct), T.Ct);
        }
        finally
        {
            store.Hook = null;
        }

        await using Dataset b = stopped!;
        Assert.Equal(from.Value + 2, (await Backend.ReadWriterAsync(store, objects.Prefix, T.Ct))!.Epoch.Value);
        Assert.True(await ConditionalWrites.ReadWholeAsync(store, new ObjectName(Prefixed(objects, Next)), T.Ct) is null, "the follower, of epoch 2, fenced the next segment after epoch 3's seal");
        Assert.False(won);
        Assert.True(followed.IsSuperseded);
        Assert.True(follower.IsFollower);
    }

    private static string Prefixed(ObjectHarness objects, string name) => (objects.Prefix is { } prefix ? prefix.Value : string.Empty) + name;

    // One step of a history: a commit, a seal's consolidation, a checkpoint,
    // or an advance of the follower beside the writer.
    private static async Task StepAsync(Dataset writer, Backend storage, int kind, int subject, bool assert, Dataset? follower)
    {
        switch (kind)
        {
            case 4:
                await storage.MaintainStorageAsync(T.Ct);
                break;
            case 5 when writer.Head.Value > 0:
                await writer.CheckpointAsync(writer.Head, T.Ct);
                break;
            case 6 when follower is not null:
                await follower.AdvanceAsync(T.Ct);
                break;
            case 5 or 6:
                break;
            default:
                (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) quad = (T.Iri("s" + subject), T.Iri("p"), T.Literal(new string('o', 40 + subject)), T.Iri("g" + (subject % 3)));
                await writer.CommitAsync(assert ? new CommitRequestBuilder().Assert(quad.S, quad.P, quad.O, quad.G) : new CommitRequestBuilder().Retract(quad.S, quad.P, quad.O, quad.G), T.Ct);
                break;
        }
    }

    private static string Show((int Kind, int Subject, bool Assert)[] steps) =>
        string.Join(" ", steps.Select(step => step.Kind + ":" + step.Subject + (step.Assert ? "+" : "-")));
}
