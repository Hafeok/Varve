// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
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
/// A data commit updates the merge incrementally and recomputes the premise
/// sets, diffing them with the sets before to find the facts whose rows may
/// have moved. Explaining a row closes only the graphs of its set, so that
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
    }

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
        Closure closure = new(Projection.Binding.Profile, Terms, new FactBudget(Projection.FactLimit), TBox);
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
        base.Release();
        _merge.Release();
    }

    protected override void Load(Dictionary<TermHandle, List<Fact>> graphs, CancellationToken cancellationToken)
    {
        HashSet<Fact> merged = [];
        foreach ((TermHandle graph, List<Fact> facts) in graphs)
        {
            Index(graph);
            _explicit[graph] = [.. facts];
            merged.UnionWith(facts);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _merge.Apply([.. merged], []);
        cancellationToken.ThrowIfCancellationRequested();
        _sets = Label();
        Mint();
    }

    protected override void ApplyData(IQuadSource view, Dictionary<TermHandle, (List<Fact> Asserted, List<Fact> Retracted)> graphs, Dictionary<TermHandle, HashSet<Fact>> changed)
    {
        List<Fact> asserted = [], retracted = [];
        foreach ((TermHandle graph, (List<Fact> graphAsserted, List<Fact> graphRetracted)) in graphs)
        {
            Index(graph);
            if (!_explicit.TryGetValue(graph, out HashSet<Fact>? held))
            {
                _explicit[graph] = held = [];
            }

            foreach (Fact fact in graphRetracted)
            {
                if (held.Remove(fact) && !HeldElsewhere(fact))
                {
                    retracted.Add(fact);
                }

                Into(changed, graph).Add(fact);
            }

            foreach (Fact fact in graphAsserted)
            {
                if (!HeldElsewhere(fact))
                {
                    asserted.Add(fact);
                }

                held.Add(fact);
                Into(changed, graph).Add(fact);
            }

            if (graphRetracted.Count > 0 && !HasQuads(view, graph))
            {
                _explicit.Remove(graph);
                Dropped(graph);
            }
        }

        PremiseSets before = _sets;
        HashSet<Fact> moved = [.. _merge.Apply([.. asserted], [.. retracted])];
        _sets = Label();
        Mint();

        // A fact whose sets changed may have moved between graphs: offer it to every graph it was or is placed in.
        foreach (Fact fact in _merge.Own)
        {
            if (!Same(before.Of(fact), _sets.Of(fact)))
            {
                moved.Add(fact);
            }
        }

        foreach (Fact fact in moved)
        {
            foreach (TermHandle graph in Placements(before, fact))
            {
                Into(changed, graph).Add(fact);
            }

            foreach (TermHandle graph in Placements(_sets, fact))
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

    /// <summary>A clash of the merge, under the graph of each minimal set of its premises: none when the TBox closure finds it alone.</summary>
    protected override void DataClashes(List<PlacedClash> into)
    {
        foreach (Clash clash in _merge.Clashes())
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

            foreach (int[] minimal in Minimal(unions))
            {
                if (minimal.Length > 0)
                {
                    into.Add(new PlacedClash(GraphOf(minimal), clash));
                }
            }
        }
    }

    /// <summary>The graphs a fact is a row of under <paramref name="sets"/>: a set's one graph unless the fact is explicit there, a set's reserved graph when it has two or more.</summary>
    private List<TermHandle> Placements(PremiseSets sets, in Fact fact)
    {
        List<TermHandle> graphs = [];
        foreach (PremiseSet set in sets.Of(fact))
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

    private static bool Same(IReadOnlyList<PremiseSet> a, IReadOnlyList<PremiseSet> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (PremiseSet x in a)
        {
            bool found = false;
            foreach (PremiseSet y in b)
            {
                found |= Equal(x.Graphs, y.Graphs);
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Equal(IReadOnlyList<DataGraphIndex> a, IReadOnlyList<DataGraphIndex> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

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
