using System;
using System.Buffers;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Reads Turtle and TriG one quad at a time, under the caller's control.
/// </summary>
/// <remarks>
/// <para>
/// The pull shape, and the peer of <see cref="NQuadsReader"/>. It allocates
/// nothing per quad and holds no unmanaged resource, so there is nothing to
/// dispose; being a <c>ref struct</c> is what keeps <see cref="Current"/> from
/// outliving the buffer it points into.
/// </para>
/// <para>
/// <strong>A statement is not a quad</strong>, which is the whole difference
/// from the N-Triples reader. One Turtle statement can carry a predicate-object
/// list, a blank node property list and a collection, and produce a dozen
/// quads; ADR 0030 makes it all-or-nothing, so the reader parses a whole
/// statement, then hands its quads out one at a time. <see cref="Current"/>
/// stays valid until the next <see cref="Read"/> that crosses into the
/// following statement — which is the same rule as the push path's "valid for
/// the callback only", expressed for a caller that holds the cursor.
/// </para>
/// <para>
/// <see cref="Read"/> returns false at the end of the input <em>and</em> when a
/// statement was rejected and no error handler asked to carry on. The two are
/// told apart by <see cref="Error"/>, whose kind is
/// <see cref="ParseErrorKind.None"/> at a clean end.
/// </para>
/// </remarks>
public ref struct TurtleReader
{
    private readonly TurtleOptions _options;
    private readonly TurtleState _state;
    private readonly ReadOnlySpan<byte> _data;
    private TurtleScanner _scanner;
    private ParseState _result;
    private int _pendingAt;
    private int _consumed;
    private int _subject;
    private int _predicate;
    private int _object;
    private int _graph;
    private bool _finished;

    /// <summary>Reads a document held in memory.</summary>
    public TurtleReader(ReadOnlySpan<byte> utf8, in TurtleOptions options)
    {
        _options = options;
        _state = TurtleParser.NewState(in options);
        _data = utf8;
        _scanner = new TurtleScanner(utf8, _state, in options, mayGrow: false);
        _result = ParseState.New();
        _pendingAt = 0;
        _consumed = 0;
        _subject = -1;
        _predicate = -1;
        _object = -1;
        _graph = -1;
        _finished = false;
        Error = default;
    }

    /// <summary>Reads a document held as a sequence of buffers.</summary>
    /// <remarks>
    /// A single-segment sequence is read in place. A fragmented one is
    /// <strong>copied once</strong>, because a pull reader hands out views that
    /// the caller holds across calls, and a buffer that is compacted underneath
    /// them would invalidate what it just returned. The copy is one allocation
    /// for the reader and none per quad, and the sequence was already in memory
    /// — nothing is read from a device that was not.
    /// <para>
    /// This is why the push path, not this one, is the streaming shape: with
    /// <see cref="TurtleParser.ParseAsync(System.IO.Pipelines.PipeReader, QuadHandler, TurtleOptions)"/>
    /// a quad's life ends when the callback returns, so the buffer can be
    /// compacted and the memory is the longest statement rather than the
    /// document.
    /// </para>
    /// </remarks>
    public TurtleReader(in ReadOnlySequence<byte> utf8, in TurtleOptions options)
        : this(Contiguous(in utf8), in options)
    {
    }

    /// <summary>The quad the last <see cref="Read"/> produced.</summary>
    public readonly QuadView Current => _state.Arena.Quad(_data, _subject, _predicate, _object, _graph);

    /// <summary>
    /// Why the last <see cref="Read"/> returned false, when it was a rejection
    /// rather than the end of the input.
    /// </summary>
    public ParseError Error { get; private set; }

    /// <summary>Where the next statement begins.</summary>
    public readonly ParsePosition Position => new(_state.DocumentOffset, _state.LineNumber, 1);

    /// <summary>What the reader has seen so far.</summary>
    public readonly ParseResult Result => _result.ToResult();

    /// <summary>
    /// Advances to the next quad. Returns false at the end of the input, and on
    /// a rejected statement that no error handler chose to continue past.
    /// </summary>
    public bool Read()
    {
        Error = default;

        while (true)
        {
            if (_pendingAt < _state.PendingCount)
            {
                TurtleState.PendingQuad pending = _state.At(_pendingAt++);

                _subject = pending.Subject;
                _predicate = pending.Predicate;
                _object = pending.Object;
                _graph = pending.Graph >= 0 ? pending.Graph : _scanner.Graph;
                _result.QuadCount++;
                return true;
            }

            if (_finished || _result.Stop)
            {
                return false;
            }

            // The statement just handed out is released here and not earlier:
            // until now its quads were the ones Current pointed at.
            _state.Discard();
            _pendingAt = 0;

            if (!NextStatement())
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Parses one statement, or settles that there is not another one.
    /// </summary>
    /// <remarks>
    /// The recovery itself is <see cref="TurtleEngine.Step"/>'s, shared with
    /// the push parsers, so a rejected statement resynchronises by the same
    /// rule and leaves none of its quads behind. What is local here is only
    /// which outcome ends the read.
    /// </remarks>
    private bool NextStatement()
    {
        long before = _result.ErrorCount;

        StatementStatus status = TurtleEngine.Step(
            ref _scanner, _data, final: true, in _options, _state, ref _result, ref _consumed);

        if (status == StatementStatus.Complete)
        {
            return true;
        }

        _finished = true;
        _state.Advance(_data[.._consumed]);

        if (_result.ErrorCount > before)
        {
            Error = _result.LastError;
        }

        return false;
    }

    /// <summary>
    /// The sequence as one span: its own when it has a single segment, a copy
    /// otherwise.
    /// </summary>
    private static ReadOnlySpan<byte> Contiguous(in ReadOnlySequence<byte> utf8) =>
        utf8.IsSingleSegment ? utf8.FirstSpan : utf8.ToArray();
}
