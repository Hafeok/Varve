// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Results;

/// <summary>
/// A growable byte buffer the readers reuse for names and raw fields. It only
/// grows; once it has reached the longest token of a document, reading
/// allocates nothing more.
/// </summary>
internal sealed class ByteBuilder
{
    private byte[] _bytes = new byte[64];

    internal int Length { get; private set; }

    internal ReadOnlySpan<byte> Span => _bytes.AsSpan(0, Length);

    internal ReadOnlySpan<byte> Slice(int start, int length) => _bytes.AsSpan(start, length);

    internal void Clear() => Length = 0;

    internal void Truncate(int length) => Length = length;

    internal void Append(byte value)
    {
        if (Length == _bytes.Length)
        {
            Array.Resize(ref _bytes, _bytes.Length * 2);
        }

        _bytes[Length++] = value;
    }

    internal void Append(ReadOnlySpan<byte> values)
    {
        if (Length + values.Length > _bytes.Length)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, Length + values.Length));
        }

        values.CopyTo(_bytes.AsSpan(Length));
        Length += values.Length;
    }

    /// <summary>Appends a code point as UTF-8. The caller has checked that it is a scalar value.</summary>
    internal void AppendCodePoint(int codePoint)
    {
        Span<byte> buffer = stackalloc byte[4];
        int written = new System.Text.Rune(codePoint).EncodeToUtf8(buffer);
        Append(buffer[..written]);
    }
}
