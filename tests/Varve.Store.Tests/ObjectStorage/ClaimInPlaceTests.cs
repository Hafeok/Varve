// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
                            late = await writer.CommitAsync(new CommitRequest().Assert(T.Iri("late"), T.Iri("p"), T.Iri("o")), T.Ct);
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
                await writer.CommitAsync(assert ? new CommitRequest().Assert(quad.S, quad.P, quad.O, quad.G) : new CommitRequest().Retract(quad.S, quad.P, quad.O, quad.G), T.Ct);
                break;
        }
    }

    private static string Show((int Kind, int Subject, bool Assert)[] steps) =>
        string.Join(" ", steps.Select(step => step.Kind + ":" + step.Subject + (step.Assert ? "+" : "-")));
}
