// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Classification;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;

namespace Varve.Benchmarks;

/// <summary>
/// <c>--row-scope-breakdown &lt;S|M&gt;</c>: one load of REC, then a full scan
/// of the site graphs timed with each part of the row rule's per-quad work
/// alone, so <see cref="RowScopeRecBenchmarks"/>' totals can be read as a
/// sum. Each pass runs five times after one warm-up; the median is printed.
/// </summary>
internal static class RowScopeBreakdown
{
    private const int Runs = 5;

    internal static async Task RunAsync(string sizeName)
    {
        RecSize size = Enum.Parse<RecSize>(sizeName);
        (FileDataset dataset, _) = await RecShapes.LoadAsync(size, 20261015, 0);
        await using (dataset)
        {
            await dataset.Dataset.CommitAsync(ClassificationProjectionBench.ClassifierShapes());
            await dataset.Dataset.MaintainAsync();
            ClassificationProjection projection = new(dataset.Dataset, ClassificationProjectionBench.RecBinding);
            using DatasetView view = dataset.Dataset.Pin();
            await projection.ResetAsync(view, default);
            projection.TryAt(view.Position, view, out IRowClassification rows);
            IQuadSource graphs = GraphScopedQuadSource.Wrap(view, GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded));
            BindingIndex binding = new(0);
            long count = Scan(graphs, static _ => 0);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"REC {size}: {count:N0} quads in the site graphs"));

            Time("the graph-scoped scan", count, () => Scan(graphs, static _ => 0));
            Time("+ the classification's SubjectsOf(subject)", count, () => Scan(graphs, q => rows.SubjectsOf(binding, q.Subject).Value));
            Time("+ SubjectsOf(subject) and SubjectsOf(object)", count, () => Scan(graphs, q => rows.SubjectsOf(binding, q.Subject).Value + rows.SubjectsOf(binding, q.Object).Value));
            Time("+ the view's TryGetTripleTermComponents(object) alone", count, () => Scan(graphs, q => view.TryGetTripleTermComponents(q.Object, out _, out _, out _) ? 1 : 0));
            Time("+ IsPublicLink(predicate) alone", count, () => Scan(graphs, q => rows.IsPublicLink(binding, q.Predicate) ? 1 : 0));
            Time("+ a hash lookup of the subject in a set of every node", count, Lookup(graphs, view));

            List<RdfTerm> subjects = [.. Subjects(view, rows).OrderBy(static t => Encoding.UTF8.GetString(t.Lexical), StringComparer.Ordinal)];
            RowScope half = RowScope.Of([new(ClassificationProjectionBench.RecBinding.Binding, SubjectScope.Of([.. subjects.Take(subjects.Count / 2)], [], [], [], []))]);
            Time("the whole rule, half the data subjects", count, () => Scan(new RowScopedQuadSource(graphs, rows, half), static _ => 0));
        }
    }

    private static Func<long> Lookup(IQuadSource graphs, DatasetView view)
    {
        HashSet<TermHandle> nodes = new(view.TermComparer);
        using (IQuadCursor cursor = graphs.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                nodes.Add(cursor.Current.Subject);
            }
        }

        return () => Scan(graphs, q => nodes.Contains(q.Subject) ? 1 : 0);
    }

    private static long Scan(IQuadSource source, Func<Quad, int> work)
    {
        long total = 0;
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            total += 1 + (work(cursor.Current) & 0);
        }

        return total;
    }

    private static void Time(string what, long quads, Func<long> pass)
    {
        pass();
        double[] times = new double[Runs];

        for (int i = 0; i < Runs; i++)
        {
            long started = Stopwatch.GetTimestamp();
            pass();
            times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(times);
        double median = times[Runs / 2];
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {what}: {median:F0} ms (min {times[0]:F0}, max {times[^1]:F0}), {median * 1e6 / quads:F1} ns a quad"));
    }

    private static List<RdfTerm> Subjects(DatasetView view, IRowClassification rows)
    {
        BindingIndex binding = new(0);
        HashSet<TermHandle> seen = new(view.TermComparer);
        List<RdfTerm> subjects = [];
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            TermHandle node = cursor.Current.Subject;

            if (rows.BindingOf(cursor.Current.Graph).IsNone || !seen.Add(node))
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
}
