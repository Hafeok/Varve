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

/// <summary>The parser's verdict on the editor's text: its first error, or the form of the answer it asks for.</summary>
/// <param name="Error">The parser's first error, or null when the text parses or is blank.</param>
/// <param name="Form">The answer's form, from the parsed query, or null when it does not parse or is blank.</param>
public sealed record QueryCheck(EditorError? Error, QueryForm? Form);

/// <summary>
/// The query editor's runs from ADR 0253's tokenizer, over the very text the
/// textarea holds (docs/ui/components/QueryEditor/README.md): one run per
/// token, its kind mapped to a <c>vw-tok--</c> class. Whitespace is no token,
/// so it stays plain text.
/// </summary>
[Contract(typeof(TheQueryEditor.ColouredByTheTokenizerCheckedByTheParser), Role = "the editor's runs, error, form and bracket match, from the parser's own lexer")]
public static class QueryColouring
{
    /// <summary>
    /// The longest text, in UTF-16 code units, that the editor colours and
    /// checks as it is typed (ADR 0250 point 6): the largest size on the
    /// measurement's grid (1,000, 10,000, 25,000, 50,000, 100,000 and 200,000)
    /// whose keystroke, median of seven, re-renders in under 100 ms in the
    /// published bundle. 25,000 measured 51.6 and 56.3 ms; 50,000 measured
    /// 104.0 and 104.6 ms. The ruling is
    /// <c>TheQueryEditor.ColouredAndCheckedUpTo25000</c>, cited here and on
    /// the class's contract; the form's is <c>TheFormFromTheParsedQuery</c>.
    /// </summary>
    public const int CheckedLength = 25_000;

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
    /// The parser's verdict on <paramref name="text"/>, from one parse: its
    /// first error as the editor shows it, at the error's UTF-16 index with
    /// the parser's line, column and message (the very words the server's
    /// <c>sparql-syntax</c> problem carries); or, for a text that parses, the
    /// form of the answer it asks for, read from the parsed query (ADR 0250
    /// point 5). A text that is only whitespace has neither: nobody has
    /// started to write it.
    /// </summary>
    public static QueryCheck Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new QueryCheck(null, null);
        }

        if (!SparqlParser.TryParseQuery(text.AsSpan(), default, out Query? query, out SparqlParseError error))
        {
            return new QueryCheck(new EditorError(Utf16Index(text, error.Offset), $"{error.Line}:{error.Column}: {error.Message}"), null);
        }

        QueryForm form = query switch
        {
            SelectQuery => QueryForm.Solutions,
            AskQuery => QueryForm.Boolean,
            _ => QueryForm.Graph,
        };
        return new QueryCheck(null, form);
    }

    /// <summary>The parser's first error on <paramref name="text"/>; see <see cref="Check(string)"/>.</summary>
    public static EditorError? Error(string text) => Check(text).Error;

    /// <summary>
    /// The form of the answer <paramref name="text"/> asks for, from the
    /// parsed query: solutions for a SELECT, a boolean for an ASK, a graph for
    /// a CONSTRUCT or a DESCRIBE; none for a text the parser refuses, which is
    /// never sent. See <see cref="Check(string)"/>.
    /// </summary>
    public static QueryForm? Form(string text) => Check(text).Form;

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
