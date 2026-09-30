// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

public class DatasetTests
{
    private static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    private static RdfTerm Iri(string text) => RdfTerm.Iri(U(text));

    private static List<Quad> Drain(IQuadCursor cursor)
    {
        using (cursor)
        {
            List<Quad> quads = [];

            while (cursor.MoveNext())
            {
                quads.Add(cursor.Current);
            }

            return quads;
        }
    }

    [Fact]
    public void interning_is_stable_and_never_issues_the_none_handle()
    {
        InMemoryDatasetBuilder builder = new();

        TermHandle first = builder.Internalise(Iri("http://a/b"));
        TermHandle again = builder.Internalise(Iri("http://a/b"));
        TermHandle other = builder.Internalise(Iri("http://a/c"));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.False(first.IsNone);
        Assert.Equal(2, builder.ToDataset().TermCount);
    }

    [Fact]
    public void a_term_the_dataset_has_not_seen_has_no_handle()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Internalise(Iri("http://a/b"));
        InMemoryDataset dataset = builder.ToDataset();

        Assert.False(dataset.TryInternalise(Iri("http://a/c"), out TermHandle handle));
        Assert.True(handle.IsNone);
        Assert.True(dataset.TryInternalise(Iri("http://a/b"), out _));
    }

    [Fact]
    public void externalising_round_trips_and_refuses_an_unknown_handle()
    {
        InMemoryDatasetBuilder builder = new();
        TermHandle handle = builder.Internalise(RdfTerm.Literal(U("chat"), U("EN")));
        InMemoryDataset dataset = builder.ToDataset();

        Assert.True(dataset.TryExternalise(handle, out RdfTerm? term));
        Assert.Equal(RdfTerm.Literal(U("chat"), U("en")), term);

        Assert.False(dataset.TryExternalise(TermHandle.None, out _));
        Assert.False(dataset.TryExternalise(new TermHandle(99), out _));
    }

    [Fact]
    public void adding_the_same_quad_twice_adds_it_once()
    {
        InMemoryDatasetBuilder builder = new();

        Assert.True(builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o")));
        Assert.False(builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o")));
        Assert.Equal(1, builder.ToDataset().Count.Value);
    }

    [Fact]
    public void the_same_triple_in_two_graphs_is_two_quads()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g"));

        Assert.Equal(2, builder.ToDataset().Count.Value);
    }

    /// <summary>
    /// Three quads: one in the default graph, one in g1, one in g2. Every graph
    /// mode is measured against the same dataset so the four are comparable.
    /// </summary>
    private static InMemoryDataset ThreeGraphs() => ThreeGraphsBuilder().ToDataset();

    /// <summary>The builder behind <see cref="ThreeGraphs"/>, for a test that interns more.</summary>
    private static InMemoryDatasetBuilder ThreeGraphsBuilder()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g1"));
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g2"));
        return builder;
    }

    [Fact]
    public void the_default_graph_mode_matches_the_default_graph_alone()
    {
        InMemoryDataset dataset = ThreeGraphs();

        List<Quad> quads = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph));

        Assert.Single(quads);
        Assert.True(quads[0].IsDefaultGraph);
    }

    [Fact]
    public void the_named_mode_matches_one_graph()
    {
        InMemoryDatasetBuilder builder = ThreeGraphsBuilder();
        TermHandle g1 = builder.Internalise(Iri("http://a/g1"));
        InMemoryDataset dataset = builder.ToDataset();

        List<Quad> quads = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(g1)));

        Assert.Single(quads);
        Assert.Equal(g1, quads[0].Graph);
    }

    /// <summary>SPARQL 1.1 §13.3: <c>GRAPH ?g</c> does not range over the default graph.</summary>
    [Fact]
    public void the_any_named_mode_excludes_the_default_graph()
    {
        InMemoryDataset dataset = ThreeGraphs();

        List<Quad> quads = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.AnyNamed));

        Assert.Equal(2, quads.Count);
        Assert.DoesNotContain(quads, quad => quad.IsDefaultGraph);
    }

    [Fact]
    public void the_any_mode_includes_the_default_graph()
    {
        InMemoryDataset dataset = ThreeGraphs();

        List<Quad> quads = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any));

        Assert.Equal(3, quads.Count);
    }

    /// <summary>
    /// The default graph and every named graph partition the dataset, and
    /// <see cref="GraphPattern.Any"/> is their union. This is the property the
    /// old wildcard could not express, and getting it wrong is the failure mode
    /// the four modes exist to prevent.
    /// </summary>
    [Fact]
    public void the_modes_partition_the_dataset()
    {
        InMemoryDataset dataset = ThreeGraphs();

        int inDefault = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph)).Count;
        int inNamed = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.AnyNamed)).Count;
        int inAny = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any)).Count;

        Assert.Equal(inAny, inDefault + inNamed);
        Assert.Equal(dataset.Count.Value, inAny);
    }

    [Fact]
    public void the_absent_handle_is_not_the_name_of_a_graph()
    {
        Assert.Throws<ArgumentException>(() => GraphPattern.Named(TermHandle.None));
    }

    [Fact]
    public void a_named_graph_that_holds_no_quad_matches_nothing()
    {
        InMemoryDatasetBuilder builder = ThreeGraphsBuilder();
        TermHandle absent = builder.Internalise(Iri("http://a/g3"));
        InMemoryDataset dataset = builder.ToDataset();

        Assert.Empty(Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(absent))));
    }

    [Fact]
    public void a_bound_position_narrows_the_match()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o1"));
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o2"));
        builder.Add(Iri("http://a/t"), Iri("http://a/p"), Iri("http://a/o1"));

        TermHandle s = builder.Internalise(Iri("http://a/s"));
        InMemoryDataset dataset = builder.ToDataset();
        List<Quad> quads = Drain(dataset.Match(s, TermHandle.None, TermHandle.None, GraphPattern.Any));

        Assert.Equal(2, quads.Count);
        Assert.All(quads, quad => Assert.Equal(s, quad.Subject));
    }

    [Fact]
    public void a_term_that_appears_in_no_quad_matches_nothing()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        TermHandle absent = builder.Internalise(Iri("http://a/z"));
        InMemoryDataset dataset = builder.ToDataset();

        Assert.Empty(Drain(dataset.Match(absent, TermHandle.None, TermHandle.None, GraphPattern.Any)));
    }

    [Fact]
    public void a_graph_pattern_compares_by_mode_and_name()
    {
        Assert.Equal(GraphPattern.Any, GraphPattern.Any);
        Assert.NotEqual(GraphPattern.Any, GraphPattern.AnyNamed);
        Assert.NotEqual(GraphPattern.DefaultGraph, GraphPattern.AnyNamed);
        Assert.Equal(GraphPattern.Named(new TermHandle(7)), GraphPattern.Named(new TermHandle(7)));
        Assert.NotEqual(GraphPattern.Named(new TermHandle(7)), GraphPattern.Named(new TermHandle(8)));
        Assert.Equal(GraphPattern.Any.GetHashCode(), GraphPattern.Any.GetHashCode());
    }

    /// <summary>
    /// <c>default(GraphPattern)</c> is the default graph, which is the reading
    /// that cannot silently widen a query.
    /// </summary>
    [Fact]
    public void the_default_pattern_is_the_default_graph()
    {
        Assert.Equal(GraphMatch.DefaultGraph, default(GraphPattern).Match);
        Assert.Equal(GraphPattern.DefaultGraph, default(GraphPattern));
    }

    [Fact]
    public void contains_and_remove_agree_with_add()
    {
        InMemoryDatasetBuilder builder = new();
        Quad quad = new(
            builder.Internalise(Iri("http://a/s")),
            builder.Internalise(Iri("http://a/p")),
            builder.Internalise(Iri("http://a/o")));

        Assert.False(builder.ToDataset().Contains(quad));
        Assert.True(builder.Add(quad));
        Assert.True(builder.ToDataset().Contains(quad));
        Assert.True(builder.Remove(quad));
        Assert.False(builder.Remove(quad));
        Assert.False(builder.ToDataset().Contains(quad));
        Assert.Equal(0, builder.ToDataset().Count.Value);
    }

    /// <summary>
    /// ADR 0067's two promises: a snapshot is a value, so a later addition to
    /// the builder does not reach it; and a handle the builder issued means the
    /// same term in every snapshot it takes, before and after that addition.
    /// </summary>
    [Fact]
    public void a_snapshot_is_unchanged_by_later_additions_and_handles_are_stable_across_snapshots()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        TermHandle s = builder.Internalise(Iri("http://a/s"));
        InMemoryDataset before = builder.ToDataset();

        Assert.True(builder.Add(Iri("http://a/t"), Iri("http://a/p"), Iri("http://a/o2")));
        InMemoryDataset after = builder.ToDataset();

        Assert.Equal(1, before.Count.Value);
        Assert.Equal(3, before.TermCount);
        Assert.False(before.TryInternalise(Iri("http://a/t"), out _));
        Assert.Single(Drain(before.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any)));

        Assert.Equal(2, after.Count.Value);
        Assert.Equal(5, after.TermCount);

        Assert.True(before.TryExternalise(s, out RdfTerm? inBefore));
        Assert.True(after.TryExternalise(s, out RdfTerm? inAfter));
        Assert.Equal(Iri("http://a/s"), inBefore);
        Assert.Equal(inBefore, inAfter);
        Assert.True(after.TryInternalise(Iri("http://a/s"), out TermHandle again));
        Assert.True(after.TermComparer.Equals(s, again));
    }

    [Fact]
    public void the_source_supplies_the_comparer_and_it_is_used_for_terms()
    {
        InMemoryDataset dataset = new InMemoryDatasetBuilder().ToDataset();
        IEqualityComparer<TermHandle> comparer = ((IQuadSource)dataset).TermComparer;

        Assert.True(comparer.Equals(new TermHandle(3), new TermHandle(3)));
        Assert.False(comparer.Equals(new TermHandle(3), new TermHandle(4)));
    }

    [Fact]
    public void a_materialised_view_can_be_interned()
    {
        byte[] text = U("http://a/s");
        TermArena arena = new();
        int slot = arena.AddIri(TermSpan.FromText(0, text.Length));

        InMemoryDatasetBuilder builder = new();
        TermHandle fromView = builder.Internalise(arena.View(text, slot).Materialise());
        TermHandle fromTerm = builder.Internalise(Iri("http://a/s"));

        Assert.Equal(fromView, fromTerm);
        Assert.Equal(1, builder.ToDataset().TermCount);
    }
}
