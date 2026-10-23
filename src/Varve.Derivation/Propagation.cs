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
/// Moves a node of a dependency's set to the nodes of its dependant's set
/// that it implies: for SHACL, the focus nodes whose value nodes include an
/// affected node (ADR 0171); for a rule, the heads a changed body fact
/// derives.
/// </summary>
/// <typeparam name="TNode">The caller's node.</typeparam>
/// <typeparam name="TLabel">The caller's edge label.</typeparam>
/// <param name="edge">The dependency, from its <see cref="DependencyEdge{TLabel}.Source"/> (the dependant) to its target.</param>
/// <param name="node">A node newly in the target's set.</param>
/// <param name="into">Where the dependant's implied nodes go; the propagation keeps only those it has not seen.</param>
[Contract(typeof(VarveDerivation.SemiNaivePropagation), Role = "the caller's transfer along one dependency")]
public delegate void PropagationTransfer<TNode, TLabel>(DependencyEdge<TLabel> edge, TNode node, List<TNode> into);

/// <summary>
/// Sets of nodes per vertex of a dependency graph, closed under its
/// dependencies to the least fixpoint (ADR 0172): every node in a vertex's
/// set is transferred along each edge into the vertex once, semi-naively, and
/// components are taken dependencies first, a cyclic one iterated until no
/// set grows. Sets only grow, and are finite, so it terminates.
/// </summary>
/// <typeparam name="TNode">The caller's node.</typeparam>
/// <typeparam name="TLabel">The caller's edge label.</typeparam>
[Contract(typeof(VarveDerivation.SemiNaivePropagation), Role = "affected keys propagated to the least fixpoint")]
public sealed class Propagation<TNode, TLabel>
{
    private readonly DependencyGraph<TLabel> _graph;
    private readonly Stratification<TLabel> _strata;
    private readonly HashSet<TNode>[] _sets;
    private readonly List<TNode>[] _frontier;

    /// <summary>Empty sets over <paramref name="graph"/>, whose stratification is <paramref name="strata"/>.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public Propagation(DependencyGraph<TLabel> graph, Stratification<TLabel> strata, IEqualityComparer<TNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(strata);
        ArgumentNullException.ThrowIfNull(nodes);
        _graph = graph;
        _strata = strata;
        _sets = new HashSet<TNode>[graph.VertexCount];
        _frontier = new List<TNode>[graph.VertexCount];
        for (int v = 0; v < graph.VertexCount; v++)
        {
            _sets[v] = new HashSet<TNode>(nodes);
            _frontier[v] = [];
        }
    }

    /// <summary>The set of <paramref name="vertex"/>.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public IReadOnlySet<TNode> SetOf(int vertex) => _sets[vertex];

    /// <summary>Adds <paramref name="node"/> to the set of <paramref name="vertex"/>; returns whether it was new.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public bool Seed(int vertex, TNode node)
    {
        if (!_sets[vertex].Add(node))
        {
            return false;
        }

        _frontier[vertex].Add(node);
        return true;
    }

    /// <summary>
    /// Closes every set under the dependencies, by <paramref name="transfer"/>:
    /// each new node of a target's set is transferred once along each edge
    /// into it, and what that implies joins the source's set.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.SearchesAllocatePerCall), Scope = ExceptionScope.HotPath)]
    public void Run(PropagationTransfer<TNode, TLabel> transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        List<TNode> implied = [];
        for (int component = 0; component < _strata.ComponentCount; component++)
        {
            IReadOnlyList<int> members = _strata.Members(component);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (int target in members)
                {
                    List<TNode> frontier = _frontier[target];
                    if (frontier.Count == 0)
                    {
                        continue;
                    }

                    List<TNode> batch = [.. frontier];
                    frontier.Clear();
                    foreach (DependencyEdge<TLabel> edge in _graph.Incoming(target))
                    {
                        foreach (TNode node in batch)
                        {
                            implied.Clear();
                            transfer(edge, node, implied);
                            foreach (TNode found in implied)
                            {
                                // A dependant in this component is iterated again; one in a later component waits its turn.
                                grew |= Seed(edge.Source, found) && _strata.ComponentOf(edge.Source) == component;
                            }
                        }
                    }
                }
            }
        }
    }
}
