// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client.Model;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.WebUi.Explorer.Organisms;

namespace Varve.WebUi.Explorer.Parts;

/// <summary>
/// The query editor's runs from ADR 0248's tokenizer, over the very text the
/// textarea holds (docs/ui/components/QueryEditor/README.md): one run per
/// token, its kind mapped to a <c>vw-tok--</c> class. Whitespace is no token,
/// so it stays plain text.
/// </summary>
[Contract(typeof(TheQueryEditor.ColouredByTheTokenizerCheckedByTheParser), Role = "the editor's runs, error, form and bracket match, from the parser's own lexer")]
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

    /// <summary>
    /// The form of the answer <paramref name="text"/> asks for, from its first
    /// query keyword, so the page asks for the right media type: solutions
    /// for SELECT, a boolean for ASK, a graph for CONSTRUCT and DESCRIBE. None
    /// before one is written.
    /// </summary>
    public static QueryForm? Form(string text)
    {
        foreach (SparqlToken token in SparqlTokenizer.Tokenize(text.AsSpan()).Tokens)
        {
            if (token.Kind != SparqlTokenKind.Keyword)
            {
                continue;
            }

            ReadOnlySpan<char> word = text.AsSpan((int)token.Start, (int)(token.End - token.Start));

            if (word.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return QueryForm.Solutions;
            }

            if (word.Equals("ASK", StringComparison.OrdinalIgnoreCase))
            {
                return QueryForm.Boolean;
            }

            if (word.Equals("CONSTRUCT", StringComparison.OrdinalIgnoreCase) || word.Equals("DESCRIBE", StringComparison.OrdinalIgnoreCase))
            {
                return QueryForm.Graph;
            }
        }

        return null;
    }

    /// <summary>
    /// The bracket pair at <paramref name="caret"/>, as the starts of its two
    /// runs, when a bracket ends or starts at the caret and has its partner;
    /// none otherwise. A bracket just typed, ending at the caret, wins.
    /// </summary>
    public static IReadOnlyList<int> Match(string text, IReadOnlyList<EditorRun> runs, int caret)
    {
        int at = -1;

        for (int i = 0; i < runs.Count && at < 0; i++)
        {
            if (runs[i].End == caret && Bracket(text, runs[i]) != 0)
            {
                at = i;
            }
        }

        for (int i = 0; i < runs.Count && at < 0; i++)
        {
            if (runs[i].Start == caret && Bracket(text, runs[i]) != 0)
            {
                at = i;
            }
        }

        if (at < 0)
        {
            return [];
        }

        char open = Opening(text[runs[at].Start]);
        int step = Bracket(text, runs[at]) > 0 ? 1 : -1;
        int depth = 0;

        for (int i = at; i >= 0 && i < runs.Count; i += step)
        {
            int bracket = Bracket(text, runs[i]);

            if (bracket == 0 || Opening(text[runs[i].Start]) != open)
            {
                continue;
            }

            depth += bracket * step;

            if (depth == 0)
            {
                return step > 0 ? [runs[at].Start, runs[i].Start] : [runs[i].Start, runs[at].Start];
            }
        }

        return [];
    }

    // +1 for an opening bracket, -1 for a closing one, 0 for any other run.
    private static int Bracket(string text, EditorRun run) =>
        run.End - run.Start != 1 || run.Kind != "punct" ? 0 : text[run.Start] switch
        {
            '(' or '{' or '[' => 1,
            ')' or '}' or ']' => -1,
            _ => 0,
        };

    private static char Opening(char bracket) => bracket switch
    {
        ')' => '(',
        '}' => '{',
        ']' => '[',
        _ => bracket,
    };

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
