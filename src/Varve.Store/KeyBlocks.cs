// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.IO;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// A block of up to 128 sorted keys, compressed (ADR 0080): the first key as
/// it is, 32 bytes, then each key as the difference from the one before it — a
/// byte naming the first of its four ids that differs, that id's increase as
/// a varint, and the ids after it as varints. Sorted keys share long prefixes
/// and dense counter ids are small (ADR 0012), so a key takes five to seven
/// bytes where it took 32.
/// </summary>
/// <remarks>
/// A block decodes on its own, so a reader still reads one block per seek and
/// the cursor still walks 128 keys at a time: what changed is only how many
/// bytes a block is.
/// </remarks>
internal static class KeyBlocks
{
    /// <summary>The most bytes a block of 128 keys can take: no key is longer than a byte and four ten-byte varints.</summary>
    internal const int MaxBytes = QuadKey.Size + ((KeySection.BlockKeys - 1) * (1 + (4 * LogFormat.MaxUlebLength)));

    /// <summary>Encodes sorted keys, at most a block of them; returns the bytes written.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Encode(ReadOnlySpan<QuadKey> keys, Span<byte> destination)
    {
        if (keys.IsEmpty)
        {
            return 0;
        }

        QuadKey previous = keys[0];
        BinaryPrimitives.WriteUInt64LittleEndian(destination, previous.K0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], previous.K1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], previous.K2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], previous.K3);
        int at = QuadKey.Size;

        for (int i = 1; i < keys.Length; i++)
        {
            QuadKey key = keys[i];
            int differs = key.K0 != previous.K0 ? 0 : key.K1 != previous.K1 ? 1 : key.K2 != previous.K2 ? 2 : 3;
            destination[at++] = (byte)differs;
            at += LogFormat.Uleb(destination[at..], Component(key, differs) - Component(previous, differs));

            for (int c = differs + 1; c < 4; c++)
            {
                at += LogFormat.Uleb(destination[at..], Component(key, c));
            }

            previous = key;
        }

        return at;
    }

    /// <summary>
    /// Decodes a block into exactly <paramref name="keys"/>' length of keys,
    /// using every byte.
    /// </summary>
    /// <exception cref="IOException">The bytes are not a block of that many keys.</exception>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void Decode(ReadOnlySpan<byte> bytes, Span<QuadKey> keys)
    {
        if (keys.IsEmpty)
        {
            return;
        }

        if (bytes.Length < QuadKey.Size)
        {
            throw new IOException(DamagedMessage);
        }

        ulong k0 = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        ulong k1 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        ulong k2 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
        ulong k3 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        keys[0] = new QuadKey(k0, k1, k2, k3);
        int at = QuadKey.Size;

        for (int i = 1; i < keys.Length; i++)
        {
            if (at >= bytes.Length)
            {
                throw new IOException(DamagedMessage);
            }

            int differs = bytes[at++];

            switch (differs)
            {
                case 0:
                    k0 += Uleb(bytes, ref at);
                    k1 = Uleb(bytes, ref at);
                    k2 = Uleb(bytes, ref at);
                    k3 = Uleb(bytes, ref at);
                    break;
                case 1:
                    k1 += Uleb(bytes, ref at);
                    k2 = Uleb(bytes, ref at);
                    k3 = Uleb(bytes, ref at);
                    break;
                case 2:
                    k2 += Uleb(bytes, ref at);
                    k3 = Uleb(bytes, ref at);
                    break;
                case 3:
                    k3 += Uleb(bytes, ref at);
                    break;
                default:
                    throw new IOException(DamagedMessage);
            }

            keys[i] = new QuadKey(k0, k1, k2, k3);
        }

        if (at != bytes.Length)
        {
            throw new IOException(DamagedMessage);
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static ulong Component(in QuadKey key, int index) => index switch
    {
        0 => key.K0,
        1 => key.K1,
        2 => key.K2,
        _ => key.K3,
    };

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static ulong Uleb(ReadOnlySpan<byte> bytes, ref int at)
    {
        ulong value = 0;

        for (int shift = 0; shift < 70; shift += 7)
        {
            if (at >= bytes.Length)
            {
                throw new IOException(DamagedMessage);
            }

            byte b = bytes[at++];
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new IOException(DamagedMessage);
    }

    private const string DamagedMessage = "A derived run's block does not decode; the run is damaged.";
}

/// <summary>
/// Sorted keys into compressed blocks, a section at a time: the bytes to
/// write, each block's first key — its fence — and where each block begins.
/// What it holds is one block of keys and the bytes not yet written.
/// </summary>
internal sealed class KeyBlockWriter
{
    /// <summary>Bytes held before the caller is told to write them.</summary>
    internal const int FlushBytes = 1 << 16;

    private readonly QuadKey[] _block = new QuadKey[KeySection.BlockKeys];
    private readonly byte[] _pending = new byte[FlushBytes + (KeyBlocks.MaxBytes * (2048 / KeySection.BlockKeys + 1))];
    private int _inBlock;

    internal int Pending { get; private set; }

    /// <summary>The section's length so far: the bytes encoded since it began.</summary>
    internal long Length { get; private set; }

    internal long Count { get; private set; }

    internal System.Collections.Generic.List<QuadKey> Fences { get; } = [];

    internal System.Collections.Generic.List<long> Blocks { get; } = [];

    /// <summary>Starts a new section; the bytes pending stay pending.</summary>
    internal void Begin()
    {
        Length = 0;
        Count = 0;
        Fences.Clear();
        Blocks.Clear();
    }

    /// <summary>Adds keys, at most 2,048 at a time; blocks that fill are encoded into the pending bytes.</summary>
    internal void Add(ReadOnlySpan<QuadKey> keys)
    {
        foreach (QuadKey key in keys)
        {
            _block[_inBlock++] = key;

            if (_inBlock == KeySection.BlockKeys)
            {
                Emit();
            }
        }

        Count += keys.Length;
    }

    /// <summary>Ends the section: a part-filled last block is encoded.</summary>
    internal void End()
    {
        if (_inBlock > 0)
        {
            Emit();
        }
    }

    /// <summary>The pending bytes; the caller writes them, then calls <see cref="Written"/>.</summary>
    internal ReadOnlyMemory<byte> PendingBytes => _pending.AsMemory(0, Pending);

    internal void Written() => Pending = 0;

    private void Emit()
    {
        Fences.Add(_block[0]);
        Blocks.Add(Length);
        int written = KeyBlocks.Encode(_block.AsSpan(0, _inBlock), _pending.AsSpan(Pending));
        Pending += written;
        Length += written;
        _inBlock = 0;
    }
}
