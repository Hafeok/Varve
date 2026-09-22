// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Turtle;

/// <summary>
/// Writes into a span, counting past the end rather than throwing.
/// </summary>
/// <remarks>
/// Overflow is a question the caller asks — "did it fit?" — and not an
/// exceptional condition, so the writer keeps counting and the caller enlarges
/// the buffer and asks again. The alternative, sizing every write beforehand,
/// means walking every term twice.
/// </remarks>
internal ref struct SpanWriter
{
    private readonly Span<byte> _destination;

    internal SpanWriter(Span<byte> destination)
    {
        _destination = destination;
        Written = 0;
        Overflowed = false;
    }

    /// <summary>How many bytes the content needs, whether or not it fitted.</summary>
    internal int Written { get; private set; }

    /// <summary>Whether the destination was too small.</summary>
    internal bool Overflowed { get; private set; }

    internal void Byte(byte value)
    {
        if (Written >= _destination.Length)
        {
            Overflowed = true;
            Written++;
            return;
        }

        _destination[Written++] = value;
    }

    internal void Bytes(ReadOnlySpan<byte> value)
    {
        if (Written + value.Length > _destination.Length)
        {
            Overflowed = true;
            Written += value.Length;
            return;
        }

        value.CopyTo(_destination[Written..]);
        Written += value.Length;
    }
}
