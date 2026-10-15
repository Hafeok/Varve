// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Varve.Store.ObjectStorage;

/// <summary>What a chunk is, beyond its bytes (ADR 0135).</summary>
[Flags]
internal enum ChunkFlags : byte
{
    /// <summary>A chunk of log bytes.</summary>
    None = 0,

    /// <summary>The first slot a writer epoch created: never deleted (ADR 0136).</summary>
    Fence = 1,

    /// <summary>Seals its segment: no chunk with bytes follows it.</summary>
    Seal = 2,

    /// <summary>Opens a new segment, holding no bytes.</summary>
    Opens = 4,
}

/// <summary>
/// The header in front of every chunk object's log bytes (ADR 0135): 136
/// bytes, little-endian, ending in the SHA-256 of what precedes it.
/// </summary>
/// <remarks>
/// <code>
///   0  "VRVC"
///   4  u8   version, 1
///   5  u8   flags
///   6  u16  zero
///   8  u64  writer epoch
///  16  u32  segment
///  20  u32  chunk
///  24  u64  offset of the first byte in the segment
///  32  u32  length of the bytes
///  36  u32  zero
///  40  [32] SHA-256 of the bytes
///  72  [32] the previous chunk's header hash in this segment; zero for chunk 0
/// 104  [32] SHA-256 of bytes 0..104: this header's hash
/// </code>
/// The bytes after the header are the log's, unchanged: the log is the
/// concatenation of every chunk's bytes in order, so format 1 does not change.
/// </remarks>
internal readonly record struct ChunkHeader(
    ChunkFlags Flags, long Epoch, int Segment, int Chunk, long Offset, int Length, byte[] PayloadHash, byte[] Previous)
{
    public const int Size = 136;

    private const byte Version = 1;
    private static ReadOnlySpan<byte> Magic => "VRVC"u8;

    /// <summary>A header's hash for chunk 0, which follows nothing.</summary>
    public static byte[] Nothing() => new byte[32];

    /// <summary>The chunk object's bytes: this header, then <paramref name="payload"/>.</summary>
    public static byte[] Compose(ChunkFlags flags, long epoch, int segment, int chunk, long offset, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> previous, out byte[] headerHash)
    {
        byte[] bytes = new byte[Size + payload.Length];
        Span<byte> header = bytes.AsSpan(0, Size);
        Magic.CopyTo(header);
        header[4] = Version;
        header[5] = (byte)flags;
        BinaryPrimitives.WriteInt64LittleEndian(header[8..], epoch);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], segment);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], chunk);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], offset);
        BinaryPrimitives.WriteInt32LittleEndian(header[32..], payload.Length);
        SHA256.HashData(payload, header[40..72]);
        previous.CopyTo(header[72..104]);
        SHA256.HashData(header[..104], header[104..136]);
        payload.CopyTo(bytes.AsSpan(Size));
        headerHash = header[104..136].ToArray();
        return bytes;
    }

    /// <summary>Reads and checks a chunk object; false when it is not one, or its bytes do not match its header.</summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out ChunkHeader header, out byte[] headerHash)
    {
        header = default;
        headerHash = [];

        if (bytes.Length < Size || !bytes[..4].SequenceEqual(Magic) || bytes[4] != Version
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]) != 0)
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes[..104], hash);

        if (!hash.SequenceEqual(bytes[104..136]))
        {
            return false;
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]);

        if (length < 0 || bytes.Length - Size != length)
        {
            return false;
        }

        SHA256.HashData(bytes[Size..], hash);

        if (!hash.SequenceEqual(bytes[40..72]))
        {
            return false;
        }

        header = new ChunkHeader(
            (ChunkFlags)bytes[5],
            BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]),
            BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]),
            length,
            bytes[40..72].ToArray(),
            bytes[72..104].ToArray());
        headerHash = bytes[104..136].ToArray();
        return true;
    }
}
