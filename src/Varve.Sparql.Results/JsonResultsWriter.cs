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
/// SPARQL 1.1 Query Results JSON Format, with the 1.2 draft's triple terms and
/// <c>its:dir</c> (<c>sparql-results.md</c> §5.2). Written by hand: the
/// BCL's JSON writer escapes every non-ASCII character by default.
/// </summary>
internal sealed class JsonResultsWriter : FormatWriter
{
    private bool _firstSolution = true;
    private bool _firstBinding;

    internal JsonResultsWriter(ResultsOutput output)
        : base(output)
    {
    }

    internal override void WriteHead(IReadOnlyList<string> variables)
    {
        base.WriteHead(variables);
        Output.Write("{\"head\":{\"vars\":["u8);

        for (int i = 0; i < Names.Length; i++)
        {
            if (i > 0)
            {
                Output.Write((byte)',');
            }

            WriteString(Names[i]);
        }

        Output.Write("]},\"results\":{\"bindings\":["u8);
    }

    internal override void WriteBoolean(bool value) =>
        Output.Write(value ? "{\"head\":{},\"boolean\":true"u8 : "{\"head\":{},\"boolean\":false"u8);

    internal override void StartSolution()
    {
        Output.Write(_firstSolution ? "{"u8 : ",{"u8);
        _firstSolution = false;
        _firstBinding = true;
    }

    internal override void WriteBinding(int variable, scoped TermInput term)
    {
        if (!_firstBinding)
        {
            Output.Write((byte)',');
        }

        _firstBinding = false;
        WriteString(Names[variable]);
        Output.Write((byte)':');
        WriteTerm(term);
    }

    internal override void EndSolution() => Output.Write((byte)'}');

    internal override void WriteEnd(bool isBoolean) => Output.Write(isBoolean ? "}\n"u8 : "]}}\n"u8);

    private void WriteTerm(scoped TermInput term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                Output.Write("{\"type\":\"uri\",\"value\":"u8);
                WriteString(term.Lexical);
                break;

            case RdfTermKind.BlankNode:
                Output.Write("{\"type\":\"bnode\",\"value\":"u8);
                WriteString(term.Lexical);
                break;

            case RdfTermKind.TripleTerm:
                Output.Write("{\"type\":\"triple\",\"value\":{\"subject\":"u8);
                WriteTerm(term.Subject);
                Output.Write(",\"predicate\":"u8);
                WriteTerm(term.Predicate);
                Output.Write(",\"object\":"u8);
                WriteTerm(term.Object);
                Output.Write((byte)'}');
                break;

            default:
                Output.Write("{\"type\":\"literal\",\"value\":"u8);
                WriteString(term.Lexical);
                ReadOnlySpan<byte> language = term.Language;

                if (!language.IsEmpty)
                {
                    Output.Write(",\"xml:lang\":"u8);
                    WriteString(language);

                    if (term.Direction != TextDirection.None)
                    {
                        Output.Write(term.Direction == TextDirection.LeftToRight ? ",\"its:dir\":\"ltr\""u8 : ",\"its:dir\":\"rtl\""u8);
                    }
                }
                else
                {
                    ReadOnlySpan<byte> datatype = term.WrittenDatatype;

                    if (!datatype.IsEmpty)
                    {
                        Output.Write(",\"datatype\":"u8);
                        WriteString(datatype);
                    }
                }

                break;
        }

        Output.Write((byte)'}');
    }

    /// <summary>
    /// A JSON string: <c>"</c>, <c>\</c> and U+0000–U+001F escaped (RFC 8259
    /// §7), the two-character forms where they exist, and nothing else.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void WriteString(ReadOnlySpan<byte> text)
    {
        Output.Write((byte)'"');
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            byte b = text[i];

            if (b >= 0x20 && b != (byte)'"' && b != (byte)'\\')
            {
                continue;
            }

            Output.Write(text[start..i]);
            start = i + 1;

            switch (b)
            {
                case (byte)'"':
                    Output.Write("\\\""u8);
                    break;
                case (byte)'\\':
                    Output.Write("\\\\"u8);
                    break;
                case (byte)'\b':
                    Output.Write("\\b"u8);
                    break;
                case (byte)'\f':
                    Output.Write("\\f"u8);
                    break;
                case (byte)'\n':
                    Output.Write("\\n"u8);
                    break;
                case (byte)'\r':
                    Output.Write("\\r"u8);
                    break;
                case (byte)'\t':
                    Output.Write("\\t"u8);
                    break;
                default:
                    Output.Write("\\u00"u8);
                    Output.Write(Hex[b >> 4]);
                    Output.Write(Hex[b & 0xF]);
                    break;
            }
        }

        Output.Write(text[start..]);
        Output.Write((byte)'"');
    }

    private static ReadOnlySpan<byte> Hex => "0123456789ABCDEF"u8;
}
