using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>What a parse has accumulated so far.</summary>
internal struct ParseState
{
    internal long QuadCount;
    internal long ErrorCount;
    internal ParseError FirstError;
    internal bool Stop;

    /// <summary>
    /// The most recent rejection, which is the one a pull reader has to report:
    /// <c>Read</c> returned false because of <em>this</em> error, not because
    /// of the first one the document contained.
    /// </summary>
    internal ParseError LastError;

    /// <summary>The byte offset of the next line, from the start of the input.</summary>
    internal long Offset;

    /// <summary>The number of the next line, counting from one.</summary>
    internal int Line;

    internal static ParseState New() => new() { Line = 1 };

    internal readonly ParseResult ToResult() => new(QuadCount, ErrorCount, FirstError);
}

/// <summary>
/// Line splitting and dispatch, shared by every entry point.
/// </summary>
/// <remarks>
/// <strong>N-Triples needs no chunk-boundary rule</strong>, unlike Turtle
/// (`turtle.md` §8). A line is only dispatched once its terminating newline has
/// been found, or at the end of the document, so <see cref="LineParser"/> never
/// sees a fragment and no token can be decided on too few bytes. The
/// conformance project's chunk-boundary oracle measures that rather than taking
/// it on trust: all 213 N-Triples and N-Quads manifest inputs give the same
/// answer parsed whole and parsed split at every byte offset.
/// </remarks>
/// <remarks>
/// <para>
/// There are two line loops because there are two shapes of input, and neither
/// reduces to the other without cost: a span is walked with an index, and a
/// sequence is walked with a <see cref="SequenceReader{T}"/> and copies a line
/// only when it crosses a segment. Both hand the line to the same
/// <see cref="ProcessLine"/>, and a test parses one document through both to
/// prove they agree.
/// </para>
/// <para>
/// <c>EOL ::= [#xD#xA]+</c> is one separator however long the run, so blank
/// lines fall out for free. The reported line number still advances once per
/// break, with <c>\r\n</c> counted as one, because that is what an editor
/// shows.
/// </para>
/// </remarks>
internal static class ParseEngine
{
    internal static void ProcessLine(
        ReadOnlySpan<byte> line,
        long lineOffset,
        int lineNumber,
        QuadHandler handler,
        in ParseOptions options,
        TermArena arena,
        ref ParseState state)
    {
        arena.Reset();
        LineParser parser = new(line, arena, options.Syntax, options.ValidateIris);

        switch (parser.Parse(out int subject, out int predicate, out int obj, out int graph))
        {
            case LineStatus.Empty:
                return;

            case LineStatus.Quad:
                QuadView quad = arena.Quad(line, subject, predicate, obj, graph);
                handler(in quad);
                state.QuadCount++;
                return;

            default:
                Reject(parser.Error, parser.IriError, parser.ErrorOffset, lineOffset, lineNumber, options, ref state);
                return;
        }
    }

    internal static void Reject(
        ParseErrorKind kind,
        Varve.Iri.IriErrorKind iri,
        int column,
        long lineOffset,
        int lineNumber,
        in ParseOptions options,
        ref ParseState state)
    {
        ParseError error = new(kind, new ParsePosition(lineOffset + column, lineNumber, column + 1), iri);
        state.ErrorCount++;
        state.LastError = error;

        if (state.ErrorCount == 1)
        {
            state.FirstError = error;
        }

        ErrorHandler? onError = options.OnError;
        state.Stop = onError is null || onError(in error) == ErrorAction.Stop;
    }

    /// <summary>
    /// Parses the complete lines in <paramref name="data"/> and returns how
    /// many bytes were consumed. With <paramref name="final"/> false a trailing
    /// line with no terminator is left unconsumed for the caller to complete.
    /// </summary>
    internal static int DrainSpan(
        ReadOnlySpan<byte> data,
        bool final,
        QuadHandler handler,
        in ParseOptions options,
        TermArena arena,
        ref ParseState state)
    {
        int at = 0;

        while (at < data.Length && !state.Stop)
        {
            int end = at;

            while (end < data.Length && !NTriplesChars.IsEol(data[end]))
            {
                end++;
            }

            if (end == data.Length && !final)
            {
                break;
            }

            int lineNumber = state.Line;
            long lineOffset = state.Offset;
            ProcessLine(data[at..end], lineOffset, lineNumber, handler, in options, arena, ref state);

            int next = end;

            while (next < data.Length && NTriplesChars.IsEol(data[next]))
            {
                next += data[next] == (byte)'\r' && next + 1 < data.Length && data[next + 1] == (byte)'\n' ? 2 : 1;
                state.Line++;
            }

            state.Offset += next - at;
            at = next;
        }

        return at;
    }

    /// <summary>
    /// The same over a sequence, returning where to resume. A line inside one
    /// segment is parsed in place; only a line that crosses one is copied.
    /// </summary>
    internal static SequencePosition DrainSequence(
        in ReadOnlySequence<byte> data,
        bool final,
        QuadHandler handler,
        in ParseOptions options,
        TermArena arena,
        LineBuffer buffer,
        ref ParseState state)
    {
        SequenceReader<byte> reader = new(data);

        while (!reader.End && !state.Stop)
        {
            SequencePosition lineStart = reader.Position;
            ReadOnlySequence<byte> lineData;

            if (reader.TryReadToAny(out ReadOnlySequence<byte> terminated, "\r\n"u8, advancePastDelimiter: false))
            {
                lineData = terminated;
            }
            else
            {
                if (!final)
                {
                    return lineStart;
                }

                lineData = reader.UnreadSequence;
                reader.AdvanceToEnd();
            }

            ReadOnlySpan<byte> line = lineData.IsSingleSegment ? lineData.FirstSpan : buffer.Copy(lineData);
            ProcessLine(line, state.Offset, state.Line, handler, in options, arena, ref state);

            long breakBytes = 0;

            while (reader.TryPeek(out byte b) && NTriplesChars.IsEol(b))
            {
                if (b == (byte)'\r' && reader.TryPeek(1, out byte next) && next == (byte)'\n')
                {
                    reader.Advance(2);
                    breakBytes += 2;
                }
                else
                {
                    reader.Advance(1);
                    breakBytes += 1;
                }

                state.Line++;
            }

            state.Offset += line.Length + breakBytes;
        }

        return reader.Position;
    }
}
