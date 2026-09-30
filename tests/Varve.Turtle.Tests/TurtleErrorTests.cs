// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Turtle.Model;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// What the reader rejects, and what it does afterwards. The recovery rule is
/// ADR 0030's: the statement is the unit, and a failed statement produces no
/// quads at all — including the ones a blank node property list inside it had
/// already emitted.
/// </summary>
public class TurtleErrorTests
{
    [Theory]
    // Truncation. The scanner cannot know which construct was left open, and
    // says so rather than guessing one.
    [InlineData("<http://a/s> <http://a/p> <http://a/o>", ParseErrorKind.UnexpectedEnd)]
    [InlineData("@prefix p: <http://a/>", ParseErrorKind.UnexpectedEnd)]
    [InlineData("<http://a/s> <http://a/p> \"a", ParseErrorKind.UnexpectedEnd)]
    [InlineData("<http://a/s> <http://a/p> \"\"\"a\"\"", ParseErrorKind.UnexpectedEnd)]
    [InlineData("<http://a/s> <http://a/p> <http://a/o", ParseErrorKind.UnexpectedEnd)]
    [InlineData("<http://a/s> <http://a/p> ( <http://a/1>", ParseErrorKind.UnexpectedEnd)]
    // A term that is not any recognised form names the position it was filling.
    [InlineData("\"a\" <http://a/p> <http://a/o> .", ParseErrorKind.ExpectedSubject)]
    [InlineData("<http://a/s> \"p\" <http://a/o> .", ParseErrorKind.ExpectedPredicate)]
    [InlineData("<http://a/s> <http://a/p> .", ParseErrorKind.ExpectedObject)]
    [InlineData("<http://a/s> ! <http://a/o> .", ParseErrorKind.ExpectedPredicate)]
    // Directives.
    [InlineData("@foo <http://a/> .", ParseErrorKind.UnknownDirective)]
    [InlineData("@BASE <http://a/> .", ParseErrorKind.UnknownDirective)]
    [InlineData("@PREFIX p: <http://a/> .", ParseErrorKind.UnknownDirective)]
    [InlineData("@prefix p <http://a/> .", ParseErrorKind.InvalidPrefix)]
    [InlineData("@prefix <http://a/> .", ParseErrorKind.InvalidPrefix)]
    [InlineData("@prefix ex: .", ParseErrorKind.ExpectedIri)]
    [InlineData("p:s <http://a/p> <http://a/o> .", ParseErrorKind.UndeclaredPrefix)]
    // Terms.
    [InlineData("<http://a/s> <http://a/p> [ <http://a/q> <http://a/r> .",
        ParseErrorKind.UnterminatedBlankNodeList)]
    [InlineData("<http://a/s> <http://a/p> \"a\"@ .", ParseErrorKind.InvalidLanguageTag)]
    [InlineData("<http://a/s> <http://a/p> \"a\"@en- .", ParseErrorKind.InvalidLanguageTag)]
    [InlineData("<http://a/s> <http://a/p> \"a\"@-en .", ParseErrorKind.InvalidLanguageTag)]
    [InlineData("<http://a/s> <http://a/p> \"a\"^^ .", ParseErrorKind.ExpectedIri)]
    [InlineData("<http://a/ b> <http://a/p> <http://a/o> .", ParseErrorKind.InvalidIriCharacter)]
    [InlineData("_: <http://a/p> <http://a/o> .", ParseErrorKind.InvalidBlankNodeLabel)]
    [InlineData("@prefix p: <http://a/> .\np:s p:p p:a%2 .", ParseErrorKind.InvalidPercentEncoding)]
    [InlineData("@prefix p: <http://a/> .\np:s p:p p:a%zz .", ParseErrorKind.InvalidPercentEncoding)]
    public void a_malformed_document_is_rejected_with_a_kind(string document, ParseErrorKind expected)
    {
        Read read = Parse(document);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(expected, read.Result.FirstError.Kind);
    }

    [Theory]
    // A byte that cannot belong to any escape is invalid however close to the
    // end of the input it sits; only an escape that genuinely ran out of bytes
    // is truncation. Deciding that by how much input remains gets the first
    // case wrong, which is what these pin.
    [InlineData("\"\\q\"", ParseErrorKind.InvalidEscape)]
    [InlineData("\"\\uZZZZ\"", ParseErrorKind.InvalidUnicodeEscape)]
    [InlineData("\"\\uAB\"", ParseErrorKind.InvalidUnicodeEscape)]
    [InlineData("\"\\uD800\"", ParseErrorKind.UnpairedSurrogate)]
    [InlineData("\"\\uDC00\"", ParseErrorKind.UnpairedSurrogate)]
    [InlineData("<http://a/\\q>", ParseErrorKind.InvalidEscape)]
    [InlineData("<http://a/\\n>", ParseErrorKind.InvalidEscape)]
    public void a_bad_escape_is_named_and_not_mistaken_for_truncation(
        string term, ParseErrorKind expected)
    {
        Read read = Parse($"<http://a/s> <http://a/p> {term} .");

        Assert.False(read.Result.Succeeded);
        Assert.Equal(expected, read.Result.FirstError.Kind);
    }

    [Fact]
    public void a_bad_escape_reports_its_own_position_not_the_end_of_input()
    {
        Read read = Parse("<http://a/s> <http://a/p> \"\\q\" .");

        Assert.Equal(ParseErrorKind.InvalidEscape, read.Result.FirstError.Kind);
        Assert.True(
            read.Result.FirstError.Position.ByteOffset < 32,
            $"reported at {read.Result.FirstError.Position.ByteOffset}, which is the end of the document");
    }

    [Fact]
    public void a_relative_iri_with_no_base_is_rejected()
    {
        Read read = Parse("<s> <p> <o> .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void a_relative_iri_is_accepted_once_a_base_exists()
    {
        Read read = Parse("@base <http://a/> .\n<s> <p> <o> .");

        Assert.True(read.Result.Succeeded);
    }

    [Fact]
    public void an_error_reports_its_line_and_column()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> .\n\n<http://a/s> ; .");

        Assert.False(read.Result.Succeeded);
        Assert.Equal(3, read.Result.FirstError.Position.Line);
        Assert.Equal(14, read.Result.FirstError.Position.Column);
    }

    [Fact]
    public void a_failed_statement_produces_no_quads_at_all()
    {
        // The property list emits its triple before the statement finishes; the
        // error arrives afterwards, and ADR 0030 says nothing survives.
        Read read = Parse(
            "<http://a/s> <http://a/p> [ <http://a/q> <http://a/r> ] <http://a/bad> .\n"
            + "<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void a_failed_collection_produces_no_quads_at_all()
    {
        Read read = Parse(
            "<http://a/s> <http://a/p> ( <http://a/1> <http://a/2> ) bad .\n"
            + "<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void recovery_resumes_after_the_next_dot_at_depth_zero()
    {
        Read read = Parse(
            "<http://a/s> ! <http://a/o> .\n<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void a_dot_inside_a_string_does_not_end_the_failed_statement()
    {
        Read read = Parse(
            "<http://a/s> ! \"a . b\" <http://a/o> .\n<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void a_dot_inside_an_iri_does_not_end_the_failed_statement()
    {
        Read read = Parse(
            "<http://a/s> ! <http://a/x.y> .\n<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void a_dot_inside_a_blank_node_property_list_does_not_end_the_failed_statement()
    {
        Read read = Parse(
            "! [ <http://a/p> 1.5 ] <http://a/q> <http://a/r> .\n<http://a/s> <http://a/p> <http://a/o> .",
            recover: true);

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void prefixes_bound_before_a_failure_still_stand()
    {
        Read read = Parse(
            "@prefix p: <http://a/> .\n<http://a/s> ! <http://a/o> .\np:s p:p p:o .",
            recover: true);

        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void a_failed_prefix_directive_binds_nothing()
    {
        Read read = Parse("@prefix p: <not a valid iri> .\np:s <http://a/p> <http://a/o> .", recover: true);

        Assert.Empty(read.Rows);
        Assert.Equal(2, read.Result.ErrorCount);
    }

    [Fact]
    public void without_an_error_handler_the_first_error_stops_the_parse()
    {
        Read read = Parse("<http://a/s> ! <http://a/o> .\n<http://a/s> <http://a/p> <http://a/o> .");

        Assert.Equal(1, read.Result.ErrorCount);
        Assert.Empty(read.Rows);
    }

    [Fact]
    public void every_error_reaches_the_handler()
    {
        Read read = Parse(
            "<http://a/s> ! <http://a/o> .\n<http://a/s> ! <http://a/o> .\n<http://a/s> ! <http://a/o> .",
            recover: true);

        Assert.Equal(3, read.Result.ErrorCount);
        Assert.Equal(3, read.Errors.Count);
    }

    [Fact]
    public void an_invalid_iri_is_caught_when_iris_are_validated()
    {
        Read read = Parse("<http://a/ b> <http://a/p> <http://a/o> .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void a_relative_iri_with_no_base_is_rejected_even_when_validation_is_off()
    {
        // Not a validation question: with no base there is nothing to resolve
        // against, so the term cannot be produced at all.
        Read read = Parse("<s> <p> <o> .", validateIris: false);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(ParseErrorKind.RelativeIri, read.Result.FirstError.Kind);
    }

    [Fact]
    public void a_malformed_absolute_iri_passes_when_validation_is_off()
    {
        Read read = Parse("<http://a/%zz> <http://a/p> <http://a/o> .", validateIris: false);

        Assert.True(read.Result.Succeeded);
    }

    [Fact]
    public void the_same_malformed_iri_is_caught_when_validation_is_on()
    {
        Read read = Parse("<http://a/%zz> <http://a/p> <http://a/o> .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void a_language_tag_must_be_well_formed()
    {
        Read read = Parse("<http://a/s> <http://a/p> \"a\"@cantbethislong .");

        Assert.False(read.Result.Succeeded);
        Assert.Equal(ParseErrorKind.InvalidLanguageTag, read.Result.FirstError.Kind);
    }

    [Fact]
    public void rdf_lang_string_may_not_be_written_as_a_datatype()
    {
        Read read = Parse(
            "@prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .\n"
            + "<http://a/s> <http://a/p> \"a\"^^rdf:langString .");

        Assert.False(read.Result.Succeeded);
        Assert.Equal(ParseErrorKind.DatatypeRequiresLanguage, read.Result.FirstError.Kind);
    }
}
