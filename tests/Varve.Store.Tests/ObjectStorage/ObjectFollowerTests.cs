// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The tier-1 follower (ADR 0164): a read-only open of a dataset another
/// process writes, advanced by probing the next log slot.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectFollowerTests
{
    // Sized for the land gate: each history is a few dozen commits with
    // seals and consolidations, about a tenth of a second.
    private const int Iterations = 60;

    private static DatasetOptions Small() => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    /// <summary>
    /// For generated histories of commits, settings changes, seals and
    /// consolidations, a follower advanced at arbitrary points is at its
    /// writer's head with its writer's state, at the head and at every
    /// position before it, whatever it missed in between.
    /// </summary>
    [Fact]
    public async Task a_follower_advanced_at_any_point_has_its_writers_state_at_every_position()
    {
        Gen<(int Kind, int Subject, bool Assert)[]> steps = Gen.Select(Gen.Int[0, 5], Gen.Int[0, 7], Gen.Bool).Array[1, 50];

        await steps.SampleAsync(
            async history =>
            {
                await using ObjectHarness objects = new();
                Backend written = await objects.OpenAsync();
                await using Dataset writer = await T.OpenOrCreate(written, Small());
                await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

                foreach ((int kind, int subject, bool assert) in history)
                {
                    switch (kind)
                    {
                        case 4:
                            await written.MaintainStorageAsync(T.Ct);
                            break;
                        case 5:
                            await follower.AdvanceAsync(T.Ct);
                            await SameAsync(writer, follower, follower.Head);
                            break;
                        case 3 when subject == 0:
                            await writer.ChangeSettingsAsync(
                                new SettingsChange { DefaultAccessScope = assert ? AccessScope.Current : AccessScope.AllHistory },
                                new CommitMetadata { Agent = T.Iri("me"), Cause = T.Literal("why") },
                                cancellationToken: T.Ct);
                            break;
                        default:
                            CommitRequest request = new();
                            request = assert
                                ? request.Assert(T.Iri("s" + subject), T.Iri("p"), T.Literal(new string('o', 40 + subject)), T.Iri("g" + (subject % 3)))
                                : request.Retract(T.Iri("s" + subject), T.Iri("p"), T.Literal(new string('o', 40 + subject)), T.Iri("g" + (subject % 3)));
                            await writer.CommitAsync(request, T.Ct);
                            break;
                    }
                }

                await follower.AdvanceAsync(T.Ct);
                Assert.Equal(writer.Head, follower.Head);

                for (long p = 0; p <= writer.Head.Value; p++)
                {
                    await SameAsync(writer, follower, new Position(p));
                }
            },
            iter: Iterations,
            print: history => string.Join(" ", history.Select(step => step.Kind + ":" + step.Subject + (step.Assert ? "+" : "-"))));
    }

    /// <summary>
    /// An idle refresh costs two GETs, the next slot and its segment's
    /// consolidated object, both absent, and lists nothing (ADR 0164).
    /// </summary>
    [Fact]
    public async Task an_idle_refresh_is_two_gets_and_lists_nothing()
    {
        await using ObjectHarness objects = new();
        await using Dataset writer = await T.OpenOrCreate(await objects.OpenAsync(), Small());
        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

        objects.Store.ResetCounts();
        Assert.Equal(writer.Head, await follower.AdvanceAsync(T.Ct));
        Assert.Equal(2, objects.Store.Count(ObjectOperation.Read));
        Assert.Equal(0, objects.Store.Count(ObjectOperation.List));

        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s2"), T.Iri("p"), T.Iri("o")), T.Ct);
        objects.Store.ResetCounts();
        Assert.Equal(writer.Head, await follower.AdvanceAsync(T.Ct));
        Assert.Equal(0, objects.Store.Count(ObjectOperation.List));
        Assert.Equal(0, objects.Store.Count(ObjectOperation.Create) + objects.Store.Count(ObjectOperation.Replace) + objects.Store.Count(ObjectOperation.Delete));
    }

    /// <summary>
    /// A follower learns from its own refresh that the writer changed (ADR
    /// 0179 point 12): the highest epoch in a chunk header it adopted is the
    /// first writer's, and the second's once that one has fenced the log and
    /// committed. No record is read for it.
    /// </summary>
    [Fact]
    public async Task a_followers_refresh_reports_the_highest_epoch_it_adopted()
    {
        await using ObjectHarness objects = new();
        Backend first = await objects.OpenAsync(instance: "a");
        await using Dataset writer = await T.OpenOrCreate(first, Small());
        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset follower = await Dataset.OpenFollowerAsync(followed, Small(), T.Ct);
        Assert.Equal(first.Epoch, followed.AdoptedEpoch);

        Backend second = await objects.OpenAsync(instance: "b");
        await using Dataset successor = await Dataset.OpenAsync(second, Small(), T.Ct);
        await successor.CommitAsync(new CommitRequest().Assert(T.Iri("s2"), T.Iri("p"), T.Iri("o")), T.Ct);

        objects.Store.ResetCounts();
        Assert.Equal(successor.Head, await follower.AdvanceAsync(T.Ct));
        Assert.Equal(second.Epoch, followed.AdoptedEpoch);
        Assert.True(second.Epoch!.Value.Value > first.Epoch!.Value.Value);
    }

    /// <summary>
    /// A follower is due to open again once its memtable has grown by more
    /// than its limit since its open (ADR 0164 point 5), counted from the
    /// open: what its open replayed does not count. A follower opened again
    /// is not due until it has applied as much once more, whether or not the
    /// writer has written a newer catalog meanwhile. A writer is never due.
    /// </summary>
    [Fact]
    public async Task a_follower_is_due_to_open_again_after_applying_its_memtable_limit()
    {
        DatasetOptions small = Small() is var options ? new DatasetOptions
        {
            Clock = options.Clock,
            MaxRecordBytes = options.MaxRecordBytes,
            SegmentBytes = options.SegmentBytes,
            CommitCache = options.CommitCache,
            Maintenance = MaintenanceMode.Off,
            MemtableLimit = new Varve.Rdf.QuadCount(8),
        } : throw new InvalidOperationException();

        await using ObjectHarness objects = new();
        await using Dataset writer = await T.OpenOrCreate(await objects.OpenAsync(), small);

        for (int n = 0; n < 12; n++)
        {
            await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s" + n), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        // What its open replayed does not count.
        await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), small, T.Ct);
        long atOpen = follower.MemtableForTests();
        Assert.True(atOpen > 8);
        Assert.False(follower.ReopenDue);
        Assert.False(writer.ReopenDue);

        // Due exactly when the memtable has grown past the limit since the
        // open, by the measure the writer's own flush compares with it.
        for (int n = 12; !follower.ReopenDue; n++)
        {
            Assert.True(n < 40, "never due");
            await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s" + n), T.Iri("p"), T.Iri("o")), T.Ct);
            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(follower.MemtableForTests() - atOpen > 8, follower.ReopenDue);
        }

        // Opened again with no newer catalog, it is not due again at once.
        await using Dataset again = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), small, T.Ct);
        Assert.False(again.ReopenDue);
        Assert.Equal(writer.Head, again.Head);
    }

    /// <summary>A follower commits nothing and writes no checkpoint, and a writer does not advance.</summary>
    [Fact]
    public async Task a_follower_refuses_writes_and_checkpoints()
    {
        await using ObjectHarness objects = new();
        await using Dataset writer = await T.OpenOrCreate(await objects.OpenAsync(), Small());
        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

        Assert.True(follower.IsFollower);
        Assert.False(writer.IsFollower);
        CommitResult refused = await follower.CommitAsync(new CommitRequest().Assert(T.Iri("x"), T.Iri("p"), T.Iri("o")), T.Ct);
        Assert.Equal(CommitOutcome.Unavailable, refused.Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await follower.CheckpointAsync(new Position(1), T.Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.AdvanceAsync(T.Ct));
    }

    /// <summary>
    /// A follower announces the catalog version it opened from in its reader
    /// record (#110's reader contract, ADR 0164's hook wired to ADR 0160), so
    /// the writer deletes nothing it reads; a re-opened follower announces the
    /// newer one, and the writer has a record of its own.
    /// </summary>
    [Fact]
    public async Task a_follower_announces_the_catalog_version_it_opened()
    {
        await using ObjectHarness objects = new();
        Backend written = await objects.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(written, Small());
        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        await writer.CheckpointAsync(new Position(1), T.Ct);
        CatalogVersion opened = writer.CatalogVersion;

        Backend first = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using (Dataset follower = await Dataset.OpenFollowerAsync(first, Small(), T.Ct))
        {
            ReaderRecord held = (await Backend.ReadReadersAsync(objects.Store, objects.Prefix, T.Ct)).Single(r => r.Reader == first.Reader);
            Assert.Equal(opened, held.Oldest);
            Assert.Equal(opened, held.Newest);
        }

        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s2"), T.Iri("p"), T.Iri("o")), T.Ct);
        await writer.CheckpointAsync(new Position(2), T.Ct);
        Backend second = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset again = await Dataset.OpenFollowerAsync(second, Small(), T.Ct);
        IReadOnlyList<ReaderRecord> records = await Backend.ReadReadersAsync(objects.Store, objects.Prefix, T.Ct);

        Assert.True(records.Single(r => r.Reader == second.Reader).Oldest > opened);
        Assert.Contains(records, r => r.Reader == written.Reader);
    }

    /// <summary>A chunk the writer did not write, in the next slot, refuses the advance.</summary>
    [Fact]
    public async Task a_chunk_that_does_not_verify_refuses_the_advance()
    {
        await using ObjectHarness objects = new();
        await using Dataset writer = await T.OpenOrCreate(await objects.OpenAsync(), Small());
        await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        await using Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

        string next = objects.Store.Names().Where(name => name.Contains(".chunks/", StringComparison.Ordinal)).Max(StringComparer.Ordinal)!;
        int slash = next.LastIndexOf('/');
        int chunk = int.Parse(next[(slash + 1)..], System.Globalization.CultureInfo.InvariantCulture) + 1;
        objects.Store.Put(next[..(slash + 1)] + chunk.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), [1, 2, 3]);

        await Assert.ThrowsAsync<LogVerificationException>(async () => await follower.AdvanceAsync(T.Ct));
    }

    private static async Task SameAsync(Dataset writer, Dataset follower, Position at)
    {
        using DatasetView expected = await writer.AsOfAsync(at, T.Ct);
        using DatasetView actual = await follower.AsOfAsync(at, T.Ct);
        Assert.Equal(Rendered(expected), Rendered(actual));
        Assert.Equal((await writer.SettingsAtAsync(at, T.Ct)).DefaultAccessScope, (await follower.SettingsAtAsync(at, T.Ct)).DefaultAccessScope);
    }

    private static List<string> Rendered(DatasetView view) =>
        [.. T.All(view).Select(quad => T.Render(view, quad)).Order(StringComparer.Ordinal)];
}
