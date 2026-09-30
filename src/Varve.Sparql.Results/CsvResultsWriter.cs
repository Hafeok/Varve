// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Results;

/// <summary>
/// SPARQL 1.1 Query Results CSV and TSV Formats §3, with the 1.2 draft's
/// triple terms (<c>sparql-results.md</c> §5.3). Lossy by the format's own
/// statement: a field is a term's string value and nothing else.
/// </summary>
internal sealed class CsvResultsWriter : FormatWriter
{
    private int _column;

    internal CsvResultsWriter(ResultsOutput output)
        : base(output)
    {
    }

    internal override void WriteHead(IReadOnlyList<string> variables)
    {
        base.WriteHead(variables);

        for (int i = 0; i < Names.Length; i++)
        {
            if (i > 0)
            {
                Output.Write((byte)',');
            }

            WriteField(Names[i]);
        }

        Output.Write("\r\n"u8);
    }

    // No boolean form in the Recommendation; the header Oxigraph and Jena use.
    internal override void WriteBoolean(bool value) =>
        Output.Write(value ? "_askResult\r\ntrue\r\n"u8 : "_askResult\r\nfalse\r\n"u8);

    internal override void StartSolution() => _column = 0;

    internal override void WriteBinding(int variable, scoped TermInput term)
    {
        Separate(variable);
        bool quoted = NeedsQuotes(term);

        if (quoted)
        {
            Output.Write((byte)'"');
        }

        WriteText(term, quoted);

        if (quoted)
        {
            Output.Write((byte)'"');
        }
    }

    internal override void EndSolution()
    {
        Separate(Names.Length - 1);

        // A row for a query of no variables is empty, and still a record.
        Output.Write("\r\n"u8);
    }

    internal override void WriteEnd(bool isBoolean)
    {
    }

    // Commas up to the field of this variable: the fields skipped are unbound.
    private void Separate(int variable)
    {
        for (; _column < variable; _column++)
        {
            Output.Write((byte)',');
        }
    }

    private void WriteText(scoped TermInput term, bool quoted)
    {
        switch (term.Kind)
        {
            case RdfTermKind.BlankNode:
                Output.Write("_:"u8);
                WriteEscaped(term.Lexical, quoted);
                break;

            case RdfTermKind.TripleTerm:
                Output.Write("<<( "u8);
                WriteText(term.Subject, quoted);
                Output.Write((byte)' ');
                WriteText(term.Predicate, quoted);
                Output.Write((byte)' ');
                WriteText(term.Object, quoted);
                Output.Write(" )>>"u8);
                break;

            default:
                WriteEscaped(term.Lexical, quoted);
                break;
        }
    }

    private void WriteField(ReadOnlySpan<byte> text)
    {
        bool quoted = NeedsQuotes(text);

        if (quoted)
        {
            Output.Write((byte)'"');
        }

        WriteEscaped(text, quoted);

        if (quoted)
        {
            Output.Write((byte)'"');
        }
    }

    // Inside quotes, a quotation mark is doubled (RFC 4180 §2 rule 7).
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void WriteEscaped(ReadOnlySpan<byte> text, bool quoted)
    {
        if (!quoted)
        {
            Output.Write(text);
            return;
        }

        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == (byte)'"')
            {
                Output.Write(text[start..(i + 1)]);
                Output.Write((byte)'"');
                start = i + 1;
            }
        }

        Output.Write(text[start..]);
    }

    private static bool NeedsQuotes(scoped TermInput term) =>
        term.Kind == RdfTermKind.TripleTerm
            ? NeedsQuotes(term.Subject) || NeedsQuotes(term.Predicate) || NeedsQuotes(term.Object)
            : NeedsQuotes(term.Lexical);

    // §3.2: a field containing ", a comma, LF or CR is quoted.
    private static bool NeedsQuotes(ReadOnlySpan<byte> text) => text.IndexOfAny("\",\n\r"u8) >= 0;
}
