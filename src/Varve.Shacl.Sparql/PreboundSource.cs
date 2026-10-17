// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Shacl.Sparql;

/// <summary>
/// The data graph as the evaluator sees it for one query: the data graph is
/// the query's default graph, and the blank nodes this query pre-binds are
/// found by the handles they came with, since a source looks up no blank node
/// by label (ADR 0044). Everything else is the data source's own.
/// </summary>
internal sealed class PreboundSource : IQuadSource
{
    private readonly IQuadSource _inner;
    private readonly GraphPattern _dataGraph;
    private readonly Dictionary<RdfTerm, TermHandle> _bound;

    internal PreboundSource(IQuadSource inner, GraphPattern dataGraph, Dictionary<RdfTerm, TermHandle> bound)
    {
        _inner = inner;
        _dataGraph = dataGraph;
        _bound = bound;
    }

    public IEqualityComparer<TermHandle> TermComparer => _inner.TermComparer;

    public bool TryInternalise(RdfTerm term, out TermHandle handle) =>
        _bound.TryGetValue(term, out handle) || _inner.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, out RdfTerm term) => _inner.TryExternalise(handle, out term!);

    public bool Contains(in Quad quad) =>
        quad.IsDefaultGraph && _dataGraph.Match == GraphMatch.Named
            ? _inner.Contains(new Quad(quad.Subject, quad.Predicate, quad.Object, _dataGraph.Graph))
            : _inner.Contains(quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _inner.Match(subject, predicate, @object, Map(graph));

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        _inner.Estimate(subject, predicate, @object, Map(graph));

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _inner.TryGetInlineValue(handle, out value);

    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _inner.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    /// <summary>The query's default graph is the data graph (SHACL 1.2 Core §6.2).</summary>
    private GraphPattern Map(GraphPattern graph) => graph.Match == GraphMatch.DefaultGraph ? _dataGraph : graph;
}
