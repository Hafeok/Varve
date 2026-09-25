// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Rdf;

/// <summary>
/// RDFC-1.0 Appendix A's canonical form of N-Quads, term by term
/// (<c>rdf-canon.md</c> §4). Written here because the N-Quads writer is at
/// layer 2 (ADR 0003). It is the same form as that writer's canonical output,
/// RDF 1.2 N-Triples §3's (ADR 0061), and a property holds the two
/// byte-identical.
/// </summary>
internal static class CanonicalNQuads
{
    private static ReadOnlySpan<byte> Hex => "0123456789ABCDEF"u8;

    /// <summary>A term other than a blank node, as Appendix A writes it.</summary>
    internal static void Write(RdfTerm term, List<byte> output)
    {
        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                output.Add((byte)'<');
                output.AddRange(term.Lexical);
                output.Add((byte)'>');
                break;

            case RdfTermKind.BlankNode:
                throw new InvalidOperationException("A blank node is written by its canonical or placeholder identifier.");

            case RdfTermKind.TripleTerm:
                // N-Quads 1.2's form; RDFC-1.0 predates it (rdf-canon.md §3.1).
                output.AddRange("<<( "u8);
                Write(term.Subject!, output);
                output.Add((byte)' ');
                Write(term.Predicate!, output);
                output.Add((byte)' ');
                Write(term.Object!, output);
                output.AddRange(" )>>"u8);
                break;

            default:
                output.Add((byte)'"');
                WriteString(term.Lexical, output);
                output.Add((byte)'"');

                if (!term.Language.IsEmpty)
                {
                    // Lowercase: tags compare case-insensitively (RDF 1.1
                    // Concepts §3.3), so one term must have one form — RDF 1.2
                    // N-Triples §3 requires it; Appendix A is silent.
                    output.Add((byte)'@');

                    foreach (byte b in term.Language)
                    {
                        output.Add(b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b);
                    }

                    if (term.Direction != TextDirection.None)
                    {
                        output.AddRange(term.Direction == TextDirection.LeftToRight ? "--ltr"u8 : "--rtl"u8);
                    }
                }
                else if (term.Datatype is { } datatype)
                {
                    // xsd:string is never held (the model folds it away), so never written.
                    output.AddRange("^^<"u8);
                    output.AddRange(datatype.Lexical);
                    output.Add((byte)'>');
                }

                break;
        }
    }

    /// <summary>
    /// A string's content: BS, HT, LF, FF, CR, <c>"</c> and <c>\</c> as
    /// <c>ECHAR</c>; U+0000–U+0007, VT, U+000E–U+001F, DEL, U+FFFE and U+FFFF —
    /// what XML 1.1's <c>Char</c> excludes that UTF-8 can hold — as
    /// <c>\u</c> with four uppercase digits; everything else as itself.
    /// </summary>
    internal static void WriteString(ReadOnlySpan<byte> text, List<byte> output)
    {
        for (int i = 0; i < text.Length; i++)
        {
            byte b = text[i];

            switch (b)
            {
                case 0x08:
                    output.AddRange("\\b"u8);
                    continue;
                case 0x09:
                    output.AddRange("\\t"u8);
                    continue;
                case 0x0A:
                    output.AddRange("\\n"u8);
                    continue;
                case 0x0C:
                    output.AddRange("\\f"u8);
                    continue;
                case 0x0D:
                    output.AddRange("\\r"u8);
                    continue;
                case (byte)'"':
                    output.AddRange("\\\""u8);
                    continue;
                case (byte)'\\':
                    output.AddRange("\\\\"u8);
                    continue;
                case < 0x20 or 0x7F:
                    Uchar(b, output);
                    continue;
                case 0xEF when i + 2 < text.Length && text[i + 1] == 0xBF && text[i + 2] >= 0xBE:
                    Uchar(0xFFFE + (text[i + 2] - 0xBE), output);
                    i += 2;
                    continue;
                default:
                    output.Add(b);
                    continue;
            }
        }
    }

    private static void Uchar(int codePoint, List<byte> output)
    {
        output.AddRange("\\u"u8);
        output.Add(Hex[(codePoint >> 12) & 0xF]);
        output.Add(Hex[(codePoint >> 8) & 0xF]);
        output.Add(Hex[(codePoint >> 4) & 0xF]);
        output.Add(Hex[codePoint & 0xF]);
    }
}
