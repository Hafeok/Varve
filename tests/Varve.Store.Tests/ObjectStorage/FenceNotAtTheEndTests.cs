// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// ADR 0136's amendment of 2026-11-24 (#396): a fence stands only at the end
/// of the log. A writer claims and lists; the writer it supersedes, not yet
/// told, commits once more, and the commit rolls its segment: it seals,
/// consolidates and deletes the chunks, the seal among them. The new writer's
/// fence create then succeeds in the slot the seal left, inside a segment
/// that is now its consolidated object. The new writer finds the object,
/// adopts the log after it and fences again, at the end.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class FenceNotAtTheEndTests
{
    private const string Vacated = "log/00000000.chunks/00000005";

    /// <summary>
    /// The open for write. With <paramref name="checkpointed"/>, a checkpoint
    /// is at the head, so the new writer's open reads no chunk below the tail
    /// and nothing else shows it that segment 0 was consolidated. Without the
    /// second fence its commits are then created in segment 0's chunks, behind
    /// the object, and acknowledged, while the old writer commits the same
    /// positions in segment 1 (#396).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_fence_that_lands_in_the_slot_a_seal_left_is_made_again_at_the_end_of_the_log(bool checkpointed)
    {
        await using ObjectHarness harness = new();
        Dataset first = await SeededAsync(harness);

        if (checkpointed)
        {
            await first.CheckpointAsync(first.Head, T.Ct);
        }

        RaceTheNextFence(harness, first);

        Backend storage = await harness.OpenAsync(instance: "two");
        harness.Store.Hook = null;
        await using Dataset second = await Dataset.OpenAsync(storage, Options(), T.Ct);

        await AssertTheNewWriterHoldsTheLogAsync(harness, first, second, storage);
    }

    /// <summary>The claim in place (ADR 0179 point 8): a warm standby that takes the write meets the same order.</summary>
    [Fact]
    public async Task a_claim_in_place_whose_fence_lands_in_the_slot_a_seal_left_fences_again_at_the_end_of_the_log()
    {
        await using ObjectHarness harness = new();
        Dataset first = await SeededAsync(harness);
        Backend storage = await harness.OpenAsync(ObjectStorageAccess.ReadOnly, instance: "two");
        Dataset second = await Dataset.OpenFollowerAsync(storage, Options(), T.Ct);

        try
        {
            RaceTheNextFence(harness, first);
            WriterEpoch from = (await Backend.ReadWriterAsync(harness.Store, harness.Prefix, T.Ct))!.Epoch;
            Assert.True(await second.TakeWriteAsync(ct => storage.ClaimInPlaceAsync(new WriterInstance("two"), null, TestRandomness.Instance, ClaimTrigger.Probe, from, ct), T.Ct));
            harness.Store.Hook = null;

            await AssertTheNewWriterHoldsTheLogAsync(harness, first, second, storage);
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    /// <summary>
    /// The chunk the first fence left stays in its slot, inside a consolidated
    /// segment. A follower that had read up to the slot before it meets that
    /// chunk on its next refresh, and still reads the whole log.
    /// </summary>
    [Fact]
    public async Task a_follower_that_meets_the_chunk_the_first_fence_left_reads_the_whole_log()
    {
        await using ObjectHarness harness = new();
        Dataset first = await SeededAsync(harness);
        await using Dataset follower = await Dataset.OpenFollowerAsync(await harness.OpenAsync(ObjectStorageAccess.ReadOnly, instance: "reader"), Options(), T.Ct);
        Assert.Equal(first.Head, follower.Head);
        RaceTheNextFence(harness, first);

        Backend storage = await harness.OpenAsync(instance: "two");
        harness.Store.Hook = null;
        await using Dataset second = await Dataset.OpenAsync(storage, Options(), T.Ct);
        await second.CommitAsync(Small("after"), T.Ct);
        Assert.Contains(Vacated, harness.Store.Names());

        await follower.AdvanceAsync(T.Ct);

        Assert.Equal(second.Head, follower.Head);
        using DatasetView expected = second.Pin();
        using DatasetView actual = follower.Pin();
        Assert.Equal(T.Terms(expected), T.Terms(actual));
        await first.DisposeAsync();
    }

    private static DatasetOptions Options() => T.Options(segmentBytes: 4096);

    private static CommitRequestBuilder Small(string subject) => new CommitRequestBuilder().Assert(T.Iri(subject), T.Iri("p"), T.Literal("o"));

    // Three commits in segment 0: its chunks end at slot 4, and slot 5 is where the next create goes.
    private static async Task<Dataset> SeededAsync(ObjectHarness harness)
    {
        Dataset first = await T.OpenOrCreate(await harness.OpenAsync(instance: "one"), Options());

        for (int i = 0; i < 3; i++)
        {
            await first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 200) + i)), T.Ct);
        }

        Assert.Contains("log/00000000.chunks/00000004", harness.Store.Names());
        Assert.DoesNotContain(Vacated, harness.Store.Names());
        return first;
    }

    // The next chunk create on the store is the new writer's fence: it has claimed, and has listed or probed the log.
    // Before the store applies it, the old writer commits once more. The commit does not fit segment 0 and rolls it, and the old writer
    // consolidates it and deletes its chunks, so the fence's slot is free again and the segment is its object.
    private static void RaceTheNextFence(ObjectHarness harness, Dataset first)
    {
        bool raced = false;
        harness.Store.Hook = (operation, name) =>
        {
            if (raced || operation != ObjectOperation.Create || !name.Value.Contains(".chunks/", StringComparison.Ordinal))
            {
                return null;
            }

            raced = true;
            Assert.EndsWith(Vacated, name.Value, StringComparison.Ordinal);
            first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s3"), T.Iri("p"), T.Literal(new string('x', 3600) + 3)), T.Ct).AsTask().GetAwaiter().GetResult();
            Stopwatch watch = Stopwatch.StartNew();

            while (!(harness.Store.Names().Contains("log/00000000.seg") && !harness.Store.Names().Contains(Vacated)))
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "The old writer did not consolidate segment 0 in 30 seconds.");
                Thread.Sleep(10);
            }

            return null;
        };
    }

    private static async Task AssertTheNewWriterHoldsTheLogAsync(ObjectHarness harness, Dataset first, Dataset second, Backend storage)
    {
        // It read the commit the old writer made while it was fencing.
        Assert.Equal(4, first.Head.Value);
        Assert.Equal(first.Head, second.Head);

        // Its fence is in the writer record, and is not the slot the seal left: that one is behind the object.
        WriterRecord record = (await Backend.ReadWriterAsync(harness.Store, harness.Prefix, T.Ct))!;
        LogSlot fence = record.History[^1].Fence!.Value;
        Assert.StartsWith("00000001.chunks/", fence.Value, StringComparison.Ordinal);
        Assert.Contains(Vacated, harness.Store.Names());

        // It commits, and the old writer is the one refused.
        CommitResult committed = await second.CommitAsync(Small("second"), T.Ct);
        Assert.Equal(CommitOutcome.Committed, committed.Outcome);
        Assert.Equal(5, second.Head.Value);
        CommitResult late;

        try
        {
            late = await first.CommitAsync(Small("late"), T.Ct);
        }
        catch (WriterSupersededException)
        {
            late = CommitResult.Unavailable(first.Head.Value, "superseded");
        }

        Assert.NotEqual(CommitOutcome.Committed, late.Outcome);
        await first.DisposeAsync();

        // Every commit acknowledged is in the log a reader opens: none is behind the object.
        await using Dataset reader = await Dataset.OpenFollowerAsync(await harness.OpenAsync(ObjectStorageAccess.ReadOnly), Options(), T.Ct);
        Assert.Equal(5, reader.Head.Value);
        using DatasetView expected = second.Pin();
        using DatasetView actual = reader.Pin();
        Assert.Equal(T.Terms(expected), T.Terms(actual));
    }
}
