// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Reads N-Triples and N-Quads, handing each quad to a callback.
/// </summary>
/// <remarks>
/// <para>
/// The push shape, and the one that streams. A quad arrives as a
/// <see cref="QuadView"/> whose spans point into the parser's own buffer and
/// are valid only for the duration of the call; keeping one past that is
/// prevented by the type system, because a <c>ref struct</c> cannot be captured
/// or stored.
/// </para>
/// <para>
/// Steady state allocates nothing. The parse unit is a line, a line inside one
/// buffer is parsed in place, and a line that crosses one is copied into a
/// buffer that stops growing once it has seen the longest line.
/// </para>
/// </remarks>
public static class NQuadsParser
{
    private const int DefaultBufferSize = 64 * 1024;

    /// <summary>Parses a whole document held in memory.</summary>
    public static ParseResult Parse(ReadOnlySpan<byte> utf8, QuadHandler handler, in ParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ParseState state = ParseState.New();
        ParseEngine.DrainSpan(utf8, final: true, handler, in options, new TermArena(), ref state);
        return state.ToResult();
    }

    /// <summary>Parses a whole document held as a sequence of buffers.</summary>
    public static ParseResult Parse(in ReadOnlySequence<byte> utf8, QuadHandler handler, in ParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ParseState state = ParseState.New();
        ParseEngine.DrainSequence(
            in utf8, final: true, handler, in options, new TermArena(), new LineBuffer(), ref state);

        return state.ToResult();
    }

    /// <summary>Parses a stream, reading it in chunks.</summary>
    public static ParseResult Parse(Stream stream, QuadHandler handler, in ParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(handler);

        ChunkParser parser = new(handler, options);

        try
        {
            while (true)
            {
                int read = stream.Read(parser.Free);

                if (!parser.Drain(read, final: read == 0))
                {
                    break;
                }
            }

            return parser.Result;
        }
        finally
        {
            parser.Return();
        }
    }

    /// <summary>Parses a stream asynchronously.</summary>
    public static ValueTask<ParseResult> ParseAsync(Stream stream, QuadHandler handler, ParseOptions options) =>
        ParseAsync(stream, handler, options, CancellationToken.None);

    /// <summary>Parses a stream asynchronously.</summary>
    public static async ValueTask<ParseResult> ParseAsync(
        Stream stream,
        QuadHandler handler,
        ParseOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(handler);

        ChunkParser parser = new(handler, options);

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(parser.FreeMemory, cancellationToken).ConfigureAwait(false);

                if (!parser.Drain(read, final: read == 0))
                {
                    break;
                }
            }

            return parser.Result;
        }
        finally
        {
            parser.Return();
        }
    }

    /// <summary>
    /// Parses a pipe. Lines are read from the pipe's own buffers, so a line
    /// that does not cross a segment is never copied.
    /// </summary>
    public static ValueTask<ParseResult> ParseAsync(PipeReader reader, QuadHandler handler, ParseOptions options) =>
        ParseAsync(reader, handler, options, CancellationToken.None);

    /// <summary>
    /// Parses a pipe. Lines are read from the pipe's own buffers, so a line
    /// that does not cross a segment is never copied.
    /// </summary>
    public static async ValueTask<ParseResult> ParseAsync(
        PipeReader reader,
        QuadHandler handler,
        ParseOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(handler);

        ParseState state = ParseState.New();
        TermArena arena = new();
        LineBuffer buffer = new();

        while (true)
        {
            ReadResult read = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> data = read.Buffer;

            SequencePosition consumed = ParseEngine.DrainSequence(
                in data, read.IsCompleted, handler, in options, arena, buffer, ref state);

            reader.AdvanceTo(consumed, data.End);

            if (read.IsCompleted || state.Stop)
            {
                break;
            }
        }

        return state.ToResult();
    }

    /// <summary>
    /// A pooled buffer that keeps the tail of an incomplete line across reads.
    /// </summary>
    private sealed class ChunkParser
    {
        private readonly QuadHandler _handler;
        private readonly ParseOptions _options;
        private readonly TermArena _arena = new();
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        private ParseState _state = ParseState.New();
        private int _filled;

        internal ChunkParser(QuadHandler handler, ParseOptions options)
        {
            _handler = handler;
            _options = options;
        }

        internal Span<byte> Free => _buffer.AsSpan(_filled);

        internal Memory<byte> FreeMemory => _buffer.AsMemory(_filled);

        internal ParseResult Result => _state.ToResult();

        /// <summary>
        /// Parses what is now in the buffer. Returns false when the caller
        /// should stop reading.
        /// </summary>
        internal bool Drain(int read, bool final)
        {
            _filled += read;

            int consumed = ParseEngine.DrainSpan(
                _buffer.AsSpan(0, _filled), final, _handler, in _options, _arena, ref _state);

            _buffer.AsSpan(consumed, _filled - consumed).CopyTo(_buffer);
            _filled -= consumed;

            if (final || _state.Stop)
            {
                return false;
            }

            if (_filled == _buffer.Length)
            {
                Grow();
            }

            return true;
        }

        internal void Return()
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        /// <summary>
        /// Doubles the buffer, for a document whose lines are longer than the
        /// buffer is. A line is the parse unit, so one has to fit.
        /// </summary>
        private void Grow()
        {
            byte[] bigger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
            _buffer.AsSpan(0, _filled).CopyTo(bigger);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }
    }
}
