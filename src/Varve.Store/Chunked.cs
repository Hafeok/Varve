// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// A list held in chunks of 64 KiB, each below the large object heap's
/// threshold: what a run holds per block — its fences and where its blocks
/// begin — grows with the dataset, and as one array per run it was rebuilt
/// larger at every merge and checkpoint on a heap that is not compacted,
/// which is how the soak's working set kept growing with the live heap flat
/// (issue #61, ADR 0078). In chunks it is never copied to grow, and the
/// collector compacts it.
/// </summary>
internal sealed class Chunked<T>
    where T : unmanaged
{
    /// <summary>The bytes of a chunk: under the 85,000 at which an array goes to the large object heap.</summary>
    internal const int ChunkBytes = 1 << 16;

    private static readonly int PerChunk = ChunkBytes / Unsafe.SizeOf<T>();

    private readonly List<T[]> _chunks = [];
    private readonly long _capacity;

    /// <summary>A list that grows a whole chunk at a time.</summary>
    internal Chunked()
    {
        _capacity = long.MaxValue;
    }

    /// <summary>A list of a known size: its last chunk is no longer than it needs, so a small one costs its items, not 64 KiB.</summary>
    internal Chunked(long capacity)
    {
        _capacity = capacity;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Count { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int ChunkCount => _chunks.Count;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal T this[long index] => _chunks[(int)(index / PerChunk)][index % PerChunk];

    internal void Add(T item)
    {
        int at = (int)(Count % PerChunk);

        if (at == 0)
        {
            _chunks.Add(new T[Math.Min(PerChunk, _capacity - Count)]);
        }

        _chunks[^1][at] = item;
        Count++;
    }

    /// <summary>The items of chunk <paramref name="chunk"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ReadOnlySpan<T> Chunk(int chunk) =>
        _chunks[chunk].AsSpan(0, chunk == _chunks.Count - 1 ? (int)(Count - ((long)chunk * PerChunk)) : PerChunk);

    /// <summary>The first index of chunk <paramref name="chunk"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static long Start(int chunk) => (long)chunk * PerChunk;
}

/// <summary>Searches of sorted keys held in chunks.</summary>
internal static class ChunkedKeys
{
    /// <summary>The first index whose key is at or above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static long LowerBound(Chunked<QuadKey> keys, in QuadKey bound)
    {
        // The first chunk whose last key is at or above the bound.
        int low = 0;
        int high = keys.ChunkCount;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (keys.Chunk(middle)[^1].CompareTo(bound) < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low == keys.ChunkCount ? keys.Count : Chunked<QuadKey>.Start(low) + Run.LowerBound(keys.Chunk(low), in bound);
    }

    /// <summary>The first index whose key is above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static long UpperBound(Chunked<QuadKey> keys, in QuadKey bound)
    {
        // The first chunk whose last key is above the bound.
        int low = 0;
        int high = keys.ChunkCount;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (keys.Chunk(middle)[^1].CompareTo(bound) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low == keys.ChunkCount ? keys.Count : Chunked<QuadKey>.Start(low) + Run.UpperBound(keys.Chunk(low), in bound);
    }
}
