// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The fence (ADR 0136) over generated schedules: a second writer claims the
/// epoch at any request of the first, before or after the store applies it,
/// and the first writer's every create after that is either a slot of its
/// own that already exists or the fence slot, which exists too.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectEpochPropertyTests
{
    private const int Iterations = 200;

    /// <summary>
    /// A writer that claimed an epoch, and lists the log only after a newer
    /// epoch's fence is in it, is superseded and fences nothing below it
    /// (ADR 0136, #165): its open finds the later epoch among the chunks it
    /// lists, not in a slot it races for. Found by the concurrent-claims
    /// case, where it was one run in about a hundred; replayed here in order.
    /// </summary>
    [Fact]
    public async Task a_writer_that_lists_a_newer_epochs_fence_is_superseded_and_the_newer_writer_holds_the_log()
    {
        DatasetOptions options = T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var small
            ? new DatasetOptions { Clock = small.Clock, MaxRecordBytes = small.MaxRecordBytes, SegmentBytes = small.SegmentBytes, CommitCache = small.CommitCache, Maintenance = MaintenanceMode.Off }
            : throw new InvalidOperationException();
        MemoryObjectStore store = new();
        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        await using (Dataset first = await T.OpenOrCreate(await objects.OpenAsync(instance: "first"), options))
        {
            await first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        // a has claimed its epoch and is about to list the log; b claims the next and fences first.
        Backend? later = null;
        bool armed = true;
        store.Hook = (operation, name) =>
        {
            if (armed && operation == ObjectOperation.List && name.Value.Contains("log/", StringComparison.Ordinal))
            {
                armed = false;
                later = Task.Run(() => objects.OpenAsync(instance: "b")).GetAwaiter().GetResult();
            }

            return null;
        };

        Backend earlier = await objects.OpenAsync(instance: "a");
        store.Hook = null;

        Assert.NotNull(later);
        Assert.Equal(new WriterEpoch(3), later!.Epoch);
        Assert.False(later.IsSuperseded);
        Assert.Null(earlier.Epoch);
        Assert.True(earlier.IsSuperseded);

        // b writes on; a, reading the log b wrote, writes nothing.
        await using Dataset writer = await Dataset.OpenAsync(later, options, T.Ct);
        Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s2"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);
    }

    /// <summary>
    /// The log's last segment holds chunks of more than one epoch: it was
    /// opened in epoch 1, and b, of epoch 3, fenced it and committed into it,
    /// before a, which claimed epoch 2, listed it. Its first chunk is of epoch 1
    /// and its last of epoch 3, so a is superseded, and creates nothing.
    /// </summary>
    [Fact]
    public async Task a_writer_whose_last_segment_starts_in_an_older_epoch_and_ends_in_a_newer_one_is_superseded()
    {
        DatasetOptions options = SmallSegments(64 << 20);
        MemoryObjectStore store = new();
        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        await using (Dataset first = await T.OpenOrCreate(await objects.OpenAsync(instance: "first"), options))
        {
            await first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        Dataset? writer = null;
        bool armed = true;
        store.Hook = (operation, name) =>
        {
            if (armed && operation == ObjectOperation.List && name.Value.Contains("log/", StringComparison.Ordinal))
            {
                armed = false;
                writer = Task.Run(async () =>
                {
                    Dataset opened = await Dataset.OpenAsync(await objects.OpenAsync(instance: "b"), options, T.Ct);
                    Assert.Equal(CommitOutcome.Committed, (await opened.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s2"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);
                    return opened;
                }).GetAwaiter().GetResult();
            }

            return null;
        };

        Backend earlier;

        try
        {
            earlier = await objects.OpenAsync(instance: "a");
        }
        finally
        {
            store.Hook = null;
        }

        await using Dataset held = writer!;
        string[] chunks = await ChunksAsync(store);

        // One segment, from epoch 1's opening chunk to epoch 3's commit, with epoch 3's fence between.
        Assert.All(chunks, name => Assert.StartsWith("log/00000000.chunks/", name, StringComparison.Ordinal));
        Assert.Equal(1, await EpochOfAsync(store, chunks[0]));
        Assert.Equal(3, await EpochOfAsync(store, chunks[^1]));
        Assert.Contains(chunks, name => EpochOfAsync(store, name).AsTask().GetAwaiter().GetResult() == 1 && name != chunks[0]);

        Assert.True(earlier.IsSuperseded);
        Assert.Null(earlier.Epoch);

        // b holds the log, and its next commit lands in the slot a did not take.
        Assert.Equal(CommitOutcome.Committed, (await held.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s3"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);
        Assert.Equal(chunks.Length + 1, (await ChunksAsync(store)).Length);
    }

    /// <summary>
    /// The log ends in a segment that b, of epoch 3, sealed and consolidated,
    /// and b stopped before it opened the next: its create of the next
    /// segment's first chunk failed. a, which claimed epoch 2, lists the log
    /// then. The listing shows the segment's consolidated object, and the
    /// open reads no chunk of a consolidated segment, so the epoch of the
    /// seal is not in hand. a must be superseded, and must not fence the next
    /// segment under b's seal.
    /// </summary>
    [Fact]
    public async Task a_writer_whose_log_ends_in_a_segment_a_newer_epoch_sealed_and_consolidated_is_superseded()
    {
        DatasetOptions options = SmallSegments(1024);
        MemoryObjectStore store = new();
        await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        await using (Dataset first = await T.OpenOrCreate(await objects.OpenAsync(instance: "first"), options))
        {
            await first.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        const string Next = "log/00000001.chunks/00000000";
        const string Consolidated = "log/00000000.seg";
        bool armed = true;
        bool failing = false;
        Dataset? stopped = null;
        store.Hook = (operation, name) =>
        {
            if (failing && operation == ObjectOperation.Create && name.Value == Next)
            {
                throw new System.IO.IOException("The writer of epoch 3 stops before it opens the next segment.");
            }

            if (armed && operation == ObjectOperation.List && name.Value.Contains("log/", StringComparison.Ordinal))
            {
                armed = false;
                failing = true;
                stopped = Task.Run(async () =>
                {
                    Dataset opened = await Dataset.OpenAsync(await objects.OpenAsync(instance: "b"), options, T.Ct);

                    for (int i = 0; await ConditionalWrites.ReadWholeAsync(store, new ObjectName(Consolidated), T.Ct) is null; i++)
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

                    return opened;
                }).GetAwaiter().GetResult();
                failing = false;
            }

            return null;
        };

        Backend earlier;

        try
        {
            earlier = await objects.OpenAsync(instance: "a");
        }
        finally
        {
            store.Hook = null;
        }

        await using Dataset b = stopped!;
        Assert.NotNull(await ConditionalWrites.ReadWholeAsync(store, new ObjectName(Consolidated), T.Ct));
        Assert.True(await ConditionalWrites.ReadWholeAsync(store, new ObjectName(Next), T.Ct) is null, "a, of epoch 2, fenced the next segment after epoch 3's seal");
        Assert.True(earlier.IsSuperseded);
        Assert.Null(earlier.Epoch);
    }

    // The land gate's count; VARVE_FENCE_SCHEDULES raises it nightly (docs/testing.md).
    private static readonly int FenceSchedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_FENCE_SCHEDULES"), out int n) ? n : 300;

    /// <summary>
    /// The fence over schedules that seal, consolidate and stall a claimant
    /// (ADR 0136 decision 2, #181). The first writer commits; a claims epoch 2
    /// and stalls, before it lists the log or before its fence's create; in
    /// the stall b claims epoch 3 and commits, sealing segments, maintains
    /// them (consolidation and its deletes) after any commit, and may stop at
    /// any of its creates. Then a goes on. Whatever the schedule:
    /// - the chunks left in the store carry epochs that never fall in log order;
    /// - if b fenced, a holds nothing, and b, unless it stopped, commits again;
    /// - if a holds, it commits, and the log still reads whole.
    /// </summary>
    [Fact]
    public async Task a_stalled_claimant_never_fences_past_a_newer_epoch_whatever_was_sealed_or_consolidated_meanwhile()
    {
        Gen<(int First, int Later, uint Maintain, int StopAt, bool BeforeFence, int Segment, bool Deletes)> schedules =
            Gen.Select(Gen.Int[0, 4], Gen.Int[0, 24], Gen.UInt, Gen.Int[0, 80], Gen.Bool, Gen.OneOfConst(1024, 2048, 4096), Gen.Bool);

        await schedules.SampleAsync(
            async schedule =>
            {
                DatasetOptions options = SmallSegments(schedule.Segment);
                MemoryObjectStore store = new();
                await using ObjectHarness objects = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
                await using (Dataset first = await T.OpenOrCreate(await objects.OpenAsync(instance: "first"), options))
                {
                    for (int i = 0; i < schedule.First; i++)
                    {
                        await first.CommitAsync(Commit("f", i), T.Ct);
                    }
                }

                System.Threading.AsyncLocal<string?> actor = new();
                int creates = 0;
                bool stalled = false;
                bool stopped = false;
                Dataset? later = null;
                Backend? laterStorage = null;

                store.Hook = (operation, name) =>
                {
                    bool log = name.Value.StartsWith("log/", StringComparison.Ordinal);

                    // b stops at any of its writes to the log, a create or a delete;
                    // its deletes after it stopped are the maintenance it ran then.
                    if (actor.Value == "b" && operation == ObjectOperation.Create && log && (stopped || creates++ >= schedule.StopAt))
                    {
                        stopped = true;
                        throw new System.IO.IOException("b stops here.");
                    }

                    if (actor.Value == "b" && operation == ObjectOperation.Delete && log && !stopped && creates++ >= schedule.StopAt)
                    {
                        stopped = true;
                        throw new System.IO.IOException("b stops here.");
                    }

                    bool stall = schedule.BeforeFence ? operation == ObjectOperation.Create && log : operation == ObjectOperation.List && log;

                    if (actor.Value == "a" && stall && !stalled)
                    {
                        stalled = true;
                        (later, laterStorage) = Task.Run(async () =>
                        {
                            actor.Value = "b";
                            Backend? storage = null;
                            Dataset? opened = null;

                            try
                            {
                                storage = await objects.OpenAsync(instance: "b");
                                opened = await Dataset.OpenAsync(storage, options, T.Ct);

                                for (int i = 0; i < schedule.Later && !stopped; i++)
                                {
                                    await opened.CommitAsync(Commit("b", i), T.Ct);

                                    if ((schedule.Maintain >> (i % 32) & 1) != 0)
                                    {
                                        while (await storage.MaintainStorageAsync(T.Ct))
                                        {
                                        }
                                    }
                                }
                            }
                            catch (Exception failure) when (failure is System.IO.IOException or WriterSupersededException)
                            {
                                // b stopped, or found itself superseded: it writes no more.
                            }

                            // A writer stopped mid-roll may still run its log's maintenance: the
                            // seal's consolidation done, its deletes leave only the fences.
                            if (schedule.Deletes && storage is not null)
                            {
                                try
                                {
                                    while (await storage.MaintainStorageAsync(T.Ct))
                                    {
                                    }
                                }
                                catch (Exception failure) when (failure is System.IO.IOException or WriterSupersededException)
                                {
                                }
                            }

                            return (opened, storage);
                        }).GetAwaiter().GetResult();
                    }

                    return null;
                };

                Backend earlier;
                actor.Value = "a";

                try
                {
                    earlier = await objects.OpenAsync(instance: "a");
                }
                finally
                {
                    actor.Value = null;
                    store.Hook = null;
                }

                await using Backend? laterHeld = laterStorage;
                await using Dataset? laterOpen = later;
                (string Name, long Epoch)[] chunks = await HeadersAsync(store);

                for (int i = 1; i < chunks.Length; i++)
                {
                    Assert.True(chunks[i].Epoch >= chunks[i - 1].Epoch, chunks[i].Name + " is of epoch " + chunks[i].Epoch + " after " + chunks[i - 1].Name + " of epoch " + chunks[i - 1].Epoch);
                }

                // b's fence, if it made one, is kept for ever: it is the proof that b fenced.
                bool fenced = chunks.Any(chunk => chunk.Epoch == 3);

                if (fenced)
                {
                    Assert.Null(earlier.Epoch);

                    if (laterOpen is not null && !stopped && laterHeld!.Epoch is not null)
                    {
                        Assert.Equal(CommitOutcome.Committed, (await laterOpen.CommitAsync(Commit("b", 1000), T.Ct)).Outcome);
                    }
                }

                if (earlier.Epoch is not null)
                {
                    Assert.False(fenced);
                    await using Dataset held = await Dataset.OpenAsync(earlier, options, T.Ct);
                    Assert.Equal(CommitOutcome.Committed, (await held.CommitAsync(Commit("a", 0), T.Ct)).Outcome);
                    chunks = await HeadersAsync(store);

                    for (int i = 1; i < chunks.Length; i++)
                    {
                        Assert.True(chunks[i].Epoch >= chunks[i - 1].Epoch, chunks[i].Name + " after " + chunks[i - 1].Name);
                    }
                }

                // A reader opened afresh verifies the whole log, once a writer has
                // written last: a writer stopped mid-roll leaves a segment only
                // the next writer seals.
                if (earlier.Epoch is not null || (laterOpen is not null && !stopped && laterHeld!.Epoch is not null))
                {
                    await using Dataset reader = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), options, T.Ct);
                    Assert.True(reader.Head.Value >= schedule.First);
                }
            },
            iter: FenceSchedules,
            print: schedule => schedule.ToString());
    }

    private static CommitRequestBuilder Commit(string who, int i) => new CommitRequestBuilder().Assert(T.Iri(who + i), T.Iri("p"), T.Literal(new string('x', 60)));

    // Every chunk still in the store, in log order, with its header's epoch.
    private static async Task<(string Name, long Epoch)[]> HeadersAsync(MemoryObjectStore store)
    {
        List<(string, long)> headers = [];

        foreach (string name in await ChunksAsync(store))
        {
            headers.Add((name, await EpochOfAsync(store, name)));
        }

        return [.. headers];
    }

    private static DatasetOptions SmallSegments(int segmentBytes) => T.Options(maxRecordBytes: 128, segmentBytes: segmentBytes) is var small
        ? new DatasetOptions { Clock = small.Clock, MaxRecordBytes = small.MaxRecordBytes, SegmentBytes = small.SegmentBytes, CommitCache = small.CommitCache, Maintenance = MaintenanceMode.Off }
        : throw new InvalidOperationException();

    // The log's chunks, in log order.
    private static async Task<string[]> ChunksAsync(MemoryObjectStore store) =>
        [.. (await store.ListAsync(new ObjectName("log/"), T.Ct)).Select(entry => entry.Name.Value).Where(name => name.Contains(".chunks/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    private static async ValueTask<long> EpochOfAsync(MemoryObjectStore store, string chunk)
    {
        ObjectRead read = (await ConditionalWrites.ReadWholeAsync(store, new ObjectName(chunk), T.Ct))!;
        Assert.True(ChunkHeader.TryRead(read.Bytes.Span, out ChunkHeader header, out _));
        return header.Epoch;
    }

    [Fact]
    public async Task a_superseded_writer_attempts_only_its_own_slots_or_the_fence_and_appends_nothing_after_it()
    {
        Gen<(int Batches, int Trigger, bool AfterApply, int SegmentBytes)> schedules =
            Gen.Select(Gen.Int[2, 12], Gen.Int[1, 90], Gen.Bool, Gen.OneOfConst(1024, 2048, 64 << 20));

        await schedules.SampleAsync(
            async schedule =>
            {
                await using ObjectHarness harness = new();
                Backend first = await harness.OpenAsync(instance: "first");
                Dataset writer = await T.OpenOrCreate(first, T.Options(maxRecordBytes: 256, segmentBytes: schedule.SegmentBytes));

                int requests = 0;
                bool taking = false;
                Backend? second = null;
                HashSet<string>? existedAtFence = null;
                List<string> attemptsAfter = [];

                harness.Store.Hook = (operation, name) =>
                {
                    if (taking)
                    {
                        return null;
                    }

                    if (second is not null)
                    {
                        if (operation == ObjectOperation.Create)
                        {
                            attemptsAfter.Add(name.Value);
                        }

                        return null;
                    }

                    if (++requests != schedule.Trigger)
                    {
                        return null;
                    }

                    void TakeOver()
                    {
                        taking = true;
                        second = harness.OpenAsync(instance: "second").GetAwaiter().GetResult();
                        existedAtFence = [.. harness.Store.Names()];
                        taking = false;
                    }

                    if (!schedule.AfterApply)
                    {
                        TakeOver();
                        if (operation == ObjectOperation.Create)
                        {
                            attemptsAfter.Add(name.Value);
                        }

                        return null;
                    }

                    return TakeOver;
                };

                int acknowledged = 0;
                bool refused = false;

                for (int batch = 0; batch < schedule.Batches && !refused; batch++)
                {
                    try
                    {
                        CommitResult result = await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + batch.ToString(CultureInfo.InvariantCulture) + new string('x', 60)), T.Iri("p"), T.Iri("o")), T.Ct);

                        if (result.Outcome == CommitOutcome.Committed)
                        {
                            acknowledged++;
                        }
                        else
                        {
                            refused = true;
                        }
                    }
                    catch (WriterSupersededException)
                    {
                        refused = true;
                    }
                }

                harness.Store.Hook = null;

                if (second is null)
                {
                    // The trigger fell after the writer's last request.
                    await writer.DisposeAsync();
                    return;
                }

                Assert.False(second.IsSuperseded);
                string fence = "log/" + second.Writer!.History[^1].Fence!.Value.Value;

                // The fence is about log slots. A consolidation the old writer
                // ran before it learned of the takeover is checked below: a
                // segment it sealed is final, so the object must hold exactly
                // the bytes the new writer holds for it.
                foreach (string attempt in attemptsAfter.Where(a => a.Contains(".chunks/", StringComparison.Ordinal)))
                {
                    Assert.True(
                        attempt == fence || existedAtFence!.Contains(attempt),
                        "after the takeover the superseded writer created " + attempt + ", which is neither the fence " + fence + " nor a slot that existed");
                }

                // Every acknowledged commit is in the log the new writer holds,
                // and nothing after the fence is the old writer's.
                await writer.DisposeAsync();
                await using Dataset current = await T.OpenOrCreate(second, T.Options(maxRecordBytes: 256, segmentBytes: schedule.SegmentBytes));
                Assert.Equal(acknowledged, current.Head.Value);
                CommitResult next = await current.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("after"), T.Iri("p"), T.Iri("o")), T.Ct);
                Assert.Equal(CommitOutcome.Committed, next.Outcome);

                foreach (string consolidated in attemptsAfter.Where(a => a.EndsWith(".seg", StringComparison.Ordinal)).Distinct())
                {
                    int number = int.Parse(consolidated["log/".Length..^".seg".Length], System.Globalization.CultureInfo.InvariantCulture);
                    ReadOnlyMemory<byte> held = await second.Log.ReadRangeAsync(new SegmentId(number), new ByteOffset(0), new ByteCount(long.MaxValue), T.Ct);
                    Assert.True(held.Span.SequenceEqual(harness.Store.Get(consolidated)), consolidated + ", written by the superseded writer, differs from the segment the new writer holds");
                }
            },
            iter: Iterations,
            print: s => s.ToString());
    }
}
