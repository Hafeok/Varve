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
/// Reads Turtle and TriG, handing each quad to a callback.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="NQuadsParser"/>, over a grammar that is not
/// line-based. The unit of work is a **statement** rather than a line, which
/// is what ADR 0030 decides and what makes recovery and buffering different
/// here: a statement's quads are held until its terminating <c>.</c> and
/// released together, so a failed statement emits nothing at all.
/// </para>
/// <para>
/// A quad arrives as a <see cref="QuadView"/> valid only for the duration of
/// the call.
/// </para>
/// </remarks>
public static class TurtleParser
{
    private const int DefaultBufferSize = 64 * 1024;

    /// <summary>Parses a whole document held in memory.</summary>
    public static ParseResult Parse(ReadOnlySpan<byte> utf8, QuadHandler handler, in TurtleOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        TurtleState state = NewState(in options);
        ParseState result = ParseState.New();
        TurtleEngine.Drain(utf8, final: true, handler, in options, state, ref result);
        return result.ToResult();
    }

    /// <summary>Parses a whole document held as a sequence of buffers.</summary>
    public static ParseResult Parse(in ReadOnlySequence<byte> utf8, QuadHandler handler, in TurtleOptions options)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (utf8.IsSingleSegment)
        {
            return Parse(utf8.FirstSpan, handler, in options);
        }

        // A statement is the unit, and a statement can cross any number of
        // segments, so the sequence is assembled into the same growable buffer
        // the stream path uses rather than walked segment by segment.
        ChunkParser parser = new(handler, options);

        try
        {
            foreach (ReadOnlyMemory<byte> segment in utf8)
            {
                if (!parser.Feed(segment.Span))
                {
                    return parser.Result;
                }
            }

            parser.Finish();
            return parser.Result;
        }
        finally
        {
            parser.Return();
        }
    }

    /// <summary>Parses a stream, reading it in chunks.</summary>
    public static ParseResult Parse(Stream stream, QuadHandler handler, in TurtleOptions options)
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
    public static ValueTask<ParseResult> ParseAsync(Stream stream, QuadHandler handler, TurtleOptions options) =>
        ParseAsync(stream, handler, options, CancellationToken.None);

    /// <summary>Parses a stream asynchronously.</summary>
    public static async ValueTask<ParseResult> ParseAsync(
        Stream stream,
        QuadHandler handler,
        TurtleOptions options,
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

    /// <summary>Parses a pipe.</summary>
    public static ValueTask<ParseResult> ParseAsync(PipeReader reader, QuadHandler handler, TurtleOptions options) =>
        ParseAsync(reader, handler, options, CancellationToken.None);

    /// <summary>Parses a pipe.</summary>
    public static async ValueTask<ParseResult> ParseAsync(
        PipeReader reader,
        QuadHandler handler,
        TurtleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(handler);

        ChunkParser parser = new(handler, options);

        try
        {
            while (true)
            {
                ReadResult read = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> data = read.Buffer;
                bool keepGoing = true;

                foreach (ReadOnlyMemory<byte> segment in data)
                {
                    if (!parser.Feed(segment.Span))
                    {
                        keepGoing = false;
                        break;
                    }
                }

                reader.AdvanceTo(data.End);

                if (!keepGoing)
                {
                    break;
                }

                if (read.IsCompleted)
                {
                    parser.Finish();
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

    internal static TurtleState NewState(in TurtleOptions options)
    {
        TurtleState state = new();

        if (!options.BaseIri.IsEmpty)
        {
            state.SetBase(options.BaseIri.Span);
        }

        return state;
    }

    /// <summary>
    /// A pooled buffer that keeps the tail of an incomplete statement across
    /// reads.
    /// </summary>
    /// <remarks>
    /// It grows to the longest <em>statement</em>, not the longest line
    /// (`turtle.md` §8). A collection of ten thousand elements is one
    /// statement, and that is the memory it costs.
    /// </remarks>
    private sealed class ChunkParser
    {
        private readonly QuadHandler _handler;
        private readonly TurtleOptions _options;
        private readonly TurtleState _state;
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        private ParseState _result = ParseState.New();
        private int _filled;

        internal ChunkParser(QuadHandler handler, TurtleOptions options)
        {
            _handler = handler;
            _options = options;
            _state = NewState(in options);
        }

        internal Span<byte> Free => _buffer.AsSpan(_filled);

        internal Memory<byte> FreeMemory => _buffer.AsMemory(_filled);

        internal ParseResult Result => _result.ToResult();

        /// <summary>Copies a segment in and parses what is now complete.</summary>
        internal bool Feed(ReadOnlySpan<byte> segment)
        {
            int at = 0;

            while (at < segment.Length)
            {
                int take = Math.Min(segment.Length - at, _buffer.Length - _filled);
                segment.Slice(at, take).CopyTo(_buffer.AsSpan(_filled));
                at += take;

                if (!Drain(take, final: false))
                {
                    return false;
                }
            }

            return true;
        }

        internal void Finish() => Drain(0, final: true);

        internal bool Drain(int read, bool final)
        {
            _filled += read;

            int consumed = TurtleEngine.Drain(
                _buffer.AsSpan(0, _filled), final, _handler, in _options, _state, ref _result);

            _buffer.AsSpan(consumed, _filled - consumed).CopyTo(_buffer);
            _filled -= consumed;

            if (final || _result.Stop)
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

        private void Grow()
        {
            byte[] bigger = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
            _buffer.AsSpan(0, _filled).CopyTo(bigger);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }
    }
}
