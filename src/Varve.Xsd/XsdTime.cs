// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:time</c> value: a time of day, with an optional timezone offset.
/// </summary>
/// <remarks>
/// <para>
/// XML Schema 1.1 Part 2 §3.3.8, on the seven-property model of §D.2.1.
/// Two orders, under two names, by ADR 0051: <see cref="Compare"/> is the
/// implicit-timezone total order SPARQL's operators use, and
/// <see cref="CompareXsd"/> is XML Schema's partial order. Equality is XML
/// Schema's: the same position on the time line, and both timezoned or both
/// not.
/// </para>
/// </remarks>
public readonly struct XsdTime : IEquatable<XsdTime>
{
    private const DateTimeFields Fields = DateTimeFields.Time;

    private readonly SevenProperties _value;

    internal XsdTime(in SevenProperties value) => _value = value;

    /// <summary>
    /// Builds a value from its properties, with <paramref name="timezoneOffset"/>
    /// in minutes east of UTC or null for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A property is outside its range.</exception>
    public XsdTime(int hour, int minute, XsdDecimal second, int? timezoneOffset = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        ArgumentOutOfRangeException.ThrowIfNegative(minute);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minute, 59);
        if (second.IsNegative || second >= XsdDecimal.FromInt64(60))
        {
            throw new ArgumentOutOfRangeException(nameof(second), "A second is a decimal in [0, 60).");
        }
        if (timezoneOffset is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timezoneOffset.Value, -840);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timezoneOffset.Value, 840);
        }

        _value = new SevenProperties(
            Fields, 0, 0, 0, hour, minute, second,
            timezoneOffset is null ? SevenProperties.NoTimezone : (short)timezoneOffset.Value);
    }

    /// <summary>The hour, 0 to 23.</summary>
    public int Hour => _value.Hour;

    /// <summary>The minute, 0 to 59.</summary>
    public int Minute => _value.Minute;

    /// <summary>The second, a decimal in [0, 60).</summary>
    public XsdDecimal Second => _value.Second;

    /// <summary>Whether a timezone offset is present.</summary>
    public bool HasTimezone => _value.HasTimezone;

    /// <summary>The timezone offset in minutes east of UTC; zero when absent, so check <see cref="HasTimezone"/>.</summary>
    public int TimezoneOffset => _value.HasTimezone ? _value.TimezoneOffset : 0;

    /// <summary>
    /// <c>timeOnTimeline</c> (§E.3.4) in seconds, with
    /// <paramref name="implicitTimezoneOffset"/> supplied when the value has
    /// no timezone of its own.
    /// </summary>
    public XsdDecimal TimeOnTimeline(int implicitTimezoneOffset) =>
        SevenPropertyModel.TimeOnTimeline(in _value, _value.HasTimezone ? _value.TimezoneOffset : implicitTimezoneOffset);

    /// <summary>Parses the lexical representation (§3.3.8, §D.2.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdTime value)
    {
        bool ok = SevenPropertyModel.TryParse(utf8, Fields, out SevenProperties parsed);
        value = new XsdTime(in parsed);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdTime)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdTime value) =>
        Lexical.ParseChars(text, TryParse, out value);

    /// <summary>Whether a lexical form is the canonical one (§E.3.6).</summary>
    public static bool IsCanonical(ReadOnlySpan<byte> lexical) => SevenPropertyModel.IsCanonical(lexical, Fields);

    /// <summary>Writes the canonical form.</summary>
    public bool TryFormat(Span<byte> destination, out int written) =>
        SevenPropertyModel.TryFormat(in _value, Fields, destination, out written);

    /// <summary>Writes the canonical form as <c>char</c>s.</summary>
    public bool TryFormat(Span<char> destination, out int written) =>
        Lexical.FormatChars(destination, TryFormat, out written);

    /// <summary>The canonical form.</summary>
    public override string ToString() => Lexical.ToString(TryFormat);

    /// <summary>
    /// The implicit-timezone total order: a value without a timezone is
    /// given <paramref name="implicitTimezoneOffset"/> (XPath Functions and
    /// Operators §10.4), and every pair is then comparable.
    /// </summary>
    public static int Compare(XsdTime left, XsdTime right, int implicitTimezoneOffset) =>
        SevenPropertyModel.Compare(in left._value, in right._value, implicitTimezoneOffset);

    /// <summary>
    /// XML Schema's partial order (§D.2.1, §E.3.4): a timezoned and an
    /// untimezoned value are comparable only when imputing both <c>+14:00</c>
    /// and <c>-14:00</c> gives the same strict answer.
    /// </summary>
    public static PartialOrdering CompareXsd(XsdTime left, XsdTime right) =>
        SevenPropertyModel.CompareXsd(in left._value, in right._value);

    /// <inheritdoc />
    public bool Equals(XsdTime other) => _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdTime other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>XML Schema equality.</summary>
    public static bool operator ==(XsdTime left, XsdTime right) => left.Equals(right);

    /// <summary>XML Schema inequality.</summary>
    public static bool operator !=(XsdTime left, XsdTime right) => !left.Equals(right);

    internal SevenProperties Properties => _value;
}
