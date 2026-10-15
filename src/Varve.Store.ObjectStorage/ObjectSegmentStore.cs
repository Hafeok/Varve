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
/// creates one chunk, a seal creates a chunk marked so, and maintenance
/// consolidates a sealed segment into one object.
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
/// </remarks>
internal sealed class ObjectSegmentStore : ISegmentStore, IDisposable
{
    private readonly IObjectStore _store;
    private readonly ObjectNames _names;
    private readonly TimeProvider _clock;
    private readonly string _cache;
    private readonly long _epoch;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly SemaphoreSlim _downloading = new(1, 1);
    private readonly List<Segment> _segments;
    private readonly Dictionary<int, SafeFileHandle> _copies = [];
    private ReadOnlyMemory<byte> _manifest;
    private bool _superseded;

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
                return Writes && !_superseded && _segments.Exists(static segment => segment.Sealed && !segment.Consolidated);
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

                    ObjectRead read = await ConditionalWrites.ReadWholeAsync(store, names.Chunk(id, i), cancellationToken).ConfigureAwait(false)
                        ?? throw new LogVerificationException("Chunk " + ObjectNames.Slot(id, i) + " was listed and is gone: the log is being consolidated by another writer.");
                    segment.Adopt(read.Bytes.Span, i);
                }
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
            await opened.FenceAsync(cancellationToken).ConfigureAwait(false);
        }

        return opened;

        static List<(int, ObjectEntry)> AddTo(List<(int, ObjectEntry)> list, (int, ObjectEntry) item)
        {
            list.Add(item);
            return list;
        }
    }

    public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
    {
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

            return new ValueTask<IReadOnlyList<SegmentInfo>>(list);
        }
    }

    public async ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
    {
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
                next.Recorded(0, hash, ChunkFlags.Opens, _epoch);
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
        Segment source;

        lock (_lock)
        {
            source = Get(segment.Value);

            if (!source.Consolidated)
            {
                // Appended and not yet flushed included, as on every backend.
                ReadOnlySpan<byte> held = source.Bytes!.WrittenSpan;
                long start = Math.Min(offset.Value, held.Length);
                int count = (int)Math.Min(held.Length - start, Math.Min(length.Value, int.MaxValue));
                return held.Slice((int)start, count).ToArray();
            }
        }

        SafeFileHandle copy = await CopyAsync(source, cancellationToken).ConfigureAwait(false);
        long end = Math.Min(source.ConsolidatedLength, offset.Value + Math.Min(length.Value, int.MaxValue));

        if (offset.Value >= end)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[] bytes = new byte[end - offset.Value];
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
    /// Consolidates the oldest sealed segment that is not yet: its bytes as
    /// <c>log/NNNNNNNN.seg</c>, then every chunk of it deleted but its fences
    /// (ADR 0135). True when it did one.
    /// </summary>
    public async ValueTask<bool> ConsolidateAsync(CancellationToken cancellationToken)
    {
        Segment? segment;
        byte[] bytes;
        int[] chunks;

        lock (_lock)
        {
            if (!Writes || _superseded)
            {
                return false;
            }

            segment = _segments.Find(static candidate => candidate.Sealed && !candidate.Consolidated);

            if (segment is null)
            {
                return false;
            }

            // Sealed: nothing appends to it again, so these bytes are final.
            bytes = segment.Bytes!.WrittenSpan.ToArray();
            chunks = [.. segment.DataChunks];
        }

        (CreateOutcome outcome, _) = await ConditionalWrites.CreateAsync(_store, _names.Segment(segment.Id), bytes, _clock, cancellationToken).ConfigureAwait(false);

        if (outcome == CreateOutcome.Taken)
        {
            throw new LogVerificationException("Segment " + segment.Id + " was consolidated with other bytes than its chunks hold: the log is damaged.");
        }

        // The local copy, from what is in hand rather than downloaded again.
        WriteCopy(segment.Id, bytes);

        lock (_lock)
        {
            segment.Consolidated = true;
            segment.ConsolidatedLength = bytes.Length;
            segment.Bytes = null;
        }

        // A fence chunk is kept for ever (ADR 0136, FenceChunksAreKept): a
        // superseded writer's next create would land in it, and must fail.
        foreach (int chunk in chunks)
        {
            await _store.DeleteAsync(_names.Chunk(segment.Id, chunk), cancellationToken).ConfigureAwait(false);
        }

        return true;
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
    }

    // The fence (ADR 0136): the slot after the last that exists, in log
    // order, created with this writer's epoch. A create that finds its slot
    // taken adopts what is there and tries the next; one that finds a later
    // epoch there is superseded already.
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
                    target.Recorded(chunk, hash, ChunkFlags.Fence, _epoch);
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
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Segment target;
            byte[] chunk;
            byte[] hash;
            int number;
            int count;
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

                count = target.Bytes!.WrittenCount - (int)target.Persisted;

                if (count == 0 && !seal)
                {
                    return;
                }

                number = target.NextChunk;
                chunk = ChunkHeader.Compose(flags, _epoch, segmentId, number, target.Persisted, target.Bytes.WrittenSpan.Slice((int)target.Persisted, count), target.Previous, out hash);
            }

            await CreateChunkAsync(segmentId, number, chunk, cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                target.Persisted += count;
                target.Recorded(number, hash, flags, _epoch);
                target.Sealed |= seal;
            }
        }
        finally
        {
            _writing.Release();
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

        Segment segment = new(id);
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

    private async ValueTask<SafeFileHandle> CopyAsync(Segment segment, CancellationToken cancellationToken)
    {
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
                WriteCopy(segment.Id, read.Bytes.Span);
            }

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
        finally
        {
            _downloading.Release();
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
        private readonly List<int> _dataChunks = [];

        public int Id { get; } = id;

        /// <summary>Whether it has been opened for appends, or holds bytes; false for one that holds only fences.</summary>
        public bool Opened { get; set; }

        public bool Sealed { get; set; }

        public bool Consolidated { get; set; }

        public long ConsolidatedLength { get; set; }

        /// <summary>Every byte of a segment not yet consolidated: those in chunks, then those appended since.</summary>
        public ArrayBufferWriter<byte>? Bytes { get; set; } = new();

        /// <summary>How many of <see cref="Bytes"/> are in chunks.</summary>
        public long Persisted { get; set; }

        public int NextChunk { get; private set; }

        public byte[] Previous { get; private set; } = ChunkHeader.Nothing();

        public long Epoch { get; private set; }

        /// <summary>Chunks that are not fences: what consolidation deletes.</summary>
        public IReadOnlyList<int> DataChunks => _dataChunks;

        public long Length => Consolidated ? ConsolidatedLength : Bytes!.WrittenCount;

        /// <summary>Notes a chunk this store created.</summary>
        public void Recorded(int chunk, byte[] hash, ChunkFlags flags, long epoch)
        {
            NextChunk = chunk + 1;
            Previous = hash;
            Epoch = epoch;

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
        /// Checks a chunk another writer created and takes in its bytes.
        /// Returns its epoch.
        /// </summary>
        /// <exception cref="LogVerificationException">The chunk does not verify, or does not follow the one before it.</exception>
        public long Adopt(ReadOnlySpan<byte> bytes, int chunk)
        {
            string slot = ObjectNames.Slot(Id, chunk);

            if (!ChunkHeader.TryRead(bytes, out ChunkHeader header, out byte[] hash))
            {
                throw new LogVerificationException("Chunk " + slot + " does not verify: its header or its bytes are damaged.");
            }

            if (header.Segment != Id || header.Chunk != chunk || chunk != NextChunk)
            {
                throw new LogVerificationException("Chunk " + slot + " names slot " + ObjectNames.Slot(header.Segment, header.Chunk) + ".");
            }

            if (Sealed && header.Length > 0)
            {
                throw new LogVerificationException("Chunk " + slot + " holds bytes after its segment's seal.");
            }

            if (header.Offset != Bytes!.WrittenCount || !header.Previous.AsSpan().SequenceEqual(Previous))
            {
                throw new LogVerificationException("Chunk " + slot + " does not follow the chunk before it.");
            }

            if (header.Epoch < Epoch)
            {
                throw new LogVerificationException("Chunk " + slot + " is of epoch " + header.Epoch + " after a chunk of epoch " + Epoch + ".");
            }

            Bytes.Write(bytes[ChunkHeader.Size..]);
            Persisted = Bytes.WrittenCount;
            Recorded(chunk, hash, header.Flags, header.Epoch);
            Opened |= header.Length > 0;
            Sealed |= (header.Flags & ChunkFlags.Seal) != 0;
            return header.Epoch;
        }
    }
}
