using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Reads N-Triples and N-Quads one quad at a time, under the caller's control.
/// </summary>
/// <remarks>
/// <para>
/// The pull shape. It allocates nothing per quad and holds no unmanaged
/// resource, so there is nothing to dispose; being a <c>ref struct</c> is what
/// keeps <see cref="Current"/> from outliving the buffer it points into.
/// </para>
/// <para>
/// <see cref="Read"/> returns false at the end of the input <em>and</em> when a
/// line was rejected and no error handler asked to carry on. The two are told
/// apart by <see cref="Error"/>.
/// </para>
/// </remarks>
public ref struct NQuadsReader
{
    private readonly ParseOptions _options;
    private readonly TermArena _arena;
    private readonly LineBuffer? _buffer;
    private readonly bool _isSequence;
    private readonly ReadOnlySpan<byte> _input;
    private SequenceReader<byte> _reader;
    private ReadOnlySequence<byte> _lineData;
    private ReadOnlySpan<byte> _line;
    private int _at;
    private int _subject;
    private int _predicate;
    private int _object;
    private int _graph;
    private long _offset;
    private int _lineNumber;
    private long _quadCount;
    private long _errorCount;
    private ParseError _firstError;

    /// <summary>Reads a document held in memory.</summary>
    public NQuadsReader(ReadOnlySpan<byte> utf8, in ParseOptions options)
        : this(options)
    {
        _input = utf8;
    }

    /// <summary>Reads a document held as a sequence of buffers.</summary>
    public NQuadsReader(in ReadOnlySequence<byte> utf8, in ParseOptions options)
        : this(options)
    {
        _reader = new SequenceReader<byte>(utf8);
        _buffer = new LineBuffer();
        _isSequence = true;
    }

    private NQuadsReader(in ParseOptions options)
    {
        _options = options;
        _arena = new TermArena();
        _buffer = null;
        _isSequence = false;
        _input = default;
        _reader = default;
        _lineData = default;
        _line = default;
        _at = 0;
        _subject = -1;
        _predicate = -1;
        _object = -1;
        _graph = -1;
        _offset = 0;
        _lineNumber = 1;
        _quadCount = 0;
        _errorCount = 0;
        Error = default;
        _firstError = default;
    }

    /// <summary>The quad the last <see cref="Read"/> produced.</summary>
    public readonly QuadView Current => _arena.Quad(_line, _subject, _predicate, _object, _graph);

    /// <summary>
    /// Why the last <see cref="Read"/> returned false, when it was a rejection
    /// rather than the end of the input.
    /// </summary>
    public ParseError Error { get; private set; }

    /// <summary>Where the next line begins.</summary>
    public readonly ParsePosition Position => new(_offset, _lineNumber, 1);

    /// <summary>What the reader has seen so far.</summary>
    public readonly ParseResult Result => new(_quadCount, _errorCount, _firstError);

    /// <summary>
    /// Advances to the next quad. Returns false at the end of the input, and
    /// on a rejected line that no error handler chose to continue past.
    /// </summary>
    public bool Read()
    {
        Error = default;

        while (TryNextLine())
        {
            long lineOffset = _offset;
            int lineNumber = _lineNumber;
            Consume();

            _arena.Reset();
            LineParser parser = new(_line, _arena, _options.Syntax, _options.ValidateIris);

            switch (parser.Parse(out _subject, out _predicate, out _object, out _graph))
            {
                case LineStatus.Empty:
                    continue;

                case LineStatus.Quad:
                    _quadCount++;
                    return true;

                default:
                    if (!Reject(parser, lineOffset, lineNumber))
                    {
                        return false;
                    }

                    continue;
            }
        }

        return false;
    }

    private bool Reject(scoped in LineParser parser, long lineOffset, int lineNumber)
    {
        ParseState state = new()
        {
            QuadCount = _quadCount,
            ErrorCount = _errorCount,
            FirstError = _firstError,
            Offset = _offset,
            Line = _lineNumber,
        };

        ParseEngine.Reject(
            parser.Error, parser.IriError, parser.ErrorOffset, lineOffset, lineNumber, in _options, ref state);

        _errorCount = state.ErrorCount;
        _firstError = state.FirstError;
        Error = state.ErrorCount == 1 ? state.FirstError : LastError(parser, lineOffset, lineNumber);
        return !state.Stop;
    }

    private static ParseError LastError(scoped in LineParser parser, long lineOffset, int lineNumber) =>
        new(
            parser.Error,
            new ParsePosition(lineOffset + parser.ErrorOffset, lineNumber, parser.ErrorOffset + 1),
            parser.IriError);

    /// <summary>Finds the next line without consuming its terminator.</summary>
    private bool TryNextLine()
    {
        if (_isSequence)
        {
            if (_reader.End)
            {
                return false;
            }

            _lineData =
                _reader.TryReadToAny(out ReadOnlySequence<byte> terminated, "\r\n"u8, advancePastDelimiter: false)
                    ? terminated
                    : TakeRest();

            _line = _lineData.IsSingleSegment ? _lineData.FirstSpan : _buffer!.Copy(_lineData);
            return true;
        }

        if (_at >= _input.Length)
        {
            return false;
        }

        int end = _at;

        while (end < _input.Length && !NTriplesChars.IsEol(_input[end]))
        {
            end++;
        }

        _line = _input[_at..end];
        _at = end;
        return true;
    }

    private ReadOnlySequence<byte> TakeRest()
    {
        ReadOnlySequence<byte> rest = _reader.UnreadSequence;
        _reader.AdvanceToEnd();
        return rest;
    }

    /// <summary>Steps past the line just read and the end-of-line run after it.</summary>
    private void Consume()
    {
        long breakBytes = 0;

        if (_isSequence)
        {
            while (_reader.TryPeek(out byte b) && NTriplesChars.IsEol(b))
            {
                bool pair = b == (byte)'\r' && _reader.TryPeek(1, out byte next) && next == (byte)'\n';
                _reader.Advance(pair ? 2 : 1);
                breakBytes += pair ? 2 : 1;
                _lineNumber++;
            }
        }
        else
        {
            while (_at < _input.Length && NTriplesChars.IsEol(_input[_at]))
            {
                bool pair = _input[_at] == (byte)'\r' && _at + 1 < _input.Length && _input[_at + 1] == (byte)'\n';
                _at += pair ? 2 : 1;
                breakBytes += pair ? 2 : 1;
                _lineNumber++;
            }
        }

        _offset += _line.Length + breakBytes;
    }
}
