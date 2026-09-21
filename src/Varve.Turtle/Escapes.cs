using System;
using System.Buffers;
using System.Text;

namespace Varve.Turtle;

/// <summary>Escaping on the way out.</summary>
/// <remarks>
/// Canonical N-Triples §4 is deliberately narrow: within a string, <c>ECHAR</c>
/// for U+0022, U+005C, U+000A and U+000D <strong>and nothing else</strong>, and
/// a character that can be written directly must be. That is what makes two
/// canonical documents comparable byte for byte.
/// </remarks>
internal static class Escapes
{
    private static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;

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
