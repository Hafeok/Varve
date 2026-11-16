// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Varve.Rdf.Tests;

/// <summary>
/// A reference classification for the layer-1 tests (ADR 0230), computed
/// term by term from its definition: one binding over the given graphs,
/// data subjects the nodes typed <see cref="Person"/>, ownership along
/// <see cref="Owns"/>, transitive, and <c>rdf:type</c> and
/// <see cref="Label"/> public. It is the oracle, so it is written for
/// clarity, not speed.
/// </summary>
internal sealed class RowFixture : IRowClassification
{
    internal const string Binding = "http://ex/binding";
    internal const string Person = "http://ex/Person";
    internal const string Owns = "http://ex/ownedBy";
    internal const string Knows = "http://ex/knows";
    internal const string Label = "http://ex/label";
    internal const string TypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    internal static readonly string[] Classified = ["http://ex/g/1", "http://ex/g/2"];
    internal const string Outside = "http://ex/g/3";

    private readonly IQuadSource _source;
    private readonly Dictionary<TermHandle, SubjectSetId> _sets;
    private readonly List<TermHandle[]> _members = [[]];
    private readonly HashSet<TermHandle> _graphs;
    private readonly HashSet<TermHandle> _public;

    internal RowFixture(IQuadSource source)
    {
        _source = source;
        _sets = new Dictionary<TermHandle, SubjectSetId>(source.TermComparer);
        _graphs = Handles(source, Classified);
        _public = Handles(source, [TypeIri, Label]);
        Dictionary<RdfTerm, HashSet<RdfTerm>> subjects = SubjectsByTerm(Quads(source));
        Dictionary<string, SubjectSetId> interned = [];

        foreach ((RdfTerm node, HashSet<RdfTerm> owners) in subjects)
        {
            if (owners.Count == 0 || !source.TryInternalise(node, out TermHandle handle))
            {
                continue;
            }

            TermHandle[] members = [.. owners.Select(o => source.TryInternalise(o, out TermHandle h) ? h : TermHandle.None).OrderBy(h => h.Value)];
            string key = string.Join(",", members.Select(m => m.Value));

            if (!interned.TryGetValue(key, out SubjectSetId id))
            {
                id = new SubjectSetId(_members.Count);
                _members.Add(members);
                interned[key] = id;
            }

            _sets[handle] = id;
        }
    }

    public IReadOnlyList<RdfTerm> Bindings { get; } = [IriOf(Binding)];

    public BindingIndex BindingOf(TermHandle graph) => !graph.IsNone && _graphs.Contains(graph) ? new BindingIndex(0) : BindingIndex.None;

    public SubjectSetId SubjectsOf(BindingIndex binding, TermHandle node)
    {
        if (_sets.TryGetValue(node, out SubjectSetId set))
        {
            return set;
        }

        // A triple term carries its components' subjects.
        if (_source.TryGetTripleTermComponents(node, out TermHandle s, out _, out TermHandle o))
        {
            SubjectSetId a = SubjectsOf(binding, s);
            SubjectSetId b = SubjectsOf(binding, o);
            return a.IsNone ? b : a;
        }

        return SubjectSetId.None;
    }

    public ReadOnlySpan<TermHandle> Members(BindingIndex binding, SubjectSetId set) => _members[set.Value];

    public bool IsPublicLink(BindingIndex binding, TermHandle predicate) => _public.Contains(predicate);

    /// <summary>The subjects of every node, by term, straight from ADR 0230 point 1.</summary>
    internal static Dictionary<RdfTerm, HashSet<RdfTerm>> SubjectsByTerm(IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)> quads)
    {
        List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)> classified = [.. quads.Where(q => q.G is not null && Classified.Contains(Text(q.G)))];
        HashSet<RdfTerm> people = [.. classified.Where(q => Text(q.P) == TypeIri && Text(q.O) == Person).Select(q => q.S)];
        Dictionary<RdfTerm, HashSet<RdfTerm>> subjects = [];

        foreach (RdfTerm node in classified.SelectMany(q => new[] { q.S, q.O }))
        {
            subjects.TryAdd(node, people.Contains(node) ? [node] : []);
        }

        bool grew = true;

        while (grew)
        {
            grew = false;

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o, _) in classified.Where(q => Text(q.P) == Owns))
            {
                foreach (RdfTerm owner in subjects[o].ToArray())
                {
                    grew |= subjects[s].Add(owner);
                }
            }
        }

        return subjects;
    }

    /// <summary>Whether a caller reading <paramref name="allowed"/> sees the quad, from ADR 0230 point 4, by term.</summary>
    internal static bool Sees(Dictionary<RdfTerm, HashSet<RdfTerm>> subjects, (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G) quad, Func<RdfTerm, bool> allowed)
    {
        if (quad.G is null || !Classified.Contains(Text(quad.G)))
        {
            return true;
        }

        IEnumerable<RdfTerm> about = Of(subjects, quad.S);

        if (Text(quad.P) != TypeIri && Text(quad.P) != Label)
        {
            about = about.Concat(Of(subjects, quad.O));
        }

        return about.All(allowed);
    }

    internal static IEnumerable<RdfTerm> Of(Dictionary<RdfTerm, HashSet<RdfTerm>> subjects, RdfTerm node) =>
        subjects.TryGetValue(node, out HashSet<RdfTerm>? owners) ? owners : [];

    internal static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)> Quads(IQuadSource source)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        List<(RdfTerm, RdfTerm, RdfTerm, RdfTerm?)> all = [];

        while (cursor.MoveNext())
        {
            Quad q = cursor.Current;
            Assert(source.TryExternalise(q.Subject, out RdfTerm? s));
            Assert(source.TryExternalise(q.Predicate, out RdfTerm? p));
            Assert(source.TryExternalise(q.Object, out RdfTerm? o));
            RdfTerm? g = null;
            Assert(q.Graph.IsNone || source.TryExternalise(q.Graph, out g));
            all.Add((s!, p!, o!, g));
        }

        return all;
    }

    internal static RdfTerm IriOf(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static string Text(RdfTerm term) => Encoding.UTF8.GetString(term.Lexical);

    private static HashSet<TermHandle> Handles(IQuadSource source, string[] iris)
    {
        HashSet<TermHandle> handles = new(source.TermComparer);

        foreach (string iri in iris)
        {
            if (source.TryInternalise(IriOf(iri), out TermHandle handle))
            {
                handles.Add(handle);
            }
        }

        return handles;
    }

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("The fixture's source could not name its own term.");
        }
    }
}
