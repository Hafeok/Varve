// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using Varve.Conformance.Tests;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Store;

namespace Varve.Benchmarks;

/// <summary>
/// ADR 0050's first measurement: the SPARQL 1.0 and 1.1 query evaluation
/// suites' wall time, whole, over the store's in-memory projection, once per
/// arm. Every case must pass in every arm, or the time is of something else.
/// </summary>
internal static class SuiteTime
{
    internal static void Run(int repetitions)
    {
        List<EvaluationEntry> entries = [.. EvaluationCatalogue.Entries.Where(e =>
            (e.Suite.StartsWith("sparql10/", StringComparison.Ordinal) || e.Suite.StartsWith("sparql11/", StringComparison.Ordinal))
            && !EvaluationCatalogue.IsBlocked(e))];
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{entries.Count} cases of the SPARQL 1.0 and 1.1 suites, over the store, {repetitions} runs per arm, interleaved, after three warm-up rounds"));

        // The arms are interleaved run by run, after three warm-up rounds of
        // each: run one arm to the end before the next and the first pays for
        // tiered compilation, which read as a 2.5× difference the first time.
        ValueAccess[] arms = Enum.GetValues<ValueAccess>();
        Dictionary<ValueAccess, List<double>> times = arms.ToDictionary(a => a, _ => new List<double>());
        for (int round = -3; round < repetitions; round++)
        {
            foreach (ValueAccess arm in arms)
            {
                double elapsed = Once(entries, arm, report: round == -3);
                if (round >= 0)
                {
                    times[arm].Add(elapsed);
                }
            }
        }

        foreach (ValueAccess arm in arms)
        {
            List<double> sorted = [.. times[arm].Order()];
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{arm,-15} median {sorted[sorted.Count / 2],8:F1} ms  min {sorted[0],8:F1}  max {sorted[^1],8:F1}"));
        }
    }

    private static double Once(List<EvaluationEntry> entries, ValueAccess arm, bool report)
    {
        Stopwatch clock = Stopwatch.StartNew();
        int failed = 0;
        foreach (EvaluationEntry entry in entries)
        {
            if (EvaluationRunner.RunAsync(entry, EvaluationSubjects.Store, arm, null, CancellationToken.None).GetAwaiter().GetResult() is { } failure)
            {
                failed++;
                if (report)
                {
                    Console.Error.WriteLine(arm + " " + entry.TestIri + ": " + failure.Split('\n')[0]);
                }
            }
        }

        clock.Stop();
        return failed == 0
            ? clock.Elapsed.TotalMilliseconds
            : throw new InvalidOperationException(arm + ": " + failed + " cases failed; the time would not be the suite's.");
    }
}

/// <summary>
/// ADR 0050's second measurement: one million quads whose objects are inline
/// integers, <c>FILTER(?o &gt; n)</c> at about 1%, 50% and 99% selectivity,
/// <c>ORDER BY ?o</c>, and the equality case, in each arm. The store's
/// integers are inline, so the accessor arm compares without externalising.
/// </summary>
[MemoryDiagnoser]
public class FilterBenchmarks
{
    internal const int Quads = 1_000_000;

    private Varve.Store.Dataset _store = null!;
    private DatasetView _view = null!;
    private Query _query = null!;
    private SparqlEvaluator _evaluator = null!;
    private int _expected;

    [Params(ValueAccess.InlineAccessor, ValueAccess.Externalise, ValueAccess.Materialise)]
    public ValueAccess Arm { get; set; }

    [Params("gt-1%", "gt-50%", "gt-99%", "order", "eq")]
    public string Case { get; set; } = "";

    internal static (string Text, int Solutions) QueryOf(string name) => name switch
    {
        "gt-1%" => ("SELECT ?s ?o { ?s ex:v ?o FILTER(?o > 990000) }", 9_999),
        "gt-50%" => ("SELECT ?s ?o { ?s ex:v ?o FILTER(?o > 500000) }", 499_999),
        "gt-99%" => ("SELECT ?s ?o { ?s ex:v ?o FILTER(?o > 10000) }", 989_999),
        "order" => ("SELECT ?o { ?s ex:v ?o } ORDER BY ?o", Quads),
        "eq" => ("SELECT ?s { ?s ex:v ?o FILTER(?o = \"5\"^^<http://www.w3.org/2001/XMLSchema#integer>) }", 1),
        _ => throw new ArgumentException(name, nameof(name)),
    };

    [GlobalSetup]
    public void Load()
    {
        (_store, _view) = Build();
        (string text, _expected) = QueryOf(Case);
        _query = SparqlParser.ParseQuery(("PREFIX ex: <http://example.org/>\n" + text).AsSpan());
        _evaluator = new SparqlEvaluator(new EvaluationOptions { ValueAccess = Arm });
        if (Evaluate() != _expected)
        {
            throw new InvalidOperationException(Case + " in " + Arm + " did not give " + _expected + " solutions.");
        }
    }

    internal static (Varve.Store.Dataset, DatasetView) Build()
    {
        Varve.Store.Dataset store = Varve.Store.Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();
        RdfTerm predicate = RdfTerm.Iri("http://example.org/v"u8);
        RdfTerm integer = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);
        const int PerCommit = 100_000;
        for (int from = 0; from < Quads; from += PerCommit)
        {
            CommitRequest request = new();
            for (int i = from; i < from + PerCommit; i++)
            {
                string n = i.ToString(CultureInfo.InvariantCulture);
                request.Assert(RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/s" + n)), predicate, RdfTerm.Literal(Encoding.UTF8.GetBytes(n), integer));
            }

            _ = store.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        }

        return (store, store.Pin());
    }

    [GlobalCleanup]
    public void Release()
    {
        _view.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public int Evaluate()
    {
        using QueryResults results = _evaluator.Evaluate(_query, _view);
        SolutionResults solutions = (SolutionResults)results;
        int count = 0;
        while (solutions.MoveNext())
        {
            count++;
        }

        return count;
    }
}
