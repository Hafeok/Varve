// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>A checkpoint as the store holds it: a run over the blob's own bytes.</summary>
internal sealed class Checkpoint
{
    internal Checkpoint(long position, Run run, long canonicalCount, long blankCount, byte[] headerHash)
    {
        Position = position;
        Run = run;
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
        HeaderHash = headerHash;
    }

    internal long Position { get; }

    internal Run Run { get; }

    internal long CanonicalCount { get; }

    internal long BlankCount { get; }

    internal byte[] HeaderHash { get; }

    internal const string Prefix = "checkpoints/";

    internal static string Name(long position) => Prefix + position.ToString("D20", CultureInfo.InvariantCulture);
}

/// <summary>
/// The checkpoint blob of ADR 0041: a header naming the commit it
/// materialises, the six sorted key arrays, the dictionary up to its
/// watermark, and a hash of all of it. Derived data, droppable, and versioned;
/// no byte is frozen.
/// </summary>
/// <remarks>
/// The key arrays are the struct's own bytes, so a load reinterprets them in
/// place. That makes the encoding little-endian-host only, which every .NET
/// target this repository builds for is; a big-endian host treats every
/// checkpoint as absent — a cache miss, never a wrong answer.
/// </remarks>
internal static class CheckpointFormat
{
    private const int HeaderLength = 72;

    private static ReadOnlySpan<byte> Magic => "VRVK\0\0\0\0"u8;

    internal static byte[] Encode(long position, byte[] headerHash, long canonicalCount, long blankCount, Run run, TermDictionary dictionary)
    {
        int quads = run.AssertedCount;
        ArrayBufferWriter<byte> writer = new(HeaderLength + (quads * QuadKey.Size * Orders.Count) + 64);
        writer.Write(Magic);
        LogFormat.WriteUInt64(writer, (ulong)position);
        writer.Write(headerHash);
        LogFormat.WriteUInt64(writer, (ulong)canonicalCount);
        LogFormat.WriteUInt64(writer, (ulong)blankCount);
        LogFormat.WriteUInt64(writer, (ulong)quads);

        for (int order = 0; order < Orders.Count; order++)
        {
            writer.Write(MemoryMarshal.AsBytes(run.Asserted((IndexOrder)order).Span));
        }

        LogFormat.WriteUInt64(writer, (ulong)canonicalCount);

        for (long counter = 1; counter <= canonicalCount; counter++)
        {
            ulong id = TermIds.Canonical(counter);
            Allocation entry = dictionary.TryComponents(counter, out (ulong S, ulong P, ulong O) parts)
                ? new Allocation(id, null, parts.S, parts.P, parts.O)
                : new Allocation(id, dictionary.Term(id));
            LogFormat.WriteAllocation(writer, in entry);
        }

        writer.Write(SHA256.HashData(writer.WrittenSpan));
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// A checkpoint over a blob's bytes, or null when the blob is not one this
    /// store can trust: wrong magic, wrong hash, a big-endian host, or a
    /// dictionary that disagrees with the log's.
    /// </summary>
    internal static Checkpoint? TryDecode(ReadOnlyMemory<byte> blob, TermDictionary dictionary)
    {
        ReadOnlySpan<byte> bytes = blob.Span;

        if (!BitConverter.IsLittleEndian || bytes.Length < HeaderLength + LogFormat.HashLength || !bytes[..8].SequenceEqual(Magic))
        {
            return null;
        }

        ReadOnlySpan<byte> covered = bytes[..^LogFormat.HashLength];

        if (!SHA256.HashData(covered).AsSpan().SequenceEqual(bytes[^LogFormat.HashLength..]))
        {
            return null;
        }

        long position = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        byte[] headerHash = bytes.Slice(16, LogFormat.HashLength).ToArray();
        long canonicalCount = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[48..]);
        long blankCount = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[56..]);
        ulong quads = BinaryPrimitives.ReadUInt64LittleEndian(bytes[64..]);

        ulong keysLength = quads * QuadKey.Size * Orders.Count;

        if (quads > int.MaxValue / (QuadKey.Size * Orders.Count) || (ulong)covered.Length < HeaderLength + keysLength)
        {
            return null;
        }

        ReadOnlyMemory<QuadKey>[] orders = new ReadOnlyMemory<QuadKey>[Orders.Count];
        int orderBytes = (int)quads * QuadKey.Size;

        for (int order = 0; order < Orders.Count; order++)
        {
            ReadOnlyMemory<byte> slice = blob.Slice(HeaderLength + (order * orderBytes), orderBytes);
            orders[order] = new KeyMemory(slice).Memory;
        }

        if (!DictionaryAgrees(covered[(HeaderLength + (int)keysLength)..], canonicalCount, dictionary, position))
        {
            return null;
        }

        return new Checkpoint(position, Run.FromSorted(orders), canonicalCount, blankCount, headerHash);
    }

    private static bool DictionaryAgrees(ReadOnlySpan<byte> section, long canonicalCount, TermDictionary dictionary, long position)
    {
        if (canonicalCount > dictionary.CanonicalCount)
        {
            return false;
        }

        try
        {
            LogFormat.Reader reader = new(section, position);

            if ((long)reader.UInt64() != canonicalCount)
            {
                return false;
            }

            for (long counter = 1; counter <= canonicalCount; counter++)
            {
                Allocation entry = LogFormat.ReadAllocation(ref reader);
                ulong id = TermIds.Canonical(counter);

                if (entry.Id != id)
                {
                    return false;
                }

                bool agrees = entry.IsTriple
                    ? dictionary.TryComponents(counter, out (ulong S, ulong P, ulong O) parts)
                        && parts == (entry.Subject, entry.Predicate, entry.Object)
                    : entry.Term!.Equals(dictionary.Term(id));

                if (!agrees)
                {
                    return false;
                }
            }

            reader.End();
            return true;
        }
        catch (LogVerificationException)
        {
            return false;
        }
    }

    /// <summary>A blob's bytes seen as quad keys, in place, with no copy.</summary>
    private sealed class KeyMemory : MemoryManager<QuadKey>
    {
        private readonly ReadOnlyMemory<byte> _bytes;

        internal KeyMemory(ReadOnlyMemory<byte> bytes) => _bytes = bytes;

        public override Span<QuadKey> GetSpan() =>
            MemoryMarshal.Cast<byte, QuadKey>(MemoryMarshal.AsMemory(_bytes).Span);

        public override MemoryHandle Pin(int elementIndex = 0) =>
            MemoryMarshal.AsMemory(_bytes).Slice(elementIndex * QuadKey.Size).Pin();

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}

/// <summary>Composes a chain of exact deltas — consecutive commits — in one pass.</summary>
/// <remarks>
/// The specification's <c>;</c> is associative only over such chains
/// (specification 1.3, §6; ADR 0047), which is every use the store makes of it. A running net
/// state per quad composes a chain in time proportional to its total size.
/// </remarks>
internal sealed class DeltaChain
{
    private readonly System.Collections.Generic.Dictionary<Quad, bool> _net = [];

    internal void Add(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        foreach (Quad quad in asserted)
        {
            if (_net.TryGetValue(quad, out bool present) && !present)
            {
                _net.Remove(quad);
            }
            else
            {
                _net[quad] = true;
            }
        }

        foreach (Quad quad in retracted)
        {
            if (_net.TryGetValue(quad, out bool present) && present)
            {
                _net.Remove(quad);
            }
            else
            {
                _net[quad] = false;
            }
        }
    }

    internal QuadDelta ToDelta()
    {
        System.Collections.Generic.List<Quad> asserted = [];
        System.Collections.Generic.List<Quad> retracted = [];

        foreach (System.Collections.Generic.KeyValuePair<Quad, bool> entry in _net)
        {
            (entry.Value ? asserted : retracted).Add(entry.Key);
        }

        return QuadDelta.Create(CollectionsMarshal.AsSpan(asserted), CollectionsMarshal.AsSpan(retracted));
    }
}
