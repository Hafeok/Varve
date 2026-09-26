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
/// SPARQL 1.1 Query Results CSV and TSV Formats §4, with the 1.2 draft's
/// triple terms and base directions (<c>sparql-results.md</c> §5.4): terms in
/// Turtle's syntax, without its triple-quoted strings.
/// </summary>
internal sealed class TsvResultsWriter : FormatWriter
{
    private int _column;

    internal TsvResultsWriter(ResultsOutput output)
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
                Output.Write((byte)'\t');
            }

            Output.Write((byte)'?');
            Output.Write(Names[i]);
        }

        Output.Write((byte)'\n');
    }

    // No boolean form in the Recommendation; as the CSV writer.
    internal override void WriteBoolean(bool value) =>
        Output.Write(value ? "?_askResult\ntrue\n"u8 : "?_askResult\nfalse\n"u8);

    internal override void StartSolution() => _column = 0;

    internal override void WriteBinding(int variable, scoped TermInput term)
    {
        Separate(variable);
        WriteTerm(term);
    }

    internal override void EndSolution()
    {
        Separate(Names.Length - 1);
        Output.Write((byte)'\n');
    }

    internal override void WriteEnd(bool isBoolean)
    {
    }

    private void Separate(int variable)
    {
        for (; _column < variable; _column++)
        {
            Output.Write((byte)'\t');
        }
    }

    private void WriteTerm(scoped TermInput term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                WriteIri(term.Lexical);
                break;

            case RdfTermKind.BlankNode:
                Output.Write("_:"u8);
                Output.Write(term.Lexical);
                break;

            case RdfTermKind.TripleTerm:
                Output.Write("<<( "u8);
                WriteTerm(term.Subject);
                Output.Write((byte)' ');
                WriteTerm(term.Predicate);
                Output.Write((byte)' ');
                WriteTerm(term.Object);
                Output.Write(" )>>"u8);
                break;

            default:
                WriteLiteral(term);
                break;
        }
    }

    private void WriteLiteral(scoped TermInput term)
    {
        ReadOnlySpan<byte> lexical = term.Lexical;
        ReadOnlySpan<byte> language = term.Language;
        ReadOnlySpan<byte> datatype = term.WrittenDatatype;

        if (language.IsEmpty && TurtleNumber.IsBare(lexical, datatype))
        {
            Output.Write(lexical);
            return;
        }

        Output.Write((byte)'"');
        WriteString(lexical);
        Output.Write((byte)'"');

        if (!language.IsEmpty)
        {
            Output.Write((byte)'@');
            Output.Write(language);

            if (term.Direction != TextDirection.None)
            {
                Output.Write(term.Direction == TextDirection.LeftToRight ? "--ltr"u8 : "--rtl"u8);
            }
        }
        else if (!datatype.IsEmpty)
        {
            Output.Write("^^"u8);
            WriteIri(datatype);
        }
    }

    // ECHAR for ", \, LF, CR and tab — what a single-quoted Turtle string and
    // a TSV line cannot hold raw — and nothing else.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void WriteString(ReadOnlySpan<byte> text)
    {
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            ReadOnlySpan<byte> escape = text[i] switch
            {
                (byte)'"' => "\\\""u8,
                (byte)'\\' => "\\\\"u8,
                (byte)'\n' => "\\n"u8,
                (byte)'\r' => "\\r"u8,
                (byte)'\t' => "\\t"u8,
                _ => default,
            };

            if (escape.IsEmpty)
            {
                continue;
            }

            Output.Write(text[start..i]);
            Output.Write(escape);
            start = i + 1;
        }

        Output.Write(text[start..]);
    }

    // IRIREF excludes controls, space and <>"{}|^`\ ; a term that holds one
    // anyway is written with UCHAR rather than as a field that does not parse.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void WriteIri(ReadOnlySpan<byte> iri)
    {
        Output.Write((byte)'<');
        int start = 0;

        for (int i = 0; i < iri.Length; i++)
        {
            byte b = iri[i];

            if (b > 0x20 && b is not ((byte)'<' or (byte)'>' or (byte)'"' or (byte)'{' or (byte)'}' or (byte)'|' or (byte)'^' or (byte)'`' or (byte)'\\'))
            {
                continue;
            }

            Output.Write(iri[start..i]);
            Output.Write("\\u00"u8);
            Output.Write(Hex[b >> 4]);
            Output.Write(Hex[b & 0xF]);
            start = i + 1;
        }

        Output.Write(iri[start..]);
        Output.Write((byte)'>');
    }

    private static ReadOnlySpan<byte> Hex => "0123456789ABCDEF"u8;
}
