// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;

namespace Varve.Sparql.Store;

/// <summary>
/// A source whose default graph is one of another source's named graphs, and
/// whose named graphs are that source's: the dataset <c>WITH &lt;g&gt;</c>
/// gives a <c>WHERE</c> (SPARQL 1.1 Update §3.1.3, <c>sparql-update-store.md</c>
/// §5.3). A view, not a copy; handles and equality are the inner source's.
/// </summary>
internal sealed class DefaultGraphView : IQuadSource
{
    private readonly IQuadSource _inner;
    private readonly TermHandle _graph;

    /// <param name="inner">The source.</param>
    /// <param name="graph">The graph that is the default; <see cref="TermHandle.None"/> when the source has none of that name, which makes the default graph empty.</param>
    internal DefaultGraphView(IQuadSource inner, TermHandle graph)
    {
        _inner = inner;
        _graph = graph;
    }

    public IEqualityComparer<TermHandle> TermComparer => _inner.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _inner.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _inner.TryExternalise(handle, out term);

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _inner.TryGetInlineValue(handle, out value);

    public bool Contains(in Quad quad)
    {
        if (!quad.Graph.IsNone)
        {
            return _inner.Contains(in quad);
        }

        return !_graph.IsNone && _inner.Contains(new Quad(quad.Subject, quad.Predicate, quad.Object, _graph));
    }

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        graph.Match switch
        {
            GraphMatch.DefaultGraph => Default(subject, predicate, @object),
            GraphMatch.Any => new Concatenated(Default(subject, predicate, @object), _inner.Match(subject, predicate, @object, GraphPattern.AnyNamed)),
            _ => _inner.Match(subject, predicate, @object, graph),
        };

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        switch (graph.Match)
        {
            case GraphMatch.DefaultGraph:
                return _graph.IsNone ? CardinalityEstimate.Exact(new QuadCount(0)) : _inner.Estimate(subject, predicate, @object, GraphPattern.Named(_graph));

            case GraphMatch.Any:
                CardinalityEstimate named = _inner.Estimate(subject, predicate, @object, GraphPattern.AnyNamed);
                CardinalityEstimate own = Estimate(subject, predicate, @object, GraphPattern.DefaultGraph);

                if (named.IsUnknown || own.IsUnknown)
                {
                    return CardinalityEstimate.Unknown;
                }

                QuadCount count = new(named.Count.Value + own.Count.Value);
                return named.IsExact && own.IsExact ? CardinalityEstimate.Exact(count) : CardinalityEstimate.Estimated(count);

            default:
                return _inner.Estimate(subject, predicate, @object, graph);
        }
    }

    private IQuadCursor Default(TermHandle subject, TermHandle predicate, TermHandle @object) =>
        _graph.IsNone ? Empty.Instance : new Regraphed(_inner.Match(subject, predicate, @object, GraphPattern.Named(_graph)));

    // The named graph's quads, as quads of the default graph.
    private sealed class Regraphed(IQuadCursor inner) : IQuadCursor
    {
        public Quad Current { get; private set; }

        public bool MoveNext()
        {
            if (!inner.MoveNext())
            {
                return false;
            }

            Quad quad = inner.Current;
            Current = new Quad(quad.Subject, quad.Predicate, quad.Object, TermHandle.None);
            return true;
        }

        public void Dispose() => inner.Dispose();
    }

    private sealed class Concatenated(IQuadCursor first, IQuadCursor second) : IQuadCursor
    {
        private bool _firstDone;

        public Quad Current { get; private set; }

        public bool MoveNext()
        {
            if (!_firstDone)
            {
                if (first.MoveNext())
                {
                    Current = first.Current;
                    return true;
                }

                _firstDone = true;
            }

            if (second.MoveNext())
            {
                Current = second.Current;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }

    private sealed class Empty : IQuadCursor
    {
        internal static Empty Instance { get; } = new();

        public Quad Current => default;

        public bool MoveNext() => false;

        public void Dispose()
        {
        }
    }
}
