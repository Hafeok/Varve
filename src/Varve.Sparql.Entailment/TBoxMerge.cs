// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Entailment;

/// <summary>
/// A quad source in which every graph is read merged with the TBox graphs
/// (ADR 0193 point 1): a <see cref="Match"/> of a graph also matches the TBox
/// graphs, each TBox quad reported in the matched graph, and one over every
/// named graph reports it once per named graph. A TBox quad the matched graph
/// also holds is reported once.
/// </summary>
/// <remarks>
/// Built once per request over the request's pinned, scoped source, which it
/// does not own; not shared between threads. The named graphs are enumerated
/// the first time a match over every named graph finds a TBox quad, and then
/// held for the request.
/// </remarks>
[Contract(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Role = "every graph read with the TBox graphs")]
internal sealed class TBoxMerge : IQuadSource
{
    private readonly TermHandle[] _tbox;
    private readonly RdfTerm[] _tboxTerms;
    private List<TermHandle>? _named;

    /// <summary><paramref name="inner"/>, with <paramref name="tboxGraphs"/> merged into each graph; a graph it does not hold is empty.</summary>
    internal TBoxMerge(IQuadSource inner, IEnumerable<RdfTerm> tboxGraphs)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tboxGraphs);
        Inner = inner;
        List<TermHandle> handles = [];
        foreach (RdfTerm graph in tboxGraphs)
        {
            if (inner.TryInternalise(graph, out TermHandle handle) && !handles.Exists(h => inner.TermComparer.Equals(h, handle)))
            {
                handles.Add(handle);
            }
        }

        _tbox = [.. handles];
        List<RdfTerm> terms = [];
        foreach (TermHandle handle in _tbox)
        {
            if (inner.TryExternalise(handle, out RdfTerm? term))
            {
                terms.Add(term);
            }
        }

        _tboxTerms = [.. terms];
    }

    /// <summary>The TBox graphs the source holds, as handles.</summary>
    internal IReadOnlyList<TermHandle> TBoxGraphs => _tbox;

    /// <summary>The TBox graphs the source holds, as terms.</summary>
    internal IReadOnlyList<RdfTerm> TBoxGraphTerms => _tboxTerms;

    /// <summary>The source the merge reads.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal IQuadSource Inner { get; }

    /// <inheritdoc />
    public IEqualityComparer<TermHandle> TermComparer => Inner.TermComparer;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => Inner.TryInternalise(term, out handle);

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => Inner.TryExternalise(handle, out term);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => Inner.TryGetInlineValue(handle, out value);

    /// <inheritdoc/>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        Inner.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad)
    {
        if (Inner.Contains(in quad))
        {
            return true;
        }

        for (int i = 0; i < _tbox.Length; i++)
        {
            Quad there = new(quad.Subject, quad.Predicate, quad.Object, _tbox[i]);
            if (Inner.Contains(in there))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        IQuadCursor own = Inner.Match(subject, predicate, @object, graph);
        if (_tbox.Length == 0)
        {
            return own;
        }

        return graph.Match switch
        {
            GraphMatch.DefaultGraph => new Cursor(this, own, subject, predicate, @object, [TermHandle.None]),
            GraphMatch.Named => new Cursor(this, own, subject, predicate, @object, [graph.Graph]),
            GraphMatch.AnyNamed => new Cursor(this, own, subject, predicate, @object, null),
            _ => new Cursor(this, own, subject, predicate, @object, null, includeDefault: true),
        };
    }

    /// <inheritdoc />
    /// <remarks>The graph's own estimate plus the TBox graphs', as an estimate: a TBox quad may be in the graph too.</remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        CardinalityEstimate own = Inner.Estimate(subject, predicate, @object, graph);
        if (_tbox.Length == 0)
        {
            return own;
        }

        long total = own.IsUnknown ? 0 : own.Count.Value;
        long copies = graph.Match is GraphMatch.DefaultGraph or GraphMatch.Named ? 1 : Math.Max(1, NamedGraphs().Count);
        foreach (TermHandle tbox in _tbox)
        {
            CardinalityEstimate there = Inner.Estimate(subject, predicate, @object, GraphPattern.Named(tbox));
            if (there.IsUnknown)
            {
                return CardinalityEstimate.Unknown;
            }

            total += there.Count.Value * copies;
        }

        return own.IsUnknown ? CardinalityEstimate.Unknown : CardinalityEstimate.Estimated(new QuadCount(total));
    }

    /// <summary>The dataset's named graphs, enumerated once per request.</summary>
    private List<TermHandle> NamedGraphs()
    {
        if (_named is not null)
        {
            return _named;
        }

        HashSet<TermHandle> seen = new(Inner.TermComparer);
        List<TermHandle> named = [];
        using (IQuadCursor cursor = Inner.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.AnyNamed))
        {
            while (cursor.MoveNext())
            {
                if (seen.Add(cursor.Current.Graph))
                {
                    named.Add(cursor.Current.Graph);
                }
            }
        }

        _named = named;
        return named;
    }

    /// <summary>
    /// The graph's own quads, then the TBox graphs' relabelled to each graph
    /// matched, skipping those the graph holds itself.
    /// </summary>
    [DesignDecision(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Scope = ExceptionScope.HotPath)]
    private sealed class Cursor : IQuadCursor
    {
        private readonly TBoxMerge _owner;
        private readonly IQuadCursor _own;
        private readonly TermHandle _subject;
        private readonly TermHandle _predicate;
        private readonly TermHandle _object;
        private readonly bool _includeDefault;
        private List<TermHandle>? _targets;
        private bool _ownDone;
        private int _tboxIndex;
        private int _targetIndex;
        private IQuadCursor? _tboxCursor;
        private Quad _found;
        private bool _hasFound;

        internal Cursor(TBoxMerge owner, IQuadCursor own, TermHandle subject, TermHandle predicate, TermHandle @object, List<TermHandle>? targets, bool includeDefault = false)
        {
            _owner = owner;
            _own = own;
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _targets = targets;
            _includeDefault = includeDefault;
        }

        public Quad Current { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            if (!_ownDone)
            {
                if (_own.MoveNext())
                {
                    Current = _own.Current;
                    return true;
                }

                _ownDone = true;
            }

            while (_tboxIndex < _owner._tbox.Length)
            {
                if (!_hasFound)
                {
                    _tboxCursor ??= _owner.Inner.Match(_subject, _predicate, _object, GraphPattern.Named(_owner._tbox[_tboxIndex]));
                    if (!_tboxCursor.MoveNext())
                    {
                        _tboxCursor.Dispose();
                        _tboxCursor = null;
                        _tboxIndex++;
                        continue;
                    }

                    _found = _tboxCursor.Current;
                    _hasFound = true;
                    _targetIndex = 0;
                }

                // The TBox quad, once in each graph matched that does not hold it already.
                List<TermHandle> targets = Targets();
                while (_targetIndex < targets.Count)
                {
                    Quad candidate = new(_found.Subject, _found.Predicate, _found.Object, targets[_targetIndex++]);
                    if (!_owner.Inner.Contains(in candidate))
                    {
                        Current = candidate;
                        return true;
                    }
                }

                _hasFound = false;
            }

            return false;
        }

        private List<TermHandle> Targets()
        {
            if (_targets is not null)
            {
                return _targets;
            }

            List<TermHandle> targets = _includeDefault ? [TermHandle.None] : [];
            targets.AddRange(_owner.NamedGraphs());
            _targets = targets;
            return targets;
        }

        public void Dispose()
        {
            _own.Dispose();
            _tboxCursor?.Dispose();
        }
    }
}
