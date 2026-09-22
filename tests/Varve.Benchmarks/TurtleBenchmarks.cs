using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using VDS.RDF;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Benchmarks;

/// <summary>
/// Reading Turtle, against dotNetRDF on the same document.
/// </summary>
/// <remarks>
/// <para>
/// The document is the interesting part. N-Quads is one statement per line and
/// measures the tokeniser; Turtle's cost is in what it adds — prefixed names to
/// expand, predicate-object and object lists to fan out, collections and blank
/// node property lists to build, a statement to buffer until its dot. The
/// dataset here carries all of them, so the number says something about
/// Turtle rather than about N-Quads with a prefix.
/// </para>
/// <para>
/// <strong>Not gating</strong> (ADR 0027). A benchmark that fails a build turns
/// a noisy measurement into a blocked merge, and this one shares a machine with
/// whatever else CI is doing. The claim it supports belongs in a report with
/// the machine stated, which is what <c>README.md</c> is for.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TurtleBenchmarks
{
    private static long sink;

    private readonly TurtleOptions _options = new()
    {
        Syntax = RdfSyntax.Turtle,
        BaseIri = System.Text.Encoding.UTF8.GetBytes("http://example.org/base/"),
    };

    [Benchmark(Baseline = true, Description = "Varve — views")]
    public long Varve_Views() =>
        TurtleParser.Parse(
            TurtleDataset.Utf8,
            static (in QuadView quad) => sink += quad.Subject.Lexical.Length + quad.Object.Lexical.Length,
            in _options).QuadCount;

    [Benchmark(Description = "Varve — owned terms")]
    public long Varve_Materialised() =>
        TurtleParser.Parse(
            TurtleDataset.Utf8,
            static (in QuadView quad) =>
            {
                RdfTerm subject = quad.Subject.Materialise();
                RdfTerm predicate = quad.Predicate.Materialise();
                RdfTerm obj = quad.Object.Materialise();
                sink += subject.Kind == predicate.Kind ? obj.Lexical.Length : 1;
            },
            in _options).QuadCount;

    [Benchmark(Description = "Varve — read and write back")]
    public int Varve_RoundTrip()
    {
        BenchmarkWriter output = new();
        TurtleWriteOptions write = default;

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix("p"u8, "http://example.org/"u8);
            TurtleParser.Parse(
                TurtleDataset.Utf8, (in QuadView quad) => writer.Write(in quad), in _options);
        }

        return output.Length;
    }

    [Benchmark(Description = "dotNetRDF — Graph")]
    public int DotNetRdf()
    {
        Graph graph = new() { BaseUri = new System.Uri("http://example.org/base/") };
        VDS.RDF.Parsing.TurtleParser parser = new();
        using StringReader reader = new(TurtleDataset.Text);
        parser.Load(graph, reader);

        return graph.Triples.Count;
    }

    /// <summary>A buffer writer that keeps only how much was written.</summary>
    private sealed class BenchmarkWriter : System.Buffers.IBufferWriter<byte>
    {
        private byte[] _bytes = new byte[1 << 20];

        internal int Length { get; private set; }

        public void Advance(int count) => Length += count;

        public System.Memory<byte> GetMemory(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _bytes.AsMemory(Length);
        }

        public System.Span<byte> GetSpan(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _bytes.AsSpan(Length);
        }

        private void Grow(int sizeHint)
        {
            int needed = Length + System.Math.Max(sizeHint, 1);

            if (needed > _bytes.Length)
            {
                System.Array.Resize(ref _bytes, System.Math.Max(needed, _bytes.Length * 2));
            }
        }
    }
}
