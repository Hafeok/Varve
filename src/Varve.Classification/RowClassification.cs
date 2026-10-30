// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Classification.Model;
using Varve.Rdf;

namespace Varve.Classification;

/// <summary>
/// Subject sets interned to ids (ADR 0231): a set is its members sorted by
/// handle, and equal sets get equal ids. Append-only and safe to read from
/// several threads while one interns.
/// </summary>
internal sealed class SetTable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SubjectSetId> _ids = [];
    private TermHandle[][] _members = [[]];
    private int _count = 1;

    /// <summary>How many sets, the empty one included.</summary>
    internal int Count => Volatile.Read(ref _count);

    /// <summary>The id of <paramref name="members"/>, sorted by handle; the empty set is <see cref="SubjectSetId.None"/>.</summary>
    internal SubjectSetId Intern(TermHandle[] members)
    {
        if (members.Length == 0)
        {
            return SubjectSetId.None;
        }

        string key = string.Join(',', members.Select(static m => m.Value));

        lock (_lock)
        {
            if (_ids.TryGetValue(key, out SubjectSetId id))
            {
                return id;
            }

            if (_count == _members.Length)
            {
                Array.Resize(ref _members, _members.Length * 2);
            }

            _members[_count] = members;
            id = new SubjectSetId(_count);
            _ids[key] = id;
            Volatile.Write(ref _count, _count + 1);
            return id;
        }
    }

    /// <summary>The members of <paramref name="set"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ReadOnlySpan<TermHandle> Members(SubjectSetId set) => _members[set.Value];

    /// <summary>The union of two sets.</summary>
    internal SubjectSetId Union(SubjectSetId a, SubjectSetId b)
    {
        if (a.IsNone || a == b)
        {
            return b;
        }

        if (b.IsNone)
        {
            return a;
        }

        TermHandle[] both = [.. Members(a).ToArray().Concat(Members(b).ToArray()).DistinctBy(static h => h.Value).OrderBy(static h => h.Value)];
        return Intern(both);
    }
}

/// <summary>The subject set of a node at one position, as a classification reads it.</summary>
internal interface INodeSets
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    SubjectSetId Of(TermHandle node);
}

/// <summary>
/// A configuration that did not classify, failing closed (ADR 0233 point 4):
/// every node belongs to a subject no scope names, so a row-restricted
/// caller sees nothing of the binding's graphs.
/// </summary>
internal sealed class FailedSets(SetTable sets) : INodeSets
{
    private readonly SubjectSetId _nobody = sets.Intern([TermHandle.None]);

    public SubjectSetId Of(TermHandle node) => _nobody;
}

/// <summary>A node map built by classification from scratch.</summary>
internal sealed class ScratchSets(Dictionary<TermHandle, SubjectSetId> sets) : INodeSets
{
    [DesignDecision(typeof(TheClassificationProjection.ClassificationLookupsMemoisedPerHandle), Scope = ExceptionScope.HotPath)]
    public SubjectSetId Of(TermHandle node) => sets.TryGetValue(node, out SubjectSetId set) ? set : SubjectSetId.None;
}

/// <summary>
/// One binding's classification at one state, in one source's handles
/// (ADR 0231): the binding of a graph, decided once per handle; the set of a
/// node, from its map; and a triple term's, the union of its components',
/// interned the first time it is asked.
/// </summary>
internal sealed class RowClassification : IRowClassification
{
    private readonly IQuadSource _source;
    private readonly GraphDecider _graphs;
    private readonly INodeSets _nodes;
    private readonly SetTable _sets;
    private readonly HashSet<TermHandle> _public;
    [Memoized]
    private readonly ConcurrentDictionary<TermHandle, SubjectSetId> _tripleTerms;

    internal RowClassification(IQuadSource source, ClassifierBinding binding, INodeSets nodes, SetTable sets, HashSet<TermHandle> publicLinks)
    {
        _source = source;
        _graphs = new GraphDecider(source, binding);
        _nodes = nodes;
        _sets = sets;
        _public = publicLinks;
        _tripleTerms = new ConcurrentDictionary<TermHandle, SubjectSetId>(source.TermComparer);
        Bindings = [binding.Binding];
    }

    public IReadOnlyList<RdfTerm> Bindings { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public BindingIndex BindingOf(TermHandle graph) => _graphs.Classifies(graph) ? new BindingIndex(0) : BindingIndex.None;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public SubjectSetId SubjectsOf(BindingIndex binding, TermHandle node)
    {
        SubjectSetId set = _nodes.Of(node);
        return set.IsNone && _source.TryGetTripleTermComponents(node, out _, out _, out _) ? TripleTerm(node) : set;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public ReadOnlySpan<TermHandle> Members(BindingIndex binding, SubjectSetId set) => _sets.Members(set);

    [DesignDecision(typeof(TheClassificationProjection.ClassificationLookupsMemoisedPerHandle), Scope = ExceptionScope.HotPath)]
    public bool IsPublicLink(BindingIndex binding, TermHandle predicate) => _public.Contains(predicate);

    // A triple term carries its components' subjects (ADR 0230 point 1),
    // interned once per handle.
    [DesignDecision(typeof(TheClassificationProjection.ClassificationLookupsMemoisedPerHandle), Scope = ExceptionScope.HotPath)]
    private SubjectSetId TripleTerm(TermHandle node) =>
        _tripleTerms.GetOrAdd(node, static (handle, self) => self.Components(handle), this);

    private SubjectSetId Components(TermHandle node)
    {
        if (!_source.TryGetTripleTermComponents(node, out TermHandle s, out TermHandle p, out TermHandle o))
        {
            return SubjectSetId.None;
        }

        BindingIndex only = new(0);
        return _sets.Union(_sets.Union(SubjectsOf(only, s), SubjectsOf(only, p)), SubjectsOf(only, o));
    }
}

/// <summary>Several bindings' classifications, whose graph sets do not overlap (ADR 0234's P4), as one.</summary>
internal sealed class ComposedClassification : IRowClassification
{
    private readonly IRowClassification[] _parts;

    internal ComposedClassification(IRowClassification[] parts)
    {
        _parts = parts;
        Bindings = [.. parts.SelectMany(static p => p.Bindings)];
    }

    public IReadOnlyList<RdfTerm> Bindings { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public BindingIndex BindingOf(TermHandle graph)
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            if (!_parts[i].BindingOf(graph).IsNone)
            {
                return new BindingIndex(i);
            }
        }

        return BindingIndex.None;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public SubjectSetId SubjectsOf(BindingIndex binding, TermHandle node) => _parts[binding.Value].SubjectsOf(new BindingIndex(0), node);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public ReadOnlySpan<TermHandle> Members(BindingIndex binding, SubjectSetId set) => _parts[binding.Value].Members(new BindingIndex(0), set);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool IsPublicLink(BindingIndex binding, TermHandle predicate) => _parts[binding.Value].IsPublicLink(new BindingIndex(0), predicate);
}
