using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Drives the scanner over one span: statement, emit, repeat.
/// </summary>
/// <remarks>
/// The whole of ADR 0030's recovery rule lives here. A statement's quads are
/// released together on success and dropped together on failure, so a blank
/// node property list that had already produced triples leaves nothing behind.
/// </remarks>
internal static class TurtleEngine
{
    /// <summary>
    /// Parses the complete statements in <paramref name="data"/> and returns
    /// how many bytes were consumed. With <paramref name="final"/> false a
    /// trailing incomplete statement is left for the caller to complete.
    /// </summary>
    internal static int Drain(
        System.ReadOnlySpan<byte> data,
        bool final,
        QuadHandler handler,
        in TurtleOptions options,
        TurtleState state,
        ref ParseState result)
    {
        TurtleScanner scanner = new(data, state, in options, mayGrow: !final);
        int consumed = 0;

        while (!result.Stop)
        {
            StatementStatus status = scanner.Next();

            if (status == StatementStatus.EndOfInput)
            {
                consumed = scanner.Consumed;
                break;
            }

            if (status == StatementStatus.Incomplete)
            {
                if (!final)
                {
                    break;
                }

                Reject(ParseErrorKind.UnexpectedEnd, scanner, data, in options, ref result);
                consumed = data.Length;
                break;
            }

            if (status == StatementStatus.Error)
            {
                state.Discard();
                Reject(scanner.Error, scanner, data, in options, ref result);

                if (result.Stop)
                {
                    consumed = scanner.Consumed;
                    break;
                }

                // ADR 0030: resume after the next '.' at depth zero, outside a
                // String and an IRIREF.
                if (!scanner.Resynchronise())
                {
                    consumed = data.Length;
                    break;
                }

                consumed = scanner.Consumed;
                continue;
            }

            state.CompleteStatement();
            Emit(data, handler, state, scanner.Graph, ref result);
            consumed = scanner.Consumed;
        }

        return consumed;
    }

    private static void Emit(
        System.ReadOnlySpan<byte> data,
        QuadHandler handler,
        TurtleState state,
        int graph,
        ref ParseState result)
    {
        for (int i = 0; i < state.PendingCount; i++)
        {
            TurtleState.PendingQuad pending = state.At(i);
            int quadGraph = pending.Graph >= 0 ? pending.Graph : graph;

            QuadView quad = state.Arena.Quad(data, pending.Subject, pending.Predicate, pending.Object, quadGraph);
            handler(in quad);
            result.QuadCount++;
        }

        state.Discard();
    }

    private static void Reject(
        ParseErrorKind kind,
        scoped in TurtleScanner scanner,
        System.ReadOnlySpan<byte> data,
        in TurtleOptions options,
        ref ParseState result)
    {
        int offset = kind == ParseErrorKind.UnexpectedEnd ? data.Length : scanner.ErrorOffset;
        ParsePosition position = Position(data, offset);
        ParseError error = new(kind, position, scanner.IriError);

        result.ErrorCount++;

        if (result.ErrorCount == 1)
        {
            result.FirstError = error;
        }

        ErrorHandler? onError = options.OnError;
        result.Stop = onError is null || onError(in error) == ErrorAction.Stop;
    }

    /// <summary>
    /// Line and column for a byte offset. Counted from the start of the span
    /// each time rather than tracked, because a Turtle statement may span any
    /// number of lines and an error is rare — paying for it only when one
    /// happens is cheaper than maintaining a counter through every term.
    /// </summary>
    private static ParsePosition Position(System.ReadOnlySpan<byte> data, int offset)
    {
        int line = 1;
        int lineStart = 0;

        for (int i = 0; i < offset && i < data.Length; i++)
        {
            if (data[i] == (byte)'\n' || (data[i] == (byte)'\r' && (i + 1 >= data.Length || data[i + 1] != (byte)'\n')))
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new ParsePosition(offset, line, offset - lineStart + 1);
    }
}
