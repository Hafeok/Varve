using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// The Turtle grammar, one production at a time. The W3C suites judge whether
/// this agrees with the specification; these judge whether a change broke
/// something, and say which production when it did.
/// </summary>
public class TurtleReaderTests
{
    [Fact]
    public void a_triple_of_iris_reads()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void an_empty_document_yields_nothing()
    {
        Read read = Parse("");

        Assert.True(read.Result.Succeeded);
        Assert.Empty(read.Rows);
    }

    [Fact]
    public void a_document_of_comments_and_whitespace_yields_nothing()
    {
        Read read = Parse("# one\n\n   \t\n# two\n");

        Assert.True(read.Result.Succeeded);
        Assert.Empty(read.Rows);
    }

    [Fact]
    public void a_prefix_is_declared_and_expands()
    {
        Read read = Parse("@prefix p: <http://a/> .\np:s p:p p:o .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal([new Declared("p", "http://a/")], read.Prefixes);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void the_sparql_prefix_form_takes_no_dot()
    {
        Read read = Parse("PREFIX p: <http://a/>\np:s p:p p:o .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void the_empty_prefix_is_a_prefix()
    {
        Read read = Parse("@prefix : <http://a/> .\n:s :p :o .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal([new Declared("", "http://a/")], read.Prefixes);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void a_prefix_may_be_redeclared_and_both_bindings_are_reported()
    {
        Read read = Parse(
            "@prefix p: <http://a/> .\np:s p:p p:o .\n@prefix p: <http://b/> .\np:s p:p p:o .");

        Assert.Equal([new Declared("p", "http://a/"), new Declared("p", "http://b/")], read.Prefixes);
        Assert.Equal(
            ["<http://a/s> <http://a/p> <http://a/o>", "<http://b/s> <http://b/p> <http://b/o>"],
            read.Lines());
    }

    [Fact]
    public void a_local_name_may_be_empty()
    {
        Read read = Parse("@prefix p: <http://a/s> .\np: <http://a/p> <http://a/o> .");

        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void a_local_name_may_contain_an_escaped_delimiter()
    {
        Read read = Parse("@prefix p: <http://a/> .\np:a\\.b p:c\\#d p:e\\~f .");

        Assert.Equal(["<http://a/a.b> <http://a/c#d> <http://a/e~f>"], read.Lines());
    }

    [Fact]
    public void a_local_name_may_contain_a_colon_and_a_percent()
    {
        Read read = Parse("@prefix p: <http://a/> .\np:a:b p:c%20d p:e .");

        Assert.Equal(["<http://a/a:b> <http://a/c%20d> <http://a/e>"], read.Lines());
    }

    [Fact]
    public void base_resolves_a_relative_iri()
    {
        Read read = Parse("@base <http://a/x/> .\n<s> <p> <o> .");

        Assert.Equal(["http://a/x/"], read.Bases);
        Assert.Equal(["<http://a/x/s> <http://a/x/p> <http://a/x/o>"], read.Lines());
    }

    [Fact]
    public void the_sparql_base_form_takes_no_dot()
    {
        Read read = Parse("BASE <http://a/x/>\n<s> <p> <o> .");

        Assert.Equal(["<http://a/x/s> <http://a/x/p> <http://a/x/o>"], read.Lines());
    }

    [Fact]
    public void a_later_base_is_resolved_against_the_earlier_one()
    {
        Read read = Parse("@base <http://a/x/> .\n@base <y/> .\n<s> <p> <o> .");

        Assert.Equal(["http://a/x/", "http://a/x/y/"], read.Bases);
        Assert.Equal(["<http://a/x/y/s> <http://a/x/y/p> <http://a/x/y/o>"], read.Lines());
    }

    [Fact]
    public void the_documents_retrieval_iri_is_the_initial_base()
    {
        Read read = Parse("<s> <p> <o> .", baseIri: "http://a/x/d.ttl");

        Assert.Equal(["<http://a/x/s> <http://a/x/p> <http://a/x/o>"], read.Lines());
    }

    [Fact]
    public void a_prefix_iri_is_resolved_against_the_base()
    {
        Read read = Parse("@base <http://a/x/> .\n@prefix p: <y/> .\np:s p:p p:o .");

        Assert.Equal(["<http://a/x/y/s> <http://a/x/y/p> <http://a/x/y/o>"], read.Lines());
    }

    [Fact]
    public void a_predicate_object_list_repeats_the_subject()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> ; <http://a/q> <http://a/r> .");

        Assert.Equal(
            ["<http://a/s> <http://a/p> <http://a/o>", "<http://a/s> <http://a/q> <http://a/r>"],
            read.Lines());
    }

    [Fact]
    public void an_object_list_repeats_the_subject_and_predicate()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o>, <http://a/r> .");

        Assert.Equal(
            ["<http://a/s> <http://a/p> <http://a/o>", "<http://a/s> <http://a/p> <http://a/r>"],
            read.Lines());
    }

    [Fact]
    public void a_trailing_semicolon_is_allowed_and_repeated()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> ;; ; .");

        Assert.True(read.Result.Succeeded);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void a_is_rdf_type()
    {
        Read read = Parse("<http://a/s> a <http://a/C> .");

        Assert.Equal(
            ["<http://a/s> <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://a/C>"],
            read.Lines());
    }

    [Fact]
    public void a_labelled_blank_node_is_one_node_across_statements()
    {
        Read read = Parse("_:x <http://a/p> <http://a/o> .\n<http://a/s> <http://a/q> _:x .");

        string label = read.Rows[0].Subject.Lexical.ToString() is var _ ? Harness.S(read.Rows[0].Subject.Lexical) : "";
        Assert.Equal(label, Harness.S(read.Rows[1].Object.Lexical));
    }

    [Fact]
    public void an_anonymous_blank_node_is_a_subject()
    {
        Read read = Parse("[] <http://a/p> <http://a/o> .");

        Assert.Single(read.Rows);
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Subject.Kind);
    }

    [Fact]
    public void a_blank_node_property_list_emits_its_own_triples_first()
    {
        Read read = Parse("<http://a/s> <http://a/p> [ <http://a/q> <http://a/r> ] .");

        Assert.Equal(2, read.Rows.Count);
        Assert.Equal("<http://a/q>", read.Lines()[0].Split(' ')[1]);
        Assert.Equal(read.Rows[0].Subject, read.Rows[1].Object);
    }

    [Fact]
    public void a_blank_node_property_list_is_a_subject_and_keeps_its_predicates()
    {
        Read read = Parse("[ <http://a/p> <http://a/o> ] <http://a/q> <http://a/r> .");

        Assert.Equal(2, read.Rows.Count);
        Assert.Equal(read.Rows[0].Subject, read.Rows[1].Subject);
    }

    [Fact]
    public void nested_blank_node_property_lists_nest()
    {
        Read read = Parse("<http://a/s> <http://a/p> [ <http://a/q> [ <http://a/r> <http://a/t> ] ] .");

        Assert.Equal(3, read.Rows.Count);
    }

    [Fact]
    public void an_empty_collection_is_rdf_nil()
    {
        Read read = Parse("<http://a/s> <http://a/p> () .");

        Assert.Equal(
            ["<http://a/s> <http://a/p> <http://www.w3.org/1999/02/22-rdf-syntax-ns#nil>"],
            read.Lines());
    }

    [Fact]
    public void a_collection_of_two_is_four_triples_and_a_nil_tail()
    {
        Read read = Parse("<http://a/s> <http://a/p> ( <http://a/1> <http://a/2> ) .");

        // two rdf:first, two rdf:rest, plus the statement itself.
        Assert.Equal(5, read.Rows.Count);
        Assert.EndsWith("22-rdf-syntax-ns#nil>", read.Lines()[3], System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_collection_may_be_a_subject()
    {
        Read read = Parse("( <http://a/1> ) <http://a/p> <http://a/o> .");

        Assert.Equal(3, read.Rows.Count);
    }

    [Fact]
    public void a_collection_may_contain_a_blank_node_property_list()
    {
        Read read = Parse("<http://a/s> <http://a/p> ( [ <http://a/q> <http://a/r> ] ) .");

        Assert.Equal(4, read.Rows.Count);
    }

    [Theory]
    [InlineData("\"a\"", "\"a\"")]
    [InlineData("'a'", "\"a\"")]
    [InlineData("\"\"\"a\"\"\"", "\"a\"")]
    [InlineData("'''a'''", "\"a\"")]
    [InlineData("\"\"\"a\"b\"\"\"", "\"a\"b\"")]
    [InlineData("\"\"\"a\"\"b\"\"\"", "\"a\"\"b\"")]
    [InlineData("'''a\nb'''", "\"a\nb\"")]
    [InlineData("\"\"", "\"\"")]
    [InlineData("''''''", "\"\"")]
    [InlineData("\"a\\nb\"", "\"a\nb\"")]
    [InlineData("\"a\\u0062c\"", "\"abc\"")]
    [InlineData("\"a\\U0001F600b\"", "\"a\U0001F600b\"")]
    [InlineData("\"\\\"\"", "\"\"\"")]
    public void a_string_reads(string written, string expected)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {written} .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal($"<http://a/s> <http://a/p> {expected}", read.Lines()[0]);
    }

    [Fact]
    public void a_language_tag_reads()
    {
        Read read = Parse("<http://a/s> <http://a/p> \"a\"@en-GB .");

        Assert.Equal(["<http://a/s> <http://a/p> \"a\"@en-GB"], read.Lines());
    }

    [Fact]
    public void an_explicit_datatype_reads()
    {
        Read read = Parse("@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n"
            + "<http://a/s> <http://a/p> \"1\"^^xsd:integer .");

        Assert.Equal(
            ["<http://a/s> <http://a/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>"],
            read.Lines());
    }

    [Theory]
    [InlineData("1", "1", "integer")]
    [InlineData("-1", "-1", "integer")]
    [InlineData("+1", "+1", "integer")]
    [InlineData("1.0", "1.0", "decimal")]
    [InlineData("-1.0", "-1.0", "decimal")]
    [InlineData(".5", ".5", "decimal")]
    [InlineData("1e0", "1e0", "double")]
    [InlineData("1.0e0", "1.0e0", "double")]
    [InlineData("-1.0E-3", "-1.0E-3", "double")]
    [InlineData(".5e1", ".5e1", "double")]
    public void a_number_keeps_its_lexical_form_and_gets_its_datatype(
        string written, string lexical, string datatype)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {written} .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(
            $"<http://a/s> <http://a/p> \"{lexical}\"^^<http://www.w3.org/2001/XMLSchema#{datatype}>",
            read.Lines()[0]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void a_boolean_reads(string written)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {written} .");

        Assert.Equal(
            $"<http://a/s> <http://a/p> \"{written}\"^^<http://www.w3.org/2001/XMLSchema#boolean>",
            read.Lines()[0]);
    }

    [Fact]
    public void an_integer_ending_a_statement_is_not_a_decimal()
    {
        // "1." is an integer then the statement's dot, not the decimal "1.".
        Read read = Parse("<http://a/s> <http://a/p> 1.");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(
            "<http://a/s> <http://a/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>",
            read.Lines()[0]);
    }

    [Fact]
    public void a_statement_may_span_lines()
    {
        Read read = Parse("<http://a/s>\n  <http://a/p>\n    <http://a/o>\n.\n");

        Assert.Single(read.Rows);
    }
}
