// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Classification.Model;
using Varve.Rdf;

namespace Varve.Classification;

/// <summary>
/// ADR 0230 point 1 over one state of one binding's graphs: which nodes are
/// data subjects, and the subjects of a node, the least set through link
/// predicates. Classification from scratch, and the nodes a delta can
/// reclassify (ADR 0232 points 4 and 5). Orchestration, run once per commit
/// or per classification, never per quad of a read.
/// </summary>
internal sealed class Engine
{
    private readonly IQuadSource _state;
    private readonly ClassifierBinding _binding;
    private readonly TermHandle _type;
    private readonly HashSet<TermHandle> _classes;
    private readonly HashSet<TermHandle> _nodes;
    private readonly HashSet<TermHandle> _subjectsOf;
    private readonly HashSet<TermHandle> _links;
    private readonly GraphDecider _graphs;

    internal Engine(IQuadSource state, ClassifierBinding binding, Annotations annotations)
    {
        _state = state;
        _binding = binding;
        Annotations = annotations;
        _graphs = new GraphDecider(state, binding);
        _type = state.TryInternalise(Annotations.RdfType, out TermHandle type) ? type : TermHandle.None;
        _classes = Handles(state, annotations.SubjectClasses);
        _nodes = Handles(state, annotations.SubjectNodes);
        _subjectsOf = Handles(state, annotations.SubjectsOf);
        _links = Handles(state, annotations.Links);
        Public = Handles(state, annotations.Public);
    }

    internal Annotations Annotations { get; }

    /// <summary>Whether a quad's change can reclassify its subject: its predicate targets or links.</summary>
    internal bool Reclassifies(in Quad quad) =>
        _graphs.Classifies(quad.Graph)
        && ((!_type.IsNone && Same(quad.Predicate, _type)) || _subjectsOf.Contains(quad.Predicate) || _links.Contains(quad.Predicate));

    /// <summary>Whether <paramref name="quad"/> is in the shapes graph, which changes the configuration.</summary>
    internal bool InShapesGraph(in Quad quad) =>
        !quad.Graph.IsNone && _state.TryExternalise(quad.Graph, out RdfTerm? graph) && graph.Equals(_binding.ShapesGraph);

    /// <summary>Whether <paramref name="node"/> is a data subject in this state.</summary>
    internal bool IsSubject(TermHandle node)
    {
        if (_nodes.Contains(node))
        {
            return true;
        }

        foreach (TermHandle @class in _classes)
        {
            if (!_type.IsNone && Any(node, _type, @class))
            {
                return true;
            }
        }

        foreach (TermHandle predicate in _subjectsOf)
        {
            if (Any(node, predicate, TermHandle.None))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The subjects of <paramref name="node"/>: itself when a data subject, and every subject a link path reaches, sorted by handle.</summary>
    internal TermHandle[] SubjectsOf(TermHandle node)
    {
        HashSet<TermHandle> seen = new(_state.TermComparer) { node };
        Stack<TermHandle> next = new([node]);
        List<TermHandle> subjects = [];

        while (next.Count > 0)
        {
            TermHandle current = next.Pop();

            if (IsSubject(current))
            {
                subjects.Add(current);
            }

            foreach (TermHandle link in _links)
            {
                foreach (Quad quad in Matches(current, link, TermHandle.None))
                {
                    if (seen.Add(quad.Object))
                    {
                        next.Push(quad.Object);
                    }
                }
            }
        }

        return [.. subjects.OrderBy(static h => h.Value)];
    }

    /// <summary>Every data subject of the state.</summary>
    internal HashSet<TermHandle> Subjects()
    {
        HashSet<TermHandle> subjects = new(_state.TermComparer);

        foreach (TermHandle node in _nodes)
        {
            subjects.Add(node);
        }

        foreach (TermHandle @class in _classes)
        {
            if (!_type.IsNone)
            {
                foreach (Quad quad in Matches(TermHandle.None, _type, @class))
                {
                    subjects.Add(quad.Subject);
                }
            }
        }

        foreach (TermHandle predicate in _subjectsOf)
        {
            foreach (Quad quad in Matches(TermHandle.None, predicate, TermHandle.None))
            {
                subjects.Add(quad.Subject);
            }
        }

        return subjects;
    }

    /// <summary>
    /// Classification from scratch (ADR 0232 point 4): each data subject,
    /// then the reverse closure along links, each node gaining the subject.
    /// </summary>
    internal Dictionary<TermHandle, TermHandle[]> Scratch()
    {
        Dictionary<TermHandle, SortedSet<ulong>> owners = new(_state.TermComparer);
        Dictionary<ulong, TermHandle> byValue = [];

        foreach (TermHandle subject in Subjects())
        {
            byValue[subject.Value] = subject;
            HashSet<TermHandle> reached = new(_state.TermComparer);
            Reverse(subject, reached);

            foreach (TermHandle node in reached)
            {
                if (!owners.TryGetValue(node, out SortedSet<ulong>? set))
                {
                    owners[node] = set = [];
                }

                set.Add(subject.Value);
            }
        }

        Dictionary<TermHandle, TermHandle[]> classification = new(_state.TermComparer);

        foreach ((TermHandle node, SortedSet<ulong> set) in owners)
        {
            classification[node] = [.. set.Select(v => byValue[v])];
        }

        return classification;
    }

    /// <summary>Adds <paramref name="node"/> and every node reaching it along links to <paramref name="into"/>.</summary>
    internal void Reverse(TermHandle node, HashSet<TermHandle> into)
    {
        if (!into.Add(node))
        {
            return;
        }

        Stack<TermHandle> next = new([node]);

        while (next.Count > 0)
        {
            TermHandle current = next.Pop();

            foreach (TermHandle link in _links)
            {
                foreach (Quad quad in Matches(TermHandle.None, link, current))
                {
                    if (into.Add(quad.Subject))
                    {
                        next.Push(quad.Subject);
                    }
                }
            }
        }
    }

    /// <summary>The public links of the state, <c>rdf:type</c> among them, as handles.</summary>
    internal HashSet<TermHandle> Public { get; }

    private bool Same(TermHandle a, TermHandle b) => _state.TermComparer.Equals(a, b);

    private bool Any(TermHandle s, TermHandle p, TermHandle o)
    {
        using IQuadCursor cursor = _state.Match(s, p, o, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            if (_graphs.Classifies(cursor.Current.Graph))
            {
                return true;
            }
        }

        return false;
    }

    private List<Quad> Matches(TermHandle s, TermHandle p, TermHandle o)
    {
        List<Quad> quads = [];
        using IQuadCursor cursor = _state.Match(s, p, o, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            if (_graphs.Classifies(cursor.Current.Graph))
            {
                quads.Add(cursor.Current);
            }
        }

        return quads;
    }

    private static HashSet<TermHandle> Handles(IQuadSource state, List<RdfTerm> terms)
    {
        HashSet<TermHandle> handles = new(state.TermComparer);

        foreach (RdfTerm term in terms)
        {
            if (state.TryInternalise(term, out TermHandle handle))
            {
                handles.Add(handle);
            }
        }

        return handles;
    }
}

/// <summary>
/// Whether a graph handle is one a binding classifies (ADR 0230 point 1),
/// decided once per handle by externalising it and memoised, as a prefix
/// grant is (ADR 0107).
/// </summary>
internal sealed class GraphDecider
{
    private readonly IQuadSource _source;
    private readonly ClassifierBinding _binding;
    private readonly bool _default;
    [Memoized]
    private readonly ConcurrentDictionary<TermHandle, bool> _decided;

    internal GraphDecider(IQuadSource source, ClassifierBinding binding)
    {
        _source = source;
        _binding = binding;
        _decided = new ConcurrentDictionary<TermHandle, bool>(source.TermComparer);
        _default = binding.ClassifiesGraph(null);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Classifies(TermHandle graph) => graph.IsNone ? _default : Decided(graph);

    [DesignDecision(typeof(TheClassificationProjection.ClassificationLookupsMemoisedPerHandle), Scope = ExceptionScope.HotPath)]
    private bool Decided(TermHandle graph) =>
        _decided.GetOrAdd(graph, static (handle, self) => self.Decide(handle), this);

    private bool Decide(TermHandle graph) =>
        _source.TryExternalise(graph, out RdfTerm? term) && term.Kind == RdfTermKind.Iri && _binding.ClassifiesGraph(term);
}
