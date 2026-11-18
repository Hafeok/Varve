// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Text;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>The tokenizer's kinds, positions and errors (<c>docs/spec/sparql-grammar.md</c> §10, ADR 0248).</summary>
public class TokenizerTests
{
    private static SparqlTokenKind[] Kinds(string text) =>
        SparqlTokenizer.Tokenize(text.AsSpan()).Tokens.Select(token => token.Kind).ToArray();

    private static string[] Texts(string text) =>
        SparqlTokenizer.Tokenize(text.AsSpan()).Tokens.Select(token => text[(int)token.Start..(int)token.End]).ToArray();

    [Fact]
    public void A_query_is_its_terminals_with_comments_and_without_whitespace()
    {
        const string Query = "SELECT ?s # who\nWHERE { ?s a <urn:x> ; ex:p \"v\"@en , 1.5 } LIMIT 10";

        Assert.Equal(
            ["SELECT", "?s", "# who", "WHERE", "{", "?s", "a", "<urn:x>", ";", "ex:p", "\"v\"", "@en", ",", "1.5", "}", "LIMIT", "10"],
            Texts(Query));
        Assert.Equal(
            [
                SparqlTokenKind.Keyword, SparqlTokenKind.Variable, SparqlTokenKind.Comment, SparqlTokenKind.Keyword,
                SparqlTokenKind.OpenBrace, SparqlTokenKind.Variable, SparqlTokenKind.Keyword, SparqlTokenKind.Iri,
                SparqlTokenKind.Punctuation, SparqlTokenKind.PrefixedName, SparqlTokenKind.String, SparqlTokenKind.LangTag,
                SparqlTokenKind.Punctuation, SparqlTokenKind.Decimal, SparqlTokenKind.CloseBrace, SparqlTokenKind.Keyword,
                SparqlTokenKind.Integer,
            ],
            Kinds(Query));
    }

    [Theory]
    [InlineData("select", SparqlTokenKind.Keyword)]
    [InlineData("Select", SparqlTokenKind.Keyword)]
    [InlineData("strlen", SparqlTokenKind.Keyword)]
    [InlineData("encode_for_uri", SparqlTokenKind.Keyword)]
    [InlineData("a", SparqlTokenKind.Keyword)]
    [InlineData("A", SparqlTokenKind.Word)]
    [InlineData("true", SparqlTokenKind.Boolean)]
    [InlineData("false", SparqlTokenKind.Boolean)]
    [InlineData("TRUE", SparqlTokenKind.Word)]
    [InlineData("selection", SparqlTokenKind.Word)]
    public void A_word_is_classified_by_itself(string word, SparqlTokenKind kind) =>
        Assert.Equal([kind], Kinds(word));

    [Fact]
    public void Brackets_keep_their_kinds_and_the_rdf_12_brackets_theirs()
    {
        Assert.Equal(
            [
                SparqlTokenKind.OpenParen, SparqlTokenKind.CloseParen, SparqlTokenKind.OpenBracket, SparqlTokenKind.CloseBracket,
                SparqlTokenKind.Nil, SparqlTokenKind.Anon, SparqlTokenKind.TripleTermOpen, SparqlTokenKind.TripleTermClose,
                SparqlTokenKind.AnnotationOpen, SparqlTokenKind.AnnotationClose, SparqlTokenKind.ReifiedOpen, SparqlTokenKind.ReifiedClose,
            ],
            Kinds("( ?x ) [ ?p ?o ] () [] <<( ?s ?p ?o )>> {| ?a ?b |} << ?s ?p ?o >>").Where(kind => kind != SparqlTokenKind.Variable));
    }

    [Fact]
    public void Operators_are_one_kind() =>
        Assert.All(Kinds("! != = < > <= >= && || ^ | / * + - ? ~"), kind => Assert.Equal(SparqlTokenKind.Operator, kind));

    [Fact]
    public void A_comment_ends_before_its_line_break()
    {
        SparqlTokens tokens = SparqlTokenizer.Tokenize("# one\r\n# two\n".AsSpan());

        Assert.Equal([new(SparqlTokenKind.Comment, 0, 5), new(SparqlTokenKind.Comment, 7, 12)], tokens.Tokens);
        Assert.True(tokens.IsComplete);
    }

    [Fact]
    public void Utf16_positions_are_code_units_and_utf8_positions_bytes()
    {
        const string Query = "\"é😀\" ?x";

        Assert.Equal(
            [new(SparqlTokenKind.String, 0, 5), new(SparqlTokenKind.Variable, 6, 8)],
            SparqlTokenizer.Tokenize(Query.AsSpan()).Tokens);
        Assert.Equal(
            [new(SparqlTokenKind.String, 0, 8), new(SparqlTokenKind.Variable, 9, 11)],
            SparqlTokenizer.Tokenize(Encoding.UTF8.GetBytes(Query)).Tokens);
    }

    [Fact]
    public void The_first_ill_formed_terminal_ends_the_tokens_with_the_parsers_error()
    {
        const string Query = "SELECT * WHERE { ?s ?p ?o FILTER(?o & 1) }";

        SparqlTokens tokens = SparqlTokenizer.Tokenize(Query.AsSpan());
        Assert.False(SparqlParser.TryParseQuery(Query.AsSpan(), default, out _, out SparqlParseError parsed));

        Assert.False(tokens.IsComplete);
        Assert.Equal(parsed, tokens.Error);
        Assert.Equal(["SELECT", "*", "WHERE", "{", "?s", "?p", "?o", "FILTER", "(", "?o"], Texts(Query));
    }

    [Fact]
    public void A_lone_surrogate_ends_the_tokens_with_the_parsers_encoding_error()
    {
        string query = "SELECT ?x \"a\ud800b\"";

        SparqlTokens tokens = SparqlTokenizer.Tokenize(query.AsSpan());
        Assert.False(SparqlParser.TryParseQuery(query.AsSpan(), default, out _, out SparqlParseError parsed));

        Assert.Equal(SparqlErrorKind.InvalidEncoding, tokens.Error.Kind);
        Assert.Equal(parsed, tokens.Error);
        Assert.Equal(["SELECT", "?x"], Texts(query));
    }

    [Fact]
    public void An_empty_text_has_no_tokens_and_is_complete()
    {
        SparqlTokens tokens = SparqlTokenizer.Tokenize(" \n\t".AsSpan());

        Assert.Empty(tokens.Tokens);
        Assert.True(tokens.IsComplete);
    }
}
