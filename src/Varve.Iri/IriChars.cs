using System.Buffers;
using System.Text;

namespace Varve.Iri;

/// <summary>
/// The RFC 3987 §2.2 character classes, over UTF-8.
/// </summary>
/// <remarks>
/// ASCII is a lookup table. Everything above it is decoded to a scalar and
/// range-checked, because <c>ucschar</c> and <c>iprivate</c> are defined over
/// code points and not over bytes.
/// </remarks>
internal static class IriChars
{
    private const byte Unreserved = 1 << 0;   // ALPHA / DIGIT / "-" / "." / "_" / "~"
    private const byte SubDelims = 1 << 1;    // "!$&'()*+,;="
    private const byte SchemeTail = 1 << 2;   // ALPHA / DIGIT / "+" / "-" / "."
    private const byte Alpha = 1 << 3;
    private const byte Digit = 1 << 4;
    private const byte Hex = 1 << 5;

    private static readonly byte[] Ascii = BuildAsciiTable();

    private static byte[] BuildAsciiTable()
    {
        byte[] table = new byte[128];

        for (char c = 'a'; c <= 'z'; c++)
        {
            table[c] |= Unreserved | SchemeTail | Alpha;
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            table[c] |= Unreserved | SchemeTail | Alpha;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            table[c] |= Unreserved | SchemeTail | Digit | Hex;
        }

        foreach (char c in "abcdefABCDEF")
        {
            table[c] |= Hex;
        }

        foreach (char c in "-._~")
        {
            table[c] |= Unreserved;
        }

        foreach (char c in "+-.")
        {
            table[c] |= SchemeTail;
        }

        foreach (char c in "!$&'()*+,;=")
        {
            table[c] |= SubDelims;
        }

        return table;
    }

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
