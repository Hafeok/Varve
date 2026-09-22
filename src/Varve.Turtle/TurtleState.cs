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

    /// <summary>
    /// The number of blank nodes this parse has invented. Fresh labels are
    /// <c>g0</c>, <c>g1</c>, … and a document's own labels are emitted with a
    /// <c>b</c> in front, so the two families cannot collide however the
    /// document names things (`turtle.md` §4).
    /// </summary>
    internal int FreshBlankNodes { get; private set; }

    /// <summary>
    /// The counter as it stood when the last statement finished, so that an
    /// attempt which is abandoned — for truncation or for an error — gives back
    /// the labels it minted.
    /// </summary>
    private int _freshAtStatementStart;

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

    internal int NextBlankNode() => FreshBlankNodes++;

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
    /// The blank node counter rewinds with them. A statement that ran past the
    /// end of a chunk is scanned again when the next chunk arrives, and without
    /// the rewind every attempt would burn labels — so the same document would
    /// name its blank nodes differently depending on how it was delivered, and
    /// a one-byte-at-a-time read would number them in the thousands.
    /// </remarks>
    internal void BeginStatement()
    {
        PendingCount = 0;
        FreshBlankNodes = _freshAtStatementStart;
        Arena.Reset();
    }

    /// <summary>Accepts a statement's labels, so the next one starts after them.</summary>
    internal void CompleteStatement() => _freshAtStatementStart = FreshBlankNodes;
}
