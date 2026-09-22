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
        InMemoryDataset dataset = new();

        TermHandle first = dataset.Internalise(Iri("http://a/b"));
        TermHandle again = dataset.Internalise(Iri("http://a/b"));
        TermHandle other = dataset.Internalise(Iri("http://a/c"));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.False(first.IsNone);
        Assert.Equal(2, dataset.TermCount);
    }

    [Fact]
    public void a_term_the_dataset_has_not_seen_has_no_handle()
    {
        InMemoryDataset dataset = new();
        dataset.Internalise(Iri("http://a/b"));

        Assert.False(dataset.TryInternalise(Iri("http://a/c"), out TermHandle handle));
        Assert.True(handle.IsNone);
        Assert.True(dataset.TryInternalise(Iri("http://a/b"), out _));
    }

    [Fact]
    public void externalising_round_trips_and_refuses_an_unknown_handle()
    {
        InMemoryDataset dataset = new();
        TermHandle handle = dataset.Internalise(RdfTerm.Literal(U("chat"), U("EN")));

        Assert.True(dataset.TryExternalise(handle, out RdfTerm? term));
        Assert.Equal(RdfTerm.Literal(U("chat"), U("en")), term);

        Assert.False(dataset.TryExternalise(TermHandle.None, out _));
        Assert.False(dataset.TryExternalise(new TermHandle(99), out _));
    }

    [Fact]
    public void adding_the_same_quad_twice_adds_it_once()
    {
        InMemoryDataset dataset = new();

        Assert.True(dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o")));
        Assert.False(dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o")));
        Assert.Equal(1, dataset.Count);
    }

    [Fact]
    public void the_same_triple_in_two_graphs_is_two_quads()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g"));

        Assert.Equal(2, dataset.Count);
    }

    [Fact]
    public void a_wildcard_in_every_position_matches_everything()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g"));

        List<Quad> all = Drain(dataset.Match(
            TermHandle.None, TermHandle.None, TermHandle.None, TermHandle.None));

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void the_default_graph_is_asked_for_by_its_own_method()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"), Iri("http://a/g"));

        List<Quad> quads = Drain(dataset.MatchDefaultGraph(
            TermHandle.None, TermHandle.None, TermHandle.None));

        Assert.Single(quads);
        Assert.True(quads[0].IsDefaultGraph);
    }

    [Fact]
    public void a_bound_position_narrows_the_match()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o1"));
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o2"));
        dataset.Add(Iri("http://a/t"), Iri("http://a/p"), Iri("http://a/o1"));

        TermHandle s = dataset.Internalise(Iri("http://a/s"));
        List<Quad> quads = Drain(dataset.Match(s, TermHandle.None, TermHandle.None, TermHandle.None));

        Assert.Equal(2, quads.Count);
        Assert.All(quads, quad => Assert.Equal(s, quad.Subject));
    }

    [Fact]
    public void a_term_that_appears_in_no_quad_matches_nothing()
    {
        InMemoryDataset dataset = new();
        dataset.Add(Iri("http://a/s"), Iri("http://a/p"), Iri("http://a/o"));
        TermHandle absent = dataset.Internalise(Iri("http://a/z"));

        Assert.Empty(Drain(dataset.Match(absent, TermHandle.None, TermHandle.None, TermHandle.None)));
    }

    [Fact]
    public void contains_and_remove_agree_with_add()
    {
        InMemoryDataset dataset = new();
        Quad quad = new(
            dataset.Internalise(Iri("http://a/s")),
            dataset.Internalise(Iri("http://a/p")),
            dataset.Internalise(Iri("http://a/o")));

        Assert.False(dataset.Contains(quad));
        Assert.True(dataset.Add(quad));
        Assert.True(dataset.Contains(quad));
        Assert.True(dataset.Remove(quad));
        Assert.False(dataset.Remove(quad));
        Assert.Equal(0, dataset.Count);
    }

    [Fact]
    public void the_source_supplies_the_comparer_and_it_is_used_for_terms()
    {
        InMemoryDataset dataset = new();
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

        InMemoryDataset dataset = new();
        TermHandle fromView = dataset.Internalise(arena.View(text, slot).Materialise());
        TermHandle fromTerm = dataset.Internalise(Iri("http://a/s"));

        Assert.Equal(fromView, fromTerm);
        Assert.Equal(1, dataset.TermCount);
    }
}
