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
    internal LoadedRun(Run run, DerivedHeader header, long blankCount)
    {
        Run = run;
        Header = header;
        BlankCount = blankCount;
    }

    internal Run Run { get; }

    internal DerivedHeader Header { get; }

    /// <summary>The canonical counter at the run's last position: its term section's end.</summary>
    internal long CanonicalCount => Run.Terms.To;

    /// <summary>The blank counter at the run's last position.</summary>
    internal long BlankCount { get; }
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
    internal const ushort Version = 2;
    internal const ushort KindRun = 1;
    internal const ushort KindCheckpoint = 2;
    internal const ushort KindState = 3;
    internal const int HeaderLength = 160;

    private const int Sections = Orders.Count * 2;
    private const int WriteKeys = 2048;
    private const int TermBuffer = 1 << 16;
    private const int OffsetsRead = 512;

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
        TermSection[] terms,
        long blankCount,
        CancellationToken cancellationToken)
    {
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        QuadKey[] keys = new QuadKey[WriteKeys];
        KeyBlockWriter blocks = new();
        long offset = 0;
        long[] offsets = new long[Sections];
        long[] counts = new long[Sections];
        long[] lengths = new long[Sections];
        QuadKey[][] fences = new QuadKey[Sections][];
        long[][] starts = new long[Sections][];

        for (int section = 0; section < Sections; section++)
        {
            offsets[section] = offset;
            blocks.Begin();

            while (true)
            {
                int produced = sections[section].Next(keys);

                if (produced == 0)
                {
                    break;
                }

                blocks.Add(keys.AsSpan(0, produced));

                if (blocks.Pending >= KeyBlockWriter.FlushBytes)
                {
                    await writer.WriteAsync(blocks.PendingBytes, cancellationToken).ConfigureAwait(false);
                    blocks.Written();
                }
            }

            blocks.End();
            counts[section] = blocks.Count;
            lengths[section] = blocks.Length;
            fences[section] = [.. blocks.Fences];
            starts[section] = [.. blocks.Blocks];
            offset += blocks.Length;
            sections[section] = null!;
        }

        await writer.WriteAsync(blocks.PendingBytes, cancellationToken).ConfigureAwait(false);
        blocks.Written();

        (long canonicalFrom, long canonicalTo, long entriesAt, long entriesLength, long offsetsAt, long hashesAt, long end) =
            await WriteTermsAsync(writer, terms, offset, cancellationToken).ConfigureAwait(false);
        offset = end;

        ArrayBufferWriter<byte> directory = new();
        LogFormat.WriteUInt32(directory, KeySection.BlockKeys);
        LogFormat.WriteUInt32(directory, Sections);

        for (int section = 0; section < Sections; section++)
        {
            LogFormat.WriteUInt64(directory, (ulong)offsets[section]);
            LogFormat.WriteUInt64(directory, (ulong)counts[section]);
            LogFormat.WriteUInt64(directory, (ulong)lengths[section]);
        }

        for (int section = 0; section < Sections; section++)
        {
            directory.Write(MemoryMarshal.AsBytes(fences[section].AsSpan()));
        }

        for (int section = 0; section < Sections; section++)
        {
            directory.Write(MemoryMarshal.AsBytes(starts[section].AsSpan()));
        }

        LogFormat.WriteUInt64(directory, (ulong)canonicalFrom);
        LogFormat.WriteUInt64(directory, (ulong)canonicalTo);
        LogFormat.WriteUInt64(directory, (ulong)entriesAt);
        LogFormat.WriteUInt64(directory, (ulong)entriesLength);
        LogFormat.WriteUInt64(directory, (ulong)offsetsAt);
        LogFormat.WriteUInt64(directory, (ulong)hashesAt);
        LogFormat.WriteUInt64(directory, (ulong)blankCount);

        byte[] directoryBytes = directory.WrittenSpan.ToArray();
        await writer.WriteAsync(directoryBytes, cancellationToken).ConfigureAwait(false);
        byte[] header = EncodeHeader(kind, dataset, from, to, toHash, offset, directoryBytes.Length, SHA256.HashData(directoryBytes));
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the term sections of adjacent runs, oldest first, as one: their
    /// entries end to end, the offsets rebased, and the hash indexes merged.
    /// Streamed: a window of each index and a buffer at a time (ADR 0079).
    /// </summary>
    private static async ValueTask<(long From, long To, long EntriesAt, long EntriesLength, long OffsetsAt, long HashesAt, long End)> WriteTermsAsync(
        IBlobWriter writer, TermSection[] terms, long offset, CancellationToken cancellationToken)
    {
        long from = terms.Length == 0 ? 0 : terms[0].From;
        long to = from;

        foreach (TermSection section in terms)
        {
            if (section.From != to)
            {
                throw new InvalidOperationException("Term sections written together must be adjacent.");
            }

            to = section.To;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(TermBuffer);

        try
        {
            long entriesAt = offset;
            long entriesLength = 0;

            foreach (TermSection section in terms)
            {
                for (long at = 0; at < section.EntriesLength; at += TermBuffer)
                {
                    int length = (int)Math.Min(TermBuffer, section.EntriesLength - at);
                    section.ReadEntries(at, buffer.AsSpan(0, length));
                    await writer.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                }

                entriesLength += section.EntriesLength;
            }

            long offsetsAt = entriesAt + entriesLength;
            long rebase = 0;
            int filled = 0;

            long[] read = new long[OffsetsRead];

            foreach (TermSection section in terms)
            {
                for (long i = 0; i < section.Count; i += OffsetsRead)
                {
                    int count = (int)Math.Min(OffsetsRead, section.Count - i);
                    section.ReadOffsets(i, read.AsSpan(0, count));

                    for (int j = 0; j < count; j++)
                    {
                        if (filled == TermBuffer)
                        {
                            await writer.WriteAsync(buffer.AsMemory(0, filled), cancellationToken).ConfigureAwait(false);
                            filled = 0;
                        }

                        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled), (ulong)(rebase + read[j]));
                        filled += 8;
                    }
                }

                rebase += section.EntriesLength;
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled), (ulong)rebase);
            filled += 8;
            await writer.WriteAsync(buffer.AsMemory(0, filled), cancellationToken).ConfigureAwait(false);

            long hashesAt = offsetsAt + ((to - from + 1) * 8);
            HashMerge merge = new(terms);
            int produced;

            while ((produced = merge.Next(MemoryMarshal.Cast<byte, TermHash>(buffer.AsSpan(0, TermBuffer)))) > 0)
            {
                await writer.WriteAsync(buffer.AsMemory(0, produced * TermHash.Size), cancellationToken).ConfigureAwait(false);
            }

            return (from, to, entriesAt, entriesLength, offsetsAt, hashesAt, hashesAt + ((to - from) * TermHash.Size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }


    /// <summary>
    /// The twelve sources of any number of runs, oldest first, merged by ADR
    /// 0041's rule and streamed: each section is read a block at a time, so
    /// writing the merge holds one block per input section of the order being
    /// written and the writer's output buffer, never a run (issue #61).
    /// Merging into the oldest run drops retractions, because nothing older
    /// remains for them to cancel; that is how a checkpoint is written.
    /// </summary>
    internal static IKeySource[] MergeOf(Run[] runs, bool dropRetractions)
    {
        IKeySource[] sources = new IKeySource[Sections];

        for (int order = 0; order < Orders.Count; order++)
        {
            IndexOrder o = (IndexOrder)order;
            KeySection[] asserted = new KeySection[runs.Length];
            KeySection[] retracted = new KeySection[runs.Length];

            for (int i = 0; i < runs.Length; i++)
            {
                asserted[i] = runs[i].Asserted(o);
                retracted[i] = runs[i].Retracted(o);
            }

            sources[order * 2] = new MergeSource(asserted, retracted, wantAsserted: true);
            sources[(order * 2) + 1] = dropRetractions
                ? new SectionSource(KeySection.Empty)
                : new MergeSource(asserted, retracted, wantAsserted: false);
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
        int fixedLength = 8 + (Sections * 24);

        if (span.Length < fixedLength
            || BinaryPrimitives.ReadUInt32LittleEndian(span) != KeySection.BlockKeys
            || BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) != Sections)
        {
            return null;
        }

        long[] offsets = new long[Sections];
        long[] counts = new long[Sections];
        long[] lengths = new long[Sections];
        long[] blockCounts = new long[Sections];
        int at = 8;
        long keysEnd = 0;
        long blocksTotal = 0;

        for (int section = 0; section < Sections; section++)
        {
            offsets[section] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]);
            counts[section] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 8)..]);
            lengths[section] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 16)..]);
            at += 24;
            blockCounts[section] = (counts[section] + KeySection.BlockKeys - 1) / KeySection.BlockKeys;
            blocksTotal += blockCounts[section];

            // A key takes at least a byte, and a block at most KeyBlocks.MaxBytes.
            if (offsets[section] != keysEnd
                || counts[section] < 0
                || lengths[section] < 0
                || lengths[section] > header.DirectoryOffset - keysEnd
                || counts[section] > lengths[section]
                || lengths[section] > blockCounts[section] * KeyBlocks.MaxBytes)
            {
                return null;
            }

            keysEnd += lengths[section];
        }

        if ((span.Length - at) / (QuadKey.Size + 8) < blocksTotal)
        {
            return null;
        }

        QuadKey[][] fences = new QuadKey[Sections][];

        for (int section = 0; section < Sections; section++)
        {
            int bytes = (int)blockCounts[section] * QuadKey.Size;
            fences[section] = MemoryMarshal.Cast<byte, QuadKey>(span.Slice(at, bytes)).ToArray();
            at += bytes;
        }

        RunBlob owner = new(name, blob);
        KeySection[] asserted = new KeySection[Orders.Count];
        KeySection[] retracted = new KeySection[Orders.Count];

        for (int section = 0; section < Sections; section++)
        {
            long[] starts = new long[blockCounts[section] + 1];

            for (int b = 0; b < blockCounts[section]; b++)
            {
                starts[b] = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]);
                at += 8;

                if (starts[b] < (b == 0 ? 0 : starts[b - 1] + 1) || (b == 0 && starts[b] != 0))
                {
                    return null;
                }
            }

            starts[blockCounts[section]] = lengths[section];

            if (blockCounts[section] > 0 && starts[blockCounts[section] - 1] >= lengths[section])
            {
                return null;
            }

            KeySection keys = KeySection.On(blob, offsets[section], counts[section], fences[section], starts);

            if (section % 2 == 0)
            {
                asserted[section / 2] = keys;
            }
            else
            {
                retracted[section / 2] = keys;
            }
        }

        if (span.Length - at != 7 * 8)
        {
            return null;
        }

        long canonicalFrom = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]);
        long canonicalTo = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 8)..]);
        long entriesAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 16)..]);
        long entriesLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 24)..]);
        long offsetsAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 32)..]);
        long hashesAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 40)..]);
        long blank = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 48)..]);
        long terms = canonicalTo - canonicalFrom;

        if (canonicalFrom < 0 || terms < 0 || blank < 0 || entriesLength < 0
            || terms > header.DirectoryOffset / TermHash.Size
            || entriesAt != keysEnd
            || offsetsAt != entriesAt + entriesLength
            || hashesAt != offsetsAt + ((terms + 1) * 8)
            || hashesAt + (terms * TermHash.Size) != header.DirectoryOffset
            || (kind == KindCheckpoint && (canonicalFrom != 0 || header.From != 0)))
        {
            return null;
        }

        TermSection termSection = TermSection.On(blob, canonicalFrom, canonicalTo, entriesAt, entriesLength, offsetsAt, hashesAt);
        return new LoadedRun(new Run(asserted, retracted, header.From, header.To, termSection, owner), header, blank);
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
/// <remarks>
/// A section on a blob reads into a block buffer from the shared pool, taken
/// at the first read and returned by <see cref="Release"/>.
/// </remarks>
internal sealed class SectionReader
{
    private readonly KeySection _section;
    private QuadKey[]? _buffer;
    private ReadOnlyMemory<QuadKey> _block;
    private long _blockStart;

    internal SectionReader(KeySection section)
    {
        _section = section;
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
            _buffer ??= ArrayPool<QuadKey>.Shared.Rent(KeySection.BlockKeys);
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

    /// <summary>Returns the block buffer to the pool; a later read takes another.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Release()
    {
        if (_buffer is { } buffer)
        {
            _buffer = null;
            _block = _section.Memory;
            _blockStart = _section.OnBlob ? long.MinValue / 2 : 0;
            ArrayPool<QuadKey>.Shared.Return(buffer);
        }
    }
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

        if (produced == 0)
        {
            _reader.Release();
        }

        return produced;
    }
}

/// <summary>
/// Runs' sections of one order merged by ADR 0041's rule, streaming: the
/// asserted keys or the retracted keys of the merged run.
/// </summary>
/// <remarks>
/// For each key, the verdicts of the runs that mention it are folded oldest
/// first by <see cref="RunMerge"/>'s rule for two runs, which composes over a
/// chain of exact deltas (ADR 0047). A section's block buffers are taken at
/// its first read and returned when it ends, so a source of twelve holds
/// buffers only for the section being written.
/// </remarks>
internal sealed class MergeSource : IKeySource
{
    private readonly SectionReader[] _asserted;
    private readonly SectionReader[] _retracted;
    private readonly bool _wantAsserted;

    internal MergeSource(KeySection[] asserted, KeySection[] retracted, bool wantAsserted)
    {
        _asserted = new SectionReader[asserted.Length];
        _retracted = new SectionReader[retracted.Length];

        for (int i = 0; i < asserted.Length; i++)
        {
            _asserted[i] = new SectionReader(asserted[i]);
            _retracted[i] = new SectionReader(retracted[i]);
        }

        _wantAsserted = wantAsserted;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<QuadKey> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length)
        {
            QuadKey min = default;
            bool any = false;

            for (int i = 0; i < _asserted.Length; i++)
            {
                Pick(_asserted[i], ref min, ref any);
                Pick(_retracted[i], ref min, ref any);
            }

            if (!any)
            {
                break;
            }

            bool asserts = false;
            bool retracts = false;

            for (int i = 0; i < _asserted.Length; i++)
            {
                bool na = Take(_asserted[i], in min);
                bool nr = Take(_retracted[i], in min);

                if (na || nr)
                {
                    bool a = RunMerge.Asserts(asserts, retracts, na, nr);
                    retracts = !a && RunMerge.Retracts(asserts, retracts, na, nr);
                    asserts = a;
                }
            }

            if (_wantAsserted ? asserts : retracts)
            {
                buffer[produced++] = min;
            }
        }

        if (produced == 0)
        {
            for (int i = 0; i < _asserted.Length; i++)
            {
                _asserted[i].Release();
                _retracted[i].Release();
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

/// <summary>
/// The hash indexes of term sections merged into one, in (hash, id) order,
/// a window of each at a time.
/// </summary>
internal sealed class HashMerge
{
    private const int Window = 256;

    private readonly TermSection[] _sections;
    private readonly TermHash[][] _windows;
    private readonly long[] _next;
    private readonly int[] _at;
    private readonly int[] _filled;

    internal HashMerge(TermSection[] sections)
    {
        _sections = sections;
        _windows = new TermHash[sections.Length][];
        _next = new long[sections.Length];
        _at = new int[sections.Length];
        _filled = new int[sections.Length];

        for (int i = 0; i < sections.Length; i++)
        {
            _windows[i] = new TermHash[(int)Math.Min(Window, Math.Max(1, sections[i].Count))];
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int Next(Span<TermHash> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length)
        {
            int best = -1;

            for (int i = 0; i < _sections.Length; i++)
            {
                if (Refill(i) && (best < 0 || _windows[i][_at[i]].CompareTo(_windows[best][_at[best]]) < 0))
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            buffer[produced++] = _windows[best][_at[best]++];
        }

        return produced;
    }

    // Whether section i has a current entry, reading its next window if needed.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Refill(int i)
    {
        if (_at[i] < _filled[i])
        {
            return true;
        }

        long remaining = _sections[i].Count - _next[i];

        if (remaining <= 0)
        {
            return false;
        }

        int count = (int)Math.Min(_windows[i].Length, remaining);
        _sections[i].ReadHashes(_next[i], _windows[i].AsSpan(0, count));
        _next[i] += count;
        _at[i] = 0;
        _filled[i] = count;
        return true;
    }
}
