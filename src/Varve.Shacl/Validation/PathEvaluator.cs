// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Shapes;

namespace Varve.Shacl.Validation;

/// <summary>
/// SHACL paths over the data graph (ADR 0152): the set of nodes a path
/// reaches from a node, sequences and alternatives deduplicated, closures as
/// least fixpoints by a breadth-first search with a visited set, a zero-length
/// path reaching the start whatever the graph holds.
/// </summary>
internal sealed class PathEvaluator(DataGraph data)
{
    private readonly Dictionary<RdfTerm, TermHandle?> _predicates = new(RdfTerm.Comparer);

    /// <summary>The nodes <paramref name="path"/> reaches from <paramref name="start"/>, distinct.</summary>
    internal List<Node> Values(ShapePath path, Node start)
    {
        List<Node> result = [];
        HashSet<Node> seen = new(data.Nodes);
        Step(path, start, forward: true, result, seen);
        return result;
    }

    /// <summary>The nodes reached from <paramref name="from"/> by one traversal of <paramref name="path"/>, forwards or backwards, added to <paramref name="into"/>.</summary>
    [DesignDecision(typeof(ShaclPathsInVarveShacl.ShaclPathsInShaclCore), Scope = ExceptionScope.HotPath)]
    private void Step(ShapePath path, Node from, bool forward, List<Node> into, HashSet<Node> seen)
    {
        switch (path)
        {
            case PredicatePath predicate:
                if (Predicate(predicate.Predicate) is TermHandle p)
                {
                    if (forward)
                    {
                        data.Objects(from, p, into, seen);
                    }
                    else
                    {
                        data.Subjects(p, from, into, seen);
                    }
                }

                return;

            case UnaryPath { Kind: UnaryPathKind.Inverse } inverse:
                Step(inverse.Inner, from, !forward, into, seen);
                return;

            case SequencePath sequence:
                {
                    List<Node> frontier = [from];
                    for (int i = 0; i < sequence.Steps.Length; i++)
                    {
                        ShapePath step = sequence.Steps[forward ? i : sequence.Steps.Length - 1 - i];
                        List<Node> next = [];
                        HashSet<Node> nextSeen = new(data.Nodes);
                        foreach (Node node in frontier)
                        {
                            Step(step, node, forward, next, nextSeen);
                        }

                        frontier = next;
                    }

                    foreach (Node node in frontier)
                    {
                        if (seen.Add(node))
                        {
                            into.Add(node);
                        }
                    }

                    return;
                }

            case AlternativePath alternative:
                foreach (ShapePath branch in alternative.Alternatives)
                {
                    Step(branch, from, forward, into, seen);
                }

                return;

            case UnaryPath { Kind: UnaryPathKind.ZeroOrOne } zeroOrOne:
                if (seen.Add(from))
                {
                    into.Add(from);
                }

                Step(zeroOrOne.Inner, from, forward, into, seen);
                return;

            case UnaryPath closure:
                {
                    // ⟦p⁺⟧ = μR. ⟦p⟧ ∪ (R ∘ ⟦p⟧), ⟦p*⟧ = Δ ∪ ⟦p⁺⟧: breadth first, each node expanded once.
                    bool zero = closure.Kind == UnaryPathKind.ZeroOrMore;
                    HashSet<Node> visited = new(data.Nodes);
                    Queue<Node> frontier = new();
                    if (zero && seen.Add(from))
                    {
                        into.Add(from);
                    }

                    frontier.Enqueue(from);
                    visited.Add(from);
                    List<Node> step = [];
                    while (frontier.Count > 0)
                    {
                        Node current = frontier.Dequeue();
                        step.Clear();
                        Step(closure.Inner, current, forward, step, new HashSet<Node>(data.Nodes));
                        foreach (Node reached in step)
                        {
                            if (seen.Add(reached))
                            {
                                into.Add(reached);
                            }

                            if (visited.Add(reached))
                            {
                                frontier.Enqueue(reached);
                            }
                        }
                    }

                    return;
                }
        }
    }

    private TermHandle? Predicate(RdfTerm iri)
    {
        if (!_predicates.TryGetValue(iri, out TermHandle? handle))
        {
            handle = data.TryHandle(iri, out TermHandle h) ? h : null;
            _predicates[iri] = handle;
        }

        return handle;
    }
}
