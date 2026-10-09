// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The commit of a bulk load, pass by pass (ADR 0081). Every pass reads its
/// inputs a buffer at a time; nothing it holds grows with the load.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>The operations, sorted, are merged with the pinned state's quads in
/// <c>SPOG</c> order (ADR 0076): the last operation on a quad decides, and it
/// is effective when it asserts an absent quad or retracts a present one. The
/// new terms the effective assertions reach are collected.</item>
/// <item>Those terms, sorted by reference — closed over triple terms'
/// components — are the allocations, and a term's final id is its rank:
/// references sort as the final ids do, so nothing is sorted again.</item>
/// <item>The merge runs again, and writes the effective delta with its final
/// ids, already in <c>SPOG</c> order.</item>
/// <item>The delta becomes a disk run, the other five orders sorted from it
/// one at a time, with a term section of the allocations; validators read it
/// there (ADR 0077); the commit's body is streamed from the same spills into
/// as many records as it takes.</item>
/// </list>
/// </remarks>
internal sealed class BulkCommit : IAsyncDisposable
{
    private const int ChunkEntries = 8_192;

    private readonly BulkLoad _load;
    private readonly SpillSpace _space;
    private readonly CancellationToken _cancellationToken;
    private readonly List<IDisposable> _open = [];
    private BlobName _allTerms;
    private BlobName _reached;
    private BlobName _asserted;
    private BlobName _retracted;
    private long _blank;
    private long _assertedCount;
    private long _retractedCount;
    private RankIndex? _ranks;
    private ExternalSort<QuadKey>? _order;

    internal BulkCommit(BulkLoad load, SpillSpace space, CancellationToken cancellationToken)
    {
        _load = load;
        _space = space;
        _cancellationToken = cancellationToken;
    }

    private Dataset Dataset => _load.Dataset;

    private long CanonicalBefore => _load.Terms.CanonicalCount;

    private long BlankBefore => _load.Terms.BlankCount;

    internal async ValueTask<CommitResult> RunAsync(BulkRef agent, BulkRef cause, BulkRef scope)
    {
        CancellationToken ct = _cancellationToken;

        // 1. The new terms, merged once into one spill read by every later pass.
        _allTerms = await MergeTermsAsync(ct).ConfigureAwait(false);

        // 2. Pass A: the terms the effective delta reaches.
        ExternalSort<BulkRef> reached = new(_space, "reached", (int)Math.Max(1024, _load.Memory / 8 / BulkRef.Size));

        foreach (BulkRef meta in new[] { agent, cause, scope })
        {
            if (meta.IsNew && reached.Add(meta))
            {
                await reached.SpillAsync(ct).ConfigureAwait(false);
            }
        }

        using (SortedReader<BulkQuad> operations = await _load.Quads.FinishAsync(ct).ConfigureAwait(false))
        {
            await EffectiveAsync(operations, reached, null, null, ct).ConfigureAwait(false);
        }

        _reached = await UniqueAsync(reached, ct).ConfigureAwait(false);

        if (_load.HasTriples)
        {
            await CloseOverTriplesAsync(ct).ConfigureAwait(false);
        }

        _ranks = await RankIndex.OpenAsync(_space.Store, _reached, _load.Memory / 2, ct).ConfigureAwait(false);
        NewCanonical = _ranks.Canonical;
        _blank = _ranks.Count - _ranks.Canonical;

        // 3. Pass B: the effective delta with final ids, in SPOG order.
        _asserted = _space.Next("asserted");
        _retracted = _space.Next("retracted");

        await using (RecordWriter<QuadKey> asserted = await RecordWriter<QuadKey>.CreateAsync(_space.Store, _asserted, ct).ConfigureAwait(false))
        await using (RecordWriter<QuadKey> retracted = await RecordWriter<QuadKey>.CreateAsync(_space.Store, _retracted, ct).ConfigureAwait(false))
        {
            using SortedReader<BulkQuad> operations = await _load.Quads.ReopenAsync(ct).ConfigureAwait(false);
            await EffectiveAsync(operations, null, asserted, retracted, ct).ConfigureAwait(false);
            await asserted.PublishAsync(ct).ConfigureAwait(false);
            await retracted.PublishAsync(ct).ConfigureAwait(false);
            _assertedCount = asserted.Written;
            _retractedCount = retracted.Written;
        }

        // The operations are read for the last time: their runs go.
        await _load.Quads.DropAsync(ct).ConfigureAwait(false);

        if (_assertedCount == 0 && _retractedCount == 0)
        {
            return CommitResult.NoChange(_load.Head);
        }

        // 4. The allocations' entries, their offsets and their hash index.
        TermSection terms = await WriteTermsAsync(ct).ConfigureAwait(false);

        // From here ranks are wanted only for triple terms' parts: the run's
        // orders are sorted in the memory the ranks held.
        _ranks.DropMemory();

        return await Dataset.CommitBulkAsync(this, terms, Final(agent), Final(cause), Final(scope), ct).ConfigureAwait(false);
    }

    internal long Head => _load.Head;

    internal IndexVersion Version => _load.Version;

    internal TermView Terms => _load.Terms;

    private long NewCanonical { get; set; }

    internal long CanonicalAfter => CanonicalBefore + NewCanonical;

    internal long BlankAfter => BlankBefore + _blank;

    // -----------------------------------------------------------------------------------------
    // The passes.

    private async ValueTask<BlobName> MergeTermsAsync(CancellationToken ct)
    {
        BlobName name = _space.Next("allterms");
        await using IBlobWriter writer = await _space.Store.CreateAsync(name, ct).ConfigureAwait(false);
        using TermMerge merge = await TermMerge.OpenAsync(_space.Store, _load.TermRuns, ct).ConfigureAwait(false);
        byte[] buffer = new byte[1 << 16];
        int filled = 0;

        while (merge.Next())
        {
            int length = BulkRef.Size + 4 + merge.KeyLength;

            if (filled + length > buffer.Length)
            {
                await writer.WriteAsync(buffer.AsMemory(0, filled), ct).ConfigureAwait(false);
                filled = 0;

                if (length > buffer.Length)
                {
                    buffer = new byte[length];
                }
            }

            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled), merge.Ref.Hi);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(filled + 8), merge.Ref.Lo);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(filled + 16), merge.KeyLength);
            merge.CurrentKey.CopyTo(buffer.AsSpan(filled + 20));
            filled += length;
        }

        await writer.WriteAsync(buffer.AsMemory(0, filled), ct).ConfigureAwait(false);
        await writer.PublishAsync(ct).ConfigureAwait(false);

        foreach (BlobName run in _load.TermRuns)
        {
            await _space.Store.DeleteAsync(run, ct).ConfigureAwait(false);
        }

        return name;
    }

    /// <summary>
    /// The sorted operations merged with the pinned state's quads in SPOG
    /// order. Pass A gives each new term an effective assertion reaches to
    /// <paramref name="reached"/>; pass B writes the effective delta with
    /// final ids.
    /// </summary>
    private async ValueTask EffectiveAsync(
        SortedReader<BulkQuad> operations, ExternalSort<BulkRef>? reached, RecordWriter<QuadKey>? asserted, RecordWriter<QuadKey>? retracted, CancellationToken ct)
    {
        using IQuadCursor state = _load.Version.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        bool stateLive = state.MoveNext();
        bool have = operations.TryNext(out BulkQuad last);
        QuadKey previous = default;
        bool any = false;

        while (have)
        {
            // The last operation on a quad decides.
            BulkQuad next = default;
            bool more;

            while ((more = operations.TryNext(out next)) && next.SameQuad(in last))
            {
                last = next;
            }

            bool present = false;

            if (last.AllExisting)
            {
                QuadKey key = new(last.Subject.Lo, last.Predicate.Lo, last.Object.Lo, last.Graph.Lo);

                while (stateLive && Orders.Key(IndexOrder.Spog, state.Current).CompareTo(key) < 0)
                {
                    stateLive = state.MoveNext();
                }

                present = stateLive && Orders.Key(IndexOrder.Spog, state.Current).Equals(key);
            }

            bool asserts = last.Asserts && !present;
            bool retracts = !last.Asserts && present;

            if (asserts && reached is not null)
            {
                for (int t = 0; t < 4; t++)
                {
                    BulkRef term = t switch { 0 => last.Subject, 1 => last.Predicate, 2 => last.Object, _ => last.Graph };

                    if (term.IsNew && reached.Add(term))
                    {
                        await reached.SpillAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            if ((asserts || retracts) && asserted is not null)
            {
                QuadKey key = new(Final(last.Subject), Final(last.Predicate), Final(last.Object), Final(last.Graph));

                // References sort as final ids do; this is that promise, checked.
                if (any && key.CompareTo(previous) <= 0 && asserts)
                {
                    throw new InvalidOperationException("A bulk load's delta is out of order after its ids were assigned.");
                }

                RecordWriter<QuadKey> target = asserts ? asserted : retracted!;

                if (target.Add(key))
                {
                    await target.WriteBufferAsync(ct).ConfigureAwait(false);
                }

                if (asserts)
                {
                    previous = key;
                    any = true;
                }
            }

            last = next;
            have = more;
        }
    }

    // A sort's records each once, written in order.
    private async ValueTask<BlobName> UniqueAsync(ExternalSort<BulkRef> sort, CancellationToken ct)
    {
        BlobName name = _space.Next("unique");
        await using RecordWriter<BulkRef> writer = await RecordWriter<BulkRef>.CreateAsync(_space.Store, name, ct).ConfigureAwait(false);
        using SortedReader<BulkRef> sorted = await sort.FinishAsync(ct).ConfigureAwait(false);
        bool first = true;
        BulkRef previous = default;

        while (sorted.TryNext(out BulkRef reference))
        {
            if (!first && reference.Equals(previous))
            {
                continue;
            }

            if (writer.Add(reference))
            {
                await writer.WriteBufferAsync(ct).ConfigureAwait(false);
            }

            previous = reference;
            first = false;
        }

        await writer.PublishAsync(ct).ConfigureAwait(false);
        sorted.Dispose();
        await sort.DropAsync(ct).ConfigureAwait(false);
        return name;
    }

    /// <summary>
    /// A triple term reached reaches its components (I3): passes over the new
    /// terms add the new components of the triple terms reached, until none
    /// is added — at most once per level of nesting.
    /// </summary>
    private async ValueTask CloseOverTriplesAsync(CancellationToken ct)
    {
        for (int pass = 0; pass <= BulkRef.MaxDepth; pass++)
        {
            ExternalSort<BulkRef> grown = new(_space, "reached", (int)Math.Max(1024, _load.Memory / 8 / BulkRef.Size));
            long before = 0;

            using (RecordReader<BulkRef> current = await RecordReader<BulkRef>.OpenAsync(_space.Store, _reached, ct).ConfigureAwait(false))
            {
                before = current.Count;

                while (current.TryNext(out BulkRef reference))
                {
                    if (grown.Add(reference))
                    {
                        await grown.SpillAsync(ct).ConfigureAwait(false);
                    }
                }
            }

            using (TermRunReader all = new(await _space.Store.OpenAsync(_allTerms, ct).ConfigureAwait(false)))
            using (RecordReader<BulkRef> current = await RecordReader<BulkRef>.OpenAsync(_space.Store, _reached, ct).ConfigureAwait(false))
            {
                bool live = current.TryNext(out BulkRef wanted);

                while (live && all.Next())
                {
                    while (live && wanted.CompareTo(all.Ref) < 0)
                    {
                        live = current.TryNext(out wanted);
                    }

                    if (!live || !wanted.Equals(all.Ref) || all.CurrentKey[0] != TermKey.Triple)
                    {
                        continue;
                    }

                    for (int c = 0; c < 3; c++)
                    {
                        BulkRef component = ReadRef(all.CurrentKey[(1 + (c * BulkRef.Size))..]);

                        if (component.IsNew && grown.Add(component))
                        {
                            await grown.SpillAsync(ct).ConfigureAwait(false);
                        }
                    }
                }
            }

            BlobName next = await UniqueAsync(grown, ct).ConfigureAwait(false);
            long after;

            using (RecordReader<BulkRef> counted = await RecordReader<BulkRef>.OpenAsync(_space.Store, next, ct).ConfigureAwait(false))
            {
                after = counted.Count;
            }

            await _space.DeleteAsync(_reached, ct).ConfigureAwait(false);
            _reached = next;

            if (after == before)
            {
                return;
            }
        }
    }

    private static BulkRef ReadRef(ReadOnlySpan<byte> bytes) =>
        new(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));

    /// <summary>A reference's final id: an existing one's own, a new one's by its rank.</summary>
    internal ulong Final(BulkRef reference)
    {
        if (!reference.IsNew)
        {
            return reference.Lo;
        }

        long rank = _ranks!.Rank(reference);

        return rank < _ranks.Canonical
            ? TermIds.Canonical(CanonicalBefore + 1 + rank)
            : TermIds.Blank(BlankBefore + 1 + (rank - _ranks.Canonical));
    }

    /// <summary>
    /// The term section of the allocations, as one blob laid out as a run's
    /// (storage format §7): the canonical entries in id order with their
    /// final ids, their offsets, and the hash index, sorted outside memory.
    /// </summary>
    private async ValueTask<TermSection> WriteTermsAsync(CancellationToken ct)
    {
        BlobName termsBlob = _space.Next("termsection");
        BlobName offsetsSpill = _space.Next("offsets");
        ExternalSort<TermHash> hashes = new(_space, "hashes", (int)Math.Max(1024, _load.Memory / 8 / TermHash.Size));
        long entriesLength = 0;
        long count = 0;

        await using IBlobWriter writer = await _space.Store.CreateAsync(termsBlob, ct).ConfigureAwait(false);

        await using (RecordWriter<long> offsets = await RecordWriter<long>.CreateAsync(_space.Store, offsetsSpill, ct).ConfigureAwait(false))
        {
            ArrayBufferWriter<byte> buffer = new(1 << 17);

            await foreach (ReadOnlyMemory<byte> entry in CanonicalEntriesAsync(ct).ConfigureAwait(false))
            {
                if (offsets.Add(entriesLength))
                {
                    await offsets.WriteBufferAsync(ct).ConfigureAwait(false);
                }

                byte[] key = TermKey.OfEntry(entry.Span).ToArray();
                TermKey.LowerLanguage(key);

                if (hashes.Add(new TermHash(TermKey.Hash(key), TermIds.Canonical(CanonicalBefore + 1 + count))))
                {
                    await hashes.SpillAsync(ct).ConfigureAwait(false);
                }

                buffer.Write(entry.Span);
                entriesLength += entry.Length;
                count++;

                if (buffer.WrittenCount >= 1 << 16)
                {
                    await writer.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
                    buffer.ResetWrittenCount();
                }
            }

            if (offsets.Add(entriesLength))
            {
                await offsets.WriteBufferAsync(ct).ConfigureAwait(false);
            }

            await writer.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);
            await offsets.PublishAsync(ct).ConfigureAwait(false);
        }

        using (RecordReader<long> offsets = await RecordReader<long>.OpenAsync(_space.Store, offsetsSpill, ct).ConfigureAwait(false))
        {
            await CopyAsync(offsets, writer, ct).ConfigureAwait(false);
        }

        await _space.DeleteAsync(offsetsSpill, ct).ConfigureAwait(false);

        using (SortedReader<TermHash> sorted = await hashes.FinishAsync(ct).ConfigureAwait(false))
        {
            TermHash[] chunk = new TermHash[4096];
            int filled;

            while ((filled = sorted.Read(chunk)) > 0)
            {
                await writer.WriteAsync(MemoryMarshal.AsBytes(chunk.AsSpan(0, filled)).ToArray(), ct).ConfigureAwait(false);
            }
        }

        await hashes.DropAsync(ct).ConfigureAwait(false);
        await writer.PublishAsync(ct).ConfigureAwait(false);
        IReadableBlob blob = await _space.Store.OpenAsync(termsBlob, ct).ConfigureAwait(false);
        _open.Add(blob);
        long offsetsAt = entriesLength;
        long hashesAt = offsetsAt + ((count + 1) * 8);
        // The load's own term section, read once while its run is written;
        // the run gets its filter from the writer (ADR 0109).
        return TermSection.On(blob, CanonicalBefore, CanonicalBefore + count, 0, entriesLength, offsetsAt, hashesAt, null);
    }

    private static async ValueTask CopyAsync(RecordReader<long> records, IBlobWriter writer, CancellationToken ct)
    {
        long[] chunk = new long[8192];

        while (true)
        {
            int filled = 0;

            while (filled < chunk.Length && records.TryNext(out long value))
            {
                chunk[filled++] = value;
            }

            if (filled == 0)
            {
                return;
            }

            await writer.WriteAsync(MemoryMarshal.AsBytes(chunk.AsSpan(0, filled)).ToArray(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The canonical allocations' log entries (storage format §4.4), in id
    /// order: the new terms the delta reaches, with a triple term's
    /// components by their final ids.
    /// </summary>
    private async IAsyncEnumerable<ReadOnlyMemory<byte>> CanonicalEntriesAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using TermRunReader all = new(await _space.Store.OpenAsync(_allTerms, ct).ConfigureAwait(false));
        using RecordReader<BulkRef> reached = await RecordReader<BulkRef>.OpenAsync(_space.Store, _reached, ct).ConfigureAwait(false);
        byte[] entry = new byte[256];
        long rank = 0;

        while (reached.TryNext(out BulkRef wanted) && wanted.Segment != BulkRef.NewBlank)
        {
            while (true)
            {
                if (!all.Next())
                {
                    throw new InvalidOperationException("A term the delta reaches is not among the load's new terms.");
                }

                if (all.Ref.Equals(wanted))
                {
                    break;
                }
            }

            ReadOnlySpan<byte> key = all.CurrentKey;

            if (entry.Length < key.Length + (4 * LogFormat.MaxUlebLength))
            {
                entry = new byte[(key.Length + (4 * LogFormat.MaxUlebLength)) * 2];
            }

            int at = LogFormat.Uleb(entry, TermIds.Canonical(CanonicalBefore + 1 + rank));

            if (key[0] == TermKey.Triple)
            {
                entry[at++] = TermKey.Triple;

                for (int c = 0; c < 3; c++)
                {
                    at += LogFormat.Uleb(entry.AsSpan(at), Final(ReadRef(key[(1 + (c * BulkRef.Size))..])));
                }
            }
            else
            {
                key.CopyTo(entry.AsSpan(at));
                at += key.Length;
            }

            rank++;
            yield return entry.AsMemory(0, at);
        }
    }

    /// <summary>
    /// The commit's body (storage format §4.3), produced twice — once for its
    /// content hash, once into the log: the allocations, then the asserted and
    /// the retracted quads, in chunks with their own counts.
    /// </summary>
    internal async IAsyncEnumerable<ReadOnlyMemory<byte>> BodyAsync([EnumeratorCancellation] CancellationToken ct)
    {
        ArrayBufferWriter<byte> chunk = new(1 << 17);
        ArrayBufferWriter<byte> entries = new(1 << 17);
        int count = 0;

        await foreach (ReadOnlyMemory<byte> entry in CanonicalEntriesAsync(ct).ConfigureAwait(false))
        {
            entries.Write(entry.Span);

            if (++count == ChunkEntries)
            {
                yield return Chunk(chunk, LogFormat.ChunkAllocations, count, entries);
                count = 0;
            }
        }

        for (long b = 0; b < _blank; b++)
        {
            LogFormat.WriteUleb(entries, TermIds.Blank(BlankBefore + 1 + b));
            LogFormat.WriteByte(entries, 1);

            if (++count == ChunkEntries)
            {
                yield return Chunk(chunk, LogFormat.ChunkAllocations, count, entries);
                count = 0;
            }
        }

        if (count > 0)
        {
            yield return Chunk(chunk, LogFormat.ChunkAllocations, count, entries);
            count = 0;
        }

        foreach ((BlobName name, byte tag) in new[] { (_asserted, LogFormat.ChunkAsserted), (_retracted, LogFormat.ChunkRetracted) })
        {
            using RecordReader<QuadKey> keys = await RecordReader<QuadKey>.OpenAsync(_space.Store, name, ct).ConfigureAwait(false);

            while (keys.TryNext(out QuadKey key))
            {
                Span<byte> span = entries.GetSpan(4 * LogFormat.MaxUlebLength);
                int at = LogFormat.Uleb(span, key.K0);
                at += LogFormat.Uleb(span[at..], key.K1);
                at += LogFormat.Uleb(span[at..], key.K2);
                at += LogFormat.Uleb(span[at..], key.K3);
                entries.Advance(at);

                if (++count == ChunkEntries)
                {
                    yield return Chunk(chunk, tag, count, entries);
                    count = 0;
                }
            }

            if (count > 0)
            {
                yield return Chunk(chunk, tag, count, entries);
                count = 0;
            }
        }
    }

    private static ReadOnlyMemory<byte> Chunk(ArrayBufferWriter<byte> chunk, byte tag, int count, ArrayBufferWriter<byte> entries)
    {
        chunk.ResetWrittenCount();
        LogFormat.WriteByte(chunk, tag);
        LogFormat.WriteUleb(chunk, (ulong)count);
        chunk.Write(entries.WrittenSpan);
        entries.ResetWrittenCount();
        return chunk.WrittenMemory;
    }

    /// <summary>The content hash of the body: SHA-256 over it, streamed.</summary>
    internal async ValueTask<byte[]> ContentHashAsync(CancellationToken ct)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await foreach (ReadOnlyMemory<byte> part in BodyAsync(ct).ConfigureAwait(false))
        {
            hash.AppendData(part.Span);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Section <paramref name="index"/> of the delta run: SPOG from the
    /// spills, the other orders each sorted from them when its turn comes.
    /// </summary>
    internal async ValueTask<IKeySource> SectionAsync(int index, CancellationToken ct)
    {
        // The section before has been written: its sort's runs go.
        if (_order is { } previous)
        {
            _order = null;
            await previous.DropAsync(ct).ConfigureAwait(false);
        }

        IndexOrder order = (IndexOrder)(index / 2);
        BlobName spill = index % 2 == 0 ? _asserted : _retracted;

        if (order == IndexOrder.Spog)
        {
            return new SpillKeySource(await RecordReader<QuadKey>.OpenAsync(_space.Store, spill, ct).ConfigureAwait(false));
        }

        ExternalSort<QuadKey> sort = new(_space, "order", (int)Math.Max(1024, _load.Memory / 4 / QuadKey.Size));

        using (RecordReader<QuadKey> keys = await RecordReader<QuadKey>.OpenAsync(_space.Store, spill, ct).ConfigureAwait(false))
        {
            while (keys.TryNext(out QuadKey key))
            {
                Quad quad = Orders.Quad(IndexOrder.Spog, in key);

                if (sort.Add(Orders.Key(order, in quad)))
                {
                    await sort.SpillAsync(ct).ConfigureAwait(false);
                }
            }
        }

        _order = sort;
        return new SpillKeySource(await sort.FinishAsync(ct).ConfigureAwait(false));
    }

    /// <summary>After the run is written: the last order's sort goes.</summary>
    internal async ValueTask DropOrderAsync(CancellationToken ct)
    {
        if (_order is { } last)
        {
            _order = null;
            await last.DropAsync(ct).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (IDisposable open in _open)
        {
            open.Dispose();
        }

        _ranks?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The new terms a delta reaches, sorted by reference, and a term's rank
/// among them: a search by interpolation within its segment, where the
/// references are hashes and spread evenly (ADR 0081), behind a fixed cache.
/// </summary>
internal sealed class RankIndex : IDisposable
{
    private const int Window = 64;
    private const int CacheSlots = 1 << 16;

    private readonly IReadableBlob _blob;
    private readonly (BulkRef Ref, long Rank)[] _cache = new (BulkRef, long)[CacheSlots];
    private BulkRef[]? _memory;

    private RankIndex(IReadableBlob blob, long memoryBytes)
    {
        _blob = blob;
        Count = blob.Length.Value / BulkRef.Size;

        // Within the load's memory, the references are read in once and
        // searched in place; beyond it, each search reads windows of the blob.
        if (Count * BulkRef.Size <= memoryBytes && Count <= int.MaxValue / 2)
        {
            _memory = new BulkRef[Count];
            ReadWindow(0, _memory);
        }

        Canonical = Count == 0 || Read(Count - 1).Segment != BulkRef.NewBlank ? Count : FirstAtOrAbove(new BulkRef((ulong)BulkRef.NewBlank << 56, 0));
    }

    internal long Count { get; }

    /// <summary>How many of them are canonical; the rest are blank nodes.</summary>
    internal long Canonical { get; }

    /// <summary>Gives back the memory: later searches read the spill.</summary>
    internal void DropMemory() => _memory = null;

    internal static async ValueTask<RankIndex> OpenAsync(IDerivedStore store, BlobName name, long memoryBytes, CancellationToken cancellationToken) =>
        new(await store.OpenAsync(name, cancellationToken).ConfigureAwait(false), memoryBytes);

    /// <summary>The rank of a reference the delta reaches.</summary>
    internal long Rank(BulkRef reference)
    {
        int slot = (int)(reference.Lo & (CacheSlots - 1));

        if (_cache[slot].Ref.Equals(reference) && _cache[slot].Rank >= 0 && !reference.Equals(default))
        {
            return _cache[slot].Rank;
        }

        long rank = _memory is not null ? Array.BinarySearch(_memory, reference) : FirstAtOrAbove(reference);

        if (_memory is not null)
        {
            return rank >= 0 ? rank : throw new InvalidOperationException("A new term of the delta was not collected as reached.");
        }


        if (rank >= Count || !Read(rank).Equals(reference))
        {
            throw new InvalidOperationException("A new term of the delta was not collected as reached.");
        }

        _cache[slot] = (reference, rank);
        return rank;
    }

    // The first index at or above the reference: interpolation on its hash
    // within the bounds while it lands, then halving.
    private long FirstAtOrAbove(BulkRef reference)
    {
        long low = 0;
        long high = Count;
        Span<BulkRef> window = stackalloc BulkRef[Window];
        int guesses = 0;

        while (high - low > Window)
        {
            long start;

            if (guesses++ < 6)
            {
                BulkRef first = Read(low);
                BulkRef last = Read(high - 1);
                double fraction = first.Segment == last.Segment && last.Hi > first.Hi && reference.Segment == first.Segment
                    ? (reference.Hi - first.Hi) / (double)(last.Hi - first.Hi)
                    : 0.5;
                start = low + (long)(Math.Clamp(fraction, 0, 1) * (high - low)) - (Window / 2);
            }
            else
            {
                start = low + ((high - low) / 2) - (Window / 2);
            }

            start = Math.Clamp(start, low, high - Window);
            ReadWindow(start, window);

            if (window[0].CompareTo(reference) >= 0)
            {
                high = start;
            }
            else if (window[Window - 1].CompareTo(reference) < 0)
            {
                low = start + Window;
            }
            else
            {
                for (int i = 1; i < Window; i++)
                {
                    if (window[i].CompareTo(reference) >= 0)
                    {
                        return start + i;
                    }
                }
            }
        }

        int count = (int)(high - low);
        ReadWindow(low, window[..count]);

        for (int i = 0; i < count; i++)
        {
            if (window[i].CompareTo(reference) >= 0)
            {
                return low + i;
            }
        }

        return high;
    }

    private BulkRef Read(long index)
    {
        Span<BulkRef> one = stackalloc BulkRef[1];
        ReadWindow(index, one);
        return one[0];
    }

    private void ReadWindow(long index, Span<BulkRef> destination)
    {
        if (_memory is not null && destination.Length < _memory.Length)
        {
            _memory.AsSpan((int)index, destination.Length).CopyTo(destination);
            return;
        }

        Span<byte> bytes = MemoryMarshal.AsBytes(destination);

        if (_blob.Read(new ByteOffset(index * BulkRef.Size), bytes) != bytes.Length)
        {
            throw new IOException("A bulk load's reached terms are shorter than were written.");
        }
    }

    public void Dispose() => _blob.Dispose();
}
