using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>
/// Everything a Turtle parse carries from one statement to the next, and
/// therefore from one buffer to the next.
/// </summary>
/// <remarks>
/// <para>
/// A class, not a <c>ref struct</c>: prefixes, the base IRI and the blank node
/// counter outlive any one span, and a streaming parse hands the scanner a
/// fresh span for every chunk.
/// </para>
/// <para>
/// It also holds the statement's pending quads. ADR 0030 makes the statement
/// all-or-nothing, so a blank node property list's triples — which are produced
/// before the statement containing them finishes — wait here until the
/// terminating <c>.</c> arrives.
/// </para>
/// </remarks>
internal sealed class TurtleState
{
    private readonly List<byte[]> _prefixNames = [];
    private readonly List<byte[]> _prefixIris = [];
    private byte[] _base = [];
    private PendingQuad[] _pending = new PendingQuad[16];

    /// <summary>One quad, as arena slot indices, waiting for its statement to close.</summary>
    internal struct PendingQuad
    {
        internal int Subject;
        internal int Predicate;
        internal int Object;
        internal int Graph;
    }

    internal TermArena Arena { get; } = new();

    /// <summary>How many quads the current statement has produced so far.</summary>
    internal int PendingCount { get; private set; }

    /// <summary>The in-scope base IRI, or empty when there is none.</summary>
    internal ReadOnlySpan<byte> Base => _base;

    internal bool HasBase => _base.Length > 0;

    /// <summary>What every blank node in this parse is called.</summary>
    internal BlankNodeNaming Names { get; } = new();

    /// <summary>The document offset the current buffer starts at.</summary>
    internal long DocumentOffset { get; private set; }

    /// <summary>The line number the current buffer starts on, counting from one.</summary>
    internal int LineNumber { get; private set; } = 1;

    /// <summary>The document offset of the start of that line.</summary>
    /// <remarks>
    /// Kept as well as the line number because a line may have begun in an
    /// earlier buffer, and without it the column of an error on such a line
    /// would be measured from the wrong place.
    /// </remarks>
    internal long LineStart { get; private set; }

    /// <summary>
    /// Records that <paramref name="consumed"/> has been parsed and the next
    /// buffer begins after it.
    /// </summary>
    /// <remarks>
    /// An error's position must name a place in the document, not in whichever
    /// fragment of it the parser happened to be holding. A reader given the
    /// same bytes in two pieces reports the same position as one given them
    /// whole, and the chunk-boundary oracle is what holds that true.
    /// </remarks>
    internal void Advance(ReadOnlySpan<byte> consumed)
    {
        for (int i = 0; i < consumed.Length; i++)
        {
            if (IsLineBreak(consumed, i))
            {
                LineNumber++;
                LineStart = DocumentOffset + i + 1;
            }
        }

        DocumentOffset += consumed.Length;
    }

    /// <summary>
    /// A line ends at a newline, and at a carriage return that is not part of
    /// a CR LF pair. One rule, used both when advancing and when reporting.
    /// </summary>
    internal static bool IsLineBreak(ReadOnlySpan<byte> text, int index) =>
        text[index] == (byte)'\n'
        || (text[index] == (byte)'\r' && (index + 1 >= text.Length || text[index + 1] != (byte)'\n'));

    internal void SetBase(ReadOnlySpan<byte> iri) => _base = iri.ToArray();

    /// <summary>
    /// Binds a prefix, replacing an earlier binding of the same name. A linear
    /// scan: a document has a handful of prefixes, and a dictionary keyed by
    /// bytes would allocate on every lookup to save nothing.
    /// </summary>
    internal void BindPrefix(ReadOnlySpan<byte> name, ReadOnlySpan<byte> iri)
    {
        for (int i = 0; i < _prefixNames.Count; i++)
        {
            if (name.SequenceEqual(_prefixNames[i]))
            {
                _prefixIris[i] = iri.ToArray();
                return;
            }
        }

        _prefixNames.Add(name.ToArray());
        _prefixIris.Add(iri.ToArray());
    }

    internal bool TryResolvePrefix(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> iri)
    {
        for (int i = 0; i < _prefixNames.Count; i++)
        {
            if (name.SequenceEqual(_prefixNames[i]))
            {
                iri = _prefixIris[i];
                return true;
            }
        }

        iri = default;
        return false;
    }


    internal void Add(int subject, int predicate, int obj, int graph)
    {
        if (PendingCount == _pending.Length)
        {
            Array.Resize(ref _pending, _pending.Length * 2);
        }

        _pending[PendingCount].Subject = subject;
        _pending[PendingCount].Predicate = predicate;
        _pending[PendingCount].Object = obj;
        _pending[PendingCount].Graph = graph;
        PendingCount++;
    }

    internal PendingQuad At(int index) => _pending[index];

    /// <summary>Drops the statement's quads without emitting them. ADR 0030.</summary>
    internal void Discard() => PendingCount = 0;

    /// <summary>Starts a statement: no quads, and the arena free for its terms.</summary>
    /// <remarks>
    /// The blank node naming rewinds with them. A statement that ran past the
    /// end of a chunk is scanned again when the next chunk arrives, and without
    /// the rewind every attempt would burn labels — so the same document would
    /// name its blank nodes differently depending on how it was delivered, and
    /// a one-byte-at-a-time read would number them in the thousands.
    /// </remarks>
    internal void BeginStatement()
    {
        PendingCount = 0;
        Names.BeginStatement();
        Arena.Reset();
    }

    /// <summary>Accepts a statement's labels, so the next one starts after them.</summary>
    internal void CompleteStatement() => Names.CompleteStatement();
}
