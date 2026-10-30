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
/// keeps them (ADR 0213).
/// </summary>
[DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.Boundary)]
internal sealed class SoughtSource : IQuadSource
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

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _sought.TryGetInlineValue(handle, out value);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _sought.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    // The view without the set: made once, by the read that found the
    // damage, from the log, off the hot path's rules as a page missed in
    // the cache is.
    [DesignDecision(typeof(VerifiedDerivedData.DamageMidReadIsRerunAndResumed), Scope = ExceptionScope.HotPath)]
    private IndexSource Below() => _fallback ??= new IndexSource(_below(), _terms);

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
