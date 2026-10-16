// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Numerics;
using CsCheck;
using Varve.Xsd;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>
/// The number rule (ADR 0143): a JSON number is emitted only when the
/// shortest ECMAScript form of the nearest double equals the literal's exact
/// decimal value. The oracle here is independent of the renderer: exact
/// rational arithmetic over BigInteger, and .NET's own shortest round-trip
/// digits.
/// </summary>
public sealed class NumberRuleTests
{
    internal const int Iterations = 5_000;

    [Theory]
    [InlineData("19.99", true)]
    [InlineData("0.1", true)]
    [InlineData("1.50", true)]
    [InlineData("-0.0", true)]
    [InlineData("9007199254740992", true)]
    [InlineData("9007199254740993", false)]
    [InlineData("18446744073709551616", false)]
    [InlineData("1000000000000000000000", true)]
    [InlineData("18446744073709551615", false)]
    [InlineData("12345678901234567890.5", false)]
    [InlineData("0.12345678901234567890", false)]
    [InlineData("1e400", false)]
    public void decimals_and_integers(string lexical, bool emitted) =>
        Assert.Equal(emitted, ValueMapping.TryNumber(lexical, XsdDatatype.Decimal, out _));

    [Theory]
    [InlineData("INF")]
    [InlineData("-INF")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    public void a_non_finite_double_has_no_number(string lexical) =>
        Assert.False(ValueMapping.TryNumber(lexical, XsdDatatype.Double, out _));

    /// <summary>Decimals of 1 to 25 significant digits around 2^53, 2^63 and 2^64 and in ordinary ranges.</summary>
    private static readonly Gen<string> Decimals =
        Gen.OneOf(
            Gen.Select(Gen.Int[1, 25], Gen.Int[-30, 30], Gen.Bool, Gen.ULong, Gen.ULong, (digits, scale, negative, a, b) =>
            {
                BigInteger mantissa = (new BigInteger(a) << 64 | b) % BigInteger.Pow(10, digits);
                return Render(negative ? -mantissa : mantissa, scale);
            }),
            Gen.Select(Gen.OneOfConst(53, 63, 64, 70), Gen.Int[-3, 3], Gen.Bool, (power, offset, negative) =>
            {
                BigInteger value = BigInteger.Pow(2, power) + offset;
                return (negative ? -value : value).ToString(CultureInfo.InvariantCulture);
            }));

    [Fact]
    public void the_rule_holds_for_generated_decimals() =>
        Decimals.Sample(lexical =>
        {
            bool emitted = ValueMapping.TryNumber(lexical, XsdDatatype.Decimal, out double value);
            double nearest = double.Parse(lexical, NumberStyles.Float, CultureInfo.InvariantCulture);
            bool expected = double.IsFinite(nearest) && Exact(nearest.ToString("R", CultureInfo.InvariantCulture)) == Exact(lexical);

            if (emitted != expected)
            {
                return false;
            }

            // An emitted number's JSON text is the literal's exact value, and
            // parses back to the same double.
            return !emitted || (value.Equals(nearest) && Exact(CanonicalText(value)) == Exact(lexical));
        }, iter: Iterations);

    [Fact]
    public void every_finite_double_is_a_number() =>
        Gen.Double.Where(double.IsFinite).Sample(d =>
            ValueMapping.TryNumber(d.ToString("R", CultureInfo.InvariantCulture), XsdDatatype.Double, out double value) && value.Equals(d),
            iter: Iterations);

    private static string Render(BigInteger mantissa, int scale)
    {
        string digits = BigInteger.Abs(mantissa).ToString(CultureInfo.InvariantCulture);
        string sign = mantissa.Sign < 0 ? "-" : "";

        if (scale <= 0)
        {
            return sign + digits + new string('0', -scale);
        }

        digits = digits.PadLeft(scale + 1, '0');
        return sign + digits[..^scale] + "." + digits[^scale..];
    }

    /// <summary>The exact value of a numeral as a reduced fraction, BigInteger over a power of ten.</summary>
    private static (BigInteger Numerator, BigInteger Denominator) Exact(string text)
    {
        int e = text.IndexOfAny(['e', 'E']);
        int exponent = e < 0 ? 0 : int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string body = e < 0 ? text : text[..e];
        int point = body.IndexOf('.');

        if (point >= 0)
        {
            exponent -= body.Length - point - 1;
            body = body.Remove(point, 1);
        }

        BigInteger numerator = BigInteger.Parse(body, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.One;

        if (exponent >= 0)
        {
            numerator *= BigInteger.Pow(10, exponent);
        }
        else
        {
            denominator = BigInteger.Pow(10, -exponent);
        }

        BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return gcd.IsZero ? (0, 1) : (numerator / gcd, denominator / gcd);
    }

    private static string CanonicalText(double value)
    {
        Span<byte> buffer = stackalloc byte[Varve.Json.Canonical.CanonicalJson.MaxNumberLength];
        int written = Varve.Json.Canonical.CanonicalJson.FormatNumber(value, buffer);
        return System.Text.Encoding.ASCII.GetString(buffer[..written]);
    }
}
