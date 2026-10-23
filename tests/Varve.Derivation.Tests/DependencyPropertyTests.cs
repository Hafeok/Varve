// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Derivation.Model;
using Xunit;

namespace Varve.Derivation.Tests;

/// <summary>
/// ADR 0172's dependency graph and propagation against naive oracles:
/// reachability for the components and the refused cycles, and iteration of
/// every edge until nothing grows for the least fixpoint.
/// </summary>
public class DependencyPropertyTests
{
    /// <summary>Generated cases per property, as reported in the pull request.</summary>
    internal const int Iterations = 3_000;

    private static readonly Gen<(int Vertices, (int S, int T, Polarity P)[] Edges)> Graphs =
        from vertices in Gen.Int[1, 7]
        from edges in Gen.Select(Gen.Int[0, vertices - 1], Gen.Int[0, vertices - 1], Gen.Enum<Polarity>()).Array[0, 12]
        select (vertices, edges);

    [Fact]
    public void Components_are_mutual_reachability_numbered_dependencies_first()
    {
        Graphs.Sample(g =>
        {
            Stratification<int> strata = Build(g.Vertices, g.Edges).Stratify();
            bool[,] reach = Reach(g.Vertices, g.Edges);
            for (int a = 0; a < g.Vertices; a++)
            {
                for (int b = 0; b < g.Vertices; b++)
                {
                    bool same = a == b || (reach[a, b] && reach[b, a]);
                    Assert.Equal(same, strata.ComponentOf(a) == strata.ComponentOf(b));
                }

                Assert.Equal(reach[a, a], strata.IsCyclic(a));
            }

            foreach ((int s, int t, Polarity _) in g.Edges)
            {
                Assert.True(strata.ComponentOf(t) <= strata.ComponentOf(s), $"{t}, a dependency of {s}, is numbered after it");
            }
        }, iter: Iterations);
    }

    [Fact]
    public void A_graph_is_refused_exactly_when_a_cycle_runs_through_a_non_positive_edge()
    {
        Graphs.Sample(g =>
        {
            Stratification<int> strata = Build(g.Vertices, g.Edges).Stratify();
            bool[,] reach = Reach(g.Vertices, g.Edges);
            bool expected = g.Edges.Any(e => e.P != Polarity.Positive && (e.T == e.S || reach[e.T, e.S]));
            Assert.Equal(!expected, strata.IsStratifiable);
            foreach (RefusedCycle<int> cycle in strata.Refused)
            {
                Assert.NotEqual(Polarity.Positive, cycle.Edge.Polarity);
                Assert.Equal(cycle.Edge, cycle.Edges[0]);
                for (int i = 0; i < cycle.Edges.Count; i++)
                {
                    DependencyEdge<int> next = cycle.Edges[(i + 1) % cycle.Edges.Count];
                    Assert.Equal(cycle.Edges[i].Target, next.Source);
                }
            }
        }, iter: Iterations);
    }

    [Fact]
    public void Propagation_is_the_least_fixpoint()
    {
        Gen.Select(Graphs, Gen.Select(Gen.Int[0, 6], Gen.Int[0, 5]).Array[0, 6]).Sample((g, seeds) =>
        {
            DependencyGraph<int> graph = Build(g.Vertices, g.Edges);
            Propagation<int, int> propagation = new(graph, graph.Stratify(), EqualityComparer<int>.Default);
            HashSet<int>[] naive = [.. Enumerable.Range(0, g.Vertices).Select(_ => new HashSet<int>())];
            foreach ((int v, int n) in seeds)
            {
                if (v < g.Vertices)
                {
                    propagation.Seed(v, n);
                    naive[v].Add(n);
                }
            }

            // A transfer that depends on the edge: a node moves to node + 1 mod 7 along a negative edge, and to itself otherwise.
            static int Moved(DependencyEdge<int> edge, int node) => edge.Polarity == Polarity.Negative ? (node + 1) % 7 : node;
            propagation.Run((edge, node, into) => into.Add(Moved(edge, node)));

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < g.Edges.Length; i++)
                {
                    (int s, int t, Polarity p) = g.Edges[i];
                    DependencyEdge<int> edge = new(s, t, p, i);
                    foreach (int n in naive[t].ToList())
                    {
                        grew |= naive[s].Add(Moved(edge, n));
                    }
                }
            }

            for (int v = 0; v < g.Vertices; v++)
            {
                Assert.Equal(naive[v].OrderBy(x => x), propagation.SetOf(v).OrderBy(x => x));
            }
        }, iter: Iterations);
    }

    private static DependencyGraph<int> Build(int vertices, (int S, int T, Polarity P)[] edges)
    {
        DependencyGraph<int> graph = new(vertices);
        for (int i = 0; i < edges.Length; i++)
        {
            graph.Add(edges[i].S, edges[i].T, edges[i].P, i);
        }

        return graph;
    }

    /// <summary>reach[a, b]: a path of one or more edges from a to b.</summary>
    private static bool[,] Reach(int vertices, (int S, int T, Polarity P)[] edges)
    {
        bool[,] reach = new bool[vertices, vertices];
        foreach ((int s, int t, Polarity _) in edges)
        {
            reach[s, t] = true;
        }

        for (int k = 0; k < vertices; k++)
        {
            for (int i = 0; i < vertices; i++)
            {
                for (int j = 0; j < vertices; j++)
                {
                    reach[i, j] |= reach[i, k] && reach[k, j];
                }
            }
        }

        return reach;
    }
}
