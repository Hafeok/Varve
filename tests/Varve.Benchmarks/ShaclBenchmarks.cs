// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Store;
using Varve.Turtle;
using VDS.RDF;
using DnrShapes = VDS.RDF.Shacl.ShapesGraph;
using DnrReport = VDS.RDF.Shacl.Validation.Report;
using StoreDatasetType = Varve.Store.Dataset;
using VarveShapes = Varve.Shacl.ShapesGraph;

namespace Varve.Benchmarks;

/// <summary>
/// SHACL, part 1 (#120): one data graph of people and companies, validated by
/// Varve.Shacl and by dotNetRDF's validator (ADR 0156) against the same
/// shapes, SHACL Core components both implement.
/// </summary>
/// <remarks>
/// About one person in fifty has a lower-case name, one age in sixteen is
/// out of range, and every hundredth person knows a company: so the report
/// has results, and both engines must agree on how many before anything is
/// timed. The shapes are read inside the measurement for both, as a
/// validation from text would; the data is loaded outside it.
/// <para>
/// Varve validates the store's pinned view, an indexed source. Over an
/// <see cref="InMemoryDataset"/>, which answers every match by a scan, the
/// same validation is quadratic in the data; it is checked once in the setup,
/// at the smallest size, and not timed.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[WarmupCount(1)]
[IterationCount(5)]
[InvocationCount(1, 1)]
public class ShaclBenchmarks : IDisposable
{
    internal static readonly ImmutableArray<int> Sizes = [10_000, 100_000, 1_000_000];

    internal const string Shapes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.org/shacl/> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
        ex:PersonShape a sh:NodeShape ;
            sh:targetClass ex:Person ;
            sh:property [ sh:path ex:name ; sh:minCount 1 ; sh:maxCount 1 ; sh:datatype xsd:string ; sh:pattern "^[A-Z]" ] ;
            sh:property [ sh:path ex:age ; sh:maxCount 1 ; sh:datatype xsd:integer ; sh:minInclusive 0 ; sh:maxInclusive 150 ] ;
            sh:property [ sh:path ex:email ; sh:nodeKind sh:Literal ; sh:maxCount 2 ] ;
            sh:property [ sh:path ex:knows ; sh:class ex:Person ] ;
            sh:property [ sh:path ex:worksFor ; sh:minCount 1 ; sh:node ex:CompanyShape ] .
        ex:CompanyShape a sh:NodeShape ;
            sh:targetClass ex:Company ;
            sh:property [ sh:path ex:label ; sh:minCount 1 ; sh:datatype xsd:string ] .
        """;

    private const string Ex = "http://example.org/shacl/";

    private InMemoryDataset? _data;
    private StoreDatasetType? _store;
    private DatasetView? _view;
    private Graph? _graph;
    private Graph? _shapesGraph;

    [ParamsSource(nameof(Counts))]
    public int Triples { get; set; }

    public static IEnumerable<int> Counts() => Sizes;

    /// <summary>The data as Turtle: about six triples a person, a company per hundred people.</summary>
    internal static string Data(int triples)
    {
        int people = triples / 6;
        int companies = Math.Max(1, people / 100);
        StringBuilder text = new("@prefix ex: <" + Ex + "> .\n");
        for (int c = 0; c < companies; c++)
        {
            text.Append("ex:c").Append(c).Append(" a ex:Company ; ex:label \"Company ").Append(c).Append("\" .\n");
        }

        for (int i = 0; i < people; i++)
        {
            text.Append("ex:p").Append(i).Append(" a ex:Person ; ex:name \"").Append(i % 50 == 7 ? "name" : "Name").Append(i).Append("\" ; ex:age ")
                .Append(i % 166).Append(" ; ex:email \"p").Append(i).Append("@example.org\" ; ex:knows ")
                .Append(i % 100 == 3 ? "ex:c" + (i % companies) : "ex:p" + ((i * 7) + 1) % people)
                .Append(" ; ex:worksFor ex:c").Append(i % companies).Append(" .\n");
        }

        return text.ToString();
    }

    [GlobalSetup]
    public void Setup()
    {
        string data = Data(Triples);
        _data = Parse(data);
        _store = StoreDatasetType.CreateAsync(new MemoryStorage(), new Varve.Store.Log.DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();
        global::Varve.Store.Log.CommitRequestBuilder request = new();
        using (IQuadCursor cursor = _data.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph))
        {
            while (cursor.MoveNext())
            {
                global::Varve.Rdf.Quad quad = cursor.Current;
                _data.TryExternalise(quad.Subject, out RdfTerm? s);
                _data.TryExternalise(quad.Predicate, out RdfTerm? p);
                _data.TryExternalise(quad.Object, out RdfTerm? o);
                request.Assert(global::Varve.Store.Log.RequestTerm.FromTerm(s!), global::Varve.Store.Log.RequestTerm.FromTerm(p!), global::Varve.Store.Log.RequestTerm.FromTerm(o!));
            }
        }

        _ = _store.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        _view = _store.Pin();

        _graph = new Graph();
        new VDS.RDF.Parsing.TurtleParser().Load(_graph, new StringReader(data));
        _shapesGraph = new Graph();
        new VDS.RDF.Parsing.TurtleParser().Load(_shapesGraph, new StringReader(Shapes));

        // Both engines, once, before anything is timed: the same verdict and the same number of results.
        int varve = Varve(), dotNetRdf = DotNetRdf();
        int inMemory = Triples <= Sizes[0] ? Validate(_data) : varve;
        Console.WriteLine($"// {Triples:N0} triples: Varve {varve} results, over InMemoryDataset {inMemory}, dotNetRDF {dotNetRdf}");
        if (varve != dotNetRdf || inMemory != varve || varve == 0)
        {
            throw new InvalidOperationException($"The engines disagree: Varve {varve}, over InMemoryDataset {inMemory}, dotNetRDF {dotNetRdf}.");
        }

        _data = null;
    }

    [Benchmark]
    public int Varve() => Validate(_view!);

    [Benchmark(Baseline = true)]
    public int DotNetRdf()
    {
        DnrReport report = new DnrShapes(_shapesGraph!).Validate(_graph!);
        return report.Conforms ? 0 : report.Results.Count;
    }

    private static int Validate(IQuadSource data)
    {
        VarveShapes shapes = VarveShapes.Load(Parse(Shapes), GraphPattern.DefaultGraph).Shapes!;
        Varve.Shacl.Model.ValidationReport report = ShaclValidator.Validate(shapes, data, GraphPattern.DefaultGraph).Report!;
        return report.Conforms ? 0 : report.Results.Count;
    }

    private static InMemoryDataset Parse(string turtle)
    {
        InMemoryDatasetBuilder builder = new();
        ParseResult parsed = global::Varve.Turtle.TurtleParser.Parse(
            Encoding.UTF8.GetBytes(turtle),
            (in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException(parsed.FirstError.ToString());
        }

        return builder.ToDataset();
    }

    public void Dispose()
    {
        _view?.Dispose();
        _store?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _graph?.Dispose();
        _shapesGraph?.Dispose();
        GC.SuppressFinalize(this);
    }
}
