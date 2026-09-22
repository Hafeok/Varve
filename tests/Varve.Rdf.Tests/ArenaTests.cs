using System;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

public class ArenaTests
{
    private static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void a_view_reads_the_input_in_place()
    {
        byte[] text = U("<http://a/b> <http://a/p> \"o\" .");
        TermArena arena = new();
        int subject = arena.AddIri(TermSpan.FromText(1, 10));

        RdfTermView view = arena.View(text, subject);

        Assert.Equal(RdfTermKind.Iri, view.Kind);
        Assert.True(view.Lexical.SequenceEqual(U("http://a/b")));
    }

    [Fact]
    public void a_view_reads_unescaped_bytes_from_the_scratch()
    {
        byte[] text = U("\"a\\nb\"");
        TermArena arena = new();
        TermSpan lexical = arena.AppendScratch(U("a\nb"));
        int slot = arena.AddLiteral(lexical, TermSpan.None, TermSpan.None, TextDirection.None);

        RdfTermView view = arena.View(text, slot);

        Assert.True(view.Lexical.SequenceEqual(U("a\nb")));
        Assert.False(view.HasDatatype);
        Assert.False(view.HasLanguage);
    }

    [Fact]
    public void reserve_then_commit_writes_at_the_tail()
    {
        TermArena arena = new();
        TermSpan first = arena.AppendScratch(U("one"));

        Span<byte> room = arena.ReserveScratch(3);
        U("two").CopyTo(room);
        TermSpan second = arena.CommitScratch(3);

        Assert.Equal(0, first.Start);
        Assert.Equal(3, second.Start);
        Assert.Equal(6, arena.ScratchLength);

        int a = arena.AddIri(first);
        int b = arena.AddIri(second);

        Assert.True(arena.View([], a).Lexical.SequenceEqual(U("one")));
        Assert.True(arena.View([], b).Lexical.SequenceEqual(U("two")));
    }

    [Fact]
    public void a_literal_carries_its_datatype_and_its_language_separately()
    {
        byte[] text = U("chaten");
        TermArena arena = new();

        int tagged = arena.AddLiteral(
            TermSpan.FromText(0, 4),
            TermSpan.None,
            TermSpan.FromText(4, 2),
            TextDirection.RightToLeft);

        RdfTermView view = arena.View(text, tagged);

        Assert.True(view.Lexical.SequenceEqual(U("chat")));
        Assert.True(view.HasLanguage);
        Assert.True(view.Language.SequenceEqual(U("en")));
        Assert.False(view.HasDatatype);
        Assert.Equal(TextDirection.RightToLeft, view.Direction);
    }

    [Fact]
    public void a_triple_term_view_nests_by_index()
    {
        byte[] text = U("spo");
        TermArena arena = new();
        int s = arena.AddIri(TermSpan.FromText(0, 1));
        int p = arena.AddIri(TermSpan.FromText(1, 1));
        int o = arena.AddIri(TermSpan.FromText(2, 1));
        int inner = arena.AddTripleTerm(s, p, o);
        int outer = arena.AddTripleTerm(inner, p, o);

        RdfTermView view = arena.View(text, outer);

        Assert.Equal(RdfTermKind.TripleTerm, view.Kind);
        Assert.Equal(RdfTermKind.TripleTerm, view.Subject.Kind);
        Assert.True(view.Subject.Subject.Lexical.SequenceEqual(U("s")));
    }

    [Fact]
    public void reset_drops_the_slots_and_the_scratch_but_keeps_the_buffers()
    {
        TermArena arena = new();
        arena.AddIri(arena.AppendScratch(U("http://a/b")));

        Assert.Equal(1, arena.Count);
        Assert.Equal(10, arena.ScratchLength);

        arena.Reset();

        Assert.Equal(0, arena.Count);
        Assert.Equal(0, arena.ScratchLength);
    }

    [Fact]
    public void the_arena_grows_past_its_initial_capacity()
    {
        TermArena arena = new();

        for (int i = 0; i < 100; i++)
        {
            arena.AddIri(arena.AppendScratch(U(new string('x', 40))));
        }

        Assert.Equal(100, arena.Count);
        Assert.Equal(4000, arena.ScratchLength);
        Assert.True(arena.View([], 99).Lexical.SequenceEqual(U(new string('x', 40))));
    }

    [Fact]
    public void a_slot_that_was_never_added_is_refused()
    {
        TermArena arena = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.View([], 0).Kind);
    }

    [Fact]
    public void a_quad_view_reports_whether_it_has_a_graph()
    {
        byte[] text = U("spog");
        TermArena arena = new();
        int s = arena.AddIri(TermSpan.FromText(0, 1));
        int p = arena.AddIri(TermSpan.FromText(1, 1));
        int o = arena.AddIri(TermSpan.FromText(2, 1));
        int g = arena.AddIri(TermSpan.FromText(3, 1));

        Assert.False(arena.Quad(text, s, p, o, -1).HasGraph);
        Assert.Throws<InvalidOperationException>(() => arena.Quad(text, s, p, o, -1).Graph.Kind);

        QuadView quad = arena.Quad(text, s, p, o, g);
        Assert.True(quad.HasGraph);
        Assert.True(quad.Graph.Lexical.SequenceEqual(U("g")));
        Assert.True(quad.Predicate.Lexical.SequenceEqual(U("p")));
    }

    [Fact]
    public void materialise_copies_the_view_into_an_owned_term()
    {
        byte[] text = U("chaten");
        TermArena arena = new();
        int slot = arena.AddLiteral(
            TermSpan.FromText(0, 4),
            TermSpan.None,
            TermSpan.FromText(4, 2),
            TextDirection.LeftToRight);

        RdfTerm term = arena.View(text, slot).Materialise();

        arena.Reset();
        Array.Clear(text);

        Assert.Equal(RdfTerm.Literal(U("chat"), U("en"), TextDirection.LeftToRight), term);
    }

    [Fact]
    public void materialise_folds_an_explicit_xsd_string()
    {
        TermArena arena = new();
        TermSpan lexical = arena.AppendScratch(U("a"));
        TermSpan datatype = arena.AppendScratch(RdfVocabulary.XsdString);
        int slot = arena.AddLiteral(lexical, datatype, TermSpan.None, TextDirection.None);

        RdfTerm term = arena.View([], slot).Materialise();

        Assert.Null(term.Datatype);
        Assert.Equal(RdfTerm.Literal(U("a")), term);
    }

    [Fact]
    public void materialise_rebuilds_a_nested_triple_term()
    {
        byte[] text = U("spo");
        TermArena arena = new();
        int s = arena.AddIri(TermSpan.FromText(0, 1));
        int p = arena.AddIri(TermSpan.FromText(1, 1));
        int o = arena.AddIri(TermSpan.FromText(2, 1));
        int outer = arena.AddTripleTerm(arena.AddTripleTerm(s, p, o), p, o);

        RdfTerm term = arena.View(text, outer).Materialise();

        RdfTerm expected = RdfTerm.TripleTerm(
            RdfTerm.TripleTerm(RdfTerm.Iri(U("s")), RdfTerm.Iri(U("p")), RdfTerm.Iri(U("o"))),
            RdfTerm.Iri(U("p")),
            RdfTerm.Iri(U("o")));

        Assert.Equal(expected, term);
    }

    [Fact]
    public void an_absent_span_is_not_an_empty_one()
    {
        Assert.False(TermSpan.None.IsPresent);
        Assert.True(TermSpan.FromText(0, 0).IsPresent);
        Assert.NotEqual(TermSpan.None, TermSpan.FromText(0, 0));
        Assert.NotEqual(TermSpan.FromText(0, 3), TermSpan.FromScratch(0, 3));
    }
}
