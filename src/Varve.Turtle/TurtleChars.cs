using System;
using System.Buffers;
using System.Text;

namespace Varve.Turtle;

/// <summary>The character classes Turtle adds to N-Triples'.</summary>
internal static class TurtleChars
{
    /// <summary>PN_CHARS_BASE [163s], over a decoded code point.</summary>
    internal static bool IsPnCharsBase(int c) => NTriplesChars.IsPnCharsBase(c);

    /// <summary>PN_CHARS_U [164s].</summary>
    internal static bool IsPnCharsU(int c) => NTriplesChars.IsPnCharsU(c);

    /// <summary>PN_CHARS [166s].</summary>
    internal static bool IsPnChars(int c) => NTriplesChars.IsPnChars(c);

    /// <summary>[167s] <c>PN_CHARS_BASE ((PN_CHARS | '.')* PN_CHARS)?</c>.</summary>
    internal static bool IsPrefixName(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty)
        {
            return false;
        }

        if (!TryRune(name, 0, out int first, out int length) || !IsPnCharsBase(first))
        {
            return false;
        }

        int last = first;
        int at = length;

        while (at < name.Length)
        {
            if (!TryRune(name, at, out int c, out int width))
            {
                return false;
            }

            if (c != '.' && !IsPnChars(c))
            {
                return false;
            }

            last = c;
            at += width;
        }

        // It may contain a dot and may not end with one.
        return last != '.';
    }

    internal static bool TryRune(ReadOnlySpan<byte> text, int index, out int codePoint, out int length)
    {
        codePoint = 0;
        length = 0;

        if (index >= text.Length)
        {
            return false;
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
