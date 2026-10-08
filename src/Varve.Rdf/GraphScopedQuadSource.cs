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
/// A quad source that shows only the graphs of a <see cref="GraphScope"/>
/// (ADR 0107): every <see cref="Match"/>, <see cref="Contains"/> and
/// <see cref="Estimate"/> is filtered by graph, so that a graph outside the
/// scope is unobservable through any pattern or enumeration, and a dataset
/// clause naming it names an empty graph. Terms pass through unfiltered: a
/// term's existence is not a graph's.
/// </summary>
/// <remarks>
/// Built once per request over the request's view, and not shared between
/// threads: the prefix decisions it memoises are kept in a plain map. A scope
/// of every graph is not wrapped (<see cref="Wrap"/>): the unwrapped source
/// costs what it did.
/// </remarks>
public sealed class GraphScopedQuadSource : IQuadSource
{
    private readonly IQuadSource _inner;
    private readonly bool _default;
    private readonly HashSet<TermHandle> _named;
    private readonly Dictionary<TermHandle, bool>? _decided;

    /// <summary><paramref name="inner"/> seen through <paramref name="scope"/>.</summary>
    public GraphScopedQuadSource(IQuadSource inner, GraphScope scope)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(scope);
        _inner = inner;
        Scope = scope;
        _default = scope.IncludesDefault;
        _named = new HashSet<TermHandle>(inner.TermComparer);

        // The explicit graphs the source knows, resolved once; one it does
        // not know holds no quad here, and a quad arriving later (a feed's)
        // is the feed's to decide by term.
        foreach (RdfTerm graph in scope.Graphs)
        {
            if (inner.TryInternalise(graph, out TermHandle handle))
            {
                _named.Add(handle);
            }
        }

        _decided = scope.HasPrefixes ? new Dictionary<TermHandle, bool>(inner.TermComparer) : null;
    }

    /// <summary>The scope this source shows.</summary>
    public GraphScope Scope { get; }

    /// <summary><paramref name="inner"/> through <paramref name="scope"/>, or <paramref name="inner"/> itself when the scope is every graph.</summary>
    public static IQuadSource Wrap(IQuadSource inner, GraphScope scope)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(scope);
        return scope.IsAll ? inner : new GraphScopedQuadSource(inner, scope);
    }

    /// <inheritdoc />
    public IEqualityComparer<TermHandle> TermComparer => _inner.TermComparer;

    /// <inheritdoc />
    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _inner.TryInternalise(term, out handle);

    /// <inheritdoc />
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _inner.TryExternalise(handle, out term);

    /// <inheritdoc />
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Contains(in Quad quad) => Allows(quad.Graph) && _inner.Contains(in quad);

    /// <inheritdoc />
    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
                return Scope.IncludesDefault ? _inner.Match(subject, predicate, @object, graph) : EmptyCursor.Instance;
            case GraphMatch.Named:
                return Allows(graph.Graph) ? _inner.Match(subject, predicate, @object, graph) : EmptyCursor.Instance;
            default:
                if (!Scope.IncludesDefault && _named.Count == 0 && _decided is null)
                {
                    return EmptyCursor.Instance;
                }

                return new Cursor(_inner.Match(subject, predicate, @object, graph), this);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The inner's answer for a pattern inside the scope; exactly zero for a
    /// graph outside it; for a pattern over several graphs, the inner's count
    /// as an estimate, since it bounds what the filter lets through (ADR 0049).
    /// </remarks>
    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
                return Scope.IncludesDefault ? _inner.Estimate(subject, predicate, @object, graph) : CardinalityEstimate.Exact(new QuadCount(0));
            case GraphMatch.Named:
                return Allows(graph.Graph) ? _inner.Estimate(subject, predicate, @object, graph) : CardinalityEstimate.Exact(new QuadCount(0));
            default:
                if (!Scope.IncludesDefault && _named.Count == 0 && _decided is null)
                {
                    return CardinalityEstimate.Exact(new QuadCount(0));
                }

                CardinalityEstimate inner = _inner.Estimate(subject, predicate, @object, graph);
                return inner.IsUnknown || !inner.IsExact ? inner : CardinalityEstimate.Estimated(inner.Count);
        }
    }

    /// <inheritdoc />
    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _inner.TryGetInlineValue(handle, out value);

    /// <summary>Whether a quad in <paramref name="graph"/> is shown; <see cref="TermHandle.None"/> is the default graph.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Allows(TermHandle graph) =>
        graph.IsNone ? _default : InNamed(graph) || (_decided is not null && ByPrefix(graph));

    [DesignDecision(typeof(GraphLevelAuthorisation.ScopeFilterIsASetLookup), Scope = ExceptionScope.HotPath)]
    private bool InNamed(TermHandle graph) => _named.Contains(graph);

    // Decided once per graph handle: the first quad of a graph pays its
    // externalisation and the prefix comparison; every later one is a lookup.
    [DesignDecision(typeof(GraphLevelAuthorisation.PrefixDecisionMemoisedPerGraph), Scope = ExceptionScope.HotPath)]
    private bool ByPrefix(TermHandle graph)
    {
        if (_decided!.TryGetValue(graph, out bool decided))
        {
            return decided;
        }

        decided = _inner.TryExternalise(graph, out RdfTerm? term) && term.Kind == RdfTermKind.Iri && Scope.AllowsByPrefix(term.Lexical);
        _decided[graph] = decided;
        return decided;
    }

    private sealed class Cursor(IQuadCursor inner, GraphScopedQuadSource owner) : IQuadCursor
    {
        public Quad Current => inner.Current;

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public bool MoveNext()
        {
            while (inner.MoveNext())
            {
                if (owner.Allows(inner.Current.Graph))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose() => inner.Dispose();
    }

    private sealed class EmptyCursor : IQuadCursor
    {
        internal static EmptyCursor Instance { get; } = new();

        public Quad Current => default;

        public bool MoveNext() => false;

        public void Dispose()
        {
        }
    }
}
