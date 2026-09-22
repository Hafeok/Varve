using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.Turtle.Tests;

/// <summary>
/// Turtle and TriG allocate nothing per quad.
/// </summary>
/// <remarks>
/// <para>
/// Measured as a difference between two documents, as <c>n-triples.md</c> §6
/// requires: an absolute number measures the harness as much as the parser, and
/// the difference cancels it. The small and large documents differ only in how
/// many statements they hold, so whatever the large one costs beyond the small
/// one is what the extra quads cost.
/// </para>
/// <para>
/// Turtle is the harder case and these are the paths that make it so: a
/// statement rather than a line is the buffering unit (ADR 0030), the arena
/// holds a statement's quads until its terminating dot, and the fragmented
/// paths stitch a statement across segments. The 16-byte segment size is
/// deliberately smaller than any statement here, so every statement crosses at
/// least one boundary and the copy-and-retry path runs on all of them.
/// </para>
/// </remarks>
public class TurtleAllocationTests
{
    private const int Small = 64;
    private const int Large = 640;

    /// <summary>
    /// What one parse may cost regardless of how long the document is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The measured figure is about 6.3 KB for the span path and 6.6 KB for
    /// the chunked ones, against N-Triples' 2 KB, and the difference is the
    /// point of ADR 0030 rather than a defect: the arena holds a whole
    /// <em>statement</em> and not a line, so it grows to the largest statement
    /// in the document (`turtle.md` §8). The statements here carry a
    /// predicate-object list, an object list, a nested blank node property
    /// list and a collection, which is a good deal more than one line's worth
    /// of terms.
    /// </para>
    /// <para>
    /// <strong>It is fixed, and that is what the test is about.</strong> The
    /// limit catches a cost that starts scaling with the document; the
    /// per-quad assertion beside it is the zero. The headroom over the
    /// measurement is deliberate — a limit set at the measurement fails on an
    /// unrelated runtime change and teaches everyone to raise it.
    /// </para>
    /// </remarks>
    private const int FixedCostLimit = 8192;

    private static readonly byte[] SmallDocument = Document(Small, trig: false);
    private static readonly byte[] LargeDocument = Document(Large, trig: false);
    private static readonly byte[] SmallTriG = Document(Small, trig: true);
    private static readonly byte[] LargeTriG = Document(Large, trig: true);
    private static readonly ReadOnlySequence<byte> SmallSequence = Fragments.Fragmented(SmallDocument, 16);
    private static readonly ReadOnlySequence<byte> LargeSequence = Fragments.Fragmented(LargeDocument, 16);
    private static readonly Harness.ArrayBufferWriter Output = new();

    private static long counter;
    private static long quadsSeen;

    private static readonly QuadHandler Count = static (in QuadView quad) =>
    {
        counter += quad.Subject.Lexical.Length
            + quad.Predicate.Lexical.Length
            + quad.Object.Lexical.Length;
        quadsSeen++;
    };

    /// <summary>
    /// Four statements per iteration, each exercising a construct that carries
    /// state across the statement: a predicate-object list, an object list, a
    /// blank node property list, a collection, an escape and a long string.
    /// </summary>
    private static byte[] Document(int statements, bool trig)
    {
        StringBuilder builder = new();
        builder.Append("@prefix p: <http://example.org/> .\n");

        for (int i = 0; i < statements; i++)
        {
            if (trig)
            {
                builder.Append("p:g").Append(i.ToString("D6", CultureInfo.InvariantCulture)).Append(" {\n");
            }

            // Fixed-width indices, so that every statement in both documents
            // is the same length. Otherwise the large document's terms are a
            // few bytes longer, its arena grows further, and the difference
            // between the two parses reports that one-off growth as a
            // per-quad cost.
            string n = i.ToString("D6", CultureInfo.InvariantCulture);

            builder.Append("p:s").Append(n)
                .Append(" p:p \"value ").Append(n).Append(" with an escape \\u00E9\"@en , 1.5e3 ;\n")
                .Append("  p:q [ p:r ( <rel").Append(n).Append("> p:t ) ] ;\n")
                .Append("  a p:C .\n");

            if (trig)
            {
                builder.Append("}\n");
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static TurtleOptions Options(RdfSyntax syntax) => new()
    {
        Syntax = syntax,
        BaseIri = Encoding.UTF8.GetBytes("http://example.org/base/"),
    };

    private static (long Fixed, long ForExtraQuads) Profile(Action<bool> parse)
    {
        for (int i = 0; i < 2; i++)
        {
            parse(true);
            parse(false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        long before = GC.GetAllocatedBytesForCurrentThread();
        parse(false);
        long small = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        parse(true);
        long large = GC.GetAllocatedBytesForCurrentThread() - before;

        return (small, large - small);
    }

    private static void AssertZeroPerQuad(Action<bool> parse, long fixedCostLimit)
    {
        quadsSeen = 0;
        (long fixedCost, long extra) = Profile(parse);

        Assert.True(
            quadsSeen > Large,
            string.Create(CultureInfo.InvariantCulture, $"the parse did not run: {quadsSeen} quads seen"));

        Assert.Equal(0, extra);

        Assert.True(
            fixedCost <= fixedCostLimit,
            string.Create(CultureInfo.InvariantCulture, $"fixed cost {fixedCost} exceeds {fixedCostLimit}"));
    }

    [Fact]
    public void the_span_path_allocates_nothing_per_quad() =>
        AssertZeroPerQuad(
            static large =>
            {
                TurtleOptions options = Options(RdfSyntax.Turtle);
                TurtleParser.Parse(large ? LargeDocument : SmallDocument, Count, in options);
            },
            fixedCostLimit: FixedCostLimit);

    [Fact]
    public void the_trig_span_path_allocates_nothing_per_quad() =>
        AssertZeroPerQuad(
            static large =>
            {
                TurtleOptions options = Options(RdfSyntax.TriG);
                TurtleParser.Parse(large ? LargeTriG : SmallTriG, Count, in options);
            },
            fixedCostLimit: FixedCostLimit);

    [Fact]
    public void the_sequence_path_allocates_nothing_per_quad() =>
        AssertZeroPerQuad(
            static large =>
            {
                TurtleOptions options = Options(RdfSyntax.Turtle);
                ReadOnlySequence<byte> sequence = large ? LargeSequence : SmallSequence;
                TurtleParser.Parse(in sequence, Count, in options);
            },
            fixedCostLimit: FixedCostLimit);

    [Fact]
    public void the_stream_path_allocates_nothing_per_quad() =>
        AssertZeroPerQuad(
            static large =>
            {
                TurtleOptions options = Options(RdfSyntax.Turtle);
                using MemoryStream stream = new(large ? LargeDocument : SmallDocument, writable: false);
                TurtleParser.Parse(stream, Count, in options);
            },
            fixedCostLimit: FixedCostLimit);

    [Fact]
    public void the_writer_allocates_nothing_per_quad() =>
        AssertZeroPerQuad(
            static large =>
            {
                TurtleOptions read = Options(RdfSyntax.Turtle);
                TurtleWriteOptions write = default;
                Output.Reset();

                using TurtleWriter writer = new(Output, in write);
                TurtleParser.Parse(
                    large ? LargeDocument : SmallDocument,
                    (in QuadView quad) =>
                    {
                        writer.Write(in quad);
                        quadsSeen++;
                    },
                    in read);
            },
            fixedCostLimit: FixedCostLimit);

    [Fact]
    public void a_statement_crossing_every_segment_boundary_costs_nothing_extra()
    {
        // The property the 16-byte segments are for: a statement here is
        // hundreds of bytes, so each is stitched from a dozen or more pieces,
        // and the stitching must not scale with the number of quads.
        quadsSeen = 0;
        counter = 0;

        (long _, long extra) = Profile(static large =>
        {
            TurtleOptions options = Options(RdfSyntax.Turtle);
            ReadOnlySequence<byte> sequence = large ? LargeSequence : SmallSequence;
            TurtleParser.Parse(in sequence, Count, in options);
        });

        Assert.Equal(0, extra);
    }

    [Fact]
    public void the_fragmented_parse_reads_the_same_quads_as_the_whole_one()
    {
        // Guards the measurement itself: a parse that stopped early would
        // allocate nothing per quad by having no quads.
        quadsSeen = 0;
        TurtleOptions options = Options(RdfSyntax.Turtle);
        TurtleParser.Parse(LargeDocument, Count, in options);
        long whole = quadsSeen;

        quadsSeen = 0;
        ReadOnlySequence<byte> sequence = LargeSequence;
        TurtleParser.Parse(in sequence, Count, in options);

        Assert.Equal(whole, quadsSeen);
        Assert.Equal(Large * 9, whole);
    }
}
