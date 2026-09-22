// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;

namespace Varve.Conformance.Tests;

/// <summary>A reusable buffer writer, so serialising a quad does not allocate one.</summary>
internal sealed class ArrayBufferWriter : IBufferWriter<byte>
{
    private byte[] _bytes = new byte[256];
    private int _written;

    internal ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _written);

    internal void Reset() => _written = 0;

    public void Advance(int count) => _written += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _bytes.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _bytes.AsSpan(_written);
    }

    private void Ensure(int sizeHint)
    {
        int wanted = _written + Math.Max(sizeHint, 1);

        if (_bytes.Length < wanted)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, wanted));
        }
    }
}
