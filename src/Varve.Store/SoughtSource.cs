// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// An as-of read inside an extent set's window, which seeks the set: its
/// keys, its rows within and its columns (ADR 0177). When a read finds the
/// set damaged, the view answers from the runs below the set and the log's
/// net delta from the set's start to the position, in its place; a scan
/// carries on after the quads it has already yielded, in the same order,
/// because both read the same quads in the order their pattern's index
/// keeps them (ADR 0213). Its distinct predicates and objects are sought
/// the same way, and resume below the set the same way (ADR 0193).
/// </summary>
[DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.Boundary)]
internal sealed class SoughtSource : IQuadSource, IDistinctTermSource
{
    private readonly IndexSource _sought;
    private readonly TermView _terms;
    private readonly Func<IndexVersion> _below;
    private IndexSource? _fallback;

    internal SoughtSource(IndexVersion sought, TermView terms, Func<IndexVersion> below)
    {
        _sought = new IndexSource(sought, terms);
        _terms = terms;
        _below = below;
    }

    public IEqualityComparer<TermHandle> TermComparer => StoreTermComparer.ById;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _terms.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _terms.TryExternalise(handle, out term);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad)
    {
        if (_fallback is { } below)
        {
            return below.Contains(in quad);
        }

        try
        {
            return _sought.Contains(in quad);
        }
        catch (DerivedDamageException)
        {
            return Below().Contains(in quad);
        }
    }

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new Cursor(this, subject, predicate, @object, graph);

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        if (_fallback is { } below)
        {
            return below.Estimate(subject, predicate, @object, graph);
        }

        try
        {
            return _sought.Estimate(subject, predicate, @object, graph);
        }
        catch (DerivedDamageException)
        {
            return Below().Estimate(subject, predicate, @object, graph);
        }
    }

    /// <inheritdoc />
    public bool TryDistinctPredicates(GraphPattern graph, [NotNullWhen(true)] out ITermCursor? predicates) =>
        TermCursor.TryOpen(this, TermHandle.None, graph, out predicates);

    /// <inheritdoc />
    public bool TryDistinctObjects(TermHandle predicate, GraphPattern graph, [NotNullWhen(true)] out ITermCursor? objects) =>
        !predicate.IsNone ? TermCursor.TryOpen(this, predicate, graph, out objects) : Refuse(out objects);

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _sought.TryGetInlineValue(handle, out value);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _sought.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    // The view without the set: made once, by the read that found the
    // damage, from the log, off the hot path's rules as a page missed in
    // the cache is.
    [DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.HotPath)]
    private IndexSource Below() => _fallback ??= new IndexSource(_below(), _terms);

    private static bool Refuse(out ITermCursor? cursor)
    {
        cursor = null;
        return false;
    }

    /// <summary>
    /// A seek of the sought set's distinct predicates of a graph, or objects
    /// of one predicate, that, once the set is found damaged, carries on over
    /// the view without it: the two hold the same quads, so they yield the
    /// same terms in the same order.
    /// </summary>
    private sealed class TermCursor : ITermCursor
    {
        private readonly SoughtSource _owner;
        private readonly TermHandle _predicate;
        private readonly GraphPattern _graph;
        private ITermCursor _inner;
        private long _yielded;
        private bool _below;

        private TermCursor(SoughtSource owner, TermHandle predicate, GraphPattern graph, ITermCursor inner, bool below)
        {
            _owner = owner;
            _predicate = predicate;
            _graph = graph;
            _inner = inner;
            _below = below;
        }

        public TermHandle Current { get; private set; }

        // A seek reads its first blocks as it starts.
        internal static bool TryOpen(SoughtSource owner, TermHandle predicate, GraphPattern graph, [NotNullWhen(true)] out ITermCursor? cursor)
        {
            cursor = null;
            bool below = owner._fallback is not null;
            ITermCursor? inner;
            try
            {
                if (!Open(below ? owner._fallback! : owner._sought, predicate, graph, out inner))
                {
                    return false;
                }
            }
            catch (DerivedDamageException) when (!below)
            {
                below = true;
                if (!Open(owner.Below(), predicate, graph, out inner))
                {
                    return false;
                }
            }

            cursor = new TermCursor(owner, predicate, graph, inner, below);
            return true;
        }

        public bool MoveNext()
        {
            while (true)
            {
                try
                {
                    if (!_inner.MoveNext())
                    {
                        Current = TermHandle.None;
                        return false;
                    }

                    Current = _inner.Current;
                    _yielded++;
                    return true;
                }
                catch (DerivedDamageException) when (!_below)
                {
                    Resume();
                }
            }
        }

        public void Dispose() => _inner.Dispose();

        private static bool Open(IndexSource source, TermHandle predicate, GraphPattern graph, [NotNullWhen(true)] out ITermCursor? cursor) =>
            predicate.IsNone ? source.TryDistinctPredicates(graph, out cursor) : source.TryDistinctObjects(predicate, graph, out cursor);

        // The same seek over the view without the set, past the terms this
        // one has already yielded.
        [DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.HotPath)]
        private void Resume()
        {
            _inner.Dispose();
            if (!Open(_owner.Below(), _predicate, _graph, out ITermCursor? below))
            {
                throw new InvalidOperationException("The view below a damaged set refused the seek the set answered.");
            }

            _inner = below;
            _below = true;
            for (long i = 0; i < _yielded; i++)
            {
                if (!_inner.MoveNext())
                {
                    throw new InvalidOperationException("The view below a damaged set holds fewer distinct terms than the seek of it had already yielded.");
                }
            }
        }
    }

    /// <summary>A scan of the sought set that, once the set is found damaged, carries on over the view without it.</summary>
    private sealed class Cursor : IQuadCursor
    {
        private readonly SoughtSource _owner;
        private readonly TermHandle _subject;
        private readonly TermHandle _predicate;
        private readonly TermHandle _object;
        private readonly GraphPattern _graph;
        private IQuadCursor _inner;
        private long _yielded;
        private bool _below;

        internal Cursor(SoughtSource owner, TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
        {
            _owner = owner;
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _graph = graph;
            _below = owner._fallback is not null;

            // A seek reads its first blocks as it starts.
            try
            {
                _inner = _below ? owner._fallback!.Match(subject, predicate, @object, graph) : owner._sought.Match(subject, predicate, @object, graph);
            }
            catch (DerivedDamageException) when (!_below)
            {
                _inner = owner.Below().Match(subject, predicate, @object, graph);
                _below = true;
            }
        }

        public Quad Current { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            while (true)
            {
                try
                {
                    if (!_inner.MoveNext())
                    {
                        Current = default;
                        return false;
                    }

                    Current = _inner.Current;
                    _yielded++;
                    return true;
                }
                catch (DerivedDamageException) when (!_below)
                {
                    Resume();
                }
            }
        }

        public void Dispose() => _inner.Dispose();

        // The same scan over the view without the set, past the quads this
        // one has already yielded: the two yield the same quads in the same
        // order.
        [DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.HotPath)]
        private void Resume()
        {
            _inner.Dispose();
            _inner = _owner.Below().Match(_subject, _predicate, _object, _graph);
            _below = true;

            for (long i = 0; i < _yielded; i++)
            {
                if (!_inner.MoveNext())
                {
                    throw new InvalidOperationException("The view below a damaged set holds fewer quads than the scan of it had already yielded.");
                }
            }
        }
    }
}
