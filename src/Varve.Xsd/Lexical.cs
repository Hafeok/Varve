// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;

namespace Varve.Xsd;

/// <summary>
/// The small pieces every lexical mapping shares: ASCII digits, the bridge
/// from <c>char</c> input to the UTF-8 parsers, and the bridge back.
/// </summary>
/// <remarks>
/// Every lexical form in this package is ASCII, so a <c>char</c> span that
/// contains anything else is not a lexical form of any of these datatypes,
/// and narrowing it to bytes cannot lose a valid input. The UTF-8 parser is
/// the one implementation; the <c>char</c> overloads narrow and call it.
/// </remarks>
internal static class Lexical
{
    /// <summary>Inputs at or below this length are narrowed on the stack.</summary>
    internal const int StackLimit = 256;

    internal static bool IsDigit(byte b) => (uint)(b - '0') <= 9;

    internal static int Digit(byte b) => b - '0';

    /// <summary>
    /// Narrows <paramref name="text"/> to ASCII bytes. False when it holds a
    /// non-ASCII character, which no lexical form here may.
    /// </summary>
    internal static bool TryNarrow(ReadOnlySpan<char> text, Span<byte> destination, out int written)
    {
        OperationStatus status = Ascii.FromUtf16(text, destination, out written);
        return status == OperationStatus.Done;
    }

    /// <summary>Widens ASCII bytes into <paramref name="destination"/>.</summary>
    internal static bool TryWiden(ReadOnlySpan<byte> ascii, Span<char> destination, out int written)
    {
        OperationStatus status = Ascii.ToUtf16(ascii, destination, out written);
        return status == OperationStatus.Done;
    }

    /// <summary>
    /// Runs a UTF-8 parser over <c>char</c> input, narrowing into a stack
    /// buffer or a rented one.
    /// </summary>
    internal static bool ParseChars<TValue>(
        ReadOnlySpan<char> text, TryParseUtf8<TValue> parse, out TValue value)
    {
        byte[]? rented = null;
        Span<byte> buffer = text.Length <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(text.Length));

        try
        {
            if (!TryNarrow(text, buffer, out int narrowed))
            {
                value = default!;
                return false;
            }

            return parse(buffer[..narrowed], out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// The same, with a state argument so that a parser needing one takes it
    /// through a static lambda rather than a closure.
    /// </summary>
    internal static bool ParseChars<TState, TValue>(
        ReadOnlySpan<char> text, TState state, TryParseUtf8<TState, TValue> parse, out TValue value)
    {
        byte[]? rented = null;
        Span<byte> buffer = text.Length <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(text.Length));

        try
        {
            if (!TryNarrow(text, buffer, out int narrowed))
            {
                value = default!;
                return false;
            }

            return parse(buffer[..narrowed], state, out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Runs a UTF-8 formatter and widens its output into <c>char</c>s.
    /// </summary>
    internal static bool FormatChars(Span<char> destination, TryFormatUtf8 format, out int written)
    {
        Span<byte> buffer = stackalloc byte[StackLimit];

        if (!format(buffer, out int bytes) || bytes > destination.Length)
        {
            written = 0;
            return false;
        }

        return TryWiden(buffer[..bytes], destination, out written);
    }

    /// <summary>Formats through a stack buffer and allocates the one string.</summary>
    internal static string ToString(TryFormatUtf8 format)
    {
        Span<byte> buffer = stackalloc byte[StackLimit];

        return format(buffer, out int bytes)
            ? Encoding.ASCII.GetString(buffer[..bytes])
            : throw new InvalidOperationException("The canonical form did not fit the stack buffer.");
    }

    /// <summary>Writes an unsigned integer in decimal, no padding.</summary>
    internal static bool TryWriteUnsigned(ulong value, Span<byte> destination, out int written)
    {
        Span<byte> digits = stackalloc byte[20];
        int count = 0;

        do
        {
            digits[count++] = (byte)('0' + (int)(value % 10));
            value /= 10;
        }
        while (value != 0);

        if (count > destination.Length)
        {
            written = 0;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            destination[i] = digits[count - 1 - i];
        }

        written = count;
        return true;
    }

    /// <summary>Writes an unsigned integer zero-padded to <paramref name="width"/> digits.</summary>
    internal static bool TryWritePadded(ulong value, int width, Span<byte> destination, out int written)
    {
        if (destination.Length < width)
        {
            written = 0;
            return false;
        }

        for (int i = width - 1; i >= 0; i--)
        {
            destination[i] = (byte)('0' + (int)(value % 10));
            value /= 10;
        }

        written = width;
        return value == 0;
    }
}

internal delegate bool TryParseUtf8<TValue>(ReadOnlySpan<byte> utf8, out TValue value);

internal delegate bool TryParseUtf8<TState, TValue>(ReadOnlySpan<byte> utf8, TState state, out TValue value);

internal delegate bool TryFormatUtf8(Span<byte> destination, out int written);
