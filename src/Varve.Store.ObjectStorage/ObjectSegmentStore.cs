// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The log as chunk objects (ADR 0135): appends are held in memory, a flush
/// creates one chunk, a seal creates a chunk marked so and consolidates the
/// segment into one object, and maintenance consolidates one a seal could not.
/// </summary>
/// <remarks>
/// <para>
/// **Every create is the fence's test** (ADR 0136). A chunk's name is created
/// at most once, so a create that finds its slot taken by bytes this writer did
/// not write means a newer epoch holds the log: the writer stops, and throws
/// <see cref="WriterSupersededException"/> from that flush and every write
/// after it.
/// </para>
/// <para>
/// A segment whose only chunks are fences holds no bytes and was never opened
/// by <see cref="CreateSegmentAsync"/>: it is not listed, so the store sees
/// what it would after a crash just before opening a segment. The slots it
/// holds still count for the fence.
/// </para>
/// <para>
/// **Opening lists names and reads no chunk** but the newest, which the
/// fence needs (ADR 0139). The dataset says where the catalog's cursor puts
/// the log's continuation with <see cref="ReadFromAsync"/>; the first use of
/// the log then reads the cursor's segment from its slot, and every other
/// segment not yet consolidated whole. A read before a segment's first held
/// chunk reads the chunks below it then, and checks that they chain into it.
/// </para>
/// </remarks>
internal sealed class ObjectSegmentStore : ISegmentStore, ISegmentCursor, IDisposable
{
    // Requests in flight at once when reading or deleting many chunks.
    private const int InFlight = 16;

    private readonly IObjectStore _store;
    private readonly ObjectNames _names;
    private readonly TimeProvider _clock;
    private readonly string _cache;
    private long _epoch;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly SemaphoreSlim _downloading = new(1, 1);
    private readonly SemaphoreSlim _consolidating = new(1, 1);
    private readonly List<Segment> _segments;
    private readonly Dictionary<int, SafeFileHandle> _copies = [];
    private ReadOnlyMemory<byte> _manifest;
    private bool _superseded;
    private bool _ready;
    private (int Segment, long Slot)? _start;

    /// <summary>Whether the segment copies still work (#146); the storage shares it with its derived store.</summary>
    internal LocalCacheHealth Cache { get; set; } = new();

    private ObjectSegmentStore(IObjectStore store, ObjectNames names, TimeProvider clock, string cache, long epoch, List<Segment> segments, ReadOnlyMemory<byte> manifest)
    {
        _store = store;
        _names = names;
        _clock = clock;
        _cache = cache;
        _epoch = epoch;
        _segments = segments;
        _manifest = manifest;
    }

    /// <summary>Whether this store writes; a read-only one refuses every write.</summary>
    public bool Writes => _epoch > 0;

    /// <summary>Whether a newer epoch has taken the log from this writer.</summary>
    public bool Superseded
    {
        get
        {
            lock (_lock)
            {
                return _superseded;
            }
        }
    }

    /// <summary>The slot this writer's fence took, once it has one.</summary>
    public LogSlot? Fence { get; private set; }

    public Durability Durability => Durability.Replicated;

    /// <summary>Whether a sealed segment waits to be consolidated.</summary>
    public bool ConsolidationDue
    {
        get
        {
            lock (_lock)
            {
                return Writes && !_superseded && _segments.Exists(static segment => (segment.Sealed && !segment.Consolidated) || segment.PendingDeletes.Length > 0);
            }
        }
    }

    /// <summary>
    /// Lists and reads the log as it stands. For a writer, <paramref name="epoch"/>
    /// is the epoch it has claimed, and the open fences the log with it.
    /// </summary>
    public static async ValueTask<ObjectSegmentStore> OpenAsync(
        IObjectStore store, ObjectNames names, TimeProvider clock, string cache, long epoch, CancellationToken cancellationToken)
    {
        IReadOnlyList<ObjectEntry> entries = await store.ListAsync(names.Log, cancellationToken).ConfigureAwait(false);
        SortedDictionary<int, (ObjectEntry? Consolidated, List<(int Chunk, ObjectEntry Entry)> Chunks)> found = [];
        bool hasManifest = false;

        foreach (ObjectEntry entry in entries)
        {
            if (entry.Name == names.Manifest)
            {
                hasManifest = true;
                continue;
            }

            if (!names.TryParse(entry.Name, out int segment, out int chunk))
            {
                continue;
            }

            if (!found.TryGetValue(segment, out var parts))
            {
                parts = (null, []);
            }

            found[segment] = chunk < 0 ? (entry, parts.Chunks) : (parts.Consolidated, AddTo(parts.Chunks, (chunk, entry)));
        }

        List<Segment> segments = [];

        foreach ((int id, var parts) in found)
        {
            Segment segment = new(id);

            if (parts.Consolidated is { } consolidated)
            {
                segment.Consolidated = true;
                segment.Sealed = true;
                segment.Opened = true;
                segment.ConsolidatedLength = consolidated.Length.Value;
            }
            else
            {
                parts.Chunks.Sort(static (left, right) => left.Chunk.CompareTo(right.Chunk));

                for (int i = 0; i < parts.Chunks.Count; i++)
                {
                    if (parts.Chunks[i].Chunk != i)
                    {
                        throw new LogVerificationException("Segment " + id + " has chunk " + parts.Chunks[i].Chunk + " where chunk " + i + " was due: a chunk of an open segment is missing.");
                    }
                }

                segment.Listed = parts.Chunks.Count;
            }

            segments.Add(segment);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Id != i)
            {
                throw new LogVerificationException("Segment " + segments[i].Id + " is in the log where segment " + i + " was due.");
            }
        }

        ReadOnlyMemory<byte> manifest = hasManifest
            ? (await ConditionalWrites.ReadWholeAsync(store, names.Manifest, cancellationToken).ConfigureAwait(false))?.Bytes ?? ReadOnlyMemory<byte>.Empty
            : ReadOnlyMemory<byte>.Empty;

        ObjectSegmentStore opened = new(store, names, clock, cache, epoch, segments, manifest);

        if (epoch > 0)
        {
            // The fence needs the newest chunk alone: where the log ends and its hash.
            if (segments.Count > 0 && !segments[^1].Consolidated)
            {
                await opened.LoadFromAsync(segments[^1], segments[^1].Listed - 1, cancellationToken).ConfigureAwait(false);
            }

            await opened.FenceAsync(cancellationToken).ConfigureAwait(false);
        }

        return opened;

        static List<(int, ObjectEntry)> AddTo(List<(int, ObjectEntry)> list, (int, ObjectEntry) item)
        {
            list.Add(item);
            return list;
        }
    }

    public async ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            List<SegmentInfo> list = [];
            int visible = Visible();

            for (int i = 0; i < visible; i++)
            {
                Segment segment = _segments[i];
                SegmentId id = new(segment.Id);
                ByteCount length = new(segment.Length);
                list.Add(segment.Sealed || i < visible - 1 ? SegmentInfo.Sealed(id, length) : SegmentInfo.Open(id, length));
            }

            return list;
        }
    }

    /// <inheritdoc />
    public SegmentSlot SlotOf(SegmentId segment, ByteOffset offset)
    {
        lock (_lock)
        {
            foreach (Segment candidate in _segments)
            {
                if (candidate.Id == segment.Value)
                {
                    return new SegmentSlot(candidate.Consolidated ? 0 : candidate.SlotOf(offset.Value));
                }
            }

            return new SegmentSlot(0);
        }
    }

    /// <inheritdoc />
    public ValueTask ReadFromAsync(SegmentId segment, SegmentSlot slot, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_ready)
            {
                _start = (segment.Value, slot.Value);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Segment? next;

            lock (_lock)
            {
                RefuseWrites();
                int visible = Visible();

                if (visible > 0 && !_segments[visible - 1].Sealed)
                {
                    throw new InvalidOperationException("The newest segment is not sealed; only one segment may be open.");
                }

                // A segment that holds only fences is already this writer's to open.
                next = visible < _segments.Count ? _segments[visible] : null;

                if (next is not null)
                {
                    next.Opened = true;
                    return new SegmentId(next.Id);
                }

                next = new Segment(_segments.Count == 0 ? 0 : _segments[^1].Id + 1);
            }

            byte[] chunk = ChunkHeader.Compose(ChunkFlags.Opens, _epoch, next.Id, 0, 0, [], ChunkHeader.Nothing(), out byte[] hash);
            await CreateChunkAsync(next.Id, 0, chunk, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                next.Opened = true;
                next.LoadedFrom = 0;
                next.Recorded(0, hash, ChunkFlags.Opens, _epoch, 0);
                _segments.Add(next);
                return new SegmentId(next.Id);
            }
        }
        finally
        {
            _writing.Release();
        }
    }

    public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            RefuseWrites();
            Segment target = Get(segment.Value);

            if (target.Sealed || target != _segments[Visible() - 1])
            {
                throw new InvalidOperationException("Segment " + segment.Value + " is sealed and never changes again.");
            }

            target.Bytes!.Write(bytes.Span);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) =>
        WriteChunkAsync(segment.Value, seal: false, cancellationToken);

    public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) =>
        WriteChunkAsync(segment.Value, seal: true, cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Segment source;
        bool below;

        lock (_lock)
        {
            source = Get(segment.Value);
            below = !source.Consolidated && offset.Value < source.Base;
        }

        if (below)
        {
            // Before the first chunk held: the chunks below it, read now.
            await ExtendDownAsync(source, 0, cancellationToken).ConfigureAwait(false);
        }

        lock (_lock)
        {
            if (!source.Consolidated)
            {
                // Appended and not yet flushed included, as on every backend.
                ReadOnlySpan<byte> held = source.Bytes!.WrittenSpan;
                long start = Math.Min(offset.Value - source.Base, held.Length);
                int count = (int)Math.Min(held.Length - start, Math.Min(length.Value, int.MaxValue));
                return held.Slice((int)start, count).ToArray();
            }
        }

        long end = Math.Min(source.ConsolidatedLength, offset.Value + Math.Min(length.Value, int.MaxValue));

        if (offset.Value >= end)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[] bytes = new byte[end - offset.Value];

        if (await CopyAsync(source, cancellationToken).ConfigureAwait(false) is { } copy)
        {
            try
            {
                int filled = 0;

                while (filled < bytes.Length)
                {
                    int read = RandomAccess.Read(copy, bytes.AsSpan(filled), offset.Value + filled);

                    if (read == 0)
                    {
                        throw new IOException("The cached copy of segment " + source.Id + " is shorter than the segment.");
                    }

                    filled += read;
                }

                return bytes;
            }
            catch (Exception error) when (LocalCacheHealth.IsLocal(error))
            {
                // A copy that cannot be read is a miss (#146): the store has the segment.
                Cache.TurnOff(LocalCache.SegmentCopies);
            }
        }

        await ReadFromStoreAsync(source, offset.Value, bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    // A consolidated segment's bytes from the store, by ranged reads: with
    // the segment copies off (#146), every read of one.
    private async ValueTask ReadFromStoreAsync(Segment segment, long offset, byte[] bytes, CancellationToken cancellationToken)
    {
        int filled = 0;

        while (filled < bytes.Length)
        {
            ObjectRead read = await _store.ReadAsync(_names.Segment(segment.Id), new ByteOffset(offset + filled), new ByteCount(bytes.Length - filled), cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("Segment " + segment.Id + " was listed as consolidated and is gone from the store.");

            if (read.Bytes.IsEmpty)
            {
                throw new IOException("Segment " + segment.Id + " in the store is shorter than it was listed.");
            }

            read.Bytes.Span.CopyTo(bytes.AsSpan(filled));
            filled += read.Bytes.Length;
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => new(_manifest);

    public async ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_lock)
            {
                RefuseWrites();

                if (Visible() > 0)
                {
                    throw new InvalidOperationException("The manifest is written before any segment.");
                }

                if (!_manifest.IsEmpty)
                {
                    throw new InvalidOperationException("The log already has a manifest; it is written once.");
                }
            }

            byte[] bytes = manifest.ToArray();
            (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, _names.Manifest, bytes, _clock, cancellationToken).ConfigureAwait(false);

            if (outcome == CreateOutcome.Taken)
            {
                throw new InvalidOperationException("The log already has a manifest; it is written once.");
            }

            _manifest = bytes;
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>
    /// One step of the log's own maintenance: deletes the chunks of a segment
    /// a seal consolidated, but its fences; or consolidates the oldest sealed
    /// segment a seal could not, and deletes its chunks (ADR 0135). True when
    /// it did something.
    /// </summary>
    public async ValueTask<bool> ConsolidateAsync(CancellationToken cancellationToken)
    {
        Segment? pending;
        Segment? sealedOnly;

        lock (_lock)
        {
            if (!Writes || _superseded)
            {
                return false;
            }

            pending = _segments.Find(static candidate => candidate.PendingDeletes.Length > 0);
            sealedOnly = _segments.Find(static candidate => candidate.Sealed && !candidate.Consolidated);
        }

        if (pending is not null)
        {
            await DeleteChunksAsync(pending, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (sealedOnly is not null && await ConsolidateAsync(sealedOnly, cancellationToken).ConfigureAwait(false))
        {
            await DeleteChunksAsync(sealedOnly, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    // Writes a sealed segment as its one object: from then on the segment is
    // that object, and its chunks wait for maintenance to delete them. True
    // when it did.
    private async ValueTask<bool> ConsolidateAsync(Segment segment, CancellationToken cancellationToken)
    {
        await _consolidating.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_lock)
            {
                if (segment.Consolidated || !segment.Sealed || !Writes || _superseded)
                {
                    return false;
                }
            }

            // Every byte, from the segment's first chunk.
            await ExtendDownAsync(segment, 0, cancellationToken).ConfigureAwait(false);
            byte[] bytes;
            int[] chunks;

            lock (_lock)
            {
                // Sealed: nothing appends to it again, so these bytes are final.
                bytes = segment.Bytes!.WrittenSpan.ToArray();
                chunks = [.. segment.DataChunks];
            }

            (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, _names.Segment(segment.Id), bytes, _clock, cancellationToken).ConfigureAwait(false);

            if (outcome == CreateOutcome.Taken)
            {
                ObjectRead? there = await ConditionalWrites.ReadWholeAsync(_store, _names.Segment(segment.Id), cancellationToken).ConfigureAwait(false);

                // Another writer of the same bytes got there first: done.
                if (there is null || !there.Bytes.Span.SequenceEqual(bytes))
                {
                    throw new LogVerificationException("Segment " + segment.Id + " was consolidated with other bytes than its chunks hold: the log is damaged.");
                }
            }

            // The local copy, from what is in hand rather than downloaded again.
            TryWriteCopy(segment.Id, bytes);

            lock (_lock)
            {
                segment.Consolidated = true;
                segment.ConsolidatedLength = bytes.Length;
                segment.Bytes = null;
                segment.PendingDeletes = chunks;
            }

            return true;
        }
        finally
        {
            _consolidating.Release();
        }
    }

    // A consolidated segment's chunks deleted, a few at a time. A fence chunk
    // is kept for ever (ADR 0136, FenceChunksAreKept): a superseded writer's
    // next create would land in it, and must fail.
    private async ValueTask DeleteChunksAsync(Segment segment, CancellationToken cancellationToken)
    {
        int[] chunks;

        lock (_lock)
        {
            chunks = segment.PendingDeletes;
        }

        for (int at = 0; at < chunks.Length; at += InFlight)
        {
            Task[] deletes = new Task[Math.Min(InFlight, chunks.Length - at)];

            for (int i = 0; i < deletes.Length; i++)
            {
                deletes[i] = _store.DeleteAsync(_names.Chunk(segment.Id, chunks[at + i]), cancellationToken).AsTask();
            }

            await Task.WhenAll(deletes).ConfigureAwait(false);
        }

        lock (_lock)
        {
            segment.PendingDeletes = [];
        }
    }

    /// <summary>
    /// A follower's refresh (ADR 0164): probes the next slot in log order with
    /// a GET, adopts a chunk found there, and probes the slot after it, until
    /// one is absent. An absent slot is nothing new once its segment's
    /// consolidated object is absent too: consolidation deletes a sealed
    /// segment's chunks, its seal among them, so a seal this store has not
    /// seen can leave nothing at the slot. A consolidated object found is
    /// adopted, and the probe moves to the next segment. Nothing is listed,
    /// and nothing is written. True when anything was adopted.
    /// </summary>
    /// <exception cref="LogVerificationException">A chunk does not verify or does not follow.</exception>
    /// <summary>
    /// The highest writer epoch in a chunk header this store holds: what a
    /// follower's refresh has seen of the writers since its open (ADR 0179
    /// point 12). 0 when it holds no chunk with a header, as when it adopted
    /// only consolidated segments.
    /// </summary>
    public long AdoptedEpoch
    {
        get
        {
            lock (_lock)
            {
                long epoch = 0;

                foreach (Segment segment in _segments)
                {
                    epoch = Math.Max(epoch, segment.Epoch);
                }

                return epoch;
            }
        }
    }

    public async ValueTask<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        if (Writes)
        {
            throw new InvalidOperationException("A writer's log grows by its own appends; only a read-only store refreshes.");
        }

        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        bool adopted = false;

        while (true)
        {
            int segmentId;
            int chunk;
            Segment? held;

            lock (_lock)
            {
                held = _segments.Count == 0 ? null : _segments[^1];

                if (held is null || held.Sealed)
                {
                    segmentId = held is null ? 0 : held.Id + 1;
                    chunk = 0;
                    held = null;
                }
                else
                {
                    segmentId = held.Id;
                    chunk = held.NextChunk;
                }
            }

            if (await ConditionalWrites.ReadWholeAsync(_store, _names.Chunk(segmentId, chunk), cancellationToken).ConfigureAwait(false) is { } read)
            {
                lock (_lock)
                {
                    (held ?? AddSegment(segmentId)).Adopt(read.Bytes.Span, chunk);
                }

                adopted = true;
                continue;
            }

            if (await ConditionalWrites.ReadWholeAsync(_store, _names.Segment(segmentId), cancellationToken).ConfigureAwait(false) is not { } whole)
            {
                return adopted;
            }

            AdoptConsolidated(held ?? AddFollowedSegment(segmentId), whole);
            adopted = true;
        }
    }

    // A segment first seen consolidated, by a follower: it holds nothing yet.
    private Segment AddFollowedSegment(int id)
    {
        lock (_lock)
        {
            Segment segment = new(id) { LoadedFrom = 0 };
            _segments.Add(segment);
            return segment;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (SafeFileHandle copy in _copies.Values)
            {
                copy.Dispose();
            }

            _copies.Clear();
        }

        _writing.Dispose();
        _downloading.Dispose();
        _consolidating.Dispose();
    }

    /// <summary>
    /// A follower's store becomes the writer's, in place (ADR 0179 point 8):
    /// it adopts every chunk the writer it followed made, by the same probe a
    /// refresh makes, then takes <paramref name="epoch"/>, already claimed in
    /// the writer record, and fences the next slot with it, as a writer's open
    /// does. A fence that finds a later epoch leaves it superseded.
    /// </summary>
    public async ValueTask ClaimInPlaceAsync(long epoch, CancellationToken cancellationToken)
    {
        if (Writes)
        {
            throw new InvalidOperationException("A writer's store holds its epoch already.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(epoch, 1L);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        lock (_lock)
        {
            _epoch = epoch;
        }

        await FenceAsync(cancellationToken).ConfigureAwait(false);
    }

    // The fence (ADR 0136): the slot after the last that exists, in log
    // order, created with this writer's epoch. A create that finds its slot
    // taken adopts what is there and tries the next; one that finds a later
    // epoch there is superseded already. So is a writer whose log already
    // ends in a later epoch's chunk: it claimed before that writer, and
    // listed the log after it fenced (#165). Fencing past it would let the
    // lower epoch write and supersede the higher.
    private async ValueTask FenceAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            int segmentId;
            int chunk;
            long offset;
            byte[] previous;

            lock (_lock)
            {
                if (_superseded)
                {
                    return;
                }

                Segment? last = _segments.Count == 0 ? null : _segments[^1];

                if (last is not null && last.Epoch > _epoch)
                {
                    _superseded = true;
                    return;
                }

                if (last is null || last.Sealed)
                {
                    segmentId = last is null ? 0 : last.Id + 1;
                    chunk = 0;
                    offset = 0;
                    previous = ChunkHeader.Nothing();
                }
                else
                {
                    segmentId = last.Id;
                    chunk = last.NextChunk;
                    offset = last.Length;
                    previous = last.Previous;
                }
            }

            byte[] bytes = ChunkHeader.Compose(ChunkFlags.Fence, _epoch, segmentId, chunk, offset, [], previous, out byte[] hash);
            (CreateOutcome outcome, ReadOnlyMemory<byte> existing) = await ConditionalWrites.CreateAsync(_store, _names.Chunk(segmentId, chunk), bytes, _clock, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                Segment target = chunk == 0 ? AddSegment(segmentId) : Get(segmentId);

                if (outcome == CreateOutcome.Ours)
                {
                    target.Recorded(chunk, hash, ChunkFlags.Fence, _epoch, offset);
                    Fence = new LogSlot(ObjectNames.Slot(segmentId, chunk));
                    return;
                }

                // Another writer's chunk, made after the listing: an older
                // epoch's last write, adopted; a newer epoch's fence, which
                // supersedes this writer before it has written anything.
                if (target.Adopt(existing.Span, chunk) > _epoch)
                {
                    _superseded = true;
                    return;
                }
            }
        }
    }

    private async ValueTask WriteChunkAsync(int segmentId, bool seal, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        Segment? sealedNow = null;

        try
        {
            Segment target;
            byte[] chunk;
            byte[] hash;
            int number;
            int count;
            long offset;
            ChunkFlags flags = seal ? ChunkFlags.Seal : ChunkFlags.None;

            lock (_lock)
            {
                RefuseWrites();
                target = Get(segmentId);

                if (target.Sealed)
                {
                    if (seal)
                    {
                        return;
                    }

                    throw new InvalidOperationException("Segment " + segmentId + " is sealed and never changes again.");
                }

                count = (int)(target.Length - target.Persisted);

                if (count == 0 && !seal)
                {
                    return;
                }

                number = target.NextChunk;
                offset = target.Persisted;
                chunk = ChunkHeader.Compose(flags, _epoch, segmentId, number, offset, target.Bytes!.WrittenSpan.Slice((int)(offset - target.Base), count), target.Previous, out hash);
            }

            await CreateChunkAsync(segmentId, number, chunk, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                target.Persisted += count;
                target.Recorded(number, hash, flags, _epoch, offset);
                target.Sealed |= seal;
            }

            sealedNow = seal ? target : null;
        }
        finally
        {
            _writing.Release();
        }

        // Consolidation on seal (ADR 0135): the sealed segment becomes one
        // object now, and its chunks are deleted by the maintenance this
        // leaves due, off the commit path, so chunks stay bounded by the open
        // segment. The seal is durable whatever happens here; a consolidation
        // that fails is left for maintenance to do again.
        if (sealedNow is not null)
        {
            try
            {
                await ConsolidateAsync(sealedNow, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (System.Net.Http.HttpRequestException)
            {
            }
        }
    }

    // The first use of the log: every segment not consolidated held from the
    // chunk it is to be read from — the cursor's segment from its slot, every
    // other from its first chunk.
    private async ValueTask EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _ready))
        {
            return;
        }

        Segment[] pending;
        (int Segment, long Slot)? start;

        lock (_lock)
        {
            pending = [.. _segments.FindAll(static segment => !segment.Consolidated)];
            start = _start;
        }

        foreach (Segment segment in pending)
        {
            int from = start is { } at && at.Segment == segment.Id ? (int)Math.Clamp(at.Slot, 0, Math.Max(0, segment.Listed - 1)) : 0;

            if (segment.LoadedFrom < 0)
            {
                await LoadFromAsync(segment, from, cancellationToken).ConfigureAwait(false);
            }
            else if (segment.LoadedFrom > from)
            {
                await ExtendDownAsync(segment, from, cancellationToken).ConfigureAwait(false);
            }
        }

        Volatile.Write(ref _ready, true);
    }

    // Reads a segment's listed chunks from `from` on, into a segment that
    // holds none yet.
    private async ValueTask LoadFromAsync(Segment segment, int from, CancellationToken cancellationToken)
    {
        await _downloading.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (segment.LoadedFrom >= 0 || segment.Consolidated)
            {
                return;
            }

            if (await ReadChunksAsync(segment.Id, from, segment.Listed, cancellationToken).ConfigureAwait(false) is not { } chunks)
            {
                await AdoptConsolidatedAsync(segment, cancellationToken).ConfigureAwait(false);
                return;
            }

            lock (_lock)
            {
                for (int i = 0; i < chunks.Length; i++)
                {
                    segment.Adopt(chunks[i].Span, from + i);
                }
            }
        }
        finally
        {
            _downloading.Release();
        }
    }

    // Reads the chunks below a segment's first held one, down to `to`, checks
    // that they chain into it, and puts their bytes before its own.
    private async ValueTask ExtendDownAsync(Segment segment, int to, CancellationToken cancellationToken)
    {
        await _downloading.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            int held = segment.LoadedFrom;

            if (held <= to || segment.Consolidated)
            {
                return;
            }

            if (await ReadChunksAsync(segment.Id, to, held, cancellationToken).ConfigureAwait(false) is not { } chunks)
            {
                await AdoptConsolidatedAsync(segment, cancellationToken).ConfigureAwait(false);
                return;
            }

            Segment below = new(segment.Id);

            for (int i = 0; i < chunks.Length; i++)
            {
                below.Adopt(chunks[i].Span, to + i);
            }

            lock (_lock)
            {
                segment.Prepend(below);
            }
        }
        finally
        {
            _downloading.Release();
        }
    }

    // Chunks [from, to) of a segment, read a few at a time; null when one
    // listed is gone, which a consolidation since the listing explains.
    private async ValueTask<ReadOnlyMemory<byte>[]?> ReadChunksAsync(int segment, int from, int to, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte>[] chunks = new ReadOnlyMemory<byte>[Math.Max(0, to - from)];

        for (int at = 0; at < chunks.Length; at += InFlight)
        {
            Task<ObjectRead?>[] reads = new Task<ObjectRead?>[Math.Min(InFlight, chunks.Length - at)];

            for (int i = 0; i < reads.Length; i++)
            {
                reads[i] = ConditionalWrites.ReadWholeAsync(_store, _names.Chunk(segment, from + at + i), cancellationToken).AsTask();
            }

            await Task.WhenAll(reads).ConfigureAwait(false);

            for (int i = 0; i < reads.Length; i++)
            {
                if (await reads[i].ConfigureAwait(false) is not { } read)
                {
                    return null;
                }

                chunks[at + i] = read.Bytes;
            }
        }

        return chunks;
    }

    // A chunk the listing showed is gone: a writer consolidated the segment
    // since — a superseded one that had not yet learned it was. The segment
    // is then its one object, which must hold every byte this store holds of
    // it; it is taken as consolidated, its copy written to the cache.
    private async ValueTask AdoptConsolidatedAsync(Segment segment, CancellationToken cancellationToken)
    {
        string slot = ObjectNames.Slot(segment.Id, 0);
        ObjectRead whole = await ConditionalWrites.ReadWholeAsync(_store, _names.Segment(segment.Id), cancellationToken).ConfigureAwait(false)
            ?? throw new LogVerificationException("A chunk of segment " + segment.Id + " was listed and is gone, and the segment was not consolidated: the log is damaged (" + slot + ").");
        AdoptConsolidated(segment, whole);
    }

    // The segment as its consolidated object, which must hold every byte this
    // store holds of it; its copy written to the cache.
    private void AdoptConsolidated(Segment segment, ObjectRead whole)
    {
        lock (_lock)
        {
            if (segment.Consolidated)
            {
                return;
            }

            ReadOnlySpan<byte> held = segment.LoadedFrom < 0 ? [] : segment.Bytes!.WrittenSpan;

            if (segment.Base + held.Length > whole.Bytes.Length
                || (held.Length > 0 && !whole.Bytes.Span.Slice((int)segment.Base, held.Length).SequenceEqual(held)))
            {
                throw new LogVerificationException("Segment " + segment.Id + " was consolidated with other bytes than its chunks hold: the log is damaged.");
            }
        }

        TryWriteCopy(segment.Id, whole.Bytes.Span);

        lock (_lock)
        {
            segment.Consolidated = true;
            segment.Sealed = true;
            segment.Opened = true;
            segment.ConsolidatedLength = whole.Bytes.Length;
            segment.Bytes = null;
        }
    }

    private async ValueTask CreateChunkAsync(int segment, int chunk, byte[] bytes, CancellationToken cancellationToken)
    {
        (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, _names.Chunk(segment, chunk), bytes, _clock, cancellationToken).ConfigureAwait(false);

        if (outcome == CreateOutcome.Taken)
        {
            lock (_lock)
            {
                _superseded = true;
            }

            throw Supersession(segment, chunk);
        }
    }

    private void RefuseWrites()
    {
        if (!Writes)
        {
            throw new InvalidOperationException("The dataset is open read-only; it claimed no writer epoch.");
        }

        if (_superseded)
        {
            throw new WriterSupersededException("This writer's epoch has passed to another writer; it appends nothing more. Reads continue.");
        }
    }

    private static WriterSupersededException Supersession(int segment, int chunk) =>
        new("Log slot " + ObjectNames.Slot(segment, chunk) + " was taken by another writer: a newer epoch holds the log, and this writer appends nothing more. Every commit it made durable before was adopted.");

    // Segments up to and including the newest that holds bytes or was opened.
    private int Visible()
    {
        int visible = _segments.Count;

        while (visible > 0 && !_segments[visible - 1].Opened && _segments[visible - 1].Length == 0)
        {
            visible--;
        }

        return visible;
    }

    private Segment AddSegment(int id)
    {
        if (_segments.Count > 0 && _segments[^1].Id == id)
        {
            return _segments[^1];
        }

        Segment segment = new(id) { LoadedFrom = 0 };
        _segments.Add(segment);
        return segment;
    }

    private Segment Get(int id)
    {
        foreach (Segment candidate in _segments)
        {
            if (candidate.Id == id)
            {
                return candidate;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(id), id, "No such segment.");
    }

    // The local copy of a consolidated segment, made from the store on a
    // miss; none once the copies are off (#146).
    private async ValueTask<SafeFileHandle?> CopyAsync(Segment segment, CancellationToken cancellationToken)
    {
        if (Cache.IsOff(LocalCache.SegmentCopies))
        {
            return null;
        }

        lock (_lock)
        {
            if (_copies.TryGetValue(segment.Id, out SafeFileHandle? open))
            {
                return open;
            }
        }

        await _downloading.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string path = CopyPath(segment.Id);

            if (!File.Exists(path) || new FileInfo(path).Length != segment.ConsolidatedLength)
            {
                ObjectRead read = await ConditionalWrites.ReadWholeAsync(_store, _names.Segment(segment.Id), cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("Segment " + segment.Id + " was listed as consolidated and is gone from the store.");

                if (!TryWriteCopy(segment.Id, read.Bytes.Span))
                {
                    return null;
                }
            }

            try
            {
                lock (_lock)
                {
                    if (!_copies.TryGetValue(segment.Id, out SafeFileHandle? handle))
                    {
                        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                        _copies[segment.Id] = handle;
                    }

                    return handle;
                }
            }
            catch (Exception error) when (LocalCacheHealth.IsLocal(error))
            {
                Cache.TurnOff(LocalCache.SegmentCopies);
                return null;
            }
        }
        finally
        {
            _downloading.Release();
        }
    }

    // A copy written, or the copies turned off when the disk refuses it
    // (#146): the cache is a miss, never a stop. False when it was not.
    private bool TryWriteCopy(int segment, ReadOnlySpan<byte> bytes)
    {
        if (Cache.IsOff(LocalCache.SegmentCopies))
        {
            return false;
        }

        try
        {
            Cache.Check(LocalCache.SegmentCopies);
            WriteCopy(segment, bytes);
            return true;
        }
        catch (Exception error) when (LocalCacheHealth.IsLocal(error))
        {
            Cache.TurnOff(LocalCache.SegmentCopies);
            return false;
        }
    }

    // A temporary file, then a rename: a copy is whole or absent.
    private void WriteCopy(int segment, ReadOnlySpan<byte> bytes)
    {
        string path = CopyPath(segment);
        string temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (SafeFileHandle handle = File.OpenHandle(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            RandomAccess.Write(handle, bytes, 0);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private string CopyPath(int segment) => Path.Combine(_cache, segment.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".seg");

    /// <summary>One segment as this store holds it.</summary>
    private sealed class Segment(int id)
    {
        private List<int> _dataChunks = [];
        private List<(int Chunk, long Offset)> _offsets = [];

        public int Id { get; } = id;

        /// <summary>Whether it has been opened for appends, or holds bytes; false for one that holds only fences.</summary>
        public bool Opened { get; set; }

        public bool Sealed { get; set; }

        public bool Consolidated { get; set; }

        public long ConsolidatedLength { get; set; }

        /// <summary>How many chunks the listing at open showed, for a segment not consolidated.</summary>
        public int Listed { get; set; }

        /// <summary>The first chunk held, or -1 when none is: the chunks before it are not read yet.</summary>
        public int LoadedFrom { get; set; } = -1;

        /// <summary>Where in the segment the first held byte is: the first held chunk's offset.</summary>
        public long Base { get; private set; }

        /// <summary>Every byte held of a segment not yet consolidated, from <see cref="Base"/>: those in chunks, then those appended since.</summary>
        public ArrayBufferWriter<byte>? Bytes { get; set; } = new();

        /// <summary>Where in the segment the bytes in chunks end.</summary>
        public long Persisted { get; set; }

        public int NextChunk { get; private set; }

        public byte[] Previous { get; private set; } = ChunkHeader.Nothing();

        /// <summary>The hash the first held chunk names as its predecessor's, which the chunks below it must end with.</summary>
        public byte[] FirstPrevious { get; private set; } = ChunkHeader.Nothing();

        public long Epoch { get; private set; }

        /// <summary>The chunks of a consolidated segment its consolidation has yet to delete.</summary>
        public int[] PendingDeletes { get; set; } = [];

        /// <summary>The held chunks that are not fences: what consolidation deletes.</summary>
        public IReadOnlyList<int> DataChunks => _dataChunks;

        public long Length => Consolidated ? ConsolidatedLength : Base + Bytes!.WrittenCount;

        /// <summary>Notes a chunk this store created.</summary>
        public void Recorded(int chunk, byte[] hash, ChunkFlags flags, long epoch, long offset)
        {
            NextChunk = chunk + 1;
            Previous = hash;
            Epoch = epoch;
            _offsets.Add((chunk, offset));

            if ((flags & ChunkFlags.Fence) == 0)
            {
                _dataChunks.Add(chunk);
            }

            if ((flags & ChunkFlags.Opens) != 0)
            {
                Opened = true;
            }
        }

        /// <summary>
        /// The held chunk holding the byte before <paramref name="offset"/>,
        /// or 0 when that is not held: a slot to read the segment from.
        /// </summary>
        public long SlotOf(long offset)
        {
            long slot = 0;

            foreach ((int chunk, long at) in _offsets)
            {
                if (at <= offset - 1)
                {
                    slot = chunk;
                }
            }

            return slot;
        }

        /// <summary>
        /// Checks a chunk another writer created, or one read back, and takes
        /// in its bytes. The first chunk a segment holds may be any: it fixes
        /// where the held bytes start. Returns its epoch.
        /// </summary>
        /// <exception cref="LogVerificationException">The chunk does not verify, or does not follow the one before it.</exception>
        public long Adopt(ReadOnlySpan<byte> bytes, int chunk)
        {
            string slot = ObjectNames.Slot(Id, chunk);

            if (!ChunkHeader.TryRead(bytes, out ChunkHeader header, out byte[] hash))
            {
                throw new LogVerificationException("Chunk " + slot + " does not verify: its header or its bytes are damaged.");
            }

            if (LoadedFrom < 0)
            {
                // The first chunk held: the chain is checked from it on, and
                // the chunks before it, when read, must end where it starts.
                LoadedFrom = chunk;
                NextChunk = chunk;
                Base = header.Offset;
                Persisted = header.Offset;
                Previous = header.Previous;
                FirstPrevious = header.Previous;
                Epoch = header.Epoch;
                Opened |= header.Offset > 0;
            }

            if (header.Segment != Id || header.Chunk != chunk || chunk != NextChunk)
            {
                throw new LogVerificationException("Chunk " + slot + " names slot " + ObjectNames.Slot(header.Segment, header.Chunk) + ".");
            }

            if (Sealed && header.Length > 0)
            {
                throw new LogVerificationException("Chunk " + slot + " holds bytes after its segment's seal.");
            }

            if (header.Offset != Length || !header.Previous.AsSpan().SequenceEqual(Previous))
            {
                throw new LogVerificationException("Chunk " + slot + " does not follow the chunk before it.");
            }

            if (header.Epoch < Epoch)
            {
                throw new LogVerificationException("Chunk " + slot + " is of epoch " + header.Epoch + " after a chunk of epoch " + Epoch + ".");
            }

            Bytes!.Write(bytes[ChunkHeader.Size..]);
            Persisted = Length;
            Recorded(chunk, hash, header.Flags, header.Epoch, header.Offset);
            Opened |= header.Length > 0;
            Sealed |= (header.Flags & ChunkFlags.Seal) != 0;
            return header.Epoch;
        }

        /// <summary>
        /// Puts the chunks of <paramref name="below"/>, read from the store,
        /// before this segment's first held one, once they are checked to end
        /// where it starts and with the hash it names.
        /// </summary>
        /// <exception cref="LogVerificationException">They do not.</exception>
        public void Prepend(Segment below)
        {
            if (below.NextChunk != LoadedFrom || below.Length != Base || !below.Previous.AsSpan().SequenceEqual(FirstPrevious) || below.Epoch > Epoch)
            {
                throw new LogVerificationException("Chunks " + ObjectNames.Slot(Id, below.LoadedFrom) + " to " + ObjectNames.Slot(Id, LoadedFrom - 1) + " do not chain into chunk " + ObjectNames.Slot(Id, LoadedFrom) + ".");
            }

            if (below.Sealed && Bytes!.WrittenCount > 0)
            {
                throw new LogVerificationException("Segment " + Id + " holds bytes after its seal.");
            }

            // A run of fence chunks holds no bytes, and a capacity of zero is
            // refused: the default capacity then (#144).
            int joinedLength = below.Bytes!.WrittenCount + Bytes!.WrittenCount;
            ArrayBufferWriter<byte> joined = joinedLength == 0 ? new() : new(joinedLength);
            joined.Write(below.Bytes.WrittenSpan);
            joined.Write(Bytes.WrittenSpan);
            Bytes = joined;
            Base = below.Base;
            LoadedFrom = below.LoadedFrom;
            FirstPrevious = below.FirstPrevious;
            Sealed |= below.Sealed;
            Opened |= below.Opened;
            _dataChunks = [.. below._dataChunks, .. _dataChunks];
            _offsets = [.. below._offsets, .. _offsets];
        }
    }
}
