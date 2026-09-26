// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:boolean</c> value (XML Schema 1.1 Part 2 §3.3.2): four lexical
/// forms, two canonical ones, and <c>false &lt; true</c>.
/// </summary>
public readonly struct XsdBoolean : IEquatable<XsdBoolean>, IComparable<XsdBoolean>
{
    /// <summary>Wraps a value.</summary>
    [DesignDecision(typeof(BoolValues.BoolParameterIsTheValue), Scope = ExceptionScope.Boundary)]
    public XsdBoolean(bool value) => Value = value;

    /// <summary>The value.</summary>
    public bool Value { get; }

    /// <summary><c>true</c>.</summary>
    public static XsdBoolean True => new(true);

    /// <summary><c>false</c>.</summary>
    public static XsdBoolean False => new(false);

    /// <summary>Parses <c>true</c>, <c>false</c>, <c>1</c> or <c>0</c> (§3.3.2.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdBoolean value)
    {
        if (utf8.SequenceEqual("true"u8) || utf8.SequenceEqual("1"u8))
        {
            value = True;
            return true;
        }

        if (utf8.SequenceEqual("false"u8) || utf8.SequenceEqual("0"u8))
        {
            value = False;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdBoolean)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdBoolean value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is <c>true</c> or <c>false</c> (<c>booleanCanonicalMap</c>).</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical) =>
        lexical.SequenceEqual("true"u8) || lexical.SequenceEqual("false"u8);

    /// <summary>Writes <c>true</c> or <c>false</c>.</summary>
    public bool TryFormat(Span<byte> destination, out int written)
    {
        ReadOnlySpan<byte> text = Value ? "true"u8 : "false"u8;

        if (text.Length > destination.Length)
        {
            written = 0;
            return false;
        }

        text.CopyTo(destination);
        written = text.Length;
        return true;
    }

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Value ? "true" : "false";

    /// <summary><c>op:boolean-less-than</c>: <c>false &lt; true</c>.</summary>
    public int CompareTo(XsdBoolean other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(XsdBoolean other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdBoolean other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value ? 1 : 0;

    /// <summary>Equality.</summary>
    public static bool operator ==(XsdBoolean left, XsdBoolean right) => left.Value == right.Value;

    /// <summary>Inequality.</summary>
    public static bool operator !=(XsdBoolean left, XsdBoolean right) => left.Value != right.Value;

    /// <summary>Order.</summary>
    public static bool operator <(XsdBoolean left, XsdBoolean right) => !left.Value && right.Value;

    /// <summary>Order.</summary>
    public static bool operator >(XsdBoolean left, XsdBoolean right) => left.Value && !right.Value;

    /// <summary>Order.</summary>
    public static bool operator <=(XsdBoolean left, XsdBoolean right) => !left.Value || right.Value;

    /// <summary>Order.</summary>
    public static bool operator >=(XsdBoolean left, XsdBoolean right) => left.Value || !right.Value;
}
