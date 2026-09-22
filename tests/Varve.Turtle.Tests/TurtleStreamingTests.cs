// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

/// <summary>
/// Turtle across chunk boundaries. Where N-Triples could buffer a line, Turtle
/// buffers a statement, and a statement has no upper bound — so the interesting
/// cases are the ones where a construct is cut in half by the reader's segment
/// size rather than by a newline.
/// </summary>
public class TurtleStreamingTests
{
    /// <summary>
    /// A document whose statements deliberately span lines, nest, and contain
    /// every construct whose scanner keeps state across bytes: a long string, an
    /// escape, a collection, a nested property list and a graph block.
    /// </summary>
    private static string Document(int statements, bool trig)
    {
        StringBuilder text = new();
        text.Append("@prefix p: <http://a/> .\n@base <http://a/x/> .\n");

        for (int i = 0; i < statements; i++)
        {
            if (trig)
            {
                text.Append("p:g").Append(i).Append("\n{\n");
            }

            text.Append("p:s").Append(i).Append("\n  p:p \"\"\"a \\u00E9 \"quoted\"\n  and wrapped\"\"\"@en-GB ,\n")
                .Append("      1.5e3 ;\n  p:q [ p:r ( <rel").Append(i).Append("> p:t ) ] ;\n  a p:C .\n");

            if (trig)
            {
                text.Append("}\n");
            }
        }

        return text.ToString();
    }

    private static TurtleOptions Options(RdfSyntax syntax) => new()
    {
        Syntax = syntax,
        BaseIri = U("http://a/doc"),
    };

    private static List<Row> ViaSpan(byte[] bytes, in TurtleOptions options)
    {
        List<Row> rows = [];
        TurtleParser.Parse(bytes, rows.Collect(), in options);
        return rows;
    }

    private static List<Row> ViaSequence(byte[] bytes, in TurtleOptions options, int segmentSize)
    {
        List<Row> rows = [];
        ReadOnlySequence<byte> sequence = Fragments.Fragmented(bytes, segmentSize);
        TurtleParser.Parse(in sequence, rows.Collect(), in options);
        return rows;
    }

    private static List<Row> ViaStream(byte[] bytes, in TurtleOptions options, int drip)
    {
        List<Row> rows = [];
        TurtleParser.Parse(new Fragments.DripStream(bytes, drip), rows.Collect(), in options);
        return rows;
    }

    private static async Task<List<Row>> ViaStreamAsync(byte[] bytes, TurtleOptions options, int drip)
    {
        List<Row> rows = [];
        await TurtleParser.ParseAsync(new Fragments.DripStream(bytes, drip), rows.Collect(), options);
        return rows;
    }

    private static async Task<List<Row>> ViaPipeAsync(byte[] bytes, TurtleOptions options, int segmentSize)
    {
        Pipe pipe = new();
        List<Row> rows = [];

        Task writing = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += segmentSize)
            {
                int length = Math.Min(segmentSize, bytes.Length - i);
                await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(bytes, i, length));
            }

            await pipe.Writer.CompleteAsync();
        });

        await TurtleParser.ParseAsync(pipe.Reader, rows.Collect(), options);
        await writing;
        return rows;
    }

    [Theory]
    [InlineData(RdfSyntax.Turtle, 1)]
    [InlineData(RdfSyntax.Turtle, 5)]
    [InlineData(RdfSyntax.TriG, 1)]
    [InlineData(RdfSyntax.TriG, 5)]
    public async Task every_entry_point_reads_the_same_quads(RdfSyntax syntax, int statements)
    {
        byte[] bytes = U(Document(statements, syntax == RdfSyntax.TriG));
        TurtleOptions options = Options(syntax);

        List<Row> expected = ViaSpan(bytes, in options);

        // Nine per statement: two objects, the four collection triples, the
        // rdf:first link into the list, the property-list link, and rdf:type.
        Assert.Equal(9 * statements, expected.Count);

        foreach (int segment in (int[])[1, 2, 3, 13, 16, 64, 997])
        {
            Assert.Equal(expected, ViaSequence(bytes, in options, segment));
            Assert.Equal(expected, ViaStream(bytes, in options, segment));
            Assert.Equal(expected, await ViaStreamAsync(bytes, options, segment));
            Assert.Equal(expected, await ViaPipeAsync(bytes, options, segment));
        }
    }

    [Fact]
    public async Task a_statement_longer_than_the_buffer_still_reads()
    {
        // One collection of twenty thousand members is a single statement, so
        // the arena has to grow to hold it rather than flushing at a newline.
        StringBuilder text = new("<http://a/s> <http://a/p> (");

        for (int i = 0; i < 20_000; i++)
        {
            text.Append(" <http://a/").Append(i).Append('>');
        }

        text.Append(" ) .\n");
        byte[] bytes = U(text.ToString());

        int expected = 2 * 20_000 + 1;
        TurtleOptions options = default;

        Assert.Equal(expected, ViaSpan(bytes, in options).Count);
        Assert.Equal(expected, ViaStream(bytes, in options, 997).Count);
        Assert.Equal(expected, (await ViaStreamAsync(bytes, options, 997)).Count);
        Assert.Equal(expected, ViaSequence(bytes, in options, 997).Count);
        Assert.Equal(expected, (await ViaPipeAsync(bytes, options, 997)).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    public void a_long_string_cut_at_every_offset_still_reads(int segmentSize)
    {
        // The long-form quote run is the scanner's most stateful loop: """a""b"""
        // ends on the third quote and not the first, and a chunk boundary can
        // fall anywhere inside the run.
        byte[] bytes = U("<http://a/s> <http://a/p> \"\"\"a\"b\"\"c\"\"\" .\n");
        TurtleOptions options = default;

        List<Row> rows = ViaSequence(bytes, in options, segmentSize);

        Assert.Single(rows);
        Assert.Equal("a\"b\"\"c", S(rows[0].Object.Lexical));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(11)]
    public void an_escape_cut_at_every_offset_still_reads(int segmentSize)
    {
        // \U0001F600 is ten bytes; a boundary inside it is truncation, not a
        // malformed escape, and the two must not be confused.
        byte[] bytes = U("<http://a/s> <http://a/p> \"\\U0001F600\\u00E9\" .\n");
        TurtleOptions options = default;

        List<Row> rows = ViaSequence(bytes, in options, segmentSize);

        Assert.Single(rows);
        Assert.Equal("\U0001F600é", S(rows[0].Object.Lexical));
    }

    [Fact]
    public void a_directive_split_across_a_boundary_still_binds()
    {
        byte[] bytes = U("@prefix p: <http://a/> .\np:s p:p p:o .\n");
        TurtleOptions options = default;

        for (int segment = 1; segment <= 8; segment++)
        {
            List<Row> rows = ViaSequence(bytes, in options, segment);

            Assert.True(rows.Count == 1, $"segment size {segment} read {rows.Count} quads");
            Assert.Equal("http://a/s", S(rows[0].Subject.Lexical));
        }
    }

    [Fact]
    public void an_incomplete_final_statement_is_an_error_and_not_silence()
    {
        // Truncation must not look like a clean end of input just because the
        // chunk loop ran out of chunks.
        byte[] bytes = U("<http://a/s> <http://a/p> <http://a/o> .\n<http://a/s> <http://a/p>");
        TurtleOptions options = default;

        List<Row> rows = [];
        ParseResult result = TurtleParser.Parse(
            new Fragments.DripStream(bytes, 3), rows.Collect(), in options);

        Assert.False(result.Succeeded);
        Assert.Equal(ParseErrorKind.UnexpectedEnd, result.FirstError.Kind);
        Assert.Single(rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    public void blank_node_labels_do_not_depend_on_how_the_input_arrived(int segmentSize)
    {
        // A statement that outran its chunk is scanned again from the start, so
        // without rewinding the counter the same document would name its blank
        // nodes differently for every delivery, and a byte-at-a-time read would
        // number them in the thousands.
        byte[] bytes = U("<http://a/s> <http://a/p> [ <http://a/q> ( <http://a/1> [] ) ] .\n"
            + "<http://a/t> <http://a/p> [] .\n");
        TurtleOptions options = default;

        List<string> whole = ViaSpan(bytes, in options).ConvertAll(Label);
        List<string> split = ViaSequence(bytes, in options, segmentSize).ConvertAll(Label);

        Assert.Equal(whole, split);
        Assert.Contains(whole, label => label.Contains("g0", StringComparison.Ordinal));
    }

    private static string Label(Row row) =>
        string.Join('|', S(row.Subject.Lexical), S(row.Predicate.Lexical), S(row.Object.Lexical));

    [Fact]
    public async Task an_empty_input_succeeds_on_every_entry_point()
    {
        byte[] bytes = [];
        TurtleOptions options = default;

        Assert.Empty(ViaSpan(bytes, in options));
        Assert.Empty(ViaSequence(bytes, in options, 1));
        Assert.Empty(ViaStream(bytes, in options, 1));
        Assert.Empty(await ViaStreamAsync(bytes, options, 1));
        Assert.Empty(await ViaPipeAsync(bytes, options, 1));
    }

    /// <summary>
    /// One statement, many quads. This is the whole difference between the two
    /// pull readers, so it is asserted on the shape that has the most of them:
    /// a property list and a collection inside one statement.
    /// </summary>
    [Fact]
    public void the_pull_reader_hands_out_one_statements_quads_one_at_a_time()
    {
        byte[] bytes = U("<http://a/s> <http://a/p> [ <http://a/q> ( <http://a/1> <http://a/2> ) ] .\n");
        TurtleOptions options = default;

        List<string> pushed = ViaSpan(bytes, in options).ConvertAll(Label);
        List<string> pulled = [];

        TurtleReader reader = new(bytes, in options);

        while (reader.Read())
        {
            pulled.Add(Label(new Row(
                reader.Current.Subject.Materialise(),
                reader.Current.Predicate.Materialise(),
                reader.Current.Object.Materialise(),
                reader.Current.HasGraph ? reader.Current.Graph.Materialise() : null)));
        }

        Assert.Equal(pushed, pulled);
        Assert.Equal(6, pulled.Count);
        Assert.Equal(6, reader.Result.QuadCount);
        Assert.True(reader.Result.Succeeded);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    public void the_pull_reader_reads_a_fragmented_sequence_the_same_way(int segmentSize)
    {
        byte[] bytes = U(Document(8, trig: false));
        TurtleOptions options = default;

        List<string> pushed = ViaSpan(bytes, in options).ConvertAll(Label);
        List<string> pulled = [];

        ReadOnlySequence<byte> sequence = Fragments.Fragmented(bytes, segmentSize);
        TurtleReader reader = new(in sequence, in options);

        while (reader.Read())
        {
            pulled.Add(Label(new Row(
                reader.Current.Subject.Materialise(),
                reader.Current.Predicate.Materialise(),
                reader.Current.Object.Materialise(),
                reader.Current.HasGraph ? reader.Current.Graph.Materialise() : null)));
        }

        Assert.Equal(pushed, pulled);
    }

    /// <summary>
    /// A rejected statement leaves nothing behind, including the quads its
    /// blank node property list had already produced. That is ADR 0030, and it
    /// has to hold on the pull path too — which it does because the recovery
    /// is <c>TurtleEngine.Step</c>'s and not a second copy.
    /// </summary>
    [Fact]
    public void the_pull_reader_drops_a_whole_rejected_statement_and_carries_on()
    {
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;
        TurtleOptions options = new() { OnError = handler };

        TurtleReader reader = new(
            U("<http://a/s> <http://a/p> [ <http://a/q> <http://a/r> ] ; <http://a/bad> .\n"
                + "<http://a/t> <http://a/p> <http://a/o> .\n"),
            in options);

        List<string> pulled = [];

        while (reader.Read())
        {
            pulled.Add(S(reader.Current.Subject.Lexical));
        }

        Assert.Equal(["http://a/t"], pulled);
        Assert.Equal(1, reader.Result.QuadCount);
        Assert.Equal(1, reader.Result.ErrorCount);
    }

    [Fact]
    public void the_pull_reader_stops_at_an_error_and_says_why()
    {
        TurtleOptions options = default;
        TurtleReader reader = new(
            U("<http://a/s> <http://a/p> <http://a/o> .\n<http://a/s> <http://a/p> ;\n"),
            in options);

        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.Equal(ParseErrorKind.ExpectedObject, reader.Error.Kind);
        Assert.Equal(2, reader.Error.Position.Line);
    }

    /// <summary>
    /// <c>Error</c> is the rejection that ended the read, not the first one the
    /// document contained. With recovery on, those differ.
    /// </summary>
    /// <remarks>
    /// Both statements carry their own terminating dot, because recovery
    /// resynchronises at the next one: a first statement with no dot would
    /// swallow the second and there would be only one error to tell apart.
    /// </remarks>
    [Fact]
    public void the_pull_reader_reports_the_error_that_ended_it()
    {
        int seen = 0;

        ErrorHandler handler = (in ParseError error) =>
        {
            seen++;
            return seen == 1 ? ErrorAction.Continue : ErrorAction.Stop;
        };

        TurtleOptions options = new() { OnError = handler };
        TurtleReader reader = new(
            U("<http://a/s> <http://a/p> \"x\"^^ .\n<http://a/s> <http://a/p> \"y\"^^ .\n"),
            in options);

        Assert.False(reader.Read());
        Assert.Equal(2, reader.Result.ErrorCount);
        Assert.Equal(1, reader.Result.FirstError.Position.Line);
        Assert.Equal(2, reader.Error.Position.Line);
    }

    /// <summary>
    /// A document with CR LF line endings reports the same error position
    /// however it was split — including a split that falls between the CR and
    /// the LF.
    /// </summary>
    /// <remarks>
    /// This is the chunk-boundary rule applied to the line ending itself, and
    /// it is the one case the conformance oracle could not reach on Linux: the
    /// W3C suite files check out with LF there, so no CR LF pair exists to be
    /// cut in half. On Windows, where git checks them out as CR LF, the oracle
    /// found it at once — 163 cases reporting a line one too high. The test is
    /// here, on a document this repository owns, so the answer no longer
    /// depends on how a contributor's git is configured.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(11)]
    public void a_crlf_document_reports_the_same_error_line_however_it_is_split(int segmentSize)
    {
        byte[] bytes = U("@prefix p: <http://a/> .\r\n<http://a/s> <http://a/p> ;\r\n");
        TurtleOptions options = default;

        ParseError whole = FirstError(ViaSpanResult(bytes, in options));
        ParseError split = FirstError(ViaSequenceResult(bytes, in options, segmentSize));

        Assert.Equal(2, whole.Position.Line);
        Assert.Equal(whole.Position.Line, split.Position.Line);
        Assert.Equal(whole.Position.Column, split.Position.Column);
        Assert.Equal(whole.Position.ByteOffset, split.Position.ByteOffset);
        Assert.Equal(whole.Kind, split.Kind);
    }

    /// <summary>Every offset, which is what the conformance oracle does.</summary>
    [Fact]
    public void a_crlf_document_reports_the_same_error_line_at_every_offset()
    {
        byte[] bytes = U(
            "@prefix p: <http://a/> .\r\n"
            + "p:s p:p [ p:q ( p:1 p:2 ) ] .\r\n"
            + "p:s p:p \"x\"\r\n"
            + "  , \"y\" ;\r\n");
        TurtleOptions options = default;

        ParseError whole = FirstError(ViaSpanResult(bytes, in options));

        for (int at = 0; at <= bytes.Length; at++)
        {
            ReadOnlySequence<byte> sequence = Fragments.Split(bytes, at);
            ParseError split = FirstError(ViaSequenceParse(in sequence, in options));

            Assert.Equal(whole.Position.Line, split.Position.Line);
            Assert.Equal(whole.Position.Column, split.Position.Column);
            Assert.Equal(whole.Position.ByteOffset, split.Position.ByteOffset);
        }
    }

    private static ParseError FirstError(ParseResult result)
    {
        Assert.False(result.Succeeded);
        return result.FirstError;
    }

    private static ParseResult ViaSpanResult(byte[] bytes, in TurtleOptions options) =>
        TurtleParser.Parse(bytes, Ignore, in options);

    private static ParseResult ViaSequenceResult(byte[] bytes, in TurtleOptions options, int segmentSize)
    {
        ReadOnlySequence<byte> sequence = Fragments.Fragmented(bytes, segmentSize);
        return TurtleParser.Parse(in sequence, Ignore, in options);
    }

    private static ParseResult ViaSequenceParse(in ReadOnlySequence<byte> sequence, in TurtleOptions options) =>
        TurtleParser.Parse(in sequence, Ignore, in options);

    private static void Ignore(in Varve.Rdf.QuadView quad)
    {
    }

    [Fact]
    public void the_pull_reader_reads_trig_graph_blocks()
    {
        TurtleOptions options = new() { Syntax = RdfSyntax.TriG };
        TurtleReader reader = new(
            U("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }\n"),
            in options);

        Assert.True(reader.Read());
        Assert.True(reader.Current.HasGraph);
        Assert.Equal("http://a/g", S(reader.Current.Graph.Lexical));
        Assert.False(reader.Read());
        Assert.True(reader.Result.Succeeded);
    }
}
