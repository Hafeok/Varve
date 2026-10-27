// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A failing cache write never abdicates (#146; ADR 0179 point 6). Over
/// generated schedules of commits, consolidations, checkpoints and reads by
/// the writer and a follower, every write to a local cache fails from any
/// step on, for the segment copies, the extent pages or both. Throughout:
/// - every commit is acknowledged, as it is without the fault;
/// - the writer keeps its epoch, its appends never fail, and it is neither
///   failed nor spilling, so no host would release the epoch;
/// - every read, the writer's and the follower's, at every position, equals
///   the reference model;
/// - the kinds that failed, and only those, are reported off.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class CacheFaultTests
{
    // The land gate's count; VARVE_CACHE_FAULT_SCHEDULES raises it nightly (docs/testing.md §8).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_CACHE_FAULT_SCHEDULES"), out int n) ? n : 40;

    private static DatasetOptions Small() => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    [Fact]
    public async Task a_failing_cache_write_never_abdicates_and_every_read_equals_the_model()
    {
        Gen<(int Kind, int Subject, bool Assert)[]> schedules = Gen.Select(Gen.Int[0, 9], Gen.Int[0, 7], Gen.Bool).Array[1, 50];

        await schedules.SampleAsync(
            RunAsync,
            iter: Schedules,
            print: schedule => string.Join(" ", schedule.Select(step => step.Kind + ":" + step.Subject + (step.Assert ? "+" : "-"))));
    }

    // One schedule: the writer, a follower, the faults armed, and the model.
    private static async Task RunAsync((int Kind, int Subject, bool Assert)[] schedule)
    {
        LocalCache failing = LocalCache.None;
        await using ObjectHarness objects = new(waits: new InstantClock(DateTimeOffset.UnixEpoch)) { CacheFault = kind => (failing & kind) != 0 };
        List<SortedSet<string>> model = [[]];
        SortedSet<string> current = [];
        Backend written = await objects.OpenAsync(instance: "writer");
        Dataset writer = await T.OpenOrCreate(written, Small());
        Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        Dataset follower = await Dataset.OpenFollowerAsync(followed, Small(), T.Ct);

        try
        {
            foreach ((int kind, int subject, bool assert) in schedule)
            {
                switch (kind)
                {
                    case 4:
                        // A consolidation writes a segment's copy.
                        await written.MaintainStorageAsync(T.Ct);
                        break;
                    case 5 when writer.Head.Value > 0:
                        // A checkpoint's extents enter the page cache as they are published.
                        await writer.CheckpointAsync(writer.Head, T.Ct);
                        break;
                    case 6:
                        await SameAsModelAsync(writer, model);
                        break;
                    case 7:
                        await follower.AdvanceAsync(T.Ct);
                        await SameAsModelAsync(follower, model);
                        break;
                    case 8:
                        // A follower opened again reads the catalog's extents and the log's copies afresh.
                        await follower.DisposeAsync();
                        followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
                        follower = await Dataset.OpenFollowerAsync(followed, Small(), T.Ct);
                        await SameAsModelAsync(follower, model);
                        break;
                    case 9:
                        // From here on the disk refuses this kind's writes.
                        failing |= (subject % 3) switch { 0 => LocalCache.SegmentCopies, 1 => LocalCache.ExtentPages, _ => LocalCache.SegmentCopies | LocalCache.ExtentPages };
                        break;
                    case 5:
                        break;
                    default:
                        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm G) quad = (T.Iri("s" + subject), T.Iri("p"), T.Literal(new string('o', 40 + subject)), T.Iri("g" + (subject % 3)));
                        CommitResult result = await writer.CommitAsync(assert ? new CommitRequestBuilder().Assert(quad.S, quad.P, quad.O, quad.G) : new CommitRequestBuilder().Retract(quad.S, quad.P, quad.O, quad.G), T.Ct);

                        // Acknowledged as without the fault: a commit writes no local cache.
                        Assert.True(result.Outcome is CommitOutcome.Committed or CommitOutcome.NoChange, result.Outcome + ": " + result.Reason);

                        if (result.Outcome == CommitOutcome.Committed)
                        {
                            string rendered = T.Render(quad.S) + " " + T.Render(quad.P) + " " + T.Render(quad.O) + " " + T.Render(quad.G);
                            current = [.. current];

                            if (assert)
                            {
                                current.Add(rendered);
                            }
                            else
                            {
                                current.Remove(rendered);
                            }

                            model.Add(current);
                        }

                        break;
                }

                // Nothing a host would release the epoch for.
                Assert.False(writer.AppendFailed);
                Assert.False(writer.IsFailed);
                Assert.Null(writer.SpillFailure);
                Assert.False(written.IsSuperseded);
                Assert.Equal(1, written.Epoch!.Value.Value);
                Assert.Equal(LocalCache.None, (written.CacheOff | followed.CacheOff) & ~failing);
            }

            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(model.Count - 1, follower.Head.Value);
            await SameAsModelAsync(follower, model);
            await SameAsModelAsync(writer, model);

            // The epoch history is the one claim: no handover.
            WriterRecord record = (await Backend.ReadWriterAsync(objects.Store, objects.Prefix, T.Ct))!;
            Assert.Single(record.History);
            Assert.Null(record.Released);
        }
        finally
        {
            await follower.DisposeAsync();
            await writer.DisposeAsync();
        }
    }

    // At every position the dataset has, it holds what the model says.
    private static async Task SameAsModelAsync(Dataset dataset, List<SortedSet<string>> model)
    {
        long head = dataset.Head.Value;
        Assert.InRange(head, 0, model.Count - 1);

        for (long p = 0; p <= head; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), T.Ct);
            Assert.Equal(model[(int)p], T.Terms(view));
        }
    }
}
