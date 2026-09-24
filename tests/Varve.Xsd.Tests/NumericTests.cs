// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// The numeric types, one example per rule the specification states, so
/// that a property failure has a worked case beside it.
/// </summary>
public class NumericTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("+7", "7")]
    [InlineData("007", "7")]
    [InlineData("-9223372036854775808", "-9223372036854775808")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    public void integers_parse_and_format_canonically(string lexical, string canonical)
    {
        Assert.True(XsdInteger.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdInteger value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdInteger.IsCanonical(Encoding.UTF8.GetBytes(canonical)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("1.0")]
    [InlineData("1e3")]
    [InlineData(" 1")]
    [InlineData("9223372036854775808")]
    [InlineData("-9223372036854775809")]
    public void an_integer_that_is_ill_formed_or_out_of_range_is_no_value(string lexical)
    {
        Assert.False(XsdInteger.TryParse(Encoding.UTF8.GetBytes(lexical), out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("01", false)]
    [InlineData("+1", false)]
    [InlineData("-0", false)]
    [InlineData("0", true)]
    [InlineData("-12", true)]
    [InlineData("99999999999999999999999999", true)]
    public void integer_canonicality_is_judged_on_the_string(string lexical, bool canonical)
    {
        Assert.Equal(canonical, XsdInteger.IsCanonical(Encoding.UTF8.GetBytes(lexical)));
    }

    [Theory]
    [InlineData("byte", "127", true)]
    [InlineData("byte", "128", false)]
    [InlineData("unsignedByte", "255", true)]
    [InlineData("unsignedByte", "-1", false)]
    [InlineData("positiveInteger", "0", false)]
    [InlineData("negativeInteger", "-1", true)]
    [InlineData("nonPositiveInteger", "0", true)]
    [InlineData("unsignedLong", "9223372036854775807", true)]
    [InlineData("unsignedLong", "9223372036854775808", false)]
    public void derived_integer_types_are_ranges(string datatype, string lexical, bool expected)
    {
        XsdDatatype type = XsdDatatypes.FromIri(Encoding.UTF8.GetBytes("http://www.w3.org/2001/XMLSchema#" + datatype));
        Assert.NotEqual(XsdDatatype.None, type);
        Assert.Equal(expected, XsdInteger.TryParse(Encoding.UTF8.GetBytes(lexical), type, out _));
    }

    [Theory]
    [InlineData("1.50", "1.5")]
    [InlineData("1.0", "1")]
    [InlineData("-0.0", "0")]
    [InlineData("+.5", "0.5")]
    [InlineData("5.", "5")]
    [InlineData("000123.4500", "123.45")]
    [InlineData("0.000000000000000001", "0.000000000000000001")]
    [InlineData("0.0000000000000000010", "0.000000000000000001")]
    [InlineData("170141183460469231731.687303715884105727", "170141183460469231731.687303715884105727")]
    [InlineData("-170141183460469231731.687303715884105728", "-170141183460469231731.687303715884105728")]
    public void decimals_parse_and_format_canonically(string lexical, string canonical)
    {
        Assert.True(XsdDecimal.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDecimal value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdDecimal.IsCanonical(Encoding.UTF8.GetBytes(canonical)));
        Assert.True(XsdDecimal.TryParse(Encoding.UTF8.GetBytes(canonical), out XsdDecimal again));
        Assert.Equal(value, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("1..0")]
    [InlineData("1e3")]
    [InlineData("0.0000000000000000001")]
    [InlineData("170141183460469231731.687303715884105728")]
    [InlineData("-170141183460469231731.687303715884105729")]
    public void a_decimal_that_is_ill_formed_or_beyond_the_policy_is_no_value(string lexical)
    {
        Assert.False(XsdDecimal.TryParse(Encoding.UTF8.GetBytes(lexical), out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.0", false)]
    [InlineData("1.5", true)]
    [InlineData("01.5", false)]
    [InlineData("0.5", true)]
    [InlineData(".5", false)]
    [InlineData("-0", false)]
    [InlineData("-0.5", true)]
    [InlineData("+1", false)]
    public void decimal_canonicality_is_judged_on_the_string(string lexical, bool canonical)
    {
        Assert.Equal(canonical, XsdDecimal.IsCanonical(Encoding.UTF8.GetBytes(lexical)));
    }

    [Fact]
    public void decimal_arithmetic_is_exact_and_division_truncates_at_the_eighteenth_digit()
    {
        XsdDecimal a = Decimal("0.1");
        XsdDecimal b = Decimal("0.2");
        Assert.Equal(Decimal("0.3"), a + b);
        Assert.Equal(Decimal("0.02"), a * b);
        Assert.Equal(Decimal("0.333333333333333333"), XsdDecimal.One / Decimal("3"));
        Assert.Equal(Decimal("-0.333333333333333333"), -XsdDecimal.One / Decimal("3"));
        Assert.Equal(Decimal("12345678901234567890"), Decimal("1234567890.123456789") * Decimal("10000000000"));
        Assert.Equal(Decimal("1234567890.123456789"), Decimal("12345678901234567890") / Decimal("10000000000"));
        Assert.Equal(Decimal("-0.000000000000000001"), Decimal("-0.000000000000000001") * XsdDecimal.One);
    }

    [Fact]
    public void decimal_overflow_fails_rather_than_wrapping()
    {
        Assert.False(XsdDecimal.TryAdd(XsdDecimal.MaxValue, XsdDecimal.One, out _));
        Assert.False(XsdDecimal.TryMultiply(Decimal("100000000000"), Decimal("10000000000"), out _));
        Assert.False(XsdDecimal.TryDivide(XsdDecimal.One, XsdDecimal.Zero, out _));
        Assert.False(XsdDecimal.TryNegate(XsdDecimal.MinValue, out _));
        Assert.Throws<OverflowException>(() => XsdDecimal.MaxValue + XsdDecimal.One);
        Assert.Throws<DivideByZeroException>(() => XsdDecimal.One / XsdDecimal.Zero);
    }

    [Fact]
    public void decimal_rounding_follows_fn_round()
    {
        Assert.Equal(Decimal("3"), Round("2.5"));
        Assert.Equal(Decimal("-2"), Round("-2.5"));
        Assert.Equal(Decimal("2"), Decimal("2.4").Floor());
        Assert.Equal(Decimal("-3"), Decimal("-2.4").Floor());
        Assert.Equal(Decimal("3"), Decimal("2.4").Ceiling());
        Assert.Equal(Decimal("-2"), Decimal("-2.4").Ceiling());

        static XsdDecimal Round(string text)
        {
            Assert.True(Decimal(text).TryRound(out XsdDecimal rounded));
            return rounded;
        }
    }

    [Theory]
    [InlineData("1", "1.0E0")]
    [InlineData("-1", "-1.0E0")]
    [InlineData("0", "0.0E0")]
    [InlineData("-0", "-0.0E0")]
    [InlineData("123.456", "1.23456E2")]
    [InlineData("0.001", "1.0E-3")]
    [InlineData("1e16", "1.0E16")]
    [InlineData("1E-7", "1.0E-7")]
    [InlineData("100", "1.0E2")]
    [InlineData("INF", "INF")]
    [InlineData("+INF", "INF")]
    [InlineData("-INF", "-INF")]
    [InlineData("NaN", "NaN")]
    [InlineData("1.7976931348623157E308", "1.7976931348623157E308")]
    [InlineData("5E-324", "5.0E-324")]
    [InlineData("4.9E-324", "5.0E-324")]
    [InlineData(".5", "5.0E-1")]
    [InlineData("5.", "5.0E0")]
    public void doubles_parse_and_format_canonically(string lexical, string canonical)
    {
        Assert.True(XsdDouble.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdDouble value));
        Assert.Equal(canonical, value.ToString());
        Assert.True(XsdDouble.IsCanonical(Encoding.UTF8.GetBytes(canonical)), canonical);
        Assert.True(XsdDouble.TryParse(Encoding.UTF8.GetBytes(canonical), out XsdDouble again));
        Assert.True(value.IsNaN ? again.IsNaN : value.Value.Equals(again.Value));
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("inf")]
    [InlineData("nan")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("1e")]
    [InlineData("e5")]
    [InlineData("0x10")]
    [InlineData("")]
    public void a_double_outside_the_xsd_grammar_is_no_value(string lexical)
    {
        Assert.False(XsdDouble.TryParse(Encoding.UTF8.GetBytes(lexical), out _));
    }

    [Theory]
    [InlineData("1.5", "1.5E0")]
    [InlineData("0.1", "1.0E-1")]
    [InlineData("3.4028235E38", "3.4028235E38")]
    public void floats_format_the_shortest_single_precision_mantissa(string lexical, string canonical)
    {
        Assert.True(XsdFloat.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdFloat value));
        Assert.Equal(canonical, value.ToString());
    }

    [Theory]
    [InlineData("true", true, "true")]
    [InlineData("1", true, "true")]
    [InlineData("false", false, "false")]
    [InlineData("0", false, "false")]
    public void booleans_have_four_lexical_forms_and_two_canonical_ones(string lexical, bool expected, string canonical)
    {
        Assert.True(XsdBoolean.TryParse(Encoding.UTF8.GetBytes(lexical), out XsdBoolean value));
        Assert.Equal(expected, value.Value);
        Assert.Equal(canonical, value.ToString());
        Assert.False(XsdBoolean.TryParse("True"u8, out _));
        Assert.Equal(lexical == canonical, XsdBoolean.IsCanonical(Encoding.UTF8.GetBytes(lexical)));
    }

    [Fact]
    public void strings_compare_by_code_point_in_both_encodings()
    {
        // U+FF5E FULLWIDTH TILDE sorts below U+1F600 GRINNING FACE by code
        // point, and above it by UTF-16 code unit (0xFF5E > 0xD83D).
        Assert.True(XsdString.CompareCodePoints("～", "\U0001F600") < 0);
        Assert.True(XsdString.CompareCodePoints(Encoding.UTF8.GetBytes("～"), Encoding.UTF8.GetBytes("\U0001F600")) < 0);
        Assert.Equal(0, XsdString.CompareCodePoints("abc", "abc"));
        Assert.True(XsdString.CompareCodePoints("ab", "abc") < 0);
        Assert.True(XsdString.CompareCodePoints("b"u8, "abc"u8) > 0);
    }

    [Fact]
    public void numerics_promote_as_xpath_does()
    {
        XsdNumeric one = XsdNumeric.FromInteger(new XsdInteger(1));
        XsdNumeric half = XsdNumeric.FromDecimal(Decimal("0.5"));
        XsdNumeric quarter = XsdNumeric.FromDouble(new XsdDouble(0.25));

        Assert.True(XsdNumeric.TryAdd(one, half, out XsdNumeric sum));
        Assert.Equal(XsdNumericKind.Decimal, sum.Kind);
        Assert.Equal("1.5", sum.ToString());

        Assert.True(XsdNumeric.TryAdd(sum, quarter, out XsdNumeric total));
        Assert.Equal(XsdNumericKind.Double, total.Kind);
        Assert.Equal("1.75E0", total.ToString());

        Assert.True(XsdNumeric.TryDivide(one, XsdNumeric.FromInteger(new XsdInteger(4)), out XsdNumeric quotient));
        Assert.Equal(XsdNumericKind.Decimal, quotient.Kind);
        Assert.Equal("0.25", quotient.ToString());

        Assert.Equal(PartialOrdering.Equal, XsdNumeric.Compare(quotter(), quotient));
        Assert.Equal(PartialOrdering.Indeterminate, XsdNumeric.Compare(one, XsdNumeric.FromDouble(new XsdDouble(double.NaN))));
        Assert.False(XsdNumeric.TryDivide(one, XsdNumeric.FromInteger(XsdInteger.Zero), out _));
        Assert.True(XsdNumeric.TryDivide(quarter, XsdNumeric.FromInteger(XsdInteger.Zero), out XsdNumeric infinite));
        Assert.True(double.IsPositiveInfinity(infinite.AsDouble.Value));

        static XsdNumeric quotter() => XsdNumeric.FromDouble(new XsdDouble(0.25));
    }

    [Fact]
    public void datatype_iris_map_both_ways()
    {
        foreach (XsdDatatype datatype in Enum.GetValues<XsdDatatype>())
        {
            if (datatype == XsdDatatype.None)
            {
                Assert.True(XsdDatatypes.Iri(datatype).IsEmpty);
                continue;
            }

            Assert.Equal(datatype, XsdDatatypes.FromIri(XsdDatatypes.Iri(datatype)));
        }

        Assert.Equal(XsdDatatype.None, XsdDatatypes.FromIri("http://www.w3.org/2001/XMLSchema#hexBinary"u8));
        Assert.Equal(XsdDatatype.None, XsdDatatypes.FromIri("http://www.w3.org/2001/XMLSchema#Integer"u8));
        Assert.True(XsdDatatypes.IsNumeric(XsdDatatype.UnsignedByte));
        Assert.False(XsdDatatypes.IsNumeric(XsdDatatype.Boolean));
    }

    private static XsdDecimal Decimal(string text)
    {
        Assert.True(XsdDecimal.TryParse(Encoding.UTF8.GetBytes(text), out XsdDecimal value), text);
        return value;
    }
}
