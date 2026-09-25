// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.Turtle.Tests;

/// <summary>
/// Allocation per quad is a defect, and the assertion is <strong>exactly
/// zero</strong> rather than a small number.
/// </summary>
/// <remarks>
/// <para>
/// A parse is measured twice, over a small document and a large one, after
/// warming up on both. The difference is what the extra quads cost, and it is
/// asserted to be zero. That is a sharper claim than an absolute figure: it
/// cannot be met by a parser that allocates a little per quad and is measured
/// on a short document, and it is not confused by the fixed cost of setting a
/// parse up.
/// </para>
/// <para>
/// The fixed cost is asserted separately, so that it is a number somebody can
/// read rather than something subtracted out of sight. It is one
/// <see cref="TermArena"/> and, on the streaming paths, the object the test
/// itself hands in.
/// </para>
/// <para>
/// Every handler is <c>static</c> and closes over nothing but static state. A
/// handler that captured a local would allocate a closure per call, which is
/// not the parser's doing, and the test avoids it rather than subtracting it.
/// </para>
/// </remarks>
public class AllocationTests
{
    private const int Small = 500;
    private const int Large = 4_000;

    private static readonly byte[] SmallDocument = Document(Small);
    private static readonly byte[] LargeDocument = Document(Large);
    private static readonly ReadOnlySequence<byte> SmallSequence = Fragments.Fragmented(SmallDocument, 16);
    private static readonly ReadOnlySequence<byte> LargeSequence = Fragments.Fragmented(LargeDocument, 16);
    private static readonly ParseOptions Options = new() { Syntax = RdfSyntax.NQuads };
    private static readonly WriteOptions Writing = new() { Syntax = RdfSyntax.NQuads };
    private static readonly Harness.ArrayBufferWriter Output = new();

    private static long counter;
    private static long quadsSeen;

    private static readonly QuadHandler Count = static (in QuadView quad) =>
    {
        quadsSeen++;
        counter += quad.Subject.Lexical.Length
            + quad.Predicate.Lexical.Length
            + quad.Object.Lexical.Length
            + quad.Object.Language.Length
            + (quad.HasGraph ? quad.Graph.Lexical.Length : 0);
    };

    private static readonly QuadHandler WriteBack = static (in QuadView quad) =>
    {
        quadsSeen++;
        NQuadsWriter.Write(Output, in quad, Writing);
    };

    private static byte[] Document(int quads)
    {
        StringBuilder builder = new();

        for (int i = 0; i < quads; i++)
        {
            builder.Append("<http://example.org/subject/").Append(i)
                .Append("> <http://example.org/predicate> \"value ").Append(i)
                .Append(" with an escape \\u00E9 and a \\\"quote\\\"\"@en <http://example.org/graph> .\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Runs the small and the large parse and reports what each allocated, in
    /// rounds until one reproduces the last (<see cref="AllocationMeter"/>):
    /// the first large parse grows every buffer to the size it needs, so the
    /// round after it is the first that can repeat.
    /// </summary>
    private static (long Fixed, long ForExtraQuads) Profile(Action<bool> parse)
    {
        (long small, long large) = AllocationMeter.MeasurePair(
            () =>
            {
                parse(false);
                return null;
            },
            () =>
            {
                parse(true);
                return null;
            });

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
    public void the_span_path_allocates_nothing_per_quad()
    {
        AssertZeroPerQuad(
            static large => NQuadsParser.Parse(large ? LargeDocument : SmallDocument, Count, Options),
            fixedCostLimit: 2048);
    }

    [Fact]
    public void the_sequence_path_allocates_nothing_per_quad()
    {
        // Sixteen-byte segments, so every single line crosses one and is
        // copied into the line buffer. This is the worst case, not the easy one.
        AssertZeroPerQuad(
            static large =>
            {
                ReadOnlySequence<byte> sequence = large ? LargeSequence : SmallSequence;
                NQuadsParser.Parse(in sequence, Count, Options);
            },
            fixedCostLimit: 2048);
    }

    [Fact]
    public void the_stream_path_allocates_nothing_per_quad()
    {
        AssertZeroPerQuad(
            static large =>
            {
                using MemoryStream stream = new(large ? LargeDocument : SmallDocument, writable: false);
                NQuadsParser.Parse(stream, Count, Options);
            },
            fixedCostLimit: 2048);
    }

    [Fact]
    public void the_pull_reader_allocates_nothing_per_quad()
    {
        AssertZeroPerQuad(
            static large =>
            {
                NQuadsReader reader = new(large ? LargeDocument : SmallDocument, Options);

                while (reader.Read())
                {
                    quadsSeen++;
                    counter += reader.Current.Object.Lexical.Length;
                }
            },
            fixedCostLimit: 2048);
    }

    [Fact]
    public void writing_allocates_nothing_per_quad()
    {
        AssertZeroPerQuad(
            static large =>
            {
                Output.Reset();
                NQuadsParser.Parse(large ? LargeDocument : SmallDocument, WriteBack, Options);
            },
            fixedCostLimit: 2048);
    }

    [Fact]
    public void the_fixed_cost_of_a_parse_is_one_arena()
    {
        // Named so the figure in the other tests has something to be compared
        // against. An arena is a slot array and a scratch buffer; both are
        // reused for every line of the parse they belong to.
        TermArena arena = new();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        long before = GC.GetAllocatedBytesForCurrentThread();
        arena = new TermArena();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, arena.Count);
        Assert.True(
            allocated is > 0 and < 2048,
            string.Create(CultureInfo.InvariantCulture, $"an arena costs {allocated} bytes"));
    }
}
