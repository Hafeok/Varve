// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Unicode;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>UCHAR and ECHAR, shared by every syntax that has them.</summary>
/// <remarks>
/// <para>
/// Escapes are resolved <em>before</em> validation, so
/// <c>&lt;http://example/ &gt;</c> is a bad IRI rather than a good one
/// containing an escape. A decoded term never grows: the shortest escape is two
/// source bytes for one output byte and the longest is twelve for four, so the
/// scratch reserved is the source length and that is always enough.
/// </para>
/// <para>
/// Static, over an explicit span and arena, because N-Triples and Turtle need
/// the same rules and a second copy is a second set of bugs. Turtle adds
/// <c>PN_LOCAL_ESC</c>, which is the one escape class N-Triples has no
/// equivalent of.
/// </para>
/// </remarks>
internal static class EscapeDecoder
{
    /// <summary>What a term's escapes are allowed to be.</summary>
    internal enum Allowed : byte
    {
        /// <summary>UCHAR only. Inside an IRIREF.</summary>
        UcharOnly,

        /// <summary>UCHAR and ECHAR. Inside a String.</summary>
        UcharAndEchar,

        /// <summary>PN_LOCAL_ESC and PERCENT. Inside a prefixed name's local part.</summary>
        LocalName,
    }

    /// <summary>
    /// Measures one escape at <paramref name="index"/>, reporting how many
    /// source bytes it occupies.
    /// </summary>
    /// <remarks>
    /// <paramref name="truncated"/> separates the two ways this fails, which a
    /// caller reading a chunk cannot otherwise tell apart: the escape ran past
    /// the end of what is here and more input might complete it, or a byte that
    /// is present cannot belong to any escape and no amount of input will help.
    /// Guessing from the bytes remaining gets the second case wrong whenever a
    /// bad escape happens to sit near the end.
    /// </remarks>
    internal static bool TryMeasure(
        ReadOnlySpan<byte> text,
        int index,
        Allowed allowed,
        out int length,
        out ParseErrorKind error,
        out int errorAt,
        out bool truncated)
    {
        length = 0;
        error = ParseErrorKind.None;
        errorAt = index;
        truncated = false;

        if (index + 1 >= text.Length)
        {
            error = ParseErrorKind.InvalidEscape;
            truncated = true;
            return false;
        }

        byte c = text[index + 1];

        if (allowed == Allowed.LocalName)
        {
            if (!IsLocalEscape(c))
            {
                error = ParseErrorKind.InvalidEscape;
                errorAt = index + 1;
                return false;
            }

            length = 2;
            return true;
        }

        int digits = c switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => 0,
        };

        if (digits == 0)
        {
            if (allowed != Allowed.UcharAndEchar || !IsEchar(c))
            {
                error = ParseErrorKind.InvalidEscape;
                errorAt = index + 1;
                return false;
            }

            length = 2;
            return true;
        }

        if (index + 2 + digits > text.Length)
        {
            error = ParseErrorKind.InvalidUnicodeEscape;
            errorAt = index + 2;
            truncated = true;

            // A hex digit that is present and is not one settles the question
            // here: the escape is wrong, not merely unfinished.
            for (int i = index + 2; i < text.Length; i++)
            {
                if (!NTriplesChars.IsHex(text[i]))
                {
                    errorAt = i;
                    truncated = false;
                    break;
                }
            }

            return false;
        }

        for (int i = index + 2; i < index + 2 + digits; i++)
        {
            if (!NTriplesChars.IsHex(text[i]))
            {
                error = ParseErrorKind.InvalidUnicodeEscape;
                errorAt = i;
                return false;
            }
        }

        length = 2 + digits;
        return true;
    }

    /// <summary>
    /// Decodes <c>text[start..end]</c> into the arena's scratch and returns the
    /// span naming the result.
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> text,
        int start,
        int end,
        Allowed allowed,
        TermArena arena,
        out TermSpan span,
        out ParseErrorKind error,
        out int errorAt)
    {
        span = TermSpan.None;
        error = ParseErrorKind.None;
        errorAt = start;

        Span<byte> destination = arena.ReserveScratch(end - start);
        int written = 0;
        int i = start;

        while (i < end)
        {
            byte b = text[i];

            if (b != (byte)'\\')
            {
                destination[written++] = b;
                i++;
                continue;
            }

            byte c = text[i + 1];

            if (allowed == Allowed.LocalName)
            {
                destination[written++] = c;
                i += 2;
                continue;
            }

            if (c is not ((byte)'u' or (byte)'U'))
            {
                destination[written++] = Unescape(c);
                i += 2;
                continue;
            }

            int digits = c == (byte)'u' ? 4 : 8;
            int codePoint = ReadHex(text, i + 2, digits);
            i += 2 + digits;

            if (codePoint is >= 0xD800 and <= 0xDBFF)
            {
                if (i + 6 > end || text[i] != (byte)'\\' || text[i + 1] != (byte)'u')
                {
                    error = ParseErrorKind.UnpairedSurrogate;
                    errorAt = i;
                    return false;
                }

                int low = ReadHex(text, i + 2, 4);

                if (low is not (>= 0xDC00 and <= 0xDFFF))
                {
                    error = ParseErrorKind.UnpairedSurrogate;
                    errorAt = i;
                    return false;
                }

                codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (low - 0xDC00);
                i += 6;
            }
            else if (codePoint is >= 0xDC00 and <= 0xDFFF)
            {
                error = ParseErrorKind.UnpairedSurrogate;
                errorAt = i - (2 + digits);
                return false;
            }
            else if (codePoint > 0x10FFFF)
            {
                error = ParseErrorKind.InvalidUnicodeEscape;
                errorAt = i - (2 + digits);
                return false;
            }

            written += EncodeUtf8(codePoint, destination[written..]);
        }

        if (allowed != Allowed.UcharOnly && !Utf8.IsValid(destination[..written]))
        {
            error = ParseErrorKind.InvalidUtf8;
            return false;
        }

        span = arena.CommitScratch(written);
        return true;
    }

    internal static int ReadHex(ReadOnlySpan<byte> text, int index, int digits)
    {
        int value = 0;

        for (int i = index; i < index + digits; i++)
        {
            value = (value << 4) | NTriplesChars.HexValue(text[i]);
        }

        return value;
    }

    internal static bool IsEchar(byte c) =>
        c is (byte)'t' or (byte)'b' or (byte)'n' or (byte)'r' or (byte)'f'
            or (byte)'"' or (byte)'\'' or (byte)'\\';

    /// <summary>PN_LOCAL_ESC [172s].</summary>
    internal static bool IsLocalEscape(byte c) =>
        c is (byte)'_' or (byte)'~' or (byte)'.' or (byte)'-' or (byte)'!' or (byte)'$'
            or (byte)'&' or (byte)'\'' or (byte)'(' or (byte)')' or (byte)'*' or (byte)'+'
            or (byte)',' or (byte)';' or (byte)'=' or (byte)'/' or (byte)'?' or (byte)'#'
            or (byte)'@' or (byte)'%';

    internal static byte Unescape(byte c) => c switch
    {
        (byte)'t' => 0x09,
        (byte)'b' => 0x08,
        (byte)'n' => 0x0A,
        (byte)'r' => 0x0D,
        (byte)'f' => 0x0C,
        _ => c,
    };

    internal static int EncodeUtf8(int codePoint, Span<byte> destination)
    {
        if (codePoint < 0x80)
        {
            destination[0] = (byte)codePoint;
            return 1;
        }

        if (codePoint < 0x800)
        {
            destination[0] = (byte)(0xC0 | (codePoint >> 6));
            destination[1] = (byte)(0x80 | (codePoint & 0x3F));
            return 2;
        }

        if (codePoint < 0x10000)
        {
            destination[0] = (byte)(0xE0 | (codePoint >> 12));
            destination[1] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
            destination[2] = (byte)(0x80 | (codePoint & 0x3F));
            return 3;
        }

        destination[0] = (byte)(0xF0 | (codePoint >> 18));
        destination[1] = (byte)(0x80 | ((codePoint >> 12) & 0x3F));
        destination[2] = (byte)(0x80 | ((codePoint >> 6) & 0x3F));
        destination[3] = (byte)(0x80 | (codePoint & 0x3F));
        return 4;
    }
}
