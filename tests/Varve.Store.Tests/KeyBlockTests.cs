// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using CsCheck;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>Compressed key blocks (ADR 0080).</summary>
public class KeyBlockTests
{
    private static readonly Gen<ulong> Id = Gen.OneOf(Gen.ULong[0, 40], Gen.ULong[0, 1UL << 20], Gen.ULong, Gen.Const(ulong.MaxValue));

    private static readonly Gen<QuadKey[]> SortedKeys =
        Gen.Select(Id, Id, Id, Id, (a, b, c, d) => new QuadKey(a, b, c, d)).Array[1, KeySection.BlockKeys]
            .Select(keys => keys.Distinct().Order().ToArray());

    /// <summary>
    /// Fences held in chunks below the large object heap (issue #61) are
    /// searched as one sorted array is: the same lower and upper bounds for
    /// any bound, across chunk boundaries — 2,048 keys to a chunk — and at
    /// both ends.
    /// </summary>
    [Fact]
    public void chunked_fences_are_searched_as_one_array()
    {
        Gen.Select(Gen.Int[0, 7000], Gen.ULong[1, 4], Gen.ULong[0, 30000].Array[1, 20]).Sample((count, step, probes) =>
        {
            QuadKey[] keys = [.. Enumerable.Range(0, count).Select(i => new QuadKey((ulong)i * step, 0, 0, 0))];
            Chunked<QuadKey> chunked = count % 2 == 0 ? new() : new(count);

            foreach (QuadKey key in keys)
            {
                chunked.Add(key);
            }

            Assert.Equal(count, chunked.Count);
            Assert.Equal((count + 2047) / 2048, chunked.ChunkCount);

            foreach (ulong probe in probes)
            {
                QuadKey bound = new(probe, 0, 0, 0);
                Assert.Equal(Run.LowerBound(keys, in bound), ChunkedKeys.LowerBound(chunked, in bound));
                Assert.Equal(Run.UpperBound(keys, in bound), ChunkedKeys.UpperBound(chunked, in bound));
            }

            for (int i = 0; i < count; i += 997)
            {
                Assert.Equal(keys[i], chunked[i]);
            }
        }, iter: 500);
    }

    [Fact]
    public void a_block_decodes_to_the_keys_it_encoded()
    {
        SortedKeys.Sample(keys =>
        {
            byte[] bytes = new byte[KeyBlocks.MaxBytes];
            int length = KeyBlocks.Encode(keys, bytes);
            QuadKey[] back = new QuadKey[keys.Length];
            KeyBlocks.Decode(bytes.AsSpan(0, length), back);
            Assert.Equal(keys, back);
        }, iter: 5_000);
    }

    [Fact]
    public void a_block_cut_short_or_left_long_does_not_decode()
    {
        SortedKeys.Where(k => k.Length > 1).Sample(keys =>
        {
            byte[] bytes = new byte[KeyBlocks.MaxBytes + 1];
            int length = KeyBlocks.Encode(keys, bytes);
            QuadKey[] back = new QuadKey[keys.Length];
            Assert.Throws<IOException>(() => KeyBlocks.Decode(bytes.AsSpan(0, length - 1), back));
            Assert.Throws<IOException>(() => KeyBlocks.Decode(bytes.AsSpan(0, length + 1), back));
        }, iter: 1_000);
    }
}
