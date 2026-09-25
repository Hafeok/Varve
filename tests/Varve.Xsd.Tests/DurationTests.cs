// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// Durations: the lexical space, the canonical reductions, the partial
/// order over the four reference dateTimes, and addition to a dateTime.
/// </summary>
public class DurationTests
{
    [Theory]
    [InlineData("P1Y13M", "P2Y1M")]
    [InlineData("P12M", "P1Y")]
    [InlineData("PT90M", "PT1H30M")]
    [InlineData("P0D", "PT0S")]
    [InlineData("PT0S", "PT0S")]
    [InlineData("P1DT0H", "P1D")]
    [InlineData("-P1Y2M3DT4H5M6.7S", "-P1Y2M3DT4H5M6.7S")]
    [InlineData("PT1.50S", "PT1.5S")]
    [InlineData("PT0.5S", "PT0.5S")]
    [InlineData("P1Y", "P1Y")]
    [InlineData("PT36H", "P1DT12H")]
    [InlineData("P0Y0M0DT0H0M0S", "PT0S")]
    public void durations_parse_and_format_canonically(string lexical, string canonical)
    {
        Assert.True(XsdDuration.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDuration value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdDuration.IsCanonical(Encoding.UTF8.GetBytes(canonical)));
        Assert.True(XsdDuration.TryParse(Encoding.UTF8.GetBytes(canonical), out XsdDuration again));
        Assert.Equal(value, again);
    }

    [Theory]
    [InlineData("P")]
    [InlineData("PT")]
    [InlineData("P1YT")]
    [InlineData("P1M1Y")]
    [InlineData("PT1S1M")]
    [InlineData("P1H")]
    [InlineData("P36H")]
    [InlineData("PT1D")]
    [InlineData("PT.5S")]
    [InlineData("PT5.S")]
    [InlineData("P1.5Y")]
    [InlineData("+P1Y")]
    [InlineData("P-1Y")]
    [InlineData("1Y")]
    [InlineData("P1Y ")]
    public void an_ill_formed_duration_is_no_value(string lexical)
    {
        Assert.False(XsdDuration.TryParse(Encoding.UTF8.GetBytes(lexical), out _));
    }

    [Fact]
    public void the_derived_types_take_only_their_half()
    {
        Assert.True(XsdYearMonthDuration.TryParse("P1Y6M"u8, out XsdYearMonthDuration yearMonth));
        Assert.Equal(18, yearMonth.Months);
        Assert.False(XsdYearMonthDuration.TryParse("P1D"u8, out _));
        Assert.Equal("P0M", new XsdYearMonthDuration(0).ToString());
        Assert.Equal("-P2Y", new XsdYearMonthDuration(-24).ToString());

        Assert.True(XsdDayTimeDuration.TryParse("P1DT12H"u8, out XsdDayTimeDuration dayTime));
        Assert.Equal(XsdDecimal.FromInt64(129600), dayTime.Seconds);
        Assert.False(XsdDayTimeDuration.TryParse("P1M"u8, out _));
        Assert.Equal("PT0S", new XsdDayTimeDuration(XsdDecimal.Zero).ToString());
        Assert.Equal("PT2H", XsdDayTimeDuration.FromMinutes(120).ToString());
        Assert.Equal("-PT5H30M", XsdDayTimeDuration.FromMinutes(-330).ToString());
    }

    [Fact]
    public void the_xsd_order_on_duration_is_partial()
    {
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(Parse("P1M"), Parse("P30D")));
        Assert.Equal(PartialOrdering.Less, XsdDuration.CompareXsd(Parse("P1M"), Parse("P32D")));
        Assert.Equal(PartialOrdering.Greater, XsdDuration.CompareXsd(Parse("P1M"), Parse("P27D")));
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(Parse("P1M"), Parse("P28D")));
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(Parse("P1M"), Parse("P31D")));
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(Parse("P1Y"), Parse("P366D")));
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(Parse("P1Y"), Parse("P365D")));
        Assert.Equal(PartialOrdering.Less, XsdDuration.CompareXsd(Parse("P1Y"), Parse("P367D")));
        Assert.Equal(PartialOrdering.Greater, XsdDuration.CompareXsd(Parse("P1Y"), Parse("P364D")));
        Assert.Equal(PartialOrdering.Equal, XsdDuration.CompareXsd(Parse("P1Y"), Parse("P12M")));
        Assert.Equal(PartialOrdering.Less, XsdDuration.CompareXsd(Parse("PT1H"), Parse("PT61M")));
        Assert.Equal(PartialOrdering.Greater, XsdDuration.CompareXsd(Parse("P1D"), Parse("-P1D")));
        Assert.Equal(PartialOrdering.Less, XsdDuration.CompareXsd(Parse("-P1M"), Parse("-P27D")));
    }

    [Fact]
    public void adding_a_duration_to_a_dateTime_follows_e_3_3()
    {
        // The XSD 1.1 §E.3.3 example: 2000-01-12T12:13:14Z + P1Y3M5DT7H10M3.3S = 2001-04-17T19:23:17.3Z.
        Assert.True(XsdDateTime.TryParse("2000-01-12T12:13:14Z"u8, out XsdDateTime start));
        Assert.True(start.TryAdd(Parse("P1Y3M5DT7H10M3.3S"), out XsdDateTime end));
        Assert.Equal("2001-04-17T19:23:17.3Z", end.ToString());

        // The day is pinned when the month is shorter.
        Assert.True(XsdDateTime.TryParse("2000-01-31T00:00:00"u8, out XsdDateTime january));
        Assert.True(january.TryAdd(Parse("P1M"), out XsdDateTime february));
        Assert.Equal("2000-02-29T00:00:00", february.ToString());

        // A negative duration and a day carry backwards across a year.
        Assert.True(XsdDateTime.TryParse("2000-01-01T00:00:00"u8, out XsdDateTime newYear));
        Assert.True(newYear.TryAdd(Parse("-PT1S"), out XsdDateTime before));
        Assert.Equal("1999-12-31T23:59:59", before.ToString());

        // Dates add too, and the difference of two dateTimes is a dayTimeDuration.
        Assert.True(XsdDate.TryParse("2000-02-29"u8, out XsdDate leap));
        Assert.True(leap.TryAdd(new XsdYearMonthDuration(12), out XsdDate next));
        Assert.Equal("2001-02-28", next.ToString());
        Assert.Equal("P1DT7H10M3.3S", XsdDateTime.Subtract(end, Parse2("2001-04-16T12:13:14Z"), 0).ToString());

        Assert.True(XsdDateTime.TryParse("2000-01-01T00:00:00-05:00"u8, out XsdDateTime zoned));
        Assert.Equal("-PT5H", zoned.TimezoneDuration.ToString());
    }

    private static XsdDuration Parse(string text)
    {
        Assert.True(XsdDuration.TryParse(Encoding.UTF8.GetBytes(text), out XsdDuration value), text);
        return value;
    }

    private static XsdDateTime Parse2(string text)
    {
        Assert.True(XsdDateTime.TryParse(Encoding.UTF8.GetBytes(text), out XsdDateTime value), text);
        return value;
    }
}
