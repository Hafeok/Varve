// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
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

    /// <summary>
    /// A checkpoint's sections are held sparsely — every sixteenth fence, the
    /// block starts read from the directory — and answer every seek and scan
    /// as the same keys written as a run, held densely, do: lower and upper
    /// bounds, membership, and every key in order, across stride boundaries
    /// and at both ends (issue #61, ADR 0080).
    /// </summary>
    [Fact]
    public async Task a_sparse_section_answers_as_a_dense_one()
    {
        await Gen.Select(Gen.Int[0, 30_000], Gen.ULong[1, 3], Gen.Int[0, 60_000].Array[1, 40]).SampleAsync(
            async (count, step, probes) =>
            {
                Quad[] quads = [.. Enumerable.Range(0, count).Select(i => new Quad(new TermHandle(((ulong)i * step) + 1), new TermHandle(((ulong)i % 7) + 1), new TermHandle(((ulong)i % 13) + 1)))];
                Run run = Run.FromDelta(quads, [], TermSection.Of([], 0), 0, 1);
                MemoryStorage storage = new();
                await DerivedFormat.WriteRunAsync(storage.Derived, new BlobName("run"), DerivedFormat.KindRun, T.Id, 0, 1, new byte[32], DerivedFormat.MergeOf([run], dropRetractions: false), [run.Terms], 0, T.Ct);
                await DerivedFormat.WriteRunAsync(storage.Derived, new BlobName("checkpoint"), DerivedFormat.KindCheckpoint, T.Id, 0, 1, new byte[32], DerivedFormat.MergeOf([run], dropRetractions: false), [run.Terms], 0, T.Ct);
                LoadedRun dense = (await DerivedFormat.TryLoadAsync(storage.Derived, new BlobName("run"), T.Id, DerivedFormat.KindRun, T.Ct))!;
                LoadedRun sparse = (await DerivedFormat.TryLoadAsync(storage.Derived, new BlobName("checkpoint"), T.Id, DerivedFormat.KindCheckpoint, T.Ct))!;

                foreach (IndexOrder order in Enum.GetValues<IndexOrder>())
                {
                    KeySection d = dense.Run.Asserted(order);
                    KeySection s = sparse.Run.Asserted(order);
                    Assert.Equal(d.Count, s.Count);
                    Assert.Equal(Keys(d), Keys(s));

                    foreach (int probe in probes)
                    {
                        // A key from the run, its neighbours, and keys between.
                        QuadKey bound = count > 0 && probe < count
                            ? Orders.Key(order, in quads[probe])
                            : new QuadKey((ulong)probe, (ulong)(probe % 5), 0, 0);
                        QuadKey after = new(bound.K0, bound.K1, bound.K2, bound.K3 + 1);

                        foreach (QuadKey key in new[] { bound, after })
                        {
                            Assert.Equal(d.LowerBound(in key), s.LowerBound(in key));
                            Assert.Equal(d.UpperBound(in key), s.UpperBound(in key));
                            Assert.Equal(d.Contains(in key), s.Contains(in key));
                        }
                    }
                }

                dense.Run.Blob!.Release();
                sparse.Run.Blob!.Release();
            },
            iter: 60);

        static List<QuadKey> Keys(KeySection section)
        {
            List<QuadKey> keys = [];
            SectionSource source = new(section);
            QuadKey[] buffer = new QuadKey[300];
            int produced;

            while ((produced = source.Next(buffer)) > 0)
            {
                keys.AddRange(buffer.AsSpan(0, produced).ToArray());
            }

            return keys;
        }
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
