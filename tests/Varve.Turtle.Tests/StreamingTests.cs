using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

/// <summary>
/// The five entry points read the same document the same way.
/// </summary>
/// <remarks>
/// There are two line loops behind them — an indexed walk over a span and a
/// <see cref="SequenceReader{T}"/> walk over a sequence — so this is the test
/// that keeps them from drifting apart. Every case is driven through all five.
/// </remarks>
public class StreamingTests
{
    private static string Document(int lines)
    {
        System.Text.StringBuilder builder = new();

        for (int i = 0; i < lines; i++)
        {
            builder.Append("<http://a/s").Append(i).Append("> <http://a/p> \"value ")
                .Append(i).Append(" \\u00E9\" .\n");
        }

        return builder.ToString();
    }

    private static List<Row> ViaSpan(byte[] bytes, ParseOptions options)
    {
        List<Row> rows = [];
        NQuadsParser.Parse(bytes, rows.Collect(), options);
        return rows;
    }

    private static List<Row> ViaSequence(byte[] bytes, ParseOptions options, int segmentSize)
    {
        List<Row> rows = [];
        ReadOnlySequence<byte> sequence = Fragments.Fragmented(bytes, segmentSize);
        NQuadsParser.Parse(in sequence, rows.Collect(), options);
        return rows;
    }

    private static List<Row> ViaStream(byte[] bytes, ParseOptions options)
    {
        List<Row> rows = [];
        NQuadsParser.Parse(new Fragments.DripStream(bytes, 7), rows.Collect(), options);
        return rows;
    }

    private static async Task<List<Row>> ViaStreamAsync(byte[] bytes, ParseOptions options)
    {
        List<Row> rows = [];
        await NQuadsParser.ParseAsync(new Fragments.DripStream(bytes, 7), rows.Collect(), options);
        return rows;
    }

    private static async Task<List<Row>> ViaPipeAsync(byte[] bytes, ParseOptions options, int segmentSize)
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

        await NQuadsParser.ParseAsync(pipe.Reader, rows.Collect(), options);
        await writing;
        return rows;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(50)]
    public async Task every_entry_point_reads_the_same_quads(int lines)
    {
        byte[] bytes = U(Document(lines));
        ParseOptions options = default;

        List<Row> expected = ViaSpan(bytes, options);

        Assert.Equal(lines, expected.Count);
        Assert.Equal(expected, ViaSequence(bytes, options, 1));
        Assert.Equal(expected, ViaSequence(bytes, options, 13));
        Assert.Equal(expected, ViaStream(bytes, options));
        Assert.Equal(expected, await ViaStreamAsync(bytes, options));
        Assert.Equal(expected, await ViaPipeAsync(bytes, options, 1));
        Assert.Equal(expected, await ViaPipeAsync(bytes, options, 13));
    }

    [Fact]
    public async Task a_line_longer_than_the_buffer_still_reads()
    {
        string document = "<http://a/s> <http://a/p> \"" + new string('x', 200_000) + "\" .\n";
        byte[] bytes = U(document);

        Assert.Single(ViaStream(bytes, default));
        Assert.Single(await ViaStreamAsync(bytes, default));
        Assert.Single(ViaSequence(bytes, default, 997));
        Assert.Single(await ViaPipeAsync(bytes, default, 997));
    }

    [Fact]
    public void the_pull_reader_reads_the_same_quads_as_the_push_parser()
    {
        byte[] bytes = U(Document(20));
        List<Row> expected = ViaSpan(bytes, default);
        List<Row> pulled = [];

        NQuadsReader reader = new(bytes, default);

        while (reader.Read())
        {
            pulled.Add(new Row(
                reader.Current.Subject.Materialise(),
                reader.Current.Predicate.Materialise(),
                reader.Current.Object.Materialise(),
                reader.Current.HasGraph ? reader.Current.Graph.Materialise() : null));
        }

        Assert.Equal(expected, pulled);
        Assert.Equal(20, reader.Result.QuadCount);
        Assert.True(reader.Result.Succeeded);
    }

    [Fact]
    public void the_pull_reader_reads_a_fragmented_sequence_the_same_way()
    {
        byte[] bytes = U(Document(20));
        List<Row> expected = ViaSpan(bytes, default);
        List<Row> pulled = [];

        ReadOnlySequence<byte> sequence = Fragments.Fragmented(bytes, 3);
        NQuadsReader reader = new(in sequence, default);

        while (reader.Read())
        {
            pulled.Add(new Row(
                reader.Current.Subject.Materialise(),
                reader.Current.Predicate.Materialise(),
                reader.Current.Object.Materialise(),
                reader.Current.HasGraph ? reader.Current.Graph.Materialise() : null));
        }

        Assert.Equal(expected, pulled);
    }

    [Fact]
    public void the_pull_reader_stops_at_an_error_and_says_why()
    {
        NQuadsReader reader = new(U("<http://a/s> <http://a/p> <http://a/o> .\nbroken\n"), default);

        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.Equal(ParseErrorKind.ExpectedSubject, reader.Error.Kind);
        Assert.Equal(2, reader.Error.Position.Line);
    }

    [Fact]
    public void the_pull_reader_carries_on_when_the_handler_says_so()
    {
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;
        NQuadsReader reader = new(
            U("broken\n<http://a/s> <http://a/p> <http://a/o> .\n"),
            new ParseOptions { OnError = handler });

        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.Equal(1, reader.Result.QuadCount);
        Assert.Equal(1, reader.Result.ErrorCount);
    }
}
