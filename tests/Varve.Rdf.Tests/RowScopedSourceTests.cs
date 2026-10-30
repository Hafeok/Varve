// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;
using static Varve.Rdf.Tests.RowFixture;

namespace Varve.Rdf.Tests;

/// <summary>
/// <see cref="RowScopedQuadSource"/> (ADRs 0230 and 0235): a caller sees a
/// classified quad exactly when every subject it is about is in its subject
/// set, a hidden node's IRI reaches it only through a public link, terms pass
/// through, and a scope restricting nothing is not a wrapper.
/// </summary>
public class RowScopedSourceTests
{
    /// <summary>Generated cases per run on the land gate; ten times as many in the nightly <c>long</c> job.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 1_000 : 10_000;

    private static readonly string[] Nodes = ["http://ex/a", "http://ex/b", "http://ex/c", "http://ex/x", "http://ex/y"];
    private static readonly string[] People = ["http://ex/a", "http://ex/b", "http://ex/c"];

    [Fact]
    public void a_scope_restricting_nothing_is_the_source_itself()
    {
        InMemoryDataset dataset = Dataset([("http://ex/a", TypeIri, Person, Classified[0])]);
        RowFixture rows = new(dataset);
        Assert.Same(dataset, RowScopedQuadSource.Wrap(dataset, rows, RowScope.All));
        Assert.Same(RowScope.All, RowScope.Of([new(IriOf(Binding), SubjectScope.All)]));
        Assert.IsType<RowScopedQuadSource>(RowScopedQuadSource.Wrap(dataset, rows, Restricted(SubjectScope.None)));
    }

    [Fact]
    public void a_link_into_a_hidden_row_is_hidden_and_a_public_link_is_not()
    {
        InMemoryDataset dataset = Dataset(
        [
            ("http://ex/a", TypeIri, Person, Classified[0]),
            ("http://ex/b", TypeIri, Person, Classified[0]),
            ("http://ex/a", Knows, "http://ex/b", Classified[0]),
            ("http://ex/a", Label, "http://ex/b", Classified[1]),
            ("http://ex/a", Knows, "http://ex/x", Classified[1]),
            ("http://ex/b", Knows, "http://ex/a", Outside),
        ]);
        IQuadSource scoped = RowScopedQuadSource.Wrap(dataset, new RowFixture(dataset), Restricted(SubjectScope.Of([IriOf("http://ex/a")], [], [], [], [])));
        HashSet<string> seen = Seen(scoped);

        Assert.Contains("http://ex/a http://www.w3.org/1999/02/22-rdf-syntax-ns#type http://ex/Person http://ex/g/1", seen);
        Assert.DoesNotContain("http://ex/a http://ex/knows http://ex/b http://ex/g/1", seen);
        Assert.Contains("http://ex/a http://ex/label http://ex/b http://ex/g/2", seen);
        Assert.Contains("http://ex/a http://ex/knows http://ex/x http://ex/g/2", seen);
        Assert.Contains("http://ex/b http://ex/knows http://ex/a http://ex/g/3", seen);
        Assert.DoesNotContain("http://ex/b http://www.w3.org/1999/02/22-rdf-syntax-ns#type http://ex/Person http://ex/g/1", seen);

        // Terms pass through: a term's existence is not a row's.
        Assert.True(scoped.TryInternalise(IriOf("http://ex/b"), out TermHandle b));
        Assert.True(scoped.TryExternalise(b, out RdfTerm? name));
        Assert.Equal(IriOf("http://ex/b"), name);
    }

    [Fact]
    public void ownership_is_transitive_through_links()
    {
        InMemoryDataset dataset = Dataset(
        [
            ("http://ex/a", TypeIri, Person, Classified[0]),
            ("http://ex/x", Owns, "http://ex/a", Classified[0]),
            ("http://ex/y", Owns, "http://ex/x", Classified[1]),
            ("http://ex/y", Label, "http://ex/x", Classified[1]),
        ]);
        RowFixture rows = new(dataset);
        Assert.Empty(Seen(new RowScopedQuadSource(dataset, rows, Restricted(SubjectScope.None))));
        Assert.Equal(4, Seen(new RowScopedQuadSource(dataset, rows, Restricted(SubjectScope.Of([], ["http://ex/a"], [], [], [])))).Count);
    }

    [Fact]
    public void a_class_or_a_link_to_the_caller_names_subjects_from_their_own_rows()
    {
        InMemoryDataset dataset = Dataset(
        [
            ("http://ex/a", TypeIri, Person, Classified[0]),
            ("http://ex/a", TypeIri, "http://ex/Client", Classified[0]),
            ("http://ex/b", TypeIri, Person, Classified[0]),
            ("http://ex/b", "http://ex/caseworker", "http://ex/me", Classified[1]),
            ("http://ex/c", TypeIri, Person, Classified[0]),
            ("http://ex/c", "http://ex/caseworker", "http://ex/me", Outside),
        ]);
        RowFixture rows = new(dataset);
        HashSet<string> byClass = Seen(new RowScopedQuadSource(dataset, rows, Restricted(SubjectScope.Of([], [], [IriOf("http://ex/Client")], [], []))));
        Assert.Equal(["http://ex/a"], byClass.Where(q => q.Contains("/g/1", StringComparison.Ordinal)).Select(q => q.Split(' ')[0]).Distinct());
        HashSet<string> byLink = Seen(new RowScopedQuadSource(dataset, rows, Restricted(SubjectScope.Of([], [], [], [new SubjectLink(IriOf("http://ex/caseworker"), IriOf("http://ex/me"))], []))));
        Assert.Equal(["http://ex/b"], byLink.Where(q => q.Contains("/g/1", StringComparison.Ordinal)).Select(q => q.Split(' ')[0]).Distinct());
    }

    /// <summary>
    /// ADR 0230 point 7 at the source: for generated datasets and subject
    /// sets, the row-scoped view equals the quads the definition says the
    /// caller sees, through every pattern, and carries no IRI of a classified
    /// node outside the set except through a public link.
    /// </summary>
    [Fact]
    public void the_row_scoped_source_shows_exactly_the_quads_the_rule_allows()
    {
        Gen.Select(Quads.List[0, 16], Gen.Int[0, 7], Gen.Bool, (quads, set, prefix) => (quads, set, prefix)).Sample(sample =>
        {
            (List<(string, string, string, string?)> quads, int set, bool prefix) = sample;
            HashSet<RdfTerm> allowed = [.. People.Where((_, i) => (set & (1 << i)) != 0).Select(IriOf)];
            SubjectScope scope = prefix && set == 7 ? SubjectScope.Of([], ["http://ex/"], [], [], []) : SubjectScope.Of([.. allowed], [], [], [], []);
            Func<RdfTerm, bool> admits = prefix && set == 7 ? _ => true : allowed.Contains;
            InMemoryDataset dataset = Dataset(quads);
            IQuadSource scoped = new RowScopedQuadSource(dataset, new RowFixture(dataset), Restricted(scope));
            List<(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)> all = [.. Quads(dataset)];
            Dictionary<RdfTerm, HashSet<RdfTerm>> subjects = SubjectsByTerm(all);
            HashSet<string> expected = [.. all.Where(q => Sees(subjects, q, admits)).Select(Line)];

            Assert.Equal(expected, Seen(scoped));
            Assert.Equal(expected.Count(q => q.Contains("/g/1", StringComparison.Ordinal)), Count(scoped, dataset, Classified[0]));

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) in all)
            {
                Quad quad = Handles(dataset, s, p, o, g);
                Assert.Equal(Sees(subjects, (s, p, o, g), admits), scoped.Contains(quad));
            }

            // No hidden node's IRI in a classified quad, unless a public link
            // names it; a graph outside every binding is its graph's alone.
            HashSet<RdfTerm> hidden = [.. subjects.Where(kv => kv.Value.Count > 0 && !kv.Value.All(admits)).Select(kv => kv.Key)];
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g) in Quads(scoped).Where(q => q.G is not null && Classified.Contains(Text(q.G))))
            {
                Assert.DoesNotContain(s, hidden);
                Assert.True(!hidden.Contains(o) || Text(p) is TypeIri or Label);
            }
        }, iter: Iterations);
    }

    /// <summary>Once a subject set is decided, reading a quad again allocates nothing (ADR 0235 point 3).</summary>
    [Fact]
    public void a_decided_quad_is_read_without_allocating()
    {
        InMemoryDataset dataset = Dataset([("http://ex/a", TypeIri, Person, Classified[0]), ("http://ex/b", TypeIri, Person, Classified[0]), ("http://ex/a", Knows, "http://ex/b", Classified[0])]);
        RowScopedQuadSource scoped = new(dataset, new RowFixture(dataset), Restricted(SubjectScope.Of([IriOf("http://ex/a")], [], [], [], [])));
        Quad[] quads = [.. Materialise(dataset)];
        bool[] first = [.. quads.Select(q => scoped.Allows(in q))];
        Assert.Equal([true, false, false], first.OrderByDescending(b => b));

        long before = GC.GetAllocatedBytesForCurrentThread();
        int shown = 0;

        for (int i = 0; i < 10_000; i++)
        {
            foreach (Quad quad in quads)
            {
                shown += scoped.Allows(in quad) ? 1 : 0;
            }
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10_000, shown);
    }

    internal static Gen<(string, string, string, string?)> Quads { get; } = Gen.Select(
        Gen.OneOfConst(Nodes),
        Gen.OneOfConst(TypeIri, Owns, Knows, Label),
        Gen.OneOfConst([.. Nodes, Person, "lit"]),
        Gen.OneOfConst<string?>(Classified[0], Classified[1], Outside, null),
        (s, p, o, g) => (s, p, p == TypeIri ? (o == "lit" ? Person : o) : o, g));

    internal static RowScope Restricted(SubjectScope subjects) => RowScope.Of([new(IriOf(Binding), subjects)]);

    internal static InMemoryDataset Dataset(IEnumerable<(string S, string P, string O, string? G)> quads)
    {
        InMemoryDatasetBuilder builder = new();

        foreach ((string s, string p, string o, string? g) in quads)
        {
            builder.Add(IriOf(s), IriOf(p), o == "lit" ? RdfTerm.Literal("lit"u8) : IriOf(o), g is null ? null : IriOf(g));
        }

        return builder.ToDataset();
    }

    internal static HashSet<string> Seen(IQuadSource source) => [.. Quads(source).Select(Line)];

    private static int Count(IQuadSource scoped, InMemoryDataset dataset, string graph)
    {
        if (!dataset.TryInternalise(IriOf(graph), out TermHandle g))
        {
            return 0;
        }

        using IQuadCursor cursor = scoped.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(g));
        int count = 0;

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    private static List<Quad> Materialise(InMemoryDataset source)
    {
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        List<Quad> quads = [];

        while (cursor.MoveNext())
        {
            quads.Add(cursor.Current);
        }

        return quads;
    }

    private static Quad Handles(InMemoryDataset dataset, RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g)
    {
        Assert.True(dataset.TryInternalise(s, out TermHandle sh));
        Assert.True(dataset.TryInternalise(p, out TermHandle ph));
        Assert.True(dataset.TryInternalise(o, out TermHandle oh));
        TermHandle gh = TermHandle.None;
        Assert.True(g is null || dataset.TryInternalise(g, out gh));
        return new Quad(sh, ph, oh, gh);
    }

    private static string Line((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G) q) =>
        $"{Text(q.S)} {Text(q.P)} {Text(q.O)} {(q.G is null ? "default" : Text(q.G))}";
}
