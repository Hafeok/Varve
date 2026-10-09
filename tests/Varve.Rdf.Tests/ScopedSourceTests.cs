// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// <see cref="GraphScopedQuadSource"/> (ADR 0107): every way of reaching a
/// quad — a named pattern, the default graph, an enumeration over every
/// graph, <c>Contains</c>, <c>Estimate</c> — sees exactly the scope's graphs,
/// for explicit and prefix scopes, and the scope of every graph is not a
/// wrapper at all.
/// </summary>
public class ScopedSourceTests
{
    private static readonly string[] GraphNames = ["http://ex/g/a", "http://ex/g/b", "http://other/c"];

    [Fact]
    public void the_scope_of_every_graph_is_the_source_itself()
    {
        InMemoryDataset dataset = Dataset([("s", "p", "o", null)]);
        Assert.Same(dataset, GraphScopedQuadSource.Wrap(dataset, GraphScope.All));
        Assert.IsType<GraphScopedQuadSource>(GraphScopedQuadSource.Wrap(dataset, GraphScope.None));
    }

    [Fact]
    public void an_explicit_scope_shows_its_graphs_through_every_pattern_and_hides_the_rest()
    {
        InMemoryDataset dataset = Dataset([("s", "p", "o", null), ("s", "p", "o", GraphNames[0]), ("s", "p", "o", GraphNames[1]), ("t", "p", "o", GraphNames[2])]);
        GraphScope scope = GraphScope.Of([Iri(GraphNames[0])], [], DefaultGraphAccess.Excluded);
        GraphScopedQuadSource scoped = new(dataset, scope);
        Assert.True(dataset.TryInternalise(Iri(GraphNames[0]), out TermHandle a));
        Assert.True(dataset.TryInternalise(Iri(GraphNames[1]), out TermHandle b));
        Assert.True(dataset.TryInternalise(Iri("s"), out TermHandle s));

        Assert.Equal(1, Count(scoped, GraphPattern.Named(a)));
        Assert.Equal(0, Count(scoped, GraphPattern.Named(b)));
        Assert.Equal(0, Count(scoped, GraphPattern.DefaultGraph));
        Assert.Equal(1, Count(scoped, GraphPattern.AnyNamed));
        Assert.Equal(1, Count(scoped, GraphPattern.Any));
        Assert.Equal(4, Count(dataset, GraphPattern.Any));

        Assert.True(scoped.Contains(Quad(dataset, "s", "p", "o", a)));
        Assert.False(scoped.Contains(Quad(dataset, "s", "p", "o", b)));
        Assert.False(scoped.Contains(Quad(dataset, "s", "p", "o", TermHandle.None)));

        Assert.Equal(0L, scoped.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(b)).Count.Value);
        Assert.True(scoped.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(b)).IsExact);
        Assert.Equal(dataset.Estimate(s, TermHandle.None, TermHandle.None, GraphPattern.Named(a)), scoped.Estimate(s, TermHandle.None, TermHandle.None, GraphPattern.Named(a)));
        CardinalityEstimate everything = scoped.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        Assert.False(everything.IsExact);
        Assert.Equal(4L, everything.Count.Value);

        // Terms pass through: existence of a term is not existence of a graph.
        Assert.True(scoped.TryInternalise(Iri(GraphNames[2]), out _));
        Assert.True(scoped.TryExternalise(b, out RdfTerm? name));
        Assert.Equal(Iri(GraphNames[1]), name);
    }

    [Fact]
    public void a_prefix_scope_decides_by_iri_and_the_default_graph_by_its_flag()
    {
        InMemoryDataset dataset = Dataset([("s", "p", "o", null), ("s", "p", "o", GraphNames[0]), ("s", "p", "o", GraphNames[1]), ("t", "p", "o", GraphNames[2])]);
        GraphScope scope = GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Included);
        GraphScopedQuadSource scoped = new(dataset, scope);
        Assert.True(dataset.TryInternalise(Iri(GraphNames[2]), out TermHandle c));

        Assert.Equal(3, Count(scoped, GraphPattern.Any));
        Assert.Equal(2, Count(scoped, GraphPattern.AnyNamed));
        Assert.Equal(1, Count(scoped, GraphPattern.DefaultGraph));
        Assert.Equal(0, Count(scoped, GraphPattern.Named(c)));
        Assert.True(scope.Allows(null));
        Assert.True(scope.Allows(Iri("http://ex/g/zzz")));
        Assert.False(scope.Allows(Iri("http://ex/other")));
    }

    [Fact]
    public void a_union_takes_in_both_sides_and_all_absorbs()
    {
        GraphScope a = GraphScope.Of([Iri(GraphNames[0])], ["http://p/"], DefaultGraphAccess.Excluded);
        GraphScope b = GraphScope.Of([Iri(GraphNames[1])], ["http://p/", "http://q/"], DefaultGraphAccess.Included);
        GraphScope both = a.Union(b);
        Assert.True(both.IncludesDefault);
        Assert.Equal(2, both.Graphs.Count);
        Assert.True(both.Allows(Iri("http://q/x")));
        Assert.True(both.Allows(Iri(GraphNames[0])));
        Assert.False(both.Allows(Iri("http://r/x")));
        Assert.Same(GraphScope.All, a.Union(GraphScope.All));
        Assert.True(new CallerScope(GraphScope.None, GraphScope.None, AdminAccess.Admin).Readable.IsAll);
        Assert.False(new CallerScope(GraphScope.None, GraphScope.None, AdminAccess.None).Writable.IsAll);
    }

    /// <summary>
    /// The scoped view of a dataset equals the dataset holding only the
    /// scope's graphs, for every pattern: ADR 0107's first property, at the
    /// source where the evaluator reads.
    /// </summary>
    [Fact]
    public void the_scoped_source_equals_the_sub_dataset_of_the_scopes_graphs()
    {
        Gen<string?> graph = Gen.OneOfConst<string?>(null, GraphNames[0], GraphNames[1], GraphNames[2]);
        Gen<(string, string, string, string?)> quad = Gen.Select(Gen.OneOfConst("s", "t"), Gen.OneOfConst("p", "q"), Gen.OneOfConst("o", "u", "s"), graph, (s, p, o, g) => (s, p, o, g));
        Gen<(bool Default, bool A, bool B, bool Prefix)> scopes = Gen.Select(Gen.Bool, Gen.Bool, Gen.Bool, Gen.Bool, (d, a, b, p) => (d, a, b, p));
        Gen.Select(quad.List[0, 12], scopes, (quads, which) => (quads, which)).Sample(sample =>
        {
            (List<(string, string, string, string?)> quads, (bool d, bool a, bool b, bool prefix) which) = sample;
            List<RdfTerm> named = [];

            if (which.a)
            {
                named.Add(Iri(GraphNames[0]));
            }

            if (which.b)
            {
                named.Add(Iri(GraphNames[1]));
            }

            GraphScope scope = GraphScope.Of([.. named], which.prefix ? ["http://other/"] : [], which.d ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);
            InMemoryDataset whole = Dataset(quads);
            InMemoryDataset sub = Dataset(quads.FindAll(q => scope.Allows(q.Item4 is null ? null : Iri(q.Item4))));
            IQuadSource scoped = GraphScopedQuadSource.Wrap(whole, scope);

            foreach (GraphPattern pattern in Patterns(whole))
            {
                foreach ((TermHandle s, TermHandle p) in Subjects(whole))
                {
                    HashSet<string> expected = Materialised(sub, sub.Match(Resolve(sub, whole, s), Resolve(sub, whole, p), TermHandle.None, Resolve(sub, whole, pattern)));
                    HashSet<string> actual = Materialised(whole, scoped.Match(s, p, TermHandle.None, pattern));
                    Assert.Equal(expected, actual);
                }
            }
        }, iter: 300);
    }

    private static IEnumerable<GraphPattern> Patterns(InMemoryDataset dataset)
    {
        yield return GraphPattern.Any;
        yield return GraphPattern.AnyNamed;
        yield return GraphPattern.DefaultGraph;

        foreach (string name in GraphNames)
        {
            if (dataset.TryInternalise(Iri(name), out TermHandle handle))
            {
                yield return GraphPattern.Named(handle);
            }
        }
    }

    private static IEnumerable<(TermHandle, TermHandle)> Subjects(InMemoryDataset dataset)
    {
        yield return (TermHandle.None, TermHandle.None);

        if (dataset.TryInternalise(Iri("s"), out TermHandle s))
        {
            yield return (s, TermHandle.None);
        }

        if (dataset.TryInternalise(Iri("p"), out TermHandle p))
        {
            yield return (TermHandle.None, p);
        }
    }

    // A handle of the whole dataset, as the sub-dataset names the same term; None when it has no such term.
    private static TermHandle Resolve(InMemoryDataset sub, InMemoryDataset whole, TermHandle handle) =>
        handle.IsNone ? handle : whole.TryExternalise(handle, out RdfTerm? term) && sub.TryInternalise(term, out TermHandle resolved) ? resolved : new TermHandle(ulong.MaxValue);

    private static GraphPattern Resolve(InMemoryDataset sub, InMemoryDataset whole, GraphPattern pattern) =>
        pattern.Match == GraphMatch.Named ? GraphPattern.Named(Resolve(sub, whole, pattern.Graph)) : pattern;

    private static HashSet<string> Materialised(IQuadSource names, IQuadCursor cursor)
    {
        using (cursor)
        {
            HashSet<string> quads = [];

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                quads.Add(Name(names, quad.Subject) + " " + Name(names, quad.Predicate) + " " + Name(names, quad.Object) + " " + (quad.Graph.IsNone ? "" : Name(names, quad.Graph)));
            }

            return quads;
        }
    }

    private static string Name(IQuadSource names, TermHandle handle) =>
        names.TryExternalise(handle, out RdfTerm? term) ? Encoding.UTF8.GetString(term.Lexical) : "?";

    private static int Count(IQuadSource source, GraphPattern graph)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, graph);
        int count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static Quad Quad(InMemoryDataset dataset, string s, string p, string o, TermHandle graph)
    {
        Assert.True(dataset.TryInternalise(Iri(s), out TermHandle sh));
        Assert.True(dataset.TryInternalise(Iri(p), out TermHandle ph));
        Assert.True(dataset.TryInternalise(Iri(o), out TermHandle oh));
        return new Quad(sh, ph, oh, graph);
    }

    private static InMemoryDataset Dataset(IEnumerable<(string S, string P, string O, string? G)> quads)
    {
        InMemoryDatasetBuilder builder = new();

        foreach ((string s, string p, string o, string? g) in quads)
        {
            builder.Add(Iri(s), Iri(p), Iri(o), g is null ? null : Iri(g));
        }

        return builder.ToDataset();
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri.Contains(':', StringComparison.Ordinal) ? iri : "http://ex/" + iri));
}
