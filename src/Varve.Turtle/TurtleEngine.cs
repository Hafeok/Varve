// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
            if (Step(ref scanner, data, final, in options, state, ref result, ref consumed)
                != StatementStatus.Complete)
            {
                break;
            }

            Emit(data, handler, state, scanner.Graph, ref result);
            consumed = scanner.Consumed;
        }

        state.Advance(data[..consumed]);
        return consumed;
    }

    /// <summary>
    /// Advances the scanner to the next statement, applying ADR 0030's recovery
    /// rule to any it has to reject on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of the reader loop that is not emitting, and it is
    /// here rather than in each caller because there are two: the push parsers
    /// drain a buffer through <see cref="Drain"/>, and <see cref="TurtleReader"/>
    /// suspends between statements so the caller can take one quad at a time.
    /// A second copy of the recovery rule is exactly the thing it would be
    /// possible to fix in one place and not the other.
    /// </para>
    /// <para>
    /// On <see cref="StatementStatus.Complete"/> the state's pending quads are
    /// the statement's, and <paramref name="consumed"/> is left for the caller
    /// to advance once it has finished with them — the quads point into
    /// <paramref name="data"/>, so a buffer cannot be compacted until they are
    /// released. Every other outcome sets it before returning.
    /// </para>
    /// </remarks>
    internal static StatementStatus Step(
        ref TurtleScanner scanner,
        System.ReadOnlySpan<byte> data,
        bool final,
        in TurtleOptions options,
        TurtleState state,
        ref ParseState result,
        ref int consumed)
    {
        while (true)
        {
            StatementStatus status = scanner.Next();

            if (status == StatementStatus.EndOfInput)
            {
                consumed = scanner.Consumed;
                return status;
            }

            if (status == StatementStatus.Incomplete)
            {
                if (!final)
                {
                    return status;
                }

                Reject(ParseErrorKind.UnexpectedEnd, scanner, data, in options, state, ref result);
                consumed = data.Length;
                return StatementStatus.Error;
            }

            if (status == StatementStatus.Error)
            {
                state.Discard();
                Reject(scanner.Error, scanner, data, in options, state, ref result);

                if (result.Stop)
                {
                    consumed = scanner.Consumed;
                    return StatementStatus.Error;
                }

                // ADR 0030: resume after the next '.' at depth zero, outside a
                // String and an IRIREF.
                if (!scanner.Resynchronise())
                {
                    consumed = data.Length;
                    return StatementStatus.EndOfInput;
                }

                consumed = scanner.Consumed;
                continue;
            }

            state.CompleteStatement();
            return StatementStatus.Complete;
        }
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
        TurtleState state,
        ref ParseState result)
    {
        int offset = kind == ParseErrorKind.UnexpectedEnd ? data.Length : scanner.ErrorOffset;
        ParsePosition position = Position(data, offset, state);
        ParseError error = new(kind, position, scanner.IriError);

        result.ErrorCount++;
        result.LastError = error;

        if (result.ErrorCount == 1)
        {
            result.FirstError = error;
        }

        ErrorHandler? onError = options.OnError;
        result.Stop = onError is null || onError(in error) == ErrorAction.Stop;
    }

    /// <summary>
    /// The document position of a byte offset within this buffer.
    /// </summary>
    /// <remarks>
    /// Counted from the start of the buffer each time rather than tracked
    /// through every term, because a Turtle statement may span any number of
    /// lines and an error is rare. The buffer's own place in the document comes
    /// from the state, so the position is the document's and not the
    /// fragment's.
    /// </remarks>
    private static ParsePosition Position(System.ReadOnlySpan<byte> data, int offset, TurtleState state)
    {
        int line = state.LineNumber;
        long lineStart = state.LineStart;
        bool afterCarriageReturn = state.AfterCarriageReturn;

        for (int i = 0; i < offset && i < data.Length; i++)
        {
            TurtleState.CountLineBreak(
                data[i], state.DocumentOffset + i, ref line, ref lineStart, ref afterCarriageReturn);
        }

        long absolute = state.DocumentOffset + offset;
        return new ParsePosition(absolute, line, (int)(absolute - lineStart) + 1);
    }
}
