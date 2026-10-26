// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Store.Model;

namespace Varve.Shacl.Store;

/// <summary>
/// A binding's data graph (ADR 0170): the commit's state through the
/// binding's graph scope, without the shapes graph and without any graph
/// under <c>https://w3id.org/varve/ns#</c>, matched as one graph — their
/// union. Whether a graph is in it is decided once per graph handle.
/// </summary>
internal sealed class DataGraphSource : IQuadSource
{
    private const string Reserved = "https://w3id.org/varve/ns#";

    private readonly IQuadSource _all;
    private readonly IQuadSource _scoped;
    private readonly GraphScope _scope;
    private readonly RdfTerm _shapesGraph;
    private readonly Dictionary<TermHandle, bool> _decided;

    internal DataGraphSource(IQuadSource state, ShapesBinding binding)
    {
        _all = state;
        _scope = binding.Validates;
        _scoped = GraphScopedQuadSource.Wrap(state, binding.Validates);
        _shapesGraph = binding.ShapesGraph;
        _decided = new Dictionary<TermHandle, bool>(state.TermComparer);
    }

    public IEqualityComparer<TermHandle> TermComparer => _all.TermComparer;

    /// <summary>Whether a quad in <paramref name="graph"/> is in the data graph; <see cref="TermHandle.None"/> is the default graph.</summary>
    [DesignDecision(typeof(CommitTimeGatingUnderAShapesBinding.ValidatorReadsOverlayAndPinnedBase), Scope = ExceptionScope.HotPath)]
    internal bool Allows(TermHandle graph)
    {
        if (graph.IsNone)
        {
            return _scope.IncludesDefault;
        }

        if (_decided.TryGetValue(graph, out bool decided))
        {
            return decided;
        }

        decided = _all.TryExternalise(graph, out RdfTerm? term)
            && !term.Equals(_shapesGraph)
            && !term.Lexical.StartsWith(System.Text.Encoding.UTF8.GetBytes(Reserved))
            && _scope.Allows(term);
        _decided[graph] = decided;
        return decided;
    }

    public bool TryInternalise(RdfTerm term, out TermHandle handle) => _all.TryInternalise(term, out handle);

    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term) => _all.TryExternalise(handle, out term);

    [DesignDecision(typeof(CommitTimeGatingUnderAShapesBinding.ValidatorReadsOverlayAndPinnedBase), Scope = ExceptionScope.HotPath)]
    public bool TryGetTripleTermComponents(TermHandle handle, out TermHandle subject, out TermHandle predicate, out TermHandle @object) =>
        _all.TryGetTripleTermComponents(handle, out subject, out predicate, out @object);

    [DesignDecision(typeof(CommitTimeGatingUnderAShapesBinding.ValidatorReadsOverlayAndPinnedBase), Scope = ExceptionScope.HotPath)]
    public bool Contains(in Quad quad) => Allows(quad.Graph) && _scoped.Contains(in quad);

    public IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) => graph.Match switch
    {
        GraphMatch.DefaultGraph => _scope.IncludesDefault ? _scoped.Match(subject, predicate, @object, graph) : new Filtered(null, this),
        GraphMatch.Named => Allows(graph.Graph) ? _scoped.Match(subject, predicate, @object, graph) : new Filtered(null, this),
        _ => new Filtered(_scoped.Match(subject, predicate, @object, graph), this),
    };

    public CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        CardinalityEstimate inner = _scoped.Estimate(subject, predicate, @object, graph);
        return inner.IsUnknown || !inner.IsExact ? inner : CardinalityEstimate.Estimated(inner.Count);
    }

    public bool TryGetInlineValue(TermHandle handle, out InlineValue value) => _all.TryGetInlineValue(handle, out value);

    /// <summary>The inner cursor's quads in the data graph; an empty cursor when there is none.</summary>
    private sealed class Filtered(IQuadCursor? inner, DataGraphSource source) : IQuadCursor
    {
        public Quad Current => inner!.Current;

        [DesignDecision(typeof(CommitTimeGatingUnderAShapesBinding.ValidatorReadsOverlayAndPinnedBase), Scope = ExceptionScope.HotPath)]
        public bool MoveNext()
        {
            if (inner is null)
            {
                return false;
            }

            while (inner.MoveNext())
            {
                if (source.Allows(inner.Current.Graph))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose() => inner?.Dispose();
    }
}
