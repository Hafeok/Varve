// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Xsd;

/// <summary>
/// An <c>xsd:gYearMonth</c> value: a calendar month.
/// </summary>
/// <remarks>
/// <para>
/// XML Schema 1.1 Part 2 §3.3.10, on the seven-property model of §D.2.1.
/// Two orders, under two names, by ADR 0051: <see cref="Compare"/> is the
/// implicit-timezone total order SPARQL's operators use, and
/// <see cref="CompareXsd"/> is XML Schema's partial order. Equality is XML
/// Schema's: the same position on the time line, and both timezoned or both
/// not.
/// </para>
/// </remarks>
public readonly struct XsdGYearMonth : IEquatable<XsdGYearMonth>
{
    private const DateTimeFields Fields = DateTimeFields.Year | DateTimeFields.Month;

    private readonly SevenProperties _value;

    internal XsdGYearMonth(in SevenProperties value) => _value = value;

    /// <summary>
    /// Builds a value from its properties, with <paramref name="timezoneOffset"/>
    /// in minutes east of UTC or null for none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A property is outside its range.</exception>
    public XsdGYearMonth(int year, int month, int? timezoneOffset = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        if (timezoneOffset is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timezoneOffset.Value, -840);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timezoneOffset.Value, 840);
        }

        _value = new SevenProperties(
            Fields, year, month, 0, 0, 0, default,
            timezoneOffset is null ? SevenProperties.NoTimezone : (short)timezoneOffset.Value);
    }

    /// <summary>The year, proleptic Gregorian with astronomical numbering: year 0 exists and precedes year 1.</summary>
    [DesignDecision(typeof(XsdValueSurfaces.XsdComponentsAreSpecIntegers), Scope = ExceptionScope.Boundary)]
    public int Year => _value.Year;

    /// <summary>The month, 1 to 12.</summary>
    [DesignDecision(typeof(XsdValueSurfaces.XsdComponentsAreSpecIntegers), Scope = ExceptionScope.Boundary)]
    public int Month => _value.Month;

    /// <summary>Whether a timezone offset is present.</summary>
    public bool HasTimezone => _value.HasTimezone;

    /// <summary>The timezone offset in minutes east of UTC; zero when absent, so check <see cref="HasTimezone"/>.</summary>
    [DesignDecision(typeof(XsdValueSurfaces.XsdComponentsAreSpecIntegers), Scope = ExceptionScope.Boundary)]
    public int TimezoneOffset => _value.HasTimezone ? _value.TimezoneOffset : 0;

    /// <summary>
    /// <c>timeOnTimeline</c> (§E.3.4) in seconds, with
    /// <paramref name="implicitTimezoneOffset"/> supplied when the value has
    /// no timezone of its own.
    /// </summary>
    [DesignDecision(typeof(XsdValueSurfaces.XsdComponentsAreSpecIntegers), Scope = ExceptionScope.Boundary)]
    public XsdDecimal TimeOnTimeline(int implicitTimezoneOffset) =>
        SevenPropertyModel.TimeOnTimeline(in _value, _value.HasTimezone ? _value.TimezoneOffset : implicitTimezoneOffset);

    /// <summary>Parses the lexical representation (§3.3.10, §D.2.2).</summary>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out XsdGYearMonth value)
    {
        bool ok = SevenPropertyModel.TryParse(utf8, Fields, out SevenProperties parsed);
        value = new XsdGYearMonth(in parsed);
        return ok;
    }

    /// <summary>The <c>char</c> form of <see cref="TryParse(ReadOnlySpan{byte}, out XsdGYearMonth)"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out XsdGYearMonth value) =>
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
    [DesignDecision(typeof(XsdValueSurfaces.XsdOrderingsReturnInt), Scope = ExceptionScope.Boundary)]
    [DesignDecision(typeof(XsdValueSurfaces.XsdComponentsAreSpecIntegers), Scope = ExceptionScope.Boundary)]
    public static int Compare(XsdGYearMonth left, XsdGYearMonth right, int implicitTimezoneOffset) =>
        SevenPropertyModel.Compare(in left._value, in right._value, implicitTimezoneOffset);

    /// <summary>
    /// XML Schema's partial order (§D.2.1, §E.3.4): a timezoned and an
    /// untimezoned value are comparable only when imputing both <c>+14:00</c>
    /// and <c>-14:00</c> gives the same strict answer.
    /// </summary>
    public static PartialOrdering CompareXsd(XsdGYearMonth left, XsdGYearMonth right) =>
        SevenPropertyModel.CompareXsd(in left._value, in right._value);

    /// <inheritdoc />
    public bool Equals(XsdGYearMonth other) => _value.Equals(other._value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XsdGYearMonth other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>XML Schema equality.</summary>
    public static bool operator ==(XsdGYearMonth left, XsdGYearMonth right) => left.Equals(right);

    /// <summary>XML Schema inequality.</summary>
    public static bool operator !=(XsdGYearMonth left, XsdGYearMonth right) => !left.Equals(right);

    /// <summary>
    /// <c>dateTimePlusDuration</c> (§E.3.3) applied to a month, as the
    /// specification's own example <c>2000-01 + -P3M = 1999-10</c> does.
    /// </summary>
    public bool TryAdd(XsdYearMonthDuration duration, out XsdGYearMonth result)
    {
        bool ok = SevenPropertyModel.TryAdd(in _value, duration.Months, XsdDecimal.Zero, out SevenProperties sum);
        result = new XsdGYearMonth(in sum);
        return ok;
    }

    internal SevenProperties Properties => _value;
}
