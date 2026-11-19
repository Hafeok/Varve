// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.WebUi.Components.Organisms;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>
/// The query editor's runs from ADR 0248's tokenizer, over the very text the
/// textarea holds (docs/ui/components/QueryEditor/README.md): one run per
/// token, its kind mapped to a <c>vw-tok--</c> class. Whitespace is no token,
/// so it stays plain text.
/// </summary>
public static class QueryColouring
{
    /// <summary>The runs of <paramref name="text"/>, up to the first ill-formed terminal.</summary>
    public static IReadOnlyList<EditorRun> Runs(string text)
    {
        SparqlTokens tokens = SparqlTokenizer.Tokenize(text.AsSpan());
        List<EditorRun> runs = new(tokens.Tokens.Count);

        foreach (SparqlToken token in tokens.Tokens)
        {
            runs.Add(new EditorRun((int)token.Start, (int)token.End, Kind(token.Kind)));
        }

        return runs;
    }

    /// <summary>
    /// The parser's first error on <paramref name="text"/>, as the editor shows
    /// it: at the error's UTF-16 index, with the parser's line, column and
    /// message, the very words the server's <c>sparql-syntax</c> problem
    /// carries. None for a text that parses, or for one that is only
    /// whitespace, which nobody has started to write.
    /// </summary>
    public static EditorError? Error(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || SparqlParser.TryParseQuery(text.AsSpan(), default, out _, out SparqlParseError error))
        {
            return null;
        }

        return new EditorError(Utf16Index(text, error.Offset), $"{error.Line}:{error.Column}: {error.Message}");
    }

    // The parser's offsets are bytes of the text's UTF-8 form (sparql-grammar.md §6).
    private static int Utf16Index(string text, long utf8Offset)
    {
        long bytes = 0;
        int i = 0;

        while (i < text.Length && bytes < utf8Offset)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                bytes += 4;
                i += 2;
            }
            else
            {
                bytes += c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                i++;
            }
        }

        return i;
    }

    /// <summary>The class suffix of a token kind.</summary>
    public static string Kind(SparqlTokenKind kind) => kind switch
    {
        SparqlTokenKind.Keyword => "keyword",
        SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName => "iri",
        SparqlTokenKind.BlankNodeLabel or SparqlTokenKind.Anon => "bnode",
        SparqlTokenKind.Variable => "variable",
        SparqlTokenKind.String or SparqlTokenKind.Integer or SparqlTokenKind.Decimal or SparqlTokenKind.Double or SparqlTokenKind.Boolean => "literal",
        SparqlTokenKind.LangTag => "annotation",
        SparqlTokenKind.Comment => "comment",
        SparqlTokenKind.Word => "word",
        _ => "punct",
    };
}
