// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using Varve.Classification;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;

namespace Varve.Benchmarks;

/// <summary>
/// The read filter's cost at REC (ADR 0235): a full scan, and 10,000 subject
/// lookups, through the scopes a caller is served by. Every arm reads the
/// same pinned head of the REC portfolio, classified by the classification
/// projection's cold start over <see cref="ClassificationProjectionBench"/>'s
/// shapes (companies and sites are data subjects). Row scopes compose over
/// the graph scope, as the protocol composes them, so the row arms' cost
/// over <see cref="GraphScope"/> is the row filter's own.
/// </summary>
[MemoryDiagnoser]
[WarmupCount(3)]
[IterationCount(10)]
public class RowScopeRecBenchmarks
{
    private const string SiteGraphs = "https://example.org/rec/graph/";

    private FileDataset? _dataset;
    private DatasetView? _view;
    private IRowClassification? _rows;
    private IRowClassification? _rowsAllPublic;
    private IQuadSource? _graphs;
    private RowScope? _one;
    private RowScope? _half;
    private TermHandle[] _subjects = [];

    /// <summary>The portfolio's size: <c>S</c> or <c>M</c>.</summary>
    [Params("M")]
    public string Size { get; set; } = "M";

    [GlobalSetup]
    public void Setup()
    {
        (FileDataset dataset, _) = RecShapes.LoadAsync(Enum.Parse<RecSize>(Size), 20261015, 0).GetAwaiter().GetResult();
        _dataset = dataset;
        _dataset.Dataset.CommitAsync(ClassificationProjectionBench.ClassifierShapes()).AsTask().GetAwaiter().GetResult();
        _dataset.Dataset.MaintainAsync().AsTask().GetAwaiter().GetResult();
        ClassificationProjection projection = new(_dataset.Dataset, ClassificationProjectionBench.RecBinding);
        _view = _dataset.Dataset.Pin();
        projection.ResetAsync(_view, default).AsTask().GetAwaiter().GetResult();

        if (!projection.TryAt(_view.Position, _view, out IRowClassification rows))
        {
            throw new InvalidOperationException("The projection has not reached the head.");
        }

        _rows = rows;
        _rowsAllPublic = new AllPublic(rows);
        _graphs = GraphScopedQuadSource.Wrap(_view, GraphScope.Of([], [SiteGraphs], DefaultGraphAccess.Excluded));

        // The data subjects, by IRI: the first alone, and the first half.
        List<RdfTerm> subjects = [.. DataSubjects(_view, rows).OrderBy(static t => Encoding.UTF8.GetString(t.Lexical), StringComparer.Ordinal)];
        _one = Scope([subjects[0]]);
        _half = Scope([.. subjects.Take(subjects.Count / 2)]);

        // 10,000 subjects of the site graphs, spread evenly over them.
        List<TermHandle> rowsOf = [];
        HashSet<TermHandle> seen = new(_view.TermComparer);
        using (IQuadCursor cursor = _graphs.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                if (seen.Add(cursor.Current.Subject))
                {
                    rowsOf.Add(cursor.Current.Subject);
                }
            }
        }

        _subjects = [.. Enumerable.Range(0, 10_000).Select(i => rowsOf[(int)((long)i * rowsOf.Count / 10_000)])];
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"// REC {Size}: {Count(_view):N0} quads, {Count(_graphs):N0} in the site graphs; {subjects.Count:N0} data subjects; one subject's scope shows {Count(new RowScopedQuadSource(_graphs, rows, _one)):N0}, half's {Count(new RowScopedQuadSource(_graphs, rows, _half)):N0}"));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _view?.Dispose();
        _dataset?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Every quad of the view, unwrapped: today's unscoped read.</summary>
    [Benchmark(Baseline = true)]
    public long FullScan() => Count(_view!);

    /// <summary>Through the graph scope of the site graphs alone (ADR 0107): graph-only scoping.</summary>
    [Benchmark]
    public long FullScanGraphScoped() => Count(_graphs!);

    /// <summary>The graph scope, and a row scope restricting nothing: <c>Wrap</c> builds no wrapper.</summary>
    [Benchmark]
    public long FullScanRowScopedAll() => Count(RowScopedQuadSource.Wrap(_graphs!, _rows!, RowScope.All));

    /// <summary>The graph scope, then the rows of one data subject.</summary>
    [Benchmark]
    public long FullScanRowScopedOne() => Count(new RowScopedQuadSource(_graphs!, _rows!, _one!));

    /// <summary>The graph scope, then the rows of half the data subjects.</summary>
    [Benchmark]
    public long FullScanRowScopedHalf() => Count(new RowScopedQuadSource(_graphs!, _rows!, _half!));

    /// <summary>
    /// As <see cref="FullScanRowScopedHalf"/>, with every predicate a public
    /// link: the subject's probe only, so the difference is the object half
    /// of about(q).
    /// </summary>
    [Benchmark]
    public long FullScanRowScopedHalfSubjectOnly() => Count(new RowScopedQuadSource(_graphs!, _rowsAllPublic!, _half!));

    /// <summary>10,000 subject lookups through the graph scope.</summary>
    [Benchmark]
    public long TenThousandSubjectLookupsGraphScoped() => Lookups(_graphs!);

    /// <summary>10,000 subject lookups through half the data subjects' rows.</summary>
    [Benchmark]
    public long TenThousandSubjectLookupsRowScopedHalf() => Lookups(new RowScopedQuadSource(_graphs!, _rows!, _half!));

    private long Lookups(IQuadSource source)
    {
        long count = 0;

        foreach (TermHandle subject in _subjects)
        {
            using IQuadCursor cursor = source.Match(subject, TermHandle.None, TermHandle.None, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                count++;
            }
        }

        return count;
    }

    private static long Count(IQuadSource source)
    {
        long count = 0;
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static RowScope Scope(RdfTerm[] subjects) =>
        RowScope.Of([new(ClassificationProjectionBench.RecBinding.Binding, SubjectScope.Of(subjects, [], [], [], []))]);

    // The nodes that are members of their own subject set.
    private static List<RdfTerm> DataSubjects(DatasetView view, IRowClassification rows)
    {
        BindingIndex binding = new(0);
        HashSet<TermHandle> nodes = new(view.TermComparer);
        List<RdfTerm> subjects = [];
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            TermHandle node = cursor.Current.Subject;

            if (rows.BindingOf(cursor.Current.Graph).IsNone || !nodes.Add(node))
            {
                continue;
            }

            SubjectSetId set = rows.SubjectsOf(binding, node);

            if (!set.IsNone && rows.Members(binding, set) is { Length: 1 } members && view.TermComparer.Equals(members[0], node) && view.TryExternalise(node, out RdfTerm? term))
            {
                subjects.Add(term);
            }
        }

        return subjects;
    }

    /// <summary>A classification as it is, but every predicate a public link.</summary>
    private sealed class AllPublic(IRowClassification rows) : IRowClassification
    {
        public IReadOnlyList<RdfTerm> Bindings => rows.Bindings;

        public BindingIndex BindingOf(TermHandle graph) => rows.BindingOf(graph);

        public SubjectSetId SubjectsOf(BindingIndex binding, TermHandle node) => rows.SubjectsOf(binding, node);

        public ReadOnlySpan<TermHandle> Members(BindingIndex binding, SubjectSetId set) => rows.Members(binding, set);

        public bool IsPublicLink(BindingIndex binding, TermHandle predicate) => true;
    }
}
