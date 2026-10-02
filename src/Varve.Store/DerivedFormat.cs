// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>What a derived file's header says (storage format §7).</summary>
internal readonly struct DerivedHeader
{
    internal DerivedHeader(ushort kind, long from, long to, byte[] toHash, long directoryOffset, long directoryLength, byte[] directoryHash)
    {
        Kind = kind;
        From = from;
        To = to;
        ToHash = toHash;
        DirectoryOffset = directoryOffset;
        DirectoryLength = directoryLength;
        DirectoryHash = directoryHash;
    }

    internal ushort Kind { get; }

    internal long From { get; }

    internal long To { get; }

    internal byte[] ToHash { get; }

    internal long DirectoryOffset { get; }

    internal long DirectoryLength { get; }

    internal byte[] DirectoryHash { get; }
}

/// <summary>A derived run or checkpoint opened from its blob.</summary>
internal sealed class LoadedRun
{
    internal LoadedRun(Run run, DerivedHeader header, long canonicalCount, long blankCount, long dictionaryOffset, long dictionaryLength, byte[] dictionaryHash)
    {
        Run = run;
        Header = header;
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
        DictionaryOffset = dictionaryOffset;
        DictionaryLength = dictionaryLength;
        DictionaryHash = dictionaryHash;
    }

    internal Run Run { get; }

    internal DerivedHeader Header { get; }

    internal long CanonicalCount { get; }

    internal long BlankCount { get; }

    internal long DictionaryOffset { get; }

    internal long DictionaryLength { get; }

    internal byte[] DictionaryHash { get; }
}

/// <summary>A run's place in the projection's persisted state.</summary>
internal readonly record struct StateEntry(BlobName Name, long From, long To);

/// <summary>
/// The files under <c>derived/</c> (storage format §7): runs, checkpoints and
/// the projection's state. Each is written as a stream — key sections, then
/// the directory, then the derived header, which ends the file because only
/// then is everything it describes known — and read through the synchronous
/// blob read (ADR 0071). Derived data is not read forever (ADR 0072): a file
/// that does not verify, is of another version or dataset, or names a header
/// hash that is not the log's is a cache miss.
/// </summary>
/// <remarks>
/// Keys are written as the struct's own little-endian bytes and read back the
/// same way, so a big-endian host treats every derived file as absent.
/// </remarks>
internal static class DerivedFormat
{
    internal const ushort Version = 1;
    internal const ushort KindRun = 1;
    internal const ushort KindCheckpoint = 2;
    internal const ushort KindState = 3;
    internal const int HeaderLength = 160;

    private const int Sections = Orders.Count * 2;
    private const int WriteKeys = 2048;

    private static ReadOnlySpan<byte> Magic => "VRVD"u8;

    internal static byte[] EncodeHeader(ushort kind, DatasetId dataset, long from, long to, ReadOnlySpan<byte> toHash, long directoryOffset, long directoryLength, ReadOnlySpan<byte> directoryHash)
    {
        byte[] bytes = new byte[HeaderLength];
        Span<byte> span = bytes;
        Magic.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], kind);
        dataset.WriteTo(span.Slice(8, DatasetId.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(span[24..], (ulong)from);
        BinaryPrimitives.WriteUInt64LittleEndian(span[32..], (ulong)to);
        toHash.CopyTo(span.Slice(40, LogFormat.HashLength));
        BinaryPrimitives.WriteUInt64LittleEndian(span[72..], (ulong)directoryOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[80..], (ulong)directoryLength);
        directoryHash.CopyTo(span.Slice(88, LogFormat.HashLength));
        SHA256.HashData(span[..128], span.Slice(128, LogFormat.HashLength));
        return bytes;
    }

    /// <summary>The header at the end of a derived blob, when it is one this store reads for this dataset.</summary>
    internal static bool TryReadHeader(IReadableBlob blob, DatasetId dataset, ushort kind, out DerivedHeader header)
    {
        header = default;
        long length = blob.Length.Value;

        if (!BitConverter.IsLittleEndian || length < HeaderLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[HeaderLength];

        if (blob.Read(new ByteOffset(length - HeaderLength), bytes) != HeaderLength
            || !bytes[..4].SequenceEqual(Magic)
            || !LogFormat.HashMatches(bytes[..128], bytes.Slice(128, LogFormat.HashLength))
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != kind
            || DatasetId.Read(bytes.Slice(8, DatasetId.Length)) != dataset)
        {
            return false;
        }

        long from = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        long to = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]);
        long directoryOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[72..]);
        long directoryLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes[80..]);

        if (from < 0 || to < from || directoryOffset < 0 || directoryLength < 0 || directoryOffset + directoryLength != length - HeaderLength || directoryLength > int.MaxValue)
        {
            return false;
        }

        header = new DerivedHeader(kind, from, to, bytes.Slice(40, LogFormat.HashLength).ToArray(), directoryOffset, directoryLength, bytes.Slice(88, LogFormat.HashLength).ToArray());
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Writing runs and checkpoints.

    /// <summary>
    /// Writes a run or a checkpoint: the twelve sections from their sources,
    /// then, for a checkpoint, the dictionary, then the directory and the
    /// header. Published atomically, or not at all (ADR 0071).
    /// </summary>
    internal static async ValueTask WriteRunAsync(
        IDerivedStore store,
        BlobName name,
        ushort kind,
        DatasetId dataset,
        long from,
        long to,
        byte[] toHash,
        IKeySource[] sections,
        CheckpointDictionary? dictionary,
        CancellationToken cancellationToken)
    {
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[WriteKeys * QuadKey.Size];
        long offset = 0;
        long[] offsets = new long[Sections];
        long[] counts = new long[Sections];
        List<QuadKey>[] fences = new List<QuadKey>[Sections];

        for (int section = 0; section < Sections; section++)
        {
            offsets[section] = offset;
            fences[section] = [];
            long count = 0;

            while (true)
            {
                Span<QuadKey> keys = MemoryMarshal.Cast<byte, QuadKey>(buffer.AsSpan());
                int produced = sections[section].Next(keys);

                if (produced == 0)
                {
                    break;
                }

                Fences(keys[..produced], count, fences[section]);
                await writer.WriteAsync(buffer.AsMemory(0, produced * QuadKey.Size), cancellationToken).ConfigureAwait(false);
                count += produced;
                offset += produced * (long)QuadKey.Size;
            }

            counts[section] = count;
        }

        long dictionaryOffset = offset;
        long dictionaryLength = 0;
        byte[] dictionaryHash = new byte[LogFormat.HashLength];

        if (dictionary is not null)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (ReadOnlyMemory<byte> chunk in dictionary.Chunks())
            {
                hash.AppendData(chunk.Span);
                await writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                dictionaryLength += chunk.Length;
            }

            dictionaryHash = hash.GetHashAndReset();
            offset += dictionaryLength;
        }

        ArrayBufferWriter<byte> directory = new();
        LogFormat.WriteUInt32(directory, KeySection.BlockKeys);
        LogFormat.WriteUInt32(directory, Sections);

        for (int section = 0; section < Sections; section++)
        {
            LogFormat.WriteUInt64(directory, (ulong)offsets[section]);
            LogFormat.WriteUInt64(directory, (ulong)counts[section]);
        }

        for (int section = 0; section < Sections; section++)
        {
            directory.Write(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(fences[section])));
        }

        if (dictionary is not null)
        {
            LogFormat.WriteUInt64(directory, (ulong)dictionary.CanonicalCount);
            LogFormat.WriteUInt64(directory, (ulong)dictionary.BlankCount);
            LogFormat.WriteUInt64(directory, (ulong)dictionaryOffset);
            LogFormat.WriteUInt64(directory, (ulong)dictionaryLength);
            directory.Write(dictionaryHash);
        }

        byte[] directoryBytes = directory.WrittenSpan.ToArray();
        await writer.WriteAsync(directoryBytes, cancellationToken).ConfigureAwait(false);
        byte[] header = EncodeHeader(kind, dataset, from, to, toHash, offset, directoryBytes.Length, SHA256.HashData(directoryBytes));
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    // Per block, not per key: one fence for every block the keys begin.
    private static void Fences(ReadOnlySpan<QuadKey> keys, long before, List<QuadKey> fences)
    {
        long first = before % KeySection.BlockKeys == 0 ? 0 : KeySection.BlockKeys - (before % KeySection.BlockKeys);

        for (long i = first; i < keys.Length; i += KeySection.BlockKeys)
        {
            fences.Add(keys[(int)i]);
        }
    }

    /// <summary>The twelve sources of a run in memory.</summary>
    internal static IKeySource[] SourcesOf(Run run)
    {
        IKeySource[] sources = new IKeySource[Sections];

        for (int order = 0; order < Orders.Count; order++)
        {
            sources[order * 2] = new SectionSource(run.Asserted((IndexOrder)order));
            sources[(order * 2) + 1] = new SectionSource(run.Retracted((IndexOrder)order));
        }

        return sources;
    }

    /// <summary>The twelve sources of two runs merged by ADR 0041's rule, streaming.</summary>
    internal static IKeySource[] MergeOf(Run older, Run newer, bool dropRetractions)
    {
        IKeySource[] sources = new IKeySource[Sections];

        for (int order = 0; order < Orders.Count; order++)
        {
            IndexOrder o = (IndexOrder)order;
            sources[order * 2] = new MergeSource(older.Asserted(o), older.Retracted(o), newer.Asserted(o), newer.Retracted(o), asserted: true);
            sources[(order * 2) + 1] = dropRetractions
                ? new SectionSource(KeySection.Empty)
                : new MergeSource(older.Asserted(o), older.Retracted(o), newer.Asserted(o), newer.Retracted(o), asserted: false);
        }

        return sources;
    }

    // ---------------------------------------------------------------------------------------------
    // Reading runs and checkpoints.

    /// <summary>
    /// A run or checkpoint over its blob, or null when the blob is not one this
    /// store can use. The blob stays open, held by the run's <see cref="RunBlob"/>.
    /// </summary>
    internal static async ValueTask<LoadedRun?> TryLoadAsync(IDerivedStore store, BlobName name, DatasetId dataset, ushort kind, CancellationToken cancellationToken)
    {
        IReadableBlob blob;

        try
        {
            blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        LoadedRun? loaded = null;

        try
        {
            loaded = TryLoad(blob, name, dataset, kind);
            return loaded;
        }
        finally
        {
            if (loaded is null)
            {
                blob.Dispose();
            }
        }
    }

    private static LoadedRun? TryLoad(IReadableBlob blob, BlobName name, DatasetId dataset, ushort kind)
    {
        if (!TryReadHeader(blob, dataset, kind, out DerivedHeader header))
        {
            return null;
        }

        byte[] directory = new byte[header.DirectoryLength];

        if (blob.Read(new ByteOffset(header.DirectoryOffset), directory) != directory.Length
            || !LogFormat.HashMatches(directory, header.DirectoryHash))
        {
            return null;
        }

        ReadOnlySpan<byte> span = directory;
        int fixedLength = 8 + (Sections * 16);

        if (span.Length < fixedLength
            || BinaryPrimitives.ReadUInt32LittleEndian(span) != KeySection.BlockKeys
            || BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) != Sections)
        {
            return null;
        }

        long[] offsets = new long[Sections];
        long[] counts = new long[Sections];
        int at = 8;
        long keysEnd = 0;

        for (int section = 0; section < Sections; section++)
        {
            offsets[section] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]);
            counts[section] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 8)..]);
            at += 16;

            if (offsets[section] != keysEnd || counts[section] < 0 || counts[section] > (header.DirectoryOffset - keysEnd) / QuadKey.Size)
            {
                return null;
            }

            keysEnd += counts[section] * QuadKey.Size;
        }

        RunBlob owner = new(name, blob);
        KeySection[] asserted = new KeySection[Orders.Count];
        KeySection[] retracted = new KeySection[Orders.Count];

        for (int section = 0; section < Sections; section++)
        {
            long blocks = (counts[section] + KeySection.BlockKeys - 1) / KeySection.BlockKeys;

            if (span.Length - at < blocks * QuadKey.Size)
            {
                return null;
            }

            QuadKey[] fences = MemoryMarshal.Cast<byte, QuadKey>(span.Slice(at, (int)blocks * QuadKey.Size)).ToArray();
            at += (int)blocks * QuadKey.Size;
            KeySection keys = KeySection.On(blob, offsets[section], counts[section], fences);

            if (section % 2 == 0)
            {
                asserted[section / 2] = keys;
            }
            else
            {
                retracted[section / 2] = keys;
            }
        }

        long canonical = 0, blank = 0, dictionaryOffset = 0, dictionaryLength = 0;
        byte[] dictionaryHash = [];

        if (kind == KindCheckpoint)
        {
            if (span.Length - at != 32 + LogFormat.HashLength)
            {
                return null;
            }

            canonical = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]);
            blank = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 8)..]);
            dictionaryOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 16)..]);
            dictionaryLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 24)..]);
            dictionaryHash = span.Slice(at + 32, LogFormat.HashLength).ToArray();

            if (dictionaryOffset != keysEnd || dictionaryOffset + dictionaryLength != header.DirectoryOffset || canonical < 0 || blank < 0)
            {
                return null;
            }
        }
        else if (span.Length != at || keysEnd != header.DirectoryOffset)
        {
            return null;
        }

        return new LoadedRun(new Run(asserted, retracted, header.From, header.To, owner), header, canonical, blank, dictionaryOffset, dictionaryLength, dictionaryHash);
    }

    /// <summary>A checkpoint's dictionary, read and checked against its hash; null when it does not verify.</summary>
    internal static Allocation[]? ReadDictionary(LoadedRun checkpoint)
    {
        if (checkpoint.DictionaryLength > int.MaxValue)
        {
            return null;
        }

        byte[] bytes = new byte[checkpoint.DictionaryLength];

        if (checkpoint.Run.Blob!.Blob.Read(new ByteOffset(checkpoint.DictionaryOffset), bytes) != bytes.Length
            || !LogFormat.HashMatches(bytes, checkpoint.DictionaryHash))
        {
            return null;
        }

        try
        {
            LogFormat.Reader reader = new(bytes, checkpoint.Header.To);
            Allocation[] entries = new Allocation[checkpoint.CanonicalCount];

            for (long counter = 1; counter <= checkpoint.CanonicalCount; counter++)
            {
                Allocation entry = LogFormat.ReadAllocation(ref reader);

                if (entry.Id != TermIds.Canonical(counter))
                {
                    return null;
                }

                entries[counter - 1] = entry;
            }

            reader.End();
            return entries;
        }
        catch (LogVerificationException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The projection's state.

    internal static async ValueTask WriteStateAsync(
        IDerivedStore store, BlobName name, DatasetId dataset, long position, byte[] positionHash, long sequence, IReadOnlyList<StateEntry> runs, CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte> body = new();
        LogFormat.WriteUInt64(body, (ulong)sequence);
        LogFormat.WriteUInt32(body, (uint)runs.Count);

        foreach (StateEntry run in runs)
        {
            byte[] runName = Encoding.UTF8.GetBytes(run.Name.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(body.GetSpan(2), (ushort)runName.Length);
            body.Advance(2);
            body.Write(runName);
            LogFormat.WriteUInt64(body, (ulong)run.From);
            LogFormat.WriteUInt64(body, (ulong)run.To);
        }

        byte[] directory = body.WrittenSpan.ToArray();
        byte[] header = EncodeHeader(KindState, dataset, 0, position, positionHash, 0, directory.Length, SHA256.HashData(directory));

        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(directory, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The projection's state, when there is one that verifies for this dataset.</summary>
    internal static async ValueTask<(DerivedHeader Header, long Sequence, StateEntry[] Runs)?> TryReadStateAsync(
        IDerivedStore store, BlobName name, DatasetId dataset, CancellationToken cancellationToken)
    {
        IReadableBlob blob;

        try
        {
            blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        using (blob)
        {
            if (!TryReadHeader(blob, dataset, KindState, out DerivedHeader header) || header.DirectoryOffset != 0)
            {
                return null;
            }

            byte[] directory = new byte[header.DirectoryLength];

            if (blob.Read(new ByteOffset(0), directory) != directory.Length || !LogFormat.HashMatches(directory, header.DirectoryHash))
            {
                return null;
            }

            try
            {
                LogFormat.Reader reader = new(directory, header.To);
                long sequence = (long)reader.UInt64();
                uint count = reader.UInt32();

                if (count > directory.Length / 18)
                {
                    return null;
                }

                StateEntry[] runs = new StateEntry[count];

                for (int i = 0; i < runs.Length; i++)
                {
                    ushort length = reader.UInt16();
                    string runName = Encoding.UTF8.GetString(reader.Fixed(length));
                    long from = (long)reader.UInt64();
                    long to = (long)reader.UInt64();

                    if (runName.Length == 0)
                    {
                        return null;
                    }

                    runs[i] = new StateEntry(new BlobName(runName), from, to);
                }

                reader.End();
                return (header, sequence, runs);
            }
            catch (LogVerificationException)
            {
                return null;
            }
        }
    }
}

/// <summary>Keys for one section of a run being written, produced in order.</summary>
internal interface IKeySource
{
    /// <summary>Fills the buffer with the next keys; returns how many, zero at the end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    int Next(Span<QuadKey> buffer);
}

/// <summary>A section's keys, a block at a time, in order.</summary>
internal sealed class SectionReader
{
    private readonly KeySection _section;
    private readonly QuadKey[]? _buffer;
    private ReadOnlyMemory<QuadKey> _block;
    private long _blockStart;

    internal SectionReader(KeySection section)
    {
        _section = section;
        _buffer = section.OnBlob ? new QuadKey[KeySection.BlockKeys] : null;
        _block = section.Memory;
        _blockStart = section.OnBlob ? long.MinValue / 2 : 0;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Next { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool HasCurrent => Next < _section.Count;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal QuadKey Current()
    {
        long at = Next - _blockStart;

        if (at < 0 || at >= _block.Length)
        {
            long block = Next / KeySection.BlockKeys;
            int count = _section.ReadBlock(block, _buffer);
            _block = new ReadOnlyMemory<QuadKey>(_buffer, 0, count);
            _blockStart = block * KeySection.BlockKeys;
            at = Next - _blockStart;
        }

        return _block.Span[(int)at];
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Advance() => Next++;
}

/// <summary>One section copied as it is.</summary>
internal sealed class SectionSource(KeySection section) : IKeySource
{
    private readonly SectionReader _reader = new(section);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<QuadKey> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length && _reader.HasCurrent)
        {
            buffer[produced++] = _reader.Current();
            _reader.Advance();
        }

        return produced;
    }
}

/// <summary>
/// Two runs' sections of one order merged by ADR 0041's rule, streaming: the
/// asserted keys or the retracted keys of the merged run.
/// </summary>
internal sealed class MergeSource(KeySection olderAsserted, KeySection olderRetracted, KeySection newerAsserted, KeySection newerRetracted, bool asserted) : IKeySource
{
    private readonly SectionReader _olderAsserted = new(olderAsserted);
    private readonly SectionReader _olderRetracted = new(olderRetracted);
    private readonly SectionReader _newerAsserted = new(newerAsserted);
    private readonly SectionReader _newerRetracted = new(newerRetracted);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<QuadKey> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length)
        {
            QuadKey min = default;
            bool any = false;
            Pick(_olderAsserted, ref min, ref any);
            Pick(_olderRetracted, ref min, ref any);
            Pick(_newerAsserted, ref min, ref any);
            Pick(_newerRetracted, ref min, ref any);

            if (!any)
            {
                break;
            }

            bool oa = Take(_olderAsserted, in min);
            bool or = Take(_olderRetracted, in min);
            bool na = Take(_newerAsserted, in min);
            bool nr = Take(_newerRetracted, in min);

            if (asserted ? RunMerge.Asserts(oa, or, na, nr) : !RunMerge.Asserts(oa, or, na, nr) && RunMerge.Retracts(oa, or, na, nr))
            {
                buffer[produced++] = min;
            }
        }

        return produced;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void Pick(SectionReader reader, ref QuadKey min, ref bool any)
    {
        if (reader.HasCurrent)
        {
            QuadKey key = reader.Current();

            if (!any || key.CompareTo(min) < 0)
            {
                min = key;
                any = true;
            }
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool Take(SectionReader reader, in QuadKey key)
    {
        if (reader.HasCurrent && reader.Current().Equals(key))
        {
            reader.Advance();
            return true;
        }

        return false;
    }
}

/// <summary>A checkpoint's dictionary: the term entries of every canonical id up to the counter.</summary>
internal sealed class CheckpointDictionary(TermDictionary dictionary, long canonicalCount, long blankCount)
{
    internal long CanonicalCount => canonicalCount;

    internal long BlankCount => blankCount;

    /// <summary>The entries, encoded as the log encodes them (storage format §4.4), in chunks.</summary>
    internal IEnumerable<ReadOnlyMemory<byte>> Chunks()
    {
        ArrayBufferWriter<byte> writer = new(1 << 16);

        for (long counter = 1; counter <= canonicalCount; counter++)
        {
            ulong id = TermIds.Canonical(counter);
            Allocation entry = dictionary.TryComponents(counter, out (ulong S, ulong P, ulong O) parts)
                ? new Allocation(id, null, parts.S, parts.P, parts.O)
                : new Allocation(id, dictionary.Term(id));
            LogFormat.WriteAllocation(writer, in entry);

            if (writer.WrittenCount >= 1 << 16)
            {
                yield return writer.WrittenSpan.ToArray();
                writer.ResetWrittenCount();
            }
        }

        if (writer.WrittenCount > 0)
        {
            yield return writer.WrittenSpan.ToArray();
        }
    }
}
