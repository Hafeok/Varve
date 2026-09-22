// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;

namespace Varve.Turtle;

/// <summary>
/// A growable landing place for a line that crosses a segment boundary.
/// </summary>
/// <remarks>
/// A plain array rather than a pooled one: it belongs to a parse, it is reused
/// for every split line in that parse, and after the longest line it never
/// grows again. Nothing to return, nothing to leak, and steady state allocates
/// nothing — which is the only property that matters here.
/// </remarks>
internal sealed class LineBuffer
{
    private byte[] _bytes = new byte[256];

    internal ReadOnlySpan<byte> Copy(ReadOnlySequence<byte> line)
    {
        int length = checked((int)line.Length);

        if (_bytes.Length < length)
        {
            _bytes = new byte[Math.Max(_bytes.Length * 2, length)];
        }

        line.CopyTo(_bytes);
        return _bytes.AsSpan(0, length);
    }
}
