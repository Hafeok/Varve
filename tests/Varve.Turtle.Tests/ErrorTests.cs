using System.Collections.Generic;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

public class ErrorTests
{
    [Theory]
    [InlineData("<http://a/s> <http://a/p> .\n", ParseErrorKind.ExpectedObject)]
    [InlineData("<http://a/s> <http://a/p> <http://a/o>\n", ParseErrorKind.UnexpectedEnd)]
    [InlineData("<http://a/s> _:p <http://a/o> .\n", ParseErrorKind.ExpectedPredicate)]
    [InlineData("\"s\" <http://a/p> <http://a/o> .\n", ParseErrorKind.ExpectedSubject)]
    [InlineData("<http://a/s <http://a/p> <http://a/o> .\n", ParseErrorKind.InvalidIriCharacter)]
    [InlineData("<http://a/s> <http://a/p> \"abc .\n", ParseErrorKind.UnterminatedLiteral)]
    [InlineData("<http://a/s> <http://a/p> \"a\\zb\" .\n", ParseErrorKind.InvalidEscape)]
    [InlineData("<http://a/s> <http://a/p> \"\\uWXYZ\" .\n", ParseErrorKind.InvalidUnicodeEscape)]
    [InlineData("<http://a/s> <http://a/p> \"\\uD800\" .\n", ParseErrorKind.UnpairedSurrogate)]
    [InlineData("<http://a/s> <http://a/p> \"\\uDC00\" .\n", ParseErrorKind.UnpairedSurrogate)]
    [InlineData("<http://a/s> <http://a/p> \"s\"@1 .\n", ParseErrorKind.InvalidLanguageTag)]
    [InlineData("<http://a/s> <http://a/p> \"s\"@en--up .\n", ParseErrorKind.InvalidBaseDirection)]
    [InlineData("_::a <http://a/p> <http://a/o> .\n", ParseErrorKind.InvalidBlankNodeLabel)]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> . junk\n", ParseErrorKind.TrailingContent)]
    [InlineData("<http://a/s> <http://a/p> <http://a/o>, <http://a/o2> .\n", ParseErrorKind.ExpectedDot)]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/s> <http://a/p> <http://a/o> )> .\n", ParseErrorKind.UnterminatedTripleTerm)]
    public void a_malformed_line_names_what_was_wrong(string document, ParseErrorKind expected)
    {
        (ParseResult result, _) = Parse(document);

        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.FirstError.Kind);
    }

    [Fact]
    public void the_first_error_stops_the_parse_when_there_is_no_handler()
    {
        (ParseResult result, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> .\nbroken\n<http://a/s2> <http://a/p> <http://a/o> .\n");

        Assert.Equal(1, result.QuadCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Single(rows);
    }

    [Fact]
    public void a_handler_that_continues_loses_the_line_and_nothing_else()
    {
        List<ParseError> errors = [];
        ErrorHandler handler = (in ParseError error) =>
        {
            errors.Add(error);
            return ErrorAction.Continue;
        };

        (ParseResult result, List<Row> rows) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> .\nbroken\n<http://a/s2> <http://a/p> <http://a/o> .\n",
            new ParseOptions { OnError = handler });

        Assert.Equal(2, result.QuadCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(2, rows.Count);
        Assert.Single(errors);
    }

    [Fact]
    public void a_rejected_line_produces_no_partial_quad()
    {
        List<Row> rows = [];
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;

        (ParseResult result, rows) = Parse(
            "<http://a/s> <http://a/p> \"unterminated\n<http://a/s2> <http://a/p> <http://a/o> .\n",
            new ParseOptions { OnError = handler });

        Assert.Equal(1, result.QuadCount);
        Assert.Equal(RdfTerm.Iri(U("http://a/s2")), rows[0].Subject);
    }

    [Fact]
    public void an_error_reports_line_column_and_byte_offset()
    {
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;

        (ParseResult result, _) = Parse(
            "<http://a/s> <http://a/p> <http://a/o> .\n<http://a/s> <http://a/p> ?\n",
            new ParseOptions { OnError = handler });

        ParsePosition position = result.FirstError.Position;

        Assert.Equal(2, position.Line);
        Assert.Equal(27, position.Column);
        Assert.Equal(41 + 26, position.ByteOffset);
    }

    [Fact]
    public void a_column_is_counted_in_bytes()
    {
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;

        // Four two-byte characters in the literal, then a bad token. Counted
        // in characters the column would be 34; counted in bytes it is 38.
        (ParseResult result, _) = Parse(
            "<http://a/s> <http://a/p> \"\u00e9\u00e9\u00e9\u00e9\" ?\n",
            new ParseOptions { OnError = handler });

        Assert.Equal(38, result.FirstError.Position.Column);
    }

    [Fact]
    public void the_first_error_is_the_one_reported_even_with_many()
    {
        ErrorHandler handler = static (in ParseError error) => ErrorAction.Continue;

        (ParseResult result, _) = Parse(
            "broken one\nbroken two\nbroken three\n", new ParseOptions { OnError = handler });

        Assert.Equal(3, result.ErrorCount);
        Assert.Equal(1, result.FirstError.Position.Line);
    }

    [Fact]
    public void an_iri_error_carries_the_validator_detail()
    {
        (ParseResult result, _) = Parse("<http:/\\u0000/a> <http://a/p> <http://a/o> .\n");

        Assert.Equal(ParseErrorKind.InvalidIri, result.FirstError.Kind);
        Assert.NotEqual(Varve.Iri.IriErrorKind.None, result.FirstError.Iri);
    }
}
