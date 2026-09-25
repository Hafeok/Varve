// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:double</c> value (XML Schema 1.1 Part 2 §3.3.5): IEEE 754
/// binary64, with XSD's lexical grammar and canonical form.
/// </summary>
/// <remarks>
/// Equality and order are IEEE's, so <c>NaN</c> is equal to nothing and
/// ordered against nothing; <see cref="Compare"/> says so rather than picking
/// a side, and <see cref="CompareTo"/> exists for containers and orders
/// <c>NaN</c> first as the runtime does.
/// </remarks>
public readonly struct XsdDouble : IEquatable<XsdDouble>, IComparable<XsdDouble>
{
    /// <summary>Wraps a value.</summary>
    public XsdDouble(double value) => Value = value;

    /// <summary>The value.</summary>
    public double Value { get; }

    /// <summary>Whether the value is <c>NaN</c>.</summary>
    public bool IsNaN => double.IsNaN(Value);

    /// <summary>Parses a <c>doubleRep</c> (§3.3.5.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdDouble value)
    {
        bool ok = FloatingPoint.TryParseDouble(utf8, out double parsed);
        value = new XsdDouble(parsed);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdDouble)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdDouble value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one (<c>doubleCanonicalMap</c>).</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical) => FloatingPoint.IsCanonical(lexical);

    /// <summary>Writes the canonical form: <c>1.0E0</c>, <c>-0.0E0</c>, <c>INF</c>, <c>NaN</c>.</summary>
    public bool TryFormat(Span<byte> destination, out int written) =>
        FloatingPoint.TryFormatDouble(Value, destination, out written);

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>
    /// IEEE order: <see cref="PartialOrdering.Indeterminate"/> when either
    /// side is <c>NaN</c>, which is what <c>op:numeric-less-than</c> reports
    /// as false in both directions.
    /// </summary>
    public static PartialOrdering Compare(XsdDouble left, XsdDouble right)
    {
        if (double.IsNaN(left.Value) || double.IsNaN(right.Value))
        {
            return PartialOrdering.Indeterminate;
        }

        return left.Value < right.Value ? PartialOrdering.Less
            : left.Value > right.Value ? PartialOrdering.Greater
            : PartialOrdering.Equal;
    }

    /// <summary>A total order for containers, with <c>NaN</c> first; not the operator's.</summary>
    public int CompareTo(XsdDouble other) => Value.CompareTo(other.Value);

    /// <summary>IEEE equality: <c>NaN</c> equals nothing, <c>0.0</c> equals <c>-0.0</c>.</summary>
    public bool Equals(XsdDouble other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdDouble other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value == 0 ? 0 : Value.GetHashCode();

    /// <summary>IEEE equality.</summary>
    public static bool operator ==(XsdDouble left, XsdDouble right) => left.Equals(right);

    /// <summary>IEEE inequality.</summary>
    public static bool operator !=(XsdDouble left, XsdDouble right) => !left.Equals(right);

    /// <summary>IEEE order.</summary>
    public static bool operator <(XsdDouble left, XsdDouble right) => left.Value < right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator >(XsdDouble left, XsdDouble right) => left.Value > right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator <=(XsdDouble left, XsdDouble right) => left.Value <= right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator >=(XsdDouble left, XsdDouble right) => left.Value >= right.Value;
}
