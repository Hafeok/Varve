// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--asof-seek &lt;file|azurite&gt; &lt;S|M&gt; [max-set-MiB]</c> (ADR 0177):
/// the REC history with a checkpoint every hundredth and maintenance after
/// each, as <c>--extent-maintenance</c>'s merged row, then, opened again:
/// head reads, as-of reads inside the largest set — a quad's lookup and a
/// subject's quads, p50 and p99, and the pages each touches — and scans of
/// subjects inside the largest set and inside one at most a quarter its size,
/// in time and pages per match. On files, the pages are the 1 MiB pages of
/// extents a read touches; on an object store, also the ranged reads a read
/// makes with a cache too small to keep a page, which is what each miss costs.
/// </summary>
internal static partial class ObjectStorageBench
{
    private const long Page = 1L << 20;

    internal static async Task AsOfSeekAsync(string kind, string sizeName, long? maxSetBytes)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        ExtentMergePolicy policy = new() { FanOut = 4, MaxSetBytes = new ByteCount(maxSetBytes ?? ExtentMergePolicy.DefaultMaxSetBytes.Value) };
        DatasetOptions options = new() { Clock = TimeProvider.System, ExtentMerges = policy, Maintenance = MaintenanceMode.Off };
        int commits = RecPortfolio.Shape(size).Commits;
        int every = commits / 100;
        Terms terms = new();
        List<RecQuad> probes = [];
        RecQuad subject = default;
        Target target = await Target.CreateAsync(kind);
        List<double> checkpointMs = [];
        List<double> maintainMs = [];
        long checkpointed = 0;
        long rewritten = 0;

        // The load, a checkpoint every hundredth, maintenance after each.
        Stopwatch load = Stopwatch.StartNew();
        IStorage written = await target.OpenAsync();
        PageCounter loadPages = new(written);
        IStorage counted = target.Counter is null ? loadPages : written;

        await using (StoreDatasetType dataset = await StoreDatasetType.CreateAsync(counted, new DatasetId(Guid.NewGuid()), options))
        {
            int n = 0;

            foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
            {
                await dataset.CommitAsync(Request(commit, terms, probes, ref subject, n));

                if (++n % every == 0)
                {
                    long before = Written();
                    long started = Stopwatch.GetTimestamp();
                    await dataset.CheckpointAsync(dataset.Head);
                    checkpointMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    checkpointed += Written() - before;
                    before = Written();
                    started = Stopwatch.GetTimestamp();
                    await dataset.MaintainAsync();
                    maintainMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    rewritten += Written() - before;
                }
            }
        }

        load.Stop();
        await Target.CloseAsync(written);

        long Written() => target.Counter is null ? loadPages.ExtentBytesWritten : target.Counter.Snapshot().BytesUp;

        // Opened again, a fresh cache: the chain is the projection's base.
        IStorage opened = await target.OpenAsync(fresh: true);
        PageCounter pages = new(opened);
        IStorage reading = target.Counter is null ? pages : opened;
        List<string> lines = [];

        await using (StoreDatasetType dataset = await StoreDatasetType.OpenAsync(reading, options))
        {
            List<(string Name, long From, long To, long Bytes)> sets = await SetsAsync(opened);
            (string Name, long From, long To, long Bytes) largest = sets.OrderByDescending(s => s.Bytes).First();
            (string Name, long From, long To, long Bytes) smaller = sets.Where(s => s.Bytes * 4 <= largest.Bytes && s.To - s.From >= 2).OrderByDescending(s => s.Bytes).FirstOrDefault();
            long inLargest = (largest.From + largest.To) / 2;

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"## As-of reads that seek, REC {size} on {kind}: {sets.Count} sets, the largest {Mib(largest.Bytes)} ({largest.From}, {largest.To}], generator output version {RecPortfolio.Version}"));
            lines.Add("");
            lines.Add("| Read | A quad's lookup p50 / p99 | Its pages p50 / max | A subject's quads p50 / p99 | Their pages p50 / max |");
            lines.Add("|---|---:|---:|---:|---:|");

            using (DatasetView head = dataset.Pin())
            {
                lines.Add(Line("head", head, pages, terms, probes, subject, target.Counter is null));
            }

            double opening = await AsOfAsync(dataset, new Position(inLargest));

            using (DatasetView inside = await dataset.AsOfAsync(new Position(inLargest)))
            {
                lines.Add(Line("as of " + inLargest + ", inside the largest set", inside, pages, terms, probes, subject, target.Counter is null));
            }

            lines.Add("");
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"- opening the view inside the largest set and its estimate of every quad, median of 5: {opening:F2} ms"));

            // The same predicate's quads at the head, beside the as-of scans below.
            using (DatasetView head = dataset.Pin())
            {
                lines.Add(PredicateLine(0, head, pages, terms, subject).Replace("inside a set of 0.0 MiB", "at the head", StringComparison.Ordinal));
            }

            // Scans inside two sets at least 4× apart, by the probes' subjects.
            if (smaller != default)
            {
                foreach ((string Name, long From, long To, long Bytes) set in new[] { largest, smaller })
                {
                    using DatasetView view = await dataset.AsOfAsync(new Position((set.From + set.To) / 2));
                    lines.Add(ScanLine(set.Bytes, view, pages, terms, probes));

                    // A broad pattern: one predicate's quads, early, halfway and late in the window.
                    foreach (int quarter in new[] { 1, 2, 3 })
                    {
                        using DatasetView at = await dataset.AsOfAsync(new Position(set.From + ((set.To - set.From) * quarter / 4)));
                        lines.Add(PredicateLine(set.Bytes, at, pages, terms, subject));
                    }
                }
            }

            // On an object store: the ranged reads of lookups from a fresh
            // cache, each view opened on its own so neither warms the other.
            if (target.Counter is not null)
            {
                await Target.CloseAsync(opened);

                foreach (bool asOf in new[] { false, true })
                {
                    IStorage cold = await target.OpenAsync(fresh: true);

                    try
                    {
                        await using StoreDatasetType reopened = await StoreDatasetType.OpenAsync(cold, options);
                        using DatasetView view = asOf ? await reopened.AsOfAsync(new Position(inLargest)) : reopened.Pin();
                        lines.Add(ReadsLine(asOf ? "as of " + inLargest + ", inside the largest set" : "head", view, target.Counter, terms, probes, subject));
                    }
                    finally
                    {
                        await Target.CloseAsync(cold);
                    }
                }

                opened = await target.OpenAsync(fresh: true);
            }
        }

        await Target.CloseAsync(opened);
        checkpointMs.Sort();
        maintainMs.Sort();

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();

        if (target.Counter is null)
        {
            Console.WriteLine("- the dataset, kept for the layout statistics (VARVE_SEEK_LAYOUT_DATASET): " + target.DatasetDirectory);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"- load with checkpoints and maintenance: {Seconds(load.Elapsed)}; a checkpoint p50 / p99 / max {Percentile(checkpointMs, 0.5):F0} / {Percentile(checkpointMs, 0.99):F0} / {checkpointMs[^1]:F0} ms, {checkpointMs.Sum() / 1000:F1} s in all; maintenance {maintainMs.Sum() / 1000:F1} s in all"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"- write amplification: {rewritten:N0} bytes rewritten by merges for {checkpointed:N0} bytes checkpointed, {(double)rewritten / Math.Max(1, checkpointed):F2} per byte{(target.Counter is null ? "" : " (every byte uploaded, log included)")}"));
        Console.WriteLine();
    }

    // A table row of reads at one view: a quad's lookup and a subject's
    // quads, p50 / p99, and the extent pages each touched at p50 / max.
    private static string Line(string what, DatasetView view, PageCounter pages, Terms terms, List<RecQuad> probes, RecQuad subject, bool counted)
    {
        List<double> lookups = [];
        List<double> scans = [];
        List<double> lookupPages = [];
        List<double> scanPages = [];
        view.TryInternalise(terms.Of(subject.Subject), out TermHandle s);
        int matches = 0;

        for (int round = 0; round < 3; round++)
        {
            foreach (RecQuad probe in probes)
            {
                pages.Reset();
                long started = Stopwatch.GetTimestamp();
                _ = Holds(view, terms, probe);
                lookups.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                lookupPages.Add(pages.Touched);
            }

            for (int i = 0; i < 100; i++)
            {
                pages.Reset();
                long started = Stopwatch.GetTimestamp();
                using IQuadCursor cursor = view.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any);
                matches = 0;

                while (cursor.MoveNext())
                {
                    matches++;
                }

                scans.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                scanPages.Add(pages.Touched);
            }
        }

        lookups.Sort();
        scans.Sort();
        lookupPages.Sort();
        scanPages.Sort();
        // On an object store the pages are counted below the cache, as ranged reads, not here.
        string Pages(List<double> sorted) => counted ? string.Create(CultureInfo.InvariantCulture, $"{Percentile(sorted, 0.5):F0} / {sorted[^1]:F0}") : "—";
        return string.Create(CultureInfo.InvariantCulture,
            $"| {what} | {Percentile(lookups, 0.5):F1} / {Percentile(lookups, 0.99):F1} µs | {Pages(lookupPages)} | {Percentile(scans, 0.5):F1} / {Percentile(scans, 0.99):F1} µs ({matches} quads) | {Pages(scanPages)} |");
    }

    // Scans of the probes' distinct subjects at one view: time and pages per scan and per match.
    private static string ScanLine(long setBytes, DatasetView view, PageCounter pages, Terms terms, List<RecQuad> probes)
    {
        List<double> times = [];
        long matches = 0;
        long touched = 0;
        int scans = 0;

        for (int round = 0; round < 3; round++)
        {
            foreach (string subject in probes.Select(p => p.Subject).Distinct().Take(200))
            {
                if (!view.TryInternalise(terms.Of(subject), out TermHandle s))
                {
                    continue;
                }

                pages.Reset();
                long started = Stopwatch.GetTimestamp();
                using IQuadCursor cursor = view.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any);
                int found = 0;

                while (cursor.MoveNext())
                {
                    found++;
                }

                times.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);

                if (round == 0)
                {
                    matches += found;
                    touched += pages.Touched;
                    scans++;
                }
            }
        }

        times.Sort();
        double total = times.Sum() / 3;
        return string.Create(CultureInfo.InvariantCulture,
            $"- subject scans inside a set of {Mib(setBytes)} at {view.Position.Value}: {scans} subjects, {matches:N0} quads; per scan p50 / p99 {Percentile(times, 0.5):F1} / {Percentile(times, 0.99):F1} µs, {(double)touched / Math.Max(1, scans):F1} pages; per match {total / Math.Max(1, matches):F2} µs, {(double)touched / Math.Max(1, matches):F3} pages");
    }

    // One predicate's quads at a view: time and pages, in all and per match.
    private static string PredicateLine(long setBytes, DatasetView view, PageCounter pages, Terms terms, RecQuad subject)
    {
        if (!view.TryInternalise(terms.Of(subject.Predicate), out TermHandle p))
        {
            return "- the predicate is not yet in the dictionary at " + view.Position.Value;
        }

        List<double> times = [];
        int matches = 0;
        int touched = 0;

        for (int round = 0; round < 5; round++)
        {
            pages.Reset();
            long started = Stopwatch.GetTimestamp();
            using IQuadCursor cursor = view.Match(TermHandle.None, p, TermHandle.None, GraphPattern.Any);
            matches = 0;

            while (cursor.MoveNext())
            {
                matches++;
            }

            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            touched = pages.Touched;
        }

        times.Sort();
        double median = times[times.Count / 2];
        return string.Create(CultureInfo.InvariantCulture,
            $"- a predicate's quads inside a set of {Mib(setBytes)} at {view.Position.Value}: {matches:N0} quads, {median:F2} ms, {touched} pages; per match {median * 1000 / Math.Max(1, matches):F3} µs, {(double)touched / Math.Max(1, matches):F4} pages");
    }

    // The ranged reads of lookups and a scan from a fresh cache: each is a page miss.
    private static string ReadsLine(string what, DatasetView view, CountingStore counter, Terms terms, List<RecQuad> probes, RecQuad subject)
    {
        List<double> lookups = [];
        List<double> reads = [];

        foreach (RecQuad probe in probes.Take(200))
        {
            long before = counter.Snapshot().Reads;
            long started = Stopwatch.GetTimestamp();
            _ = Holds(view, terms, probe);
            lookups.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            reads.Add(counter.Snapshot().Reads - before);
        }

        view.TryInternalise(terms.Of(subject.Subject), out TermHandle s);
        long scanBefore = counter.Snapshot().Reads;
        long scanStarted = Stopwatch.GetTimestamp();
        int matches = T(view.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any));
        double scanMs = Stopwatch.GetElapsedTime(scanStarted).TotalMilliseconds;
        long scanReads = counter.Snapshot().Reads - scanBefore;
        lookups.Sort();
        reads.Sort();
        return string.Create(CultureInfo.InvariantCulture,
            $"- {what}, from a fresh cache, 200 lookups: a lookup p50 / p99 {Percentile(lookups, 0.5):F2} / {Percentile(lookups, 0.99):F2} ms, ranged reads p50 / max {Percentile(reads, 0.5):F0} / {reads[^1]:F0}, {reads.Sum():F0} in all; then a subject's {matches} quads {scanMs:F2} ms, {scanReads} ranged reads");

        static int T(IQuadCursor cursor)
        {
            using (cursor)
            {
                int count = 0;

                while (cursor.MoveNext())
                {
                    count++;
                }

                return count;
            }
        }
    }

    // The catalog's sets and the bytes of each, read through the derived store.
    private static async Task<List<(string Name, long From, long To, long Bytes)>> SetsAsync(IStorage storage)
    {
        byte[] file;

        using (IReadableBlob blob = await storage.Derived.OpenAsync(new BlobName("catalog"), CancellationToken.None))
        {
            file = new byte[blob.Length.Value];
            _ = blob.Read(new ByteOffset(0), file);
        }

        CatalogSummary catalog = CatalogSummary.TryRead(file) ?? throw new InvalidOperationException("No catalog after the checkpoints.");
        List<(string, long, long, long)> sets = [];

        foreach (CatalogSet set in catalog.Sets)
        {
            long bytes = 0;

            foreach (BlobName name in set.Blobs)
            {
                try
                {
                    using IReadableBlob blob = await storage.Derived.OpenAsync(name, CancellationToken.None);
                    bytes += name.Value.EndsWith("graphs", StringComparison.Ordinal) || name.Value.EndsWith("commits", StringComparison.Ordinal) ? 0 : blob.Length.Value;
                }
                catch (KeyNotFoundException)
                {
                }
            }

            sets.Add((set.Name.Value, set.From.Value, set.To.Value, bytes));
        }

        return sets;
    }

    /// <summary>
    /// A storage whose derived store counts the distinct 1 MiB pages of
    /// extents read since the last reset, and the bytes written under
    /// <c>extents/</c>.
    /// </summary>
    private sealed class PageCounter(IStorage inner) : IStorage
    {
        // Each page read since the reset, as the blob name's hash and the
        // page, deduplicated when counted: a read records, it never allocates.
        private readonly long[] _pages = new long[1 << 16];
        private readonly long[] _written = new long[1];
        private int _count;

        public ISegmentStore Log => inner.Log;

        public IDerivedStore Derived => new Store(inner.Derived, this);

        internal int Touched => _pages.AsSpan(0, Math.Min(_count, _pages.Length)).ToArray().Distinct().Count();

        internal long ExtentBytesWritten => Interlocked.Read(ref _written[0]);

        internal void Reset() => _count = 0;

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        private void Touch(int name, long offset, int length)
        {
            if (length <= 0)
            {
                return;
            }

            for (long page = offset / Page; page <= (offset + length - 1) / Page; page++)
            {
                // The benchmark reads on one thread at a time.
                int at = _count++;

                if (at < _pages.Length)
                {
                    _pages[at] = ((long)name << 32) ^ page;
                }
            }
        }

        private sealed class Store(IDerivedStore store, PageCounter counter) : IDerivedStore
        {
            public async ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
            {
                IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
                return name.Value.StartsWith("extents/", StringComparison.Ordinal) ? new Writer(writer, counter) : writer;
            }

            public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
            {
                IReadableBlob blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false);
                return name.Value.StartsWith("extents/", StringComparison.Ordinal) ? new Blob(blob, StringComparer.Ordinal.GetHashCode(name.Value), counter) : blob;
            }

            public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) => store.DeleteAsync(name, cancellationToken);

            public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
        }

        private sealed class Blob(IReadableBlob inner, int name, PageCounter counter) : IReadableBlob
        {
            public ByteCount Length => inner.Length;

            public int Read(ByteOffset offset, Span<byte> destination)
            {
                int read = inner.Read(offset, destination);
                counter.Touch(name, offset.Value, read);
                return read;
            }

            public void Dispose() => inner.Dispose();
        }

        private sealed class Writer(IBlobWriter inner, PageCounter counter) : IBlobWriter
        {
            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                Interlocked.Add(ref counter._written[0], bytes.Length);
                return inner.WriteAsync(bytes, cancellationToken);
            }

            public ValueTask PublishAsync(CancellationToken cancellationToken) => inner.PublishAsync(cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
