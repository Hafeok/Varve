// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:duration</c> value (XML Schema 1.1 Part 2 §3.3.6): a number of
/// months and a decimal number of seconds, never of opposite signs.
/// </summary>
/// <remarks>
/// Partially ordered (§3.3.6.1): <c>P1M</c> and <c>P30D</c> are neither
/// less, equal nor greater, because a month is 28 to 31 days. The two
/// derived types, <see cref="XsdYearMonthDuration"/> and
/// <see cref="XsdDayTimeDuration"/>, are the totally ordered halves.
/// </remarks>
public readonly struct XsdDuration : IEquatable<XsdDuration>
{
    /// <summary>Builds a duration; the two components must not have opposite signs.</summary>
    /// <exception cref="ArgumentException">The signs differ.</exception>
    public XsdDuration(long months, XsdDecimal seconds)
    {
        if ((months < 0 && seconds > XsdDecimal.Zero) || (months > 0 && seconds.IsNegative))
        {
            throw new ArgumentException("A duration's months and seconds cannot have opposite signs (§3.3.6.1).", nameof(seconds));
        }

        Months = months;
        Seconds = seconds;
    }

    /// <summary>The <c>months</c> property.</summary>
    public long Months { get; }

    /// <summary>The <c>seconds</c> property.</summary>
    public XsdDecimal Seconds { get; }

    /// <summary>Whether both components are zero.</summary>
    public bool IsZero => Months == 0 && Seconds.IsZero;

    /// <summary>Whether the duration is negative.</summary>
    public bool IsNegative => Months < 0 || Seconds.IsNegative;

    /// <summary>The value of a year-month duration.</summary>
    public static XsdDuration FromYearMonth(XsdYearMonthDuration value) => new(value.Months, XsdDecimal.Zero);

    /// <summary>The value of a day-time duration.</summary>
    public static XsdDuration FromDayTime(XsdDayTimeDuration value) => new(0, value.Seconds);

    /// <summary>The year-month half, when the seconds are zero.</summary>
    public bool TryToYearMonth(out XsdYearMonthDuration value)
    {
        value = new XsdYearMonthDuration(Months);
        return Seconds.IsZero;
    }

    /// <summary>The day-time half, when the months are zero.</summary>
    public bool TryToDayTime(out XsdDayTimeDuration value)
    {
        value = new XsdDayTimeDuration(Seconds);
        return Months == 0;
    }

    /// <summary>Parses a <c>durationLexicalRep</c> (§3.3.6.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdDuration value)
    {
        bool ok = DurationLexical.TryParse(utf8, allowYearMonth: true, allowDayTime: true, out long months, out XsdDecimal seconds);
        value = ok ? new XsdDuration(months, seconds) : default;
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdDuration)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdDuration value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one (<c>durationCanonicalMap</c>).</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical)
    {
        if (!TryParse(lexical, out XsdDuration value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Lexical.StackLimit];
        return value.TryFormat(buffer, out int written) && buffer[..written].SequenceEqual(lexical);
    }

    /// <summary>Writes the canonical form: <c>P2Y1M</c>, <c>PT1H30M</c>, <c>PT0S</c>.</summary>
    public bool TryFormat(Span<byte> destination, out int written) =>
        DurationLexical.TryFormat(Months, Seconds, destination, out written);

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>
    /// The order of §3.3.6.1, by adding each duration to the four reference
    /// dateTimes; <see cref="PartialOrdering.Indeterminate"/> when they
    /// disagree.
    /// </summary>
    public static PartialOrdering CompareXsd(XsdDuration left, XsdDuration right) =>
        DurationLexical.CompareXsd(left.Months, left.Seconds, right.Months, right.Seconds);

    /// <summary>Equality is identity of both components (§3.3.6.1).</summary>
    public bool Equals(XsdDuration other) => Months == other.Months && Seconds == other.Seconds;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdDuration other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Months, Seconds);

    /// <summary>Equality.</summary>
    public static bool operator ==(XsdDuration left, XsdDuration right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(XsdDuration left, XsdDuration right) => !left.Equals(right);
}
