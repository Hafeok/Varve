// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Xsd.Tests;

/// <summary>
/// The properties <c>docs/spec/xsd.md</c> §8 names as the gate until the
/// SPARQL evaluation suite can be one: round trips, canonical idempotence,
/// the orders with their incomparable pairs generated explicitly, and
/// arithmetic against <see cref="BigInteger"/> and <see cref="decimal"/> as
/// oracles where the ranges overlap.
/// </summary>
public class PropertyTests
{
    private const int Iterations = 2_000;

    private static readonly BigInteger Scale = BigInteger.Pow(10, XsdDecimal.Scale);

    // --- generators ---------------------------------------------------------

    private static readonly Gen<XsdInteger> Integer = Gen.Frequency(
        (8, Gen.Long.Select(v => new XsdInteger(v))),
        (1, Gen.OneOfConst(new XsdInteger(long.MinValue), new XsdInteger(long.MaxValue), XsdInteger.Zero, new XsdInteger(-1))),
        (3, Gen.Long[-1000, 1000].Select(v => new XsdInteger(v))));

    /// <summary>Mantissas across the whole 128-bit range, biased toward the edges and toward small values.</summary>
    private static readonly Gen<XsdDecimal> Decimal = Gen.Frequency(
        (6, Gen.Select(Gen.Long, Gen.Int[0, 64], (m, shift) => XsdDecimal.FromMantissa(unchecked((Int128)m << shift)))),
        (1, Gen.OneOfConst(XsdDecimal.MaxValue, XsdDecimal.MinValue, XsdDecimal.Zero, XsdDecimal.One, -XsdDecimal.One)),
        (3, Gen.Long[-100_000, 100_000].Select(XsdDecimal.FromInt64)),
        (3, Gen.Long[-1_000_000_000_000_000, 1_000_000_000_000_000].Select(m => XsdDecimal.FromMantissa(m))));

    /// <summary>Values whose magnitude fits <see cref="decimal"/>'s 28 digits alongside 18 fractional ones.</summary>
    private static readonly Gen<XsdDecimal> SmallDecimal =
        Gen.Long[-9_999_999_999_999_999, 9_999_999_999_999_999].Select(m => XsdDecimal.FromMantissa((Int128)m * 1000));

    private static readonly Gen<XsdDouble> Double = Gen.Frequency(
        (8, Gen.Double.Select(d => new XsdDouble(d))),
        (2, Gen.OneOfConst(
            new XsdDouble(double.NaN), new XsdDouble(double.PositiveInfinity), new XsdDouble(double.NegativeInfinity),
            new XsdDouble(0.0), new XsdDouble(-0.0), new XsdDouble(double.Epsilon), new XsdDouble(double.MaxValue),
            new XsdDouble(1e16), new XsdDouble(0.1), new XsdDouble(123456.789))));

    private static readonly Gen<XsdFloat> Float = Gen.Frequency(
        (8, Gen.Float.Select(f => new XsdFloat(f))),
        (2, Gen.OneOfConst(
            new XsdFloat(float.NaN), new XsdFloat(float.PositiveInfinity), new XsdFloat(-0f), new XsdFloat(float.Epsilon),
            new XsdFloat(float.MaxValue), new XsdFloat(0.1f))));

    private static readonly Gen<int?> Timezone = Gen.Frequency(
        (1, Gen.Const((int?)null)),
        (1, Gen.Const((int?)0)),
        (2, Gen.Int[-840, 840].Select(v => (int?)v)));

    private static readonly Gen<XsdDecimal> Second = Gen.Frequency(
        (3, Gen.Int[0, 59].Select(s => XsdDecimal.FromInt64(s))),
        (2, Gen.Select(Gen.Int[0, 59], Gen.Long[1, 999_999_999_999_999_999], (s, f) => XsdDecimal.FromMantissa(((Int128)s * 1_000_000_000_000_000_000) + f))));

    private static readonly Gen<int> Year = Gen.Frequency(
        (6, Gen.Int[1, 9999]),
        (2, Gen.Int[-9999, 0]),
        (1, Gen.Int[10000, 999_999]));

    private static readonly Gen<XsdDateTime> DateTime =
        Gen.Select(Year, Gen.Int[1, 12], Gen.Int[1, 31], Gen.Int[0, 23], Gen.Int[0, 59], Second, Timezone)
            .Select(t => new XsdDateTime(
                t.Item1, t.Item2, Math.Min(t.Item3, DaysInMonth(t.Item1, t.Item2)), t.Item4, t.Item5, t.Item6, t.Item7));

    /// <summary>Years the runtime's calendar covers, whole seconds, always timezoned: the oracle's domain.</summary>
    private static readonly Gen<XsdDateTime> OracleDateTime =
        Gen.Select(Gen.Int[1, 9999], Gen.Int[1, 12], Gen.Int[1, 31], Gen.Int[0, 23], Gen.Int[0, 59], Gen.Int[0, 59], Gen.Int[-840, 840])
            .Select(t => new XsdDateTime(
                t.Item1, t.Item2, Math.Min(t.Item3, DaysInMonth(t.Item1, t.Item2)), t.Item4, t.Item5, XsdDecimal.FromInt64(t.Item6), t.Item7));

    private static readonly Gen<XsdDuration> Duration = Gen.Frequency(
        (4, Gen.Select(Gen.Long[0, 100_000], Gen.Long[0, 100_000_000_000], (m, s) => new XsdDuration(m, XsdDecimal.FromInt64(s)))),
        (4, Gen.Select(Gen.Long[0, 100_000], Gen.Long[0, 100_000_000_000], (m, s) => new XsdDuration(-m, XsdDecimal.FromInt64(-s)))),
        (2, Gen.Select(Gen.Long[0, 1000], Gen.Long[0, 999_999_999_999_999_999], (m, f) => new XsdDuration(m, XsdDecimal.FromMantissa(f)))),
        (1, Gen.OneOfConst(new XsdDuration(0, XsdDecimal.Zero))));

    private static int DaysInMonth(int year, int month) => month switch
    {
        2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    // --- round trips and canonical forms ------------------------------------

    [Fact]
    public void integers_round_trip_through_their_canonical_form()
    {
        Integer.Sample(value => RoundTrips(value, XsdInteger.TryParse, static bytes => XsdInteger.IsCanonical(bytes)), iter: Iterations);
    }

    [Fact]
    public void decimals_round_trip_through_their_canonical_form()
    {
        Decimal.Sample(value => RoundTrips(value, XsdDecimal.TryParse, static bytes => XsdDecimal.IsCanonical(bytes)), iter: Iterations);
    }

    [Fact]
    public void doubles_round_trip_through_their_canonical_form()
    {
        Double.Sample(value =>
        {
            string canonical = value.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(canonical);

            if (!XsdDouble.TryParse(bytes, out XsdDouble again) || !XsdDouble.IsCanonical(bytes))
            {
                return false;
            }

            // NaN is equal to nothing, so compare bits for it and for the zeros.
            return BitConverter.DoubleToInt64Bits(value.Value) == BitConverter.DoubleToInt64Bits(again.Value)
                && again.ToString() == canonical;
        }, iter: Iterations);
    }

    [Fact]
    public void floats_round_trip_through_their_canonical_form()
    {
        Float.Sample(value =>
        {
            string canonical = value.ToString();
            byte[] bytes = Encoding.UTF8.GetBytes(canonical);

            return XsdFloat.TryParse(bytes, out XsdFloat again)
                && XsdFloat.IsCanonical(bytes)
                && BitConverter.SingleToInt32Bits(value.Value) == BitConverter.SingleToInt32Bits(again.Value)
                && again.ToString() == canonical;
        }, iter: Iterations);
    }

    [Fact]
    public void dateTimes_round_trip_through_their_canonical_form()
    {
        DateTime.Sample(value => RoundTrips(value, XsdDateTime.TryParse, static bytes => XsdDateTime.IsCanonical(bytes)), iter: Iterations);
    }

    [Fact]
    public void durations_round_trip_through_their_canonical_form()
    {
        Duration.Sample(value => RoundTrips(value, XsdDuration.TryParse, static bytes => XsdDuration.IsCanonical(bytes)), iter: Iterations);
    }

    /// <summary>
    /// Formatting is a fixed point, and <c>IsCanonical</c> holds on formatted
    /// output and on nothing that formats differently.
    /// </summary>
    [Fact]
    public void a_non_canonical_form_is_recognised_as_such()
    {
        Gen.Select(Integer, Gen.OneOfConst("+", "0", "00")).Sample((value, prefix) =>
        {
            string canonical = value.ToString();
            string padded = value.IsNegative ? "-" + prefix.TrimStart('+') + "0" + canonical[1..] : prefix + canonical;
            bool sameValue = XsdInteger.TryParse(Encoding.UTF8.GetBytes(padded), out XsdInteger parsed) && parsed == value;
            return sameValue && !XsdInteger.IsCanonical(Encoding.UTF8.GetBytes(padded));
        }, iter: Iterations);

        Decimal.Sample(value =>
        {
            string trailing = value.ToString() + (value.IsInteger ? ".0" : "0");
            return XsdDecimal.TryParse(Encoding.UTF8.GetBytes(trailing), out XsdDecimal parsed)
                && parsed == value
                && !XsdDecimal.IsCanonical(Encoding.UTF8.GetBytes(trailing));
        }, iter: Iterations);
    }

    private delegate bool Parser<T>(ReadOnlySpan<byte> utf8, out T value);

    private delegate bool CharFormatter<T>(T value, Span<char> destination, out int written);

    private static bool RoundTrips<T>(T value, Parser<T> parse, Func<byte[], bool> isCanonical)
        where T : struct, IEquatable<T>
    {
        return RoundTrips(value, parse, isCanonical, CharFormat<T>());
    }

    private static CharFormatter<T> CharFormat<T>()
        where T : struct
    {
        if (typeof(T) == typeof(XsdInteger))
        {
            return (CharFormatter<T>)(object)new CharFormatter<XsdInteger>((XsdInteger v, Span<char> d, out int w) => v.TryFormat(d, out w));
        }

        if (typeof(T) == typeof(XsdDecimal))
        {
            return (CharFormatter<T>)(object)new CharFormatter<XsdDecimal>((XsdDecimal v, Span<char> d, out int w) => v.TryFormat(d, out w));
        }

        if (typeof(T) == typeof(XsdDateTime))
        {
            return (CharFormatter<T>)(object)new CharFormatter<XsdDateTime>((XsdDateTime v, Span<char> d, out int w) => v.TryFormat(d, out w));
        }

        return (CharFormatter<T>)(object)new CharFormatter<XsdDuration>((XsdDuration v, Span<char> d, out int w) => v.TryFormat(d, out w));
    }

    private static bool RoundTrips<T>(T value, Parser<T> parse, Func<byte[], bool> isCanonical, CharFormatter<T> formatChars)
        where T : struct, IEquatable<T>
    {
        string canonical = value.ToString()!;
        byte[] bytes = Encoding.UTF8.GetBytes(canonical);

        if (!parse(bytes, out T again) || !again.Equals(value) || !isCanonical(bytes))
        {
            return false;
        }

        // Idempotence: the parsed value formats to the same bytes.
        if (again.ToString() != canonical)
        {
            return false;
        }

        // The char path agrees with the byte path.
        Span<char> chars = stackalloc char[256];
        return formatChars(value, chars, out int written) && new string(chars[..written]) == canonical;
    }

    // --- orders -------------------------------------------------------------

    [Fact]
    public void the_decimal_order_is_total()
    {
        Gen.Select(Decimal, Decimal, Decimal).Sample((a, b, c) => IsTotalOrder(a, b, c, static (x, y) => x.CompareTo(y)), iter: Iterations);
    }

    [Fact]
    public void the_integer_order_is_total()
    {
        Gen.Select(Integer, Integer, Integer).Sample((a, b, c) => IsTotalOrder(a, b, c, static (x, y) => x.CompareTo(y)), iter: Iterations);
    }

    [Fact]
    public void the_implicit_timezone_dateTime_order_is_total()
    {
        Gen.Select(DateTime, DateTime, DateTime, Gen.Int[-840, 840]).Sample(
            (a, b, c, implicitOffset) => IsTotalOrder(a, b, c, (x, y) => XsdDateTime.Compare(x, y, implicitOffset)),
            iter: Iterations);
    }

    [Fact]
    public void the_derived_duration_orders_are_total()
    {
        Gen<XsdDayTimeDuration> dayTime = Duration.Select(d => new XsdDayTimeDuration(d.Seconds));
        Gen<XsdYearMonthDuration> yearMonth = Duration.Select(d => new XsdYearMonthDuration(d.Months));

        Gen.Select(dayTime, dayTime, dayTime).Sample((a, b, c) => IsTotalOrder(a, b, c, static (x, y) => x.CompareTo(y)), iter: Iterations);
        Gen.Select(yearMonth, yearMonth, yearMonth).Sample((a, b, c) => IsTotalOrder(a, b, c, static (x, y) => x.CompareTo(y)), iter: Iterations);
    }

    private static bool IsTotalOrder<T>(T a, T b, T c, Func<T, T, int> compare)
    {
        int ab = Math.Sign(compare(a, b));
        int ba = Math.Sign(compare(b, a));
        int bc = Math.Sign(compare(b, c));
        int ac = Math.Sign(compare(a, c));

        bool antisymmetric = ab == -ba && Math.Sign(compare(a, a)) == 0;
        bool transitive = !(ab <= 0 && bc <= 0) || ac <= 0;
        bool transitiveStrict = !(ab < 0 && bc < 0) || ac < 0;
        return antisymmetric && transitive && transitiveStrict;
    }

    /// <summary>
    /// XSD's dateTime order is partial: a timezoned and an untimezoned value
    /// are ordered exactly when they are more than fourteen hours apart under
    /// every imputation, and the reverse comparison always agrees.
    /// </summary>
    [Fact]
    public void the_xsd_dateTime_order_is_partial_with_the_fourteen_hour_band_indeterminate()
    {
        Gen<XsdDateTime> zoned = DateTime.Where(d => d.HasTimezone);
        Gen<XsdDateTime> unzoned = DateTime.Where(d => !d.HasTimezone);

        Gen.Select(zoned, unzoned).Sample((p, q) =>
        {
            XsdDecimal distance = p.TimeOnTimeline(0) - q.TimeOnTimeline(0);
            XsdDecimal band = XsdDecimal.FromInt64(14 * 3600);
            PartialOrdering expected = distance > band ? PartialOrdering.Greater
                : distance < -band ? PartialOrdering.Less
                : PartialOrdering.Indeterminate;

            PartialOrdering forward = XsdDateTime.CompareXsd(p, q);
            PartialOrdering backward = XsdDateTime.CompareXsd(q, p);
            return forward == expected && backward == Reverse(forward);
        }, iter: Iterations);

        // Same-kind pairs are always comparable, and agree with the total order at any implicit timezone.
        Gen.Select(DateTime, DateTime, Gen.Int[-840, 840]).Sample((a, b, implicitOffset) =>
        {
            if (a.HasTimezone != b.HasTimezone)
            {
                return true;
            }

            PartialOrdering partial = XsdDateTime.CompareXsd(a, b);
            int total = XsdDateTime.Compare(a, b, implicitOffset);
            return partial != PartialOrdering.Indeterminate && Math.Sign(total) == Sign(partial);
        }, iter: Iterations);
    }

    /// <summary>
    /// XSD's duration order, against the runtime's calendar as an oracle: a
    /// months-only duration against a days-only one is ordered exactly when
    /// the four reference dates agree, and it is the days between 28n and 31n
    /// that make it indeterminate.
    /// </summary>
    [Fact]
    public void the_xsd_duration_order_is_partial_and_agrees_with_the_calendar_oracle()
    {
        DateTimeOffset[] references =
        [
            new(1696, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new(1697, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new(1903, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new(1903, 7, 1, 0, 0, 0, TimeSpan.Zero),
        ];

        Gen.Select(Gen.Int[1, 36], Gen.Int[27, 1130]).Sample((months, days) =>
        {
            XsdDuration monthly = new(months, XsdDecimal.Zero);
            XsdDuration daily = new(0, XsdDecimal.FromInt64(days * 86400L));

            List<int> verdicts = [];

            foreach (DateTimeOffset reference in references)
            {
                double length = (reference.AddMonths(months) - reference).TotalDays;
                verdicts.Add(Math.Sign(length.CompareTo(days)));
            }

            PartialOrdering expected = verdicts.TrueForAll(v => v < 0) ? PartialOrdering.Less
                : verdicts.TrueForAll(v => v > 0) ? PartialOrdering.Greater
                : verdicts.TrueForAll(v => v == 0) ? PartialOrdering.Equal
                : PartialOrdering.Indeterminate;

            return XsdDuration.CompareXsd(monthly, daily) == expected
                && XsdDuration.CompareXsd(daily, monthly) == Reverse(expected);
        }, iter: Iterations);
    }

    private static PartialOrdering Reverse(PartialOrdering ordering) => ordering switch
    {
        PartialOrdering.Less => PartialOrdering.Greater,
        PartialOrdering.Greater => PartialOrdering.Less,
        _ => ordering,
    };

    private static int Sign(PartialOrdering ordering) => ordering switch
    {
        PartialOrdering.Less => -1,
        PartialOrdering.Greater => 1,
        _ => 0,
    };

    // --- arithmetic against oracles -------------------------------------------

    [Fact]
    public void decimal_arithmetic_agrees_with_biginteger_including_where_it_overflows()
    {
        Gen.Select(Decimal, Decimal).Sample((a, b) =>
        {
            BigInteger x = a.Mantissa;
            BigInteger y = b.Mantissa;

            return Agrees(XsdDecimal.TryAdd(a, b, out XsdDecimal sum), sum, x + y)
                && Agrees(XsdDecimal.TrySubtract(a, b, out XsdDecimal difference), difference, x - y)
                && Agrees(XsdDecimal.TryMultiply(a, b, out XsdDecimal product), product, BigInteger.Divide(x * y, Scale))
                && (b.IsZero
                    ? !XsdDecimal.TryDivide(a, b, out _)
                    : Agrees(XsdDecimal.TryDivide(a, b, out XsdDecimal quotient), quotient, BigInteger.Divide(x * Scale, y)));
        }, iter: Iterations);

        static bool Agrees(bool ok, XsdDecimal result, BigInteger expected)
        {
            bool fits = expected >= (BigInteger)Int128.MinValue && expected <= (BigInteger)Int128.MaxValue;
            return ok == fits && (!ok || (BigInteger)result.Mantissa == expected);
        }
    }

    [Fact]
    public void decimal_addition_and_subtraction_agree_with_system_decimal()
    {
        Gen.Select(SmallDecimal, SmallDecimal).Sample((a, b) =>
        {
            decimal x = decimal.Parse(a.ToString(), CultureInfo.InvariantCulture);
            decimal y = decimal.Parse(b.ToString(), CultureInfo.InvariantCulture);

            return XsdDecimal.TryAdd(a, b, out XsdDecimal sum) && Same(sum, x + y)
                && XsdDecimal.TrySubtract(a, b, out XsdDecimal difference) && Same(difference, x - y)
                && Same(a.Floor(), Math.Floor(x))
                && Same(a.Ceiling(), Math.Ceiling(x))
                && a.TryRound(out XsdDecimal rounded) && Same(rounded, Math.Floor(x + 0.5m));
        }, iter: Iterations);

        static bool Same(XsdDecimal value, decimal expected) =>
            decimal.Parse(value.ToString(), CultureInfo.InvariantCulture) == expected;
    }

    [Fact]
    public void integer_arithmetic_agrees_with_biginteger_including_where_it_overflows()
    {
        Gen.Select(Integer, Integer).Sample((a, b) =>
        {
            BigInteger x = a.Value;
            BigInteger y = b.Value;

            return Agrees(XsdInteger.TryAdd(a, b, out XsdInteger sum), sum, x + y)
                && Agrees(XsdInteger.TrySubtract(a, b, out XsdInteger difference), difference, x - y)
                && Agrees(XsdInteger.TryMultiply(a, b, out XsdInteger product), product, x * y)
                && (b.IsZero
                    ? !XsdInteger.TryDivide(a, b, out _)
                    : Agrees(XsdInteger.TryDivide(a, b, out XsdInteger quotient), quotient, BigInteger.Divide(x, y)))
                && Agrees(XsdInteger.TryNegate(a, out XsdInteger negated), negated, -x);
        }, iter: Iterations);

        static bool Agrees(bool ok, XsdInteger result, BigInteger expected)
        {
            bool fits = expected >= long.MinValue && expected <= long.MaxValue;
            return ok == fits && (!ok || result.Value == expected);
        }
    }

    [Fact]
    public void promotion_from_integer_to_decimal_never_fails_and_is_exact()
    {
        Integer.Sample(value =>
        {
            XsdDecimal promoted = XsdDecimal.FromInteger(value);
            return promoted.IsInteger
                && promoted.TryToInteger(out XsdInteger back)
                && back == value
                && (BigInteger)promoted.Mantissa == (BigInteger)value.Value * Scale;
        }, iter: Iterations);
    }

    [Fact]
    public void time_on_timeline_agrees_with_the_runtime_calendar()
    {
        Gen.Select(OracleDateTime, OracleDateTime).Sample((a, b) =>
        {
            DateTimeOffset x = ToRuntime(a);
            DateTimeOffset y = ToRuntime(b);
            XsdDecimal difference = a.TimeOnTimeline(0) - b.TimeOnTimeline(0);
            long expected = (x - y).Ticks / TimeSpan.TicksPerSecond;

            return difference == XsdDecimal.FromInt64(expected)
                && Math.Sign(XsdDateTime.Compare(a, b, 0)) == Math.Sign(x.CompareTo(y))
                && a.Equals(b) == (x == y);
        }, iter: Iterations);

        static DateTimeOffset ToRuntime(XsdDateTime value)
        {
            Assert.True(value.Second.TryToInteger(out XsdInteger second));
            return new DateTimeOffset(
                value.Year, value.Month, value.Day, value.Hour, value.Minute, (int)second.Value,
                TimeSpan.FromMinutes(value.TimezoneOffset));
        }
    }

    [Fact]
    public void adding_a_day_time_duration_agrees_with_the_runtime_calendar()
    {
        Gen.Select(OracleDateTime, Gen.Long[-3_000_000_000, 3_000_000_000]).Sample((start, seconds) =>
        {
            DateTimeOffset runtime = new(
                start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, TimeSpan.FromMinutes(start.TimezoneOffset));
            Assert.True(start.Second.TryToInteger(out XsdInteger second));
            DateTimeOffset expected;

            try
            {
                expected = runtime.AddSeconds(second.Value + seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }

            if (expected.Year < 1)
            {
                return true;
            }

            XsdDayTimeDuration duration = new(XsdDecimal.FromInt64(seconds));
            return start.TryAdd(duration, out XsdDateTime end)
                && end.Year == expected.Year && end.Month == expected.Month && end.Day == expected.Day
                && end.Hour == expected.Hour && end.Minute == expected.Minute
                && end.Second == XsdDecimal.FromInt64(expected.Second)
                && end.TimezoneOffset == start.TimezoneOffset;
        }, iter: Iterations);
    }
}
