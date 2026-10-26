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
/// A dependency graph's strongly connected components, numbered
/// dependencies first — a component depends only on components with smaller
/// numbers or on itself — and the cycles that make it unstratifiable: one per
/// component a negative, both or target edge runs inside (ADR 0150, ADR 0172).
/// </summary>
/// <typeparam name="TLabel">The caller's label of an edge.</typeparam>
[Contract(typeof(VarveDerivation.StratificationIsDerivations), Role = "the components and the refused cycles")]
public sealed class Stratification<TLabel>
{
    private readonly int[] _component;
    private readonly bool[] _cyclic;
    private readonly List<int>[] _members;

    private Stratification(int[] component, bool[] cyclic, List<int>[] members, IReadOnlyList<RefusedCycle<TLabel>> refused)
    {
        _component = component;
        _cyclic = cyclic;
        _members = members;
        Refused = refused;
    }

    /// <summary>The number of components.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public int ComponentCount => _members.Length;

    /// <summary>The component of <paramref name="vertex"/>; a dependency's component is never greater than its dependant's.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public int ComponentOf(int vertex) => _component[vertex];

    /// <summary>The vertices of <paramref name="component"/>, in ascending order.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public IReadOnlyList<int> Members(int component) => _members[component];

    /// <summary>Whether <paramref name="vertex"/> is on a cycle: its component has more than one vertex, or it depends on itself.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public bool IsCyclic(int vertex) => _cyclic[vertex];

    /// <summary>The cycles through a negative, both or target edge, one per component that has one; empty when the graph is stratifiable.</summary>
    public IReadOnlyList<RefusedCycle<TLabel>> Refused { get; }

    /// <summary>Whether no cycle runs through a negative, both or target edge.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public bool IsStratifiable => Refused.Count == 0;

    internal static Stratification<TLabel> Of(DependencyGraph<TLabel> graph)
    {
        int count = graph.VertexCount;
        int[] component = Components(graph);
        int components = 0;
        foreach (int c in component)
        {
            components = Math.Max(components, c + 1);
        }

        List<int>[] members = new List<int>[components];
        for (int c = 0; c < components; c++)
        {
            members[c] = [];
        }

        for (int v = 0; v < count; v++)
        {
            members[component[v]].Add(v);
        }

        bool[] cyclic = new bool[count];
        List<RefusedCycle<TLabel>> refused = [];
        bool[] reported = new bool[components];
        for (int v = 0; v < count; v++)
        {
            bool selfLoop = false;
            foreach (DependencyEdge<TLabel> edge in graph.Outgoing(v))
            {
                selfLoop |= edge.Target == v;
            }

            cyclic[v] = members[component[v]].Count > 1 || selfLoop;
            foreach (DependencyEdge<TLabel> edge in graph.Outgoing(v))
            {
                if (edge.Polarity == Polarity.Positive || component[edge.Target] != component[v] || reported[component[v]])
                {
                    continue;
                }

                reported[component[v]] = true;
                refused.Add(new RefusedCycle<TLabel>(edge, Cycle(graph, component, edge)));
            }
        }

        return new Stratification<TLabel>(component, cyclic, members, refused);
    }

    /// <summary>The cycle through <paramref name="edge"/>: the edge, then a shortest path inside its component from its target back to its source.</summary>
    private static List<DependencyEdge<TLabel>> Cycle(DependencyGraph<TLabel> graph, int[] component, DependencyEdge<TLabel> edge)
    {
        List<DependencyEdge<TLabel>> cycle = [edge];
        int source = edge.Source;
        if (edge.Target == source)
        {
            return cycle;
        }

        Dictionary<int, DependencyEdge<TLabel>> previous = [];
        Queue<int> pending = new();
        pending.Enqueue(edge.Target);
        HashSet<int> seen = [edge.Target];
        while (pending.Count > 0 && !previous.ContainsKey(source))
        {
            int current = pending.Dequeue();
            foreach (DependencyEdge<TLabel> next in graph.Outgoing(current))
            {
                if (component[next.Target] == component[source] && seen.Add(next.Target))
                {
                    previous[next.Target] = next;
                    pending.Enqueue(next.Target);
                }
            }
        }

        List<DependencyEdge<TLabel>> back = [];
        for (int at = source; at != edge.Target; at = previous[at].Source)
        {
            back.Add(previous[at]);
        }

        back.Reverse();
        cycle.AddRange(back);
        return cycle;
    }

    /// <summary>
    /// Tarjan's strongly connected components, iteratively. Tarjan closes a
    /// component only after every component it reaches, so numbering them in
    /// closing order numbers dependencies first.
    /// </summary>
    private static int[] Components(DependencyGraph<TLabel> graph)
    {
        int count = graph.VertexCount;
        int[] index = new int[count], low = new int[count], component = new int[count];
        bool[] onStack = new bool[count];
        Array.Fill(index, -1);
        Stack<int> stack = new();
        int next = 0, components = 0;
        for (int root = 0; root < count; root++)
        {
            if (index[root] >= 0)
            {
                continue;
            }

            Stack<(int Vertex, int Edge)> work = new();
            work.Push((root, 0));
            index[root] = low[root] = next++;
            stack.Push(root);
            onStack[root] = true;
            while (work.Count > 0)
            {
                (int v, int e) = work.Pop();
                IReadOnlyList<DependencyEdge<TLabel>> edges = graph.Outgoing(v);
                if (e < edges.Count)
                {
                    work.Push((v, e + 1));
                    int w = edges[e].Target;
                    if (index[w] < 0)
                    {
                        index[w] = low[w] = next++;
                        stack.Push(w);
                        onStack[w] = true;
                        work.Push((w, 0));
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }

                    continue;
                }

                if (low[v] == index[v])
                {
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        component[w] = components;
                    }
                    while (w != v);
                    components++;
                }

                if (work.Count > 0)
                {
                    int parent = work.Peek().Vertex;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }

        return component;
    }
}
