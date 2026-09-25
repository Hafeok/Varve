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

    /// <remarks>
    /// RDF 1.2 N-Triples §3's set (ADR 0061): BS, HT, LF, FF, CR, <c>"</c> and
    /// <c>\</c> as <c>ECHAR</c>. RDF 1.1's was the last four only, and wrote a
    /// tab raw.
    /// </remarks>
    [Fact]
    public void canonical_form_escapes_seven_characters_as_echar()
    {
        Assert.Equal(
            "\"a\\\"b\\\\c\\nd\\re\\tf\\bg\\fh\"",
            WriteTerm(RdfTerm.Literal(U("a\"b\\c\nd\re\tf\bg\fh"))));
    }

    [Fact]
    public void canonical_form_escapes_other_controls_del_and_two_noncharacters_as_uchar()
    {
        Assert.Equal(
            "\"\\u0000\\u000B\\u001F\\u007F\\uFFFE\\uFFFF\"",
            WriteTerm(RdfTerm.Literal(U("\u0000\u000B\u001F\u007F\uFFFE\uFFFF"))));
    }

    [Fact]
    public void canonical_form_lowercases_a_language_tag_and_keeps_the_direction()
    {
        Assert.Equal("\"chat\"@en-gb--ltr", WriteTerm(RdfTerm.Literal(U("chat"), U("EN-GB"), TextDirection.LeftToRight)));
    }

    [Fact]
    public void non_canonical_form_keeps_a_language_tag_as_held()
    {
        Assert.Equal("\"chat\"@EN-GB", WriteTerm(RdfTerm.Literal(U("chat"), U("EN-GB")), new WriteOptions { Canonical = false }));
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
    /// One space inside each bracket, as RDF 1.2 N-Triples §3's canonical form
    /// and its c14n test cases write it (ADR 0061). Written tight until the
    /// 1.2 form existed to follow. The reader accepts padding either way.
    /// </remarks>
    [Fact]
    public void a_triple_term_is_written_in_the_rdf_1_2_form()
    {
        Assert.Equal(
            "<<( <http://a/s> <http://a/p> \"o\" )>>",
            WriteTerm(RdfTerm.TripleTerm(
                RdfTerm.Iri(U("http://a/s")),
                RdfTerm.Iri(U("http://a/p")),
                RdfTerm.Literal(U("o")))));
    }

    [Fact]
    public void a_triple_term_is_read_with_any_padding_and_written_canonically()
    {
        Assert.Equal(
            "<http://a/s> <http://a/p> <<( <http://a/s2> <http://a/p2> \"o\" )>> .\n",
            Write("<http://a/s> <http://a/p> <<(  <http://a/s2>  <http://a/p2>  \"o\"  )>> .\n"));
    }

    /// <remarks>
    /// <c>'^^'</c> and <c>LANG_DIR</c> are terminals, and whitespace may
    /// separate terminals. The line parser once read the suffix only directly
    /// after the closing quote; RDF 1.2's c14n suite (extra_whitespace-03 and
    /// -04) found it.
    /// </remarks>
    [Theory]
    [InlineData("<http://a/s> <http://a/p> \"Alice\" @EN .\n", "<http://a/s> <http://a/p> \"Alice\"@en .\n")]
    [InlineData("<http://a/s> <http://a/p> \"2\"  ^^  <http://www.w3.org/2001/XMLSchema#integer>  .\n", "<http://a/s> <http://a/p> \"2\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n")]
    [InlineData("<http://a/s> <http://a/p> \"x\"   .\n", "<http://a/s> <http://a/p> \"x\" .\n")]
    public void whitespace_before_a_literals_suffix_is_read_and_written_canonically(string document, string canonical)
    {
        Assert.Equal(canonical, Write(document));
    }

    [Fact]
    public void a_datatype_marker_with_nothing_after_it_is_rejected()
    {
        Assert.False(Parse("<http://a/s> <http://a/p> \"x\" ^^ .\n").Result.Succeeded);
    }

    [Fact]
    public void canonical_form_writes_an_explicit_xsd_string_as_a_simple_literal()
    {
        Assert.Equal(
            "<http://a/s> <http://a/p> \"foo\" .\n",
            Write("<http://a/s> <http://a/p> \"foo\"^^<http://www.w3.org/2001/XMLSchema#string> .\n"));
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
