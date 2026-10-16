// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The object-storage backend measured at the REC sizes (ADRs 0135, 0139,
/// 0141): a history loaded commit by commit — each commit's latency and the
/// requests it costs — a checkpoint written as extents, a cold start from an
/// empty cache, and eight writers at once; and, on the file backend, head and
/// as-of reads after 10, 50 and 100 checkpoints. The store is Azurite or an
/// S3-compatible server on loopback (VARVE_AZURITE, VARVE_RUSTFS, as the
/// tests read them), or a directory.
/// </summary>
internal static class ObjectStorageBench
{
    private const ulong Seed = 20261015;

    /// <summary><c>--objects &lt;file|azurite|rustfs&gt; &lt;S|M|L&gt;</c>.</summary>
    internal static async Task RunAsync(string target, string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        Target store = await Target.CreateAsync(target);
        Console.WriteLine("## " + target + ", REC " + size);
        Console.WriteLine();

        // The load: every commit of the history, one at a time.
        IStorage storage = await store.OpenAsync();
        StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System });
        Terms terms = new();
        List<double> latencies = [];
        Counts before = store.Counter?.Snapshot() ?? default;
        Stopwatch load = Stopwatch.StartNew();
        RecQuad probe = default;

        foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
        {
            CommitRequest request = new();

            foreach (RecQuad quad in commit.Asserted)
            {
                request.Assert(terms.Of(quad.Subject), terms.Of(quad.Predicate), terms.Of(quad.Object), terms.Of(quad.Graph));
                probe = probe == default ? quad : probe;
            }

            foreach (RecQuad quad in commit.Retracted)
            {
                request.Retract(terms.Of(quad.Subject), terms.Of(quad.Predicate), terms.Of(quad.Object), terms.Of(quad.Graph));
            }

            long started = Stopwatch.GetTimestamp();
            await dataset.CommitAsync(request);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        load.Stop();
        Counts loaded = (store.Counter?.Snapshot() ?? default) - before;
        long head = dataset.Head.Value;
        long quads;

        using (DatasetView view = dataset.Pin())
        {
            quads = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
        }

        latencies.Sort();
        Row("commits", head.ToString("N0", CultureInfo.InvariantCulture));
        Row("quads at the head", quads.ToString("N0", CultureInfo.InvariantCulture));
        Row("load", Seconds(load.Elapsed) + ", " + (head / load.Elapsed.TotalSeconds).ToString("N0", CultureInfo.InvariantCulture) + " commits/s");
        Row("commit latency p50 / p99 / max", Ms(Percentile(latencies, 0.50)) + " / " + Ms(Percentile(latencies, 0.99)) + " / " + Ms(latencies[^1]));

        if (store.Counter is not null)
        {
            Row("requests during the load", loaded.ToString());
            Row("requests per commit", (loaded.Total / (double)head).ToString("F2", CultureInfo.InvariantCulture) + " (creates " + (loaded.Creates / (double)head).ToString("F2", CultureInfo.InvariantCulture) + ")");
        }

        if (store.Counter is not null)
        {
            Row("the log after the load", await store.Counter.CensusAsync(new ObjectName("dataset/log/")));
        }

        // The checkpoint: one extent set for the history but its last 4%,
        // which stay the log's tail, as after a checkpoint policy's last one.
        await dataset.MaintainAsync();
        long tail = head / 25;
        Counts beforeCheckpoint = store.Counter?.Snapshot() ?? default;
        Stopwatch checkpoint = Stopwatch.StartNew();
        await dataset.CheckpointAsync(new Position(head - tail));
        checkpoint.Stop();
        Row("checkpoint " + tail.ToString("N0", CultureInfo.InvariantCulture) + " commits behind the head", Seconds(checkpoint.Elapsed) + (store.Counter is null ? string.Empty : ", " + ((store.Counter.Snapshot()) - beforeCheckpoint)));
        await dataset.DisposeAsync();
        await Target.CloseAsync(storage);

        // The cold start: a new instance, an empty cache, the catalog and the log.
        Counts beforeOpen = store.Counter?.Snapshot() ?? default;
        store.Counter?.ResetKinds();
        Stopwatch cold = Stopwatch.StartNew();
        IStorage reopened = await store.OpenAsync(fresh: true);
        TimeSpan backendTime = cold.Elapsed;
        StoreDatasetType opened = await StoreDatasetType.OpenAsync(reopened, new DatasetOptions { Clock = TimeProvider.System });
        TimeSpan openTime = cold.Elapsed;
        bool found;

        using (DatasetView view = opened.Pin())
        {
            found = Holds(view, terms, probe);
        }

        cold.Stop();
        Counts coldCounts = (store.Counter?.Snapshot() ?? default) - beforeOpen;
        Row("cold start: the backend's open", Seconds(backendTime));
        Row("cold start: open", Seconds(openTime));
        Row("cold start: open and a first read", Seconds(cold.Elapsed) + (found ? string.Empty : " (the probe was not found)"));

        if (store.Counter is not null)
        {
            Row("cold start requests", coldCounts.ToString());
            Row("cold start reads by kind", store.Counter.Kinds());
            Row("cold start time by operation", store.Counter.Times());
        }

        // The first read below the catalog: the log's headers below it, once.
        Counts beforeBelow = store.Counter?.Snapshot() ?? default;
        Stopwatch below = Stopwatch.StartNew();
        using (DatasetView view = await opened.AsOfAsync(new Position(1)))
        {
            _ = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        }

        below.Stop();
        Row("first as-of read below the catalog, at position 1", Seconds(below.Elapsed) + (store.Counter is null ? string.Empty : ", " + ((store.Counter.Snapshot()) - beforeBelow)));

        await opened.DisposeAsync();
        await Target.CloseAsync(reopened);

        // Eight writers at once on a fresh dataset: group commit's batches.
        IStorage concurrent = await store.OpenAsync(fresh: true, prefix: "concurrent/");
        StoreDatasetType eight = await StoreDatasetType.CreateAsync(concurrent, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System });
        Counts beforeEight = store.Counter?.Snapshot() ?? default;
        Stopwatch writers = Stopwatch.StartNew();
        const int PerWriter = 250;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(w => Task.Run(async () =>
        {
            for (int c = 0; c < PerWriter; c++)
            {
                CommitRequest request = new();

                for (int q = 0; q < 10; q++)
                {
                    request.Assert(Iri("w" + w + "/c" + c), Iri("p" + q), Iri("o" + q));
                }

                await eight.CommitAsync(request);
            }
        })));

        writers.Stop();
        Counts eightCounts = (store.Counter?.Snapshot() ?? default) - beforeEight;
        Row("8 writers, 2,000 commits of 10 quads", (8 * PerWriter / writers.Elapsed.TotalSeconds).ToString("N0", CultureInfo.InvariantCulture) + " commits/s"
            + (store.Counter is null ? string.Empty : ", " + (eightCounts.Creates / (8.0 * PerWriter)).ToString("F2", CultureInfo.InvariantCulture) + " creates per commit"));
        await eight.DisposeAsync();
        await Target.CloseAsync(concurrent);
        Console.WriteLine();
    }

    /// <summary>
    /// <c>--extent-reads &lt;S|M&gt;</c>: the history on files with a checkpoint
    /// every hundredth of it, then head and as-of reads with 100, 50 and 10
    /// sets in the chain — fewer by dropping, which merges (ADR 0138) — each
    /// after opening again, so the chain is the projection's base.
    /// </summary>
    internal static async Task ExtentReadsAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        string directory = Directory.CreateTempSubdirectory("varve-extent-reads-").FullName;
        DatasetOptions options = new() { Clock = TimeProvider.System };
        int commits = RecPortfolio.Shape(size).Commits;
        int every = commits / 100;
        Terms terms = new();
        List<RecQuad> probes = [];
        RecQuad subject = default;

        try
        {
            Stopwatch load = Stopwatch.StartNew();

            await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }))
            await using (StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), options))
            {
                int n = 0;

                foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
                {
                    CommitRequest request = new();

                    foreach (RecQuad quad in commit.Asserted)
                    {
                        request.Assert(terms.Of(quad.Subject), terms.Of(quad.Predicate), terms.Of(quad.Object), terms.Of(quad.Graph));

                        if (probes.Count < 1000 && (n % 7) == 0)
                        {
                            probes.Add(quad);
                        }

                        subject = subject == default && quad.Predicate.Contains("hasPart", StringComparison.Ordinal) ? quad : subject;
                    }

                    foreach (RecQuad quad in commit.Retracted)
                    {
                        request.Retract(terms.Of(quad.Subject), terms.Of(quad.Predicate), terms.Of(quad.Object), terms.Of(quad.Graph));
                    }

                    await dataset.CommitAsync(request);

                    if (++n % every == 0)
                    {
                        await dataset.CheckpointAsync(dataset.Head);
                    }
                }

                await dataset.MaintainAsync();
            }

            load.Stop();
            Console.WriteLine("## Reads after checkpoints, REC " + size + " on files (load and 100 checkpoints: " + Seconds(load.Elapsed) + ")");
            Console.WriteLine();
            Console.WriteLine("| Checkpoints | Head: lookup p50 | Head: a subject's quads p50 | Head: exact count p50 | As-of inside a set | As-of at a set's end |");
            Console.WriteLine("|---:|---:|---:|---:|---:|---:|");

            foreach (int keep in new[] { 100, 50, 10 })
            {
                await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
                await using StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, options);

                // Fewer sets by dropping, which merges each into the next.
                List<Position> checkpoints = [.. dataset.Checkpoints];
                int stride = checkpoints.Count / keep;

                for (int i = 0; i < checkpoints.Count; i++)
                {
                    if ((i + 1) % stride != 0)
                    {
                        await dataset.DropCheckpointAsync(checkpoints[i]);
                    }
                }

                await dataset.DisposeAsync();
                await using StoreDatasetType reopened = await StoreDatasetType.OpenAsync(storage, options);
                await Measure(reopened, keep, terms, probes, subject);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// <c>--format3-open &lt;dir&gt;</c>: a dataset the previous release wrote
    /// (derived format 3), on a copy — opened as it is, with its full
    /// checkpoints (ADR 0139); its first checkpoint under this release, which
    /// starts the chain of extent sets; and, for reference, an open with
    /// <c>derived/</c> removed, which is the rebuild from the log.
    /// </summary>
    internal static async Task Format3OpenAsync(string written)
    {
        string copy = Directory.CreateTempSubdirectory("varve-format3-").FullName;
        Copy(written, copy);
        DatasetOptions options = new() { Clock = TimeProvider.System };
        Console.WriteLine("## A derived format 3 dataset, opened by this build");
        Console.WriteLine();

        try
        {
            Stopwatch open = Stopwatch.StartNew();

            await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(copy), new FileStorageOptions { Clock = TimeProvider.System }))
            await using (StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, options))
            {
                long quads;

                using (DatasetView view = dataset.Pin())
                {
                    quads = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
                }

                open.Stop();
                Row("head", dataset.Head.Value.ToString("N0", CultureInfo.InvariantCulture) + " commits, " + quads.ToString("N0", CultureInfo.InvariantCulture) + " quads, full checkpoints " + string.Join(", ", dataset.Checkpoints.Select(p => p.Value.ToString(CultureInfo.InvariantCulture))));
                Row("open as it is, and a first read", Seconds(open.Elapsed));

                Stopwatch first = Stopwatch.StartNew();
                await dataset.CheckpointAsync(dataset.Head);
                first.Stop();
                Row("the first checkpoint under this release: an extent set over the whole history", Seconds(first.Elapsed));
            }

            Directory.Delete(Path.Combine(copy, "derived"), recursive: true);
            Stopwatch rebuild = Stopwatch.StartNew();

            await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(copy), new FileStorageOptions { Clock = TimeProvider.System }))
            await using (StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, options))
            {
                rebuild.Stop();
                Row("for reference, a rebuild from the log (derived/ removed)", Seconds(rebuild.Elapsed));
            }
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }

        Console.WriteLine();
    }

    private static void Copy(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);

            if (relative.EndsWith("LOCK", StringComparison.Ordinal) || relative.EndsWith("LOCK.owner", StringComparison.Ordinal))
            {
                continue;
            }

            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task Measure(StoreDatasetType dataset, int sets, Terms terms, List<RecQuad> probes, RecQuad subject)
    {
        List<double> lookups = [];
        List<double> scans = [];
        List<double> counts = [];

        using (DatasetView view = dataset.Pin())
        {
            view.TryInternalise(terms.Of(subject.Subject), out TermHandle s);

            for (int round = 0; round < 3; round++)
            {
                foreach (RecQuad probe in probes)
                {
                    long started = Stopwatch.GetTimestamp();
                    _ = Holds(view, terms, probe);
                    lookups.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                }

                for (int i = 0; i < 100; i++)
                {
                    long started = Stopwatch.GetTimestamp();
                    using IQuadCursor cursor = view.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any);

                    while (cursor.MoveNext())
                    {
                    }

                    scans.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                    started = Stopwatch.GetTimestamp();
                    _ = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
                    counts.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                }
            }
        }

        IReadOnlyList<Position> checkpoints = dataset.Checkpoints;
        Position end = checkpoints[checkpoints.Count / 2];
        Position inside = new(end.Value - 1);
        double insideMs = await AsOfAsync(dataset, inside);
        double endMs = await AsOfAsync(dataset, end);
        lookups.Sort();
        scans.Sort();
        counts.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {sets} | {Percentile(lookups, 0.5):F1} µs | {Percentile(scans, 0.5):F1} µs | {Percentile(counts, 0.5):F1} µs | {insideMs:F1} ms | {endMs:F1} ms |"));
    }

    private static async Task<double> AsOfAsync(StoreDatasetType dataset, Position position)
    {
        double[] times = new double[5];

        for (int r = 0; r < times.Length; r++)
        {
            long started = Stopwatch.GetTimestamp();
            using DatasetView view = await dataset.AsOfAsync(position);
            _ = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
            times[r] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(times);
        return times[times.Length / 2];
    }

    // A generated quad in the view: its terms looked up, then the quad.
    private static bool Holds(DatasetView view, Terms terms, RecQuad quad) =>
        view.TryInternalise(terms.Of(quad.Subject), out TermHandle s)
        && view.TryInternalise(terms.Of(quad.Predicate), out TermHandle p)
        && view.TryInternalise(terms.Of(quad.Object), out TermHandle o)
        && view.TryInternalise(terms.Of(quad.Graph), out TermHandle g)
        && view.Contains(new Quad(s, p, o, g));

    private static double Percentile(List<double> sorted, double at) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * at))];

    private static string Ms(double ms) => ms.ToString("F2", CultureInfo.InvariantCulture) + " ms";

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";

    private static void Row(string what, string value) => Console.WriteLine("- " + what + ": " + value);

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    /// <summary>The generator's N-Triples terms as store terms, each built once.</summary>
    private sealed class Terms
    {
        private readonly Dictionary<string, RdfTerm> _terms = new(StringComparer.Ordinal);

        internal RdfTerm Of(string text)
        {
            if (!_terms.TryGetValue(text, out RdfTerm? term))
            {
                term = text[0] == '<'
                    ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1]))
                    : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));
                _terms[text] = term;
            }

            return term;
        }
    }

    /// <summary>Requests by operation, and bytes each way.</summary>
    internal readonly record struct Counts(long Creates, long Replaces, long Reads, long Lists, long Deletes, long BytesUp, long BytesDown)
    {
        internal long Total => Creates + Replaces + Reads + Lists + Deletes;

        public static Counts operator -(Counts a, Counts b) => new(a.Creates - b.Creates, a.Replaces - b.Replaces, a.Reads - b.Reads, a.Lists - b.Lists, a.Deletes - b.Deletes, a.BytesUp - b.BytesUp, a.BytesDown - b.BytesDown);

        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"{Total:N0} requests ({Creates:N0} creates, {Replaces:N0} replaces, {Reads:N0} reads, {Lists:N0} lists, {Deletes:N0} deletes), {BytesUp / 1048576.0:F1} MiB up, {BytesDown / 1048576.0:F1} MiB down");
    }

    /// <summary>An object store that counts what passes through it.</summary>
    private sealed class CountingStore(IObjectStore inner) : IObjectStore, IDisposable
    {
        private long _creates;
        private long _replaces;
        private long _reads;
        private long _lists;
        private long _deletes;
        private long _up;
        private long _down;
        private readonly long[] _kindReads = new long[4];
        private readonly long[] _kindTicks = new long[4];

        private readonly long[] _operationTicks = new long[5];

        internal void ResetKinds()
        {
            for (int i = 0; i < 4; i++)
            {
                Interlocked.Exchange(ref _kindReads[i], 0);
                Interlocked.Exchange(ref _kindTicks[i], 0);
            }

            for (int i = 0; i < 5; i++)
            {
                Interlocked.Exchange(ref _operationTicks[i], 0);
            }
        }

        // Time in each operation since ResetKinds, summed over its requests.
        internal string Times() => string.Create(CultureInfo.InvariantCulture,
            $"creates {Ms(_operationTicks[0])}, replaces {Ms(_operationTicks[1])}, reads {Ms(_operationTicks[2])}, lists {Ms(_operationTicks[3])}, deletes {Ms(_operationTicks[4])}");

        private static string Ms(long ticks) => Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture) + " ms";

        // The objects under a prefix: segments consolidated, and chunks by segment.
        internal async Task<string> CensusAsync(ObjectName prefix)
        {
            IReadOnlyList<ObjectEntry> entries = await inner.ListAsync(prefix, CancellationToken.None);
            int consolidated = 0;
            SortedDictionary<string, int> chunks = new(StringComparer.Ordinal);

            foreach (ObjectEntry entry in entries)
            {
                string name = entry.Name.Value;

                if (name.EndsWith(".seg", StringComparison.Ordinal))
                {
                    consolidated++;
                }
                else if (name.IndexOf(".chunks/", StringComparison.Ordinal) is int at and > 0)
                {
                    string segment = name[(at - 8)..at];
                    chunks[segment] = chunks.GetValueOrDefault(segment) + 1;
                }
            }

            return string.Create(CultureInfo.InvariantCulture, $"{entries.Count:N0} objects, {consolidated} segments consolidated, chunks by segment: {string.Join(", ", chunks.Select(c => c.Key.TrimStart('0').PadLeft(1, '0') + " " + c.Value.ToString("N0", CultureInfo.InvariantCulture)))}");
        }

        // Reads since ResetKinds by what they read, with the time each kind's
        // requests took between them; parallel requests add up past the wall clock.
        internal string Kinds()
        {
            List<string> parts = [];

            for (int i = 0; i < 4; i++)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{KindName(i)} {Interlocked.Read(ref _kindReads[i]):N0} ({Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _kindTicks[i])).TotalMilliseconds:N0} ms)"));
            }

            return string.Join(", ", parts);
        }

        private static string KindName(int kind) => kind switch
        {
            0 => "log chunks",
            1 => "log segments",
            2 => "derived/",
            _ => "other",
        };

        private static int KindOf(ObjectName name) =>
            name.Value.Contains(".chunks/", StringComparison.Ordinal) ? 0
            : name.Value.EndsWith(".seg", StringComparison.Ordinal) ? 1
            : name.Value.Contains("derived/", StringComparison.Ordinal) ? 2
            : 3;

        internal Counts Snapshot() => new(Interlocked.Read(ref _creates), Interlocked.Read(ref _replaces), Interlocked.Read(ref _reads), Interlocked.Read(ref _lists), Interlocked.Read(ref _deletes), Interlocked.Read(ref _up), Interlocked.Read(ref _down));

        public async ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _creates);
            Interlocked.Add(ref _up, content.Length);
            long started = Stopwatch.GetTimestamp();
            ObjectVersion? version = await inner.CreateIfAbsentAsync(name, content, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _operationTicks[0], Stopwatch.GetTimestamp() - started);
            return version;
        }

        public async ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _replaces);
            Interlocked.Add(ref _up, content.Length);
            long started = Stopwatch.GetTimestamp();
            ObjectVersion? version = await inner.ReplaceIfUnchangedAsync(name, expected, content, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _operationTicks[1], Stopwatch.GetTimestamp() - started);
            return version;
        }

        public async ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            int kind = KindOf(name);
            long started = Stopwatch.GetTimestamp();
            ObjectRead? read = await inner.ReadAsync(name, offset, length, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _kindReads[kind]);
            Interlocked.Add(ref _kindTicks[kind], Stopwatch.GetTimestamp() - started);
            Interlocked.Add(ref _operationTicks[2], Stopwatch.GetTimestamp() - started);
            Interlocked.Add(ref _down, read?.Bytes.Length ?? 0);
            return read;
        }

        public async ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName prefix, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _lists);
            long started = Stopwatch.GetTimestamp();
            IReadOnlyList<ObjectEntry> entries = await inner.ListAsync(prefix, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _operationTicks[3], Stopwatch.GetTimestamp() - started);
            return entries;
        }

        public async ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _deletes);
            long started = Stopwatch.GetTimestamp();
            bool deleted = await inner.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _operationTicks[4], Stopwatch.GetTimestamp() - started);
            return deleted;
        }

        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

    /// <summary>Where a run's datasets live: a directory, or a fresh container or bucket.</summary>
    private sealed class Target
    {
        private readonly string _kind;
        private readonly string _root = Directory.CreateTempSubdirectory("varve-objects-bench-").FullName;
        private int _opens;

        private Target(string kind, CountingStore? counter)
        {
            _kind = kind;
            Counter = counter;
        }

        internal CountingStore? Counter { get; }

        internal static async Task<Target> CreateAsync(string kind)
        {
            string run = "b" + Guid.NewGuid().ToString("N")[..20];

            switch (kind)
            {
                case "file":
                    return new Target(kind, null);
                case "azurite":
                {
                    Uri container = new((Environment.GetEnvironmentVariable("VARVE_AZURITE") ?? "http://127.0.0.1:10000/devstoreaccount1").TrimEnd('/') + "/" + run);
                    using (HttpClient http = new())
                    using (DevelopmentKeySigner signer = new(TimeProvider.System))
                    using (HttpRequestMessage create = new(HttpMethod.Put, new Uri(container + "?restype=container")))
                    {
                        create.Headers.Add("x-ms-version", "2025-11-05");
                        await signer.SignAsync(create, CancellationToken.None);
                        (await http.SendAsync(create)).EnsureSuccessStatusCode();
                    }

                    return new Target(kind, new CountingStore(AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = container, Clock = TimeProvider.System })));
                }

                case "rustfs":
                {
                    string[] parts = (Environment.GetEnvironmentVariable("VARVE_RUSTFS") ?? "http://127.0.0.1:9000,varvetest,varvetestsecret").Split(',');
                    Uri endpoint = new(parts[0]);
                    S3Bucket bucket = new(run);
                    using (HttpClient http = new())
                    using (HttpRequestMessage create = new(HttpMethod.Put, new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/" + bucket.Value)))
                    {
                        SigV4.Sign(create, [], new SigningCredentials(parts[1], parts[2], null, DateTimeOffset.MaxValue), S3Region.Default.Value, DateTimeOffset.UtcNow);
                        (await http.SendAsync(create)).EnsureSuccessStatusCode();
                    }

                    return new Target(kind, new CountingStore(S3ObjectStore.Create(new S3ObjectStoreOptions
                    {
                        Endpoint = endpoint,
                        Bucket = bucket,
                        DevelopmentKey = new S3DevelopmentKey(parts[1], parts[2]),
                        Clock = TimeProvider.System,
                    })));
                }

                default:
                    throw new ArgumentException("A target is file, azurite or rustfs.", nameof(kind));
            }
        }

        /// <summary>The dataset's storage, opened for write; a fresh cache directory when asked, as a new instance has.</summary>
        internal async Task<IStorage> OpenAsync(bool fresh = false, string prefix = "dataset/")
        {
            if (Counter is null)
            {
                string directory = Path.Combine(_root, prefix.TrimEnd('/'));
                return await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
            }

            string cache = Path.Combine(_root, "cache" + (fresh ? (++_opens).ToString(CultureInfo.InvariantCulture) : "0"));
            return await Backend.OpenAsync(new ObjectStorageOptions
            {
                Store = Counter,
                Prefix = new ObjectName(prefix),
                Cache = new CacheDirectory(cache),
                Instance = new WriterInstance("bench-" + _kind),
                Clock = TimeProvider.System,
            });
        }

        internal static async Task CloseAsync(IStorage storage)
        {
            if (storage is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }
}
