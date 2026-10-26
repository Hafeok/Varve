// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Derivation.Model;

namespace Varve.Derivation;

/// <summary>
/// A dependency graph over the vertices <c>0 … n−1</c>, each edge labelled
/// with a polarity (ADR 0172): what a stratification, a semi-naive
/// propagation and an affected-key computation all run over.
/// </summary>
/// <typeparam name="TLabel">The caller's label of an edge.</typeparam>
[Contract(typeof(VarveDerivation.DerivationAtLayerTwo), Role = "the dependency graph Varve.Shacl and Inference stratify by")]
public sealed class DependencyGraph<TLabel>
{
    private readonly List<DependencyEdge<TLabel>>[] _outgoing;
    private readonly List<DependencyEdge<TLabel>>[] _incoming;

    /// <summary>A graph of <paramref name="vertexCount"/> vertices and no edge.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public DependencyGraph(int vertexCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexCount);
        _outgoing = new List<DependencyEdge<TLabel>>[vertexCount];
        _incoming = new List<DependencyEdge<TLabel>>[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            _outgoing[i] = [];
            _incoming[i] = [];
        }
    }

    /// <summary>The number of vertices.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public int VertexCount => _outgoing.Length;

    /// <summary>Adds the dependency of <paramref name="source"/> on <paramref name="target"/>.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public void Add(int source, int target, Polarity polarity, TLabel label)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(source, VertexCount);
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(target, VertexCount);
        DependencyEdge<TLabel> edge = new(source, target, polarity, label);
        _outgoing[source].Add(edge);
        _incoming[target].Add(edge);
    }

    /// <summary>The dependencies of <paramref name="vertex"/>, in the order they were added.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public IReadOnlyList<DependencyEdge<TLabel>> Outgoing(int vertex) => _outgoing[vertex];

    /// <summary>The dependants of <paramref name="vertex"/>, in the order they were added.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public IReadOnlyList<DependencyEdge<TLabel>> Incoming(int vertex) => _incoming[vertex];

    /// <summary>
    /// The strongly connected components, dependencies first, and whether a
    /// negative or target edge runs inside one (ADR 0150): the stratification,
    /// or the cycles that refuse it.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.StratificationIsDerivations), Scope = ExceptionScope.Boundary)]
    public Stratification<TLabel> Stratify() => Stratification<TLabel>.Of(this);
}
