// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--idempotency-index &lt;S|M&gt;</c> (ADR 0246, #132): what the
/// writer's index of idempotency keys costs to read back from the log before
/// its first keyed write, over the full default window of 100,000 commits.
/// The REC portfolio's history is loaded into an object store held in memory,
/// then keyed commits, each carrying the attachment a keyed write carries,
/// fill the window twice over: first so that it spans every REC commit, then
/// so that it holds keyed commits only. At each, two measurements:
/// <list type="bullet">
/// <item>after a restart: the dataset opened anew by another instance, with a
/// cold cache;</item>
/// <item>after a takeover: a follower that has followed the log claims the
/// epoch in place (ADR 0179).</item>
/// </list>
/// Each reports the time of <see cref="IdempotencyKeys.PrepareAsync"/>, the
/// bytes it allocated, and the heap it leaves held, after a full collection.
/// </summary>
internal static class IdempotencyIndexBench
{
    private const int Concurrency = 16;

    internal static async Task RunAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        long window = CommitWindow.Default.Commits;
        MemoryObjectStore store = new();
        List<string> caches = [];
        Stopwatch clock = Stopwatch.StartNew();

        Backend written = await OpenAsync(store, caches, "a", ObjectStorageAccess.Write);
        StoreDatasetType writer = await StoreDatasetType.CreateAsync(written, new DatasetId(new Guid("6f8c0000-0000-4000-8000-000000000132")), Options());
        long rec = await LoadRecAsync(writer, size);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"// REC {size} (generator output version {RecPortfolio.Version}): {rec:N0} commits in {clock.Elapsed.TotalSeconds:F1} s"));

        // The window spans every REC commit, and the keyed commits after them.
        int key = 0;
        clock.Restart();
        key = await KeyedAsync(writer, key, (int)(window - writer.Head.Value));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"// {key:N0} keyed commits in {clock.Elapsed.TotalSeconds:F1} s; head {writer.Head.Value:N0}"));
        (writer, written) = await MeasureAsync("REC " + size + " and keyed commits, the window spanning all of REC", store, caches, writer, written, window);

        // The window holds keyed commits only.
        clock.Restart();
        int before = key;
        key = await KeyedAsync(writer, key, (int)rec);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"// {key - before:N0} keyed commits more in {clock.Elapsed.TotalSeconds:F1} s; head {writer.Head.Value:N0}"));
        (writer, written) = await MeasureAsync("REC " + size + " and keyed commits, the window all keyed", store, caches, writer, written, window);

        await writer.DisposeAsync();
        await written.DisposeAsync();

        foreach (string cache in caches)
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

    // A restart, then a takeover, each measured; the writer the takeover
    // made is the one returned.
    private static async Task<(StoreDatasetType Writer, Backend Storage)> MeasureAsync(string label, MemoryObjectStore store, List<string> caches, StoreDatasetType writer, Backend written, long window)
    {
        long head = writer.Head.Value;

        // A follower, opened now and advanced to the head, for the takeover.
        Backend followed = await OpenAsync(store, caches, "b", ObjectStorageAccess.ReadOnly);
        StoreDatasetType follower = await StoreDatasetType.OpenFollowerAsync(followed, Options());
        await follower.AdvanceAsync();

        // The restart: the writer closes; another instance opens it anew.
        await writer.DisposeAsync();
        await written.DisposeAsync();
        Backend reopened = await OpenAsync(store, caches, "c", ObjectStorageAccess.Write);
        StoreDatasetType restarted = await StoreDatasetType.OpenAsync(reopened, Options());
        Report(label + ", after a restart", await PrepareAsync(restarted), head, window);
        await restarted.DisposeAsync();
        await reopened.DisposeAsync();

        // The takeover: the follower claims the epoch in place.
        WriterEpoch from = (await Backend.ReadWriterAsync(store, null))!.Epoch;

        if (!await follower.TakeWriteAsync(ct => followed.ClaimInPlaceAsync(new WriterInstance("b"), null, Randomness.Instance, ClaimTrigger.Probe, from, ct)))
        {
            throw new InvalidOperationException("The follower did not take the dataset over.");
        }

        Report(label + ", after a takeover", await PrepareAsync(follower), head, window);
        return (follower, followed);
    }

    private static async Task<(TimeSpan Time, long Allocated, long Held)> PrepareAsync(StoreDatasetType dataset)
    {
        // Timed on its own: a fresh index, with no collection inside.
        IdempotencyKeys timed = new();
        GC.GetTotalMemory(forceFullCollection: true);
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch clock = Stopwatch.StartNew();
        await timed.PrepareAsync(dataset);
        TimeSpan time = clock.Elapsed;
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        GC.KeepAlive(timed);

        // Then the heap one holds, measured on fresh indexes: the median of
        // five, each printed.
        long[] helds = new long[5];

        for (int i = 0; i < helds.Length; i++)
        {
            helds[i] = await HeldAsync(dataset);
        }

        Console.WriteLine("//   held, each: " + string.Join(", ", helds.Select(h => (h / 1048576.0).ToString("F1", CultureInfo.InvariantCulture))) + " MiB");
        Array.Sort(helds);
        return (time, allocated, helds[2]);
    }

    // The heap the index holds: the heap with it, less the heap just before
    // it was made. The timed run before this has already paged in what the
    // reads need of the dataset, so that is not counted.
    private static async Task<long> HeldAsync(StoreDatasetType dataset)
    {
        long before = Heap();
        IdempotencyKeys keys = new();
        await keys.PrepareAsync(dataset);
        long with = Heap();
        GC.KeepAlive(keys);
        return with - before;
    }

    // The heap after a blocking, compacting collection of every generation.
    private static long Heap()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        // Less the free space: the large-object heap keeps a dropped index's
        // arrays as fragmentation, which the next one reuses.
        GCMemoryInfo info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return info.HeapSizeBytes - info.FragmentedBytes;
    }

    private static void Report(string label, (TimeSpan Time, long Allocated, long Held) measured, long head, long window) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{label}: head {head:N0}, window {window:N0}: {measured.Time.TotalMilliseconds:F0} ms, {measured.Allocated / 1048576.0:F1} MiB allocated, {measured.Held / 1048576.0:F1} MiB held"));

    private static async Task<long> LoadRecAsync(StoreDatasetType dataset, RecSize size)
    {
        Dictionary<string, RdfTerm> terms = new(StringComparer.Ordinal);
        RdfTerm Of(string text)
        {
            if (!terms.TryGetValue(text, out RdfTerm? term))
            {
                term = text[0] == '<' ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1])) : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));
                terms[text] = term;
            }

            return term;
        }

        foreach (RecCommit commit in RecPortfolio.Generate(size, 20261015))
        {
            CommitRequestBuilder request = new();

            foreach (RecQuad quad in commit.Retracted)
            {
                request.Retract(Of(quad.Subject), Of(quad.Predicate), Of(quad.Object), Of(quad.Graph));
            }

            foreach (RecQuad quad in commit.Asserted)
            {
                request.Assert(Of(quad.Subject), Of(quad.Predicate), Of(quad.Object), Of(quad.Graph));
            }

            await dataset.CommitAsync(request);
        }

        return dataset.Head.Value;
    }

    // Keyed commits, each a small write with the attachment a keyed write
    // carries (ADR 0246): the hash of an agent and a key, a fingerprint,
    // the status. Sent Concurrency at a time, as clients would.
    private static async Task<int> KeyedAsync(StoreDatasetType dataset, int from, int count)
    {
        RdfTerm agent = RdfTerm.Iri("https://issuer.example/#bench"u8);
        RdfTerm datatype = RdfTerm.Iri("https://w3id.org/varve/ns#idempotencyKey"u8);
        RdfTerm predicate = RdfTerm.Iri("https://example.org/bench/p"u8);
        RdfTerm graph = RdfTerm.Iri("https://example.org/bench/g"u8);
        int next = from;

        async Task WorkerAsync()
        {
            while (true)
            {
                int n = System.Threading.Interlocked.Increment(ref next);

                if (n > from + count)
                {
                    return;
                }

                string text = n.ToString(CultureInfo.InvariantCulture);
                string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("https://issuer.example/#bench\0key-" + text)));
                string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("body-" + text)));
                RdfTerm attachment = RdfTerm.Literal(Encoding.UTF8.GetBytes("{\"fingerprint\":\"" + fingerprint + "\",\"keyHash\":\"" + hash + "\",\"status\":204}"), datatype);
                CommitRequestBuilder request = new()
                {
                    Metadata = new CommitMetadata
                    {
                        Agent = RequestTerm.FromTerm(agent),
                        Cause = RequestTerm.FromTerm(RdfTerm.Literal(Encoding.UTF8.GetBytes("bench:" + text))),
                        Attachments = [RequestTerm.FromTerm(attachment)],
                    },
                };
                request.Assert(RdfTerm.Iri(Encoding.UTF8.GetBytes("https://example.org/bench/s" + text)), predicate, RdfTerm.Literal(Encoding.UTF8.GetBytes(text)), graph);
                CommitResult result = await dataset.CommitAsync(request);

                if (result.Outcome != CommitOutcome.Committed)
                {
                    throw new InvalidOperationException("A keyed commit did not commit: " + result.Outcome);
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => Task.Run(WorkerAsync)));
        return from + count;
    }

    private static DatasetOptions Options() => new() { Clock = TimeProvider.System };

    private static async Task<Backend> OpenAsync(MemoryObjectStore store, List<string> caches, string instance, ObjectStorageAccess access)
    {
        string cache = Directory.CreateTempSubdirectory("varve-keys-").FullName;
        caches.Add(cache);
        return await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Cache = new CacheDirectory(cache),
            CacheBytes = new ByteCount(8L << 30),
            Instance = new WriterInstance(instance),
            Access = access,
            Clock = TimeProvider.System,
            ProbeRandomness = Randomness.Instance,
        });
    }

    /// <summary>A writer's probe key's random part (ADR 0134).</summary>
    private sealed class Randomness : IProbeRandomness
    {
        internal static Randomness Instance { get; } = new();

        public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
    }
}
