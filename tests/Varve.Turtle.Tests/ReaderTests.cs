using System.Collections.Generic;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

public class ReaderTests
{
    [Fact]
    public void a_triple_of_iris_reads()
    {
        (ParseResult result, List<Row> rows) =
            Parse("<http://a/s> <http://a/p> <http://a/o> .\n");

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.QuadCount);
        Assert.Equal(RdfTerm.Iri(U("http://a/s")), rows[0].Subject);
        Assert.Equal(RdfTerm.Iri(U("http://a/p")), rows[0].Predicate);
        Assert.Equal(RdfTerm.Iri(U("http://a/o")), rows[0].Object);
        Assert.Null(rows[0].Graph);
    }

    [Fact]
    public void an_empty_document_succeeds_and_yields_nothing()
    {
        (ParseResult result, List<Row> rows) = Parse("");

        Assert.True(result.Succeeded);
        Assert.Empty(rows);
    }

    [Fact]
    public void blank_lines_and_comments_are_not_quads()
    {
        (ParseResult result, List<Row> rows) = Parse(
            "# a comment\n\n<http://a/s> <http://a/p> <http://a/o> . # trailing\n\n# another\n");

        Assert.True(result.Succeeded);
        Assert.Single(rows);
    }

    [Fact]
    public void a_hash_inside_an_iri_is_not_a_comment()
    {
        (_, List<Row> rows) = Parse("<http://a/s#f> <http://a/p> \"a#b\" .\n");

        Assert.Equal(RdfTerm.Iri(U("http://a/s#f")), rows[0].Subject);
        Assert.Equal(RdfTerm.Literal(U("a#b")), rows[0].Object);
    }

    [Fact]
    public void a_document_without_a_final_newline_still_reads()
    {
        (ParseResult result, List<Row> rows) = Parse("<http://a/s> <http://a/p> <http://a/o> .");

        Assert.True(result.Succeeded);
        Assert.Single(rows);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\n\r")]
    public void every_end_of_line_form_separates_statements(string eol)
    {
        (ParseResult result, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> ." + eol + "<http://a/s2> <http://a/p> <http://a/o> ." + eol);

        Assert.True(result.Succeeded);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void blank_nodes_read_without_their_prefix()
    {
        (_, List<Row> rows) = Parse("_:b1 <http://a/p> _:b.2 .\n");

        Assert.Equal(RdfTerm.BlankNode(U("b1")), rows[0].Subject);
        Assert.Equal(RdfTerm.BlankNode(U("b.2")), rows[0].Object);
    }

    [Fact]
    public void a_blank_node_label_does_not_swallow_the_terminating_dot()
    {
        (ParseResult result, List<Row> rows) = Parse("<http://a/s> <http://a/p> _:b.\n");

        Assert.True(result.Succeeded);
        Assert.Equal(RdfTerm.BlankNode(U("b")), rows[0].Object);
    }

    [Fact]
    public void a_plain_literal_has_no_datatype()
    {
        (_, List<Row> rows) = Parse("<http://a/s> <http://a/p> \"chat\" .\n");

        Assert.Equal(RdfTerm.Literal(U("chat")), rows[0].Object);
        Assert.Null(rows[0].Object.Datatype);
    }

    [Fact]
    public void a_typed_literal_carries_its_datatype()
    {
        (_, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n");

        Assert.Equal(
            RdfTerm.Literal(U("1"), RdfTerm.Iri(U("http://www.w3.org/2001/XMLSchema#integer"))),
            rows[0].Object);
    }

    [Fact]
    public void an_explicit_xsd_string_reads_as_the_shorthand()
    {
        (_, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> \"a\"^^<http://www.w3.org/2001/XMLSchema#string> .\n");

        Assert.Equal(RdfTerm.Literal(U("a")), rows[0].Object);
    }

    [Fact]
    public void a_language_tag_reads_with_its_subtags()
    {
        (_, List<Row> rows) = Parse("<http://a/s> <http://a/p> \"chat\"@en-GB-oed .\n");

        Assert.Equal(RdfTerm.Literal(U("chat"), U("en-GB-oed")), rows[0].Object);
    }

    [Fact]
    public void a_base_direction_reads_as_rdf_1_2_writes_it()
    {
        (_, List<Row> rows) = Parse("<http://a/s> <http://a/p> \"chat\"@ar--rtl .\n");

        Assert.Equal(RdfTerm.Literal(U("chat"), U("ar"), TextDirection.RightToLeft), rows[0].Object);
    }

    [Fact]
    public void string_escapes_are_resolved()
    {
        (_, List<Row> rows) = Parse("<http://a/s> <http://a/p> \"a\\tb\\nc\\\\d\\\"e\" .\n");

        Assert.Equal(RdfTerm.Literal(U("a\tb\nc\\d\"e")), rows[0].Object);
    }

    [Fact]
    public void unicode_escapes_are_resolved_including_surrogate_pairs()
    {
        (_, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> \"\\u00E9 \\U0001F600 \\uD83D\\uDE00\" .\n");

        Assert.Equal(RdfTerm.Literal(U("é \U0001F600 \U0001F600")), rows[0].Object);
    }

    [Fact]
    public void escapes_in_an_iri_are_resolved_before_it_is_validated()
    {
        (ParseResult result, _) = Parse("<http://a/\\u0020> <http://a/p> <http://a/o> .\n");

        Assert.False(result.Succeeded);
        Assert.Equal(ParseErrorKind.InvalidIri, result.FirstError.Kind);
    }

    [Fact]
    public void a_resolved_escape_that_is_legal_gives_the_character()
    {
        (_, List<Row> rows) = Parse("<http://a/\\u00E9> <http://a/p> <http://a/o> .\n");

        Assert.Equal(RdfTerm.Iri(U("http://a/é")), rows[0].Subject);
    }

    [Fact]
    public void a_relative_iri_is_rejected()
    {
        (ParseResult result, _) = Parse("<s> <http://a/p> <http://a/o> .\n");

        Assert.Equal(ParseErrorKind.RelativeIri, result.FirstError.Kind);
    }

    [Fact]
    public void iri_validation_can_be_turned_off()
    {
        (ParseResult result, List<Row> rows) = Parse(
            "<s> <http://a/p> <http://a/o> .\n", new ParseOptions { ValidateIris = false });

        Assert.True(result.Succeeded);
        Assert.Equal(RdfTerm.Iri(U("s")), rows[0].Subject);
    }

    [Fact]
    public void the_default_options_validate_iris()
    {
        Assert.True(default(ParseOptions).ValidateIris);
        Assert.Equal(RdfSyntax.NTriples, default(ParseOptions).Syntax);
        Assert.Null(default(ParseOptions).OnError);
    }

    [Fact]
    public void a_graph_label_is_an_error_in_n_triples_and_a_quad_in_n_quads()
    {
        string document = "<http://a/s> <http://a/p> <http://a/o> <http://a/g> .\n";

        (ParseResult asTriples, _) = Parse(document);
        Assert.Equal(ParseErrorKind.GraphLabelNotAllowed, asTriples.FirstError.Kind);

        (ParseResult asQuads, List<Row> rows) = Parse(document, new ParseOptions { Syntax = RdfSyntax.NQuads });
        Assert.True(asQuads.Succeeded);
        Assert.Equal(RdfTerm.Iri(U("http://a/g")), rows[0].Graph);
    }

    [Fact]
    public void an_n_quads_statement_without_a_graph_is_in_the_default_graph()
    {
        (_, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> .\n", new ParseOptions { Syntax = RdfSyntax.NQuads });

        Assert.Null(rows[0].Graph);
    }

    [Fact]
    public void a_triple_term_reads_in_the_object_position()
    {
        (ParseResult result, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> <<( <http://a/s2> <http://a/p2> \"o\" )>> .\n");

        Assert.True(result.Succeeded);
        Assert.Equal(
            RdfTerm.TripleTerm(
                RdfTerm.Iri(U("http://a/s2")),
                RdfTerm.Iri(U("http://a/p2")),
                RdfTerm.Literal(U("o"))),
            rows[0].Object);
    }

    [Fact]
    public void a_triple_term_is_not_a_subject_and_not_a_graph_label()
    {
        (ParseResult asSubject, _) = Parse("<<( <http://a/s> <http://a/p> <http://a/o> )>> <http://a/p> <http://a/o> .\n");
        Assert.Equal(ParseErrorKind.ExpectedSubject, asSubject.FirstError.Kind);

        (ParseResult asGraph, _) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> <<( <http://a/s> <http://a/p> <http://a/o> )>> .\n",
            new ParseOptions { Syntax = RdfSyntax.NQuads });
        Assert.Equal(ParseErrorKind.ExpectedGraphLabel, asGraph.FirstError.Kind);
    }
}
