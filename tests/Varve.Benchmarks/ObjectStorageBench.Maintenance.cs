// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--extent-maintenance &lt;S|M&gt; [fan-out] [max-set-MiB]</c> (ADRs 0158,
/// 0159): in one run on one machine, the reads of #92's table — 100, 50 and
/// 10 sets by dropping, merges off — and the same reads after 10, 50 and 100
/// checkpoints with the merge policy on, a checkpoint every hundredth of the
/// history and maintenance after each; the bytes merges rewrite per byte
/// checkpointed; and the read latency gate: head reads at 100 checkpoints
/// with merges within 1.5× of the 10-checkpoint row at p50 and p99, and an
/// as-of read inside the largest set within 2× of its as-of row.
/// </summary>
internal static partial class ObjectStorageBench
{
    internal static async Task ExtentMaintenanceAsync(string sizeName, int fanOut, long? maxSetBytes)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        ExtentMergePolicy policy = new() { FanOut = fanOut, MaxSetBytes = new ByteCount(maxSetBytes ?? ExtentMergePolicy.DefaultMaxSetBytes.Value) };
        int commits = RecPortfolio.Shape(size).Commits;
        int every = commits / 100;
        Terms terms = new();
        List<RecQuad> probes = [];
        RecQuad subject = default;
        List<ReadRow> rows = [];
        string without = Directory.CreateTempSubdirectory("varve-maintenance-off-").FullName;
        string with = Directory.CreateTempSubdirectory("varve-maintenance-on-").FullName;

        try
        {
            // Without merges: #92's procedure — the load with 100
            // checkpoints, then fewer sets by dropping, each read after an open.
            DatasetOptions off = new() { Clock = TimeProvider.System, ExtentMerges = ExtentMergePolicy.Off, Maintenance = MaintenanceMode.Off };

            await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(without), new FileStorageOptions { Clock = TimeProvider.System }))
            await using (StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), off))
            {
                int n = 0;

                foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
                {
                    await dataset.CommitAsync(Request(commit, terms, probes, ref subject, n));

                    if (++n % every == 0)
                    {
                        await dataset.CheckpointAsync(dataset.Head);
                        await dataset.MaintainAsync();
                    }
                }
            }

            foreach (int keep in new[] { 100, 50, 10 })
            {
                await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(without), new FileStorageOptions { Clock = TimeProvider.System });

                await using (StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, off))
                {
                    List<Position> checkpoints = [.. dataset.Checkpoints];
                    int stride = checkpoints.Count / keep;

                    for (int i = 0; i < checkpoints.Count; i++)
                    {
                        if ((i + 1) % stride != 0)
                        {
                            await dataset.DropCheckpointAsync(checkpoints[i]);
                        }
                    }
                }

                await using StoreDatasetType reopened = await StoreDatasetType.OpenAsync(storage, off);
                rows.Add(await ReadsAsync(reopened, without, "without merges", keep, terms, probes, subject));
            }

            // With merges: a checkpoint every hundredth, maintenance after
            // each, and the reads after 10, 50 and 100 checkpoints, each after
            // an open. Bytes under extents/ are counted by who wrote them.
            DatasetOptions on = new() { Clock = TimeProvider.System, ExtentMerges = policy, Maintenance = MaintenanceMode.Off };
            CountingDerived? counted = null;
            long checkpointed = 0;
            long rewritten = 0;
            int taken = 0;
            List<(int Checkpoints, int Sets)> chain = [];
            IEnumerator<RecCommit> history = RecPortfolio.Generate(size, Seed).GetEnumerator();
            int loaded = 0;
            Stopwatch load = Stopwatch.StartNew();

            foreach (int at in new[] { 10, 50, 100 })
            {
                await using (FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(with), new FileStorageOptions { Clock = TimeProvider.System }))
                {
                    counted = new CountingDerived(files);
                    await using StoreDatasetType dataset = loaded == 0
                        ? await StoreDatasetType.CreateAsync(counted, new DatasetId(Guid.NewGuid()), on)
                        : await StoreDatasetType.OpenAsync(counted, on);
                    RecQuad unused = default;

                    while (taken < at && history.MoveNext())
                    {
                        await dataset.CommitAsync(Request(history.Current, terms, [], ref unused, loaded));

                        if (++loaded % every == 0)
                        {
                            long before = counted.ExtentBytes;
                            await dataset.CheckpointAsync(dataset.Head);
                            checkpointed += counted.ExtentBytes - before;
                            before = counted.ExtentBytes;
                            await dataset.MaintainAsync();
                            rewritten += counted.ExtentBytes - before;
                            taken++;
                            chain.Add((taken, dataset.Checkpoints.Count));
                        }
                    }
                }

                load.Stop();
                await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(with), new FileStorageOptions { Clock = TimeProvider.System });
                await using StoreDatasetType measured = await StoreDatasetType.OpenAsync(storage, on);
                rows.Add(await ReadsAsync(measured, with, "with merges", at, terms, probes, subject));
                load.Start();
            }

            load.Stop();
            Console.WriteLine("## Extent maintenance, REC " + size + " on files: fan-out " + fanOut + ", sets capped at " + Mib(policy.MaxSetBytes.Value));
            Console.WriteLine();
            Console.WriteLine("Rows slower first, by a quad's lookup at p50. \"Largest set\" is the as-of read inside the set of the most bytes.");
            Console.WriteLine();
            Console.WriteLine("| Checkpoints | Merges | Sets | Lookup p50 / p99 | A subject's quads p50 / p99 | Exact count p50 / p99 | As-of inside a set | As-of inside the largest set | As-of at a set's end |");
            Console.WriteLine("|---:|---|---:|---:|---:|---:|---:|---:|---:|");

            foreach (ReadRow row in rows.OrderByDescending(r => r.Lookup.P50))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {row.Checkpoints} | {row.Kind} | {row.Sets} | {row.Lookup.P50:F1} / {row.Lookup.P99:F1} µs | {row.Scan.P50:F1} / {row.Scan.P99:F1} µs | {row.Count.P50:F1} / {row.Count.P99:F1} µs | {row.Inside:F1} ms | {row.Largest:F1} ms ({Mib(row.LargestBytes)}) | {row.End:F1} ms |"));
            }

            Console.WriteLine();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"- write amplification: {rewritten:N0} bytes rewritten by merges for {checkpointed:N0} bytes checkpointed, {(double)rewritten / Math.Max(1, checkpointed):F2} per byte"));
            Console.WriteLine("- load with checkpoints and maintenance: " + Seconds(load.Elapsed));
            Console.WriteLine("- sets after each checkpoint (checkpoints:sets): " + string.Join(" ", chain.Where(c => c.Checkpoints % 10 == 0).Select(c => c.Checkpoints + ":" + c.Sets)));
            Console.WriteLine("- the policy's bound below half the cap: " + ((fanOut - 1) * (Tier(policy.MaxSetBytes.Value / 2, fanOut) + 1)));

            ReadRow baseline = rows.Single(r => r.Kind == "without merges" && r.Checkpoints == 10);
            ReadRow merged = rows.Single(r => r.Kind == "with merges" && r.Checkpoints == 100);
            Console.WriteLine();
            Console.WriteLine("Gate (this run's 10-checkpoint row without merges against 100 checkpoints with merges; the as-of read inside each chain's largest set):");
            Gate("lookup p50", merged.Lookup.P50, baseline.Lookup.P50, 1.5);
            Gate("lookup p99", merged.Lookup.P99, baseline.Lookup.P99, 1.5);
            Gate("a subject's quads p50", merged.Scan.P50, baseline.Scan.P50, 1.5);
            Gate("a subject's quads p99", merged.Scan.P99, baseline.Scan.P99, 1.5);
            Gate("exact count p50", merged.Count.P50, baseline.Count.P50, 1.5);
            Gate("exact count p99", merged.Count.P99, baseline.Count.P99, 1.5);
            // Like for like: the largest set of each chain, read at its middle.
            // REC M's history is front-loaded, so the largest set is the
            // first, and a middle set's end, the "inside a set" column, reads
            // a small one.
            Gate("as-of inside the largest set", merged.Largest, baseline.Largest, 2.0);
            Console.WriteLine();
        }
        finally
        {
            Directory.Delete(without, recursive: true);
            Directory.Delete(with, recursive: true);
        }
    }

    private static void Gate(string what, double measured, double baseline, double factor) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"- {what}: {measured:F1} against {baseline:F1} × {factor:F1} = {baseline * factor:F1}: {(measured <= baseline * factor ? "within" : "OVER")}"));

    private static int Tier(long bytes, int fanOut)
    {
        int tier = 0;

        for (long bound = 1L << 20; bytes > bound; bound *= fanOut)
        {
            tier++;
        }

        return tier;
    }

    private static string Mib(long bytes) => (bytes / (double)(1 << 20)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

    private static CommitRequestBuilder Request(RecCommit commit, Terms terms, List<RecQuad> probes, ref RecQuad subject, int n)
    {
        CommitRequestBuilder request = new();

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

        return request;
    }

    private static async Task<ReadRow> ReadsAsync(StoreDatasetType dataset, string directory, string kind, int checkpoints, Terms terms, List<RecQuad> probes, RecQuad subject)
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

        lookups.Sort();
        scans.Sort();
        counts.Sort();

        // The sets, read from the catalog and their files: the middle set's
        // end, and the set of the most bytes.
        CatalogSummary catalog = CatalogSummary.TryRead(await File.ReadAllBytesAsync(Path.Combine(directory, "derived", "catalog")))
            ?? throw new InvalidOperationException("No catalog after the checkpoints.");
        CatalogSet middle = catalog.Sets[catalog.Sets.Count / 2];
        CatalogSet largest = catalog.Sets[0];
        long largestBytes = 0;

        foreach (CatalogSet set in catalog.Sets)
        {
            long bytes = new DirectoryInfo(Path.Combine(directory, "derived", set.Name.Value)).EnumerateFiles().Sum(f => f.Length);

            if (bytes > largestBytes)
            {
                largestBytes = bytes;
                largest = set;
            }
        }

        double inside = await AsOfAsync(dataset, new Position(middle.To.Value - 1));
        double inLargest = await AsOfAsync(dataset, new Position((largest.From.Value + largest.To.Value) / 2));
        double end = await AsOfAsync(dataset, middle.To);
        return new ReadRow(checkpoints, kind, catalog.Sets.Count, Of(lookups), Of(scans), Of(counts), inside, inLargest, largestBytes, end);
    }

    private static (double P50, double P99) Of(List<double> sorted) => (Percentile(sorted, 0.5), Percentile(sorted, 0.99));

    private sealed record ReadRow(
        int Checkpoints, string Kind, int Sets, (double P50, double P99) Lookup, (double P50, double P99) Scan, (double P50, double P99) Count, double Inside, double Largest, long LargestBytes, double End);

    /// <summary>A file storage whose <c>derived/</c> counts the bytes written under <c>extents/</c>.</summary>
    private sealed class CountingDerived : IStorage
    {
        private readonly Counter _counter = new();

        internal CountingDerived(FileStorage files)
        {
            Log = files.Log;
            Derived = new Store(files.Derived, _counter);
        }

        public ISegmentStore Log { get; }

        public IDerivedStore Derived { get; }

        internal long ExtentBytes => _counter.Bytes;

        private sealed class Counter
        {
            private long _bytes;

            internal long Bytes => Interlocked.Read(ref _bytes);

            internal void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);
        }

        private sealed class Store(IDerivedStore inner, Counter counter) : IDerivedStore
        {
            public async ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
            {
                IBlobWriter writer = await inner.CreateAsync(name, cancellationToken).ConfigureAwait(false);
                return name.Value.StartsWith("extents/", StringComparison.Ordinal) ? new Counting(writer, counter) : writer;
            }

            public ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken) => inner.OpenAsync(name, cancellationToken);

            public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) => inner.DeleteAsync(name, cancellationToken);

            public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);
        }

        private sealed class Counting(IBlobWriter inner, Counter counter) : IBlobWriter
        {
            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                counter.Add(bytes.Length);
                return inner.WriteAsync(bytes, cancellationToken);
            }

            public ValueTask PublishAsync(CancellationToken cancellationToken) => inner.PublishAsync(cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
