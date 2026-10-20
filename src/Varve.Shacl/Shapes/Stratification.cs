// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Validation;

namespace Varve.Shacl.Shapes;

/// <summary>
/// ADR 0150's static check: the dependency graph's strongly connected
/// components, each shape told whether conformance to it is a fixpoint, and a
/// problem naming the cycle for every component a negative or target edge
/// runs inside.
/// </summary>
internal static class Stratification
{
    /// <summary>Assigns components and returns the problems; an empty list means the shapes graph is stratifiable.</summary>
    internal static List<ShaclProblem> Apply(IReadOnlyList<Shape> shapes)
    {
        List<List<ShapeReference>> edges = new(shapes.Count);
        foreach (Shape shape in shapes)
        {
            List<ShapeReference> outgoing = [];
            foreach (Constraint constraint in shape.Constraints)
            {
                outgoing.AddRange(constraint.References);
            }

            foreach (int where in shape.TargetWhere)
            {
                outgoing.Add(new ShapeReference(where, Vocabulary.TargetWhere, Polarity.Target));
            }

            edges.Add(outgoing);
        }

        int[] component = Components(shapes.Count, edges);
        Dictionary<int, int> sizes = [];
        foreach (int c in component)
        {
            sizes[c] = sizes.TryGetValue(c, out int n) ? n + 1 : 1;
        }

        List<ShaclProblem> problems = [];
        HashSet<int> reported = [];
        for (int s = 0; s < shapes.Count; s++)
        {
            shapes[s].Component = component[s];
            bool selfLoop = edges[s].Exists(e => e.Target == s);
            shapes[s].Cyclic = sizes[component[s]] > 1 || selfLoop;

            foreach (ShapeReference edge in edges[s])
            {
                if (edge.Polarity == Polarity.Positive || component[edge.Target] != component[s] || !reported.Add(component[s]))
                {
                    continue;
                }

                List<RdfTerm> cycle = Cycle(shapes, edges, component, s, edge);
                problems.Add(new ShaclProblem(
                    ShaclProblemKind.UnstratifiableRecursion,
                    shapes[s].Node,
                    null,
                    "The shapes graph is not stratifiable (ADR 0150): the cycle " + Describe(cycle) + " runs through "
                        + (edge.Polarity == Polarity.Target ? "a target" : "a negative") + " reference, "
                        + Strings.NTriples(edge.Parameter) + ", so it has no defined meaning (SHACL 1.2 Core §6.6.3).",
                    cycle));
            }
        }

        return problems;
    }

    /// <summary>The cycle through <paramref name="edge"/>: source, parameter, target, and back to source by a shortest path inside the component.</summary>
    private static List<RdfTerm> Cycle(IReadOnlyList<Shape> shapes, List<List<ShapeReference>> edges, int[] component, int source, ShapeReference edge)
    {
        List<RdfTerm> cycle = [shapes[source].Node, edge.Parameter, shapes[edge.Target].Node];
        if (edge.Target == source)
        {
            return cycle;
        }

        // Breadth first from the edge's target back to its source, inside the component.
        Dictionary<int, (int From, RdfTerm Parameter)> previous = [];
        Queue<int> pending = new();
        pending.Enqueue(edge.Target);
        previous[edge.Target] = (-1, edge.Parameter);
        while (pending.Count > 0 && !previous.ContainsKey(source))
        {
            int current = pending.Dequeue();
            foreach (ShapeReference next in edges[current])
            {
                if (component[next.Target] == component[source] && !previous.ContainsKey(next.Target))
                {
                    previous[next.Target] = (current, next.Parameter);
                    pending.Enqueue(next.Target);
                }
            }
        }

        List<(RdfTerm Parameter, int Shape)> steps = [];
        for (int at = source; at != edge.Target; at = previous[at].From)
        {
            steps.Add((previous[at].Parameter, at));
        }

        steps.Reverse();
        foreach ((RdfTerm parameter, int shape) in steps)
        {
            cycle.Add(parameter);
            cycle.Add(shapes[shape].Node);
        }

        return cycle;
    }

    private static string Describe(List<RdfTerm> cycle)
    {
        List<string> parts = cycle.ConvertAll(Strings.NTriples);
        return string.Join(" → ", parts);
    }

    /// <summary>Tarjan's strongly connected components, iteratively: a component number per vertex.</summary>
    private static int[] Components(int count, List<List<ShapeReference>> edges)
    {
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
                if (e < edges[v].Count)
                {
                    work.Push((v, e + 1));
                    int w = edges[v][e].Target;
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
