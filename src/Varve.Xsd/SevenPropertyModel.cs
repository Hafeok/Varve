// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Xsd;

/// <summary>Which of the seven properties a date/time datatype carries.</summary>
[Flags]
internal enum DateTimeFields : byte
{
    None = 0,
    Year = 1,
    Month = 2,
    Day = 4,
    Time = 8,
    Date = Year | Month | Day,
    DateTime = Date | Time,
}

/// <summary>
/// The seven-property model of XML Schema 1.1 Part 2 §D.2.1, shared by every
/// date and time type: year, month, day, hour, minute, second and an optional
/// timezone offset in minutes, with the properties a datatype leaves absent
/// held at zero and masked by <see cref="Present"/>.
/// </summary>
internal readonly struct SevenProperties : IEquatable<SevenProperties>
{
    /// <summary>The timezone offset value meaning "absent".</summary>
    internal const short NoTimezone = short.MinValue;

    internal SevenProperties(
        DateTimeFields present, int year, int month, int day, int hour, int minute, XsdDecimal second, short timezoneOffset)
    {
        Present = present;
        Year = year;
        Month = (byte)month;
        Day = (byte)day;
        Hour = (byte)hour;
        Minute = (byte)minute;
        Second = second;
        TimezoneOffset = timezoneOffset;
    }

    internal DateTimeFields Present { get; }

    internal int Year { get; }

    internal byte Month { get; }

    internal byte Day { get; }

    internal byte Hour { get; }

    internal byte Minute { get; }

    internal XsdDecimal Second { get; }

    /// <summary>Minutes east of UTC, or <see cref="NoTimezone"/>.</summary>
    internal short TimezoneOffset { get; }

    internal bool HasTimezone => TimezoneOffset != NoTimezone;

    internal bool Has(DateTimeFields field) => (Present & field) != 0;

    /// <summary>XSD equality: the same time-line position and the same timezone presence.</summary>
    public bool Equals(SevenProperties other) => SevenPropertyModel.CompareXsd(in this, in other) == PartialOrdering.Equal;

    public override bool Equals(object? obj) => obj is SevenProperties other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        SevenPropertyModel.TimeOnTimeline(in this, HasTimezone ? TimezoneOffset : 0), HasTimezone);
}

/// <summary>
/// The lexical mappings (§D.2.2, §E.3.5), the canonical mappings (§E.3.6),
/// <c>timeOnTimeline</c> (§E.3.4), the normalisation procedures (§E.3.1) and
/// the two orders over the seven-property model.
/// </summary>
internal static class SevenPropertyModel
{
    private const int MaximumOffset = 14 * 60;

    // --- calendar arithmetic -----------------------------------------------

    /// <summary><c>daysInMonth</c> (§E.3.2); an absent year is a leap year, as §3.3.12 permits 29 February.</summary>
    internal static int DaysInMonth(int? year, int month) => month switch
    {
        2 => year is null || IsLeap(year.Value) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static bool IsLeap(int year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

    /// <summary>The number of days from the fixed origin to the start of a proleptic Gregorian year (astronomical numbering).</summary>
    private static long DaysBeforeYear(long year)
    {
        // Days in complete years before `year`, counting year 0 as a leap year,
        // as §E.3.4 does with its 400/100/4 terms.
        long y = year;
        return (365 * y) + Floor(y, 4) - Floor(y, 100) + Floor(y, 400);
    }

    private static long Floor(long value, long divisor)
    {
        long quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }

    private static int DaysBeforeMonth(int year, int month)
    {
        int days = 0;

        for (int m = 1; m < month; m++)
        {
            days += DaysInMonth(year, m);
        }

        return days;
    }

    /// <summary>
    /// <c>timeOnTimeline</c> (§E.3.4): seconds from the origin, with the
    /// absent properties filled as the specification fills them and the
    /// timezone offset taken from <paramref name="timezoneOffset"/>, which
    /// the caller has chosen (the value's own, an implicit one, or an imputed
    /// extreme).
    /// </summary>
    internal static XsdDecimal TimeOnTimeline(in SevenProperties value, int timezoneOffset)
    {
        // §E.3.4 works from year − 1, filling an absent year with 1971 so that
        // yr + 1 = 1972, a leap year; the day is filled with the month's last.
        int year = value.Has(DateTimeFields.Year) ? value.Year : 1972;
        int month = value.Has(DateTimeFields.Month) ? value.Month : 12;
        int day = value.Has(DateTimeFields.Day) ? value.Day : DaysInMonth(year, month);
        long minutes = value.Has(DateTimeFields.Time) ? value.Minute : 0;
        long hours = value.Has(DateTimeFields.Time) ? value.Hour : 0;

        long days = DaysBeforeYear(year) + DaysBeforeMonth(year, month) + day - 1;
        long seconds = (days * 86400) + (hours * 3600) + ((minutes - timezoneOffset) * 60);

        XsdDecimal whole = XsdDecimal.FromInt64(seconds);
        return value.Has(DateTimeFields.Time) ? whole + value.Second : whole;
    }

    // --- orders --------------------------------------------------------------

    /// <summary>The implicit-timezone total order (XPath F&amp;O §10.4; ADR 0051).</summary>
    internal static int Compare(in SevenProperties left, in SevenProperties right, int implicitTimezoneOffset)
    {
        XsdDecimal a = TimeOnTimeline(in left, left.HasTimezone ? left.TimezoneOffset : implicitTimezoneOffset);
        XsdDecimal b = TimeOnTimeline(in right, right.HasTimezone ? right.TimezoneOffset : implicitTimezoneOffset);
        return a.CompareTo(b);
    }

    /// <summary>
    /// XML Schema's partial order (§D.2.1, §E.3.4): both timezoned or both
    /// not, by time line; otherwise the untimezoned value is imputed both
    /// <c>+14:00</c> and <c>-14:00</c>, and the pair is comparable only when
    /// both imputations give the same strict inequality.
    /// </summary>
    internal static PartialOrdering CompareXsd(in SevenProperties left, in SevenProperties right)
    {
        if (left.HasTimezone == right.HasTimezone)
        {
            int offset = 0;
            return Ordering(Compare(in left, in right, offset));
        }

        // Impute the extremes to whichever side lacks a timezone.
        int withMaximum = left.HasTimezone
            ? TimeOnTimeline(in left, left.TimezoneOffset).CompareTo(TimeOnTimeline(in right, MaximumOffset))
            : TimeOnTimeline(in left, MaximumOffset).CompareTo(TimeOnTimeline(in right, right.TimezoneOffset));
        int withMinimum = left.HasTimezone
            ? TimeOnTimeline(in left, left.TimezoneOffset).CompareTo(TimeOnTimeline(in right, -MaximumOffset))
            : TimeOnTimeline(in left, -MaximumOffset).CompareTo(TimeOnTimeline(in right, right.TimezoneOffset));

        if (withMaximum < 0 && withMinimum < 0)
        {
            return PartialOrdering.Less;
        }

        if (withMaximum > 0 && withMinimum > 0)
        {
            return PartialOrdering.Greater;
        }

        return PartialOrdering.Indeterminate;
    }

    private static PartialOrdering Ordering(int comparison) =>
        comparison < 0 ? PartialOrdering.Less : comparison > 0 ? PartialOrdering.Greater : PartialOrdering.Equal;

    // --- lexical mapping ----------------------------------------------------

    /// <summary>
    /// Parses the lexical representation of a datatype with the given
    /// fields, per §D.2.2's fragments: <c>yearFrag</c>, <c>-monthFrag</c>,
    /// <c>-dayFrag</c>, <c>T</c> and the time fragments, and an optional
    /// <c>timezoneFrag</c>, with the leading <c>--</c> and <c>---</c> of the
    /// month and day types.
    /// </summary>
    internal static bool TryParse(ReadOnlySpan<byte> utf8, DateTimeFields fields, out SevenProperties value)
    {
        value = default;
        int i = 0;
        int year = 0;
        int month = 0;
        int day = 0;
        int hour = 0;
        int minute = 0;
        XsdDecimal second = default;

        if ((fields & DateTimeFields.Year) != 0)
        {
            if (!ParseYear(utf8, ref i, out year))
            {
                return false;
            }
        }

        if ((fields & DateTimeFields.Month) != 0)
        {
            // gMonth and gMonthDay open with "--"; a month after a year is "-MM".
            if ((fields & DateTimeFields.Year) != 0)
            {
                if (!Expect(utf8, ref i, (byte)'-'))
                {
                    return false;
                }
            }
            else if (!Expect(utf8, ref i, (byte)'-') || !Expect(utf8, ref i, (byte)'-'))
            {
                return false;
            }

            if (!ParseTwoDigits(utf8, ref i, 1, 12, out month))
            {
                return false;
            }
        }

        if ((fields & DateTimeFields.Day) != 0)
        {
            // gDay opens with "---"; a day after a month is "-DD".
            if ((fields & DateTimeFields.Month) != 0)
            {
                if (!Expect(utf8, ref i, (byte)'-'))
                {
                    return false;
                }
            }
            else if (!Expect(utf8, ref i, (byte)'-') || !Expect(utf8, ref i, (byte)'-') || !Expect(utf8, ref i, (byte)'-'))
            {
                return false;
            }

            int limit = (fields & DateTimeFields.Month) != 0
                ? DaysInMonth((fields & DateTimeFields.Year) != 0 ? year : null, month)
                : 31;

            if (!ParseTwoDigits(utf8, ref i, 1, limit, out day))
            {
                return false;
            }
        }

        bool endOfDay = false;

        if ((fields & DateTimeFields.Time) != 0)
        {
            if ((fields & DateTimeFields.Date) != 0 && !Expect(utf8, ref i, (byte)'T'))
            {
                return false;
            }

            if (!ParseTime(utf8, ref i, out hour, out minute, out second, out endOfDay))
            {
                return false;
            }
        }

        short timezone = SevenProperties.NoTimezone;

        if (i < utf8.Length && !ParseTimezone(utf8, ref i, out timezone))
        {
            return false;
        }

        if (i != utf8.Length)
        {
            return false;
        }

        if (endOfDay)
        {
            // §3.3.7.2: 24:00:00 is the first instant of the following day.
            hour = 0;

            if ((fields & DateTimeFields.Date) != 0)
            {
                day++;

                if (day > DaysInMonth(year, month))
                {
                    day = 1;
                    month++;

                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                }
            }
        }

        value = new SevenProperties(fields, year, month, day, hour, minute, second, timezone);
        return true;
    }

    private static bool Expect(ReadOnlySpan<byte> utf8, ref int i, byte expected)
    {
        if (i < utf8.Length && utf8[i] == expected)
        {
            i++;
            return true;
        }

        return false;
    }

    /// <summary><c>yearFrag</c>: an optional minus, four digits or more with no leading zero beyond four.</summary>
    private static bool ParseYear(ReadOnlySpan<byte> utf8, ref int i, out int year)
    {
        year = 0;
        bool negative = false;

        if (i < utf8.Length && utf8[i] == (byte)'-')
        {
            negative = true;
            i++;
        }

        int start = i;
        long value = 0;

        while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
        {
            value = (value * 10) + Lexical.Digit(utf8[i]);

            if (value > int.MaxValue)
            {
                return false;
            }

            i++;
        }

        int digits = i - start;

        if (digits < 4 || (digits > 4 && utf8[start] == (byte)'0'))
        {
            return false;
        }

        year = negative ? -(int)value : (int)value;
        return true;
    }

    private static bool ParseTwoDigits(ReadOnlySpan<byte> utf8, ref int i, int minimum, int maximum, out int value)
    {
        value = 0;

        if (i + 2 > utf8.Length || !Lexical.IsDigit(utf8[i]) || !Lexical.IsDigit(utf8[i + 1]))
        {
            return false;
        }

        value = (Lexical.Digit(utf8[i]) * 10) + Lexical.Digit(utf8[i + 1]);
        i += 2;
        return value >= minimum && value <= maximum;
    }

    /// <summary><c>hourFrag ':' minuteFrag ':' secondFrag</c>, or <c>endOfDayFrag</c>.</summary>
    private static bool ParseTime(
        ReadOnlySpan<byte> utf8, ref int i, out int hour, out int minute, out XsdDecimal second, out bool endOfDay)
    {
        minute = 0;
        second = default;
        endOfDay = false;

        if (!ParseTwoDigits(utf8, ref i, 0, 24, out hour) || !Expect(utf8, ref i, (byte)':')
            || !ParseTwoDigits(utf8, ref i, 0, 59, out minute) || !Expect(utf8, ref i, (byte)':'))
        {
            return false;
        }

        int secondStart = i;

        if (i + 2 > utf8.Length || !Lexical.IsDigit(utf8[i]) || !Lexical.IsDigit(utf8[i + 1]))
        {
            return false;
        }

        i += 2;

        if (i < utf8.Length && utf8[i] == (byte)'.')
        {
            i++;
            int fractionStart = i;

            while (i < utf8.Length && Lexical.IsDigit(utf8[i]))
            {
                i++;
            }

            if (i == fractionStart)
            {
                return false;
            }
        }

        if (!XsdDecimal.TryParse(utf8[secondStart..i], out second) || second >= XsdDecimal.FromInt64(60))
        {
            return false;
        }

        if (hour == 24)
        {
            // Only 24:00:00 with a zero fraction is a lexical form.
            if (minute != 0 || !second.IsZero)
            {
                return false;
            }

            endOfDay = true;
        }

        return true;
    }

    /// <summary><c>timezoneFrag</c>: <c>Z</c>, or a signed <c>hh:mm</c> up to <c>14:00</c>.</summary>
    private static bool ParseTimezone(ReadOnlySpan<byte> utf8, ref int i, out short offset)
    {
        offset = SevenProperties.NoTimezone;

        if (utf8[i] == (byte)'Z')
        {
            i++;
            offset = 0;
            return true;
        }

        if (utf8[i] != (byte)'+' && utf8[i] != (byte)'-')
        {
            return false;
        }

        bool negative = utf8[i] == (byte)'-';
        i++;

        if (!ParseTwoDigits(utf8, ref i, 0, 14, out int hours) || !Expect(utf8, ref i, (byte)':')
            || !ParseTwoDigits(utf8, ref i, 0, 59, out int minutes))
        {
            return false;
        }

        int total = (hours * 60) + minutes;

        if (total > MaximumOffset)
        {
            return false;
        }

        offset = (short)(negative ? -total : total);
        return true;
    }

    // --- canonical mapping ----------------------------------------------------

    /// <summary>Writes the canonical form (§E.3.6) of a value with the given fields.</summary>
    internal static bool TryFormat(in SevenProperties value, DateTimeFields fields, Span<byte> destination, out int written)
    {
        written = 0;
        int i = 0;

        if ((fields & DateTimeFields.Year) != 0)
        {
            if (!WriteYear(value.Year, destination, ref i))
            {
                return false;
            }
        }

        if ((fields & DateTimeFields.Month) != 0)
        {
            if (!Write((fields & DateTimeFields.Year) != 0 ? "-"u8 : "--"u8, destination, ref i)
                || !WriteTwo(value.Month, destination, ref i))
            {
                return false;
            }
        }

        if ((fields & DateTimeFields.Day) != 0)
        {
            if (!Write((fields & DateTimeFields.Month) != 0 ? "-"u8 : "---"u8, destination, ref i)
                || !WriteTwo(value.Day, destination, ref i))
            {
                return false;
            }
        }

        if ((fields & DateTimeFields.Time) != 0)
        {
            if ((fields & DateTimeFields.Date) != 0 && !Write("T"u8, destination, ref i))
            {
                return false;
            }

            if (!WriteTwo(value.Hour, destination, ref i) || !Write(":"u8, destination, ref i)
                || !WriteTwo(value.Minute, destination, ref i) || !Write(":"u8, destination, ref i)
                || !WriteSecond(value.Second, destination, ref i))
            {
                return false;
            }
        }

        if (value.HasTimezone && !WriteTimezone(value.TimezoneOffset, destination, ref i))
        {
            return false;
        }

        written = i;
        return true;
    }

    /// <summary>A lexical form is canonical when it parses and formats back to itself.</summary>
    internal static bool IsCanonical(ReadOnlySpan<byte> lexical, DateTimeFields fields)
    {
        if (!TryParse(lexical, fields, out SevenProperties value))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Lexical.StackLimit];
        return TryFormat(in value, fields, buffer, out int written) && buffer[..written].SequenceEqual(lexical);
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

    /// <summary><c>yearCanonicalFragmentMap</c>: four digits, or as many as needed above 9999.</summary>
    private static bool WriteYear(int year, Span<byte> destination, ref int i)
    {
        if (year < 0)
        {
            if (!Write("-"u8, destination, ref i))
            {
                return false;
            }
        }

        ulong magnitude = year < 0 ? (ulong)(-(long)year) : (ulong)year;

        if (magnitude > 9999)
        {
            if (!Lexical.TryWriteUnsigned(magnitude, destination[i..], out int digits))
            {
                return false;
            }

            i += digits;
            return true;
        }

        if (!Lexical.TryWritePadded(magnitude, 4, destination[i..], out int padded))
        {
            return false;
        }

        i += padded;
        return true;
    }

    private static bool WriteTwo(int value, Span<byte> destination, ref int i)
    {
        if (!Lexical.TryWritePadded((ulong)value, 2, destination[i..], out int written))
        {
            return false;
        }

        i += written;
        return true;
    }

    /// <summary><c>secondCanonicalFragmentMap</c>: two digits, and a fraction only when non-zero.</summary>
    private static bool WriteSecond(XsdDecimal second, Span<byte> destination, ref int i)
    {
        if (second.IsInteger)
        {
            return WriteTwo((int)(second.Mantissa / 1_000_000_000_000_000_000), destination, ref i);
        }

        // The decimal's canonical form is d.ddd; the second fragment wants two
        // integral digits, so pad a single one.
        Span<byte> buffer = stackalloc byte[48];

        if (!second.TryFormat(buffer, out int length))
        {
            return false;
        }

        int point = buffer[..length].IndexOf((byte)'.');

        if (point == 1 && !Write("0"u8, destination, ref i))
        {
            return false;
        }

        return Write(buffer[..length], destination, ref i);
    }

    /// <summary><c>timezoneCanonicalFragmentMap</c>: <c>Z</c> for zero, else signed <c>hh:mm</c>.</summary>
    private static bool WriteTimezone(int offset, Span<byte> destination, ref int i)
    {
        if (offset == 0)
        {
            return Write("Z"u8, destination, ref i);
        }

        int magnitude = Math.Abs(offset);
        return Write(offset < 0 ? "-"u8 : "+"u8, destination, ref i)
            && WriteTwo(magnitude / 60, destination, ref i)
            && Write(":"u8, destination, ref i)
            && WriteTwo(magnitude % 60, destination, ref i);
    }

    // --- normalisation (§E.3.1) and duration arithmetic (§E.3.3) ------------

    /// <summary>
    /// <c>dateTimePlusDuration</c> (§E.3.3): the months are added first, the
    /// day pinned to the new month's length, then the seconds are added and
    /// the result normalised. The timezone offset is kept.
    /// </summary>
    internal static bool TryAdd(in SevenProperties value, long months, XsdDecimal seconds, out SevenProperties result)
    {
        result = default;

        // Months, with the day pinned (E.3.3 step 1; normalizeMonth).
        long totalMonths = (value.Has(DateTimeFields.Year) ? (long)value.Year * 12 : 0)
            + (value.Has(DateTimeFields.Month) ? value.Month - 1 : 0)
            + months;
        long year = Floor(totalMonths, 12);
        int month = (int)(totalMonths - (year * 12)) + 1;

        if (year < int.MinValue || year > int.MaxValue)
        {
            return false;
        }

        int day = value.Has(DateTimeFields.Day) ? Math.Min(value.Day, DaysInMonth((int)year, month)) : 1;

        // Seconds (E.3.3 step 2; normalizeSecond, normalizeMinute, normalizeDay).
        XsdDecimal second = (value.Has(DateTimeFields.Time) ? value.Second : XsdDecimal.Zero) + seconds;
        XsdDecimal sixty = XsdDecimal.FromInt64(60);
        XsdDecimal wholeMinutes = (second / sixty).Floor();

        if (!wholeMinutes.TryToInteger(out XsdInteger carryMinutes))
        {
            return false;
        }

        second -= wholeMinutes * sixty;
        long minute = (value.Has(DateTimeFields.Time) ? value.Minute : 0) + carryMinutes.Value;
        long hour = (value.Has(DateTimeFields.Time) ? value.Hour : 0) + Floor(minute, 60);
        minute -= Floor(minute, 60) * 60;
        long dayCarry = Floor(hour, 24);
        hour -= dayCarry * 24;

        // normalizeDay: walk the day carry month by month.
        long days = day + dayCarry;

        while (days > DaysInMonth((int)year, month))
        {
            days -= DaysInMonth((int)year, month);
            month++;

            if (month > 12)
            {
                month = 1;
                year++;
            }
        }

        while (days < 1)
        {
            month--;

            if (month < 1)
            {
                month = 12;
                year--;
            }

            days += DaysInMonth((int)year, month);
        }

        if (year < int.MinValue || year > int.MaxValue)
        {
            return false;
        }

        result = new SevenProperties(
            value.Present, (int)year, month, (int)days, (int)hour, (int)minute, second, value.TimezoneOffset);
        return true;
    }
}
