// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// The examples XML Schema 1.1 Part 2 gives in its own text, each parsed
/// and formatted or compared as the text says. A differential test against
/// the specification rather than against another implementation: the gate
/// for this package until the SPARQL evaluation suite can be (ADR 0051).
/// </summary>
public class XsdExamplesTests
{
    /// <summary>§3.3.3.1: "For example: -1.23, 12678967.543233, +100000.00, 210."</summary>
    [Theory]
    [InlineData("-1.23", "-1.23")]
    [InlineData("12678967.543233", "12678967.543233")]
    [InlineData("+100000.00", "100000")]
    [InlineData("210", "210")]
    public void the_decimal_examples_of_3_3_3_1(string lexical, string canonical)
    {
        Assert.True(XsdDecimal.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDecimal value));
        Assert.Equal(canonical, value.ToString());
    }

    /// <summary>§3.4.13, §3.4.14, §3.4.15: the integer, nonPositiveInteger and negativeInteger examples.</summary>
    [Theory]
    [InlineData("integer", "-1")]
    [InlineData("integer", "0")]
    [InlineData("integer", "12678967543233")]
    [InlineData("integer", "+100000")]
    [InlineData("nonPositiveInteger", "-12678967543233")]
    [InlineData("nonPositiveInteger", "0")]
    [InlineData("negativeInteger", "-100000")]
    public void the_integer_examples_of_3_4_13_to_3_4_15(string datatype, string lexical)
    {
        XsdDatatype type = XsdDatatypes.FromIri(Encoding.UTF8.GetBytes("http://www.w3.org/2001/XMLSchema#" + datatype));
        Assert.True(XsdInteger.TryParse(Encoding.UTF8.GetBytes(lexical), type, out XsdInteger value));
        Assert.Equal(lexical.TrimStart('+'), value.ToString());
    }

    /// <summary>§D.1.1's canonical mappings for the special floating-point values and zeros.</summary>
    [Theory]
    [InlineData("0", "0.0E0")]
    [InlineData("-0", "-0.0E0")]
    [InlineData("INF", "INF")]
    [InlineData("-INF", "-INF")]
    [InlineData("NaN", "NaN")]
    public void the_special_double_forms_of_d_1_1(string lexical, string canonical)
    {
        Assert.True(XsdDouble.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDouble value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdFloat.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdFloat single));
        Assert.Equal(canonical, single.ToString());
    }

    /// <summary>§3.3.7: 2002-10-10T12:00:00-05:00 is equal to 2002-10-10T17:00:00Z, five hours later than 2002-10-10T12:00:00Z.</summary>
    [Fact]
    public void the_dateTime_instants_of_3_3_7()
    {
        XsdDateTime central = DateTime("2002-10-10T12:00:00-05:00");
        XsdDateTime utc = DateTime("2002-10-10T17:00:00Z");
        XsdDateTime noonUtc = DateTime("2002-10-10T12:00:00Z");

        Assert.Equal(central, utc);
        Assert.Equal(PartialOrdering.Equal, XsdDateTime.CompareXsd(central, utc));
        Assert.Equal(XsdDecimal.FromInt64(5 * 3600), utc.TimeOnTimeline(0) - noonUtc.TimeOnTimeline(0));
        Assert.Equal("PT5H", XsdDateTime.Subtract(utc, noonUtc, 0).ToString());
    }

    /// <summary>§3.3.7.2: '0000' maps to the year 1 BCE and '-0001' to 2 BCE, one year apart.</summary>
    [Fact]
    public void the_year_zero_rule_of_3_3_7_2()
    {
        XsdDateTime yearZero = DateTime("0000-01-01T00:00:00Z");
        XsdDateTime yearMinusOne = DateTime("-0001-01-01T00:00:00Z");
        XsdDateTime yearOne = DateTime("0001-01-01T00:00:00Z");

        // Year 0 is a leap year under astronomical numbering: 366 days to year 1.
        Assert.Equal(XsdDecimal.FromInt64(366L * 86400), yearOne.TimeOnTimeline(0) - yearZero.TimeOnTimeline(0));
        Assert.Equal(XsdDecimal.FromInt64(365L * 86400), yearZero.TimeOnTimeline(0) - yearMinusOne.TimeOnTimeline(0));
    }

    /// <summary>§3.3.8: 05:00:00-03:00 and 10:00:00+02:00 are equal; 23:00:00-03:00 is greater than 02:00:00Z.</summary>
    [Fact]
    public void the_time_examples_of_3_3_8()
    {
        Assert.Equal(Time("05:00:00-03:00"), Time("10:00:00+02:00"));
        Assert.Equal(PartialOrdering.Greater, XsdTime.CompareXsd(Time("23:00:00-03:00"), Time("02:00:00Z")));
        Assert.True(XsdTime.Compare(Time("23:00:00-03:00"), Time("02:00:00Z"), 0) > 0);
        Assert.Equal(Time("00:00:00"), Time("24:00:00"));
    }

    /// <summary>§3.3.9: 2000-01-01+13:00 and 1999-12-31-11:00 are equal but not identical.</summary>
    [Fact]
    public void the_date_example_of_3_3_9()
    {
        Assert.True(XsdDate.TryParse("2000-01-01+13:00"u8, out XsdDate a));
        Assert.True(XsdDate.TryParse("1999-12-31-11:00"u8, out XsdDate b));
        Assert.Equal(a, b);
        Assert.NotEqual(a.ToString(), b.ToString());
    }

    /// <summary>§3.3.6.1: 1697-02-01Z + P1M &lt; 1697-02-01Z + P30D but 1903-03-01Z + P1M &gt; 1903-03-01Z + P30D, so P1M &lt;&gt; P30D.</summary>
    [Fact]
    public void the_duration_example_of_3_3_6_1()
    {
        XsdDateTime february = DateTime("1697-02-01T00:00:00Z");
        XsdDateTime march = DateTime("1903-03-01T00:00:00Z");
        Assert.True(XsdDuration.TryParse("P1M"u8, out XsdDuration month));
        Assert.True(XsdDuration.TryParse("P30D"u8, out XsdDuration thirtyDays));

        Assert.True(february.TryAdd(month, out XsdDateTime a));
        Assert.True(february.TryAdd(thirtyDays, out XsdDateTime b));
        Assert.True(XsdDateTime.Compare(a, b, 0) < 0);
        Assert.True(march.TryAdd(month, out XsdDateTime c));
        Assert.True(march.TryAdd(thirtyDays, out XsdDateTime d));
        Assert.True(XsdDateTime.Compare(c, d, 0) > 0);
        Assert.Equal(PartialOrdering.Indeterminate, XsdDuration.CompareXsd(month, thirtyDays));
    }

    /// <summary>§E.3.3's table: three additions with their results.</summary>
    [Fact]
    public void the_dateTimePlusDuration_examples_of_e_3_3()
    {
        Assert.True(XsdDuration.TryParse("P1Y3M5DT7H10M3.3S"u8, out XsdDuration first));
        Assert.True(DateTime("2000-01-12T12:13:14Z").TryAdd(first, out XsdDateTime firstResult));
        Assert.Equal("2001-04-17T19:23:17.3Z", firstResult.ToString());

        Assert.True(XsdGYearMonth.TryParse("2000-01"u8, out XsdGYearMonth month));
        Assert.True(XsdYearMonthDuration.TryParse("-P3M"u8, out XsdYearMonthDuration minusThree));
        Assert.True(month.TryAdd(minusThree, out XsdGYearMonth monthResult));
        Assert.Equal("1999-10", monthResult.ToString());

        Assert.True(XsdDate.TryParse("2000-01-12"u8, out XsdDate date));
        Assert.True(XsdDayTimeDuration.TryParse("PT33H"u8, out XsdDayTimeDuration hours));
        Assert.True(date.TryAdd(hours, out XsdDate dateResult));
        Assert.Equal("2000-01-13", dateResult.ToString());
    }

    /// <summary>§3.4.26.1: a zero yearMonthDuration's duration form PT0S is outside its lexical space; this package writes P0M.</summary>
    [Fact]
    public void a_zero_yearMonthDuration_is_written_p0m()
    {
        Assert.False(XsdYearMonthDuration.TryParse("PT0S"u8, out _));
        Assert.True(XsdYearMonthDuration.TryParse("P0M"u8, out XsdYearMonthDuration zero));
        Assert.Equal("P0M", zero.ToString());
        Assert.True(XsdYearMonthDuration.TryParse("P0Y"u8, out XsdYearMonthDuration zeroYears));
        Assert.Equal(zero, zeroYears);
    }

    private static XsdDateTime DateTime(string text)
    {
        Assert.True(XsdDateTime.TryParse(Encoding.UTF8.GetBytes(text), out XsdDateTime value), text);
        return value;
    }

    private static XsdTime Time(string text)
    {
        Assert.True(XsdTime.TryParse(Encoding.UTF8.GetBytes(text), out XsdTime value), text);
        return value;
    }
}
