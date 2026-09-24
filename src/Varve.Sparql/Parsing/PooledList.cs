// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Parsing;

/// <summary>
/// A growable list over a rented array, so that accumulating a node's
/// children allocates only the exact array the node keeps.
/// </summary>
/// <remarks>
/// The parser's allocation claim is that it allocates the tree and nothing
/// else (<c>docs/spec/sparql-grammar.md</c> §8). A <c>List&lt;T&gt;</c> would
/// allocate its growth steps on top; this rents them from the shared pool and
/// hands back an array of exactly the final length.
/// </remarks>
internal ref struct PooledList<T>
{
    private T[]? _items;
    private int _count;

    public readonly int Count => Span.Length;

    public readonly ReadOnlySpan<T> Span => _items is null ? default : _items.AsSpan(0, _count);

    public readonly T this[int index] => Span[index];

    public void Add(T item)
    {
        if (_items is null)
        {
            _items = ArrayPool<T>.Shared.Rent(8);
        }
        else if (_count == _items.Length)
        {
            T[] grown = ArrayPool<T>.Shared.Rent(_items.Length * 2);
            _items.AsSpan(0, _count).CopyTo(grown);
            ArrayPool<T>.Shared.Return(_items, clearArray: true);
            _items = grown;
        }

        _items[_count++] = item;
    }

    public void Clear()
    {
        if (_items is not null)
        {
            Array.Clear(_items, 0, _count);
        }

        _count = 0;
    }

    /// <summary>The elements as an exact-length list, leaving this one empty.</summary>
    public AlgebraList<T> Drain()
    {
        if (_count == 0)
        {
            return default;
        }

        T[] exact = new T[_count];
        _items.AsSpan(0, _count).CopyTo(exact);
        Clear();
        return AlgebraList.Own(exact);
    }

    public void Dispose()
    {
        if (_items is not null)
        {
            ArrayPool<T>.Shared.Return(_items, clearArray: true);
            _items = null;
            _count = 0;
        }
    }
}

/// <summary>A growable byte buffer over a rented array, for decoded strings and resolved IRIs.</summary>
internal ref struct PooledBytes
{
    private byte[]? _bytes;
    private int _count;

    public readonly int Count => Span.Length;

    public readonly ReadOnlySpan<byte> Span => _bytes is null ? default : _bytes.AsSpan(0, _count);

    public void Clear() => _count = 0;

    public void Add(byte b)
    {
        Ensure(1);
        _bytes![_count++] = b;
    }

    public void Add(ReadOnlySpan<byte> bytes)
    {
        Ensure(bytes.Length);
        bytes.CopyTo(_bytes.AsSpan(_count));
        _count += bytes.Length;
    }

    /// <summary>Reserves room for at least this many more bytes and returns the writable tail.</summary>
    public Span<byte> Reserve(int length)
    {
        Ensure(length);
        return _bytes.AsSpan(_count, length);
    }

    public void Commit(int written) => _count += written;

    private void Ensure(int more)
    {
        int needed = _count + more;

        if (_bytes is null)
        {
            _bytes = ArrayPool<byte>.Shared.Rent(Math.Max(256, needed));
        }
        else if (needed > _bytes.Length)
        {
            byte[] grown = ArrayPool<byte>.Shared.Rent(Math.Max(needed, _bytes.Length * 2));
            _bytes.AsSpan(0, _count).CopyTo(grown);
            ArrayPool<byte>.Shared.Return(_bytes);
            _bytes = grown;
        }
    }

    public void Dispose()
    {
        if (_bytes is not null)
        {
            ArrayPool<byte>.Shared.Return(_bytes);
            _bytes = null;
            _count = 0;
        }
    }
}
