// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Json.Canonical;

/// <summary>
/// RFC 8785, the JSON Canonicalization Scheme, as three functions over UTF-8:
/// how a string is escaped (§3.2.2.2), how a number is written (§3.2.2.3) and
/// how an object's members are ordered (§3.2.3). What each writes for an input
/// never changes (ADR 0147, <c>CanonicalBytesReadForever</c>).
/// </summary>
[Contract(typeof(VarveJsonCanonical.CanonicalJsonAtLayerZero), Role = "the one RFC 8785 implementation")]
public static class CanonicalJson
{
    /// <summary>The longest text <see cref="FormatNumber"/> writes, in bytes.</summary>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public const int MaxNumberLength = 32;

    /// <summary>
    /// Writes <paramref name="utf8"/> as a JSON string: quoted, with only
    /// <c>"</c>, <c>\</c> and the control characters escaped, the latter with
    /// the short forms where one exists and <c>\u00xx</c> in lower-case hex
    /// otherwise (§3.2.2.2). Every other byte is written as it is.
    /// </summary>
    public static void WriteString(IBufferWriter<byte> output, ReadOnlySpan<byte> utf8)
    {
        ArgumentNullException.ThrowIfNull(output);
        Put(output, (byte)'"');

        foreach (byte b in utf8)
        {
            switch (b)
            {
                case (byte)'"':
                    Put(output, "\\\""u8);
                    break;
                case (byte)'\\':
                    Put(output, "\\\\"u8);
                    break;
                case (byte)'\b':
                    Put(output, "\\b"u8);
                    break;
                case (byte)'\t':
                    Put(output, "\\t"u8);
                    break;
                case (byte)'\n':
                    Put(output, "\\n"u8);
                    break;
                case (byte)'\f':
                    Put(output, "\\f"u8);
                    break;
                case (byte)'\r':
                    Put(output, "\\r"u8);
                    break;
                default:
                    if (b < 0x20)
                    {
                        Span<byte> escape = output.GetSpan(6);
                        "\\u00"u8.CopyTo(escape);
                        escape[4] = Hex(b >> 4);
                        escape[5] = Hex(b & 0xF);
                        output.Advance(6);
                    }
                    else
                    {
                        Put(output, b);
                    }

                    break;
            }
        }

        Put(output, (byte)'"');
    }

    /// <summary>Writes <paramref name="value"/> as a JSON number in ECMAScript form (§3.2.2.3).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite, which JSON cannot hold.</exception>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public static void WriteNumber(IBufferWriter<byte> output, double value)
    {
        ArgumentNullException.ThrowIfNull(output);
        Span<byte> buffer = output.GetSpan(MaxNumberLength);
        output.Advance(FormatNumber(value, buffer));
    }

    /// <summary>
    /// Formats a double as ECMAScript's <c>Number::toString(10)</c> does, which
    /// is RFC 8785's number form: the shortest digits that round-trip, plain
    /// notation for 1e-7 ≤ |x| &lt; 1e21 and exponent notation outside it, and
    /// <c>-0</c> as <c>0</c>. Answers the number of bytes written.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    public static int FormatNumber(double value, Span<byte> destination)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "JSON has no NaN or infinity (RFC 8785 §3.2.2.3).");
        }

        if (value == 0)
        {
            // -0 is "0".
            destination[0] = (byte)'0';
            return 1;
        }

        int at = 0;

        if (value < 0)
        {
            destination[at++] = (byte)'-';
            value = -value;
        }

        // The shortest round-trip digits, from the runtime's "R" formatting,
        // which is either plain (123.456) or d.dddE+xx; the digits and the
        // exponent are pulled apart and re-laid per ECMAScript's rules.
        Span<char> text = stackalloc char[40];
        value.TryFormat(text, out int written, "R", CultureInfo.InvariantCulture);
        ReadOnlySpan<char> r = text[..written];

        Span<char> digits = stackalloc char[32];
        int digitCount = 0;
        int pointPosition = -1;
        int explicitExponent = 0;

        for (int i = 0; i < r.Length; i++)
        {
            char c = r[i];

            if (c == '.')
            {
                pointPosition = digitCount;
            }
            else if (c == 'E' || c == 'e')
            {
                explicitExponent = int.Parse(r[(i + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
            else
            {
                digits[digitCount++] = c;
            }
        }

        if (pointPosition < 0)
        {
            pointPosition = digitCount;
        }

        // Strip leading zeros (0.001 → digits 0001, point at 1) and trailing zeros.
        int lead = 0;

        while (lead < digitCount - 1 && digits[lead] == '0')
        {
            lead++;
        }

        if (lead > 0)
        {
            digits[lead..digitCount].CopyTo(digits);
            digitCount -= lead;
            pointPosition -= lead;
        }

        while (digitCount > 1 && digits[digitCount - 1] == '0')
        {
            digitCount--;
        }

        // n: the position of the decimal point relative to the digits, per
        // ECMAScript: value = 0.d1d2…dk × 10^n.
        int n = pointPosition + explicitExponent;
        int k = digitCount;

        if (k <= n && n <= 21)
        {
            for (int d = 0; d < k; d++)
            {
                destination[at++] = (byte)digits[d];
            }

            for (int z = 0; z < n - k; z++)
            {
                destination[at++] = (byte)'0';
            }

            return at;
        }

        if (0 < n && n <= 21)
        {
            for (int d = 0; d < n; d++)
            {
                destination[at++] = (byte)digits[d];
            }

            destination[at++] = (byte)'.';

            for (int d = n; d < k; d++)
            {
                destination[at++] = (byte)digits[d];
            }

            return at;
        }

        if (-6 < n && n <= 0)
        {
            destination[at++] = (byte)'0';
            destination[at++] = (byte)'.';

            for (int z = 0; z < -n; z++)
            {
                destination[at++] = (byte)'0';
            }

            for (int d = 0; d < k; d++)
            {
                destination[at++] = (byte)digits[d];
            }

            return at;
        }

        // Exponent form: d[.ddd]e±x with x = n - 1.
        destination[at++] = (byte)digits[0];

        if (k > 1)
        {
            destination[at++] = (byte)'.';

            for (int d = 1; d < k; d++)
            {
                destination[at++] = (byte)digits[d];
            }
        }

        destination[at++] = (byte)'e';
        int e = n - 1;
        destination[at++] = (byte)(e < 0 ? '-' : '+');
        e = Math.Abs(e);
        e.TryFormat(destination[at..], out int eWritten, default, CultureInfo.InvariantCulture);
        return at + eWritten;
    }

    /// <summary>
    /// Compares two member names, UTF-8 encoded, by their UTF-16 code units, as
    /// RFC 8785 §3.2.3 orders an object's members: negative when
    /// <paramref name="left"/> sorts first, zero when they are equal.
    /// </summary>
    [DesignDecision(typeof(VarveJsonCanonical.JsonValuesAreBclPrimitives), Scope = ExceptionScope.Boundary)]
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public static int CompareNames(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        int i = 0, j = 0;

        while (i < left.Length && j < right.Length)
        {
            int a = NextUnit(left, ref i, out int aSecond);
            int b = NextUnit(right, ref j, out int bSecond);

            if (a != b)
            {
                return a - b;
            }

            if (aSecond != bSecond)
            {
                return aSecond - bSecond;
            }
        }

        return (left.Length - i) - (right.Length - j);
    }

    /// <summary>The next UTF-16 code unit of a UTF-8 string, with the low surrogate of a supplementary character as the second.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static int NextUnit(ReadOnlySpan<byte> text, ref int at, out int second)
    {
        second = -1;
        byte b = text[at];

        if (b < 0x80)
        {
            at++;
            return b;
        }

        if ((b & 0xE0) == 0xC0 && at + 1 < text.Length)
        {
            int cp = ((b & 0x1F) << 6) | (text[at + 1] & 0x3F);
            at += 2;
            return cp;
        }

        if ((b & 0xF0) == 0xE0 && at + 2 < text.Length)
        {
            int cp = ((b & 0x0F) << 12) | ((text[at + 1] & 0x3F) << 6) | (text[at + 2] & 0x3F);
            at += 3;
            return cp;
        }

        if ((b & 0xF8) == 0xF0 && at + 3 < text.Length)
        {
            int cp = ((b & 0x07) << 18) | ((text[at + 1] & 0x3F) << 12) | ((text[at + 2] & 0x3F) << 6) | (text[at + 3] & 0x3F);
            at += 4;
            cp -= 0x10000;
            second = 0xDC00 | (cp & 0x3FF);
            return 0xD800 | (cp >> 10);
        }

        at++;
        return b;
    }

    private static byte Hex(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static void Put(IBufferWriter<byte> output, byte b)
    {
        output.GetSpan(1)[0] = b;
        output.Advance(1);
    }

    private static void Put(IBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }
}
