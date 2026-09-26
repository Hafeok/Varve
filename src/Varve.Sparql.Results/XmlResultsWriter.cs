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
/// SPARQL Query Results XML Format §2, with the 1.2 draft's <c>&lt;triple&gt;</c>
/// and <c>its:dir</c> (<c>sparql-results.md</c> §5.1).
/// </summary>
internal sealed class XmlResultsWriter : FormatWriter
{
    // The ITS namespace is declared on every document: the root is written
    // before the writer can know whether a directional literal follows.
    private static ReadOnlySpan<byte> Prologue =>
        "<?xml version=\"1.0\"?>\n<sparql xmlns=\"http://www.w3.org/2005/sparql-results#\" xmlns:its=\"http://www.w3.org/2005/11/its\">\n<head>"u8;

    internal XmlResultsWriter(ResultsOutput output)
        : base(output)
    {
    }

    internal override void WriteHead(IReadOnlyList<string> variables)
    {
        base.WriteHead(variables);
        Output.Write(Prologue);

        foreach (byte[] name in Names)
        {
            Output.Write("<variable name=\""u8);
            Escape(name, attribute: true);
            Output.Write("\"/>"u8);
        }

        Output.Write("</head>\n<results>\n"u8);
    }

    internal override void WriteBoolean(bool value)
    {
        Output.Write(Prologue);
        Output.Write("</head>\n<boolean>"u8);
        Output.Write(value ? "true"u8 : "false"u8);
        Output.Write("</boolean>\n"u8);
    }

    internal override void StartSolution() => Output.Write("<result>"u8);

    internal override void WriteBinding(int variable, scoped TermInput term)
    {
        Output.Write("<binding name=\""u8);
        Escape(Names[variable], attribute: true);
        Output.Write("\">"u8);
        WriteTerm(term);
        Output.Write("</binding>"u8);
    }

    internal override void EndSolution() => Output.Write("</result>\n"u8);

    internal override void WriteEnd(bool isBoolean)
    {
        if (!isBoolean)
        {
            Output.Write("</results>\n"u8);
        }

        Output.Write("</sparql>\n"u8);
    }

    private void WriteTerm(scoped TermInput term)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                Output.Write("<uri>"u8);
                Escape(term.Lexical, attribute: false);
                Output.Write("</uri>"u8);
                break;

            case RdfTermKind.BlankNode:
                Output.Write("<bnode>"u8);
                Escape(term.Lexical, attribute: false);
                Output.Write("</bnode>"u8);
                break;

            case RdfTermKind.TripleTerm:
                Output.Write("<triple><subject>"u8);
                WriteTerm(term.Subject);
                Output.Write("</subject><predicate>"u8);
                WriteTerm(term.Predicate);
                Output.Write("</predicate><object>"u8);
                WriteTerm(term.Object);
                Output.Write("</object></triple>"u8);
                break;

            default:
                WriteLiteral(term);
                break;
        }
    }

    private void WriteLiteral(scoped TermInput term)
    {
        Output.Write("<literal"u8);
        ReadOnlySpan<byte> language = term.Language;

        if (!language.IsEmpty)
        {
            Output.Write(" xml:lang=\""u8);
            Escape(language, attribute: true);
            Output.Write((byte)'"');

            if (term.Direction != TextDirection.None)
            {
                Output.Write(term.Direction == TextDirection.LeftToRight ? " its:dir=\"ltr\""u8 : " its:dir=\"rtl\""u8);
            }
        }
        else
        {
            ReadOnlySpan<byte> datatype = term.WrittenDatatype;

            if (!datatype.IsEmpty)
            {
                Output.Write(" datatype=\""u8);
                Escape(datatype, attribute: true);
                Output.Write((byte)'"');
            }
        }

        Output.Write((byte)'>');
        Escape(term.Lexical, attribute: false);
        Output.Write("</literal>"u8);
    }

    /// <summary>
    /// Character data or an attribute value. Markup characters become
    /// entities; CR always becomes a reference, because an XML reader turns a
    /// literal CR into LF (XML 1.0 §2.11), and in an attribute TAB and LF do
    /// too, because attribute-value normalisation turns them into spaces
    /// (§3.3.3). A character outside XML 1.0's <c>Char</c> (§2.2) cannot be
    /// written at all, not even as a reference.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Escape(ReadOnlySpan<byte> text, bool attribute)
    {
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            byte b = text[i];
            ReadOnlySpan<byte> replacement;

            switch (b)
            {
                case (byte)'&':
                    replacement = "&amp;"u8;
                    break;
                case (byte)'<':
                    replacement = "&lt;"u8;
                    break;
                case (byte)'>':
                    replacement = "&gt;"u8;
                    break;
                case (byte)'"' when attribute:
                    replacement = "&quot;"u8;
                    break;
                case (byte)'\r':
                    replacement = "&#13;"u8;
                    break;
                case (byte)'\n' when attribute:
                    replacement = "&#10;"u8;
                    break;
                case (byte)'\t' when attribute:
                    replacement = "&#9;"u8;
                    break;
                case < 0x20 and not (byte)'\t' and not (byte)'\n':
                    throw NotXmlChar(b);
                case 0xEF when i + 2 < text.Length && text[i + 1] == 0xBF && text[i + 2] >= 0xBE:
                    // U+FFFE and U+FFFF.
                    throw NotXmlChar(0xFFFE + (text[i + 2] - 0xBE));
                default:
                    continue;
            }

            Output.Write(text[start..i]);
            Output.Write(replacement);
            start = i + 1;
        }

        Output.Write(text[start..]);
    }

    private static ArgumentException NotXmlChar(int codePoint) =>
        new("U+" + codePoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)
            + " cannot be written in XML 1.0, whose Char production (§2.2) excludes it; "
            + "choose the JSON or TSV format for this result.");
}
