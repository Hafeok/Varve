// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A slice of a derived blob's directory that is read when needed rather
/// than held (ADR 0240): where it begins, how many items it has, and the
/// check of its words that the blob's open took while it hashed the whole
/// directory.
/// </summary>
internal readonly record struct SeekSlice(long At, long Count, ulong Check);

/// <summary>
/// Where a format-5 extent's seek directory is, in its blob (ADR 0240): the
/// rows within's key section and its fences, block starts and block checks,
/// and the checks of the three columns, the open positions', the closing
/// positions' and the rows within's.
/// </summary>
internal sealed record SeekDirectorySource(
    IReadableBlob Blob, long KeysAt, long KeysCount, long KeysLength, SeekSlice Fences, SeekSlice Starts, SeekSlice Checks, SeekSlice[] Columns)
{
    /// <summary>The bytes the directory takes in memory: 48 a block of rows within, and 8 a block of each column.</summary>
    internal long Bytes
    {
        get
        {
            long bytes = (Fences.Count * (QuadKey.Size + 8 + 8)) + 8;

            foreach (SeekSlice column in Columns)
            {
                bytes += column.Count * 8;
            }

            return bytes;
        }
    }
}

/// <summary>
/// A check of a run of little-endian words in order, the same however the
/// reads that pass them are cut: what holds a slice of a directory read a
/// second time to what it was when the directory's hash was verified.
/// </summary>
internal static class WordCheck
{
    internal const ulong Seed = 0x243F6A8885A308D3UL;

    /// <summary>The check with <paramref name="bytes"/>' words added; its length is a multiple of eight.</summary>
    internal static ulong Add(ulong check, ReadOnlySpan<byte> bytes)
    {
        for (int at = 0; at + 8 <= bytes.Length; at += 8)
        {
            check = (check ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..])) * 0x9E3779B97F4A7C15UL;
            check ^= check >> 32;
        }

        return check;
    }

    internal static ulong Finish(ulong check)
    {
        check = (check ^ (check >> 30)) * 0xBF58476D1CE4E5B9UL;
        check = (check ^ (check >> 27)) * 0x94D049BB133111EBUL;
        return check ^ (check >> 31);
    }
}

/// <summary>
/// The seek directories a dataset holds in memory (ADR 0240): of the extents
/// an as-of read inside a set's window last read, the most recently used
/// first, up to a ceiling in bytes. A directory is read from its extent and
/// checked when a read asks for one the cache does not hold; the least
/// recently used go when the ceiling is passed, and a directory larger than
/// the ceiling is given to its one read and not kept. A read holds what it
/// was given until it ends, so eviction never takes a directory from under it.
/// </summary>
[DesignDecision(typeof(ResidentMemory.SeekDirectoriesAreReadOnDemand), Scope = ExceptionScope.Boundary)]
internal sealed class SeekDirectoryCache(ByteCount ceiling)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ExtentInfo, LinkedListNode<Held>> _held = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Held> _recent = new();
    private long _bytes;
    private long _loads;

    /// <summary>The most bytes of directories kept.</summary>
    internal ByteCount Ceiling { get; } = ceiling;

    /// <summary>The bytes of the directories kept now: never more than <see cref="Ceiling"/>.</summary>
    internal ByteCount Bytes => new(Volatile.Read(ref _bytes));

    /// <summary>How many directories have been read from their extents: the cache's misses.</summary>
    internal long Loads => Volatile.Read(ref _loads);

    /// <summary>The extent with its seek directory: the one kept, or read now and kept if it fits.</summary>
    /// <exception cref="DerivedDamageException">The directory does not match the check the extent's open took.</exception>
    internal ExtentInfo Get(ExtentInfo extent)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(extent, out LinkedListNode<Held>? node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Sought;
            }
        }

        // Read outside the gate: a read of one extent never waits for another's.
        ExtentInfo sought = extent.WithSeekDirectory();
        Interlocked.Increment(ref _loads);
        long bytes = extent.SeekDirectoryBytes;

        lock (_gate)
        {
            if (_held.TryGetValue(extent, out LinkedListNode<Held>? raced))
            {
                return raced.Value.Sought;
            }

            if (bytes > Ceiling.Value)
            {
                return sought;
            }

            _held[extent] = _recent.AddFirst(new Held(extent, sought, bytes));
            _bytes += bytes;

            while (_bytes > Ceiling.Value && _recent.Last is { } oldest)
            {
                _recent.RemoveLast();
                _held.Remove(oldest.Value.Extent);
                _bytes -= oldest.Value.Bytes;
            }
        }

        return sought;
    }

    /// <summary>Drops an extent's directory: its set has left the chain.</summary>
    internal void Forget(ExtentInfo extent)
    {
        lock (_gate)
        {
            if (_held.Remove(extent, out LinkedListNode<Held>? node))
            {
                _recent.Remove(node);
                _bytes -= node.Value.Bytes;
            }
        }
    }

    private sealed record Held(ExtentInfo Extent, ExtentInfo Sought, long Bytes);
}
