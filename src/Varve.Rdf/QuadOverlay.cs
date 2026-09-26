// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad source that is another source with a delta applied:
/// <c>Overlay(B, (A, R)) = (B \ R) ∪ A</c>, merged at scan time.
/// </summary>
/// <remarks>
/// <para>
/// Specification R4 and ADR 0017. One implementation serves as-of reads,
/// <c>Diff</c> and pre-commit validation, which is why it lives at layer 1 and
/// knows nothing about a store: an as-of read is an overlay of a log tail on a
/// checkpoint, and a validator sees an overlay of the pending delta on the
/// head.
/// </para>
/// <para>
/// The delta's handles are the base's handles, and the overlay delegates term
/// lookup and equality to the base. A delta that names a term the base has not
/// seen needs a base that can externalise it — the store's pending view does.
/// </para>
/// <para>
/// The specification states R4 as exact when <c>R ⊆ B</c> and <c>A ∩ B = ∅</c>,
/// which I2 guarantees for every use it makes. This implementation does not
/// lean on that: a base quad the delta also asserts is emitted once, from the
/// delta, at the cost of one lookup per base quad. The answer is
/// <c>(B \ R) ∪ A</c> for any delta, exact or not.
/// </para>
/// </remarks>
public sealed class QuadOverlay : IQuadSource
{
    private readonly IQuadSource _base;
    private readonly QuadDelta _delta;

    /// <summary>The base with the delta applied.</summary>
    public QuadOverlay(IQuadSource @base, QuadDelta delta)
    {
        ArgumentNullException.ThrowIfNull(@base);
        _base = @base;
        _delta = delta;
    }

    /// <summary>The base's equality: the delta's handles are the base's.</summary>
    public IEqualityComparer<TermHandle> TermComparer => _base.TermComparer;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _base.TryInternalise(term, out handle);

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) =>
        _base.TryExternalise(handle, out term);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) =>
        _delta.Asserts(in quad) || (!_delta.Retracts(in quad) && _base.Contains(in quad));

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new Cursor(_base.Match(subject, predicate, @object, graph), _delta, subject, predicate, @object, graph);

    /// <inheritdoc />
    /// <remarks>
    /// The base's estimate adjusted by the delta (ADR 0049): for each delta
    /// quad matching the pattern, plus one if asserted and not in the base,
    /// minus one if retracted, not asserted, and in the base. Exact when the
    /// base is, at one base lookup per matching delta quad; unknown when the
    /// base is.
    /// </remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        CardinalityEstimate below = _base.Estimate(subject, predicate, @object, graph);

        if (below.IsUnknown)
        {
            return below;
        }

        long count = below.Count;

        foreach (Quad quad in _delta.Asserted)
        {
            if (QuadPatterns.Matches(in quad, subject, predicate, @object, graph) && !_base.Contains(in quad))
            {
                count++;
            }
        }

        foreach (Quad quad in _delta.Retracted)
        {
            if (QuadPatterns.Matches(in quad, subject, predicate, @object, graph)
                && !_delta.Asserts(in quad)
                && _base.Contains(in quad))
            {
                count--;
            }
        }

        return below.IsExact ? CardinalityEstimate.Exact(count) : CardinalityEstimate.Estimated(count);
    }

    /// <inheritdoc />
    /// <remarks>The base's answer: the delta's handles are the base's (ADR 0050).</remarks>
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _base.TryGetInlineValue(handle, out value);

    private sealed class Cursor : IQuadCursor
    {
        private readonly IQuadCursor _base;
        private readonly QuadDelta _delta;
        private readonly TermHandle _subject;
        private readonly TermHandle _predicate;
        private readonly TermHandle _object;
        private readonly GraphPattern _graph;
        private bool _baseDone;
        private int _next;
        private readonly int _end;

        internal Cursor(
            IQuadCursor @base,
            QuadDelta delta,
            TermHandle subject,
            TermHandle predicate,
            TermHandle @object,
            GraphPattern graph)
        {
            _base = @base;
            _delta = delta;
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _graph = graph;

            // Asserted is sorted subject first, so a bound subject is a range.
            ReadOnlySpan<Quad> asserted = delta.Asserted;
            _next = 0;
            _end = asserted.Length;

            if (!subject.IsNone)
            {
                _next = Bound(asserted, subject.Value, inclusive: false);
                _end = Bound(asserted, subject.Value, inclusive: true);
            }
        }

        public Quad Current { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            if (!_baseDone)
            {
                while (_base.MoveNext())
                {
                    Quad candidate = _base.Current;

                    if (!_delta.Retracts(in candidate) && !_delta.Asserts(in candidate))
                    {
                        Current = candidate;
                        return true;
                    }
                }

                _baseDone = true;
            }

            ReadOnlySpan<Quad> asserted = _delta.Asserted;

            while (_next < _end)
            {
                Quad candidate = asserted[_next++];

                if (Matches(in candidate))
                {
                    Current = candidate;
                    return true;
                }
            }

            Current = default;
            return false;
        }

        public void Dispose() => _base.Dispose();

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        private bool Matches(in Quad quad) =>
            (_subject.IsNone || _subject.Equals(quad.Subject))
            && (_predicate.IsNone || _predicate.Equals(quad.Predicate))
            && (_object.IsNone || _object.Equals(quad.Object))
            && _graph.Matches(quad.Graph);

        // The first index whose subject is at or above (or, inclusive, above) the value.
        private static int Bound(ReadOnlySpan<Quad> sorted, ulong subject, bool inclusive)
        {
            int low = 0;
            int high = sorted.Length;

            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                ulong value = sorted[middle].Subject.Value;

                if (value < subject || (inclusive && value == subject))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }
}
