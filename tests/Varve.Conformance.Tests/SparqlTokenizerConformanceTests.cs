// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The tokenizer's three guarantees (<c>docs/spec/sparql-grammar.md</c> §10,
/// ADR 0253) over every case of every wired SPARQL syntax suite, positive and
/// negative: the tokens and the gaps between them are the input; where the
/// tokens stop on an error, the parser fails there or earlier; and UTF-8 and
/// UTF-16 give the same tokens.
/// </summary>
public class SparqlTokenizerConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (SparqlManifestEntry entry in SparqlCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_tokens_and_the_gaps_between_them_are_the_input(string testIri)
    {
        SparqlManifestEntry entry = SparqlCatalogue.ByIri[testIri];
        byte[] bytes = File.ReadAllBytes(entry.ActionPath);
        SparqlTokens tokens = SparqlTokenizer.Tokenize(bytes);

        long at = 0;

        foreach (SparqlToken token in tokens.Tokens)
        {
            Assert.True(token.Start >= at && token.End > token.Start, $"{testIri}: a token at {token.Start}–{token.End} overlaps or is empty after {at}.");
            AssertWhitespace(bytes, at, token.Start, testIri);
            at = token.End;
        }

        if (tokens.IsComplete)
        {
            AssertWhitespace(bytes, at, bytes.Length, testIri);
            return;
        }

        // Stopped on an error: it is at or after the last token, and the error
        // may point inside the terminal it could not finish, so only the gap
        // before that terminal is whitespace.
        Assert.True(tokens.Error.Offset >= at, $"{testIri}: the error {tokens.Error} is before the last token's end, {at}.");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Where_the_tokens_stop_on_an_error_the_parser_fails_there_or_earlier(string testIri)
    {
        SparqlManifestEntry entry = SparqlCatalogue.ByIri[testIri];
        SparqlTokens tokens = SparqlTokenizer.Tokenize(File.ReadAllBytes(entry.ActionPath));

        if (tokens.IsComplete)
        {
            return;
        }

        SparqlOutcome parsed = SparqlSubjects.Current!.Parse(entry, asUtf16: false);

        Assert.False(parsed.Succeeded, $"{testIri}: the tokens stop at {tokens.Error}, but the parser accepts it.");
        Assert.True(
            parsed.Error.Offset < tokens.Error.Offset
                || (parsed.Error.Offset == tokens.Error.Offset && parsed.Error.Kind == tokens.Error.Kind
                    && parsed.Error.Line == tokens.Error.Line && parsed.Error.Column == tokens.Error.Column),
            $"{testIri}: the tokens stop at {tokens.Error}; the parser's error is {parsed.Error}.");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Utf16_gives_the_tokens_of_utf8_at_the_mapped_positions(string testIri)
    {
        SparqlManifestEntry entry = SparqlCatalogue.ByIri[testIri];
        byte[] bytes = File.ReadAllBytes(entry.ActionPath);
        string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

        SparqlTokens fromBytes = SparqlTokenizer.Tokenize(bytes);
        SparqlTokens fromChars = SparqlTokenizer.Tokenize(text.AsSpan());

        Assert.Equal(fromBytes.Error, fromChars.Error);
        Assert.Equal(fromBytes.Tokens.Count, fromChars.Tokens.Count);

        for (int i = 0; i < fromBytes.Tokens.Count; i++)
        {
            SparqlToken b = fromBytes.Tokens[i];
            SparqlToken c = fromChars.Tokens[i];

            Assert.Equal(b.Kind, c.Kind);
            Assert.Equal(b.Start, Encoding.UTF8.GetByteCount(text.AsSpan(0, (int)c.Start)));
            Assert.Equal(b.End, Encoding.UTF8.GetByteCount(text.AsSpan(0, (int)c.End)));
        }
    }

    private static void AssertWhitespace(byte[] bytes, long from, long to, string testIri)
    {
        for (long i = from; i < to; i++)
        {
            Assert.True(bytes[i] is 0x20 or 0x09 or 0x0A or 0x0D, $"{testIri}: byte {i} (0x{bytes[i]:X2}) is in no token and is not whitespace.");
        }
    }
}
