// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.WebUi.Organisms;

namespace Varve.WebUi.Parts;

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
