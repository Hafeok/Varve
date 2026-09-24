// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:dayTimeDuration</c> value (XML Schema 1.1 Part 2 §3.4.27): a
/// duration whose months are zero, and therefore totally ordered.
/// </summary>
/// <remarks>
/// SPARQL 1.1 §17.4.5.8's <c>timezone</c> returns one of these, which is why
/// the type carries a constructor from minutes.
/// </remarks>
public readonly struct XsdDayTimeDuration : IEquatable<XsdDayTimeDuration>, IComparable<XsdDayTimeDuration>
{
    /// <summary>Wraps a number of seconds.</summary>
    public XsdDayTimeDuration(XsdDecimal seconds) => Seconds = seconds;

    /// <summary>The <c>seconds</c> property.</summary>
    public XsdDecimal Seconds { get; }

    /// <summary>A duration of whole minutes, as a timezone offset is.</summary>
    public static XsdDayTimeDuration FromMinutes(long minutes) => new(XsdDecimal.FromInt64(minutes * 60));

    /// <summary>Parses a <c>dayTimeDurationLexicalRep</c> (§3.4.27.1).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdDayTimeDuration value)
    {
        bool ok = DurationLexical.TryParse(utf8, allowYearMonth: false, allowDayTime: true, out _, out XsdDecimal seconds);
        value = new XsdDayTimeDuration(seconds);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdDayTimeDuration)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdDayTimeDuration value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one.</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        if (!TryParse(lexical, out XsdDayTimeDuration value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Lexical.StackLimit];
        return value.TryFormat(buffer, out int written) && buffer[..written].SequenceEqual(lexical);
    }

    /// <summary>Writes the canonical form: <c>P1DT2H</c>, <c>-PT0.5S</c>, <c>PT0S</c>.</summary>
    public bool TryFormat(Span<byte> destination, out int written) =>
        DurationLexical.TryFormat(0, Seconds, destination, out written);

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>Sum; false on overflow.</summary>
    public static bool TryAdd(XsdDayTimeDuration left, XsdDayTimeDuration right, out XsdDayTimeDuration result)
    {
        bool ok = XsdDecimal.TryAdd(left.Seconds, right.Seconds, out XsdDecimal sum);
        result = new XsdDayTimeDuration(sum);
        return ok;
    }

    /// <summary>Difference; false on overflow.</summary>
    public static bool TrySubtract(XsdDayTimeDuration left, XsdDayTimeDuration right, out XsdDayTimeDuration result)
    {
        bool ok = XsdDecimal.TrySubtract(left.Seconds, right.Seconds, out XsdDecimal difference);
        result = new XsdDayTimeDuration(difference);
        return ok;
    }

    /// <summary>Total order, by seconds.</summary>
    public int CompareTo(XsdDayTimeDuration other) => Seconds.CompareTo(other.Seconds);

    /// <inheritdoc />
    public bool Equals(XsdDayTimeDuration other) => Seconds == other.Seconds;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdDayTimeDuration other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Seconds.GetHashCode();

    /// <summary>Equality.</summary>
    public static bool operator ==(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds == right.Seconds;

    /// <summary>Inequality.</summary>
    public static bool operator !=(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds != right.Seconds;

    /// <summary>Order.</summary>
    public static bool operator <(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds < right.Seconds;

    /// <summary>Order.</summary>
    public static bool operator >(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds > right.Seconds;

    /// <summary>Order.</summary>
    public static bool operator <=(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds <= right.Seconds;

    /// <summary>Order.</summary>
    public static bool operator >=(XsdDayTimeDuration left, XsdDayTimeDuration right) => left.Seconds >= right.Seconds;
}
