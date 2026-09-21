using System.IO;
using BenchmarkDotNet.Attributes;
using VDS.RDF;
using VDS.RDF.Parsing;
using Varve.Rdf;
using Varve.Turtle;

// Both libraries call their N-Quads parser NQuadsParser, which is the one place
// a benchmark comparing two of anything is guaranteed to collide.
using VarveParser = Varve.Turtle.NQuadsParser;

namespace Varve.Benchmarks;

/// <summary>
/// Reading 100,000 N-Quads, against dotNetRDF.
/// </summary>
/// <remarks>
/// <para>
/// The two are not doing the same work, and the report has to say so rather
/// than let a ratio imply otherwise. Varve hands out views over its own buffer
/// and allocates nothing; dotNetRDF builds an object graph of
/// <see cref="INode"/> instances and interns them in a
/// <see cref="TripleStore"/>. The second benchmark here is the fair comparison
/// — Varve materialising a term per position — and the first is what the
/// streaming path actually costs.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private static long sink;

    private readonly ParseOptions _options = new() { Syntax = RdfSyntax.NQuads };

    /// <summary>Varve, view path: the parser's own buffer, nothing owned.</summary>
    [Benchmark(Baseline = true, Description = "Varve — views")]
    public long Varve_Views() =>
        VarveParser.Parse(
            Dataset.Utf8,
            static (in QuadView quad) => sink += quad.Subject.Lexical.Length + quad.Object.Lexical.Length,
            _options).QuadCount;

    /// <summary>Varve, materialising a term per position. The like-for-like run.</summary>
    [Benchmark(Description = "Varve — owned terms")]
    public long Varve_Materialised() =>
        VarveParser.Parse(
            Dataset.Utf8,
            static (in QuadView quad) =>
            {
                RdfTerm subject = quad.Subject.Materialise();
                RdfTerm predicate = quad.Predicate.Materialise();
                RdfTerm obj = quad.Object.Materialise();
                sink += subject.Kind == predicate.Kind ? obj.Lexical.Length : 1;
            },
            _options).QuadCount;

    /// <summary>Varve, interning into a dataset. The closest thing to a store.</summary>
    [Benchmark(Description = "Varve — into InMemoryDataset")]
    public int Varve_Interned()
    {
        InMemoryDataset dataset = new();

        VarveParser.Parse(
            Dataset.Utf8,
            (in QuadView quad) => dataset.Add(
                quad.Subject.Materialise(),
                quad.Predicate.Materialise(),
                quad.Object.Materialise(),
                quad.HasGraph ? quad.Graph.Materialise() : null),
            _options);

        return dataset.Count;
    }

    /// <summary>dotNetRDF, building its object graph.</summary>
    [Benchmark(Description = "dotNetRDF — TripleStore")]
    public long DotNetRdf()
    {
        TripleStore store = new();
        VDS.RDF.Parsing.NQuadsParser parser = new(NQuadsSyntax.Rdf11);
        using StringReader reader = new(Dataset.Text);
        parser.Load(store, reader);

        long count = 0;

        foreach (IGraph graph in store.Graphs)
        {
            count += graph.Triples.Count;
        }

        return count;
    }
}
