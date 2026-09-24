// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;

namespace Varve.Sparql.Parsing;

/// <summary>The character classes of SPARQL 1.2 §19.7, over UTF-8.</summary>
internal static class Chars
{
    internal static bool IsWhitespace(byte b) => b is 0x20 or 0x09 or 0x0D or 0x0A;

    internal static bool IsHex(byte b) =>
        b is >= (byte)'0' and <= (byte)'9'
            or >= (byte)'A' and <= (byte)'F'
            or >= (byte)'a' and <= (byte)'f';

    internal static int HexValue(byte b) =>
        b <= (byte)'9' ? b - '0' : ((b | 0x20) - 'a' + 10);

    internal static bool IsAsciiLetter(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    internal static bool IsAsciiDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    /// <summary>The characters <c>[159] IRIREF</c> excludes, backslash included: it may start only a <c>UCHAR</c>.</summary>
    internal static bool IsForbiddenInIri(byte b) =>
        b <= 0x20 || b is (byte)'<' or (byte)'>' or (byte)'"' or (byte)'{'
            or (byte)'}' or (byte)'|' or (byte)'^' or (byte)'`';

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

    internal static bool IsPnCharsU(int c) => c == '_' || IsPnCharsBase(c);

    internal static bool IsPnChars(int c) =>
        c is '-' or >= '0' and <= '9' or 0x00B7
            or >= 0x0300 and <= 0x036F
            or >= 0x203F and <= 0x2040
            || IsPnCharsU(c);

    /// <summary><c>[187] VARNAME</c>'s continuation characters.</summary>
    internal static bool IsVarNameChar(int c) =>
        IsPnCharsU(c) || c is >= '0' and <= '9' or 0x00B7 or >= 0x0300 and <= 0x036F or >= 0x203F and <= 0x2040;

    /// <summary><c>[194] PN_LOCAL_ESC</c>'s escapable characters.</summary>
    internal static bool IsLocalEscape(byte b) =>
        b is (byte)'_' or (byte)'~' or (byte)'.' or (byte)'-' or (byte)'!' or (byte)'$' or (byte)'&'
            or (byte)'\'' or (byte)'(' or (byte)')' or (byte)'*' or (byte)'+' or (byte)',' or (byte)';'
            or (byte)'=' or (byte)'/' or (byte)'?' or (byte)'#' or (byte)'@' or (byte)'%';

    /// <summary>Decodes one scalar value at an index; false on malformed UTF-8 or at the end.</summary>
    internal static bool TryRune(ReadOnlySpan<byte> text, int index, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;

        if (index >= text.Length)
        {
            return false;
        }

        byte b = text[index];

        if (b < 0x80)
        {
            codePoint = b;
            length = 1;
            return true;
        }

        if (Rune.DecodeFromUtf8(text[index..], out Rune rune, out int consumed) != OperationStatus.Done)
        {
            return false;
        }

        codePoint = rune.Value;
        length = consumed;
        return true;
    }
}
