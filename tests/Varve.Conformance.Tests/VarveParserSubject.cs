using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>Varve.Turtle</c> as the harness needs to see it.
/// </summary>
/// <remarks>
/// <para>
/// The adapter wraps the real API rather than the real API being shaped to
/// suit the harness, which is what <see cref="IParserSubject"/> says it is for.
/// A syntax test is one small document, accepted or rejected, so the whole file
/// goes in at once; <see cref="ParseSplit"/> is the same parse with the bytes
/// handed over in two pieces.
/// </para>
/// <para>
/// Quads are collected even for a syntax test. They cost one materialisation
/// per quad on files of a few lines, they are what an evaluation test compares,
/// and they mean a failure report can say what was parsed rather than only that
/// something was.
/// </para>
/// </remarks>
internal sealed class VarveParserSubject : IParserSubject
{
    [ModuleInitializer]
    internal static void Register() => ParserSubjects.Current = new VarveParserSubject();

    public ParseOutcome Parse(RdfFormat format, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Collector collector = new();

        ParseResult result = IsLineBased(format)
            ? NQuadsParser.Parse(bytes, collector.Handler, LineOptions(format))
            : TurtleParser.Parse(bytes, collector.Handler, TurtleOptionsFor(format));

        return Outcome(result, collector);
    }

    public ParseOutcome ParseSplit(RdfFormat format, string path, int at)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Collector collector = new();
        ReadOnlySequence<byte> sequence = Split(bytes, at);

        ParseResult result = IsLineBased(format)
            ? NQuadsParser.Parse(in sequence, collector.Handler, LineOptions(format))
            : TurtleParser.Parse(in sequence, collector.Handler, TurtleOptionsFor(format));

        return Outcome(result, collector);
    }

    private static bool IsLineBased(RdfFormat format) =>
        format is RdfFormat.NTriples or RdfFormat.NQuads;

    private static ParseOptions LineOptions(RdfFormat format) => new()
    {
        Syntax = format == RdfFormat.NQuads ? RdfSyntax.NQuads : RdfSyntax.NTriples,
    };

    private static TurtleOptions TurtleOptionsFor(RdfFormat format) => new()
    {
        Syntax = format == RdfFormat.TriG ? RdfSyntax.TriG : RdfSyntax.Turtle,
    };

    private static ParseOutcome Outcome(ParseResult result, Collector collector) =>
        result.Succeeded
            ? ParseOutcome.Parsed(collector.Quads)
            : ParseOutcome.Rejected(result.FirstError.ToString());

    /// <summary>
    /// <paramref name="bytes"/> as two segments meeting at <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// Always two segments, even when the split is at 0 or at the end: a
    /// sequence with one segment takes the span fast path, and the point here
    /// is to exercise the one that stitches.
    /// </remarks>
    private static ReadOnlySequence<byte> Split(byte[] bytes, int at)
    {
        Segment first = new(new ReadOnlyMemory<byte>(bytes, 0, at), 0);
        Segment second = first.Append(new ReadOnlyMemory<byte>(bytes, at, bytes.Length - at));
        return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
    }

    private sealed class Collector
    {
        private readonly ArrayBufferWriter _output = new();
        private readonly WriteOptions _writeOptions = new() { Syntax = RdfSyntax.NQuads };

        internal List<string> Quads { get; } = [];

        internal QuadHandler Handler => Collect;

        private void Collect(in QuadView quad)
        {
            _output.Reset();
            NQuadsWriter.Write(_output, in quad, _writeOptions);
            Quads.Add(Encoding.UTF8.GetString(_output.Written).TrimEnd('\n'));
        }
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
