// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.JsonLd.Json;

/// <summary>
/// RFC 8785, the JSON Canonicalization Scheme: the lexical form of an
/// <c>rdf:JSON</c> literal (JSON-LD 1.1 API §8.6, step 3; ADR 0112,
/// <c>JsonLiteralsAreCanonical</c>). Members sorted by UTF-16 code units, no
/// whitespace, strings escaped only where JSON requires it, numbers in
/// ECMAScript's <c>Number::toString</c> form.
/// </summary>
[DesignDecision(typeof(JsonLdOverUtf8Json.JsonLiteralsAreCanonical), Scope = ExceptionScope.HotPath)]
internal static class Jcs
{
    /// <summary>Writes the canonical form of <paramref name="node"/> into the tree's text; returns its range.</summary>
    internal static TextRange Canonicalise(JsonTree tree, int node)
    {
        // The canonical bytes go through a pooled buffer, because sorting an
        // object's members reorders nodes whose text would otherwise be
        // written where it is read from.
        ArrayBufferWriter<byte> buffer = new(256);
        Write(buffer, tree, node);
        return tree.AddText(buffer.WrittenSpan);
    }

    private static void Write(ArrayBufferWriter<byte> output, JsonTree tree, int node)
    {
        switch (tree.Kind(node))
        {
            case JsonKind.Object:
                tree.SortMembers(node);
                Put(output, (byte)'{');
                bool first = true;

                for (int member = tree.First(node); member >= 0; member = tree.Next(member))
                {
                    if (!first)
                    {
                        Put(output, (byte)',');
                    }

                    first = false;
                    WriteString(output, tree.NameBytes(member));
                    Put(output, (byte)':');
                    Write(output, tree, tree.ValueOf(member));
                }

                Put(output, (byte)'}');
                break;
            case JsonKind.Array:
                Put(output, (byte)'[');
                bool firstItem = true;

                for (int item = tree.First(node); item >= 0; item = tree.Next(item))
                {
                    if (!firstItem)
                    {
                        Put(output, (byte)',');
                    }

                    firstItem = false;
                    Write(output, tree, item);
                }

                Put(output, (byte)']');
                break;
            case JsonKind.String:
                WriteString(output, tree.Bytes(node));
                break;
            case JsonKind.Number:
                WriteNumber(output, tree.Bytes(node));
                break;
            case JsonKind.True:
                Put(output, "true"u8);
                break;
            case JsonKind.False:
                Put(output, "false"u8);
                break;
            default:
                Put(output, "null"u8);
                break;
        }
    }

    /// <summary>RFC 8785 §3.2.2.2: only <c>"</c>, <c>\</c> and the control characters are escaped, the latter with the short forms where one exists.</summary>
    private static void WriteString(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> text)
    {
        Put(output, (byte)'"');

        foreach (byte b in text)
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

    private static byte Hex(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    /// <summary>
    /// RFC 8785 §3.2.2.3: the number as ECMAScript's <c>Number::toString</c>
    /// renders the IEEE double it denotes — shortest round-trip digits,
    /// exponent form outside 1e-7 ≤ |x| &lt; 1e21, <c>-0</c> as <c>0</c>.
    /// </summary>
    internal static void WriteNumber(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> raw)
    {
        double value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        Span<byte> buffer = stackalloc byte[48];
        int length = FormatEcmaScript(value, buffer);
        Put(output, buffer[..length]);
    }

    /// <summary>Formats a double as ECMAScript's <c>Number::toString(10)</c> would.</summary>
    internal static int FormatEcmaScript(double value, Span<byte> destination)
    {
        if (value == 0 || double.IsNaN(value))
        {
            // -0 is "0"; NaN cannot occur in JSON.
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
        int exponent = 0;  // the decimal point sits after digitCount + exponent digits... computed below as n
        int pointPosition = -1;
        int explicitExponent = 0;
        int i = 0;

        for (; i < r.Length; i++)
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
        exponent = n;

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
        int e = exponent - 1;
        destination[at++] = (byte)(e < 0 ? '-' : '+');
        e = Math.Abs(e);
        e.TryFormat(destination[at..], out int eWritten, default, CultureInfo.InvariantCulture);
        return at + eWritten;
    }

    private static void Put(ArrayBufferWriter<byte> output, byte b)
    {
        output.GetSpan(1)[0] = b;
        output.Advance(1);
    }

    private static void Put(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }
}
