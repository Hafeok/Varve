// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// Inference, part 1 (#170): what a vocabulary redefinition costs a dataset
/// that is written to (ADR 0193 point 2). One commit to a data graph of N
/// typed people, then one query under RDFS against that graph, through a
/// schema cache: with no redefinition, with <c>rdf:type rdfs:subPropertyOf
/// ex:classifiedAs</c> in the data graph, and with it in the TBox graph.
/// </summary>
/// <remarks>
/// A commit changes the data graph's version, so its schema is saturated
/// again on the next query. Without a redefinition that is the probes; with
/// one, wherever it is held, it is a read of the scoping graph whole, since
/// the scoping graph is the data graph merged with its TBox.
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(2)]
[IterationCount(8)]
[InvocationCount(16)]
public class FullReadBenchmarks : IDisposable
{
    private const string Ex = "http://example.org/inference/";
    private const string Ask = "PREFIX ex: <" + Ex + ">\nASK { GRAPH ex:data { ex:p0 a ex:Person } }";

    private static readonly RdfTerm Tbox = Iri("tbox");
    private static readonly RdfTerm Data = Iri("data");
    private static readonly RdfTerm Type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
    private static readonly RdfTerm SubClassOf = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8);
    private static readonly RdfTerm SubPropertyOf = RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subPropertyOf"u8);

    private StoreDatasetType? _store;
    private SchemaCache _schemas = new();
    private TBoxCover[] _covers = [];
    private Varve.Sparql.Algebra.Query? _query;
    private int _commits;

    /// <summary>The people in the data graph.</summary>
    [Params(10_000, 100_000)]
    public int People { get; set; }

    /// <summary>Where <c>rdf:type rdfs:subPropertyOf ex:classifiedAs</c> is held, if anywhere.</summary>
    [Params("none", "data", "tbox")]
    public string Redefinition { get; set; } = "none";

    [GlobalSetup]
    public void Setup()
    {
        CommitRequest request = new();
        Add(request, Iri("Student"), SubClassOf, Iri("Person"), Tbox);
        if (Redefinition != "none")
        {
            Add(request, Type, SubPropertyOf, Iri("classifiedAs"), Redefinition == "data" ? Data : Tbox);
        }

        for (int i = 0; i < People; i++)
        {
            Add(request, Iri("p" + i), Type, Iri("Student"), Data);
        }

        _store = StoreDatasetType.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();
        _ = _store.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        _covers = [new TBoxCover([Tbox], GraphScope.All)];
        _query = Varve.Sparql.SparqlParser.ParseQuery(Ask.AsSpan());
        _schemas = new SchemaCache();
        if (!CommitThenAsk())
        {
            throw new InvalidOperationException("ex:p0 is a Person under RDFS.");
        }

        using DatasetView view = _store.Pin();
        Console.WriteLine($"// {People:N0} people, redefinition in {Redefinition}: full reads {Show(EntailmentRewriter.FullReads(view, _covers, _schemas))}");
    }

    /// <summary>One commit to the data graph, then one query under RDFS against it.</summary>
    [Benchmark]
    public bool CommitThenAsk()
    {
        CommitRequest request = new();
        Add(request, Iri("q" + _commits++), Type, Iri("Student"), Data);
        _ = _store!.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        using DatasetView view = _store.Pin();
        EntailedQuery entailed = EntailmentRewriter.Rewrite(_query!, view, _covers, EntailmentRegime.Rdfs, schemas: _schemas);
        using QueryResults results = new SparqlEvaluator().Evaluate(entailed.Query, entailed.Source);
        return ((BooleanResult)results).Value;
    }

    [GlobalCleanup]
    public void Cleanup() =>
        Console.WriteLine($"// {People:N0} people, redefinition in {Redefinition}: the schema cache answered {_schemas.Hits.Value:N0} lookups and saturated {_schemas.Misses.Value:N0}");

    private static string Show(FullReadGraphs reads)
    {
        List<string> names = [];
        foreach (RdfTerm? graph in reads.Graphs)
        {
            names.Add(graph is null ? "the default graph" : Encoding.UTF8.GetString(graph.Lexical));
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static void Add(CommitRequest request, RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm graph) =>
        request.Assert(RequestTerm.FromTerm(s), RequestTerm.FromTerm(p), RequestTerm.FromTerm(o), RequestTerm.FromTerm(graph));

    private static RdfTerm Iri(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + local));

    public void Dispose()
    {
        _store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
