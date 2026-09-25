// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:dateTime</c> value, and the value of <c>xsd:dateTimeStamp</c> (§3.4.28), which is a dateTime whose timezone offset is required: see <see cref="HasTimezone"/>.
/// </summary>
/// <remarks>
/// <para>
/// XML Schema 1.1 Part 2 §3.3.7, on the seven-property model of §D.2.1.
/// Two orders, under two names, by ADR 0051: <see cref="Compare"/> is the
/// implicit-timezone total order SPARQL's operators use, and
/// <see cref="CompareXsd"/> is XML Schema's partial order. Equality is XML
/// Schema's: the same position on the time line, and both timezoned or both
/// not.
/// </para>
/// </remarks>
public readonly struct XsdDateTime : IEquatable<XsdDateTime>
{
    private const DateTimeFields Fields = DateTimeFields.DateTime;

    private readonly SevenProperties _value;

    internal XsdDateTime(in SevenProperties value) => _value = value;

    /// <summary>
    /// Builds a value from its properties, with <paramref name="timezoneOffset"/>
    /// in minutes east of UTC or null for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A property is outside its range.</exception>
    public XsdDateTime(int year, int month, int day, int hour, int minute, XsdDecimal second, int? timezoneOffset = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, SevenPropertyModel.DaysInMonth(year, month));
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
            Fields, year, month, day, hour, minute, second,
            timezoneOffset is null ? SevenProperties.NoTimezone : (short)timezoneOffset.Value);
    }

    /// <summary>The year, proleptic Gregorian with astronomical numbering: year 0 exists and precedes year 1.</summary>
    public int Year => _value.Year;

    /// <summary>The month, 1 to 12.</summary>
    public int Month => _value.Month;

    /// <summary>The day of the month.</summary>
    public int Day => _value.Day;

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
    /// Whether this value is also an <c>xsd:dateTimeStamp</c> (§3.4.28): a
    /// dateTime with a timezone offset. The lexical space is the same with
    /// the timezone made mandatory, so parsing under that datatype is
    /// <see cref="TryParse(ReadOnlySpan{byte}, out XsdDateTime)"/> plus this check.
    /// </summary>
    public bool IsDateTimeStamp => _value.HasTimezone;

    /// <summary>
    /// <c>timeOnTimeline</c> (§E.3.4) in seconds, with
    /// <paramref name="implicitTimezoneOffset"/> supplied when the value has
    /// no timezone of its own.
    /// </summary>
    public XsdDecimal TimeOnTimeline(int implicitTimezoneOffset) =>
        SevenPropertyModel.TimeOnTimeline(in _value, _value.HasTimezone ? _value.TimezoneOffset : implicitTimezoneOffset);

    /// <summary>Parses the lexical representation (§3.3.7, §D.2.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdDateTime value)
    {
        bool ok = SevenPropertyModel.TryParse(utf8, Fields, out SevenProperties parsed);
        value = new XsdDateTime(in parsed);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdDateTime)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdDateTime value) =>
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
    public static int Compare(XsdDateTime left, XsdDateTime right, int implicitTimezoneOffset) =>
        SevenPropertyModel.Compare(in left._value, in right._value, implicitTimezoneOffset);

    /// <summary>
    /// XML Schema's partial order (§D.2.1, §E.3.4): a timezoned and an
    /// untimezoned value are comparable only when imputing both <c>+14:00</c>
    /// and <c>-14:00</c> gives the same strict answer.
    /// </summary>
    public static PartialOrdering CompareXsd(XsdDateTime left, XsdDateTime right) =>
        SevenPropertyModel.CompareXsd(in left._value, in right._value);

    /// <inheritdoc />
    public bool Equals(XsdDateTime other) => _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdDateTime other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>XML Schema equality.</summary>
    public static bool operator ==(XsdDateTime left, XsdDateTime right) => left.Equals(right);

    /// <summary>XML Schema inequality.</summary>
    public static bool operator !=(XsdDateTime left, XsdDateTime right) => !left.Equals(right);


    /// <summary>
    /// <c>dateTimePlusDuration</c> (§E.3.3): the months first, with the day
    /// pinned to the new month's length, then the seconds. False when the
    /// year leaves the representable range.
    /// </summary>
    public bool TryAdd(XsdDuration duration, out XsdDateTime result)
    {
        bool ok = SevenPropertyModel.TryAdd(in _value, duration.Months, duration.Seconds, out SevenProperties sum);
        result = new XsdDateTime(in sum);
        return ok;
    }

    /// <summary>Adds a year-month duration (<c>op:add-yearMonthDuration-to-dateTime</c>).</summary>
    public bool TryAdd(XsdYearMonthDuration duration, out XsdDateTime result) =>
        TryAdd(XsdDuration.FromYearMonth(duration), out result);

    /// <summary>Adds a day-time duration (<c>op:add-dayTimeDuration-to-dateTime</c>).</summary>
    public bool TryAdd(XsdDayTimeDuration duration, out XsdDateTime result) =>
        TryAdd(XsdDuration.FromDayTime(duration), out result);

    /// <summary>
    /// The elapsed time from <paramref name="right"/> to <paramref name="left"/>
    /// (<c>op:subtract-dateTimes</c>), with the implicit timezone
    /// supplied to an operand that has none.
    /// </summary>
    public static XsdDayTimeDuration Subtract(XsdDateTime left, XsdDateTime right, int implicitTimezoneOffset) =>
        new(left.TimeOnTimeline(implicitTimezoneOffset) - right.TimeOnTimeline(implicitTimezoneOffset));

    /// <summary>
    /// The timezone offset as a day-time duration, which is what SPARQL 1.1
    /// §17.4.5.8's <c>timezone</c> returns; check <see cref="HasTimezone"/>
    /// first, because that function is an error without one.
    /// </summary>
    public XsdDayTimeDuration TimezoneDuration => XsdDayTimeDuration.FromMinutes(TimezoneOffset);

    internal SevenProperties Properties => _value;
}
