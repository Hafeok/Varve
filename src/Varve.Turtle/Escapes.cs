// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Turtle;

/// <summary>Escaping on the way out.</summary>
/// <remarks>
/// <para>
/// Two string forms. <see cref="WriteLineString"/> is N-Triples' and N-Quads':
/// canonical RDF 1.2 N-Triples §3 (ADR 0061) — <c>ECHAR</c> for BS, HT, LF,
/// FF, CR, <c>"</c> and <c>\</c>; <c>UCHAR</c> with uppercase hex for the other
/// C0 controls, DEL, U+FFFE and U+FFFF; every other character as itself. It is
/// the form RDFC-1.0 Appendix A writes, and what makes two canonical documents
/// comparable byte for byte.
/// </para>
/// <para>
/// <see cref="WriteString"/> is Turtle's, which has no canonical form: RDF 1.1
/// N-Triples' narrower set, <c>ECHAR</c> for U+0022, U+005C, U+000A and U+000D
/// and nothing else, which every Turtle reader accepts.
/// </para>
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class Escapes
{
    private static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;

    /// <summary>
    /// A string of N-Triples or N-Quads, in RDF 1.2's canonical form when
    /// <paramref name="canonical"/> is set; otherwise the same with every
    /// non-ASCII character as a <c>UCHAR</c>.
    /// </summary>
    internal static void WriteLineString(ref SpanWriter writer, ReadOnlySpan<byte> lexical, bool canonical)
    {
        int i = 0;

        while (i < lexical.Length)
        {
            byte b = lexical[i];

            switch (b)
            {
                case 0x08:
                    writer.Bytes("\\b"u8);
                    i++;
                    continue;
                case 0x09:
                    writer.Bytes("\\t"u8);
                    i++;
                    continue;
                case 0x0A:
                    writer.Bytes("\\n"u8);
                    i++;
                    continue;
                case 0x0C:
                    writer.Bytes("\\f"u8);
                    i++;
                    continue;
                case 0x0D:
                    writer.Bytes("\\r"u8);
                    i++;
                    continue;
                case (byte)'"':
                    writer.Bytes("\\\""u8);
                    i++;
                    continue;
                case (byte)'\\':
                    writer.Bytes("\\\\"u8);
                    i++;
                    continue;
                case < 0x20 or 0x7F:
                    writer.Bytes("\\u"u8);
                    WriteHex(ref writer, b, 4);
                    i++;
                    continue;
                case < 0x80:
                    writer.Byte(b);
                    i++;
                    continue;
                default:
                    break;
            }

            // U+FFFE and U+FFFF are not XML 1.1 Chars, and the canonical form
            // escapes them whatever else it writes directly.
            if (b == 0xEF && i + 2 < lexical.Length && lexical[i + 1] == 0xBF && lexical[i + 2] >= 0xBE)
            {
                writer.Bytes("\\u"u8);
                WriteHex(ref writer, 0xFFFE + (lexical[i + 2] - 0xBE), 4);
                i += 3;
                continue;
            }

            i += WriteNonAscii(ref writer, lexical[i..], canonical);
        }
    }

    internal static void WriteString(ref SpanWriter writer, ReadOnlySpan<byte> lexical, bool canonical)
    {
        int i = 0;

        while (i < lexical.Length)
        {
            byte b = lexical[i];

            switch (b)
            {
                case (byte)'"':
                    writer.Bytes("\\\""u8);
                    i++;
                    continue;

                case (byte)'\\':
                    writer.Bytes("\\\\"u8);
                    i++;
                    continue;

                case 0x0A:
                    writer.Bytes("\\n"u8);
                    i++;
                    continue;

                case 0x0D:
                    writer.Bytes("\\r"u8);
                    i++;
                    continue;

                default:
                    break;
            }

            if (b < 0x80)
            {
                writer.Byte(b);
                i++;
                continue;
            }

            i += WriteNonAscii(ref writer, lexical[i..], canonical);
        }
    }

    internal static void WriteIri(ref SpanWriter writer, ReadOnlySpan<byte> text, bool canonical)
    {
        int i = 0;

        while (i < text.Length)
        {
            byte b = text[i];

            if (b < 0x80)
            {
                writer.Byte(b);
                i++;
                continue;
            }

            i += WriteNonAscii(ref writer, text[i..], canonical);
        }
    }

    /// <summary>
    /// Writes the first character of <paramref name="rest"/> and returns how
    /// many bytes of it were consumed. Canonical form writes the character;
    /// otherwise it becomes a UCHAR with uppercase hex.
    /// </summary>
    private static int WriteNonAscii(ref SpanWriter writer, ReadOnlySpan<byte> rest, bool canonical)
    {
        if (Rune.DecodeFromUtf8(rest, out Rune rune, out int consumed) != OperationStatus.Done)
        {
            // Not well-formed UTF-8. Nothing can represent it, and silently
            // dropping it would produce a document that looks fine, so the byte
            // is written through and the output is as malformed as the input.
            writer.Byte(rest[0]);
            return 1;
        }

        if (canonical)
        {
            writer.Bytes(rest[..consumed]);
            return consumed;
        }

        int value = rune.Value;

        if (value <= 0xFFFF)
        {
            writer.Bytes("\\u"u8);
            WriteHex(ref writer, value, 4);
        }
        else
        {
            writer.Bytes("\\U"u8);
            WriteHex(ref writer, value, 8);
        }

        return consumed;
    }

    private static void WriteHex(ref SpanWriter writer, int value, int digits)
    {
        for (int shift = (digits - 1) * 4; shift >= 0; shift -= 4)
        {
            writer.Byte(HexDigits[(value >> shift) & 0xF]);
        }
    }
}
