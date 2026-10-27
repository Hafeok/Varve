// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// Inference, part 1 (#170): query latency under RDFS rewriting against simple
/// entailment, over a store whose people are typed at the leaves of a small
/// class and property hierarchy held in a TBox graph, and the
/// container-membership scan a pattern over <c>rdfs:member</c> pays (ADR 0193).
/// </summary>
/// <remarks>
/// Each query counts, so that what is timed is the evaluation and not the
/// writing of rows. Every count is checked once in the setup against what the
/// data entails before anything is timed.
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(8)]
public class EntailmentBenchmarks : IDisposable
{
    internal static readonly ImmutableArray<int> Sizes = [10_000, 100_000];

    private const string Ex = "http://example.org/inference/";
    private const string Prefix = "PREFIX ex: <" + Ex + ">\nPREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>\n";
    private const string Students = Prefix + "SELECT (COUNT(*) AS ?n) { ?x a ex:Student }";
    private const string Agents = Prefix + "SELECT (COUNT(*) AS ?n) { ?x a ex:Agent }";
    private const string Knows = Prefix + "SELECT (COUNT(*) AS ?n) { ?x ex:knows ?y }";
    private const string Members = Prefix + "SELECT (COUNT(*) AS ?n) { ?s rdfs:member ?o }";

    private static readonly RdfTerm Tbox = Iri("tbox");

    private StoreDatasetType? _store;
    private DatasetView? _view;
    private TBoxCover[] _covers = [];
    private readonly Dictionary<string, Varve.Sparql.Algebra.Query> _queries = new(StringComparer.Ordinal);

    /// <summary>The number of people; one in twenty also holds a bag of five.</summary>
    [ParamsSource(nameof(Counts))]
    public int People { get; set; }

    public static IEnumerable<int> Counts() => Sizes;

    [GlobalSetup]
    public void Setup()
    {
        CommitRequest request = new();
        RdfTerm subClassOf = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8);
        RdfTerm subPropertyOf = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subPropertyOf"u8);
        RdfTerm domain = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#domain"u8);
        RdfTerm type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
        Add(request, Iri("Student"), subClassOf, Iri("Person"), Tbox);
        Add(request, Iri("Person"), subClassOf, Iri("Agent"), Tbox);
        Add(request, Iri("Agent"), subClassOf, Iri("Thing"), Tbox);
        Add(request, Iri("advisor"), subPropertyOf, Iri("knows"), Tbox);
        Add(request, Iri("knows"), domain, Iri("Person"), Tbox);
        for (int i = 0; i < People; i++)
        {
            RdfTerm person = Iri("p" + i);
            Add(request, person, type, Iri("Student"), null);
            Add(request, person, Iri("advisor"), Iri("p" + ((i * 7) + 1) % People), null);
            if (i % 20 == 0)
            {
                RdfTerm bag = Iri("bag" + i);
                for (int n = 1; n <= 5; n++)
                {
                    Add(request, bag, RdfTerm.Iri(Encoding.UTF8.GetBytes("http://www.w3.org/1999/02/22-rdf-syntax-ns#_" + n)), Iri("p" + ((i + n) % People)), null);
                }
            }
        }

        _store = StoreDatasetType.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();
        _ = _store.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        _view = _store.Pin();
        _covers = [new TBoxCover([Tbox], GraphScope.All)];
        foreach (string text in (string[])[Students, Agents, Knows, Members])
        {
            _queries[text] = Varve.Sparql.SparqlParser.ParseQuery(text.AsSpan());
        }

        long bags = (People + 19) / 20 * 5;
        Expect(SimpleStudents(), People);
        Expect(RdfsStudents(), People);
        Expect(SimpleAgents(), 0);
        Expect(RdfsAgents(), People);
        Expect(RdfsKnows(), People);
        Expect(SimpleMembers(), 0);
        Expect(RdfsMembers(), bags);
        Console.WriteLine($"// {People:N0} people, {bags:N0} container members: every count as the data entails it");
    }

    /// <summary>A pattern the TBox says nothing new about: the rewriting's overhead alone.</summary>
    [Benchmark(Baseline = true)]
    public long SimpleStudents() => Count(Students, EntailmentRegime.Simple);

    [Benchmark]
    public long RdfsStudents() => Count(Students, EntailmentRegime.Rdfs);

    /// <summary>Two levels of rdfs:subClassOf above the asserted type.</summary>
    [Benchmark]
    public long SimpleAgents() => Count(Agents, EntailmentRegime.Simple);

    [Benchmark]
    public long RdfsAgents() => Count(Agents, EntailmentRegime.Rdfs);

    /// <summary>One rdfs:subPropertyOf below the predicate asked.</summary>
    [Benchmark]
    public long RdfsKnows() => Count(Knows, EntailmentRegime.Rdfs);

    /// <summary>rdfs:member: each rdf:_n the data holds is found by the container-membership scan.</summary>
    [Benchmark]
    public long SimpleMembers() => Count(Members, EntailmentRegime.Simple);

    [Benchmark]
    public long RdfsMembers() => Count(Members, EntailmentRegime.Rdfs);

    /// <summary>The rewriting alone, the schema saturated and the tables built, nothing evaluated.</summary>
    [Benchmark]
    public int RewriteAgents() => EntailmentRewriter.Rewrite(_queries[Agents], _view!, _covers, EntailmentRegime.Rdfs).Query.GetHashCode();

    private long Count(string text, EntailmentRegime regime)
    {
        EntailedQuery entailed = EntailmentRewriter.Rewrite(_queries[text], _view!, _covers, regime);
        using QueryResults results = new SparqlEvaluator().Evaluate(entailed.Query, entailed.Source);
        SolutionResults solutions = (SolutionResults)results;
        return solutions.MoveNext() && solutions.TryGetTerm(new ColumnIndex(0), out RdfTerm? n) ? long.Parse(Encoding.UTF8.GetString(n!.Lexical), System.Globalization.CultureInfo.InvariantCulture) : -1;
    }

    private static void Expect(long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"A count is {actual}, where the data entails {expected}.");
        }
    }

    private static void Add(CommitRequest request, RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? graph)
    {
        if (graph is null)
        {
            request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o));
        }
        else
        {
            request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(graph));
        }
    }

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + local));

    public void Dispose()
    {
        _view?.Dispose();
        _store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
