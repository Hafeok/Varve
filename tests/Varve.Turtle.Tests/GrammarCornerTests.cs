// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// The productions the suites caught this reader missing, pinned here so that
/// each is a named test rather than only a line in a manifest.
/// </summary>
public class GrammarCornerTests
{
    [Theory]
    // [167s] PN_PREFIX ::= PN_CHARS_BASE ((PN_CHARS | '.')* PN_CHARS)?
    [InlineData("e.g")]
    [InlineData("a.b.c")]
    [InlineData("a..b")]
    // [164s] PN_CHARS adds U+00B7, U+0300–U+036F, U+203F–U+2040 — none of them
    // allowed to lead, all allowed after.
    [InlineData("a·b")]
    [InlineData("à")]
    [InlineData("aͯ")]
    [InlineData("a‿")]
    [InlineData("a⁀")]
    [InlineData("a·̀ͯ‿.⁀")]
    public void a_prefix_name_may_carry_dots_and_the_non_leading_extras(string prefix)
    {
        Read read = Parse($"@prefix {prefix}: <http://a/> .\n{prefix}:s {prefix}:p {prefix}:o .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Theory]
    [InlineData(".b")]
    [InlineData("-b")]
    [InlineData("·b")]
    [InlineData("̀b")]
    public void a_prefix_name_may_not_lead_with_one(string prefix)
    {
        // PN_CHARS_BASE excludes all of them, and the first character must be
        // one, so these are the negative half of the same rule.
        Read read = Parse($"@prefix {prefix}: <http://a/> .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void a_prefix_name_may_not_end_with_a_dot()
    {
        Read read = Parse("@prefix ab.: <http://a/> .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void a_dot_that_ends_a_statement_is_not_part_of_a_prefix_name()
    {
        // The other side of allowing dots: "<s> <p> ." must still report a
        // missing object rather than scanning the terminator into a name.
        Read read = Parse("<http://a/s> <http://a/p> .\n");

        Assert.False(read.Result.Succeeded);
        Assert.Equal(ParseErrorKind.ExpectedObject, read.Result.FirstError.Kind);
    }

    [Theory]
    // [21] DOUBLE ::= [+-]? ([0-9]+ '.' [0-9]* EXPONENT | '.' [0-9]+ EXPONENT | [0-9]+ EXPONENT)
    [InlineData("123.E+1", "123.E+1")]
    [InlineData("1.e0", "1.e0")]
    [InlineData("-1.E-3", "-1.E-3")]
    [InlineData(".5e1", ".5e1")]
    [InlineData("1e0", "1e0")]
    public void a_double_needs_no_fractional_digits(string written, string lexical)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {written} .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            $"<http://a/s> <http://a/p> \"{lexical}\"^^<http://www.w3.org/2001/XMLSchema#double>",
            read.Lines()[0]);
    }

    [Fact]
    public void a_dot_with_no_exponent_after_it_still_ends_the_statement()
    {
        // "1." is the integer 1 and a terminator, not a DOUBLE missing its
        // exponent. The dot becomes the number's only when something claims it.
        Read read = Parse("<http://a/s> <http://a/p> 1.");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(
            "<http://a/s> <http://a/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>",
            read.Lines()[0]);
    }

    [Theory]
    [InlineData(".e1")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData(".")]
    public void a_number_with_no_digits_at_all_is_rejected(string written)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {written} .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void an_anonymous_blank_node_may_label_a_trig_graph()
    {
        // [7g] labelOrSubject ::= iri | BlankNode, and BlankNode includes ANON.
        Read read = ParseTriG("[] {<http://a/s> <http://a/p> <http://a/o> .}");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Single(read.Rows);
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Graph!.Kind);
    }

    [Fact]
    public void the_graph_keyword_accepts_an_anonymous_blank_node_too()
    {
        Read read = ParseTriG("GRAPH [] {<http://a/s> <http://a/p> <http://a/o>}");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Graph!.Kind);
    }

    [Fact]
    public void an_anonymous_blank_node_is_still_a_subject_when_no_brace_follows()
    {
        Read read = ParseTriG("[] <http://a/p> <http://a/o> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Single(read.Rows);
        Assert.Null(read.Rows[0].Graph);
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Subject.Kind);
    }

    [Fact]
    public void two_anonymous_graph_labels_are_two_graphs()
    {
        Read read = ParseTriG(
            "[] {<http://a/s> <http://a/p> <http://a/o> .}\n[] {<http://a/s> <http://a/p> <http://a/o> .}");

        Assert.Equal(2, read.Rows.Count);
        Assert.NotEqual(read.Rows[0].Graph, read.Rows[1].Graph);
    }

    [Theory]
    // [6g] triplesBlock ::= triples ('.' triplesBlock?)? — the last '.' is
    // optional, so a sole property list may be closed by the brace.
    [InlineData("{[ <http://a/p> <http://a/o> ] .}")]
    [InlineData("{[ <http://a/p> <http://a/o> ]}")]
    [InlineData("<http://a/g> { [ <http://a/p> <http://a/o> ] .}")]
    [InlineData("<http://a/g> { [ <http://a/p> <http://a/o> ] }")]
    public void a_sole_blank_node_property_list_is_a_whole_statement_in_a_graph(string document)
    {
        Read read = ParseTriG(document);

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Single(read.Rows);
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Subject.Kind);
    }

    [Fact]
    public void a_sole_blank_node_property_list_still_needs_its_dot_outside_a_graph()
    {
        Read read = Parse("[ <http://a/p> <http://a/o> ]");

        Assert.False(read.Result.Succeeded);
    }
}
