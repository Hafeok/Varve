namespace Varve.Turtle;

/// <summary>
/// The character classes of the N-Triples grammar (§2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>PN_CHARS_U excludes <c>':'</c> here</strong>, although RDF 1.1
/// N-Triples [158s] writes <c>PN_CHARS_BASE | '_' | ':'</c>. The suite settles
/// it against the published grammar: <c>nt-syntax-bad-bnode-01</c>
/// (<c>_::a</c>) and <c>nt-syntax-bad-bnode-02</c> (<c>_:abc:def</c>) are
/// negative tests, so a colon in a blank node label must be rejected. RDF 1.2
/// N-Triples has since dropped the colon from the production, which confirms
/// the 1.1 text was the error and not the tests.
/// </para>
/// </remarks>
internal static class NTriplesChars
{
    internal static bool IsWhitespace(byte b) => b is 0x20 or 0x09;

    internal static bool IsEol(byte b) => b is 0x0A or 0x0D;

    internal static bool IsHex(byte b) =>
        b is >= (byte)'0' and <= (byte)'9'
            or >= (byte)'A' and <= (byte)'F'
            or >= (byte)'a' and <= (byte)'f';

    internal static int HexValue(byte b) =>
        b <= (byte)'9' ? b - '0' : ((b | 0x20) - 'a' + 10);

    internal static bool IsAsciiLetter(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    internal static bool IsAsciiDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    /// <summary>
    /// Characters the IRIREF production [8] excludes from appearing raw.
    /// <c>&gt;</c> and <c>\</c> are excluded too, and are handled by the
    /// scanner before this is reached.
    /// </summary>
    internal static bool IsForbiddenInIri(byte b) =>
        b <= 0x20 || b is (byte)'<' or (byte)'>' or (byte)'"' or (byte)'{'
            or (byte)'}' or (byte)'|' or (byte)'^' or (byte)'`' or (byte)'\\';

    /// <summary>PN_CHARS_BASE [157s].</summary>
    internal static bool IsPnCharsBase(int c) =>
        c is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= 0x00C0 and <= 0x00D6
            or >= 0x00D8 and <= 0x00F6
            or >= 0x00F8 and <= 0x02FF
            or >= 0x0370 and <= 0x037D
            or >= 0x037F and <= 0x1FFF
            or >= 0x200C and <= 0x200D
            or >= 0x2070 and <= 0x218F
            or >= 0x2C00 and <= 0x2FEF
            or >= 0x3001 and <= 0xD7FF
            or >= 0xF900 and <= 0xFDCF
            or >= 0xFDF0 and <= 0xFFFD
            or >= 0x10000 and <= 0xEFFFF;

    /// <summary>PN_CHARS_U [158s], without the colon. See the type's remarks.</summary>
    internal static bool IsPnCharsU(int c) => c == '_' || IsPnCharsBase(c);

    /// <summary>PN_CHARS [160s].</summary>
    internal static bool IsPnChars(int c) =>
        c is '-' or >= '0' and <= '9' or 0x00B7
            or >= 0x0300 and <= 0x036F
            or >= 0x203F and <= 0x2040
            || IsPnCharsU(c);
}
