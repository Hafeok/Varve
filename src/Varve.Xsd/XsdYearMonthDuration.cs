// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:yearMonthDuration</c> value (XML Schema 1.1 Part 2 §3.4.26): a
/// duration whose seconds are zero, and therefore totally ordered.
/// </summary>
public readonly struct XsdYearMonthDuration : IEquatable<XsdYearMonthDuration>, IComparable<XsdYearMonthDuration>
{
    /// <summary>Wraps a number of months.</summary>
    public XsdYearMonthDuration(long months) => Months = months;

    /// <summary>The <c>months</c> property.</summary>
    public long Months { get; }

    /// <summary>Parses a <c>yearMonthDurationLexicalRep</c> (§3.4.26.1).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdYearMonthDuration value)
    {
        bool ok = DurationLexical.TryParse(utf8, allowYearMonth: true, allowDayTime: false, out long months, out _);
        value = new XsdYearMonthDuration(months);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdYearMonthDuration)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdYearMonthDuration value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one.</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        if (!TryParse(lexical, out XsdYearMonthDuration value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Lexical.StackLimit];
        return value.TryFormat(buffer, out int written) && buffer[..written].SequenceEqual(lexical);
    }

    /// <summary>Writes the canonical form: <c>P2Y1M</c>, <c>-P1M</c>, <c>P0M</c>.</summary>
    public bool TryFormat(Span<byte> destination, out int written)
    {
        if (Months != 0)
        {
            return DurationLexical.TryFormat(Months, XsdDecimal.Zero, destination, out written);
        }

        // yearMonthDurationCanonicalMap writes the months fragment even at zero.
        written = 0;

        if (destination.Length < 3)
        {
            return false;
        }

        "P0M"u8.CopyTo(destination);
        written = 3;
        return true;
    }

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>Sum; false on overflow.</summary>
    public static bool TryAdd(XsdYearMonthDuration left, XsdYearMonthDuration right, out XsdYearMonthDuration result)
    {
        bool ok = XsdInteger.TryAdd(new XsdInteger(left.Months), new XsdInteger(right.Months), out XsdInteger sum);
        result = new XsdYearMonthDuration(sum.Value);
        return ok;
    }

    /// <summary>Difference; false on overflow.</summary>
    public static bool TrySubtract(XsdYearMonthDuration left, XsdYearMonthDuration right, out XsdYearMonthDuration result)
    {
        bool ok = XsdInteger.TrySubtract(new XsdInteger(left.Months), new XsdInteger(right.Months), out XsdInteger difference);
        result = new XsdYearMonthDuration(difference.Value);
        return ok;
    }

    /// <summary>Total order, by months.</summary>
    public int CompareTo(XsdYearMonthDuration other) => Months.CompareTo(other.Months);

    /// <inheritdoc />
    public bool Equals(XsdYearMonthDuration other) => Months == other.Months;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdYearMonthDuration other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Months.GetHashCode();

    /// <summary>Equality.</summary>
    public static bool operator ==(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months == right.Months;

    /// <summary>Inequality.</summary>
    public static bool operator !=(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months != right.Months;

    /// <summary>Order.</summary>
    public static bool operator <(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months < right.Months;

    /// <summary>Order.</summary>
    public static bool operator >(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months > right.Months;

    /// <summary>Order.</summary>
    public static bool operator <=(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months <= right.Months;

    /// <summary>Order.</summary>
    public static bool operator >=(XsdYearMonthDuration left, XsdYearMonthDuration right) => left.Months >= right.Months;
}
