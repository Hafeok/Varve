// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using CsCheck;
using Xunit;

namespace Varve.Json.Canonical.Tests;

public sealed class CanonicalJsonTests
{
    // RFC 8785, Appendix B: IEEE 754 bits and the canonical number text.
    [Theory]
    [InlineData("0000000000000000", "0")]
    [InlineData("8000000000000000", "0")]
    [InlineData("0000000000000001", "5e-324")]
    [InlineData("8000000000000001", "-5e-324")]
    [InlineData("7fefffffffffffff", "1.7976931348623157e+308")]
    [InlineData("ffefffffffffffff", "-1.7976931348623157e+308")]
    [InlineData("4340000000000000", "9007199254740992")]
    [InlineData("c340000000000000", "-9007199254740992")]
    [InlineData("4430000000000000", "295147905179352830000")]
    [InlineData("44b52d02c7e14af5", "9.999999999999997e+22")]
    [InlineData("44b52d02c7e14af6", "1e+23")]
    [InlineData("44b52d02c7e14af7", "1.0000000000000001e+23")]
    [InlineData("444b1ae4d6e2ef4e", "999999999999999700000")]
    [InlineData("444b1ae4d6e2ef4f", "999999999999999900000")]
    [InlineData("444b1ae4d6e2ef50", "1e+21")]
    [InlineData("3eb0c6f7a0b5ed8c", "9.999999999999997e-7")]
    [InlineData("3eb0c6f7a0b5ed8d", "0.000001")]
    [InlineData("41b3de4355555553", "333333333.3333332")]
    [InlineData("41b3de4355555554", "333333333.33333325")]
    [InlineData("41b3de4355555555", "333333333.3333333")]
    [InlineData("41b3de4355555556", "333333333.3333334")]
    [InlineData("41b3de4355555557", "333333333.33333343")]
    [InlineData("becbf647612f3696", "-0.0000033333333333333333")]
    [InlineData("43143ff3c1cb0959", "1424953923781206.2")]
    public void rfc_8785_appendix_b_numbers(string bits, string expected) =>
        Assert.Equal(expected, Format(FromBits(bits)));

    [Fact]
    public void every_number_matches_ecmascript_as_node_wrote_it()
    {
        int count = 0;

        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "numbers.txt")))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ');
            Assert.Equal(parts[1], Format(FromBits(parts[0])));
            count++;
        }

        Assert.Equal(3015, count);
    }

    [Fact]
    public void every_number_written_parses_back_to_the_same_double() =>
        Gen.Double.Where(double.IsFinite).Sample(value =>
        {
            double back = double.Parse(Format(value), NumberStyles.Float, CultureInfo.InvariantCulture);
            return back.Equals(value) || (value == 0 && back == 0);
        }, iter: 20_000);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void a_non_finite_number_has_no_form(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Format(value));

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("q\"b\\", "\"q\\\"b\\\\\"")]
    [InlineData("\b\t\n\f\r", "\"\\b\\t\\n\\f\\r\"")]
    [InlineData("\u0001\u001f", "\"\\u0001\\u001f\"")]
    [InlineData("</script>€😀\u007f", "\"</script>€😀\u007f\"")]
    public void strings_escape_only_what_json_requires(string input, string expected)
    {
        ArrayBufferWriter<byte> output = new();
        CanonicalJson.WriteString(output, Encoding.UTF8.GetBytes(input));
        Assert.Equal(expected, Encoding.UTF8.GetString(output.WrittenSpan));
    }

    // RFC 8785 §3.2.3's example: names sorted by UTF-16 code units, so a
    // supplementary character's high surrogate sorts before U+E000..U+FFFF.
    [Fact]
    public void names_sort_by_utf16_code_units()
    {
        string[] names = ["€", "\r", "דּ", "1", "\U0001F600", "\u0080", "ö"];
        string[] expected = ["\r", "1", "\u0080", "ö", "€", "\U0001F600", "דּ"];
        Array.Sort(names, (a, b) => CanonicalJson.CompareNames(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b)));
        Assert.Equal(expected, names);
    }

    [Fact]
    public void the_comparison_agrees_with_ordinal_utf16_order() =>
        Gen.String.Select(Gen.String).Sample((a, b) =>
            Math.Sign(CanonicalJson.CompareNames(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b)))
            == Math.Sign(string.CompareOrdinal(Normalise(a), Normalise(b))), iter: 5_000);

    // A lone surrogate does not survive UTF-8, so the comparison is of what
    // UTF-8 can carry.
    private static string Normalise(string s) => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(s));

    internal static string Format(double value)
    {
        Span<byte> buffer = stackalloc byte[CanonicalJson.MaxNumberLength];
        int written = CanonicalJson.FormatNumber(value, buffer);
        return Encoding.ASCII.GetString(buffer[..written]);
    }

    private static double FromBits(string hex) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(Convert.FromHexString(hex)));
}
