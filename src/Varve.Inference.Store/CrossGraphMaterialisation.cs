// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Varve.Inference.Model;
using Varve.Rdf;

namespace Varve.Inference.Store;

/// <summary>
/// A cross-graph binding's closures (ADR 0190 point 6): one closure of the
/// data graphs' merge over the TBox closure, and each fact's minimal premise
/// sets over it.
/// </summary>
/// <remarks>
/// <para>
/// A fact the merge derives, not held by the TBox closure, is a row once
/// for each of its minimal premise sets: in the set's one graph when it has
/// one graph and the fact is not explicit there, and in the set's reserved
/// graph, <c>varve:inferred/{enc(binding)}/{d}</c>, when it has two or more.
/// <c>d</c> is the lower-case hex SHA-256 of the set's graph IRIs, sorted,
/// each in canonical N-Triples and ended by a line feed; the default graph
/// is written as <c>varve:defaultGraph</c>.
/// </para>
/// <para>
/// A data commit updates the merge incrementally, and the premise sets from
/// its delta (<see cref="PremiseSets.Update"/>), which answers the facts
/// whose sets changed: only their rows may have moved. Explaining a row closes only the graphs of its set, so that
/// no proof draws on a graph outside it.
/// </para>
/// </remarks>
internal sealed class CrossGraphMaterialisation : Materialisation
{
    private const string DefaultGraph = "https://w3id.org/varve/ns#defaultGraph";

    private readonly Closure _merge;
    private readonly Dictionary<TermHandle, HashSet<Fact>> _explicit = [];
    private readonly Dictionary<TermHandle, int> _indices = [];
    private readonly List<TermHandle> _graphs = [];
    private readonly Dictionary<string, TermHandle> _setGraphs = new(StringComparer.Ordinal);
    private readonly Dictionary<TermHandle, TermHandle[]> _table = [];
    private PremiseSets _sets;

    internal CrossGraphMaterialisation(ProjectionTerms terms, InferenceProjection projection)
        : base(terms, projection)
    {
        _merge = new Closure(projection.Binding.Profile, terms, Budget, TBox);
        _sets = new PremiseSets(_merge, []);
        _first = terms.Handle(RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#first"u8));
        _rest = terms.Handle(RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest"u8));
    }

    private readonly TermHandle _first;
    private readonly TermHandle _rest;

    internal override IReadOnlyDictionary<TermHandle, TermHandle[]> PremiseSetGraphs => _table;

    internal override Closure? ClosureOf(TermHandle graph)
    {
        if (graph == TBoxGraph)
        {
            return TBox;
        }

        TermHandle[]? members = _table.TryGetValue(graph, out TermHandle[]? set) ? set : _explicit.ContainsKey(graph) ? [graph] : null;
        if (members is null)
        {
            return null;
        }

        // Only the graphs of the row's set: a proof never draws on another.
        Closure closure = new(Projection.Binding.Profile, Terms, Projection.NewBudget(), TBox);
        List<Fact> facts = [];
        foreach (TermHandle member in members)
        {
            if (_explicit.TryGetValue(member, out HashSet<Fact>? held))
            {
                facts.AddRange(held);
            }
        }

        closure.Apply([.. new HashSet<Fact>(facts)], []);
        return closure;
    }

    internal override void Release()
    {
        _sets.Release();
        _merge.Release();
        base.Release();
    }

    protected override void Load(Dictionary<TermHandle, List<Fact>> graphs, CancellationToken cancellationToken)
    {
        HashSet<Fact> merged = [];
        foreach ((TermHandle graph, List<Fact> facts) in graphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Index(graph);
            _explicit[graph] = [.. facts];
            merged.UnionWith(facts);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _merge.Apply([.. merged], [], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _sets = Label();
        Mint();
    }

    protected override void ApplyData(Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs, HashSet<TermHandle> emptied, Dictionary<TermHandle, HashSet<Fact>> changed, CancellationToken cancellationToken)
    {
        List<Fact> asserted = [], retracted = [];
        List<(Fact, DataGraphIndex)> unpaired = [];
        List<(Fact, DataGraphIndex)> paired = [];
        foreach ((TermHandle graph, (List<Fact> graphAsserted, List<Fact> graphRetracted)) in graphs)
        {
            Index(graph);
            DataGraphIndex index = new(_indices[graph]);
            if (!_explicit.TryGetValue(graph, out HashSet<Fact>? held))
            {
                _explicit[graph] = held = [];
            }

            foreach (Fact fact in graphRetracted)
            {
                if (held.Remove(fact))
                {
                    unpaired.Add((fact, index));
                    if (!HeldElsewhere(fact))
                    {
                        retracted.Add(fact);
                    }
                }

                Into(changed, graph).Add(fact);
            }

            foreach (Fact fact in graphAsserted)
            {
                if (!HeldElsewhere(fact))
                {
                    asserted.Add(fact);
                }

                if (held.Add(fact))
                {
                    paired.Add((fact, index));
                }

                Into(changed, graph).Add(fact);
            }

            if (emptied.Contains(graph))
            {
                _explicit.Remove(graph);
                Dropped(graph);
            }
        }

        // The sets a retraction may change, over the merge as it was; then the
        // merge, then the sets from the delta (ADR 0195 point 3).
        IReadOnlySet<Fact> affected = _sets.Reach(unpaired, (fact, graph) => _explicit.TryGetValue(_graphs[graph.Value], out HashSet<Fact>? held) && held.Contains(fact));
        ListReadings readings = _merge.ListReadings;
        HashSet<Fact> moved = [.. _merge.Apply([.. asserted], [.. retracted], cancellationToken)];
        if (_merge.ListReadings != readings)
        {
            // The merge read its lists again and was rebuilt: a list rule's instances changed with no
            // change to their premises, so the sets are labelled again from scratch and every fact is
            // offered to the graphs it was and is placed in (ADR 0197, amended 2026-11-24).
            PremiseSets was = _sets;
            _sets = Label();
            _placements.Clear();
            _placedFrom = null;
            foreach (Fact fact in _merge.Own.Concat(moved).Distinct())
            {
                foreach (TermHandle graph in Placements(was.Of(fact), fact).Concat(Placements(_sets.Of(fact), fact)))
                {
                    Into(changed, graph).Add(fact);
                }
            }

            was.Release();
            Mint();
            return;
        }
        foreach (Fact fact in affected)
        {
            foreach ((TermHandle graph, HashSet<Fact> facts) in _explicit)
            {
                if (facts.Contains(fact))
                {
                    paired.Add((fact, new DataGraphIndex(_indices[graph])));
                }
            }
        }

        IReadOnlyDictionary<Fact, IReadOnlyList<PremiseSet>> before = _sets.Update(affected, paired);
        _setsChanged.UnionWith(before.Keys);

        // A fact whose sets changed may have moved between graphs: offer it to every graph it was or is placed in.
        foreach ((Fact fact, IReadOnlyList<PremiseSet> was) in before)
        {
            foreach (TermHandle graph in Placements(was, fact))
            {
                Into(changed, graph).Add(fact);
            }

            foreach (TermHandle graph in Placements(_sets.Of(fact), fact))
            {
                Into(changed, graph).Add(fact);
            }
        }

        foreach (Fact fact in moved)
        {
            foreach (TermHandle graph in Placements(_sets.Of(fact), fact))
            {
                Into(changed, graph).Add(fact);
            }
        }
    }

    protected override bool IsDataRow(TermHandle graph, in Fact fact)
    {
        if (!_merge.Contains(fact) || TBox.Contains(fact) || !Servable(fact))
        {
            return false;
        }

        foreach (TermHandle placed in Placements(_sets, fact))
        {
            if (placed == graph)
            {
                return true;
            }
        }

        return false;
    }

    protected override IEnumerable<(TermHandle Graph, Fact Fact)> DataRows()
    {
        foreach (Fact fact in _merge.Own)
        {
            if (TBox.Contains(fact) || !Servable(fact))
            {
                continue;
            }

            foreach (TermHandle graph in Placements(_sets, fact))
            {
                yield return (graph, fact);
            }
        }
    }

    /// <summary>An unread cell of the merge's lists, under each data graph that asserts an <c>rdf:first</c> or <c>rdf:rest</c> of it.</summary>
    protected override void DataUnreadLists(List<PlacedUnreadList> into)
    {
        foreach (UnreadList list in _merge.UnreadLists())
        {
            foreach (TermHandle graph in _graphs)
            {
                if (_explicit.TryGetValue(graph, out HashSet<Fact>? held) && Asserts(held, list.Cell))
                {
                    into.Add(new PlacedUnreadList(graph, list));
                }
            }
        }

        bool Asserts(HashSet<Fact> held, TermHandle cell)
        {
            foreach (Fact fact in held)
            {
                if (fact.Subject.Value == cell.Value && (fact.Predicate.Value == _first.Value || fact.Predicate.Value == _rest.Value))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A clash of the merge, under the graph of each minimal set of its premises: none when the TBox closure finds it alone.</summary>
    protected override void DataClashes(List<IReadOnlyList<PlacedClash>> into)
    {
        IReadOnlyList<Clash> clashes = _merge.Clashes();
        if (!ReferenceEquals(clashes, _placedFrom) || _setsChanged.Count > 0)
        {
            // A clash is placed again when it is new or a premise's sets changed since it was placed.
            Dictionary<Clash, PlacedClash[]> placements = new(ReferenceEqualityComparer.Instance);
            List<PlacedClash> placed = [];
            foreach (Clash clash in clashes)
            {
                if (!_placements.TryGetValue(clash, out PlacedClash[]? those) || Moved(clash))
                {
                    those = Place(clash);
                }

                placements[clash] = those;
                placed.AddRange(those);
            }

            _placements = placements;
            _placed = placed;
            _placedFrom = clashes;
            _setsChanged.Clear();
        }

        into.Add(_placed);
    }

    protected override void DataClashesInFull(List<PlacedClash> into)
    {
        foreach (Clash clash in _merge.ClashesInFull())
        {
            into.AddRange(Place(clash));
        }
    }

    // Whether a premise of the clash has had its premise sets changed since the clashes were last placed.
    private bool Moved(Clash clash)
    {
        foreach (Fact premise in clash.Premises)
        {
            if (_setsChanged.Contains(premise))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A clash of the merge, under the graph of each minimal set of its premises: none when the TBox closure finds it alone.</summary>
    private PlacedClash[] Place(Clash clash)
    {
        List<int[]> unions = [[]];
        foreach (Fact premise in clash.Premises)
        {
            List<int[]> next = [];
            foreach (int[] sofar in unions)
            {
                foreach (PremiseSet set in _sets.Of(premise))
                {
                    next.Add(Union(sofar, set));
                }
            }

            unions = next;
        }

        List<PlacedClash> placed = [];
        foreach (int[] minimal in Minimal(unions))
        {
            if (minimal.Length > 0)
            {
                placed.Add(new PlacedClash(GraphOf(minimal), clash));
            }
        }

        return [.. placed];
    }

    // The merge's clashes as last placed: each clash's placements, the list placed from, and the facts whose sets changed since.
    private Dictionary<Clash, PlacedClash[]> _placements = new(ReferenceEqualityComparer.Instance);
    private List<PlacedClash> _placed = [];
    private IReadOnlyList<Clash>? _placedFrom;
    private readonly HashSet<Fact> _setsChanged = [];

    /// <summary>The graphs a fact is a row of under <paramref name="sets"/>: a set's one graph unless the fact is explicit there, a set's reserved graph when it has two or more.</summary>
    private List<TermHandle> Placements(PremiseSets sets, in Fact fact) => Placements(sets.Of(fact), fact);

    /// <summary>The graphs a fact is a row of under its sets <paramref name="sets"/>.</summary>
    private List<TermHandle> Placements(IReadOnlyList<PremiseSet> sets, in Fact fact)
    {
        List<TermHandle> graphs = [];
        foreach (PremiseSet set in sets)
        {
            IReadOnlyList<DataGraphIndex> members = set.Graphs;
            if (members.Count == 1)
            {
                TermHandle graph = _graphs[members[0].Value];
                if (!(_explicit.TryGetValue(graph, out HashSet<Fact>? held) && held.Contains(fact)))
                {
                    graphs.Add(graph);
                }
            }
            else if (members.Count >= 2)
            {
                graphs.Add(SetGraph(set));
            }
        }

        return graphs;
    }

    /// <summary>Names every cross-graph set's reserved graph now, so the table the projection learns holds each one a row is placed in.</summary>
    private void Mint()
    {
        foreach (PremiseSet set in _sets.CrossGraph())
        {
            SetGraph(set);
        }
    }

    private PremiseSets Label()
    {
        List<(Fact, DataGraphIndex)> held = [];
        foreach ((TermHandle graph, HashSet<Fact> facts) in _explicit)
        {
            DataGraphIndex index = new(_indices[graph]);
            foreach (Fact fact in facts)
            {
                held.Add((fact, index));
            }
        }

        return new PremiseSets(_merge, held);
    }

    private bool HeldElsewhere(in Fact fact)
    {
        foreach (HashSet<Fact> facts in _explicit.Values)
        {
            if (facts.Contains(fact))
            {
                return true;
            }
        }

        return false;
    }

    private void Index(TermHandle graph)
    {
        if (!_indices.ContainsKey(graph))
        {
            _indices[graph] = _graphs.Count;
            _graphs.Add(graph);
        }
    }

    private TermHandle SetGraph(PremiseSet set)
    {
        IReadOnlyList<DataGraphIndex> members = set.Graphs;
        int[] indices = new int[members.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = members[i].Value;
        }

        return GraphOf(indices);
    }

    /// <summary>The handle of a set's graph: its one data graph, or its reserved graph, minted once and kept in the table.</summary>
    private TermHandle GraphOf(int[] indices)
    {
        if (indices.Length == 1)
        {
            return _graphs[indices[0]];
        }

        List<string> lines = [];
        TermHandle[] members = new TermHandle[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            members[i] = _graphs[indices[i]];
            lines.Add("<" + Iri(members[i]) + ">\n");
        }

        lines.Sort(StringComparer.Ordinal);
        string text = string.Concat(lines);
        if (!_setGraphs.TryGetValue(text, out TermHandle graph))
        {
            graph = Terms.Handle(PremiseSetGraph(Projection.Binding.Binding.Lexical, Encoding.UTF8.GetBytes(text)));
            _setGraphs[text] = graph;
            _table[graph] = members;
        }

        return graph;
    }

    /// <summary><c>varve:inferred/{enc(binding)}/{d}</c>, <paramref name="lines"/> the set's graph IRIs as ADR 0190 point 6 writes them.</summary>
    internal static RdfTerm PremiseSetGraph(ReadOnlySpan<byte> binding, ReadOnlySpan<byte> lines)
    {
        string tbox = Encoding.UTF8.GetString(InferenceProjection.InferredTBox(binding).Lexical);
        string digest = Convert.ToHexStringLower(SHA256.HashData(lines));
        return RdfTerm.Iri(Encoding.UTF8.GetBytes(string.Concat(tbox.AsSpan(0, tbox.Length - "tbox".Length), digest)));
    }

    private string Iri(TermHandle graph) =>
        graph.IsNone ? DefaultGraph
        : Terms.TryExternalise(graph, out RdfTerm? term) ? Encoding.UTF8.GetString(term.Lexical)
        : throw new InvalidOperationException("A data graph the projection cannot name.");

    private static int[] Union(int[] sofar, PremiseSet set)
    {
        SortedSet<int> union = [.. sofar];
        foreach (DataGraphIndex graph in set.Graphs)
        {
            union.Add(graph.Value);
        }

        return [.. union];
    }

    private static List<int[]> Minimal(List<int[]> sets)
    {
        List<int[]> minimal = [];
        foreach (int[] set in sets)
        {
            bool absorbed = false;
            foreach (int[] other in sets)
            {
                absorbed |= !ReferenceEquals(set, other) && other.Length < set.Length && Subset(other, set);
            }

            bool duplicate = false;
            foreach (int[] kept in minimal)
            {
                duplicate |= kept.AsSpan().SequenceEqual(set);
            }

            if (!absorbed && !duplicate)
            {
                minimal.Add(set);
            }
        }

        return minimal;
    }

    private static bool Subset(int[] small, int[] large)
    {
        foreach (int item in small)
        {
            if (Array.IndexOf(large, item) < 0)
            {
                return false;
            }
        }

        return true;
    }
}
