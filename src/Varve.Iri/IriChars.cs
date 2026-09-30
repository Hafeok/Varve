// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Iri;

/// <summary>
/// The RFC 3987 §2.2 character classes, over UTF-8.
/// </summary>
/// <remarks>
/// ASCII is a lookup table. Everything above it is decoded to a scalar and
/// range-checked, because <c>ucschar</c> and <c>iprivate</c> are defined over
/// code points and not over bytes.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
internal static class IriChars
{
    private const byte Unreserved = 1 << 0;   // ALPHA / DIGIT / "-" / "." / "_" / "~"
    private const byte SubDelims = 1 << 1;    // "!$&'()*+,;="
    private const byte SchemeTail = 1 << 2;   // ALPHA / DIGIT / "+" / "-" / "."
    private const byte Alpha = 1 << 3;
    private const byte Digit = 1 << 4;
    private const byte Hex = 1 << 5;

    /// <summary>
    /// One byte of class bits per ASCII code point. A constant table: the
    /// compiler keeps it in the assembly's data and a read of it allocates
    /// nothing, where a <c>static readonly byte[]</c> was a heap array anybody
    /// holding the reference could write. Each entry is the OR of the class
    /// bits above for that code point, per RFC 3987 §2.2 and RFC 3986 §3.1.
    /// </summary>
    internal static System.ReadOnlySpan<byte> Ascii =>
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x00-0x0F
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 0x10-0x1F
        0x00, 0x02, 0x00, 0x00, 0x02, 0x00, 0x02, 0x02, 0x02, 0x02, 0x02, 0x06, 0x02, 0x05, 0x05, 0x00, // 0x20-0x2F
        0x35, 0x35, 0x35, 0x35, 0x35, 0x35, 0x35, 0x35, 0x35, 0x35, 0x00, 0x02, 0x00, 0x02, 0x00, 0x00, // 0x30-0x3F
        0x00, 0x2D, 0x2D, 0x2D, 0x2D, 0x2D, 0x2D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, // 0x40-0x4F
        0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x01, // 0x50-0x5F
        0x00, 0x2D, 0x2D, 0x2D, 0x2D, 0x2D, 0x2D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, // 0x60-0x6F
        0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x0D, 0x00, 0x00, 0x00, 0x01, 0x00, // 0x70-0x7F
    ];

    internal static bool IsAlpha(byte b) => b < 128 && (Ascii[b] & Alpha) != 0;

    internal static bool IsDigit(byte b) => b < 128 && (Ascii[b] & Digit) != 0;

    internal static bool IsHex(byte b) => b < 128 && (Ascii[b] & Hex) != 0;

    internal static bool IsSchemeTail(byte b) => b < 128 && (Ascii[b] & SchemeTail) != 0;

    internal static bool IsUnreservedAscii(byte b) => b < 128 && (Ascii[b] & Unreserved) != 0;

    internal static bool IsSubDelim(byte b) => b < 128 && (Ascii[b] & SubDelims) != 0;

    /// <summary>
    /// RFC 3987 <c>ucschar</c>. Note that it excludes U+E000–U+F8FF, which are
    /// <c>iprivate</c> and permitted in a query only.
    /// </summary>
    internal static bool IsUcsChar(int scalar) =>
        (scalar >= 0x00A0 && scalar <= 0xD7FF)
        || (scalar >= 0xF900 && scalar <= 0xFDCF)
        || (scalar >= 0xFDF0 && scalar <= 0xFFEF)
        || (scalar >= 0x10000 && scalar <= 0xDFFFD && (scalar & 0xFFFF) <= 0xFFFD)
        || (scalar >= 0xE1000 && scalar <= 0xEFFFD);

    /// <summary>RFC 3987 <c>iprivate</c>. Permitted in a query and nowhere else.</summary>
    internal static bool IsPrivate(int scalar) =>
        (scalar >= 0xE000 && scalar <= 0xF8FF)
        || (scalar >= 0xF0000 && scalar <= 0xFFFFD)
        || (scalar >= 0x100000 && scalar <= 0x10FFFD);

    /// <summary>
    /// Decodes one scalar. Returns false for ill-formed UTF-8, which is a
    /// distinct failure from an invalid character: the causes and the fixes
    /// differ.
    /// </summary>
    internal static bool TryDecode(System.ReadOnlySpan<byte> utf8, out int scalar, out int length)
    {
        OperationStatus status = Rune.DecodeFromUtf8(utf8, out Rune rune, out length);
        scalar = rune.Value;
        return status == OperationStatus.Done;
    }
}
