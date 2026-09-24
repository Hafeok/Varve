// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:decimal</c> value (XML Schema 1.1 Part 2 §3.3.3): exact, as a
/// fixed-point <see cref="Int128"/> with <see cref="Scale"/> fractional digits.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0051's precision policy. The value is <c>mantissa × 10⁻¹⁸</c>, so the
/// representable set is the multiples of 10⁻¹⁸ with an integral part below
/// about ±1.7 × 10²⁰. A lexical form that needs more fractional digits, or
/// more integral range, fails to parse as a value and remains a valid RDF
/// term. Arithmetic that overflows fails: the <c>Try</c> forms return false and
/// the operators throw, which is the dynamic error XPath Functions and
/// Operators §4.2 requires of decimal overflow. Division is exact to the
/// eighteenth digit and truncates toward zero at the nineteenth.
/// </para>
/// <para>
/// No allocation anywhere: parsing, formatting and arithmetic work on the
/// stack, and the 256-bit intermediates a multiply or divide can need are
/// two <see cref="UInt128"/>s.
/// </para>
/// </remarks>
public readonly struct XsdDecimal : IEquatable<XsdDecimal>, IComparable<XsdDecimal>
{
    /// <summary>The number of fractional digits every value carries.</summary>
    public const int Scale = 18;

    private static readonly Int128 ScaleFactor = Pow10(Scale);

    private static readonly UInt128 ScaleFactorUnsigned = (UInt128)ScaleFactor;

    private XsdDecimal(Int128 mantissa) => Mantissa = mantissa;

    /// <summary>Zero.</summary>
    public static XsdDecimal Zero => default;

    /// <summary>One.</summary>
    public static XsdDecimal One => new(ScaleFactor);

    /// <summary>The largest representable value.</summary>
    public static XsdDecimal MaxValue => new(Int128.MaxValue);

    /// <summary>The smallest representable value.</summary>
    public static XsdDecimal MinValue => new(Int128.MinValue);

    /// <summary>The value scaled by 10¹⁸: the exact integer this struct holds.</summary>
    public Int128 Mantissa { get; }

    /// <summary>Whether the value is negative.</summary>
    public bool IsNegative => Mantissa < 0;

    /// <summary>Whether the value is zero.</summary>
    public bool IsZero => Mantissa == 0;

    /// <summary>Whether the value has no fractional part.</summary>
    public bool IsInteger => Mantissa % ScaleFactor == 0;

    /// <summary>The value of an integer, which always fits (ADR 0051).</summary>
    public static XsdDecimal FromInteger(XsdInteger value) => new((Int128)value.Value * ScaleFactor);

    /// <summary>The value of a <see cref="long"/>, which always fits.</summary>
    public static XsdDecimal FromInt64(long value) => new((Int128)value * ScaleFactor);

    /// <summary>A value from its mantissa, for a caller that has one.</summary>
    public static XsdDecimal FromMantissa(Int128 mantissa) => new(mantissa);

    /// <summary>
    /// The integral part, truncated toward zero, when it fits a
    /// <see cref="long"/>. XPath's cast from decimal to integer.
    /// </summary>
    public bool TryToInteger(out XsdInteger value)
    {
        Int128 integral = Mantissa / ScaleFactor;

        if (integral < long.MinValue || integral > long.MaxValue)
        {
            value = default;
            return false;
        }

        value = new XsdInteger((long)integral);
        return true;
    }

    /// <summary>The nearest <see cref="double"/>.</summary>
    public double ToDouble() => (double)Mantissa / 1e18;

    /// <summary>The nearest <see cref="float"/>.</summary>
    public float ToSingle() => (float)ToDouble();

    // --- lexical mapping ----------------------------------------------------

    /// <summary>
    /// Parses a <c>decimalLexicalRep</c> (§3.3.3.1): an optional sign, digits
    /// with an optional point among them, at least one digit in all. False
    /// when ill-formed, when more than eighteen fractional digits are
    /// significant, or when the integral part does not fit.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdDecimal value)
    {
        value = default;

        if (utf8.IsEmpty)
        {
            return false;
        }

        bool negative = false;
        int i = 0;

        if (utf8[0] == (byte)'+' || utf8[0] == (byte)'-')
        {
            negative = utf8[0] == (byte)'-';
            i = 1;
        }

        UInt128 limit = negative ? (UInt128)Int128.MaxValue + 1 : (UInt128)Int128.MaxValue;
        UInt128 mantissa = 0;
        int digits = 0;
        int fractionDigits = 0;
        bool inFraction = false;

        for (; i < utf8.Length; i++)
        {
            byte b = utf8[i];

            if (b == (byte)'.')
            {
                if (inFraction)
                {
                    return false;
                }

                inFraction = true;
                continue;
            }

            if (!Lexical.IsDigit(b))
            {
                return false;
            }

            digits++;

            if (inFraction)
            {
                if (fractionDigits == Scale)
                {
                    // A nineteenth fractional digit is representable only when
                    // it, and every one after it, is zero.
                    if (b != (byte)'0')
                    {
                        return false;
                    }

                    continue;
                }

                fractionDigits++;
            }

            UInt128 digit = (UInt128)Lexical.Digit(b);

            if (mantissa > (limit - digit) / 10)
            {
                return false;
            }

            mantissa = (mantissa * 10) + digit;
        }

        if (digits == 0)
        {
            return false;
        }

        for (; fractionDigits < Scale; fractionDigits++)
        {
            if (mantissa > limit / 10)
            {
                return false;
            }

            mantissa *= 10;
        }

        return FromMagnitude(mantissa, negative, out value);
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdDecimal)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdDecimal value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>
    /// Whether a lexical form is the canonical one (<c>decimalCanonicalMap</c>,
    /// §D.1.1): no plus sign, no leading zeros beyond a single zero before
    /// the point, no point at all for an integer, no trailing zeros after it,
    /// and zero written <c>0</c>. Judged on the string alone.
    /// </summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        int start = !lexical.IsEmpty && lexical[0] == (byte)'-' ? 1 : 0;
        ReadOnlySpan<byte> rest = lexical[start..];
        int point = rest.IndexOf((byte)'.');
        ReadOnlySpan<byte> integral = point < 0 ? rest : rest[..point];
        ReadOnlySpan<byte> fraction = point < 0 ? default : rest[(point + 1)..];

        if (integral.IsEmpty || (integral[0] == (byte)'0' && integral.Length > 1))
        {
            return false;
        }

        foreach (byte b in integral)
        {
            if (!Lexical.IsDigit(b))
            {
                return false;
            }
        }

        if (point >= 0)
        {
            if (fraction.IsEmpty || fraction[^1] == (byte)'0')
            {
                return false;
            }

            foreach (byte b in fraction)
            {
                if (!Lexical.IsDigit(b))
                {
                    return false;
                }
            }
        }

        // -0 and -0.0 are not canonical; -0.5 is.
        return start == 0 || !(integral[0] == (byte)'0' && integral.Length == 1 && point < 0);
    }

    /// <summary>Writes the canonical form (<c>decimalCanonicalMap</c>).</summary>
    public bool TryFormat(Span<byte> destination, out int written)
    {
        written = 0;
        int i = 0;
        UInt128 magnitude;

        if (Mantissa < 0)
        {
            if (destination.IsEmpty)
            {
                return false;
            }

            destination[0] = (byte)'-';
            i = 1;
            magnitude = (UInt128)(-(Mantissa + 1)) + 1;
        }
        else
        {
            magnitude = (UInt128)Mantissa;
        }

        UInt128 integral = magnitude / ScaleFactorUnsigned;
        ulong fraction = (ulong)(magnitude % ScaleFactorUnsigned);

        if (!TryWriteUInt128(integral, destination[i..], out int integralDigits))
        {
            return false;
        }

        i += integralDigits;

        if (fraction != 0)
        {
            if (destination.Length < i + 1 + Scale)
            {
                return false;
            }

            destination[i++] = (byte)'.';
            Lexical.TryWritePadded(fraction, Scale, destination[i..], out _);
            int end = i + Scale;

            while (destination[end - 1] == (byte)'0')
            {
                end--;
            }

            i = end;
        }

        written = i;
        return true;
    }

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    private static bool TryWriteUInt128(UInt128 value, Span<byte> destination, out int written)
    {
        Span<byte> digits = stackalloc byte[40];
        int count = 0;

        do
        {
            digits[count++] = (byte)('0' + (int)(value % 10));
            value /= 10;
        }
        while (value != 0);

        if (count > destination.Length)
        {
            written = 0;
            return false;
        }

        for (int j = 0; j < count; j++)
        {
            destination[j] = digits[count - 1 - j];
        }

        written = count;
        return true;
    }

    // --- arithmetic ---------------------------------------------------------

    /// <summary><c>op:numeric-add</c>; false on overflow.</summary>
    public static bool TryAdd(XsdDecimal left, XsdDecimal right, out XsdDecimal result)
    {
        Int128 sum = unchecked(left.Mantissa + right.Mantissa);
        result = new XsdDecimal(sum);
        return ((left.Mantissa ^ sum) & (right.Mantissa ^ sum)) >= 0;
    }

    /// <summary><c>op:numeric-subtract</c>; false on overflow.</summary>
    public static bool TrySubtract(XsdDecimal left, XsdDecimal right, out XsdDecimal result)
    {
        Int128 difference = unchecked(left.Mantissa - right.Mantissa);
        result = new XsdDecimal(difference);
        return ((left.Mantissa ^ right.Mantissa) & (left.Mantissa ^ difference)) >= 0;
    }

    /// <summary><c>op:numeric-multiply</c>, exact; false on overflow.</summary>
    public static bool TryMultiply(XsdDecimal left, XsdDecimal right, out XsdDecimal result)
    {
        bool negative = (left.Mantissa < 0) != (right.Mantissa < 0);
        UInt128 a = Magnitude(left.Mantissa);
        UInt128 b = Magnitude(right.Mantissa);
        UInt128 quotient;

        if (a == 0 || b == 0)
        {
            result = default;
            return true;
        }

        if (a <= UInt128.MaxValue / b)
        {
            quotient = (a * b) / ScaleFactorUnsigned;
        }
        else
        {
            (UInt128 high, UInt128 low) = Int256.Multiply(a, b);

            if (!Int256.TryDivide(high, low, ScaleFactorUnsigned, out quotient))
            {
                result = default;
                return false;
            }
        }

        return FromMagnitude(quotient, negative, out result);
    }

    /// <summary>
    /// <c>op:numeric-divide</c>, exact to eighteen fractional digits and
    /// truncated toward zero at the nineteenth; false on a zero divisor or on
    /// overflow.
    /// </summary>
    public static bool TryDivide(XsdDecimal left, XsdDecimal right, out XsdDecimal result)
    {
        if (right.Mantissa == 0)
        {
            result = default;
            return false;
        }

        bool negative = (left.Mantissa < 0) != (right.Mantissa < 0);
        UInt128 a = Magnitude(left.Mantissa);
        UInt128 b = Magnitude(right.Mantissa);
        UInt128 quotient;

        if (a <= UInt128.MaxValue / ScaleFactorUnsigned)
        {
            quotient = (a * ScaleFactorUnsigned) / b;
        }
        else
        {
            (UInt128 high, UInt128 low) = Int256.Multiply(a, ScaleFactorUnsigned);

            if (!Int256.TryDivide(high, low, b, out quotient))
            {
                result = default;
                return false;
            }
        }

        return FromMagnitude(quotient, negative, out result);
    }

    /// <summary><c>op:numeric-unary-minus</c>; false for the minimum value.</summary>
    public static bool TryNegate(XsdDecimal value, out XsdDecimal result)
    {
        result = new XsdDecimal(unchecked(-value.Mantissa));
        return value.Mantissa != Int128.MinValue;
    }

    /// <summary><c>fn:abs</c>; false for the minimum value.</summary>
    public static bool TryAbs(XsdDecimal value, out XsdDecimal result)
    {
        if (value.Mantissa < 0)
        {
            return TryNegate(value, out result);
        }

        result = value;
        return true;
    }

    /// <summary><c>fn:floor</c>: the largest integer not greater than the value.</summary>
    public XsdDecimal Floor()
    {
        Int128 remainder = Mantissa % ScaleFactor;
        Int128 floor = Mantissa - remainder;
        return new XsdDecimal(remainder < 0 ? floor - ScaleFactor : floor);
    }

    /// <summary><c>fn:ceiling</c>: the smallest integer not less than the value.</summary>
    public XsdDecimal Ceiling()
    {
        Int128 remainder = Mantissa % ScaleFactor;
        Int128 floor = Mantissa - remainder;
        return new XsdDecimal(remainder > 0 ? floor + ScaleFactor : floor);
    }

    /// <summary>
    /// <c>fn:round</c>: the nearest integer, and of two equally near the one
    /// toward positive infinity. False when that integer does not fit.
    /// </summary>
    public bool TryRound(out XsdDecimal result)
    {
        Int128 half = ScaleFactor / 2;

        if (Mantissa > Int128.MaxValue - half)
        {
            result = default;
            return false;
        }

        result = new XsdDecimal(Mantissa + half).Floor();
        return true;
    }

    /// <summary>Adds; throws on overflow.</summary>
    public static XsdDecimal operator +(XsdDecimal left, XsdDecimal right) =>
        TryAdd(left, right, out XsdDecimal result) ? result : throw new OverflowException();

    /// <summary>Subtracts; throws on overflow.</summary>
    public static XsdDecimal operator -(XsdDecimal left, XsdDecimal right) =>
        TrySubtract(left, right, out XsdDecimal result) ? result : throw new OverflowException();

    /// <summary>Multiplies; throws on overflow.</summary>
    public static XsdDecimal operator *(XsdDecimal left, XsdDecimal right) =>
        TryMultiply(left, right, out XsdDecimal result) ? result : throw new OverflowException();

    /// <summary>Divides; throws on a zero divisor or overflow.</summary>
    public static XsdDecimal operator /(XsdDecimal left, XsdDecimal right) =>
        right.IsZero
            ? throw new DivideByZeroException()
            : TryDivide(left, right, out XsdDecimal result) ? result : throw new OverflowException();

    /// <summary>Negates; throws for the minimum value.</summary>
    public static XsdDecimal operator -(XsdDecimal value) =>
        TryNegate(value, out XsdDecimal result) ? result : throw new OverflowException();

    /// <summary>The named form of <c>+</c>.</summary>
    public static XsdDecimal Add(XsdDecimal left, XsdDecimal right) => left + right;

    /// <summary>The named form of binary <c>-</c>.</summary>
    public static XsdDecimal Subtract(XsdDecimal left, XsdDecimal right) => left - right;

    /// <summary>The named form of <c>*</c>.</summary>
    public static XsdDecimal Multiply(XsdDecimal left, XsdDecimal right) => left * right;

    /// <summary>The named form of <c>/</c>.</summary>
    public static XsdDecimal Divide(XsdDecimal left, XsdDecimal right) => left / right;

    /// <summary>The named form of unary <c>-</c>.</summary>
    public static XsdDecimal Negate(XsdDecimal value) => -value;

    private static UInt128 Magnitude(Int128 value) =>
        value < 0 ? (UInt128)(-(value + 1)) + 1 : (UInt128)value;

    private static bool FromMagnitude(UInt128 magnitude, bool negative, out XsdDecimal result)
    {
        UInt128 limit = negative ? (UInt128)Int128.MaxValue + 1 : (UInt128)Int128.MaxValue;

        if (magnitude > limit)
        {
            result = default;
            return false;
        }

        result = new XsdDecimal(negative ? unchecked(-(Int128)(magnitude - 1)) - 1 : (Int128)magnitude);
        return true;
    }

    private static Int128 Pow10(int exponent)
    {
        Int128 result = 1;

        for (int i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    // --- order --------------------------------------------------------------

    /// <inheritdoc />
    public int CompareTo(XsdDecimal other) => Mantissa.CompareTo(other.Mantissa);

    /// <inheritdoc />
    public bool Equals(XsdDecimal other) => Mantissa == other.Mantissa;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdDecimal other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Mantissa.GetHashCode();

    /// <summary>Value equality.</summary>
    public static bool operator ==(XsdDecimal left, XsdDecimal right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(XsdDecimal left, XsdDecimal right) => !left.Equals(right);

    /// <summary>Numeric order.</summary>
    public static bool operator <(XsdDecimal left, XsdDecimal right) => left.Mantissa < right.Mantissa;

    /// <summary>Numeric order.</summary>
    public static bool operator >(XsdDecimal left, XsdDecimal right) => left.Mantissa > right.Mantissa;

    /// <summary>Numeric order.</summary>
    public static bool operator <=(XsdDecimal left, XsdDecimal right) => left.Mantissa <= right.Mantissa;

    /// <summary>Numeric order.</summary>
    public static bool operator >=(XsdDecimal left, XsdDecimal right) => left.Mantissa >= right.Mantissa;
}
