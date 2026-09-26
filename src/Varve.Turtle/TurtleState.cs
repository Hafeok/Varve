// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
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
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal sealed class TurtleState
{
    // Arrays with a count rather than lists, so that resolving a prefixed
    // name, once per term, is a loop over memory this state owns (VARVE0003).
    private byte[][] _prefixNames = new byte[4][];
    private byte[][] _prefixIris = new byte[4][];
    private int _prefixCount;
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
    /// Whether the last byte advanced over was a carriage return, so that an
    /// <c>LF</c> opening the next buffer completes a pair rather than ending a
    /// second line. See <see cref="CountLineBreak"/>.
    /// </summary>
    internal bool AfterCarriageReturn { get; private set; }

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
        int line = LineNumber;
        long lineStart = LineStart;
        bool afterCarriageReturn = AfterCarriageReturn;

        for (int i = 0; i < consumed.Length; i++)
        {
            CountLineBreak(consumed[i], DocumentOffset + i, ref line, ref lineStart, ref afterCarriageReturn);
        }

        LineNumber = line;
        LineStart = lineStart;
        AfterCarriageReturn = afterCarriageReturn;
        DocumentOffset += consumed.Length;
    }

    /// <summary>
    /// Counts <paramref name="b"/> as a line break if it is one, given whether
    /// the byte before it was a carriage return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line ends at a carriage return and at a newline, and <c>CR LF</c> is
    /// one ending rather than two. The obvious way to say that is to look at
    /// the byte after a <c>CR</c> — and that is the chunk-boundary rule's own
    /// trap (`turtle.md` §8), because a <c>CR</c> at the end of a buffer has no
    /// byte after it yet. A buffer that ends between the <c>CR</c> and the
    /// <c>LF</c> then counts two lines where one document has one.
    /// </para>
    /// <para>
    /// So the rule looks backwards instead: a <c>CR</c> always ends a line, and
    /// an <c>LF</c> ends one only when the byte before it was not a <c>CR</c>.
    /// Backwards needs no lookahead, so the only state that crosses a buffer is
    /// one <see cref="bool"/> — and a document split anywhere reports the same
    /// line as the same document whole.
    /// </para>
    /// </remarks>
    internal static void CountLineBreak(
        byte b, long absolute, ref int line, ref long lineStart, ref bool afterCarriageReturn)
    {
        if (b == (byte)'\r')
        {
            line++;
            lineStart = absolute + 1;
            afterCarriageReturn = true;
            return;
        }

        if (b == (byte)'\n')
        {
            if (!afterCarriageReturn)
            {
                line++;
            }

            lineStart = absolute + 1;
        }

        afterCarriageReturn = false;
    }

    [DesignDecision(typeof(HotPathScope.DirectivesAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    internal void SetBase(ReadOnlySpan<byte> iri) => _base = iri.ToArray();

    /// <summary>
    /// Binds a prefix, replacing an earlier binding of the same name. A linear
    /// scan: a document has a handful of prefixes, and a dictionary keyed by
    /// bytes would allocate on every lookup to save nothing.
    /// </summary>
    [DesignDecision(typeof(HotPathScope.DirectivesAreNotPerQuad), Scope = ExceptionScope.HotPath)]
    internal void BindPrefix(ReadOnlySpan<byte> name, ReadOnlySpan<byte> iri)
    {
        for (int i = 0; i < _prefixCount; i++)
        {
            if (name.SequenceEqual(_prefixNames[i]))
            {
                _prefixIris[i] = iri.ToArray();
                return;
            }
        }

        if (_prefixCount == _prefixNames.Length)
        {
            Array.Resize(ref _prefixNames, _prefixCount * 2);
            Array.Resize(ref _prefixIris, _prefixCount * 2);
        }

        _prefixNames[_prefixCount] = name.ToArray();
        _prefixIris[_prefixCount] = iri.ToArray();
        _prefixCount++;
    }

    internal bool TryResolvePrefix(ReadOnlySpan<byte> name, out ReadOnlySpan<byte> iri)
    {
        for (int i = 0; i < _prefixCount; i++)
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
            GrowPending();
        }

        _pending[PendingCount].Subject = subject;
        _pending[PendingCount].Predicate = predicate;
        _pending[PendingCount].Object = obj;
        _pending[PendingCount].Graph = graph;
        PendingCount++;
    }

    internal PendingQuad At(int index) => _pending[index];

    // The statement's buffer of pending quads grows only when a statement has
    // more quads than any before it, so it is amortised to nothing per quad.
    // ADR 0030 decided the buffer; this is what having one costs.
    [DesignDecision(typeof(TurtleRecoveryAndPrefixes.StatementQuadsBuffered), Scope = ExceptionScope.HotPath)]
    private void GrowPending() => Array.Resize(ref _pending, _pending.Length * 2);

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
