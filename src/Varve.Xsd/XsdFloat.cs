// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:float</c> value (XML Schema 1.1 Part 2 §3.3.4): IEEE 754
/// binary32, with XSD's lexical grammar and canonical form.
/// </summary>
/// <remarks>See <see cref="XsdDouble"/>; the two differ only in width.</remarks>
public readonly struct XsdFloat : IEquatable<XsdFloat>, IComparable<XsdFloat>
{
    /// <summary>Wraps a value.</summary>
    public XsdFloat(float value) => Value = value;

    /// <summary>The value.</summary>
    public float Value { get; }

    /// <summary>Whether the value is <c>NaN</c>.</summary>
    public bool IsNaN => float.IsNaN(Value);

    /// <summary>Parses a <c>floatRep</c> (§3.3.4.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdFloat value)
    {
        bool ok = FloatingPoint.TryParseSingle(utf8, out float parsed);
        value = new XsdFloat(parsed);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdFloat)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdFloat value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one (<c>floatCanonicalMap</c>).</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical) => FloatingPoint.IsCanonical(lexical);

    /// <summary>Writes the canonical form.</summary>
    public bool TryFormat(Span<byte> destination, out int written) =>
        FloatingPoint.TryFormatSingle(Value, destination, out written);

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>IEEE order, <see cref="PartialOrdering.Indeterminate"/> against <c>NaN</c>.</summary>
    public static PartialOrdering Compare(XsdFloat left, XsdFloat right)
    {
        if (float.IsNaN(left.Value) || float.IsNaN(right.Value))
        {
            return PartialOrdering.Indeterminate;
        }

        return left.Value < right.Value ? PartialOrdering.Less
            : left.Value > right.Value ? PartialOrdering.Greater
            : PartialOrdering.Equal;
    }

    /// <summary>A total order for containers, with <c>NaN</c> first; not the operator's.</summary>
    public int CompareTo(XsdFloat other) => Value.CompareTo(other.Value);

    /// <summary>IEEE equality.</summary>
    public bool Equals(XsdFloat other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdFloat other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value == 0 ? 0 : Value.GetHashCode();

    /// <summary>IEEE equality.</summary>
    public static bool operator ==(XsdFloat left, XsdFloat right) => left.Equals(right);

    /// <summary>IEEE inequality.</summary>
    public static bool operator !=(XsdFloat left, XsdFloat right) => !left.Equals(right);

    /// <summary>IEEE order.</summary>
    public static bool operator <(XsdFloat left, XsdFloat right) => left.Value < right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator >(XsdFloat left, XsdFloat right) => left.Value > right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator <=(XsdFloat left, XsdFloat right) => left.Value <= right.Value;

    /// <summary>IEEE order.</summary>
    public static bool operator >=(XsdFloat left, XsdFloat right) => left.Value >= right.Value;
}
