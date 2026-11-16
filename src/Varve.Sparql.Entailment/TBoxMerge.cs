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
/// A quad source in which every graph is read merged with the TBox graphs of
/// the binding that covers it (ADR 0193 point 1): a <see cref="Match"/> of a
/// graph also matches its TBox graphs, each TBox quad reported in the matched
/// graph, and one over every named graph reports it once per covered named
/// graph. A TBox quad the matched graph also holds is reported once. A graph
/// no binding covers is read as it is, and so is a named graph the source
/// holds nothing of: under a caller's scope, one it does not read.
/// </summary>
/// <remarks>
/// Built once per request over the request's pinned, scoped source, which it
/// does not own; not shared between threads. Which binding covers a graph is
/// decided once per graph handle; the named graphs are enumerated the first
/// time a match over every named graph needs them, and then held for the
/// request.
/// </remarks>
[Contract(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Role = "every graph read with the TBox graphs")]
internal sealed class TBoxMerge : IQuadSource, IDistinctTermSource
{
    private readonly TBoxCover[] _covers;
    private readonly TermHandle[][] _tbox;
    private readonly Dictionary<TermHandle, int> _coverOf;
    private int _coverOfDefault = -2;
    private List<TermHandle>? _named;
    private List<TermHandle>[]? _coveredNamed;

    /// <summary><paramref name="inner"/>, each graph merged with the TBox graphs of the one of <paramref name="covers"/> that covers it.</summary>
    internal TBoxMerge(IQuadSource inner, IReadOnlyList<TBoxCover> covers)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(covers);
        Inner = inner;
        _coverOf = new Dictionary<TermHandle, int>(inner.TermComparer);
        _covers = [.. covers];
        _tbox = new TermHandle[_covers.Length][];
        for (int c = 0; c < _covers.Length; c++)
        {
            List<TermHandle> handles = [];
            foreach (RdfTerm graph in _covers[c].TBoxGraphs)
            {
                if (inner.TryInternalise(graph, out TermHandle handle) && !handles.Exists(h => inner.TermComparer.Equals(h, handle)))
                {
                    handles.Add(handle);
                }
            }

            _tbox[c] = [.. handles];
        }
    }

    /// <summary>The bindings, in the order given.</summary>
    internal IEnumerable<CoverIndex> Covers()
    {
        for (int c = 0; c < _covers.Length; c++)
        {
            yield return new CoverIndex(c);
        }
    }

    /// <summary>A binding's TBox graphs the source holds, as handles.</summary>
    internal IReadOnlyList<TermHandle> TBoxGraphsOf(CoverIndex cover) => _tbox[cover.Value];

    /// <inheritdoc />
    /// <remarks>The graph's own distinct terms and those of its binding's TBox graphs, each by the inner's seek where it has one.</remarks>
    public bool TryDistinctPredicates(GraphPattern graph, [NotNullWhen(true)] out ITermCursor? predicates)
    {
        predicates = Distinct(graph, (source, one) => DistinctTerms.Predicates(source, one));
        return true;
    }

    /// <inheritdoc />
    /// <remarks>The graph's own distinct terms and those of its binding's TBox graphs, each by the inner's seek where it has one.</remarks>
    public bool TryDistinctObjects(TermHandle predicate, GraphPattern graph, [NotNullWhen(true)] out ITermCursor? objects)
    {
        objects = Distinct(graph, (source, one) => DistinctTerms.Objects(source, predicate, one));
        return true;
    }

    private TermCursor Distinct(GraphPattern graph, Func<IQuadSource, GraphPattern, List<TermHandle>> of)
    {
        HashSet<TermHandle> seen = new(Inner.TermComparer);
        List<TermHandle> terms = [];
        Add(of(Inner, graph));
        if (TryGetCover(graph.Match == GraphMatch.Named ? graph.Graph : TermHandle.None, out CoverIndex cover))
        {
            foreach (TermHandle tbox in TBoxGraphsOf(cover))
            {
                Add(of(Inner, GraphPattern.Named(tbox)));
            }
        }

        return new TermCursor(terms);

        void Add(List<TermHandle> found)
        {
            foreach (TermHandle term in found)
            {
                if (seen.Add(term))
                {
                    terms.Add(term);
                }
            }
        }
    }

    private sealed class TermCursor(List<TermHandle> terms) : ITermCursor
    {
        private int _at = -1;

        public TermHandle Current => terms[_at];

        public bool MoveNext() => ++_at < terms.Count;

        public void Dispose()
        {
        }
    }

    /// <summary>The binding that covers a graph, if one does; <see cref="TermHandle.None"/> is the default graph.</summary>
    internal bool TryGetCover(TermHandle graph, out CoverIndex cover)
    {
        int of = _covers.Length == 0 ? -1 : CoverOf(graph);
        cover = new CoverIndex(of);
        return of >= 0;
    }

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

        TermHandle[] tbox = TBoxOf(quad.Graph);
        for (int i = 0; i < tbox.Length; i++)
        {
            Quad there = new(quad.Subject, quad.Predicate, quad.Object, tbox[i]);
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
        if (_covers.Length == 0)
        {
            return own;
        }

        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
            case GraphMatch.Named:
                TermHandle target = graph.Match == GraphMatch.Named ? graph.Graph : TermHandle.None;
                int cover = CoverOf(target);
                return cover < 0 || _tbox[cover].Length == 0 ? own : new Cursor(this, own, subject, predicate, @object, cover, target);
            case GraphMatch.AnyNamed:
                return new Cursor(this, own, subject, predicate, @object, includeDefault: false);
            default:
                return new Cursor(this, own, subject, predicate, @object, includeDefault: true);
        }
    }

    /// <inheritdoc />
    /// <remarks>The graph's own estimate plus its TBox graphs', as an estimate: a TBox quad may be in the graph too.</remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        CardinalityEstimate own = Inner.Estimate(subject, predicate, @object, graph);
        if (_covers.Length == 0 || own.IsUnknown)
        {
            return own;
        }

        long total = own.Count.Value;
        for (int c = 0; c < _covers.Length; c++)
        {
            long copies = graph.Match switch
            {
                GraphMatch.DefaultGraph => CoverOf(TermHandle.None) == c ? 1 : 0,
                GraphMatch.Named => CoverOf(graph.Graph) == c ? 1 : 0,
                GraphMatch.AnyNamed => CoveredNamedGraphs(c).Count,
                _ => CoveredNamedGraphs(c).Count + (CoverOf(TermHandle.None) == c ? 1 : 0),
            };

            if (copies == 0)
            {
                continue;
            }

            foreach (TermHandle tbox in _tbox[c])
            {
                CardinalityEstimate there = Inner.Estimate(subject, predicate, @object, GraphPattern.Named(tbox));
                if (there.IsUnknown)
                {
                    return CardinalityEstimate.Unknown;
                }

                total += there.Count.Value * copies;
            }
        }

        return CardinalityEstimate.Estimated(new QuadCount(total));
    }

    // The index of the binding that covers a graph, or -1, decided once per handle.
    [DesignDecision(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Scope = ExceptionScope.HotPath)]
    private int CoverOf(TermHandle graph)
    {
        if (graph.IsNone)
        {
            if (_coverOfDefault == -2)
            {
                _coverOfDefault = Decide(null);
            }

            return _coverOfDefault;
        }

        if (!_coverOf.TryGetValue(graph, out int cover))
        {
            // A named graph the source holds no quad of is no graph of the
            // dataset as the caller reads it: nothing is merged into it.
            cover = Inner.TryExternalise(graph, out RdfTerm? term) && Holds(graph) ? Decide(term) : -1;
            _coverOf[graph] = cover;
        }

        return cover;

        bool Holds(TermHandle named)
        {
            using IQuadCursor cursor = Inner.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(named));
            return cursor.MoveNext();
        }

        int Decide(RdfTerm? term)
        {
            for (int c = 0; c < _covers.Length; c++)
            {
                if (_covers[c].Covers(term))
                {
                    return c;
                }
            }

            return -1;
        }
    }

    /// <summary>The TBox graphs merged into a single graph, none when no binding covers it or the source holds nothing of it.</summary>
    internal TermHandle[] TBoxGraphsOf(GraphPattern graph) => TBoxOf(graph.Match == GraphMatch.Named ? graph.Graph : TermHandle.None);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private TermHandle[] TBoxOf(TermHandle graph)
    {
        int cover = _covers.Length == 0 ? -1 : CoverOf(graph);
        return cover < 0 ? [] : _tbox[cover];
    }

    /// <summary>The dataset's named graphs, enumerated once per request.</summary>
    internal List<TermHandle> NamedGraphs()
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

    /// <summary>The named graphs a binding covers, partitioned once per request.</summary>
    internal List<TermHandle> CoveredNamedGraphs(CoverIndex cover) => CoveredNamedGraphs(cover.Value);

    private List<TermHandle> CoveredNamedGraphs(int cover)
    {
        if (_coveredNamed is null)
        {
            _coveredNamed = new List<TermHandle>[_covers.Length];
            for (int c = 0; c < _covers.Length; c++)
            {
                _coveredNamed[c] = [];
            }

            foreach (TermHandle graph in NamedGraphs())
            {
                int of = CoverOf(graph);
                if (of >= 0)
                {
                    _coveredNamed[of].Add(graph);
                }
            }
        }

        return _coveredNamed[cover];
    }

    /// <summary>
    /// The graph's own quads, then each binding's TBox graphs' relabelled to
    /// each graph matched that it covers, skipping those the graph holds
    /// itself.
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
        private readonly TermHandle _single;
        private readonly bool _isSingle;
        private bool _ownDone;
        private int _cover;
        private readonly int _lastCover;
        private List<TermHandle>? _targets;
        private int _tboxIndex;
        private int _targetIndex;
        private IQuadCursor? _tboxCursor;
        private Quad _found;
        private bool _hasFound;

        // One graph, covered by one binding.
        internal Cursor(TBoxMerge owner, IQuadCursor own, TermHandle subject, TermHandle predicate, TermHandle @object, int cover, TermHandle graph)
            : this(owner, own, subject, predicate, @object, includeDefault: false)
        {
            _cover = cover;
            _lastCover = cover;
            _single = graph;
            _isSingle = true;
        }

        // Every named graph, and the default graph too when asked.
        internal Cursor(TBoxMerge owner, IQuadCursor own, TermHandle subject, TermHandle predicate, TermHandle @object, bool includeDefault)
        {
            _owner = owner;
            _own = own;
            _subject = subject;
            _predicate = predicate;
            _object = @object;
            _includeDefault = includeDefault;
            _lastCover = owner._covers.Length - 1;
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

            while (_cover <= _lastCover)
            {
                TermHandle[] tbox = _owner._tbox[_cover];
                if (_tboxIndex >= tbox.Length || (_tboxIndex == 0 && _tboxCursor is null && Targets().Count == 0))
                {
                    _cover++;
                    _tboxIndex = 0;
                    _targets = null;
                    continue;
                }

                if (!_hasFound)
                {
                    _tboxCursor ??= _owner.Inner.Match(_subject, _predicate, _object, GraphPattern.Named(tbox[_tboxIndex]));
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

        // The graphs matched that the current binding covers.
        private List<TermHandle> Targets()
        {
            if (_targets is not null)
            {
                return _targets;
            }

            if (_isSingle)
            {
                _targets = [_single];
                return _targets;
            }

            List<TermHandle> covered = _owner.CoveredNamedGraphs(_cover);
            if (_includeDefault && _owner.CoverOf(TermHandle.None) == _cover)
            {
                _targets = [TermHandle.None, .. covered];
            }
            else
            {
                _targets = covered;
            }

            return _targets;
        }

        public void Dispose()
        {
            _own.Dispose();
            _tboxCursor?.Dispose();
        }
    }
}

/// <summary>A binding's place among a merge's bindings.</summary>
/// <param name="Value">Its index.</param>
internal readonly record struct CoverIndex(int Value);
