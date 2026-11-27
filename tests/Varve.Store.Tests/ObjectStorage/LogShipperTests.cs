// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Formats;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Log shipping's log (ADR 0184): a writer on one in-memory store, a shipper,
/// and a second store it ships to. What is held throughout is the restated
/// byte-identity gate of #166: every object both stores hold is identical,
/// the log read from the target is the source's, and a follower of the
/// target reads what a follower of the source reads.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class LogShipperTests
{
    // Small segments, so a handful of commits seals and consolidates several.
    private static DatasetOptions Small() => T.Options(segmentBytes: 2048);

    [Fact]
    public async Task a_shipped_log_is_the_sources_and_a_follower_of_the_target_reads_it()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 3);

        LogShipper shipper = (await pair.StartAsync())!;
        ShippedLog first = await shipper.ShipLogAsync(T.Ct);

        Assert.True(first.Moved);
        pair.AssertIdentical();
        Assert.Contains("lineage", pair.Target.Store.Names());
        Assert.DoesNotContain(pair.Target.Store.Names(), name => name is "writer" || name.StartsWith("readers/", StringComparison.Ordinal) || name.StartsWith("probe/", StringComparison.Ordinal));

        // Nothing new: a round writes nothing, and says so.
        pair.Target.Store.ResetCounts();
        Assert.False((await shipper.ShipLogAsync(T.Ct)).Moved);
        Assert.Equal(0, pair.Target.Store.Count(ObjectOperation.Create) + pair.Target.Store.Count(ObjectOperation.Replace) + pair.Target.Store.Count(ObjectOperation.Delete));

        // More, across segment boundaries and their consolidation.
        await CommitAsync(writer, 3, 40);
        await shipper.ShipLogAsync(T.Ct);
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// The case the gate names (#166): the source consolidates a segment
    /// after its chunks were shipped. A follower of the target that adopted
    /// the chunks reads the same log while the target gets the segment and
    /// loses the chunks, inside that segment and after it.
    /// </summary>
    [Fact]
    public async Task a_follower_of_the_target_reads_on_while_a_segment_shipped_as_chunks_is_consolidated()
    {
        await using Pair pair = new();
        Backend storage = await pair.Source.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, Small());
        await CommitAsync(writer, 0, 2);

        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);
        Assert.DoesNotContain(pair.Target.Store.Names(), name => name.EndsWith(".seg", StringComparison.Ordinal));

        // A follower of the target adopts segment 0 as chunks.
        await using Dataset follower = await Dataset.OpenFollowerAsync(await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);
        Assert.Equal(writer.Head, follower.Head);
        List<SortedSet<string>> inside = await FormatFixtures.ViewsAsync(follower);

        // The source seals segment 0, consolidates it and deletes its chunks.
        await CommitAsync(writer, 2, 30);
        await storage.MaintainStorageAsync(T.Ct);
        Assert.Contains("log/00000000.seg", pair.Source.Store.Names());
        Assert.DoesNotContain("log/00000000.chunks/00000001", pair.Source.Store.Names());

        await shipper.ShipLogAsync(T.Ct);

        Assert.Contains("log/00000000.seg", pair.Target.Store.Names());
        Assert.DoesNotContain("log/00000000.chunks/00000001", pair.Target.Store.Names());
        Assert.Equal(pair.Source.Store.Get("log/00000000.seg"), pair.Target.Store.Get("log/00000000.seg"));
        pair.AssertIdentical();

        // The follower goes on: the positions it had read the same, and it reaches the head.
        await follower.AdvanceAsync(T.Ct);
        Assert.Equal(writer.Head, follower.Head);
        Assert.Equal(inside, (await FormatFixtures.ViewsAsync(follower)).Take(inside.Count));
        Assert.Equal(await FormatFixtures.ViewsAsync(writer), await FormatFixtures.ViewsAsync(follower));
    }

    /// <summary>The shipper stopped between the segment's create and the deletes: both are there, and it reads the same.</summary>
    [Fact]
    public async Task a_shipper_stopped_between_the_segment_and_the_deletes_leaves_both_and_the_next_one_finishes()
    {
        await using Pair pair = new();
        Backend storage = await pair.Source.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, Small());
        await CommitAsync(writer, 0, 2);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);
        await using Dataset follower = await Dataset.OpenFollowerAsync(await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);

        await CommitAsync(writer, 2, 30);
        await storage.MaintainStorageAsync(T.Ct);
        pair.Target.Store.Hook = (operation, name) => operation == ObjectOperation.Delete && name.Value.StartsWith("log/", StringComparison.Ordinal)
            ? throw new IOException("stopped before the deletes")
            : null;

        await Assert.ThrowsAsync<IOException>(async () => await shipper.ShipLogAsync(T.Ct));

        pair.Target.Store.Hook = null;
        Assert.Contains("log/00000000.seg", pair.Target.Store.Names());
        Assert.Contains("log/00000000.chunks/00000001", pair.Target.Store.Names());
        pair.AssertIdentical();
        await follower.AdvanceAsync(T.Ct);
        Assert.Equal(await FormatFixtures.ViewsAsync(writer).ContinueWith(views => views.Result.Take((int)follower.Head.Value + 1), TaskScheduler.Default), await FormatFixtures.ViewsAsync(follower));

        // A shipper started again finishes the deletes and goes on.
        LogShipper again = (await pair.StartAsync())!;
        await again.ShipLogAsync(T.Ct);
        Assert.DoesNotContain("log/00000000.chunks/00000001", pair.Target.Store.Names());
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>A segment the source consolidated before it was shipped reaches the target as the segment and its fence chunks.</summary>
    [Fact]
    public async Task a_segment_consolidated_before_it_was_shipped_arrives_whole_and_a_follower_started_after_reads_it()
    {
        await using Pair pair = new();
        Backend storage = await pair.Source.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, Small());
        await CommitAsync(writer, 0, 40);
        await storage.MaintainStorageAsync(T.Ct);
        Assert.Contains("log/00000000.seg", pair.Source.Store.Names());

        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);

        pair.AssertIdentical();

        // The target holds nothing the source does not, and of a segment it
        // has whole, only the fence chunks. How far the source's own deletes
        // have got is the source's business, so the names are not compared.
        string[] target = [.. pair.Target.Store.Names().Where(name => name.StartsWith("log/", StringComparison.Ordinal))];
        Assert.Subset(pair.Source.Store.Names().ToHashSet(), target.ToHashSet());
        Assert.Contains("log/00000000.seg", target);
        Assert.All(
            target.Where(name => name.StartsWith("log/00000000.chunks/", StringComparison.Ordinal)),
            name => Assert.True(ChunkHeader.TryRead(pair.Target.Store.Get(name), out ChunkHeader header, out _) && (header.Flags & ChunkFlags.Fence) != 0, name + " is not a fence chunk."));
        await pair.AssertFollowersAgreeAsync(writer);
    }

    [Fact]
    public async Task a_shipper_started_again_resumes_from_the_targets_tail_and_two_at_once_ship_one_copy()
    {
        await using Pair pair = new();
        Backend storage = await pair.Source.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, Small());
        await CommitAsync(writer, 0, 12);
        await (await pair.StartAsync())!.ShipLogAsync(T.Ct);

        await CommitAsync(writer, 12, 25);
        LogShipper one = (await pair.StartAsync())!;
        LogShipper two = (await pair.StartAsync())!;
        await Task.WhenAll(one.ShipLogAsync(T.Ct).AsTask(), two.ShipLogAsync(T.Ct).AsTask());
        pair.AssertIdentical();

        await CommitAsync(writer, 25, 50);
        await storage.MaintainStorageAsync(T.Ct);
        await Task.WhenAll(one.ShipLogAsync(T.Ct).AsTask(), two.ShipLogAsync(T.Ct).AsTask());
        await one.ShipLogAsync(T.Ct);
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// ADR 0184 decision 4, two shippers at once: one copied a chunk of the
    /// source's before the source consolidated its segment, and the other,
    /// which listed the target before that, comes to seal the segment through
    /// the same slot. The chunk there is the source's own bytes, so the
    /// second shipper follows it and seals through the slot after. It is not
    /// a divergence. Found by a whole pipeline run on ff790965, where
    /// <c>a_shipper_started_again_resumes…</c> met this order once.
    /// </summary>
    [Fact]
    public async Task a_shipper_that_finds_another_shippers_chunk_of_the_source_at_the_slot_seals_through_the_next()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options(segmentBytes: 4096));
        await CommitAsync(writer, 0, 3);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);

        // One more chunk in segment 0, which the other shipper will have copied; then the commit that rolls the
        // segment, and the source's consolidation, which deletes that chunk.
        const string Slot = "log/00000000.chunks/00000005";
        Assert.DoesNotContain(Slot, pair.Source.Store.Names());
        await CommitAsync(writer, 3, 4);
        byte[] copied = pair.Source.Store.Get(Slot)!;
        Assert.True(ChunkHeader.TryRead(copied, out ChunkHeader header, out _));
        Assert.Equal(ChunkFlags.None, header.Flags & (ChunkFlags.Seal | ChunkFlags.Fence));
        await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("big"), T.Iri("p"), T.Literal(new string('x', 3600))), T.Ct);
        await UntilAsync(() => pair.Source.Store.Names().Contains("log/00000000.seg") && !pair.Source.Store.Names().Contains(Slot));

        // The other shipper's create of that chunk lands just before this shipper's create at the same slot.
        bool landed = false;
        pair.Target.Store.Hook = (operation, name) =>
        {
            if (!landed && operation == ObjectOperation.Create && name.Value == Slot)
            {
                landed = true;
                pair.Target.Store.Put(Slot, copied);
            }

            return null;
        };

        await shipper.ShipLogAsync(T.Ct);
        pair.Target.Store.Hook = null;

        Assert.True(landed);
        Assert.Contains("log/00000000.seg", pair.Target.Store.Names());
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// The same, where the chunk the other shipper copied is the first of a
    /// segment: the chunk that opens it, which the source wrote before any
    /// of the segment's records. Found by the hosted devcontainer run on
    /// b6c19958, where the two-shippers test met it at chunk 0 of a segment.
    /// </summary>
    [Fact]
    public async Task a_shipper_that_finds_another_shippers_copy_of_a_segments_first_chunk_seals_through_the_next()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options(segmentBytes: 4096));
        await CommitAsync(writer, 0, 3);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);

        // A commit that rolls segment 0 and opens segment 1, whose first chunk the other shipper will have
        // copied; then one that rolls segment 1, and the source's consolidation of both.
        const string Slot = "log/00000001.chunks/00000000";
        await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("big0"), T.Iri("p"), T.Literal(new string('x', 3600))), T.Ct);
        byte[] copied = pair.Source.Store.Get(Slot)!;
        Assert.True(ChunkHeader.TryRead(copied, out ChunkHeader header, out _));
        Assert.Equal(ChunkFlags.None, header.Flags & (ChunkFlags.Seal | ChunkFlags.Fence));
        await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("big1"), T.Iri("p"), T.Literal(new string('x', 3600))), T.Ct);
        await UntilAsync(() => pair.Source.Store.Names().Contains("log/00000001.seg") && !pair.Source.Store.Names().Contains(Slot));

        bool landed = false;
        pair.Target.Store.Hook = (operation, name) =>
        {
            if (!landed && operation == ObjectOperation.Create && name.Value == Slot)
            {
                landed = true;
                pair.Target.Store.Put(Slot, copied);
            }

            return null;
        };

        await shipper.ShipLogAsync(T.Ct);
        pair.Target.Store.Hook = null;

        Assert.True(landed, "flags " + header.Flags + ", length " + header.Length);
        Assert.Contains("log/00000001.seg", pair.Target.Store.Names());
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>Nothing on a target is ever overwritten: other bytes at the next slot stop the shipper, and stay.</summary>
    [Fact]
    public async Task other_bytes_at_the_next_slot_are_shipping_diverged_and_are_left_as_they_were()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options());
        await CommitAsync(writer, 0, 2);
        LogShipper shipper = (await pair.StartAsync())!;
        ShippedLog shipped = await shipper.ShipLogAsync(T.Ct);

        // Something else wrote the next slot: a writer of another epoch, as a
        // promotion's fence is, chained to the target's last chunk as a real one would be.
        string next = "log/" + shipped.NextSlot.Value;
        int at = int.Parse(next[^8..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ChunkHeader.TryRead(pair.Target.Store.Get("log/" + ObjectNames.Slot(0, at - 1)), out ChunkHeader last, out byte[] lastHash));
        byte[] other = ChunkHeader.Compose(ChunkFlags.Fence, 99, 0, at, last.Offset + last.Length, [], lastHash, out _);
        pair.Target.Store.Put(next, other);
        await CommitAsync(writer, 2, 4);
        string[] before = [.. pair.Target.Store.Names()];

        ShippingDivergedException diverged = await Assert.ThrowsAsync<ShippingDivergedException>(async () => await shipper.ShipLogAsync(T.Ct));

        Assert.Equal(shipped.NextSlot, diverged.Slot);
        Assert.Contains("Nothing was overwritten", diverged.Message, StringComparison.Ordinal);
        Assert.Equal(other, pair.Target.Store.Get(next));
        Assert.Equal(before, pair.Target.Store.Names());

        // And a shipper started again is refused the same way, at the tail.
        ShippingDivergedException again = await Assert.ThrowsAsync<ShippingDivergedException>(async () => await pair.StartAsync());
        Assert.Equal(shipped.NextSlot, again.Slot);
        Assert.Contains("the target's last chunk is not the source's", again.Message, StringComparison.Ordinal);
        Assert.Equal(other, pair.Target.Store.Get(next));

        // A chunk on the target that does not even follow is divergence too, never a verdict on the source's log.
        pair.Target.Store.Put(next, ChunkHeader.Compose(ChunkFlags.None, 1, 0, at, 0, [1, 2, 3], ChunkHeader.Nothing(), out _));
        ShippingDivergedException broken = await Assert.ThrowsAsync<ShippingDivergedException>(async () => await pair.StartAsync());
        Assert.Contains("does not follow the one before it", broken.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_target_that_holds_another_dataset_or_another_sources_copy_is_refused_with_nothing_written()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options());
        await CommitAsync(writer, 0, 2);

        // A dataset of its own.
        await using (Dataset other = await T.OpenOrCreate(await pair.Target.OpenAsync(), T.Options()))
        {
            await CommitAsync(other, 0, 1);
        }

        string[] before = [.. pair.Target.Store.Names()];
        ShippingTargetNotEmptyException own = await Assert.ThrowsAsync<ShippingTargetNotEmptyException>(async () => await pair.StartAsync());
        Assert.Contains("no lineage record", own.Message, StringComparison.Ordinal);
        Assert.Equal(before, pair.Target.Store.Names());

        // A copy of another source.
        await using Pair second = new();
        await using Dataset again = await T.OpenOrCreate(await second.Source.OpenAsync(), T.Options());
        await CommitAsync(again, 0, 1);
        await (await second.StartAsync())!.ShipLogAsync(T.Ct);
        string[] copy = [.. second.Target.Store.Names()];

        ShippingTargetNotEmptyException another = await Assert.ThrowsAsync<ShippingTargetNotEmptyException>(async () =>
            await second.StartAsync(new StoreAddress("https://elsewhere.example/store")));
        Assert.Contains("a copy of another dataset", another.Message, StringComparison.Ordinal);
        Assert.Equal(copy, second.Target.Store.Names());
    }

    [Fact]
    public async Task a_promoted_target_stops_its_old_sources_shipper_and_a_source_with_no_dataset_ships_nothing()
    {
        await using Pair empty = new();
        Assert.Null(await empty.StartAsync());
        Assert.Equal([ObjectStoreVerification.RecordName.Value], empty.Target.Store.Names());

        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options());
        await CommitAsync(writer, 0, 2);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);

        LineageRecord replica = (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!;
        pair.Target.Store.Put("lineage", LineageRecordFormat.Write(replica with
        {
            State = LineageState.Promoted,
            Promoted = new LineagePromotion
            {
                Epoch = new WriterEpoch(2),
                Position = new Position(2),
                Hash = new CommitHeaderHash(new string('0', 64)),
                At = new ClaimTime(DateTimeOffset.UnixEpoch),
                By = new WriterInstance("target-1"),
            },
        }));
        await CommitAsync(writer, 2, 4);
        string[] before = [.. pair.Target.Store.Names()];

        ReplicaPromotedException promoted = await Assert.ThrowsAsync<ReplicaPromotedException>(async () => await shipper.ShipLogAsync(T.Ct));
        Assert.Contains("promoted at position 2 by target-1", promoted.Message, StringComparison.Ordinal);
        Assert.Equal(before, pair.Target.Store.Names());
        await Assert.ThrowsAsync<ReplicaPromotedException>(async () => await pair.StartAsync());
    }

    /// <summary>
    /// A whole round ships the sets and the catalog too (ADR 0184 decision 4):
    /// the target's catalog is the source's bytes, every blob of every set it
    /// names is there and identical, the shipper's reader record on the source
    /// announces the version shipped, and a follower of the target opens from
    /// the shipped catalog.
    /// </summary>
    [Fact]
    public async Task a_whole_round_ships_the_sets_and_the_catalog_last_and_announces_what_it_copies()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        LogShipper shipper = (await pair.StartAsync())!;

        // No checkpoint yet: the log alone, and no catalog on the target.
        await CommitAsync(writer, 0, 6);
        ShippedRound first = await shipper.ShipAsync(T.Ct);
        Assert.True(first.Moved);
        Assert.Null(first.Catalog);
        Assert.DoesNotContain(pair.Target.Store.Names(), name => name.StartsWith("derived/", StringComparison.Ordinal));

        await writer.CheckpointAsync(new Position(5), T.Ct);
        await CommitAsync(writer, 6, 10);
        ShippedRound second = await shipper.ShipAsync(T.Ct);

        Assert.True(second.Moved);
        Assert.Equal(new Position(5), second.Catalog!.Position);
        pair.AssertDerivedIdentical();
        pair.AssertIdentical();
        Assert.Contains("\"catalogOldest\": " + second.Catalog.Version.Value + ",", System.Text.Encoding.UTF8.GetString(pair.Source.Store.Get("readers/shipper-ship-target")!), StringComparison.Ordinal);
        Assert.DoesNotContain(pair.Target.Store.Names(), name => name.StartsWith("readers/", StringComparison.Ordinal));

        // A newer catalog replaces the target's, and a round with nothing new writes nothing.
        await writer.CheckpointAsync(new Position(9), T.Ct);
        ShippedRound third = await shipper.ShipAsync(T.Ct);
        Assert.Equal(new Position(9), third.Catalog!.Position);
        Assert.True(third.Catalog.Version.Value > second.Catalog.Version.Value);
        pair.AssertDerivedIdentical();
        pair.Target.Store.ResetCounts();
        Assert.False((await shipper.ShipAsync(T.Ct)).Moved);
        Assert.Equal(0, pair.Target.Store.Count(ObjectOperation.Create) + pair.Target.Store.Count(ObjectOperation.Replace) + pair.Target.Store.Count(ObjectOperation.Delete));

        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// The catalog is last: a shipper stopped while it copies a set leaves the
    /// target's catalog as it was, so it never names a set the target lacks.
    /// </summary>
    [Fact]
    public async Task a_shipper_stopped_while_it_copies_a_set_leaves_the_targets_catalog_naming_only_what_is_there()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        LogShipper shipper = (await pair.StartAsync())!;
        await CommitAsync(writer, 0, 6);
        await writer.CheckpointAsync(new Position(5), T.Ct);
        await shipper.ShipAsync(T.Ct);
        byte[] before = pair.Target.Store.Get("derived/catalog")!;

        await CommitAsync(writer, 6, 12);
        await writer.CheckpointAsync(new Position(11), T.Ct);
        // Every attempt at one blob of the new set fails, so the store's own retries do not get past it.
        pair.Target.Store.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value.StartsWith("derived/extents/", StringComparison.Ordinal) && name.Value.EndsWith("/order3", StringComparison.Ordinal)
            ? throw new IOException("stopped while copying a set")
            : null;

        await Assert.ThrowsAsync<IOException>(async () => await shipper.ShipAsync(T.Ct));

        pair.Target.Store.Hook = null;
        Assert.Equal(before, pair.Target.Store.Get("derived/catalog"));
        pair.AssertTargetCatalogNamesOnlyWhatIsThere();
        await using (Dataset follower = await Dataset.OpenFollowerAsync(await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct))
        {
            Assert.Equal(writer.Head, follower.Head);
        }

        // Started again, it finishes: two shippers at once, as after a handover.
        LogShipper again = (await pair.StartAsync())!;
        await Task.WhenAll(shipper.ShipAsync(T.Ct).AsTask(), again.ShipAsync(T.Ct).AsTask());
        Assert.Equal(pair.Source.Store.Get("derived/catalog"), pair.Target.Store.Get("derived/catalog"));
        pair.AssertDerivedIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// ADR 0184 decision 4, step 6: a set the target's catalog stopped naming
    /// is deleted there only when the source has deleted it and no reader
    /// record of the target holds a version that could name it.
    /// </summary>
    [Fact]
    public async Task a_retired_set_leaves_the_target_only_after_the_source_and_the_targets_readers_let_it_go()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), ExtentMaintenanceCases.Merging());
        LogShipper shipper = (await pair.StartAsync())!;
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);
        await writer.CheckpointAsync(new Position(6), T.Ct);
        await shipper.ShipAsync(T.Ct);
        string[] first = pair.TargetSets();
        Assert.Equal(2, first.Length);

        // A reader of the target attaches to the catalog that names both sets.
        ReaderId reader = new("target-reader");
        Backend attached = await Backend.OpenAsync(pair.Target.Options(ObjectStorageAccess.ReadOnly, reader: reader), T.Ct);
        Dataset held = await Dataset.OpenAsync(attached, T.Options(), T.Ct);

        // The source merges. Its writer cannot delete the two sets yet: the
        // shipper's record holds the version the target's catalog is.
        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.All(first, set => Assert.Contains(set, pair.SourceSets()));

        // The round ships the merged set and the catalog. The two sets are
        // still on the source, so they stay on the target.
        Assert.True((await shipper.ShipAsync(T.Ct)).Moved);
        pair.AssertDerivedIdentical();
        Assert.All(first, set => Assert.Contains(set, pair.TargetSets()));

        // The source lets them go, and the target's reader still holds them.
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.All(first, set => Assert.DoesNotContain(set, pair.SourceSets()));
        Assert.False((await shipper.ShipAsync(T.Ct)).Moved);
        Assert.All(first, set => Assert.Contains(set, pair.TargetSets()));

        // The reader closes: nothing holds them, and they go.
        await held.DisposeAsync();
        await attached.DisposeAsync();
        Assert.True((await shipper.ShipAsync(T.Ct)).Moved);
        Assert.Equal(pair.SourceSets(), pair.TargetSets());
        pair.AssertDerivedIdentical();
        Assert.False((await shipper.ShipAsync(T.Ct)).Moved);
        await pair.AssertFollowersAgreeAsync(writer);
    }

    /// <summary>
    /// ADR 0184 decision 9: a target whose store has no verification record,
    /// one this build does not read, or none at all, is refused before
    /// anything is written to it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("{\"format\": 99}")]
    [InlineData("not a record")]
    public async Task a_target_whose_store_is_not_verified_is_refused_with_nothing_written(string? record)
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 2);
        pair.Target.Store.Remove(ObjectStoreVerification.RecordName.Value);

        if (record is not null)
        {
            pair.Target.Store.Put(ObjectStoreVerification.RecordName.Value, System.Text.Encoding.UTF8.GetBytes(record));
        }

        string[] before = [.. pair.Target.Store.Names()];
        pair.Target.Store.ResetCounts();

        StoreUnverifiedException refused = await Assert.ThrowsAsync<StoreUnverifiedException>(async () => await pair.StartAsync());

        Assert.Contains("varve storage verify", refused.Message, StringComparison.Ordinal);
        Assert.Equal(record is null, refused.Found is null);
        Assert.Equal(before, pair.Target.Store.Names());
        Assert.Equal(0, pair.Target.Store.Count(ObjectOperation.Create) + pair.Target.Store.Count(ObjectOperation.Replace) + pair.Target.Store.Count(ObjectOperation.Delete));
    }

    /// <summary>
    /// ADR 0185 decision 4: a follower of a replica promotes it in place. The
    /// lineage says promoted, with the verified tail; the claim's trigger is
    /// promotion, above every epoch the copy carries; the dataset commits at
    /// once; and the old source's shipper is refused.
    /// </summary>
    [Fact]
    public async Task a_follower_promotes_its_replica_in_place_and_writes_on_from_the_shipped_tail()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 6);
        await writer.CheckpointAsync(new Position(4), T.Ct);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipAsync(T.Ct);

        Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset replica = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct);
        Position tail = replica.Head;
        Assert.Equal(writer.Head, tail);

        PromotionOutcome outcome = PromotionOutcome.Lost;
        Assert.True(await replica.TakeWriteAsync(async claim => (outcome = await storage.PromoteInPlaceAsync((await Pair.PromotionAsync(replica, tail)), claim)) == PromotionOutcome.Promoted, T.Ct));
        Assert.Equal(PromotionOutcome.Promoted, outcome);

        // Fenced, and not finished until the lineage says where: the promotion's last write.
        Assert.Equal(ReplicaState.PromotionIncomplete, await Backend.ReplicaStateAsync(pair.Target.Store, null, T.Ct));
        await Pair.CompleteAsync(storage, replica);
        Assert.Equal(ReplicaState.None, await Backend.ReplicaStateAsync(pair.Target.Store, null, T.Ct));

        LineageRecord lineage = (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!;
        Assert.False(lineage.IsReplica);
        Assert.Equal(tail, lineage.Promoted!.Position);
        Assert.NotNull(lineage.Promoted.Fence);
        Assert.Equal(Convert.ToHexStringLower((await replica.HeaderHashAsync(tail, T.Ct)).Span), lineage.Promoted.Hash.Value);
        Assert.Equal("promoter", lineage.Promoted.By.Value);

        // One above the source writer's epoch, which the copy's chunks and its catalog's wrapper carry and its own record never showed.
        WriterRecord source = (await Backend.ReadWriterAsync(pair.Source.Store, null, T.Ct))!;
        WriterRecord record = (await Backend.ReadWriterAsync(pair.Target.Store, null, T.Ct))!;
        Assert.Equal(source.Epoch.Value + 1, record.Epoch.Value);
        Assert.Equal(record.Epoch, lineage.Promoted.Epoch);
        Assert.Equal(ClaimTrigger.Promotion, record.History[^1].Trigger);
        Assert.Equal(lineage.Promoted.Fence, record.History[^1].Fence);

        // It writes on, from the tail.
        await CommitAsync(replica, 6, 9);
        Assert.Equal(tail.Value + 3, replica.Head.Value);

        // The returning primary: its shipper is refused, and writes nothing.
        await CommitAsync(writer, 6, 8);
        Dictionary<string, byte[]> before = pair.Target.Snapshot();
        ReplicaPromotedException refused = await Assert.ThrowsAsync<ReplicaPromotedException>(async () => await shipper.ShipAsync(T.Ct));
        Assert.Equal(tail, refused.Lineage!.Promoted!.Position);
        await AssertNothingOfTheSourceArrivedAsync(pair, before, replica);
    }

    /// <summary>
    /// The header hash a lineage records (ADR 0185): every position from 0
    /// to the head has one, a position past the head is refused, and a
    /// position below the checkpoint a dataset opened from reads the same
    /// after the open as before it.
    /// </summary>
    [Fact]
    public async Task a_position_has_one_header_hash_before_and_after_a_reopen_and_none_past_the_head()
    {
        await using ObjectHarness harness = new();
        string[] before;

        await using (Dataset writer = await T.OpenOrCreate(await harness.OpenAsync(), Small()))
        {
            await CommitAsync(writer, 0, 12);
            before = [.. await HashesAsync(writer)];
            Assert.Equal(13, before.Distinct().Count());
            Assert.Equal(new string('0', 64).Length, before[0].Length);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await writer.HeaderHashAsync(new Position(writer.Head.Value + 1), T.Ct));
            await writer.CheckpointAsync(new Position(9), T.Ct);
        }

        // Opened from the checkpoint: positions at or below it are in a prefix nothing has read yet.
        await using Dataset reopened = await T.OpenOrCreate(await harness.OpenAsync(instance: "second"), Small());
        Assert.Equal(before[^1], Convert.ToHexStringLower((await reopened.HeaderHashAsync(reopened.Head, T.Ct)).Span));
        Assert.Equal(before[3], Convert.ToHexStringLower((await reopened.HeaderHashAsync(new Position(3), T.Ct)).Span));
        Assert.Equal(before, await HashesAsync(reopened));

        static async Task<List<string>> HashesAsync(Dataset dataset)
        {
            List<string> hashes = [];

            for (long position = 0; position <= dataset.Head.Value; position++)
            {
                hashes.Add(Convert.ToHexStringLower((await dataset.HeaderHashAsync(new Position(position), T.Ct)).Span));
            }

            return hashes;
        }
    }

    /// <summary>ADR 0185 decision 4: the operator states the source and the position, and a promotion that finds otherwise writes nothing.</summary>
    [Fact]
    public async Task a_promotion_that_does_not_find_what_the_operator_expects_writes_nothing()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 4);
        await (await pair.StartAsync())!.ShipAsync(T.Ct);

        await using Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset replica = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct);
        string[] before = [.. pair.Target.Store.Names()];
        pair.Target.Store.ResetCounts();

        Assert.Equal(PromotionOutcome.SourceMismatch, await storage.PromoteInPlaceAsync((await Pair.PromotionAsync(replica, replica.Head)) with { Source = new StoreAddress("memory://another") }, T.Ct));
        Assert.Equal(PromotionOutcome.PositionMismatch, await storage.PromoteInPlaceAsync((await Pair.PromotionAsync(replica, new Position(replica.Head.Value - 1))), T.Ct));

        // A dataset that is no copy: the source itself, followed.
        await using Backend own = await pair.Source.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset follower = await Dataset.OpenFollowerAsync(own, Small(), T.Ct);
        Assert.Equal(PromotionOutcome.NotAReplica, await own.PromoteInPlaceAsync((await Pair.PromotionAsync(follower, follower.Head)), T.Ct));

        Assert.Equal(before, pair.Target.Store.Names());
        Assert.Equal(0, pair.Target.Store.Count(ObjectOperation.Create) + pair.Target.Store.Count(ObjectOperation.Replace) + pair.Target.Store.Count(ObjectOperation.Delete));
        Assert.True((await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.IsReplica);
    }

    /// <summary>
    /// ADR 0185 decision 6: a promotion that replaced the lineage and then
    /// failed leaves a dataset that is not opened for write, and only
    /// promoting again completes it.
    /// </summary>
    [Fact]
    public async Task a_promotion_that_failed_part_way_is_completed_only_by_promoting_again()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 4);
        await (await pair.StartAsync())!.ShipAsync(T.Ct);
        Position tail;

        await using (Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly))
        await using (Dataset replica = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct))
        {
            tail = replica.Head;

            // The store stops taking the writer record: the lineage is replaced, and the claim is not made.
            pair.Target.Store.Hook = (operation, name) => name.Value == "writer" && operation is ObjectOperation.Create or ObjectOperation.Replace
                ? throw new IOException("stopped at the claim")
                : null;
            await Assert.ThrowsAsync<IOException>(async () => await storage.PromoteInPlaceAsync((await Pair.PromotionAsync(replica, tail)), T.Ct));
            pair.Target.Store.Hook = null;
        }

        Assert.False((await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.IsReplica);
        DatasetIsReplicaException refused = await Assert.ThrowsAsync<DatasetIsReplicaException>(async () => await pair.Target.OpenAsync());
        Assert.True(refused.IsPromotionIncomplete);
        Assert.Contains("promoted again", refused.Message, StringComparison.Ordinal);

        // Promoting again, on the same lineage, completes it. A promotion that names another position is still refused.
        Backend again = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset second = await Dataset.OpenFollowerAsync(again, Small(), T.Ct);
        LineagePromotion first = (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.Promoted!;
        Assert.Null(first.Fence);
        Assert.True(await second.TakeWriteAsync(async claim => await again.PromoteInPlaceAsync((await Pair.PromotionAsync(second, tail)), claim) == PromotionOutcome.Promoted, T.Ct));
        await Pair.CompleteAsync(again, second);
        LineagePromotion finished = (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.Promoted!;
        Assert.Equal(first with { Fence = finished.Fence }, finished);
        Assert.NotNull(finished.Fence);
        await CommitAsync(second, 4, 6);
        await second.DisposeAsync();
        await again.DisposeAsync();

        // Closed and opened for write, as any dataset.
        await using Dataset reopened = await T.OpenOrCreate(await pair.Target.OpenAsync(), Small());
        Assert.Equal(tail.Value + 2, reopened.Head.Value);
    }

    /// <summary>
    /// ADR 0184 decision 5, the architect's point A: a set's blobs are
    /// verified as ADR 0213 does before they are created on a target. A
    /// damaged blob is not shipped, the catalog that names its set is not
    /// published, and the log is shipped all the same. Once the source's
    /// writer has written the set anew, the round ships it.
    /// </summary>
    [Fact]
    public async Task a_damaged_blob_of_a_set_is_not_shipped_and_the_catalog_waits_for_it()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        LogShipper shipper = (await pair.StartAsync())!;
        await CommitAsync(writer, 0, 6);
        await writer.CheckpointAsync(new Position(5), T.Ct);

        string name = pair.Source.Store.Names().First(blob => blob.StartsWith("derived/extents/", StringComparison.Ordinal) && blob.EndsWith("/order2", StringComparison.Ordinal));
        byte[] whole = pair.Source.Store.Get(name)!;
        byte[] damaged = [.. whole];
        damaged[0] ^= 0x01;
        pair.Source.Store.Put(name, damaged);

        SourceDamagedException refused = await Assert.ThrowsAsync<SourceDamagedException>(async () => await shipper.ShipAsync(T.Ct));
        Assert.Equal(name["derived/".Length..], refused.Blob!.Value.Value);
        Assert.Null(refused.Slot);

        // The log is there, whole. The damaged blob and the catalog are not; nothing on the target names what it lacks.
        pair.AssertIdentical();
        Assert.DoesNotContain(name, pair.Target.Store.Names());
        Assert.DoesNotContain("derived/catalog", pair.Target.Store.Names());

        // And again at the next round, while it stays damaged.
        await Assert.ThrowsAsync<SourceDamagedException>(async () => await shipper.ShipAsync(T.Ct));
        Assert.DoesNotContain("derived/catalog", pair.Target.Store.Names());

        // The source holds the set whole again: the round ships it, and the catalog after it.
        pair.Source.Store.Put(name, whole);
        ShippedRound round = await shipper.ShipAsync(T.Ct);
        Assert.Equal(new Position(5), round.Catalog!.Position);
        pair.AssertDerivedIdentical();
    }

    /// <summary>
    /// What the shipper's check is (ADR 0213, ADR 0184 decision 5): every
    /// blob of a set a writer wrote verifies, and with any one byte changed
    /// it does not.
    /// </summary>
    [Fact]
    public async Task every_blob_of_a_set_verifies_and_no_byte_of_one_can_change_unseen()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 6);
        await writer.CheckpointAsync(new Position(5), T.Ct);
        DatasetId dataset = CatalogSummary.TryRead(pair.Source.Store.Get("derived/catalog")!.AsMemory(12))!.Dataset;
        string[] blobs = [.. pair.Source.Store.Names().Where(blob => blob.StartsWith("derived/extents/", StringComparison.Ordinal))];
        Assert.Equal(9, blobs.Length);
        string directory = Directory.CreateTempSubdirectory("varve-blob-check-").FullName;
        FileInfo file = new(Path.Combine(directory, "blob"));

        try
        {
            foreach (string blob in blobs)
            {
                BlobName name = new(blob["derived/".Length..]);
                byte[] whole = pair.Source.Store.Get(blob)!;
                await File.WriteAllBytesAsync(file.FullName, whole, T.Ct);
                Assert.True(ExtentBlobCheck.Verifies(name, dataset, file, T.Ct), blob);
                Assert.False(ExtentBlobCheck.Verifies(name, new DatasetId(Guid.NewGuid()), file, T.Ct), blob + " as another dataset's");
                List<int> unseen = [];

                for (int at = 0; at < whole.Length; at++)
                {
                    byte[] changed = [.. whole];
                    changed[at] ^= 0x10;
                    await File.WriteAllBytesAsync(file.FullName, changed, T.Ct);

                    if (ExtentBlobCheck.Verifies(name, dataset, file, T.Ct))
                    {
                        unseen.Add(at);
                    }
                }

                Assert.True(unseen.Count == 0, blob + " of " + whole.Length + " bytes verifies with a byte changed at " + string.Join(", ", unseen));

                // And cut short, or with a byte more.
                await File.WriteAllBytesAsync(file.FullName, whole.AsMemory(0, whole.Length - 1), T.Ct);
                Assert.False(ExtentBlobCheck.Verifies(name, dataset, file, T.Ct), blob + " cut short");
                await File.WriteAllBytesAsync(file.FullName, (byte[])[.. whole, 0], T.Ct);
                Assert.False(ExtentBlobCheck.Verifies(name, dataset, file, T.Ct), blob + " with a byte more");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// ADR 0184 decision 6, the architect's point B: the shipper checks each
    /// source chunk's header and its content hash before its create. A chunk
    /// that fails either is not shipped, the shipper says which slot, and it
    /// ships on once the source reads whole.
    /// </summary>
    [Theory]
    [InlineData(40)]
    [InlineData(-1)]
    public async Task a_source_chunk_that_does_not_verify_is_not_shipped(int flip)
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options());
        await CommitAsync(writer, 0, 2);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipLogAsync(T.Ct);

        // One more commit, in one more chunk, damaged where it lies: in its header's content hash, or in its last byte.
        HashSet<string> had = [.. pair.Source.Store.Names()];
        await CommitAsync(writer, 2, 3);
        string name = Assert.Single(pair.Source.Store.Names(), added => !had.Contains(added) && added.Contains(".chunks/", StringComparison.Ordinal));
        byte[] whole = pair.Source.Store.Get(name)!;
        byte[] damaged = [.. whole];
        damaged[flip < 0 ? damaged.Length - 1 : flip] ^= 0x01;
        pair.Source.Store.Put(name, damaged);

        Dictionary<string, byte[]> before = pair.Target.Snapshot();
        SourceDamagedException refused = await Assert.ThrowsAsync<SourceDamagedException>(async () => await shipper.ShipLogAsync(T.Ct));
        Assert.NotNull(refused.Slot);
        Assert.Null(refused.Blob);
        Assert.Contains("does not verify", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), pair.Target.Store.Names().Order(StringComparer.Ordinal));
        Assert.DoesNotContain(name, pair.Target.Store.Names());

        // Again at the next round, by a shipper started afresh as the role starts one after a failure.
        LogShipper again = (await pair.StartAsync())!;
        await Assert.ThrowsAsync<SourceDamagedException>(async () => await again.ShipLogAsync(T.Ct));
        Assert.DoesNotContain(name, pair.Target.Store.Names());

        // The source reads whole again: the chunk is shipped, and the copy is the source.
        pair.Source.Store.Put(name, whole);
        Assert.True((await again.ShipLogAsync(T.Ct)).Moved);
        pair.AssertIdentical();
    }

    /// <summary>
    /// ADR 0185 decisions 4, 6 and 8, the race the architect's review named:
    /// a shipper that is mid-round when the lineage is replaced creates a
    /// chunk above the position the lineage has just recorded, before the
    /// fence. The promoter keeps those commits, the lineage records the
    /// position at the fence, and they are the source fleet's. The shipper's
    /// next create, at the fence slot, finds other bytes and stops.
    /// </summary>
    /// <remarks>
    /// Three ways. The shipper creates its chunk while the promoter replaces
    /// the lineage, before the promoter has read the tail. The shipper wins
    /// the very slot the promoter was creating its fence at, so the fence
    /// takes the next. And the source has rolled and consolidated its segment
    /// before the shipper copies the rest of it: the shipper seals the
    /// target's segment through its next slot, creates the consolidated
    /// segment and deletes that chunk, all before the promoter's fence, which
    /// then lands in the slot the chunk left. The promoter finds the
    /// segment's object there, adopts the log after it and fences again at
    /// the end, as any writer does since #396: the promotion finishes in the
    /// one request.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task a_shipper_that_wins_a_slot_before_the_fence_has_its_commits_kept_and_recorded_as_the_sources(int way)
    {
        bool atTheFenceSlot = way > 0;
        bool consolidated = way == 2;
        DatasetOptions options = consolidated ? Small() : T.Options();
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), options);
        await CommitAsync(writer, 0, 3);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipAsync(T.Ct);

        Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        Dataset replica = await Dataset.OpenFollowerAsync(storage, options, T.Ct);
        Position recorded = replica.Head;

        // The source commits once more. Its shipper has read the lineage for this round already.
        await CommitAsync(writer, 3, 4);

        if (consolidated)
        {
            // The commit rolled the source's segment, and the source has consolidated it and deleted its chunks:
            // the chunk that sealed it is gone before the shipper could copy it.
            await UntilAsync(() => pair.Source.Store.Names().Contains("log/00000000.seg") && pair.Source.Store.Names().Count(name => name.StartsWith("log/00000000.chunks/", StringComparison.Ordinal)) <= 1);
            Assert.Contains(pair.Source.Store.Names(), name => name.StartsWith("log/00000001.chunks/", StringComparison.Ordinal));
        }

        bool won = false;
        string? contested = null;

        // The shipper itself, past its read of the lineage, so that what arrives is what a shipper creates.
        void Ship(string name)
        {
            won = true;
            contested = name;
            shipper.ShipLogAsyncPastTheLineageForTests(T.Ct).AsTask().GetAwaiter().GetResult();
        }

        pair.Target.Store.Hook = (operation, name) =>
        {
            if (won)
            {
                return null;
            }

            if (atTheFenceSlot && operation == ObjectOperation.Create && name.Value.Contains(".chunks/", StringComparison.Ordinal))
            {
                // The only chunk a promoter creates is its fence. The shipper's creates reach the store first.
                Ship(name.Value);
                return null;
            }

            // Just after the promoter has replaced the lineage, and before it reads the tail.
            return !atTheFenceSlot && operation == ObjectOperation.Replace && name.Value == "lineage" ? () => Ship(name.Value) : null;
        };

        Assert.True(await replica.TakeWriteAsync(async claim => await storage.PromoteInPlaceAsync(await Pair.PromotionAsync(replica, recorded), claim) == PromotionOutcome.Promoted, T.Ct));
        pair.Target.Store.Hook = null;
        Assert.True(won);
        Assert.Equal(recorded, (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.Promoted!.Position);

        if (consolidated)
        {
            // The promoter's first fence took a slot in a segment the shipper had sealed, consolidated and cleared.
            // The writer finds the segment's object after its create (ADR 0136, amended 2026-11-24, #396), adopts
            // it and the log after it, and fences again at the end: the promotion finishes in this one request, and
            // the copy holds the commit the shipper brought.
            Assert.Contains("log/00000000.seg", pair.Target.Store.Names());
            Assert.Contains(contested, pair.Target.Store.Names());
            Assert.StartsWith("log/00000000.chunks/", contested, StringComparison.Ordinal);
            Assert.Equal(recorded.Value + 1, replica.Head.Value);
        }

        await using Backend held = storage;
        await using Dataset promoted = replica;

        // The copy holds the commit the source acknowledged, one above what was first recorded.
        Assert.Equal(recorded.Value + 1, promoted.Head.Value);
        Assert.Equal(writer.Head, promoted.Head);

        // The last write records the position at the fence, so that commit is the source fleet's and the next one is not.
        await Pair.CompleteAsync(storage, promoted);
        LineageRecord lineage = (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!;
        Assert.Equal(promoted.Head, lineage.Promoted!.Position);

        // Where the first fence landed behind a consolidated segment, the fence the lineage names is the second one,
        // in the segment after it.
        Assert.True(way != 2 || lineage.Promoted.Fence!.Value.Value.StartsWith("00000001.chunks/", StringComparison.Ordinal), "the fence is at " + lineage.Promoted.Fence);

        // Where the shipper won the slot the promoter's fence was going to, the fence is above it.
        Assert.True(way != 1 || string.CompareOrdinal("log/" + lineage.Promoted.Fence!.Value.Value, contested) > 0, "the fence is at " + lineage.Promoted.Fence + ", not above " + contested);
        Assert.Equal(Convert.ToHexStringLower((await writer.HeaderHashAsync(writer.Head, T.Ct)).Span), lineage.Promoted.Hash.Value);
        Assert.Equal(lineage.Source, lineage.WriterOf(promoted.Head));
        Assert.Null(lineage.WriterOf(new Position(promoted.Head.Value + 1)));

        // A second promote request finds nothing to promote, whichever position it names.
        await using (Backend other = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly))
        await using (Dataset follower = await Dataset.OpenFollowerAsync(other, options, T.Ct))
        {
            Assert.Equal(PromotionOutcome.NotAReplica, await other.PromoteInPlaceAsync(await Pair.PromotionAsync(follower, follower.Head), T.Ct));
            Assert.Equal(PromotionOutcome.PositionMismatch, await other.PromoteInPlaceAsync(await Pair.PromotionAsync(follower, recorded), T.Ct));
        }

        // The promoted copy writes on. The old source's shipper, still in its round, meets the fence slot and stops there.
        await CommitAsync(promoted, 10, 12);
        await CommitAsync(writer, 4, 6);
        Dictionary<string, byte[]> before = pair.Target.Snapshot();
        Exception stopped = await Assert.ThrowsAnyAsync<Exception>(async () => await shipper.ShipLogAsyncPastTheLineageForTests(T.Ct));
        Assert.IsType<ShippingDivergedException>(stopped);
        await AssertNothingOfTheSourceArrivedAsync(pair, before, promoted, options);

        // And at its next round it reads the lineage, and is refused as promoted.
        await Assert.ThrowsAsync<ReplicaPromotedException>(async () => await shipper.ShipAsync(T.Ct));
    }

    /// <summary>
    /// ADR 0184 decision 4 and ADR 0185 decision 4: a target advances only
    /// through its next chunk slot, so the store decides between a promoter's
    /// fence and a shipper that is about to put a consolidated segment there.
    /// The source has consolidated a segment whose end the target does not
    /// hold. Either the promoter fences first, and the shipper stops without
    /// creating the segment; or the shipper has sealed the segment through
    /// that slot and not yet created its object, and the promoter adopts a
    /// sealed segment and fences in the next.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_consolidated_segment_reaches_a_target_through_its_next_slot_or_not_at_all(bool shipperSealsFirst)
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), Small());
        await CommitAsync(writer, 0, 3);
        LogShipper shipper = (await pair.StartAsync())!;
        await shipper.ShipAsync(T.Ct);
        await CommitAsync(writer, 3, 4);
        await UntilAsync(() => pair.Source.Store.Names().Contains("log/00000000.seg") && pair.Source.Store.Names().Count(name => name.StartsWith("log/00000000.chunks/", StringComparison.Ordinal)) <= 1);
        string slot = "log/00000000.chunks/" + pair.Target.Store.Names().Count(name => name.StartsWith("log/00000000.chunks/", StringComparison.Ordinal)).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain(slot, pair.Source.Store.Names());

        if (shipperSealsFirst)
        {
            // The shipper gets as far as the chunk that seals the segment, and its create of the segment's object fails.
            pair.Target.Store.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value == "log/00000000.seg" ? throw new IOException("the target is held back") : null;
            await Assert.ThrowsAsync<IOException>(async () => await shipper.ShipLogAsyncPastTheLineageForTests(T.Ct));
            pair.Target.Store.Hook = null;
            Assert.Contains(slot, pair.Target.Store.Names());
            Assert.DoesNotContain("log/00000000.seg", pair.Target.Store.Names());
        }

        await using Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset replica = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct);
        Position tail = replica.Head;
        Assert.True(await replica.TakeWriteAsync(async claim => await storage.PromoteInPlaceAsync(await Pair.PromotionAsync(replica, tail), claim) == PromotionOutcome.Promoted, T.Ct));
        await Pair.CompleteAsync(storage, replica);
        string fence = "log/" + (await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.Promoted!.Fence!.Value.Value;

        // The fence holds the slot, or the shipper's sealing chunk does and the fence is in the next segment.
        Assert.Equal(shipperSealsFirst ? "log/00000001.chunks/00000000" : slot, fence);
        await CommitAsync(replica, 10, 12);

        // The shipper, still in its round: it stops at a slot it does not hold, and no segment of the source's
        // replaces a segment the promoted copy writes in.
        Dictionary<string, byte[]> before = pair.Target.Snapshot();
        Exception stopped = await Assert.ThrowsAnyAsync<Exception>(async () => await shipper.ShipLogAsyncPastTheLineageForTests(T.Ct));
        Assert.IsType<ShippingDivergedException>(stopped);

        // It stopped at the fence: the one slot both wanted. Before it, where the promoter came first, the shipper
        // had created nothing: neither the chunk that seals the segment nor the segment's object.
        Assert.Equal(fence, "log/" + ((ShippingDivergedException)stopped).Slot!.Value.Value);
        await AssertNothingOfTheSourceArrivedAsync(pair, before, replica);
        await CommitAsync(replica, 12, 13);
        Assert.Equal(tail.Value + 3, replica.Head.Value);
    }

    private static async Task UntilAsync(Func<bool> reached)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

        while (!reached())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "The source did not get there in thirty seconds.");
            await Task.Delay(10, T.Ct);
        }
    }

    /// <summary>
    /// ADR 0185 decision 4, amended 2026-11-25: the promotion's last write
    /// still refuses a fence whose segment has a consolidated object. No
    /// order of a shipper and a promoter reaches that since #396, so the
    /// object is put in the store by hand here: the guard is what is tested.
    /// </summary>
    [Fact]
    public async Task the_last_write_of_a_promotion_refuses_a_fence_behind_a_consolidated_segment()
    {
        await using Pair pair = new();
        await using Dataset writer = await T.OpenOrCreate(await pair.Source.OpenAsync(), T.Options());
        await CommitAsync(writer, 0, 3);
        await (await pair.StartAsync())!.ShipAsync(T.Ct);

        await using Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset replica = await Dataset.OpenFollowerAsync(storage, T.Options(), T.Ct);
        Position tail = replica.Head;
        Assert.True(await replica.TakeWriteAsync(async claim => await storage.PromoteInPlaceAsync(await Pair.PromotionAsync(replica, tail), claim) == PromotionOutcome.Promoted, T.Ct));
        pair.Target.Store.Put("log/00000000.seg", [1]);

        IOException unfinished = await Assert.ThrowsAsync<IOException>(async () => await Pair.CompleteAsync(storage, replica));

        Assert.Contains("Promote again", unfinished.Message, StringComparison.Ordinal);
        Assert.Null((await Backend.ReadLineageAsync(pair.Target.Store, null, T.Ct))!.Promoted!.Fence);
        storage.ReleaseOnClose = ReleaseReason.Promotion;
    }

    /// <summary>
    /// ADR 0185 decision 2, the architect's point C: the lineage carries the
    /// chain. A promoted copy shipped onward and promoted again says which
    /// fleet wrote which range of positions.
    /// </summary>
    [Fact]
    public async Task a_copy_of_a_promoted_copy_carries_the_chain_and_names_the_fleet_of_every_position()
    {
        DatasetId firstFleet = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        DatasetId secondFleet = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        await using ObjectHarness origin = new();
        await using ObjectHarness middle = Pair.Verified(new ObjectHarness());
        await using ObjectHarness last = Pair.Verified(new ObjectHarness());
        await using Dataset writer = await T.OpenOrCreate(await origin.OpenAsync(), Small());
        await CommitAsync(writer, 0, 3);

        // origin, written under the first fleet, is shipped to middle, which is promoted at p1 and written under the second.
        LineageSource fromOrigin = new(new StoreAddress("memory://origin"), null, T.Id, firstFleet);
        await (await Pair.StartAsync(origin, middle, fromOrigin))!.ShipAsync(T.Ct);
        Backend promoting = await middle.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset second = await Dataset.OpenFollowerAsync(promoting, Small(), T.Ct);
        Position p1 = second.Head;
        Assert.True(await second.TakeWriteAsync(async claim => await promoting.PromoteInPlaceAsync(await Pair.PromotionAsync(second, p1, "memory://origin"), claim) == PromotionOutcome.Promoted, T.Ct));
        await Pair.CompleteAsync(promoting, second);
        await CommitAsync(second, 3, 6);

        // middle is shipped on to last, which is promoted at p2.
        LineageSource fromMiddle = new(new StoreAddress("memory://middle"), null, T.Id, secondFleet);
        await (await Pair.StartAsync(middle, last, fromMiddle))!.ShipAsync(T.Ct);
        LineageRecord replica = (await Backend.ReadLineageAsync(last.Store, null, T.Ct))!;
        Assert.True(replica.IsReplica);
        Assert.Equal(fromMiddle, replica.Source);
        LineageLink link = Assert.Single(replica.Earlier);
        Assert.Equal(fromOrigin, link.Source);
        Assert.Equal(p1, link.Promoted.Position);

        // Every commit of the replica has a writer that is not the serving fleet: the first fleet to p1, the second after.
        Assert.Equal(fromOrigin, replica.WriterOf(new Position(1)));
        Assert.Equal(fromOrigin, replica.WriterOf(p1));
        Assert.Equal(fromMiddle, replica.WriterOf(new Position(p1.Value + 1)));
        Assert.Equal(fromMiddle, replica.WriterOf(new Position(p1.Value + 1000)));

        Backend promotingLast = await last.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset third = await Dataset.OpenFollowerAsync(promotingLast, Small(), T.Ct);
        Position p2 = third.Head;
        Assert.Equal(p1.Value + 3, p2.Value);
        Assert.True(await third.TakeWriteAsync(async claim => await promotingLast.PromoteInPlaceAsync(await Pair.PromotionAsync(third, p2, "memory://middle"), claim) == PromotionOutcome.Promoted, T.Ct));
        await Pair.CompleteAsync(promotingLast, third);

        // After two promotions: three ranges, read from the lineage alone, and it reads back the same from its bytes.
        LineageRecord twice = (await Backend.ReadLineageAsync(last.Store, null, T.Ct))!;
        Assert.Equal(fromOrigin, twice.WriterOf(p1));
        Assert.Equal(fromMiddle, twice.WriterOf(new Position(p1.Value + 1)));
        Assert.Equal(fromMiddle, twice.WriterOf(p2));
        Assert.Null(twice.WriterOf(new Position(p2.Value + 1)));
        Assert.Equal(twice.Earlier, LineageRecordFormat.Read(LineageRecordFormat.Write(twice)).Earlier);
        Assert.Equal(2, twice.ChainForACopy().Count);
    }

    /// <summary>
    /// The failure-injection gate (#166): the shipper is stopped at every
    /// write and delete it makes on the target, once before the store sees
    /// the request and once after the store applied it. Whatever it left, the
    /// target's log is a prefix of the source's, its catalog names only what
    /// is there, and a follower of it reads what the source's writer reads to
    /// that point. A shipper started again finishes, and the copy is whole.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_shipper_stopped_at_any_write_leaves_a_readable_prefix_and_the_next_one_finishes(bool answerLost)
    {
        int mutations = await ScheduleAsync(null, answerLost);
        Assert.True(mutations > 40, "the schedule makes " + mutations + " writes and deletes on the target");

        for (int at = 0; at < mutations; at++)
        {
            try
            {
                await ScheduleAsync(at, answerLost);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                throw new Xunit.Sdk.XunitException("Stopped at the target's write " + at + " of " + mutations + (answerLost ? ", its answer lost: " : ", before the store saw it: ") + failure);
            }
        }
    }

    // One history of a source with every kind of change a round meets, shipped
    // after each: chunks, a checkpoint, segments consolidated after and before
    // they were shipped, a newer catalog, a merge and the retired sets'
    // removal. Returns the writes and deletes the target saw.
    private static async Task<int> ScheduleAsync(int? stopAt, bool answerLost)
    {
        await using Pair pair = new();
        Backend storage = await pair.Source.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMergeTests.Merging(Small(), 2));
        int mutations = 0;
        bool stopped = false;
        pair.Target.Store.Hook = (operation, _) =>
        {
            if (stopped)
            {
                throw new IOException("the shipper has stopped");
            }

            if (operation is ObjectOperation.Read or ObjectOperation.List || mutations++ != stopAt)
            {
                return null;
            }

            stopped = true;
            return answerLost ? () => throw new IOException("stopped after the store applied the write") : throw new IOException("stopped before the write");
        };

        LogShipper? shipper = null;

        async Task ShipAsync()
        {
            try
            {
                shipper ??= await pair.StartAsync();
                await shipper!.ShipAsync(T.Ct);
            }
            catch (IOException) when (stopped)
            {
                stopped = false;
                stopAt = null;
                await pair.AssertAStoppedShipperLeftAPrefixAsync(writer);
                shipper = await pair.StartAsync();
                await shipper!.ShipAsync(T.Ct);
            }
        }

        await CommitAsync(writer, 0, 3);
        await ShipAsync();
        await writer.CheckpointAsync(new Position(2), T.Ct);
        await CommitAsync(writer, 3, 30);
        await storage.MaintainStorageAsync(T.Ct);
        await ShipAsync();
        await writer.CheckpointAsync(new Position(20), T.Ct);
        await CommitAsync(writer, 30, 34);
        await ShipAsync();
        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        await ShipAsync();
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        await ShipAsync();
        await ShipAsync();

        Assert.False(stopped);
        pair.Target.Store.Hook = null;
        Assert.Equal(pair.SourceSets(), pair.TargetSets());
        pair.AssertDerivedIdentical();
        pair.AssertIdentical();
        await pair.AssertFollowersAgreeAsync(writer);
        return mutations;
    }

    /// <summary>
    /// A refused or stopped shipper overwrote nothing, and the promoted
    /// copy's log is still the promoted writer's. The names are not compared:
    /// the promoted writer consolidates its own segments in the background.
    /// </summary>
    private static async Task AssertNothingOfTheSourceArrivedAsync(Pair pair, Dictionary<string, byte[]> before, Dataset promoted, DatasetOptions? options = null)
    {
        foreach (string name in pair.Target.Store.Names())
        {
            if (before.TryGetValue(name, out byte[]? bytes) && pair.Target.Store.Get(name) is { } now)
            {
                Assert.Equal(bytes, now);
            }
        }

        await using Backend storage = await pair.Target.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset follower = await Dataset.OpenFollowerAsync(storage, options ?? Small(), T.Ct);
        Assert.Equal(promoted.Head, follower.Head);
        Assert.Equal((await promoted.HeaderHashAsync(promoted.Head, T.Ct)).ToArray(), (await follower.HeaderHashAsync(follower.Head, T.Ct)).ToArray());
    }

    private static async Task CommitAsync(Dataset dataset, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 200) + i)), T.Ct);
        }
    }

    /// <summary>A source and a target, each an in-memory store with its own harness.</summary>
    private sealed class Pair : IAsyncDisposable
    {
        private readonly List<string> _staging = [];

        public ObjectHarness Source { get; } = new();

        public ObjectHarness Target { get; } = Verified(new ObjectHarness());

        // A target is a verified store (ADR 0184 decision 9): the record a passing run of the suite leaves at its root.
        public static ObjectHarness Verified(ObjectHarness harness)
        {
            harness.Store.Put(ObjectStoreVerification.RecordName.Value, VerificationRecordFormat.Write(new VerificationRecord
            {
                Understood = true,
                Suite = ObjectStoreVerification.Suite,
                VerifiedAt = new VerificationTime(DateTimeOffset.UnixEpoch),
            }));
            return harness;
        }

        public ValueTask<LogShipper?> StartAsync(StoreAddress? store = null)
        {
            string staging = Directory.CreateTempSubdirectory("varve-ship-").FullName;
            _staging.Add(staging);
            return LogShipper.StartAsync(
                new ShippingOptions
                {
                    Source = Source.Store,
                    Target = Target.Store,
                    Lineage = new LineageSource(store ?? new StoreAddress("memory://source"), null, T.Id, null),
                    Reader = new ReaderId("shipper-ship-target"),
                    Holder = new WriterInstance("shipper"),
                    Staging = new CacheDirectory(staging),
                    Clock = new InstantClock(DateTimeOffset.UnixEpoch),
                },
                T.Ct);
        }

        /// <summary>
        /// The byte-identity gate: every log object both stores hold is the
        /// same bytes, the target holds no log object the source never had a
        /// place for, and the log read from the target is a prefix of the
        /// source's that ends where the target's objects end.
        /// </summary>
        public void AssertIdentical()
        {
            foreach (string name in Target.Store.Names().Where(name => name.StartsWith("log/", StringComparison.Ordinal)))
            {
                if (Source.Store.Get(name) is { } source)
                {
                    Assert.True(source.AsSpan().SequenceEqual(Target.Store.Get(name)), name + " differs between the source and the target.");
                }
            }

            byte[] theirs = LogOf(Source.Store);
            byte[] ours = LogOf(Target.Store);
            Assert.True(ours.Length <= theirs.Length, "The target's log is longer than the source's.");
            Assert.True(theirs.AsSpan(0, ours.Length).SequenceEqual(ours), "The log read from the target is not the source's.");
        }

        /// <summary>The target's catalog is the source's bytes, and every extent blob both hold is identical.</summary>
        /// <summary>What a stopped shipper left: a prefix of the log, a catalog that names only what is there, and a dataset a follower reads.</summary>
        public async Task AssertAStoppedShipperLeftAPrefixAsync(Dataset writer)
        {
            AssertIdentical();
            AssertTargetCatalogNamesOnlyWhatIsThere();

            if (!Target.Store.Names().Contains("log/MANIFEST"))
            {
                return;
            }

            // A follower opens it wherever the shipper stopped: also where the newest segment holds only its opening
            // chunk, or its trailer and no seal chunk, which the shipper passes through at every roll (#384, ADR 0164).
            // Closed with the check, so its reader record does not hold the target's sets through the rest of the
            // schedule.
            await using Backend storage = await Target.OpenAsync(ObjectStorageAccess.ReadOnly);
            await using Dataset follower = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct);
            List<SortedSet<string>> views = await FormatFixtures.ViewsAsync(follower);
            Assert.Equal((await FormatFixtures.ViewsAsync(writer)).Take(views.Count), views);
        }

        /// <summary>The promotion's last write: the lineage records the head and its hash as they are at the fence.</summary>
        public static async Task CompleteAsync(Backend storage, Dataset promoted) =>
            await storage.CompletePromotionAsync(promoted.Head, new CommitHeaderHash(Convert.ToHexStringLower((await promoted.HeaderHashAsync(promoted.Head, T.Ct)).Span)), T.Ct);

        /// <summary>A promotion of the target by the instance "promoter", as an operator who expects <paramref name="expected"/> asks for it.</summary>
        public static async Task<PromotionRequest> PromotionAsync(Dataset replica, Position expected, string source = "memory://source") => new()
        {
            Instance = new WriterInstance("promoter"),
            Source = new StoreAddress(source),
            Expected = expected,
            Verified = replica.Head,
            Hash = new CommitHeaderHash(Convert.ToHexStringLower((await replica.HeaderHashAsync(replica.Head, T.Ct)).Span)),
            Randomness = TestRandomness.Instance,
        };

        /// <summary>A shipper from one harness to another, under the lineage given.</summary>
        public static ValueTask<LogShipper?> StartAsync(ObjectHarness source, ObjectHarness target, LineageSource lineage) =>
            LogShipper.StartAsync(
                new ShippingOptions
                {
                    Source = source.Store,
                    Target = target.Store,
                    Lineage = lineage,
                    Reader = new ReaderId("shipper-ship-" + lineage.Store.Value[^6..]),
                    Holder = new WriterInstance("shipper"),
                    Staging = new CacheDirectory(Directory.CreateTempSubdirectory("varve-ship-").FullName),
                    Clock = new InstantClock(DateTimeOffset.UnixEpoch),
                },
                T.Ct);

        public string[] SourceSets() => Sets(Source.Store);

        public string[] TargetSets() => Sets(Target.Store);

        private static string[] Sets(MemoryObjectStore store) =>
            [.. store.Names().Where(name => name.StartsWith("derived/extents/", StringComparison.Ordinal)).Select(name => name[..(name.IndexOf('/', "derived/extents/".Length) + 1)]).Distinct().Order(StringComparer.Ordinal)];

        public void AssertDerivedIdentical()
        {
            Assert.Equal(Source.Store.Get("derived/catalog"), Target.Store.Get("derived/catalog"));

            foreach (string name in Target.Store.Names().Where(name => name.StartsWith("derived/extents/", StringComparison.Ordinal)))
            {
                Assert.True(Source.Store.Get(name) is { } source && source.AsSpan().SequenceEqual(Target.Store.Get(name)), name + " is not the source's.");
            }

            AssertTargetCatalogNamesOnlyWhatIsThere();
        }

        /// <summary>Every set the target's catalog names has its six order extents and its dictionary on the target.</summary>
        public void AssertTargetCatalogNamesOnlyWhatIsThere()
        {
            if (Target.Store.Get("derived/catalog") is not { Length: > 12 } wrapped)
            {
                return;
            }

            CatalogSummary catalog = CatalogSummary.TryRead(wrapped.AsMemory(12)) ?? throw new Xunit.Sdk.XunitException("The target's catalog does not read.");
            HashSet<string> names = [.. Target.Store.Names()];

            foreach (CatalogSet set in catalog.Sets)
            {
                foreach (string part in new[] { "order0", "order1", "order2", "order3", "order4", "order5", "terms" })
                {
                    Assert.Contains("derived/" + set.Name.Value + part, names);
                }
            }
        }

        /// <summary>A follower of the target and one of the source read the same at every position, to the writer's head.</summary>
        public async Task AssertFollowersAgreeAsync(Dataset writer)
        {
            await using Dataset target = await Dataset.OpenFollowerAsync(await Target.OpenAsync(ObjectStorageAccess.ReadOnly), Small(), T.Ct);
            Assert.Equal(writer.Head, target.Head);
            Assert.Equal(await FormatFixtures.ViewsAsync(writer), await FormatFixtures.ViewsAsync(target));
            Assert.True(LogOf(Source.Store).AsSpan().SequenceEqual(LogOf(Target.Store)), "The target's log is not the whole of the source's.");
        }

        // The log's bytes as the store's objects give them: each segment's
        // consolidated object, or else its chunks' bytes in order.
        private static byte[] LogOf(MemoryObjectStore store)
        {
            SortedDictionary<int, SortedDictionary<int, byte[]>> chunks = [];
            SortedDictionary<int, byte[]> consolidated = [];

            // The names are listed and then read one by one, and a writer's consolidation deletes chunks behind
            // its last commit. A name gone between the two means the store moved: the whole reading is taken again.
            for (bool whole = false; !whole;)
            {
                whole = true;
                chunks.Clear();
                consolidated.Clear();

                foreach (string name in store.Names().Where(name => name.StartsWith("log/", StringComparison.Ordinal) && name != "log/MANIFEST"))
                {
                    if (store.Get(name) is not { } bytes)
                    {
                        whole = false;
                        break;
                    }

                    int segment = int.Parse(name.AsSpan(4, 8), System.Globalization.CultureInfo.InvariantCulture);

                    if (name.EndsWith(".seg", StringComparison.Ordinal))
                    {
                        consolidated[segment] = bytes;
                    }
                    else
                    {
                        (chunks.TryGetValue(segment, out var of) ? of : chunks[segment] = [])[int.Parse(name.AsSpan(^8), System.Globalization.CultureInfo.InvariantCulture)] = bytes[ChunkHeader.Size..];
                    }
                }
            }

            using MemoryStream log = new();

            foreach (int segment in consolidated.Keys.Union(chunks.Keys).Order())
            {
                if (consolidated.TryGetValue(segment, out byte[]? whole))
                {
                    log.Write(whole);
                }
                else
                {
                    foreach (byte[] part in chunks[segment].Values)
                    {
                        log.Write(part);
                    }
                }
            }

            return log.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            await Target.DisposeAsync();

            foreach (string staging in _staging)
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
