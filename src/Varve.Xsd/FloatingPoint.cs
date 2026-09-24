// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Xsd;

/// <summary>
/// The lexical and canonical mappings <see cref="XsdDouble"/> and
/// <see cref="XsdFloat"/> share (XML Schema 1.1 Part 2 §3.3.5.2, §3.3.4.2,
/// <c>doubleCanonicalMap</c>, <c>floatCanonicalMap</c>).
/// </summary>
/// <remarks>
/// <para>
/// The lexical grammar is XSD's and not the runtime's: an optional sign,
/// digits with an optional point, an optional exponent, or one of the four
/// special forms. The runtime's parser also accepts <c>Infinity</c>,
/// whitespace, thousands separators and more, so the grammar is checked here
/// first and the runtime is trusted only with the rounding, which is
/// round-to-nearest-even as §D.1's <c>floatingPointRound</c> requires.
/// </para>
/// <para>
/// The canonical form is scientific with one digit before the point, the
/// shortest mantissa that round-trips, and an exponent with no plus sign and
/// no leading zeros: <c>1.0E0</c>, <c>-1.5E-3</c>. The runtime's shortest
/// round-trip formatting supplies the digits; this rewrites their shape.
/// </para>
/// </remarks>
internal static class FloatingPoint
{
    internal enum Special
    {
        None,
        PositiveInfinity,
        NegativeInfinity,
        NotANumber,
    }

    /// <summary>
    /// Whether the input matches XSD's numeric lexical grammar, or one of the
    /// special forms.
    /// </summary>
    internal static bool IsLexical(ReadOnlySpan<byte> utf8, out Special special)
    {
        special = Special.None;

        if (utf8.SequenceEqual("INF"u8) || utf8.SequenceEqual("+INF"u8))
        {
            special = Special.PositiveInfinity;
            return true;
        }

        if (utf8.SequenceEqual("-INF"u8))
        {
            special = Special.NegativeInfinity;
            return true;
        }

        if (utf8.SequenceEqual("NaN"u8))
        {
            special = Special.NotANumber;
            return true;
        }

        int i = 0;

        if (i < utf8.Length && (utf8[i] == (byte)'+' || utf8[i] == (byte)'-'))
        {
            i++;
        }

        int digits = 0;

        while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
        {
            i++;
            digits++;
        }

        if (i < utf8.Length && utf8[i] == (byte)'.')
        {
            i++;

            while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < utf8.Length && (utf8[i] == (byte)'e' || utf8[i] == (byte)'E'))
        {
            i++;

            if (i < utf8.Length && (utf8[i] == (byte)'+' || utf8[i] == (byte)'-'))
            {
                i++;
            }

            int exponentDigits = 0;

            while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                return false;
            }
        }

        return i == utf8.Length;
    }

    /// <summary>
    /// Whether the input is a canonical form: <c>d.dddE-e</c> with a single
    /// non-zero leading digit (or <c>0.0E0</c>), at least one fractional digit
    /// and no trailing zero beyond the first, an unsigned or negative exponent
    /// with no leading zeros, or a special form.
    /// </summary>
    internal static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        if (lexical.SequenceEqual("INF"u8) || lexical.SequenceEqual("-INF"u8) || lexical.SequenceEqual("NaN"u8))
        {
            return true;
        }

        int i = 0;

        if (i < lexical.Length && lexical[i] == (byte)'-')
        {
            i++;
        }

        if (i >= lexical.Length || !Lexical.IsDigit(lexical[i]))
        {
            return false;
        }

        bool zero = lexical[i] == (byte)'0';
        i++;

        if (i >= lexical.Length || lexical[i] != (byte)'.')
        {
            return false;
        }

        i++;
        int fractionStart = i;

        while (i < lexical.Length && Lexical.IsDigit(lexical[i]))
        {
            i++;
        }

        int fractionLength = i - fractionStart;

        if (fractionLength == 0 || (fractionLength > 1 && lexical[i - 1] == (byte)'0'))
        {
            return false;
        }

        if (zero && (fractionLength != 1 || lexical[fractionStart] != (byte)'0'))
        {
            return false;
        }

        if (i >= lexical.Length || lexical[i] != (byte)'E')
        {
            return false;
        }

        i++;

        if (i < lexical.Length && lexical[i] == (byte)'-')
        {
            i++;
        }

        int exponentStart = i;

        while (i < lexical.Length && Lexical.IsDigit(lexical[i]))
        {
            i++;
        }

        int exponentLength = i - exponentStart;

        if (exponentLength == 0 || (exponentLength > 1 && lexical[exponentStart] == (byte)'0'))
        {
            return false;
        }

        if (zero && !lexical[exponentStart..i].SequenceEqual("0"u8))
        {
            return false;
        }

        return i == lexical.Length;
    }

    internal static bool TryParseDouble(ReadOnlySpan<byte> utf8, out double value)
    {
        if (!IsLexical(utf8, out Special special))
        {
            value = 0;
            return false;
        }

        switch (special)
        {
            case Special.PositiveInfinity:
                value = double.PositiveInfinity;
                return true;
            case Special.NegativeInfinity:
                value = double.NegativeInfinity;
                return true;
            case Special.NotANumber:
                value = double.NaN;
                return true;
            default:
                return double.TryParse(utf8, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    internal static bool TryParseSingle(ReadOnlySpan<byte> utf8, out float value)
    {
        if (!IsLexical(utf8, out Special special))
        {
            value = 0;
            return false;
        }

        switch (special)
        {
            case Special.PositiveInfinity:
                value = float.PositiveInfinity;
                return true;
            case Special.NegativeInfinity:
                value = float.NegativeInfinity;
                return true;
            case Special.NotANumber:
                value = float.NaN;
                return true;
            default:
                return float.TryParse(utf8, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    internal static bool TryFormatDouble(double value, Span<byte> destination, out int written)
    {
        if (TryFormatSpecial(double.IsNaN(value), double.IsPositiveInfinity(value), double.IsNegativeInfinity(value), destination, out written))
        {
            return true;
        }

        if (value == 0)
        {
            return WriteZero(double.IsNegative(value), destination, out written);
        }

        Span<byte> shortest = stackalloc byte[64];

        return value.TryFormat(shortest, out int length, "R", CultureInfo.InvariantCulture)
            && Canonicalise(shortest[..length], destination, out written);
    }

    internal static bool TryFormatSingle(float value, Span<byte> destination, out int written)
    {
        if (TryFormatSpecial(float.IsNaN(value), float.IsPositiveInfinity(value), float.IsNegativeInfinity(value), destination, out written))
        {
            return true;
        }

        if (value == 0)
        {
            return WriteZero(float.IsNegative(value), destination, out written);
        }

        Span<byte> shortest = stackalloc byte[64];

        return value.TryFormat(shortest, out int length, "R", CultureInfo.InvariantCulture)
            && Canonicalise(shortest[..length], destination, out written);
    }

    private static bool TryFormatSpecial(bool nan, bool positive, bool negative, Span<byte> destination, out int written)
    {
        ReadOnlySpan<byte> text = nan ? "NaN"u8 : positive ? "INF"u8 : negative ? "-INF"u8 : default;
        written = 0;

        if (text.IsEmpty)
        {
            return false;
        }

        if (text.Length > destination.Length)
        {
            // Reported as handled, with nothing written: a caller with too
            // small a buffer gets false from the outer call either way.
            return false;
        }

        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    private static bool WriteZero(bool negative, Span<byte> destination, out int written)
    {
        ReadOnlySpan<byte> text = negative ? "-0.0E0"u8 : "0.0E0"u8;
        written = 0;

        if (text.Length > destination.Length)
        {
            return false;
        }

        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    /// <summary>
    /// Rewrites the runtime's shortest round-trip form — digits, an optional
    /// point, an optional <c>E±dd</c> — into XSD's canonical scientific form.
    /// </summary>
    private static bool Canonicalise(ReadOnlySpan<byte> shortest, Span<byte> destination, out int written)
    {
        written = 0;
        int i = 0;
        bool negative = false;

        if (shortest[i] == (byte)'-')
        {
            negative = true;
            i++;
        }

        // Collect the significant digits and note where the point fell.
        Span<byte> digits = stackalloc byte[32];
        int count = 0;
        int integralDigits = 0;
        bool pointSeen = false;

        for (; i < shortest.Length && shortest[i] != (byte)'E' && shortest[i] != (byte)'e'; i++)
        {
            if (shortest[i] == (byte)'.')
            {
                pointSeen = true;
                continue;
            }

            digits[count++] = shortest[i];

            if (!pointSeen)
            {
                integralDigits++;
            }
        }

        int exponent = 0;

        if (i < shortest.Length)
        {
            i++;
            bool exponentNegative = false;

            if (shortest[i] == (byte)'+' || shortest[i] == (byte)'-')
            {
                exponentNegative = shortest[i] == (byte)'-';
                i++;
            }

            for (; i < shortest.Length; i++)
            {
                exponent = (exponent * 10) + Lexical.Digit(shortest[i]);
            }

            if (exponentNegative)
            {
                exponent = -exponent;
            }
        }

        // The value is 0.d₁d₂…dₙ × 10^(integralDigits + exponent). Strip leading
        // zeros, which move the point, and trailing zeros, which do not.
        int leading = 0;

        while (leading < count - 1 && digits[leading] == (byte)'0')
        {
            leading++;
        }

        int trailing = count;

        while (trailing > leading + 1 && digits[trailing - 1] == (byte)'0')
        {
            trailing--;
        }

        int decimalExponent = integralDigits + exponent - leading - 1;
        int significant = trailing - leading;

        // sign, first digit, '.', fraction (at least one digit), 'E', exponent.
        int needed = (negative ? 1 : 0) + 1 + 1 + Math.Max(1, significant - 1) + 1 + 11;

        if (destination.Length < needed)
        {
            return false;
        }

        int w = 0;

        if (negative)
        {
            destination[w++] = (byte)'-';
        }

        destination[w++] = digits[leading];
        destination[w++] = (byte)'.';

        if (significant == 1)
        {
            destination[w++] = (byte)'0';
        }
        else
        {
            for (int d = leading + 1; d < trailing; d++)
            {
                destination[w++] = digits[d];
            }
        }

        destination[w++] = (byte)'E';

        if (decimalExponent < 0)
        {
            destination[w++] = (byte)'-';
            decimalExponent = -decimalExponent;
        }

        Lexical.TryWriteUnsigned((ulong)decimalExponent, destination[w..], out int exponentDigits);
        written = w + exponentDigits;
        return true;
    }
}
