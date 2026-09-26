// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;

namespace Varve.Xsd;

/// <summary>
/// The lexical mapping (§3.3.6.2) and canonical mapping
/// (<c>durationCanonicalMap</c>, §E.2) the three duration types share.
/// </summary>
/// <remarks>
/// The grammar is the intersection of the three regular expressions §3.3.6.2
/// gives: fields in order, at least one field, and nothing ending in
/// <c>T</c>. A seconds field has digits on both sides of its point.
/// </remarks>
internal static class DurationLexical
{
    private static readonly XsdDecimal SecondsPerDay = XsdDecimal.FromInt64(86400);
    private static readonly XsdDecimal SecondsPerHour = XsdDecimal.FromInt64(3600);
    private static readonly XsdDecimal SecondsPerMinute = XsdDecimal.FromInt64(60);

    internal static bool TryParse(
        ReadOnlySpan<byte> utf8, bool allowYearMonth, bool allowDayTime, out long months, out XsdDecimal seconds)
    {
        months = 0;
        seconds = default;
        int i = 0;
        bool negative = false;

        if (i < utf8.Length && utf8[i] == (byte)'-')
        {
            negative = true;
            i++;
        }

        if (i >= utf8.Length || utf8[i] != (byte)'P')
        {
            return false;
        }

        i++;
        int fields = 0;
        long monthTotal = 0;
        XsdDecimal secondTotal = XsdDecimal.Zero;
        bool inTime = false;
        byte last = 0;

        while (i < utf8.Length)
        {
            if (utf8[i] == (byte)'T')
            {
                if (inTime || last == (byte)'T')
                {
                    return false;
                }

                inTime = true;
                last = (byte)'T';
                i++;
                continue;
            }

            int start = i;

            while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
            {
                i++;
            }

            if (i < utf8.Length && utf8[i] == (byte)'.')
            {
                i++;

                while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
                {
                    i++;
                }
            }

            if (i == start || i >= utf8.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> number = utf8[start..i];
            byte designator = utf8[i++];
            bool hasPoint = number.IndexOf((byte)'.') >= 0;

            if (hasPoint && (designator != (byte)'S' || number[0] == (byte)'.' || number[^1] == (byte)'.'))
            {
                return false;
            }

            // Fields in order: Y M before T, then H M S after it.
            int rank = designator switch
            {
                (byte)'Y' when !inTime => 1,
                (byte)'M' when !inTime => 2,
                (byte)'D' when !inTime => 3,
                (byte)'H' when inTime => 4,
                (byte)'M' when inTime => 5,
                (byte)'S' when inTime => 6,
                _ => 0,
            };

            if (rank == 0 || rank <= fields)
            {
                return false;
            }

            fields = rank;
            last = designator;

            if (rank <= 2)
            {
                if (!allowYearMonth || !XsdInteger.TryParse(number, out XsdInteger count))
                {
                    return false;
                }

                long scaled;

                if (rank == 1)
                {
                    long high = Math.BigMul(count.Value, 12, out scaled);

                    if (high != (scaled >> 63))
                    {
                        return false;
                    }
                }
                else
                {
                    scaled = count.Value;
                }

                if (!XsdInteger.TryAdd(new XsdInteger(monthTotal), new XsdInteger(scaled), out XsdInteger sum))
                {
                    return false;
                }

                monthTotal = sum.Value;
            }
            else
            {
                if (!allowDayTime || !XsdDecimal.TryParse(number, out XsdDecimal count))
                {
                    return false;
                }

                XsdDecimal factor = rank switch
                {
                    3 => SecondsPerDay,
                    4 => SecondsPerHour,
                    5 => SecondsPerMinute,
                    _ => XsdDecimal.One,
                };

                if (!XsdDecimal.TryMultiply(count, factor, out XsdDecimal scaled)
                    || !XsdDecimal.TryAdd(secondTotal, scaled, out secondTotal))
                {
                    return false;
                }
            }
        }

        if (fields == 0 || last == (byte)'T')
        {
            return false;
        }

        if (negative)
        {
            if (monthTotal == long.MinValue || !XsdDecimal.TryNegate(secondTotal, out secondTotal))
            {
                return false;
            }

            monthTotal = -monthTotal;
        }

        months = monthTotal;
        seconds = secondTotal;
        return true;
    }

    /// <summary><c>durationCanonicalMap</c>: reduced fragments, zero ones omitted, <c>PT0S</c> for zero.</summary>
    internal static bool TryFormat(long months, XsdDecimal seconds, Span<byte> destination, out int written)
    {
        written = 0;
        int i = 0;

        if (months < 0 || seconds.IsNegative)
        {
            if (!Write("-"u8, destination, ref i))
            {
                return false;
            }
        }

        if (!Write("P"u8, destination, ref i))
        {
            return false;
        }

        ulong monthMagnitude = months < 0 ? unchecked((ulong)(-(months + 1))) + 1 : (ulong)months;

        if (monthMagnitude != 0)
        {
            ulong years = monthMagnitude / 12;
            ulong remaining = monthMagnitude % 12;

            if (years != 0 && !WriteField(years, (byte)'Y', destination, ref i))
            {
                return false;
            }

            if (remaining != 0 && !WriteField(remaining, (byte)'M', destination, ref i))
            {
                return false;
            }
        }

        if (!seconds.IsZero)
        {
            XsdDecimal magnitude = seconds.IsNegative ? -seconds : seconds;
            Int128 whole = magnitude.Mantissa / 1_000_000_000_000_000_000;
            XsdDecimal fraction = magnitude - XsdDecimal.FromMantissa(whole * 1_000_000_000_000_000_000);
            ulong wholeSeconds = (ulong)whole;
            ulong days = wholeSeconds / 86400;
            ulong hours = wholeSeconds % 86400 / 3600;
            ulong minutes = wholeSeconds % 3600 / 60;
            ulong wholeSecond = wholeSeconds % 60;

            if (days != 0 && !WriteField(days, (byte)'D', destination, ref i))
            {
                return false;
            }

            if (hours != 0 || minutes != 0 || wholeSecond != 0 || !fraction.IsZero)
            {
                if (!Write("T"u8, destination, ref i))
                {
                    return false;
                }

                if (hours != 0 && !WriteField(hours, (byte)'H', destination, ref i))
                {
                    return false;
                }

                if (minutes != 0 && !WriteField(minutes, (byte)'M', destination, ref i))
                {
                    return false;
                }

                if (wholeSecond != 0 || !fraction.IsZero)
                {
                    XsdDecimal second = XsdDecimal.FromInt64((long)wholeSecond) + fraction;

                    if (!second.TryFormat(destination[i..], out int length))
                    {
                        return false;
                    }

                    i += length;

                    if (!Write("S"u8, destination, ref i))
                    {
                        return false;
                    }
                }
            }
        }
        else if (monthMagnitude == 0 && !Write("T0S"u8, destination, ref i))
        {
            return false;
        }

        written = i;
        return true;
    }

    private static bool WriteField(ulong value, byte designator, Span<byte> destination, ref int i)
    {
        if (!Lexical.TryWriteUnsigned(value, destination[i..], out int digits))
        {
            return false;
        }

        i += digits;

        if (i >= destination.Length)
        {
            return false;
        }

        destination[i++] = designator;
        return true;
    }

    private static bool Write(ReadOnlySpan<byte> text, Span<byte> destination, ref int i)
    {
        if (i + text.Length > destination.Length)
        {
            return false;
        }

        text.CopyTo(destination[i..]);
        i += text.Length;
        return true;
    }

    /// <summary>
    /// The order of §3.3.6.1: add both durations to each of the four
    /// reference dateTimes and compare; ordered only when all four agree.
    /// </summary>
    internal static PartialOrdering CompareXsd(long leftMonths, XsdDecimal leftSeconds, long rightMonths, XsdDecimal rightSeconds)
    {
        if (leftMonths == rightMonths)
        {
            return Ordering(leftSeconds.CompareTo(rightSeconds));
        }

        if (leftSeconds == rightSeconds)
        {
            return Ordering(leftMonths.CompareTo(rightMonths));
        }

        PartialOrdering? verdict = null;

        foreach (SevenProperties reference in References)
        {
            if (!SevenPropertyModel.TryAdd(in reference, leftMonths, leftSeconds, out SevenProperties a)
                || !SevenPropertyModel.TryAdd(in reference, rightMonths, rightSeconds, out SevenProperties b))
            {
                return PartialOrdering.Indeterminate;
            }

            PartialOrdering here = Ordering(SevenPropertyModel.Compare(in a, in b, 0));

            if (verdict is null)
            {
                verdict = here;
            }
            else if (verdict != here)
            {
                return PartialOrdering.Indeterminate;
            }
        }

        return verdict ?? PartialOrdering.Indeterminate;
    }

    private static PartialOrdering Ordering(int comparison) =>
        comparison < 0 ? PartialOrdering.Less : comparison > 0 ? PartialOrdering.Greater : PartialOrdering.Equal;

    // The four reference dateTimes the duration order is decided against.
    // Immutable: a static array was writable by anyone holding it (DD0004).
    private static readonly ImmutableArray<SevenProperties> References =
    [
        Reference(1696, 9, 1),
        Reference(1697, 2, 1),
        Reference(1903, 3, 1),
        Reference(1903, 7, 1),
    ];

    private static SevenProperties Reference(int year, int month, int day) =>
        new(DateTimeFields.DateTime, year, month, day, 0, 0, default, 0);
}
