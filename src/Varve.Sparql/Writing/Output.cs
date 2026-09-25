// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace Varve.Sparql.Writing;

/// <summary>UTF-8 out to a buffer writer, with the few conveniences the serialiser needs.</summary>
internal ref struct Output
{
    private readonly IBufferWriter<byte> _writer;
    private Span<byte> _buffer;
    private int _used;

    internal Output(IBufferWriter<byte> writer)
    {
        _writer = writer;
        _buffer = default;
        _used = 0;
    }

    internal void Write(byte b)
    {
        if (_used == _buffer.Length)
        {
            Flush(1);
        }

        _buffer[_used++] = b;
    }

    internal void Write(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (_used == _buffer.Length)
            {
                Flush(bytes.Length);
            }

            int take = Math.Min(bytes.Length, _buffer.Length - _used);
            bytes[..take].CopyTo(_buffer[_used..]);
            _used += take;
            bytes = bytes[take..];
        }
    }

    internal void Write(string text)
    {
        int needed = Encoding.UTF8.GetMaxByteCount(text.Length);

        if (_buffer.Length - _used < needed)
        {
            Flush(needed);
        }

        _used += Encoding.UTF8.GetBytes(text, _buffer[_used..]);
    }

    internal void Write(long value)
    {
        if (_buffer.Length - _used < 24)
        {
            Flush(24);
        }

        value.TryFormat(_buffer[_used..], out int written, default, CultureInfo.InvariantCulture);
        _used += written;
    }

    internal void Flush(int sizeHint)
    {
        if (_used > 0)
        {
            _writer.Advance(_used);
        }

        _buffer = _writer.GetSpan(Math.Max(sizeHint, 256));
        _used = 0;
    }

    internal void Complete()
    {
        if (_used > 0)
        {
            _writer.Advance(_used);
            _used = 0;
        }

        _buffer = default;
    }
}
