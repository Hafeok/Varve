// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

public class WriterTests
{
    private static string WriteTerm(RdfTerm term, WriteOptions options = default)
    {
        Span<byte> destination = stackalloc byte[512];
        Assert.True(NQuadsWriter.TryWriteTerm(term, destination, out int written, options));
        return Encoding.UTF8.GetString(destination[..written]);
    }

    [Fact]
    public void a_canonical_line_has_one_space_between_terms_and_ends_with_a_line_feed()
    {
        Assert.Equal(
            "<http://a/s> <http://a/p> <http://a/o> .\n",
            Write("<http://a/s>\t<http://a/p>   <http://a/o>  . # comment\n"));
    }

    [Fact]
    public void the_default_write_options_are_canonical_n_triples()
    {
        Assert.True(default(WriteOptions).Canonical);
        Assert.Equal(RdfSyntax.NTriples, default(WriteOptions).Syntax);
    }

    [Fact]
    public void a_graph_label_is_written_only_in_n_quads()
    {
        string document = "<http://a/s> <http://a/p> <http://a/o> <http://a/g> .\n";

        Assert.Equal(document, Write(document, new WriteOptions { Syntax = RdfSyntax.NQuads }));
    }

    [Fact]
    public void canonical_form_escapes_four_characters_and_no_others()
    {
        Assert.Equal(
            "\"a\\\"b\\\\c\\nd\\re\tf\"",
            WriteTerm(RdfTerm.Literal(U("a\"b\\c\nd\re\tf"))));
    }

    [Fact]
    public void canonical_form_writes_a_character_rather_than_an_escape()
    {
        Assert.Equal("\"\u00e9 \U0001F600\"", WriteTerm(RdfTerm.Literal(U("\u00e9 \U0001F600"))));
    }

    [Fact]
    public void non_canonical_form_escapes_non_ascii_with_uppercase_hex()
    {
        Assert.Equal(
            "\"\\u00E9 \\U0001F600\"",
            WriteTerm(RdfTerm.Literal(U("\u00e9 \U0001F600")), new WriteOptions { Canonical = false }));
    }

    [Fact]
    public void a_blank_node_is_written_with_its_prefix()
    {
        Assert.Equal("_:b1", WriteTerm(RdfTerm.BlankNode(U("b1"))));
    }

    [Fact]
    public void an_implied_datatype_is_not_written()
    {
        Assert.Equal("\"a\"", WriteTerm(RdfTerm.Literal(U("a"))));
        Assert.Equal("\"a\"", WriteTerm(RdfTerm.Literal(U("a"), RdfTerm.Iri(RdfVocabulary.XsdString))));
        Assert.Equal("\"a\"@en", WriteTerm(RdfTerm.Literal(U("a"), U("en"))));
    }

    [Fact]
    public void an_explicit_datatype_is_written()
    {
        Assert.Equal(
            "\"1\"^^<http://www.w3.org/2001/XMLSchema#integer>",
            WriteTerm(RdfTerm.Literal(U("1"), RdfTerm.Iri(U("http://www.w3.org/2001/XMLSchema#integer")))));
    }

    [Fact]
    public void a_base_direction_is_written_after_the_language_tag()
    {
        Assert.Equal("\"a\"@ar--rtl", WriteTerm(RdfTerm.Literal(U("a"), U("ar"), TextDirection.RightToLeft)));
        Assert.Equal("\"a\"@en--ltr", WriteTerm(RdfTerm.Literal(U("a"), U("en"), TextDirection.LeftToRight)));
    }

    /// <remarks>
    /// No padding inside the brackets: N-Triples §4 allows whitespace only
    /// between the three terms, and RDF 1.2 has not defined a canonical form
    /// of its own, so the narrower reading is the one that stays comparable.
    /// The reader accepts padding either way.
    /// </remarks>
    [Fact]
    public void a_triple_term_is_written_in_the_rdf_1_2_form()
    {
        Assert.Equal(
            "<<(<http://a/s> <http://a/p> \"o\")>>",
            WriteTerm(RdfTerm.TripleTerm(
                RdfTerm.Iri(U("http://a/s")),
                RdfTerm.Iri(U("http://a/p")),
                RdfTerm.Literal(U("o")))));
    }

    [Fact]
    public void a_padded_triple_term_is_read_and_written_tight()
    {
        Assert.Equal(
            "<http://a/s> <http://a/p> <<(<http://a/s2> <http://a/p2> \"o\")>> .\n",
            Write("<http://a/s> <http://a/p> <<(  <http://a/s2>  <http://a/p2>  \"o\"  )>> .\n"));
    }

    [Fact]
    public void a_destination_that_is_too_small_is_reported_rather_than_truncated()
    {
        Span<byte> tiny = stackalloc byte[4];

        Assert.False(NQuadsWriter.TryWriteTerm(RdfTerm.Iri(U("http://a/s")), tiny, out int written, default));
        Assert.Equal(0, written);
    }

    [Fact]
    public void writing_from_a_quad_source_materialises_its_terms()
    {
        InMemoryDataset dataset = new();
        Quad quad = new(
            dataset.Internalise(RdfTerm.Iri(U("http://a/s"))),
            dataset.Internalise(RdfTerm.Iri(U("http://a/p"))),
            dataset.Internalise(RdfTerm.Literal(U("o"), U("en"))),
            dataset.Internalise(RdfTerm.Iri(U("http://a/g"))));

        ArrayBufferWriter output = new();
        NQuadsWriter.Write(output, in quad, dataset, new WriteOptions { Syntax = RdfSyntax.NQuads });

        Assert.Equal(
            "<http://a/s> <http://a/p> \"o\"@en <http://a/g> .\n",
            Encoding.UTF8.GetString(output.Written));
    }

    [Fact]
    public void writing_a_handle_the_source_cannot_externalise_is_refused()
    {
        InMemoryDataset dataset = new();
        Quad quad = new(new TermHandle(1), new TermHandle(2), new TermHandle(3));

        Assert.Throws<InvalidOperationException>(
            () => NQuadsWriter.Write(new ArrayBufferWriter(), in quad, dataset, default));
    }

    [Fact]
    public void a_term_longer_than_the_first_buffer_still_writes()
    {
        string long_iri = "http://a/" + new string('x', 5000);
        ArrayBufferWriter output = new();

        NQuadsParser.Parse(
            U("<" + long_iri + "> <http://a/p> <http://a/o> .\n"),
            (in QuadView quad) => NQuadsWriter.Write(output, in quad, default),
            default);

        Assert.Equal(
            "<" + long_iri + "> <http://a/p> <http://a/o> .\n",
            Encoding.UTF8.GetString(output.Written));
    }
}
