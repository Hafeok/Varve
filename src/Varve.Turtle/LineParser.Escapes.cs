using System;
using System.Text.Unicode;
using Varve.Rdf;

namespace Varve.Turtle;

/// <summary>UCHAR [10] and ECHAR [153s].</summary>
/// <remarks>
/// Escapes are resolved <em>before</em> validation, so
/// <c>&lt;http://example/ &gt;</c> is a bad IRI rather than a good one
/// containing an escape. A decoded term never grows: the shortest escape is two
/// source bytes for one output byte and the longest is twelve for four, so the
/// scratch reserved is the source length and that is always enough.
/// </remarks>
internal ref partial struct LineParser
{
    private bool TryMeasureEscape(int index, bool allowEchar, out int length)
    {
        length = 0;

        if (index + 1 >= _line.Length)
        {
            return Fail(ParseErrorKind.InvalidEscape, index);
        }

        byte c = _line[index + 1];
        int digits = c switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => 0,
        };

        if (digits == 0)
        {
            if (!allowEchar || !IsEchar(c))
            {
                return Fail(ParseErrorKind.InvalidEscape, index + 1);
            }

            length = 2;
            return true;
        }

        if (index + 2 + digits > _line.Length)
        {
            return Fail(ParseErrorKind.InvalidUnicodeEscape, index + 2);
        }

        for (int i = index + 2; i < index + 2 + digits; i++)
        {
            if (!NTriplesChars.IsHex(_line[i]))
            {
                return Fail(ParseErrorKind.InvalidUnicodeEscape, i);
            }
        }

        length = 2 + digits;
        return true;
    }

    private bool TryDecodeEscaped(int start, int end, bool allowEchar, out TermSpan span)
    {
        span = TermSpan.None;
        Span<byte> destination = _arena.ReserveScratch(end - start);
        int written = 0;
        int i = start;

        while (i < end)
        {
            byte b = _line[i];

            if (b != (byte)'\\')
            {
                destination[written++] = b;
                i++;
                continue;
            }

            byte c = _line[i + 1];

            if (c is not ((byte)'u' or (byte)'U'))
            {
                destination[written++] = Unescape(c);
                i += 2;
                continue;
            }

            int digits = c == (byte)'u' ? 4 : 8;
            int codePoint = ReadHex(i + 2, digits);
            i += 2 + digits;

            if (codePoint is >= 0xD800 and <= 0xDBFF)
            {
                if (i + 6 > end || _line[i] != (byte)'\\' || _line[i + 1] != (byte)'u')
                {
                    return Fail(ParseErrorKind.UnpairedSurrogate, i);
                }

                int low = ReadHex(i + 2, 4);

                if (low is not (>= 0xDC00 and <= 0xDFFF))
                {
                    return Fail(ParseErrorKind.UnpairedSurrogate, i);
                }

                codePoint = 0x10000 + ((codePoint - 0xD800) << 10) + (low - 0xDC00);
                i += 6;
            }
            else if (codePoint is >= 0xDC00 and <= 0xDFFF)
            {
                return Fail(ParseErrorKind.UnpairedSurrogate, i - (2 + digits));
            }
            else if (codePoint > 0x10FFFF)
            {
                return Fail(ParseErrorKind.InvalidUnicodeEscape, i - (2 + digits));
            }

            written += EncodeUtf8(codePoint, destination[written..]);
        }

        ReadOnlySpan<byte> decoded = destination[..written];

        if (allowEchar)
        {
            if (!Utf8.IsValid(decoded))
            {
                return Fail(ParseErrorKind.InvalidUtf8, start);
            }
        }
        else if (!TryValidateIri(decoded, start))
        {
            return false;
        }

        span = _arena.CommitScratch(written);
        return true;
    }

    private readonly int ReadHex(int index, int digits)
    {
        int value = 0;

        for (int i = index; i < index + digits; i++)
        {
            value = (value << 4) | NTriplesChars.HexValue(_line[i]);
        }

        return value;
    }

    private static bool IsEchar(byte c) =>
        c is (byte)'t' or (byte)'b' or (byte)'n' or (byte)'r' or (byte)'f'
            or (byte)'"' or (byte)'\'' or (byte)'\\';

    private static byte Unescape(byte c) => c switch
    {
        (byte)'t' => 0x09,
        (byte)'b' => 0x08,
        (byte)'n' => 0x0A,
        (byte)'r' => 0x0D,
        (byte)'f' => 0x0C,
        _ => c,
    };

    private static int EncodeUtf8(int codePoint, Span<byte> destination)
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
