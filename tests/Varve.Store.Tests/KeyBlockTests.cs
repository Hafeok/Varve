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
