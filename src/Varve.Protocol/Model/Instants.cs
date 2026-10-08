// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>
/// RFC 3339 instants as the protocol reads and writes them (ADRs 0096, 0097):
/// read with any offset and at most seven fractional digits, written in UTC
/// with exactly seven and <c>Z</c>.
/// </summary>
public static class Instants
{
    /// <summary>
    /// Reads an RFC 3339 <c>date-time</c>. <c>T</c> and <c>Z</c> may be lower
    /// case, as RFC 3339 §5.6 allows; more than seven fractional digits are
    /// refused rather than truncated.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out CommitTimestamp time)
    {
        time = default;

        // date-time = full-date "T" partial-time time-offset
        if (text.Length < 20 || !IsDecimal(text[..4]) || text[4] != '-' || !IsDecimal(text.Slice(5, 2)) || text[7] != '-'
            || !IsDecimal(text.Slice(8, 2)) || (text[10] is not ('T' or 't')) || !IsDecimal(text.Slice(11, 2)) || text[13] != ':'
            || !IsDecimal(text.Slice(14, 2)) || text[16] != ':' || !IsDecimal(text.Slice(17, 2)))
        {
            return false;
        }

        int index = 19;
        int fraction = 0;

        if (index < text.Length && text[index] == '.')
        {
            index++;
            int start = index;

            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            fraction = index - start;

            if (fraction is 0 or > 7)
            {
                return false;
            }
        }

        ReadOnlySpan<char> offset = text[index..];
        TimeSpan shift;

        if (offset is "Z" or "z")
        {
            shift = TimeSpan.Zero;
        }
        else if (offset.Length == 6 && offset[0] is '+' or '-' && IsDecimal(offset.Slice(1, 2)) && offset[3] == ':' && IsDecimal(offset.Slice(4, 2)))
        {
            int hours = int.Parse(offset.Slice(1, 2), NumberStyles.None, CultureInfo.InvariantCulture);
            int minutes = int.Parse(offset.Slice(4, 2), NumberStyles.None, CultureInfo.InvariantCulture);

            if (hours > 23 || minutes > 59)
            {
                return false;
            }

            shift = new TimeSpan(hours, minutes, 0);
            shift = offset[0] == '-' ? -shift : shift;
        }
        else
        {
            return false;
        }

        int year = Number(text[..4]);
        int month = Number(text.Slice(5, 2));
        int day = Number(text.Slice(8, 2));
        int hour = Number(text.Slice(11, 2));
        int minute = Number(text.Slice(14, 2));
        int second = Number(text.Slice(17, 2));
        long ticks = 0;

        if (fraction > 0)
        {
            ticks = Number(text.Slice(20, fraction));

            for (int i = fraction; i < 7; i++)
            {
                ticks *= 10;
            }
        }

        // RFC 3339 allows a leap second (60); .NET has none, and the store's
        // clock never produced one, so it is refused rather than moved.
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        try
        {
            DateTimeOffset local = new DateTimeOffset(year, month, day, hour, minute, second, shift).AddTicks(ticks);
            time = new CommitTimestamp(local.ToUniversalTime());
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int Number(ReadOnlySpan<char> digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Writes an instant in UTC with seven fractional digits and <c>Z</c>.</summary>
    public static string Format(CommitTimestamp time) =>
        time.Value.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff'Z'", CultureInfo.InvariantCulture);

    internal static bool IsDecimal(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
