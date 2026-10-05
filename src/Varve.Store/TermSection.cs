// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>One entry of a term section's hash index: a term key's hash and its id.</summary>
/// <remarks>Ordered by hash, then id. Sixteen bytes, written as the struct's own little-endian bytes.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct TermHash : IComparable<TermHash>, IOrdered<TermHash>
{
    internal TermHash(ulong hash, ulong id)
    {
        Hash = hash;
        Id = id;
    }

    internal const int Size = 16;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ulong Hash { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ulong Id { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(TermHash other)
    {
        int c = Hash.CompareTo(other.Hash);
        return c != 0 ? c : Id.CompareTo(other.Id);
    }
}

/// <summary>
/// A term's key: its log entry (storage format §4.4) without the id — the kind
/// and what follows it — and that key's 64-bit hash. Two canonical terms are
/// the same term exactly when their keys are equal, a triple term's key
/// naming its components by id.
/// </summary>
internal static class TermKey
{
    internal const byte Iri = 0;
    internal const byte Literal = 2;
    internal const byte Triple = 3;

    /// <summary>
    /// Writes an IRI's or a literal's key; returns its length, or the length
    /// needed, negated, when <paramref name="destination"/> is too short.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Write(RdfTerm term, Span<byte> destination)
    {
        ReadOnlySpan<byte> lexical = term.Lexical;

        if (term.Kind == RdfTermKind.Iri)
        {
            int needed = 1 + LogFormat.MaxUlebLength + lexical.Length;

            if (destination.Length < needed)
            {
                return -needed;
            }

            destination[0] = Iri;
            int at = 1 + LogFormat.Uleb(destination[1..], (ulong)lexical.Length);
            lexical.CopyTo(destination[at..]);
            return at + lexical.Length;
        }

        ReadOnlySpan<byte> datatype = term.Datatype is null ? default : term.Datatype.Lexical;
        ReadOnlySpan<byte> language = term.Language;
        int size = 2 + (3 * LogFormat.MaxUlebLength) + lexical.Length + datatype.Length + language.Length;

        if (destination.Length < size)
        {
            return -size;
        }

        destination[0] = Literal;
        int written = 1;
        written += Field(destination[written..], lexical);
        written += Field(destination[written..], datatype);
        written += Field(destination[written..], language);
        destination[written++] = (byte)term.Direction;
        LowerLanguage(destination[..written]);
        return written;
    }

    /// <summary>
    /// Lowercases, in place, the language tag of a literal's key: tags compare
    /// ignoring case (RDF 1.1 Concepts §3.3), so a key is compared and hashed
    /// with its tag lowercased, whatever case the entry keeps.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void LowerLanguage(Span<byte> key)
    {
        if (key.IsEmpty || key[0] != Literal)
        {
            return;
        }

        int at = 1;
        at = Skip(key, at);
        at = Skip(key, at);
        int length = (int)ReadUleb(key, ref at);

        for (int i = at; i < at + length && i < key.Length; i++)
        {
            if (key[i] is >= (byte)'A' and <= (byte)'Z')
            {
                key[i] = (byte)(key[i] + 32);
            }
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static int Skip(ReadOnlySpan<byte> key, int at)
    {
        long length = ReadUleb(key, ref at);
        return (int)Math.Min(key.Length, at + length);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static long ReadUleb(ReadOnlySpan<byte> key, ref int at)
    {
        long value = 0;

        for (int shift = 0; at < key.Length && shift < 63; shift += 7)
        {
            byte b = key[at++];
            value |= (long)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    /// <summary>A triple term's key, from its components' ids; at most 31 bytes.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int WriteTriple(ulong subject, ulong predicate, ulong @object, Span<byte> destination)
    {
        destination[0] = Triple;
        int at = 1;
        at += LogFormat.Uleb(destination[at..], subject);
        at += LogFormat.Uleb(destination[at..], predicate);
        at += LogFormat.Uleb(destination[at..], @object);
        return at;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static int Field(Span<byte> destination, ReadOnlySpan<byte> bytes)
    {
        int at = LogFormat.Uleb(destination, (ulong)bytes.Length);
        bytes.CopyTo(destination[at..]);
        return at + bytes.Length;
    }

    /// <summary>
    /// The key of a log entry: the entry with its leading id skipped. The
    /// entry is one this store wrote, so its id is a well-formed varint.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static ReadOnlySpan<byte> OfEntry(ReadOnlySpan<byte> entry)
    {
        int at = 0;

        while (at < entry.Length && (entry[at] & 0x80) != 0)
        {
            at++;
        }

        return entry[Math.Min(at + 1, entry.Length)..];
    }

    /// <summary>
    /// A 64-bit hash of a key, the same on every machine: eight bytes at a
    /// time multiplied in, then the bits mixed so that the hash is spread
    /// evenly enough to search the index by interpolation (ADR 0079). Not a
    /// cryptographic hash; a collision costs one more entry compared.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static ulong Hash(ReadOnlySpan<byte> key) => Hash(key, 0x243F6A8885A308D3UL);

    /// <summary>The same hash from another seed: an independent 64 bits of the same key.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static ulong Hash(ReadOnlySpan<byte> key, ulong seed)
    {
        const ulong Multiplier = 0x9E3779B97F4A7C15UL;
        ulong h = seed ^ (ulong)key.Length;
        int at = 0;

        while (key.Length - at >= 8)
        {
            h = (h ^ BinaryPrimitives.ReadUInt64LittleEndian(key[at..])) * Multiplier;
            h = (h << 27) | (h >> 37);
            at += 8;
        }

        ulong tail = 0;

        for (int shift = 0; at < key.Length; at++, shift += 8)
        {
            tail |= (ulong)key[at] << shift;
        }

        h = (h ^ tail) * Multiplier;

        // The finaliser of SplitMix64: every input bit reaches every output bit.
        h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 27)) * 0x94D049BB133111EBUL;
        return h ^ (h >> 31);
    }
}

/// <summary>
/// The dictionary entries a run carries: those of the canonical ids its
/// commits allocated, the counters <c>(From, To]</c> (ADR 0079). In memory for
/// a run of the memtable, or three regions of a derived blob — the entries,
/// their offsets, and a hash index — read through the synchronous blob read
/// (ADR 0071), so a run on disk holds nothing per term in memory.
/// </summary>
/// <remarks>
/// <para>
/// **By id**, an entry is two reads: its offset and the next, then its bytes.
/// **By term**, the term's key is hashed and the hash index — sorted by hash,
/// which a good hash spreads evenly — is searched by interpolation, so a
/// lookup reads one window of the index in the common case; each entry whose
/// hash matches is read and its key compared.
/// </para>
/// <para>
/// The entries are the log's own encoding (storage format §4.4), so a
/// checkpoint's section is the dictionary of storage format §7 with an
/// index beside it.
/// </para>
/// </remarks>
internal sealed class TermSection
{
    /// <summary>Hash index entries read per window: 512 bytes.</summary>
    internal const int Window = 32;

    private readonly byte[]? _entries;
    private readonly long[]? _offsets;
    private readonly TermHash[]? _hashes;
    private readonly IReadableBlob? _blob;
    private readonly long _entriesAt;
    private readonly long _offsetsAt;
    private readonly long _hashesAt;

    private TermSection(long from, long to, long entriesLength, byte[]? entries, long[]? offsets, TermHash[]? hashes, IReadableBlob? blob, long entriesAt, long offsetsAt, long hashesAt)
    {
        From = from;
        To = to;
        EntriesLength = entriesLength;
        _entries = entries;
        _offsets = offsets;
        _hashes = hashes;
        _blob = blob;
        _entriesAt = entriesAt;
        _offsetsAt = offsetsAt;
        _hashesAt = hashesAt;
    }

    /// <summary>The canonical counter before the section's first entry.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long From { get; }

    /// <summary>The canonical counter of the section's last entry.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long To { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Count => To - From;

    /// <summary>The total length of the entries.</summary>
    internal long EntriesLength { get; }

    internal bool OnBlob => _blob is not null;

    /// <summary>A section with no entries, at a counter.</summary>
    internal static TermSection Empty(long at) => new(at, at, 0, [], [0], [], null, 0, 0, 0);

    /// <summary>
    /// The section of one commit's allocations: the canonical ones, which are
    /// dense from <paramref name="from"/> + 1 in the order given (ADR 0012).
    /// Blank nodes have no entry.
    /// </summary>
    internal static TermSection Of(ReadOnlySpan<Allocation> allocations, long from)
    {
        int count = 0;

        foreach (Allocation allocation in allocations)
        {
            if (TermIds.ClassOf(allocation.Id) == IdClass.Canonical)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return Empty(from);
        }

        ArrayBufferWriter<byte> writer = new();
        long[] offsets = new long[count + 1];
        TermHash[] hashes = new TermHash[count];
        int index = 0;

        foreach (Allocation allocation in allocations)
        {
            if (TermIds.ClassOf(allocation.Id) != IdClass.Canonical)
            {
                continue;
            }

            if (TermIds.Counter(allocation.Id) != from + index + 1)
            {
                throw new InvalidOperationException("Canonical ids must be allocated densely and in order.");
            }

            offsets[index] = writer.WrittenCount;
            LogFormat.WriteAllocation(writer, in allocation);
            byte[] key = TermKey.OfEntry(writer.WrittenSpan[(int)offsets[index]..]).ToArray();
            TermKey.LowerLanguage(key);
            hashes[index] = new TermHash(TermKey.Hash(key), allocation.Id);
            index++;
        }

        offsets[count] = writer.WrittenCount;
        hashes.AsSpan().Sort();
        return new TermSection(from, from + count, writer.WrittenCount, writer.WrittenSpan.ToArray(), offsets, hashes, null, 0, 0, 0);
    }

    /// <summary>Two adjacent sections in memory as one.</summary>
    internal static TermSection Concat(TermSection older, TermSection newer)
    {
        if (newer.Count == 0)
        {
            return older.Count == 0 ? Empty(newer.To) : older;
        }

        if (older.Count == 0)
        {
            return newer;
        }

        if (older.To != newer.From || older.OnBlob || newer.OnBlob)
        {
            throw new InvalidOperationException("Only adjacent sections in memory are concatenated.");
        }

        byte[] entries = new byte[older.EntriesLength + newer.EntriesLength];
        older._entries.AsSpan().CopyTo(entries);
        newer._entries.AsSpan().CopyTo(entries.AsSpan((int)older.EntriesLength));

        long[] offsets = new long[older.Count + newer.Count + 1];
        older._offsets.AsSpan(0, (int)older.Count).CopyTo(offsets);

        for (int i = 0; i <= newer.Count; i++)
        {
            offsets[older.Count + i] = older.EntriesLength + newer._offsets![i];
        }

        TermHash[] hashes = new TermHash[older.Count + newer.Count];
        older._hashes.AsSpan().CopyTo(hashes);
        newer._hashes.AsSpan().CopyTo(hashes.AsSpan((int)older.Count));
        hashes.AsSpan().Sort();
        return new TermSection(older.From, newer.To, entries.Length, entries, offsets, hashes, null, 0, 0, 0);
    }

    /// <summary>A section of a derived blob, laid out as storage format §7 says.</summary>
    internal static TermSection On(IReadableBlob blob, long from, long to, long entriesAt, long entriesLength, long offsetsAt, long hashesAt) =>
        new(from, to, entriesLength, null, null, null, blob, entriesAt, offsetsAt, hashesAt);

    /// <summary>Whether the section holds the entry of a canonical counter.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Holds(long counter) => counter > From && counter <= To;

    /// <summary>The offset, within the entries, of entry <paramref name="index"/>; <c>Count</c> gives their end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Offset(long index)
    {
        if (_blob is null)
        {
            return _offsets![index];
        }

        Span<byte> bytes = stackalloc byte[8];
        Fill(_offsetsAt + (index * 8), bytes);
        return (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    /// <summary>Copies the offsets of the entries from <paramref name="index"/>.</summary>
    internal void ReadOffsets(long index, Span<long> destination)
    {
        if (_blob is null)
        {
            _offsets.AsSpan((int)index, destination.Length).CopyTo(destination);
            return;
        }

        Fill(_offsetsAt + (index * 8), MemoryMarshal.AsBytes(destination));
    }

    /// <summary>
    /// Copies the entry of a canonical counter the section holds; returns its
    /// length, or the length needed, negated, when the destination is short.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int ReadEntry(long counter, Span<byte> destination)
    {
        long index = counter - From - 1;
        long start = Offset(index);
        long length = Offset(index + 1) - start;

        if (length > destination.Length)
        {
            return (int)-length;
        }

        ReadEntries(start, destination[..(int)length]);
        return (int)length;
    }

    /// <summary>Copies entry bytes from an offset within the entries.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void ReadEntries(long offset, Span<byte> destination)
    {
        if (_blob is null)
        {
            _entries.AsSpan((int)offset, destination.Length).CopyTo(destination);
            return;
        }

        Fill(_entriesAt + offset, destination);
    }

    /// <summary>Copies hash index entries from <paramref name="index"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void ReadHashes(long index, Span<TermHash> destination)
    {
        if (_blob is null)
        {
            _hashes.AsSpan((int)index, destination.Length).CopyTo(destination);
            return;
        }

        Fill(_hashesAt + (index * TermHash.Size), MemoryMarshal.AsBytes(destination));
    }

    /// <summary>
    /// The id of the canonical term whose key this is, when the section holds
    /// it. <paramref name="scratch"/> takes the entries compared; one longer
    /// than the key is enough.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryFind(ReadOnlySpan<byte> key, ulong hash, Span<byte> scratch, out ulong id)
    {
        id = 0;
        long count = Count;

        if (count == 0)
        {
            return false;
        }

        Span<TermHash> window = stackalloc TermHash[Window];
        long first = FirstAtOrAbove(hash, window);

        for (long at = first; at < count; at += Window)
        {
            int read = (int)Math.Min(Window, count - at);
            ReadHashes(at, window[..read]);

            for (int i = 0; i < read; i++)
            {
                if (window[i].Hash != hash)
                {
                    return false;
                }

                if (Matches(window[i].Id, key, scratch))
                {
                    id = window[i].Id;
                    return true;
                }
            }
        }

        return false;
    }

    // Whether the entry of an id has this key. An entry longer than the
    // scratch cannot have it: the scratch is longer than the key and the id.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Matches(ulong id, ReadOnlySpan<byte> key, Span<byte> scratch)
    {
        int length = ReadEntry(TermIds.Counter(id), scratch);

        if (length <= 0)
        {
            return false;
        }

        int skip = length - TermKey.OfEntry(scratch[..length]).Length;
        Span<byte> entryKey = scratch[skip..length];
        TermKey.LowerLanguage(entryKey);
        return entryKey.SequenceEqual(key);
    }

    // The first index whose hash is at or above the one sought, by
    // interpolation while the range is wide and the guess lands, then by
    // halving. A window read at the guess usually brackets the hash at once.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private long FirstAtOrAbove(ulong hash, Span<TermHash> window)
    {
        long low = 0;
        long high = Count;
        ulong lowHash = 0;
        ulong highHash = ulong.MaxValue;
        int guesses = 0;

        while (high - low > Window)
        {
            long start;

            if (guesses++ < 4 && highHash > lowHash)
            {
                double fraction = (hash - lowHash) / (double)(highHash - lowHash);
                start = low + (long)(fraction * (high - low)) - (Window / 2);
            }
            else
            {
                start = low + ((high - low) / 2) - (Window / 2);
            }

            start = Math.Clamp(start, low, high - Window);
            ReadHashes(start, window);

            if (window[0].Hash >= hash)
            {
                high = start;
                highHash = window[0].Hash;
            }
            else if (window[Window - 1].Hash < hash)
            {
                low = start + Window;
                lowHash = window[Window - 1].Hash;
            }
            else
            {
                for (int i = 1; i < Window; i++)
                {
                    if (window[i].Hash >= hash)
                    {
                        return start + i;
                    }
                }
            }
        }

        int read = (int)(high - low);
        ReadHashes(low, window[..read]);

        for (int i = 0; i < read; i++)
        {
            if (window[i].Hash >= hash)
            {
                return low + i;
            }
        }

        return high;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Fill(long offset, Span<byte> destination)
    {
        if (_blob!.Read(new ByteOffset(offset), destination) != destination.Length)
        {
            throw new IOException("A derived run's dictionary is shorter than its directory says.");
        }
    }
}
