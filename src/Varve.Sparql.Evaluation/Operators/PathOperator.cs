// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation.Execution;

namespace Varve.Sparql.Evaluation.Operators;

/// <summary>
/// A path pattern that normalisation left as a path — a closure or a negated
/// property set (ADR 0054, <c>sparql-evaluation.md</c> §6.10) — evaluated per
/// §18.4: <c>ALP</c> from each start node with a visited set per start, the
/// node set of the active graph as the starts when both ends are unbound.
/// </summary>
internal sealed class PathOperator : Operator
{
    private readonly PathEnd _subject;
    private readonly PathEnd _object;
    private readonly PropertyPath _path;
    private readonly ScanCursor _scan = new();
    private bool _zeroNeedsGraph;
    private Dictionary<RdfTerm, TermHandle?>? _predicates;
    private Exec? _for;

    internal PathOperator(PathEnd subject, PropertyPath path, PathEnd @object)
    {
        _subject = subject;
        _path = path;
        _object = @object;
    }

    internal override bool Substitutable => true;

    internal override IEnumerator<ulong[]> Open(Exec exec, ulong[] input, ActiveGraph graph)
    {
        if (!ReferenceEquals(_for, exec))
        {
            _predicates = new Dictionary<RdfTerm, TermHandle?>(RdfTerm.Comparer);
            _for = exec;
        }

        if (graph.Mode == GraphMode.Slot && input[graph.Slot] == 0)
        {
            return EachGraph(exec, input, graph.Slot);
        }

        if (graph.Mode == GraphMode.Slot)
        {
            if (!exec.TryGetSourceHandle(Rows.Get(input, exec.Width, graph.Slot), out TermHandle bound))
            {
                return Solutions.Empty;
            }

            graph = ActiveGraph.Named(bound);
        }

        return Evaluate(exec, input, graph);
    }

    private IEnumerator<ulong[]> EachGraph(Exec exec, ulong[] input, int slot)
    {
        foreach (TermHandle named in exec.NamedGraphs().ToArray())
        {
            ulong[] seeded = Rows.Copy(input);
            Rows.Set(seeded, exec.Width, slot, exec.FromSource(named));
            using IEnumerator<ulong[]> solutions = Evaluate(exec, seeded, ActiveGraph.Named(named));
            while (solutions.MoveNext())
            {
                yield return solutions.Current;
            }
        }
    }

    private IEnumerator<ulong[]> Evaluate(Exec exec, ulong[] input, ActiveGraph graph)
    {
        TermRef x = _subject.Resolve(exec, input);
        TermRef y = _object.Resolve(exec, input);

        // §18.4: a zero-length path from a written term reaches that term
        // whatever the graph holds; with both ends variables, the start comes
        // from nodes(G). A variable bound by the incoming solution is still a
        // variable, so the node must be in the graph (the suite's
        // values_and_path).
        _zeroNeedsGraph = _subject.Slot >= 0 && _object.Slot >= 0;

        if (x.IsBound && y.IsBound)
        {
            if (_path is NegatedPropertySet bothBound)
            {
                // Once per half that holds: !(a|^b) is alt(NPS(a), inv(NPS(b))), §18.2.2.3.
                foreach (TermRef node in Negated(exec, graph, x, bothBound, forward: true))
                {
                    if (exec.TermEquals(node, y))
                    {
                        yield return Rows.Copy(input);
                    }
                }

                yield break;
            }

            if (Reaches(exec, graph, x, _path, y))
            {
                yield return Rows.Copy(input);
            }

            yield break;
        }

        if (x.IsBound)
        {
            foreach (TermRef node in Targets(exec, graph, x, _path, forward: true))
            {
                yield return Bind(exec, input, _object.Slot, node);
            }

            yield break;
        }

        if (y.IsBound)
        {
            foreach (TermRef node in Targets(exec, graph, y, _path, forward: false))
            {
                yield return Bind(exec, input, _subject.Slot, node);
            }

            yield break;
        }

        // Both ends unbound: every node of the active graph is a start (§18.4, nodes(G)).
        if (_path is NegatedPropertySet negated)
        {
            foreach ((TermRef s, TermRef o) in AllNegated(exec, graph, negated))
            {
                if (_subject.Slot == _object.Slot)
                {
                    if (exec.TermEquals(s, o))
                    {
                        yield return Bind(exec, input, _subject.Slot, s);
                    }

                    continue;
                }

                yield return Bind(exec, Bind(exec, input, _subject.Slot, s), _object.Slot, o);
            }

            yield break;
        }

        foreach (TermRef start in Nodes(exec, graph))
        {
            if (_subject.Slot == _object.Slot)
            {
                if (Reaches(exec, graph, start, _path, start))
                {
                    yield return Bind(exec, input, _subject.Slot, start);
                }

                continue;
            }

            ulong[] seeded = Bind(exec, input, _subject.Slot, start);
            foreach (TermRef node in Targets(exec, graph, start, _path, forward: true))
            {
                yield return Bind(exec, seeded, _object.Slot, node);
            }
        }
    }

    private static ulong[] Bind(Exec exec, ulong[] row, int slot, TermRef value)
    {
        ulong[] bound = Rows.Copy(row);
        Rows.Set(bound, exec.Width, slot, value);
        return bound;
    }

    /// <summary>Whether <paramref name="to"/> is among the targets from <paramref name="from"/>.</summary>
    private bool Reaches(Exec exec, ActiveGraph graph, TermRef from, PropertyPath path, TermRef to)
    {
        foreach (TermRef node in Targets(exec, graph, from, path, forward: true))
        {
            if (exec.TermEquals(node, to))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The nodes a path reaches from one node, in the direction given: a set
    /// for the closures and <c>?</c>, as §18.4 makes them, a multiset otherwise.
    /// </summary>
    private IEnumerable<TermRef> Targets(Exec exec, ActiveGraph graph, TermRef start, PropertyPath path, bool forward)
    {
        switch (path)
        {
            case ZeroOrMorePath star:
                return _zeroNeedsGraph && !InGraph(exec, graph, start) ? [] : Closure(exec, graph, start, star.Inner, forward, includeStart: true);
            case OneOrMorePath plus:
                return Closure(exec, graph, start, plus.Inner, forward, includeStart: false);
            case ZeroOrOnePath optional:
                return _zeroNeedsGraph && !InGraph(exec, graph, start) ? [] : ZeroOrOne(exec, graph, start, optional.Inner, forward);
            default:
                return Step(exec, graph, start, path, forward);
        }
    }

    /// <summary>Whether a node is in nodes(G): the subject or object of some triple of the active graph.</summary>
    private bool InGraph(Exec exec, ActiveGraph graph, TermRef node)
    {
        if (!exec.TryGetSourceHandle(node, out TermHandle handle))
        {
            return false;
        }

        foreach (bool asSubject in (bool[])[true, false])
        {
            bool open = asSubject
                ? _scan.Open(exec, handle, default, default, graph, [])
                : _scan.Open(exec, default, default, handle, graph, []);
            try
            {
                if (open && _scan.MoveNext())
                {
                    return true;
                }
            }
            finally
            {
                _scan.Close();
            }
        }

        return false;
    }

    /// <summary>§18.4's <c>ALP</c>: a depth-first search with one visited set for this start.</summary>
    private IEnumerable<TermRef> Closure(Exec exec, ActiveGraph graph, TermRef start, PropertyPath inner, bool forward, bool includeStart)
    {
        HashSet<TermRef> visited = new(new TermRefComparer(exec));
        Stack<TermRef> pending = new();
        if (includeStart)
        {
            pending.Push(start);
        }
        else
        {
            foreach (TermRef first in Step(exec, graph, start, inner, forward))
            {
                pending.Push(first);
            }
        }

        while (pending.Count > 0)
        {
            exec.Step();
            TermRef node = pending.Pop();
            if (!visited.Add(node))
            {
                continue;
            }

            yield return node;
            foreach (TermRef next in Step(exec, graph, node, inner, forward))
            {
                if (!visited.Contains(next))
                {
                    pending.Push(next);
                }
            }
        }
    }

    private IEnumerable<TermRef> ZeroOrOne(Exec exec, ActiveGraph graph, TermRef start, PropertyPath inner, bool forward)
    {
        HashSet<TermRef> seen = new(new TermRefComparer(exec)) { start };
        yield return start;
        foreach (TermRef node in Step(exec, graph, start, inner, forward))
        {
            if (seen.Add(node))
            {
                yield return node;
            }
        }
    }

    /// <summary>One application of a path from a node: its own definition in §18.4, in either direction.</summary>
    private IEnumerable<TermRef> Step(Exec exec, ActiveGraph graph, TermRef from, PropertyPath path, bool forward)
    {
        switch (path)
        {
            case PredicatePath link:
                return Link(exec, graph, from, link.Predicate, forward);
            case InversePath inverse:
                return Targets(exec, graph, from, inverse.Inner, !forward);
            case SequencePath sequence:
                return Sequence(exec, graph, from, forward ? sequence.Left : sequence.Right, forward ? sequence.Right : sequence.Left, forward);
            case AlternativePath alternative:
                return Alternative(exec, graph, from, alternative, forward);
            case NegatedPropertySet negated:
                return Negated(exec, graph, from, negated, forward);
            default:
                return Targets(exec, graph, from, path, forward);
        }
    }

    private IEnumerable<TermRef> Sequence(Exec exec, ActiveGraph graph, TermRef from, PropertyPath first, PropertyPath second, bool forward)
    {
        foreach (TermRef middle in Targets(exec, graph, from, first, forward))
        {
            foreach (TermRef end in Targets(exec, graph, middle, second, forward))
            {
                yield return end;
            }
        }
    }

    private IEnumerable<TermRef> Alternative(Exec exec, ActiveGraph graph, TermRef from, AlternativePath path, bool forward)
    {
        foreach (TermRef node in Targets(exec, graph, from, path.Left, forward))
        {
            yield return node;
        }

        foreach (TermRef node in Targets(exec, graph, from, path.Right, forward))
        {
            yield return node;
        }
    }

    private IEnumerable<TermRef> Link(Exec exec, ActiveGraph graph, TermRef from, RdfTerm predicate, bool forward)
    {
        if (Predicate(exec, predicate) is not { } p || !exec.TryGetSourceHandle(from, out TermHandle node))
        {
            yield break;
        }

        ScanCursor scan = new();
        if (!(forward ? scan.Open(exec, node, p, default, graph, []) : scan.Open(exec, default, p, node, graph, [])))
        {
            yield break;
        }

        try
        {
            while (scan.MoveNext())
            {
                yield return exec.FromSource(forward ? scan.Current.Object : scan.Current.Subject);
            }
        }
        finally
        {
            scan.Close();
        }
    }

    /// <summary>
    /// A negated property set from a node. Its forward half — present when it
    /// names forward members, or names none at all — follows triples out of the
    /// node whose predicate it does not name; its inverse half follows triples
    /// into the node. Each half is a set (§18.4: <c>{ μ | ∃ triple … }</c>), so
    /// two triples between the same nodes are one step; the halves are joined
    /// as the <c>alt</c> §18.2.2.3 makes of them, a multiset union.
    /// </summary>
    private IEnumerable<TermRef> Negated(Exec exec, ActiveGraph graph, TermRef from, NegatedPropertySet set, bool forward)
    {
        if (!exec.TryGetSourceHandle(from, out TermHandle node))
        {
            yield break;
        }

        bool hasForward = set.Forward.Count > 0 || set.Inverse.Count == 0;
        if (hasForward)
        {
            foreach (TermRef n in Excluding(exec, graph, node, set.Forward, outgoing: forward))
            {
                yield return n;
            }
        }

        if (set.Inverse.Count > 0)
        {
            foreach (TermRef n in Excluding(exec, graph, node, set.Inverse, outgoing: !forward))
            {
                yield return n;
            }
        }
    }

    private IEnumerable<TermRef> Excluding(Exec exec, ActiveGraph graph, TermHandle node, AlgebraList<RdfTerm> excluded, bool outgoing)
    {
        ScanCursor scan = new();
        if (!(outgoing ? scan.Open(exec, node, default, default, graph, []) : scan.Open(exec, default, default, node, graph, [])))
        {
            yield break;
        }

        HashSet<TermHandle> seen = new(exec.Comparer);
        try
        {
            while (scan.MoveNext())
            {
                Quad quad = scan.Current;
                TermHandle target = outgoing ? quad.Object : quad.Subject;
                if (!IsExcluded(exec, quad.Predicate, excluded) && seen.Add(target))
                {
                    yield return exec.FromSource(target);
                }
            }
        }
        finally
        {
            scan.Close();
        }
    }

    /// <summary>A negated property set with both ends unbound: one scan per half, each a set of pairs.</summary>
    private IEnumerable<(TermRef Subject, TermRef Object)> AllNegated(Exec exec, ActiveGraph graph, NegatedPropertySet set)
    {
        bool hasForward = set.Forward.Count > 0 || set.Inverse.Count == 0;
        for (int half = 0; half < 2; half++)
        {
            bool inverse = half == 1;
            if ((inverse && set.Inverse.Count == 0) || (!inverse && !hasForward))
            {
                continue;
            }

            ScanCursor scan = new();
            if (!scan.Open(exec, default, default, default, graph, []))
            {
                continue;
            }

            HashSet<(TermHandle, TermHandle)> seen = new(new PairComparer(exec.Comparer));
            try
            {
                while (scan.MoveNext())
                {
                    Quad quad = scan.Current;
                    if (!IsExcluded(exec, quad.Predicate, inverse ? set.Inverse : set.Forward) && seen.Add((quad.Subject, quad.Object)))
                    {
                        TermRef s = exec.FromSource(quad.Subject);
                        TermRef o = exec.FromSource(quad.Object);
                        yield return inverse ? (o, s) : (s, o);
                    }
                }
            }
            finally
            {
                scan.Close();
            }
        }
    }

    private bool IsExcluded(Exec exec, TermHandle predicate, AlgebraList<RdfTerm> excluded)
    {
        foreach (RdfTerm member in excluded)
        {
            if (Predicate(exec, member) is { } handle && exec.Comparer.Equals(handle, predicate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The node set of the active graph: every subject and object, once each.</summary>
    private List<TermRef> Nodes(Exec exec, ActiveGraph graph)
    {
        HashSet<TermHandle> seen = new(exec.Comparer);
        List<TermRef> nodes = [];
        if (_scan.Open(exec, default, default, default, graph, []))
        {
            try
            {
                while (_scan.MoveNext())
                {
                    Quad quad = _scan.Current;
                    if (seen.Add(quad.Subject))
                    {
                        nodes.Add(exec.FromSource(quad.Subject));
                    }

                    if (seen.Add(quad.Object))
                    {
                        nodes.Add(exec.FromSource(quad.Object));
                    }
                }
            }
            finally
            {
                _scan.Close();
            }
        }

        return nodes;
    }

    private TermHandle? Predicate(Exec exec, RdfTerm iri)
    {
        if (!_predicates!.TryGetValue(iri, out TermHandle? handle))
        {
            handle = exec.Source.TryInternalise(iri, out TermHandle found) ? found : null;
            _predicates[iri] = handle;
        }

        return handle;
    }
}

/// <summary>One end of a path pattern: a constant, or a variable's slot.</summary>
internal sealed class PathEnd
{
    private readonly RdfTerm? _constant;
    private TermRef _resolved;
    private Exec? _for;

    private PathEnd(RdfTerm? constant, int slot)
    {
        _constant = constant;
        Slot = slot;
    }

    internal int Slot { get; }

    internal static PathEnd OfConstant(RdfTerm term) => new(term, -1);

    internal static PathEnd OfSlot(int slot) => new(null, slot);

    /// <summary>The end's value in a solution, or unbound. A constant the graph lacks is a local term: zero-length paths still reach it.</summary>
    internal TermRef Resolve(Exec exec, ulong[] row)
    {
        if (_constant is null)
        {
            return Rows.Get(row, exec.Width, Slot);
        }

        if (!ReferenceEquals(_for, exec))
        {
            _resolved = exec.Intern(_constant);
            _for = exec;
        }

        return _resolved;
    }
}

/// <summary>Term equality over slot values, for visited sets.</summary>
internal sealed class TermRefComparer(Exec exec) : IEqualityComparer<TermRef>
{
    public bool Equals(TermRef x, TermRef y) => exec.TermEquals(x, y);

    public int GetHashCode(TermRef obj) => exec.TermHash(obj);
}

/// <summary>Pairs of handles, under the source's comparer.</summary>
internal sealed class PairComparer(IEqualityComparer<TermHandle> comparer) : IEqualityComparer<(TermHandle, TermHandle)>
{
    public bool Equals((TermHandle, TermHandle) x, (TermHandle, TermHandle) y) =>
        comparer.Equals(x.Item1, y.Item1) && comparer.Equals(x.Item2, y.Item2);

    public int GetHashCode((TermHandle, TermHandle) obj) =>
        System.HashCode.Combine(comparer.GetHashCode(obj.Item1), comparer.GetHashCode(obj.Item2));
}
