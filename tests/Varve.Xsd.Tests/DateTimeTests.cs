// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// The date and time family: lexical forms, canonical forms, and the two
/// orders, one example per rule the specification states.
/// </summary>
public class DateTimeTests
{
    [Theory]
    [InlineData("2002-10-10T12:00:00-05:00", "2002-10-10T12:00:00-05:00")]
    [InlineData("2002-10-10T17:00:00Z", "2002-10-10T17:00:00Z")]
    [InlineData("2002-10-10T17:00:00+00:00", "2002-10-10T17:00:00Z")]
    [InlineData("2002-10-10T17:00:00-00:00", "2002-10-10T17:00:00Z")]
    [InlineData("2002-10-10T17:00:00.500", "2002-10-10T17:00:00.5")]
    [InlineData("2002-10-10T17:00:00.000", "2002-10-10T17:00:00")]
    [InlineData("2002-10-10T24:00:00", "2002-10-11T00:00:00")]
    [InlineData("2002-12-31T24:00:00", "2003-01-01T00:00:00")]
    [InlineData("0000-01-01T00:00:00", "0000-01-01T00:00:00")]
    [InlineData("-0001-01-01T00:00:00", "-0001-01-01T00:00:00")]
    [InlineData("12345-01-01T00:00:00", "12345-01-01T00:00:00")]
    [InlineData("2000-02-29T00:00:00+14:00", "2000-02-29T00:00:00+14:00")]
    [InlineData("2000-02-29T00:00:00-13:59", "2000-02-29T00:00:00-13:59")]
    public void dateTimes_parse_and_format_canonically(string lexical, string canonical)
    {
        Assert.True(XsdDateTime.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDateTime value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdDateTime.IsCanonical(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(lexical == canonical, XsdDateTime.IsCanonical(Encoding.UTF8.GetBytes(lexical)));
    }

    [Theory]
    [InlineData("2002-10-10")]
    [InlineData("2002-10-10T12:00")]
    [InlineData("2002-10-10T12:00:60")]
    [InlineData("2002-10-10T24:00:01")]
    [InlineData("2002-10-10T24:01:00")]
    [InlineData("2002-13-10T12:00:00")]
    [InlineData("2001-02-29T12:00:00")]
    [InlineData("2002-04-31T12:00:00")]
    [InlineData("02002-10-10T12:00:00")]
    [InlineData("002-10-10T12:00:00")]
    [InlineData("2002-10-10T12:00:00+14:01")]
    [InlineData("2002-10-10T12:00:00+15:00")]
    [InlineData("2002-10-10T12:00:00z")]
    [InlineData("2002-10-10T12:00:00 ")]
    [InlineData("2002-10-10t12:00:00")]
    public void an_ill_formed_dateTime_is_no_value(string lexical)
    {
        Assert.False(XsdDateTime.TryParse(Encoding.UTF8.GetBytes(lexical), out _));
    }

    [Fact]
    public void dateTimeStamp_requires_a_timezone()
    {
        Assert.True(XsdDateTime.TryParse("2002-10-10T12:00:00Z"u8, out XsdDateTime stamped));
        Assert.True(stamped.IsDateTimeStamp);
        Assert.True(XsdDateTime.TryParse("2002-10-10T12:00:00"u8, out XsdDateTime plain));
        Assert.False(plain.IsDateTimeStamp);
    }

    [Theory]
    [InlineData("2002-10-10", "2002-10-10")]
    [InlineData("2002-10-10Z", "2002-10-10Z")]
    [InlineData("2002-10-10+02:00", "2002-10-10+02:00")]
    public void dates_parse_and_format(string lexical, string canonical)
    {
        Assert.True(XsdDate.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDate value));
        Assert.Equal(canonical, value.ToString());
        Assert.False(XsdDate.TryParse("2002-10-10T00:00:00"u8, out _));
    }

    [Theory]
    [InlineData("12:00:00", "12:00:00")]
    [InlineData("24:00:00", "00:00:00")]
    [InlineData("23:59:59.9999", "23:59:59.9999")]
    [InlineData("00:00:00+14:00", "00:00:00+14:00")]
    public void times_parse_and_format(string lexical, string canonical)
    {
        Assert.True(XsdTime.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdTime value));
        Assert.Equal(canonical, value.ToString());
    }

    [Theory]
    [InlineData("2002-10", "2002-10")]
    [InlineData("2002", "2002")]
    [InlineData("--10-10", "--10-10")]
    [InlineData("--02-29", "--02-29")]
    [InlineData("---31", "---31")]
    [InlineData("--12Z", "--12Z")]
    public void the_gregorian_fragments_parse_and_format(string lexical, string canonical)
    {
        bool parsed = lexical.StartsWith("---", StringComparison.Ordinal)
            ? XsdGDay.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdGDay d) && d.ToString() == canonical
            : lexical.StartsWith("--", StringComparison.Ordinal) && lexical.Length > 4 && lexical[4] == '-'
                ? XsdGMonthDay.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdGMonthDay md) && md.ToString() == canonical
                : lexical.StartsWith("--", StringComparison.Ordinal)
                    ? XsdGMonth.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdGMonth m) && m.ToString() == canonical
                    : lexical.Length > 4
                        ? XsdGYearMonth.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdGYearMonth ym) && ym.ToString() == canonical
                        : XsdGYear.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdGYear y) && y.ToString() == canonical;

        Assert.True(parsed, lexical);
        Assert.False(XsdGMonthDay.TryParse("--02-30"u8, out _));
        Assert.False(XsdGMonth.TryParse("--13"u8, out _));
    }

    [Fact]
    public void the_same_instant_in_two_timezones_is_equal()
    {
        XsdDateTime a = Parse("2002-10-10T12:00:00-05:00");
        XsdDateTime b = Parse("2002-10-10T17:00:00Z");
        XsdDateTime c = Parse("2002-10-10T17:00:00");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(PartialOrdering.Equal, XsdDateTime.CompareXsd(a, b));
        Assert.NotEqual(b, c);
        Assert.Equal(0, XsdDateTime.Compare(b, c, implicitTimezoneOffset: 0));
        Assert.NotEqual(0, XsdDateTime.Compare(b, c, implicitTimezoneOffset: 60));
    }

    [Fact]
    public void the_xsd_order_is_partial_and_the_implicit_timezone_order_is_total()
    {
        // XSD 1.1 §3.3.7.1's own note: many mixed pairs are incomparable.
        XsdDateTime timezoned = Parse("2000-01-15T12:00:00Z");
        XsdDateTime near = Parse("2000-01-15T12:00:00");
        XsdDateTime farBefore = Parse("2000-01-14T00:00:00");
        XsdDateTime farAfter = Parse("2000-01-17T00:00:00");

        Assert.Equal(PartialOrdering.Indeterminate, XsdDateTime.CompareXsd(timezoned, near));
        Assert.Equal(PartialOrdering.Greater, XsdDateTime.CompareXsd(timezoned, farBefore));
        Assert.Equal(PartialOrdering.Less, XsdDateTime.CompareXsd(timezoned, farAfter));

        Assert.Equal(0, XsdDateTime.Compare(timezoned, near, 0));
        Assert.True(XsdDateTime.Compare(timezoned, near, 60) > 0);
        Assert.True(XsdDateTime.Compare(timezoned, near, -60) < 0);

        // The boundary: exactly fourteen hours apart is still indeterminate.
        Assert.Equal(PartialOrdering.Indeterminate, XsdDateTime.CompareXsd(Parse("2000-01-15T00:00:00Z"), Parse("2000-01-15T14:00:00")));
        Assert.Equal(PartialOrdering.Less, XsdDateTime.CompareXsd(Parse("2000-01-15T00:00:00Z"), Parse("2000-01-15T14:00:01")));
    }

    [Fact]
    public void time_on_timeline_matches_the_specification_fill_values()
    {
        // 1972-12-31T00:00:00Z is the origin §E.3.4 gives an absent date.
        Assert.True(XsdTime.TryParse("00:00:00Z"u8, out XsdTime midnight));
        Assert.True(XsdDateTime.TryParse("1972-12-31T00:00:00Z"u8, out XsdDateTime origin));
        Assert.Equal(origin.TimeOnTimeline(0), midnight.TimeOnTimeline(0));

        Assert.True(XsdDateTime.TryParse("1970-01-01T00:00:00Z"u8, out XsdDateTime epoch));
        Assert.True(XsdDateTime.TryParse("1970-01-02T00:00:00Z"u8, out XsdDateTime next));
        Assert.Equal(XsdDecimal.FromInt64(86400), next.TimeOnTimeline(0) - epoch.TimeOnTimeline(0));

        Assert.True(XsdDateTime.TryParse("0000-03-01T00:00:00Z"u8, out XsdDateTime leapDay));
        Assert.True(XsdDateTime.TryParse("0000-02-28T00:00:00Z"u8, out XsdDateTime beforeLeap));
        Assert.Equal(XsdDecimal.FromInt64(2 * 86400), leapDay.TimeOnTimeline(0) - beforeLeap.TimeOnTimeline(0));
    }

    [Fact]
    public void constructors_validate_their_ranges()
    {
        XsdDateTime built = new(2026, 9, 24, 10, 30, XsdDecimal.FromInt64(15), 120);
        Assert.Equal("2026-09-24T10:30:15+02:00", built.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new XsdDateTime(2026, 2, 30, 0, 0, XsdDecimal.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XsdDateTime(2026, 1, 1, 24, 0, XsdDecimal.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XsdDate(2026, 1, 1, 841));
        Assert.Equal("--02-29", new XsdGMonthDay(2, 29).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => new XsdGMonthDay(2, 30));
    }

    private static XsdDateTime Parse(string text)
    {
        Assert.True(XsdDateTime.TryParse(Encoding.UTF8.GetBytes(text), out XsdDateTime value), text);
        return value;
    }
}
