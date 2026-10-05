// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The new terms a bulk load has met since its last spill: each one's
/// reference and key, written sorted by reference and without repeats when
/// the table fills (ADR 0081). Records are a reference, a <c>u32</c> length
/// and the key.
/// </summary>
internal sealed class TermTable
{
    private readonly List<(BulkRef Ref, int Offset, int Length)> _entries = [];
    private byte[] _arena;
    private int _used;

    internal TermTable(int arenaBytes) => _arena = new byte[arenaBytes];

    internal int Count => _entries.Count;

    /// <summary>Adds a term; true when the table is full and must be spilled before the next.</summary>
    internal bool Add(BulkRef reference, ReadOnlySpan<byte> key)
    {
        if (key.Length > _arena.Length - _used)
        {
            // A key longer than what is left: grow once to fit it, then spill.
            Array.Resize(ref _arena, Math.Max(_arena.Length, _used + key.Length));
        }

        key.CopyTo(_arena.AsSpan(_used));
        _entries.Add((reference, _used, key.Length));
        _used += key.Length;
        return _used >= _arena.Length * 3 / 4;
    }

    /// <summary>Writes the table sorted by reference, each term once, and empties it.</summary>
    /// <exception cref="BulkLoadException">Two different keys have the same reference.</exception>
    internal async ValueTask<BlobName?> SpillAsync(SpillSpace space, CancellationToken cancellationToken)
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        _entries.Sort((a, b) => a.Ref.CompareTo(b.Ref));
        BlobName name = space.Next("terms");
        await using IBlobWriter writer = await space.Store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[1 << 16];
        int filled = 0;

        for (int i = 0; i < _entries.Count; i++)
        {
            (BulkRef reference, int offset, int length) = _entries[i];

            if (i > 0 && _entries[i - 1].Ref.Equals(reference))
            {
                (_, int previous, int previousLength) = _entries[i - 1];

                if (!_arena.AsSpan(previous, previousLength).SequenceEqual(_arena.AsSpan(offset, length)))
                {
                    throw BulkLoadException.Collision();
                }

                continue;
            }

            if (filled + BulkRef.Size + 4 + length > buffer.Length)
            {
                await writer.WriteAsync(buffer.AsMemory(0, filled), cancellationToken).ConfigureAwait(false);
                filled = 0;

                if (BulkRef.Size + 4 + length > buffer.Length)
                {
                    buffer = new byte[BulkRef.Size + 4 + length];
                }
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled), reference.Hi);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled + 8), reference.Lo);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(filled + 16), length);
            _arena.AsSpan(offset, length).CopyTo(buffer.AsSpan(filled + 20));
            filled += BulkRef.Size + 4 + length;
        }

        await writer.WriteAsync(buffer.AsMemory(0, filled), cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
        _entries.Clear();
        _used = 0;
        return name;
    }
}

/// <summary>One spilled term table, read in order, synchronously.</summary>
internal sealed class TermRunReader : IDisposable
{
    private readonly IReadableBlob _blob;
    private readonly long _length;
    private long _at;

    internal TermRunReader(IReadableBlob blob)
    {
        _blob = blob;
        _length = blob.Length.Value;
    }

    internal BulkRef Ref { get; private set; }

    internal byte[] Key { get; private set; } = new byte[256];

    internal int KeyLength { get; private set; }

    internal ReadOnlySpan<byte> CurrentKey => Key.AsSpan(0, KeyLength);

    internal bool Next()
    {
        if (_at >= _length)
        {
            return false;
        }

        Span<byte> head = stackalloc byte[BulkRef.Size + 4];
        Fill(_at, head);
        Ref = new BulkRef(BinaryPrimitives.ReadUInt64LittleEndian(head), BinaryPrimitives.ReadUInt64LittleEndian(head[8..]));
        KeyLength = BinaryPrimitives.ReadInt32LittleEndian(head[16..]);

        if (KeyLength > Key.Length)
        {
            Key = new byte[KeyLength];
        }

        Fill(_at + head.Length, Key.AsSpan(0, KeyLength));
        _at += head.Length + KeyLength;
        return true;
    }

    public void Dispose() => _blob.Dispose();

    private void Fill(long offset, Span<byte> destination)
    {
        if (_blob.Read(new ByteOffset(offset), destination) != destination.Length)
        {
            throw new IOException("A bulk load's term spill is shorter than was written.");
        }
    }
}

/// <summary>
/// Spilled term tables merged in reference order, each term once: the
/// load's new terms, in the order their final ids follow (ADR 0081).
/// </summary>
internal sealed class TermMerge : IDisposable
{
    private readonly List<TermRunReader> _live = [];

    private TermMerge(List<TermRunReader> readers)
    {
        foreach (TermRunReader reader in readers)
        {
            if (reader.Next())
            {
                _live.Add(reader);
            }
            else
            {
                reader.Dispose();
            }
        }
    }

    internal BulkRef Ref { get; private set; }

    internal byte[] Key { get; private set; } = new byte[256];

    internal int KeyLength { get; private set; }

    internal ReadOnlySpan<byte> CurrentKey => Key.AsSpan(0, KeyLength);

    internal static async ValueTask<TermMerge> OpenAsync(IDerivedStore store, IEnumerable<BlobName> runs, CancellationToken cancellationToken)
    {
        List<TermRunReader> readers = [];

        foreach (BlobName run in runs)
        {
            readers.Add(new TermRunReader(await store.OpenAsync(run, cancellationToken).ConfigureAwait(false)));
        }

        return new TermMerge(readers);
    }

    /// <summary>The next term; false at the end.</summary>
    /// <exception cref="BulkLoadException">Two different keys have the same reference.</exception>
    internal bool Next()
    {
        if (_live.Count == 0)
        {
            return false;
        }

        int best = 0;

        for (int i = 1; i < _live.Count; i++)
        {
            if (_live[i].Ref.CompareTo(_live[best].Ref) < 0)
            {
                best = i;
            }
        }

        BulkRef reference = _live[best].Ref;
        Ref = reference;
        KeyLength = _live[best].KeyLength;

        if (KeyLength > Key.Length)
        {
            Key = new byte[KeyLength];
        }

        _live[best].CurrentKey.CopyTo(Key);

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            TermRunReader reader = _live[i];

            if (!reader.Ref.Equals(reference))
            {
                continue;
            }

            if (!reader.CurrentKey.SequenceEqual(CurrentKey))
            {
                throw BulkLoadException.Collision();
            }

            if (!reader.Next())
            {
                reader.Dispose();
                _live.RemoveAt(i);
            }
        }

        return true;
    }

    public void Dispose()
    {
        foreach (TermRunReader reader in _live)
        {
            reader.Dispose();
        }

        _live.Clear();
    }
}
