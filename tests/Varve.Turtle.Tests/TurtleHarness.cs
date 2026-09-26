// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Turtle.Model;

namespace Varve.Turtle.Tests;

/// <summary>
/// Materialises a Turtle or TriG parse, and records the directives it declared.
/// </summary>
internal static class TurtleHarness
{
    internal sealed record Declared(string Prefix, string Iri);

    internal sealed record Read(
        ParseResult Result,
        List<Harness.Row> Rows,
        List<Declared> Prefixes,
        List<string> Bases,
        List<ParseError> Errors);

    /// <summary>
    /// Parses <paramref name="document"/> and collects everything a test might
    /// want to assert on. <paramref name="recover"/> installs an error handler
    /// that continues, which is what exercises ADR 0030's recovery rule; without
    /// one the first error stops the parse, as <c>default</c> options do.
    /// </summary>
    internal static Read Parse(
        string document,
        RdfSyntax syntax = RdfSyntax.Turtle,
        string? baseIri = null,
        bool recover = false,
        bool validateIris = true)
    {
        List<Harness.Row> rows = [];
        List<Declared> prefixes = [];
        List<string> bases = [];
        List<ParseError> errors = [];

        TurtleOptions options = new()
        {
            Syntax = syntax,
            BaseIri = baseIri is null ? default : Harness.U(baseIri),
            ValidateIris = validateIris,
            OnPrefix = (prefix, iri) => prefixes.Add(new Declared(Harness.S(prefix), Harness.S(iri))),
            OnBase = iri => bases.Add(Harness.S(iri)),
            OnError = (in ParseError error) =>
            {
                errors.Add(error);
                return recover ? ErrorAction.Continue : ErrorAction.Stop;
            },
        };

        ParseResult result = TurtleParser.Parse(Harness.U(document), rows.Collect(), in options);
        return new Read(result, rows, prefixes, bases, errors);
    }

    internal static Read ParseTriG(string document, string? baseIri = null, bool recover = false) =>
        Parse(document, RdfSyntax.TriG, baseIri, recover);

    /// <summary>
    /// One line per quad in N-Quads shape, so that a test states a whole
    /// document's output in one string rather than a term at a time.
    /// </summary>
    internal static string[] Lines(this Read read)
    {
        string[] lines = new string[read.Rows.Count];

        for (int i = 0; i < read.Rows.Count; i++)
        {
            Harness.Row row = read.Rows[i];
            StringBuilder text = new();
            Append(text, row.Subject);
            text.Append(' ');
            Append(text, row.Predicate);
            text.Append(' ');
            Append(text, row.Object);

            if (row.Graph is not null)
            {
                text.Append(' ');
                Append(text, row.Graph);
            }

            lines[i] = text.ToString();
        }

        return lines;
    }

    private static void Append(StringBuilder text, RdfTerm term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                text.Append('<').Append(Harness.S(term.Lexical)).Append('>');
                break;

            case RdfTermKind.BlankNode:
                text.Append("_:").Append(Harness.S(term.Lexical));
                break;

            default:
                text.Append('"').Append(Harness.S(term.Lexical)).Append('"');

                if (!term.Language.IsEmpty)
                {
                    text.Append('@').Append(Harness.S(term.Language));
                }
                else if (term.Datatype is not null)
                {
                    text.Append("^^<").Append(Harness.S(term.Datatype.Lexical)).Append('>');
                }

                break;
        }
    }
}
