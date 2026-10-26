// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Derivation;

/// <summary>
/// A regular path compiled to an automaton by Thompson's construction
/// (ADR 0172): searched forwards for the nodes a path reaches, backwards for
/// the nodes that reach a node, and from a changed edge for the start nodes
/// whose image may change. Every search is a breadth-first search over pairs
/// of a node and a state, each pair visited once, so it terminates on cyclic
/// graphs. Immutable and safe to share.
/// </summary>
/// <typeparam name="TLabel">The caller's edge label.</typeparam>
[Contract(typeof(VarveDerivation.PathsAreRegularPaths), Role = "a path's automaton and its searches")]
public sealed class PathAutomaton<TLabel>
{
    private readonly int _start;
    private readonly int _accept;
    private readonly List<int>[] _epsilon;
    private readonly List<int>[] _epsilonBack;
    private readonly List<Step>[] _steps;
    private readonly List<Step>[] _stepsBack;
    private readonly List<(TLabel Label, bool Forward, int From)> _transitions;

    private PathAutomaton(int states, int start, int accept, List<(int From, int To)> epsilon, List<(int From, TLabel Label, bool Forward, int To)> transitions)
    {
        _start = start;
        _accept = accept;
        _epsilon = Lists<int>(states);
        _epsilonBack = Lists<int>(states);
        _steps = Lists<Step>(states);
        _stepsBack = Lists<Step>(states);
        _transitions = [];
        foreach ((int from, int to) in epsilon)
        {
            _epsilon[from].Add(to);
            _epsilonBack[to].Add(from);
        }

        foreach ((int from, TLabel label, bool forward, int to) in transitions)
        {
            _steps[from].Add(new Step(label, forward, to));
            _stepsBack[to].Add(new Step(label, forward, from));
            _transitions.Add((label, forward, from));
        }

        IsBounded = !HasCycle(states);
        AcceptsEmpty = Reaches(start, accept);
    }

    /// <summary>
    /// Whether the automaton has no cycle, so every walk it matches has a
    /// bounded length: no closure (ADR 0171's incremental paths).
    /// </summary>
    public bool IsBounded { get; }

    /// <summary>Whether the path matches the empty walk, so it reaches its start whatever the graph holds.</summary>
    public bool AcceptsEmpty { get; }

    /// <summary>The labels of the path's edges, each once per occurrence.</summary>
    [DesignDecision(typeof(VarveDerivation.DerivationAtLayerTwo), Scope = ExceptionScope.Boundary)]
    public IEnumerable<TLabel> Labels
    {
        get
        {
            foreach ((TLabel label, bool _, int _) in _transitions)
            {
                yield return label;
            }
        }
    }

    /// <summary>
    /// Adds to <paramref name="into"/> the nodes the path reaches from
    /// <paramref name="start"/> that <paramref name="seen"/> does not hold yet,
    /// and adds them to <paramref name="seen"/>.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.SearchesAllocatePerCall), Scope = ExceptionScope.HotPath)]
    public void Evaluate<TNode>(IPathGraph<TNode, TLabel> graph, TNode start, List<TNode> into, HashSet<TNode> seen)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Search(graph, [(start, _start)], forward: true, _accept, into, seen);
    }

    /// <summary>
    /// Adds to <paramref name="into"/> the nodes from which the path reaches
    /// <paramref name="end"/>, those <paramref name="seen"/> does not hold
    /// yet, and adds them to <paramref name="seen"/>.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.SearchesAllocatePerCall), Scope = ExceptionScope.HotPath)]
    public void Reverse<TNode>(IPathGraph<TNode, TLabel> graph, TNode end, List<TNode> into, HashSet<TNode> seen)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Search(graph, [(end, _accept)], forward: false, _start, into, seen);
    }

    /// <summary>
    /// Adds to <paramref name="into"/> the start nodes from which a walk of
    /// the path may use the edge <paramref name="subject"/> →
    /// <paramref name="obj"/> labelled <paramref name="label"/>, forwards or,
    /// through an inverse step, backwards: the nodes whose image the edge's
    /// assertion or retraction may change, when the search runs in the graph
    /// that holds the edge (ADR 0171). It errs wide: a walk need not
    /// complete after the edge.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.SearchesAllocatePerCall), Scope = ExceptionScope.HotPath)]
    public void Anchors<TNode>(IPathGraph<TNode, TLabel> graph, TNode subject, TLabel label, TNode obj, List<TNode> into, HashSet<TNode> seen)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<(TNode, int)> seeds = [];
        EqualityComparer<TLabel> labels = EqualityComparer<TLabel>.Default;
        foreach ((TLabel step, bool forward, int from) in _transitions)
        {
            if (labels.Equals(step, label))
            {
                // A forward step stands on the subject before the edge, an inverse step on the object.
                seeds.Add((forward ? subject : obj, from));
            }
        }

        if (seeds.Count > 0)
        {
            Search(graph, seeds, forward: false, _start, into, seen);
        }
    }

    /// <summary>
    /// The breadth-first search over (node, state) pairs: forwards from the
    /// seeds to the pairs at <paramref name="goal"/>, or backwards, each step
    /// read through the graph in the direction the walk runs.
    /// </summary>
    [DesignDecision(typeof(VarveDerivation.SearchesAllocatePerCall), Scope = ExceptionScope.HotPath)]
    private void Search<TNode>(IPathGraph<TNode, TLabel> graph, List<(TNode Node, int State)> seeds, bool forward, int goal, List<TNode> into, HashSet<TNode> seen)
    {
        HashSet<(TNode, int)> visited = new(new PairComparer<TNode>(graph.Nodes));
        Queue<(TNode Node, int State)> pending = new();
        foreach ((TNode, int) seed in seeds)
        {
            if (visited.Add(seed))
            {
                pending.Enqueue(seed);
            }
        }

        List<TNode> next = [];
        while (pending.Count > 0)
        {
            (TNode node, int state) = pending.Dequeue();
            if (state == goal && seen.Add(node))
            {
                into.Add(node);
            }

            foreach (int other in forward ? _epsilon[state] : _epsilonBack[state])
            {
                if (visited.Add((node, other)))
                {
                    pending.Enqueue((node, other));
                }
            }

            foreach (Step step in forward ? _steps[state] : _stepsBack[state])
            {
                next.Clear();

                // Forwards, a forward step reads objects; backwards it reads subjects. An inverse step the other way round.
                if (step.Forward == forward)
                {
                    graph.Objects(node, step.Label, next);
                }
                else
                {
                    graph.Subjects(step.Label, node, next);
                }

                foreach (TNode reached in next)
                {
                    if (visited.Add((reached, step.State)))
                    {
                        pending.Enqueue((reached, step.State));
                    }
                }
            }
        }
    }

    internal static PathAutomaton<TLabel> Build(RegularPath<TLabel> path)
    {
        Builder builder = new();
        (int start, int accept) = builder.Fragment(path);
        return new PathAutomaton<TLabel>(builder.States, start, accept, builder.Epsilon, builder.Transitions);
    }

    private bool Reaches(int from, int to)
    {
        HashSet<int> seen = [from];
        Stack<int> pending = new();
        pending.Push(from);
        while (pending.Count > 0)
        {
            int state = pending.Pop();
            if (state == to)
            {
                return true;
            }

            foreach (int next in _epsilon[state])
            {
                if (seen.Add(next))
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    private bool HasCycle(int states)
    {
        // 0 unvisited, 1 on the stack, 2 done: a back edge to 1 is a cycle.
        int[] mark = new int[states];
        for (int root = 0; root < states; root++)
        {
            if (mark[root] != 0)
            {
                continue;
            }

            Stack<(int State, int Edge)> work = new();
            work.Push((root, 0));
            mark[root] = 1;
            while (work.Count > 0)
            {
                (int state, int edge) = work.Pop();
                int epsilons = _epsilon[state].Count;
                if (edge < epsilons + _steps[state].Count)
                {
                    work.Push((state, edge + 1));
                    int next = edge < epsilons ? _epsilon[state][edge] : _steps[state][edge - epsilons].State;
                    if (mark[next] == 1)
                    {
                        return true;
                    }

                    if (mark[next] == 0)
                    {
                        mark[next] = 1;
                        work.Push((next, 0));
                    }

                    continue;
                }

                mark[state] = 2;
            }
        }

        return false;
    }

    private static List<T>[] Lists<T>(int count)
    {
        List<T>[] lists = new List<T>[count];
        for (int i = 0; i < count; i++)
        {
            lists[i] = [];
        }

        return lists;
    }

    private readonly struct Step(TLabel label, bool forward, int state)
    {
        internal TLabel Label { get; } = label;

        internal bool Forward { get; } = forward;

        internal int State { get; } = state;
    }

    private sealed class PairComparer<TNode>(IEqualityComparer<TNode> nodes) : IEqualityComparer<(TNode, int)>
    {
        public bool Equals((TNode, int) x, (TNode, int) y) => x.Item2 == y.Item2 && nodes.Equals(x.Item1, y.Item1);

        public int GetHashCode((TNode, int) obj) => HashCode.Combine(obj.Item1 is null ? 0 : nodes.GetHashCode(obj.Item1), obj.Item2);
    }

    /// <summary>Thompson's construction: each part a fragment with one start and one accepting state.</summary>
    private sealed class Builder
    {
        internal int States { get; private set; }

        internal List<(int From, int To)> Epsilon { get; } = [];

        internal List<(int From, TLabel Label, bool Forward, int To)> Transitions { get; } = [];

        internal (int Start, int Accept) Fragment(RegularPath<TLabel> path)
        {
            int start = States++;
            int accept = States++;
            switch (path)
            {
                case EdgePath<TLabel> edge:
                    Transitions.Add((start, edge.Label, edge.Forward, accept));
                    break;

                case CompoundPath<TLabel> { Kind: PathKind.Sequence } sequence:
                    {
                        int at = start;
                        foreach (RegularPath<TLabel> part in sequence.Parts)
                        {
                            (int s, int a) = Fragment(part);
                            Epsilon.Add((at, s));
                            at = a;
                        }

                        Epsilon.Add((at, accept));
                        break;
                    }

                case CompoundPath<TLabel> { Kind: PathKind.Alternative } alternative:
                    foreach (RegularPath<TLabel> part in alternative.Parts)
                    {
                        (int s, int a) = Fragment(part);
                        Epsilon.Add((start, s));
                        Epsilon.Add((a, accept));
                    }

                    break;

                case CompoundPath<TLabel> closure:
                    {
                        (int s, int a) = Fragment(closure.Parts[0]);
                        Epsilon.Add((start, s));
                        Epsilon.Add((a, accept));
                        if (closure.Kind != PathKind.OneOrMore)
                        {
                            Epsilon.Add((start, accept));
                        }

                        if (closure.Kind != PathKind.ZeroOrOne)
                        {
                            Epsilon.Add((a, s));
                        }

                        break;
                    }

                default:
                    throw new ArgumentException("An unknown kind of path.", nameof(path));
            }

            return (start, accept);
        }
    }
}
