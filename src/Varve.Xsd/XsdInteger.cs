// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:integer</c> value, and the value of every datatype derived from
/// it (XML Schema 1.1 Part 2 §3.4.13–§3.4.25).
/// </summary>
/// <remarks>
/// <para>
/// A checked <see cref="long"/>, by ADR 0051. XML Schema's integer is
/// unbounded; a lexical form outside ±2⁶³ fails to parse as a value and
/// remains a valid RDF term, and arithmetic that overflows fails rather than
/// wrapping, which XPath Functions and Operators §4.2 permits for
/// limited-precision integers. Promotion to <see cref="XsdDecimal"/> never
/// fails, which is why the width is 64 bits and not 128.
/// </para>
/// <para>
/// The derived types are ranges over the same value, not separate types:
/// <see cref="TryParse(ReadOnlySpan{byte}, XsdDatatype, out XsdInteger)"/>
/// checks the range the named datatype fixes, and <c>xsd:unsignedLong</c>'s
/// upper half is out of range for the same reason the base type's is.
/// </para>
/// </remarks>
public readonly struct XsdInteger : IEquatable<XsdInteger>, IComparable<XsdInteger>
{
    /// <summary>Wraps a value.</summary>
    public XsdInteger(long value) => Value = value;

    /// <summary>The value.</summary>
    public long Value { get; }

    /// <summary>Zero.</summary>
    public static XsdInteger Zero => default;

    /// <summary>Whether the value is negative.</summary>
    public bool IsNegative => Value < 0;

    /// <summary>Whether the value is zero.</summary>
    public bool IsZero => Value == 0;

    // --- lexical mapping ----------------------------------------------------

    /// <summary>
    /// Parses an <c>xsd:integer</c> lexical form (§3.4.13.1: an optional sign
    /// and one or more digits). False when the form is ill-formed or the
    /// value does not fit.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdInteger value) =>
        TryParse(utf8, XsdDatatype.Integer, out value);

    /// <summary>
    /// Parses a lexical form of <paramref name="datatype"/>, which must be
    /// <c>xsd:integer</c> or one of its derived types, and checks the range
    /// that datatype fixes.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, XsdDatatype datatype, out XsdInteger value)
    {
        value = default;

        if (!XsdDatatypes.IsIntegerType(datatype) || !TryParseCore(utf8, out long parsed))
        {
            return false;
        }

        (long minimum, long maximum) = Range(datatype);

        if (parsed < minimum || parsed > maximum)
        {
            return false;
        }

        value = new XsdInteger(parsed);
        return true;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdInteger)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdInteger value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, XsdDatatype, out XsdInteger)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, XsdDatatype datatype, out XsdInteger value) =>
        Lexical.ParseChars(
            text,
            datatype,
            static (ReadOnlySpan<byte> utf8, XsdDatatype d, out XsdInteger v) => TryParse(utf8, d, out v),
            out value);

    private static bool TryParseCore(ReadOnlySpan<byte> utf8, out long value)
    {
        value = 0;

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

        if (i == utf8.Length)
        {
            return false;
        }

        // Accumulate the magnitude as a negative number so that -2^63 fits.
        long magnitude = 0;

        for (; i < utf8.Length; i++)
        {
            byte b = utf8[i];

            if (!Lexical.IsDigit(b))
            {
                return false;
            }

            int digit = Lexical.Digit(b);

            if (magnitude < (long.MinValue + digit) / 10)
            {
                return false;
            }

            magnitude = (magnitude * 10) - digit;
        }

        if (!negative)
        {
            if (magnitude == long.MinValue)
            {
                return false;
            }

            magnitude = -magnitude;
        }

        value = magnitude;
        return true;
    }

    /// <summary>
    /// The value space bounds of an integer datatype (§3.4.13–§3.4.25), as
    /// far as a <see cref="long"/> reaches: <c>xsd:unsignedLong</c>'s upper
    /// bound is <see cref="long.MaxValue"/> here, not 2⁶⁴ − 1.
    /// </summary>
    public static (long Minimum, long Maximum) Range(XsdDatatype datatype) => datatype switch
    {
        XsdDatatype.Integer => (long.MinValue, long.MaxValue),
        XsdDatatype.NonPositiveInteger => (long.MinValue, 0),
        XsdDatatype.NegativeInteger => (long.MinValue, -1),
        XsdDatatype.Long => (long.MinValue, long.MaxValue),
        XsdDatatype.Int => (int.MinValue, int.MaxValue),
        XsdDatatype.Short => (short.MinValue, short.MaxValue),
        XsdDatatype.Byte => (sbyte.MinValue, sbyte.MaxValue),
        XsdDatatype.NonNegativeInteger => (0, long.MaxValue),
        XsdDatatype.UnsignedLong => (0, long.MaxValue),
        XsdDatatype.UnsignedInt => (0, uint.MaxValue),
        XsdDatatype.UnsignedShort => (0, ushort.MaxValue),
        XsdDatatype.UnsignedByte => (0, byte.MaxValue),
        XsdDatatype.PositiveInteger => (1, long.MaxValue),
        _ => throw new ArgumentOutOfRangeException(nameof(datatype), datatype, "Not an integer datatype."),
    };

    /// <summary>
    /// Whether a lexical form is the canonical one (§3.4.13.2): no sign but a
    /// minus, no leading zeros, and zero written <c>0</c> and never <c>-0</c>.
    /// Judged on the string, so a form too long to be a value is still judged.
    /// </summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        int start = !lexical.IsEmpty && lexical[0] == (byte)'-' ? 1 : 0;
        ReadOnlySpan<byte> digits = lexical[start..];

        if (digits.IsEmpty)
        {
            return false;
        }

        if (digits[0] == (byte)'0')
        {
            return digits.Length == 1 && start == 0;
        }

        foreach (byte b in digits)
        {
            if (!Lexical.IsDigit(b))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes the canonical form.</summary>
    public bool TryFormat(Span<byte> destination, out int written)
    {
        written = 0;
        int i = 0;
        ulong magnitude;

        if (Value < 0)
        {
            if (destination.IsEmpty)
            {
                return false;
            }

            destination[0] = (byte)'-';
            i = 1;
            magnitude = unchecked((ulong)(-(Value + 1))) + 1;
        }
        else
        {
            magnitude = (ulong)Value;
        }

        if (!Lexical.TryWriteUnsigned(magnitude, destination[i..], out int digits))
        {
            return false;
        }

        written = i + digits;
        return true;
    }

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    // --- arithmetic, checked ------------------------------------------------

    /// <summary><c>op:numeric-add</c>; false on overflow.</summary>
    public static bool TryAdd(XsdInteger left, XsdInteger right, out XsdInteger result)
    {
        long sum = unchecked(left.Value + right.Value);
        result = new XsdInteger(sum);
        return ((left.Value ^ sum) & (right.Value ^ sum)) >= 0;
    }

    /// <summary><c>op:numeric-subtract</c>; false on overflow.</summary>
    public static bool TrySubtract(XsdInteger left, XsdInteger right, out XsdInteger result)
    {
        long difference = unchecked(left.Value - right.Value);
        result = new XsdInteger(difference);
        return ((left.Value ^ right.Value) & (left.Value ^ difference)) >= 0;
    }

    /// <summary><c>op:numeric-multiply</c>; false on overflow.</summary>
    public static bool TryMultiply(XsdInteger left, XsdInteger right, out XsdInteger result)
    {
        long high = Math.BigMul(left.Value, right.Value, out long low);
        result = new XsdInteger(low);
        return high == (low >> 63);
    }

    /// <summary>
    /// Integer division truncating toward zero (<c>op:numeric-integer-divide</c>);
    /// false on a zero divisor or on the one overflowing quotient.
    /// </summary>
    public static bool TryDivide(XsdInteger left, XsdInteger right, out XsdInteger result)
    {
        if (right.Value == 0 || (left.Value == long.MinValue && right.Value == -1))
        {
            result = default;
            return false;
        }

        result = new XsdInteger(left.Value / right.Value);
        return true;
    }

    /// <summary><c>op:numeric-unary-minus</c>; false for the minimum value.</summary>
    public static bool TryNegate(XsdInteger value, out XsdInteger result)
    {
        result = new XsdInteger(unchecked(-value.Value));
        return value.Value != long.MinValue;
    }

    /// <summary><c>fn:abs</c>; false for the minimum value.</summary>
    public static bool TryAbs(XsdInteger value, out XsdInteger result)
    {
        if (value.Value < 0)
        {
            return TryNegate(value, out result);
        }

        result = value;
        return true;
    }

    /// <summary>Adds; throws on overflow.</summary>
    public static XsdInteger operator +(XsdInteger left, XsdInteger right) =>
        TryAdd(left, right, out XsdInteger result) ? result : throw new OverflowException();

    /// <summary>Subtracts; throws on overflow.</summary>
    public static XsdInteger operator -(XsdInteger left, XsdInteger right) =>
        TrySubtract(left, right, out XsdInteger result) ? result : throw new OverflowException();

    /// <summary>Multiplies; throws on overflow.</summary>
    public static XsdInteger operator *(XsdInteger left, XsdInteger right) =>
        TryMultiply(left, right, out XsdInteger result) ? result : throw new OverflowException();

    /// <summary>Negates; throws for the minimum value.</summary>
    public static XsdInteger operator -(XsdInteger value) =>
        TryNegate(value, out XsdInteger result) ? result : throw new OverflowException();

    /// <summary>The named form of <c>+</c>.</summary>
    public static XsdInteger Add(XsdInteger left, XsdInteger right) => left + right;

    /// <summary>The named form of binary <c>-</c>.</summary>
    public static XsdInteger Subtract(XsdInteger left, XsdInteger right) => left - right;

    /// <summary>The named form of <c>*</c>.</summary>
    public static XsdInteger Multiply(XsdInteger left, XsdInteger right) => left * right;

    /// <summary>The named form of unary <c>-</c>.</summary>
    public static XsdInteger Negate(XsdInteger value) => -value;

    // --- order --------------------------------------------------------------

    /// <inheritdoc />
    public int CompareTo(XsdInteger other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(XsdInteger other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdInteger other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Value equality.</summary>
    public static bool operator ==(XsdInteger left, XsdInteger right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(XsdInteger left, XsdInteger right) => !left.Equals(right);

    /// <summary>Numeric order.</summary>
    public static bool operator <(XsdInteger left, XsdInteger right) => left.Value < right.Value;

    /// <summary>Numeric order.</summary>
    public static bool operator >(XsdInteger left, XsdInteger right) => left.Value > right.Value;

    /// <summary>Numeric order.</summary>
    public static bool operator <=(XsdInteger left, XsdInteger right) => left.Value <= right.Value;

    /// <summary>Numeric order.</summary>
    public static bool operator >=(XsdInteger left, XsdInteger right) => left.Value >= right.Value;
}
