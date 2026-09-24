// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Store;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The quads the store benchmarks load, generated from the index so that a run
/// reproduces without a file (ADR 0027).
/// </summary>
/// <remarks>
/// 1,000,000 quads: 100,000 subjects with ten quads each, seventeen
/// predicates, a sixth of the quads in the default graph and the rest across
/// five named graphs, and objects mixed between IRIs shared across
/// subjects, canonical integers (inline ids), plain and language-tagged
/// literals, and non-canonical integers (dictionary entries). Synthetic, and
/// read as such.
/// </remarks>
internal static class StoreDataset
{
    internal const int Quads = 1_000_000;

    internal static (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] Build(int count, int offset = 0)
    {
        RdfTerm[] predicates = [.. Enumerable.Range(0, 17).Select(i => Iri("predicate/" + i.ToString(CultureInfo.InvariantCulture)))];
        RdfTerm[] graphs = [.. Enumerable.Range(0, 5).Select(i => Iri("graph/" + i.ToString(CultureInfo.InvariantCulture)))];
        RdfTerm integer = Iri("http://www.w3.org/2001/XMLSchema#integer", absolute: true);
        var quads = new (RdfTerm, RdfTerm, RdfTerm, RdfTerm?)[count];

        for (int n = 0; n < count; n++)
        {
            int i = n + offset;
            string text = i.ToString(CultureInfo.InvariantCulture);
            RdfTerm subject = Iri("subject/" + (i / 10).ToString(CultureInfo.InvariantCulture));
            RdfTerm @object = (i % 6) switch
            {
                0 => Iri("object/" + (i % 5000).ToString(CultureInfo.InvariantCulture)),
                1 => RdfTerm.Literal(Encoding.UTF8.GetBytes(text), integer),
                2 => RdfTerm.Literal(Encoding.UTF8.GetBytes("value " + text)),
                3 => RdfTerm.Literal(Encoding.UTF8.GetBytes("tagged " + text), "en"u8),
                4 => RdfTerm.Literal(Encoding.UTF8.GetBytes("0" + text), integer),
                _ => Iri("object/" + text),
            };

            quads[n] = (subject, predicates[i % 17], @object, i % 6 == 5 ? null : graphs[i % 5]);
        }

        return quads;
    }

    internal static CommitRequest Request((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads, int start, int count)
    {
        CommitRequest request = new();

        for (int i = start; i < start + count; i++)
        {
            (RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) = quads[i];
            _ = g is null ? request.Assert(s, p, o) : request.Assert(s, p, o, g);
        }

        return request;
    }

    internal static async Task<StoreDatasetType> LoadAsync((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads, int batch)
    {
        StoreDatasetType dataset = await StoreDatasetType.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System });

        for (int start = 0; start < quads.Length; start += batch)
        {
            await dataset.CommitAsync(Request(quads, start, Math.Min(batch, quads.Length - start)));
        }

        return dataset;
    }

    private static RdfTerm Iri(string local, bool absolute = false) =>
        RdfTerm.Iri(Encoding.UTF8.GetBytes(absolute ? local : "http://example.org/store/" + local));
}

/// <summary>Commit throughput: quads into the log, the dictionary and the default projection.</summary>
[MemoryDiagnoser]
public class CommitBenchmarks
{
    private const int Loaded = 100_000;
    private (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] _quads = [];
    private (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] _fresh = [];
    private CommitRequest _bulk = new();
    private CommitRequest[] _batches = [];
    private CommitRequest[] _singles = [];
    private StoreDatasetType? _dataset;

    [GlobalSetup]
    public void Setup()
    {
        _quads = StoreDataset.Build(Loaded);
        _fresh = StoreDataset.Build(1_000, offset: StoreDataset.Quads);
        _bulk = StoreDataset.Request(_quads, 0, Loaded);
        _batches = [.. Enumerable.Range(0, 100).Select(b => StoreDataset.Request(_quads, b * 1_000, 1_000))];
        _singles = [.. Enumerable.Range(0, 1_000).Select(i => StoreDataset.Request(_fresh, i, 1))];
    }

    [IterationSetup(Targets = [nameof(OneCommitOf100000), nameof(HundredCommitsOf1000)])]
    public void Empty() => _dataset = StoreDatasetType.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();

    [IterationSetup(Target = nameof(ThousandCommitsOf1IntoLoaded))]
    public void Preloaded() => _dataset = StoreDataset.LoadAsync(_quads, 10_000).GetAwaiter().GetResult();

    /// <summary>100,000 quads, one commit, into an empty dataset.</summary>
    [Benchmark]
    public async Task<long> OneCommitOf100000() => (await _dataset!.CommitAsync(_bulk)).Position;

    /// <summary>100,000 quads as 100 commits of 1,000, into an empty dataset.</summary>
    [Benchmark]
    public async Task<long> HundredCommitsOf1000()
    {
        long position = 0;

        foreach (CommitRequest batch in _batches)
        {
            position = (await _dataset!.CommitAsync(batch)).Position;
        }

        return position;
    }

    /// <summary>1,000 single-quad commits of new terms, into a dataset of 100,000.</summary>
    [Benchmark]
    public async Task<long> ThousandCommitsOf1IntoLoaded()
    {
        long position = 0;

        foreach (CommitRequest single in _singles)
        {
            position = (await _dataset!.CommitAsync(single)).Position;
        }

        return position;
    }
}

/// <summary>Scan throughput over a pinned read of 1,000,000 quads.</summary>
[MemoryDiagnoser]
public class ScanBenchmarks
{
    private StoreDatasetType? _dataset;
    private DatasetView? _view;
    private TermHandle _predicate;
    private TermHandle[] _subjects = [];

    [GlobalSetup]
    public void Setup()
    {
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads = StoreDataset.Build(StoreDataset.Quads);
        _dataset = StoreDataset.LoadAsync(quads, 10_000).GetAwaiter().GetResult();
        _view = _dataset.Pin();
        _view.TryInternalise(quads[3].P, out _predicate);
        _subjects = [.. Enumerable.Range(0, 10_000).Select(i => _view.TryInternalise(quads[i * 97].S, out TermHandle h) ? h : TermHandle.None)];

        long count = Count(TermHandle.None, TermHandle.None, GraphPattern.Any);

        if (count != StoreDataset.Quads)
        {
            throw new InvalidOperationException("The dataset holds " + count + " quads, not " + StoreDataset.Quads + ".");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _view?.Dispose();

    private long Count(TermHandle subject, TermHandle predicate, GraphPattern graph)
    {
        long count = 0;

        using IQuadCursor cursor = _view!.Match(subject, predicate, TermHandle.None, graph);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    /// <summary>Every quad, every graph.</summary>
    [Benchmark]
    public long FullScan() => Count(TermHandle.None, TermHandle.None, GraphPattern.Any);

    /// <summary>One predicate of seventeen, every graph: a POSG range.</summary>
    [Benchmark]
    public long BoundPredicate() => Count(TermHandle.None, _predicate, GraphPattern.Any);

    /// <summary>The default graph only: a graph-first range.</summary>
    [Benchmark]
    public long DefaultGraph() => Count(TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph);

    /// <summary>10,000 subject lookups, ten quads each: SPOG ranges.</summary>
    [Benchmark]
    public long TenThousandSubjectLookups()
    {
        long count = 0;

        foreach (TermHandle subject in _subjects)
        {
            count += Count(subject, TermHandle.None, GraphPattern.Any);
        }

        return count;
    }
}

/// <summary>Sizes, not times: what ADR 0012's revisit condition asks about index size.</summary>
internal static class StoreSizes
{
    internal static async Task PrintAsync()
    {
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads = StoreDataset.Build(StoreDataset.Quads);
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        MemoryStorage storage = new();
        StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System });

        for (int start = 0; start < quads.Length; start += 10_000)
        {
            await dataset.CommitAsync(StoreDataset.Request(quads, start, 10_000));
        }

        long logBytes = 0;

        foreach (SegmentInfo segment in await storage.Log.ListSegmentsAsync(default))
        {
            logBytes += segment.Length;
        }

        await dataset.CheckpointAsync(dataset.Head);
        string name = (await storage.Derived.ListAsync(default)).Single();
        long checkpointBytes = (await storage.Derived.GetRangeAsync(name, 0, int.MaxValue, default)).Length;
        GC.KeepAlive(quads);
        long after = GC.GetTotalMemory(forceFullCollection: true);

        using DatasetView view = dataset.Pin();
        int terms = quads.SelectMany(q => new[] { q.S, q.P, q.O, q.G }).OfType<RdfTerm>().Distinct().Count(t => !view.TryInternalise(t, out TermHandle h) || (h.Value >> 62) != 3);
        long keyBytes = (long)StoreDataset.Quads * 6 * 32;

        Print("quads", StoreDataset.Quads);
        Print("distinct terms with a dictionary entry (inline integers excluded)", terms);
        Print("log bytes", logBytes, logBytes / (double)StoreDataset.Quads);
        Print("checkpoint bytes (six key arrays + dictionary + header)", checkpointBytes, checkpointBytes / (double)StoreDataset.Quads);
        Print("of which the six key arrays", keyBytes, 192);
        Print("managed heap growth for the whole store (log, dictionary, runs, checkpoint)", after - before, (after - before) / (double)StoreDataset.Quads);

        static void Print(string what, long value, double? perQuad = null) =>
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{what}: {value:N0}{(perQuad is double p ? $" ({p:N1} bytes per quad)" : string.Empty)}"));
    }
}
