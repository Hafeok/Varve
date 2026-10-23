// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The writer fence of ADR 0136 as cases any store runs, through the real
/// backend and nothing but the store's five operations: the claim, the
/// fence slot, the release on close, a second writer superseding a stalled
/// first, writers claiming at once, and the operator's takeover. In memory
/// they are a fast gate; against Azurite and a real account (#143) they
/// say what the service does.
/// </summary>
/// <remarks>
/// There is no lease and no expiry (ADR 0136): a writer that stalls is not
/// found dead by a timer, it is fenced by its successor's claim and refused
/// at its very next write, whenever that comes. These cases assert that
/// property, and never anything about elapsed time.
/// </remarks>
internal static class WriterFenceCases
{
    /// <summary>How many writers open at once in the concurrent claim.</summary>
    public const int Claimants = 6;

    public static (string Name, Func<IObjectStore, ObjectName?, TimeProvider, Task> Run)[] All() =>
    [
        ("a writer claims the first epoch, fences the first slot and releases it on close", ClaimAndRelease),
        ("a second writer supersedes a stalled first, whose next write is refused and lands nowhere", StalledWriterIsFenced),
        ("of writers claiming at once exactly one holds the epoch and the rest do not retry", ConcurrentClaims),
        ("an operator takeover names the epoch and fences the writer that held it", OperatorTakeover),
        ("a segment of fence chunks alone, left by writers that committed nothing, opens and takes a commit", FenceRunOpens),
    ];

    public static async Task ClaimAndRelease(IObjectStore store, ObjectName? prefix, TimeProvider clock)
    {
        using Caches caches = new();
        Backend storage = await Backend.OpenAsync(caches.Options(store, prefix, "writer-a", clock), T.Ct);

        Assert.Equal(new WriterEpoch(1), storage.Epoch);
        Assert.False(storage.IsSuperseded);
        WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, prefix, T.Ct));
        Assert.Equal(new WriterInstance("writer-a"), record.Holder);
        Assert.Null(record.Released);
        EpochClaim claim = Assert.Single(record.History);
        Assert.Equal(ClaimTrigger.Restart, claim.Trigger);
        LogSlot slot = Assert.IsType<LogSlot>(claim.Fence);
        Assert.Equal(new LogSlot("00000000.chunks/00000000"), slot);

        // The fence is a chunk in the store, 136 bytes, under the log.
        ObjectRead fence = Assert.IsType<ObjectRead>(await store.ReadAsync(new ObjectName((prefix?.Value ?? string.Empty) + "log/" + slot.Value), new ByteOffset(0), new ByteCount(1024), T.Ct));
        Assert.Equal(136, fence.Length.Value);

        await storage.DisposeAsync();

        record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, prefix, T.Ct));
        Assert.Equal(ReleaseReason.Shutdown, record.Released);
        Assert.Equal(ReleaseReason.Shutdown, Assert.Single(record.History).Released);
    }

    /// <summary>
    /// The first writer stalls after two commits; its renewals, had it any,
    /// would have stopped. A second writer opens, claims epoch 2 and fences
    /// the next slot. The first's next commit is refused, it is superseded,
    /// its reads go on, and a third writer sees the log the second left: the
    /// refused commit landed nowhere. What prevents the split-brain append is
    /// the fence slot alone (ADR 0136, decision 3).
    /// </summary>
    public static async Task StalledWriterIsFenced(IObjectStore store, ObjectName? prefix, TimeProvider clock)
    {
        using Caches caches = new();
        Backend first = await Backend.OpenAsync(caches.Options(store, prefix, "first", clock), T.Ct);
        Dataset stalled = await T.Open(first);
        await stalled.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("one")), T.Ct);
        await stalled.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("two")), T.Ct);

        Backend second = await Backend.OpenAsync(caches.Options(store, prefix, "second", clock), T.Ct);
        Assert.Equal(new WriterEpoch(2), second.Epoch);
        Assert.False(second.IsSuperseded);

        await Assert.ThrowsAsync<WriterSupersededException>(async () => await stalled.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("lost")), T.Ct));
        Assert.True(first.IsSuperseded);
        Assert.Null(first.Epoch);

        using (DatasetView view = stalled.Pin())
        {
            Assert.Equal(2, T.All(view).Count);
        }

        await using (Dataset current = await T.Open(second))
        {
            Assert.Equal(new Position(2), current.Head);
            await current.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("three")), T.Ct);
        }

        await stalled.DisposeAsync();
        await first.DisposeAsync();

        // The superseded writer released nothing: the record is the second's.
        WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, prefix, T.Ct));
        Assert.Equal(new WriterEpoch(2), record.Epoch);
        Assert.Null(record.Released);
        Assert.Equal([ClaimTrigger.Restart, ClaimTrigger.Restart], record.History.Select(c => c.Trigger));

        await second.DisposeAsync();

        // A third writer reads what the second wrote, and nothing of the refused commit.
        Backend third = await Backend.OpenAsync(caches.Options(store, prefix, "third", clock), T.Ct);
        Assert.Equal(new WriterEpoch(3), third.Epoch);
        await using Dataset resumed = await T.Open(third);
        Assert.Equal(new Position(3), resumed.Head);
        using DatasetView all = resumed.Pin();
        SortedSet<string> terms = T.Terms(all);
        Assert.Contains(terms, t => t.Contains("\"three\"", StringComparison.Ordinal));
        Assert.DoesNotContain(terms, t => t.Contains("\"lost\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="Claimants"/> writers open at once, twice: on an empty
    /// prefix, racing the record's creation, and on a dataset a writer left,
    /// racing its replacement. Claims that read one version of the record
    /// race it and exactly one wins; a claim that reads the winner's record
    /// supersedes the winner in its turn (ADR 0136, decision 2: an open for
    /// write claims the next epoch). So the record is a linearisation of
    /// the claims that succeeded, each epoch once, and the holder is the
    /// last; every other claimant either lost its claim and knows it, or
    /// holds a stale epoch and is refused at its first write by the fence
    /// slot the holder created. No two of them append.
    /// </summary>
    public static async Task ConcurrentClaims(IObjectStore store, ObjectName? prefix, TimeProvider clock)
    {
        using Caches caches = new();

        // The record's creation: who holds it, and the history is the claims that succeeded.
        Backend[] opened = await OpenAtOnceAsync(store, prefix, clock, caches, "create");

        try
        {
            Backend[] claimed = [.. opened.Where(storage => storage.Epoch is not null)];
            Assert.NotEmpty(claimed);
            Assert.All(opened.Except(claimed), storage => Assert.True(storage.IsSuperseded));
            WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, prefix, T.Ct));
            Assert.Equal(claimed.Select(storage => storage.Epoch!.Value.Value).Order(), record.History.Select(claim => claim.Epoch.Value));
            Assert.Equal(claimed.Max(storage => storage.Epoch!.Value), record.Epoch);
            Assert.Null(record.Released);
        }
        finally
        {
            foreach (Backend storage in opened)
            {
                await storage.DisposeAsync();
            }
        }

        // A dataset left by its writer: of the claimants, exactly one commits, and the rest are fenced.
        Backend creator = await Backend.OpenAsync(caches.Options(store, prefix, "creator", clock), T.Ct);
        await using (Dataset created = await T.Open(creator))
        {
            await created.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("before")), T.Ct);
        }

        await creator.DisposeAsync();
        opened = await OpenAtOnceAsync(store, prefix, clock, caches, "reopen");
        List<Dataset> datasets = [];

        try
        {
            // Who lost the claim outright, counted before a fenced writer is superseded too.
            int lost = opened.Count(storage => storage.Epoch is null);
            int committed = 0;
            int fenced = 0;
            WriterInstance? writer = null;

            foreach (Backend storage in opened.Where(storage => storage.Epoch is not null))
            {
                Dataset dataset = await T.Open(storage);
                datasets.Add(dataset);

                try
                {
                    CommitResult result = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("after")), T.Ct);
                    Assert.Equal(CommitOutcome.Committed, result.Outcome);
                    committed++;
                    writer = storage.Writer!.Holder;
                }
                catch (WriterSupersededException)
                {
                    fenced++;
                    Assert.True(storage.IsSuperseded);
                }
            }

            Assert.Equal(1, committed);
            Assert.Equal(Claimants, committed + fenced + lost);
            WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, prefix, T.Ct));
            Assert.Equal(writer, record.Holder);
            TestContext.Current.TestOutputHelper?.WriteLine(Claimants + " claimants: " + (committed + fenced) + " claimed an epoch, 1 committed, " + fenced + " fenced at their first write, " + lost + " lost the claim");
        }
        finally
        {
            foreach (Dataset dataset in datasets)
            {
                await dataset.DisposeAsync();
            }

            foreach (Backend storage in opened)
            {
                await storage.DisposeAsync();
            }
        }

        // What the one writer committed is the log, and nothing else landed.
        Backend after = await Backend.OpenAsync(caches.Options(store, prefix, "after", clock), T.Ct);
        await using Dataset resumed = await T.Open(after);
        Assert.Equal(new Position(2), resumed.Head);
        await after.DisposeAsync();
    }

    private static Task<Backend[]> OpenAtOnceAsync(IObjectStore store, ObjectName? prefix, TimeProvider clock, Caches caches, string round) =>
        Task.WhenAll(Enumerable.Range(0, Claimants).Select(i =>
            Task.Run(async () => await Backend.OpenAsync(caches.Options(store, prefix, round + "-" + i.ToString(CultureInfo.InvariantCulture), clock), T.Ct), T.Ct)));

    /// <summary>
    /// Three writers open in turn and commit nothing: segment 0 is then three
    /// fence chunks and no bytes. The fourth opens the dataset over it, reads
    /// the fences below its own, commits, and a fifth reads the commit (#144).
    /// </summary>
    public static async Task FenceRunOpens(IObjectStore store, ObjectName? prefix, TimeProvider clock)
    {
        using Caches caches = new();

        foreach (string instance in new[] { "a", "b", "c" })
        {
            Backend fencer = await Backend.OpenAsync(caches.Options(store, prefix, instance, clock), T.Ct);
            Assert.NotNull(fencer.Epoch);
            await fencer.DisposeAsync();
        }

        Backend fourth = await Backend.OpenAsync(caches.Options(store, prefix, "d", clock), T.Ct);
        Assert.Equal(new WriterEpoch(4), fourth.Epoch);
        await using (Dataset dataset = await T.Open(fourth))
        {
            Assert.Equal(new Position(0), dataset.Head);
            CommitResult result = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")), T.Ct);
            Assert.Equal(CommitOutcome.Committed, result.Outcome);
        }

        await fourth.DisposeAsync();

        Backend fifth = await Backend.OpenAsync(caches.Options(store, prefix, "e", clock), T.Ct);
        await using Dataset reopened = await T.Open(fifth);
        Assert.Equal(new Position(1), reopened.Head);
        await fifth.DisposeAsync();
    }

    public static async Task OperatorTakeover(IObjectStore store, ObjectName? prefix, TimeProvider clock)
    {
        using Caches caches = new();
        Backend stuck = await Backend.OpenAsync(caches.Options(store, prefix, "stuck", clock), T.Ct);
        await using Dataset dataset = await T.Open(stuck);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")), T.Ct);

        Assert.Null(await Backend.TakeOverAsync(caches.Options(store, prefix, "operator", clock), new WriterEpoch(7), T.Ct));
        Assert.Equal(new WriterEpoch(1), (await Backend.ReadWriterAsync(store, prefix, T.Ct))!.Epoch);

        WriterRecord taken = Assert.IsType<WriterRecord>(await Backend.TakeOverAsync(caches.Options(store, prefix, "operator", clock), new WriterEpoch(1), T.Ct));
        Assert.Equal(new WriterEpoch(2), taken.Epoch);
        Assert.Equal(ReleaseReason.OperatorTakeover, taken.Released);
        Assert.Equal(ClaimTrigger.Operator, taken.History[^1].Trigger);

        await Assert.ThrowsAsync<WriterSupersededException>(async () => await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("after")), T.Ct));

        Backend next = await Backend.OpenAsync(caches.Options(store, prefix, "next", clock), T.Ct);
        Assert.Equal(new WriterEpoch(3), next.Epoch);
        await using Dataset resumed = await T.Open(next);
        Assert.Equal(new Position(1), resumed.Head);
        await next.DisposeAsync();
        await stuck.DisposeAsync();
    }

    /// <summary>The cache directories a case's opens use, deleted with it.</summary>
    private sealed class Caches : IDisposable
    {
        private readonly List<string> _directories = [];

        public ObjectStorageOptions Options(IObjectStore store, ObjectName? prefix, string instance, TimeProvider clock)
        {
            string cache = Directory.CreateTempSubdirectory("varve-fence-").FullName;

            lock (_directories)
            {
                _directories.Add(cache);
            }

            return new ObjectStorageOptions
            {
                Store = store,
                Prefix = prefix,
                Cache = new CacheDirectory(cache),
                Instance = new WriterInstance(instance),
                Clock = clock,
                ProbeRandomness = TestRandomness.Instance,
            };
        }

        public void Dispose()
        {
            foreach (string cache in _directories)
            {
                try
                {
                    Directory.Delete(cache, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}

/// <summary>The fence cases over the in-memory store: the gate every build runs.</summary>
public sealed class MemoryWriterFenceTests
{
    public static TheoryData<string> Cases() => [.. WriterFenceCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public Task the_in_memory_store_fences_a_superseded_writer(string name) =>
        WriterFenceCases.All().Single(c => c.Name == name).Run(new MemoryObjectStore(), null, new InstantClock(ManualClock.Epoch().Now));
}
