// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// What the store keeps per closed commit, beside the log: its timestamp, the
/// hash of its header, where its records begin, the dictionary's counters
/// after it, and the bytes of the log up to its end. Eighty bytes, the same in
/// memory as on disk (ADR 0089).
/// </summary>
internal readonly struct CommitEntry
{
    /// <summary>The bytes of an entry in a commit index blob.</summary>
    internal const int Size = 80;

    private readonly ulong _hash0;
    private readonly ulong _hash1;
    private readonly ulong _hash2;
    private readonly ulong _hash3;

    internal CommitEntry(long timestampTicks, ReadOnlySpan<byte> headerHash, CommitLocation location, long canonicalCount, long blankCount, long logBytes)
    {
        TimestampTicks = timestampTicks;
        _hash0 = BinaryPrimitives.ReadUInt64LittleEndian(headerHash);
        _hash1 = BinaryPrimitives.ReadUInt64LittleEndian(headerHash[8..]);
        _hash2 = BinaryPrimitives.ReadUInt64LittleEndian(headerHash[16..]);
        _hash3 = BinaryPrimitives.ReadUInt64LittleEndian(headerHash[24..]);
        Location = location;
        CanonicalCount = canonicalCount;
        BlankCount = blankCount;
        LogBytes = logBytes;
    }

    internal long TimestampTicks { get; }

    internal CommitLocation Location { get; }

    internal long CanonicalCount { get; }

    internal long BlankCount { get; }

    /// <summary>The bytes of every commit's records up to this one's end.</summary>
    internal long LogBytes { get; }

    /// <summary>The hash of the commit's header, as a new array.</summary>
    internal byte[] HeaderHash()
    {
        byte[] hash = new byte[32];
        WriteHash(hash);
        return hash;
    }

    internal bool HashEquals(ReadOnlySpan<byte> hash) =>
        hash.Length == 32
        && BinaryPrimitives.ReadUInt64LittleEndian(hash) == _hash0
        && BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]) == _hash1
        && BinaryPrimitives.ReadUInt64LittleEndian(hash[16..]) == _hash2
        && BinaryPrimitives.ReadUInt64LittleEndian(hash[24..]) == _hash3;

    internal bool SameAs(in CommitEntry other) =>
        TimestampTicks == other.TimestampTicks
        && _hash0 == other._hash0 && _hash1 == other._hash1 && _hash2 == other._hash2 && _hash3 == other._hash3
        && Location.Segment == other.Location.Segment && Location.Offset == other.Location.Offset
        && CanonicalCount == other.CanonicalCount && BlankCount == other.BlankCount && LogBytes == other.LogBytes;

    // Little-endian: the timestamp, the header hash, the segment, four bytes
    // of zeros, the offset, the two counters and the log's bytes.
    internal void Write(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, TimestampTicks);
        WriteHash(destination[8..40]);
        BinaryPrimitives.WriteInt32LittleEndian(destination[40..], Location.Segment);
        BinaryPrimitives.WriteInt32LittleEndian(destination[44..], 0);
        BinaryPrimitives.WriteInt64LittleEndian(destination[48..], Location.Offset);
        BinaryPrimitives.WriteInt64LittleEndian(destination[56..], CanonicalCount);
        BinaryPrimitives.WriteInt64LittleEndian(destination[64..], BlankCount);
        BinaryPrimitives.WriteInt64LittleEndian(destination[72..], LogBytes);
    }

    internal static CommitEntry Read(ReadOnlySpan<byte> source) =>
        new(
            BinaryPrimitives.ReadInt64LittleEndian(source),
            source[8..40],
            new CommitLocation(BinaryPrimitives.ReadInt32LittleEndian(source[40..]), BinaryPrimitives.ReadInt64LittleEndian(source[48..])),
            BinaryPrimitives.ReadInt64LittleEndian(source[56..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[64..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[72..]));

    private void WriteHash(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, _hash0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], _hash1);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], _hash2);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], _hash3);
    }
}

/// <summary>The settings from a settings commit's position on (ADR 0089).</summary>
internal readonly record struct SettingsPoint(long Position, DatasetSettings Settings);

/// <summary>
/// A blob of the commit index: the entries of the positions after
/// <see cref="From"/> up to <see cref="To"/>, end to end, in blocks of 128
/// with the first timestamp of each block held as its fence, and the hash of
/// the header at <see cref="To"/> in the derived header, so that a blob beside
/// another log is a cache miss (ADR 0089).
/// </summary>
internal sealed class CommitSegment
{
    /// <summary>The entries of a block, whose first timestamp is its fence.</summary>
    internal const int BlockEntries = 128;

    internal CommitSegment(RunBlob blob, long from, long to, Chunked<long> fences)
    {
        Blob = blob;
        From = from;
        To = to;
        Fences = fences;
    }

    internal RunBlob Blob { get; }

    internal long From { get; }

    internal long To { get; }

    internal long Count => To - From;

    /// <summary>The first timestamp of every block.</summary>
    internal Chunked<long> Fences { get; }

    /// <summary>The entry at <paramref name="position"/>, which the caller holds the blob for.</summary>
    internal CommitEntry Read(long position)
    {
        Span<byte> bytes = stackalloc byte[CommitEntry.Size];

        if (Blob.Blob.Read(new ByteOffset((position - From - 1) * CommitEntry.Size), bytes) != bytes.Length)
        {
            throw new System.IO.IOException("A commit index blob is shorter than its directory says.");
        }

        return CommitEntry.Read(bytes);
    }

    /// <summary>The greatest position whose timestamp is at or before <paramref name="ticks"/>, given the first one is.</summary>
    internal long PositionAt(long ticks)
    {
        // The last block whose fence is at or before the timestamp.
        long low = 0;
        long high = Fences.Count - 1;

        while (low < high)
        {
            long middle = low + ((high - low + 1) / 2);

            if (Fences[middle] <= ticks)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        // Within it, the last entry at or before the timestamp; its first is.
        long first = From + 1 + (low * BlockEntries);
        long last = Math.Min(To, first + BlockEntries - 1);

        while (first < last)
        {
            long middle = first + ((last - first + 1) / 2);

            if (Read(middle).TimestampTicks <= ticks)
            {
                first = middle;
            }
            else
            {
                last = middle - 1;
            }
        }

        return first;
    }
}

/// <summary>
/// The commit index: an entry per closed commit, the newest in memory and the
/// rest in blobs of <c>derived/</c>, read through the synchronous blob read
/// (ADR 0089). Immutable: the sequencer publishes a new version with each
/// commit, as part of the dataset's state, and readers keep the version they
/// captured.
/// </summary>
/// <remarks>
/// <para>
/// **The newest entries** are in memory: between <c>cache</c> and twice that,
/// once there are that many. Maintenance writes the oldest of them as a blob
/// when there are twice as many, and merges the newest two blobs while the
/// newer is as large as the older, so that blobs are few — logarithmic in the
/// number of commits — and each entry is written a logarithmic number of times.
/// </para>
/// <para>
/// **A version stays readable.** A blob a newer version has merged away is
/// retired, and deleted once the last reader lets go of it (ADR 0078's rule
/// for runs). A read takes the blob for the length of the read; a reader that
/// finds it already closed reads the position from the current version
/// instead, which holds the same entry, because a commit's entry never
/// changes. A holder of <see cref="TryAcquire"/> keeps every blob of its
/// version open until it calls <see cref="Release"/>.
/// </para>
/// </remarks>
internal sealed class CommitIndex
{
    private readonly Tail _tail;
    private readonly Func<CommitIndex> _current;

    private CommitIndex(CommitSegment[] segments, Tail tail, long head, SettingsPoint[] settings, Func<CommitIndex> current, CommitPrefix? prefix)
    {
        Segments = segments;
        _tail = tail;
        Head = head;
        Settings = settings;
        _current = current;
        Prefix = prefix;
    }

    internal CommitSegment[] Segments { get; }

    internal long Head { get; }

    /// <summary>
    /// The entries at or below the catalog's position when the open began
    /// there (ADR 0139), read from the log on first use; the blobs continue
    /// from its end. None when the open read the whole log.
    /// </summary>
    internal CommitPrefix? Prefix { get; }

    /// <summary>The position the blobs reach; the entries after it are in memory.</summary>
    internal long PagedTo => Segments.Length == 0 ? Prefix?.To ?? 0 : Segments[^1].To;

    /// <summary>How many entries are in memory.</summary>
    internal long InMemory => Head - PagedTo;

    /// <summary>The settings commits' positions and the settings from each on.</summary>
    internal SettingsPoint[] Settings { get; }

    /// <summary>An empty index whose readers fall back to <paramref name="current"/>.</summary>
    internal static CommitIndex Empty(Func<CommitIndex> current) => new([], new Tail(0), 0, [], current, null);

    /// <summary>An index of <paramref name="segments"/> after <paramref name="prefix"/>, and nothing in memory yet.</summary>
    internal static CommitIndex Of(CommitSegment[] segments, SettingsPoint[] settings, Func<CommitIndex> current, CommitPrefix? prefix = null)
    {
        long pagedTo = segments.Length == 0 ? prefix?.To ?? 0 : segments[^1].To;
        return new(segments, new Tail(pagedTo), pagedTo, settings, current, prefix);
    }

    /// <summary>This version with the settings points given.</summary>
    internal CommitIndex WithSettings(SettingsPoint[] settings) => new(Segments, _tail, Head, settings, _current, Prefix);

    /// <summary>The next position's entry, and its settings when it changed them. Only the sequencer appends.</summary>
    internal CommitIndex Append(in CommitEntry entry, DatasetSettings? changed)
    {
        _tail.Append(Head + 1, in entry);
        SettingsPoint[] settings = changed is null ? Settings : [.. Settings, new SettingsPoint(Head + 1, changed)];
        return new(Segments, _tail, Head + 1, settings, _current, Prefix);
    }

    /// <summary>This version with <paramref name="segment"/> after its blobs, and the entries it covers let go of.</summary>
    internal CommitIndex WithPaged(CommitSegment segment)
    {
        if (segment.From != PagedTo || segment.To > Head)
        {
            throw new InvalidOperationException("A commit index blob must continue the index's blobs.");
        }

        Tail tail = new(segment.To);

        for (long p = segment.To + 1; p <= Head; p++)
        {
            tail.Append(p, _tail.Get(p));
        }

        return new([.. Segments, segment], tail, Head, Settings, _current, Prefix);
    }

    /// <summary>This version with its last two blobs replaced by <paramref name="merged"/>, which covers both.</summary>
    internal CommitIndex WithMerged(CommitSegment merged)
    {
        if (Segments.Length < 2 || merged.From != Segments[^2].From || merged.To != Segments[^1].To)
        {
            throw new InvalidOperationException("A merged commit index blob must cover the two it replaces.");
        }

        return new([.. Segments[..^2], merged], _tail, Head, Settings, _current, Prefix);
    }

    /// <summary>
    /// Whether the entry at <paramref name="position"/> can be had without
    /// reading the log: false only inside a prefix not yet read, off its pins.
    /// Checks of derived data at open ask this first, and treat a position
    /// it denies as a cache miss (ADR 0139).
    /// </summary>
    internal bool Knows(long position) =>
        position <= 0 || Prefix is not { } prefix || position > prefix.To || prefix.Loaded || prefix.Pins.ContainsKey(position);

    /// <summary>The entry at <paramref name="position"/>, from 1 to the head.</summary>
    internal CommitEntry Entry(long position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Head);

        if (position > PagedTo)
        {
            return _tail.Get(position);
        }

        if (Prefix is { } prefix && position <= prefix.To)
        {
            return prefix.Entry(position);
        }

        CommitSegment segment = SegmentOf(position);

        if (!segment.Blob.TryAcquire())
        {
            // Merged away and closed since this version was captured: the
            // current version holds the same entry.
            CommitIndex current = _current();
            return ReferenceEquals(current, this)
                ? throw new InvalidOperationException("The commit index's own blob is closed.")
                : current.Entry(position);
        }

        try
        {
            return segment.Read(position);
        }
        finally
        {
            segment.Blob.Release();
        }
    }

    /// <summary>The greatest position whose timestamp is at or before <paramref name="ticks"/>; 0 when none is.</summary>
    internal long PositionAt(long ticks)
    {
        if (Head == 0)
        {
            return 0;
        }

        if (Head > PagedTo && _tail.Get(PagedTo + 1).TimestampTicks <= ticks)
        {
            long low = PagedTo + 1;
            long high = Head;

            while (low < high)
            {
                long middle = low + ((high - low + 1) / 2);

                if (_tail.Get(middle).TimestampTicks <= ticks)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        // The last blob whose first timestamp is at or before it.
        int found = -1;

        for (int lowSegment = 0, highSegment = Segments.Length - 1; lowSegment <= highSegment;)
        {
            int middle = lowSegment + ((highSegment - lowSegment) / 2);

            if (Segments[middle].Fences[0] <= ticks)
            {
                found = middle;
                lowSegment = middle + 1;
            }
            else
            {
                highSegment = middle - 1;
            }
        }

        if (found < 0)
        {
            // Below every blob: in the prefix, whose last entry is pinned, so
            // a time at or after it needs nothing read.
            return Prefix is { } prefix ? prefix.PositionAt(ticks) : 0;
        }

        CommitSegment segment = Segments[found];

        if (!segment.Blob.TryAcquire())
        {
            // The current version may reach further; timestamps never
            // decrease, so this version's answer is the current one's, capped
            // at this version's head.
            CommitIndex current = _current();
            return ReferenceEquals(current, this)
                ? throw new InvalidOperationException("The commit index's own blob is closed.")
                : Math.Min(Head, current.PositionAt(ticks));
        }

        try
        {
            return segment.PositionAt(ticks);
        }
        finally
        {
            segment.Blob.Release();
        }
    }

    /// <summary>The settings at a closed position: the last settings commit's at or before it.</summary>
    internal DatasetSettings SettingsAt(long position)
    {
        DatasetSettings settings = DatasetSettings.Default;

        foreach (SettingsPoint point in Settings)
        {
            if (point.Position > position)
            {
                break;
            }

            settings = point.Settings;
        }

        return settings;
    }

    /// <summary>Holds every blob of this version open until <see cref="Release"/>; false when one has closed.</summary>
    internal bool TryAcquire()
    {
        for (int i = 0; i < Segments.Length; i++)
        {
            if (!Segments[i].Blob.TryAcquire())
            {
                for (int j = 0; j < i; j++)
                {
                    Segments[j].Blob.Release();
                }

                return false;
            }
        }

        return true;
    }

    internal void Release()
    {
        foreach (CommitSegment segment in Segments)
        {
            segment.Blob.Release();
        }
    }

    private CommitSegment SegmentOf(long position)
    {
        int low = 0;
        int high = Segments.Length - 1;

        while (low < high)
        {
            int middle = low + ((high - low) / 2);

            if (Segments[middle].To < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return Segments[low];
    }

    /// <summary>
    /// The entries in memory, after a position: appended by the sequencer
    /// alone, read by any version that reaches them. A version reads only
    /// positions up to its own head, which were written before it was
    /// published, so a reader never sees an entry being written.
    /// </summary>
    private sealed class Tail(long after)
    {
        // 512 entries of 80 bytes to a chunk, below the large object heap (issue #61).
        private const int PerChunk = 512;

        private CommitEntry[][] _chunks = [];

        internal void Append(long position, in CommitEntry entry)
        {
            long index = position - after - 1;
            int chunk = (int)(index / PerChunk);

            if (chunk == _chunks.Length)
            {
                CommitEntry[][] grown = new CommitEntry[Math.Max(4, _chunks.Length * 2)][];
                Array.Copy(_chunks, grown, _chunks.Length);
                Volatile.Write(ref _chunks, grown);
            }

            if (_chunks[chunk] is null)
            {
                _chunks[chunk] = new CommitEntry[PerChunk];
            }

            _chunks[chunk][index % PerChunk] = entry;
        }

        internal CommitEntry Get(long position)
        {
            long index = position - after - 1;
            return Volatile.Read(ref _chunks)[index / PerChunk][index % PerChunk];
        }
    }
}

/// <summary>
/// The commit index at or below the catalog's position, when an open began
/// there rather than at the log's start (ADR 0139): the entries the catalog
/// pins — its sets' ends — are known, and the rest are read from the log's
/// headers by the first read that needs one, then kept as a commit index
/// blob in the local cache. Shared by every version of the index, so it is
/// read once.
/// </summary>
internal sealed class CommitPrefix : IDisposable
{
    private readonly Dictionary<long, CommitEntry> _pins;
    private readonly Func<CancellationToken, ValueTask<CommitSegment>> _load;
    private readonly List<CommitSegment> _candidates;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CommitSegment? _segment;

    /// <summary>
    /// A prefix to <paramref name="to"/>. <paramref name="candidates"/> are
    /// blobs it holds for <paramref name="load"/> to try; the load takes them
    /// out of the list, and a prefix disposed before it loads releases them.
    /// </summary>
    internal CommitPrefix(long to, Dictionary<long, CommitEntry> pins, List<CommitSegment> candidates, Func<CancellationToken, ValueTask<CommitSegment>> load)
    {
        _candidates = candidates;
        if (!pins.ContainsKey(to))
        {
            throw new ArgumentException("A prefix pins its last entry.", nameof(pins));
        }

        To = to;
        _pins = pins;
        _load = load;
    }

    /// <summary>The position the prefix reaches: the catalog's.</summary>
    internal long To { get; }

    /// <summary>Whether the entries have been read from the log.</summary>
    internal bool Loaded => Volatile.Read(ref _segment) is not null;

    /// <summary>The entries the catalog pins, by position.</summary>
    internal IReadOnlyDictionary<long, CommitEntry> Pins => _pins;

    /// <summary>The entry at <paramref name="position"/>, read from the log first if it is not pinned and not yet read.</summary>
    internal CommitEntry Entry(long position)
    {
        if (_pins.TryGetValue(position, out CommitEntry pinned))
        {
            return pinned;
        }

        CommitSegment segment = Segment();

        if (!segment.Blob.TryAcquire())
        {
            throw new InvalidOperationException("The commit index's prefix blob is closed.");
        }

        try
        {
            return segment.Read(position);
        }
        finally
        {
            segment.Blob.Release();
        }
    }

    /// <summary>The greatest position at or below <see cref="To"/> whose timestamp is at or before <paramref name="ticks"/>; 0 when none is.</summary>
    internal long PositionAt(long ticks)
    {
        if (_pins[To].TimestampTicks <= ticks)
        {
            return To;
        }

        CommitSegment segment = Segment();

        if (!segment.Blob.TryAcquire())
        {
            throw new InvalidOperationException("The commit index's prefix blob is closed.");
        }

        try
        {
            return segment.Fences[0] > ticks ? 0 : Math.Min(To, segment.PositionAt(ticks));
        }
        finally
        {
            segment.Blob.Release();
        }
    }

    /// <summary>Reads the entries from the log now, unless they are read already.</summary>
    internal async ValueTask LoadAsync(CancellationToken cancellationToken)
    {
        if (Loaded)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_segment is null)
            {
                Volatile.Write(ref _segment, await _load(cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _segment?.Blob.Release();

        foreach (CommitSegment candidate in _candidates)
        {
            candidate.Blob.Release();
        }

        _candidates.Clear();
        _gate.Dispose();
    }

    // The read is synchronous (ADR 0071), as an extent page's miss is; the
    // first read below the catalog waits for the log's headers.
    [DesignDecision(typeof(ExtentsAndTheProjectionCatalog.CommitLocationsBelowTheCatalogReadOnFirstUse), Scope = ExceptionScope.HotPath)]
    private CommitSegment Segment()
    {
        if (Volatile.Read(ref _segment) is { } loaded)
        {
            return loaded;
        }

        LoadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return _segment!;
    }
}

/// <summary>Writing and reading the commit index's blobs (ADR 0089, storage-format.md §7).</summary>
internal static class CommitIndexFormat
{
    internal const string Prefix = "index/commits/";

    /// <summary>
    /// Writes the entries after <paramref name="from"/> up to <paramref name="to"/>
    /// as a blob: the entries end to end, then a directory of the entries per
    /// block, the count and each block's first timestamp, then the derived
    /// header, whose end hash is the header hash at <paramref name="to"/>.
    /// </summary>
    internal static async ValueTask WriteAsync(
        IDerivedStore store, BlobName name, DatasetId dataset, long from, long to, Func<long, CommitEntry> entryAt, CancellationToken cancellationToken,
        ushort version = DerivedFormat.CheckedVersion)
    {
        await using CommitIndexWriter writer = await CommitIndexWriter.StartAsync(store, name, dataset, from, to, cancellationToken, version).ConfigureAwait(false);

        for (long p = from + 1; p <= to; p++)
        {
            await writer.AddAsync(entryAt(p), cancellationToken).ConfigureAwait(false);
        }

        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A commit index blob, or null when it is not one this store can use:
    /// another version, kind or dataset, or damaged. A version-6 blob is
    /// verified whole, every block against its check, before it is used (ADR
    /// 0213). An older one is used only where <paramref name="shared"/>
    /// says it may be a set's, written for a reader of version 4: a local
    /// blob below 6 is a miss, and is rebuilt.
    /// </summary>
    internal static async ValueTask<CommitSegment?> TryLoadAsync(IDerivedStore store, BlobName name, DatasetId dataset, CancellationToken cancellationToken, bool shared = false)
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

        CommitSegment? loaded = null;

        try
        {
            loaded = TryLoad(blob, name, dataset, shared);
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

    [DesignDecision(typeof(VerifiedDerivedData.VerifiedBeforeFirstUse), Scope = ExceptionScope.Boundary)]
    private static CommitSegment? TryLoad(IReadableBlob blob, BlobName name, DatasetId dataset, bool shared)
    {
        if (!DerivedFormat.TryReadHeader(blob, dataset, DerivedFormat.KindCommits, out DerivedHeader header)
            || (header.Version < DerivedFormat.CheckedVersion && !shared))
        {
            return null;
        }

        bool checks = header.Version >= DerivedFormat.CheckedVersion;
        long count = header.To - header.From;
        long blocks = (count + CommitSegment.BlockEntries - 1) / CommitSegment.BlockEntries;

        if (header.From < 0 || count <= 0
            || header.DirectoryOffset != count * CommitEntry.Size
            || header.DirectoryLength != 4 + 8 + (blocks * (checks ? 16 : 8)))
        {
            return null;
        }

        byte[] directory = new byte[header.DirectoryLength];

        if (blob.Read(new ByteOffset(header.DirectoryOffset), directory) != directory.Length
            || !SHA256.HashData(directory).AsSpan().SequenceEqual(header.DirectoryHash)
            || BinaryPrimitives.ReadUInt32LittleEndian(directory) != CommitSegment.BlockEntries
            || BinaryPrimitives.ReadInt64LittleEndian(directory.AsSpan(4)) != count)
        {
            return null;
        }

        Chunked<long> fences = new(blocks);

        for (long i = 0; i < blocks; i++)
        {
            fences.Add(BinaryPrimitives.ReadInt64LittleEndian(directory.AsSpan((int)(12 + (i * 8)))));
        }

        if (checks && !BlocksMatch(blob, count, blocks, directory.AsSpan((int)(12 + (blocks * 8)))))
        {
            return null;
        }

        CommitSegment segment = new(new RunBlob(name, blob), header.From, header.To, fences);

        // The end hash names the commit the blob reaches; its last entry must agree.
        return segment.Read(header.To).HashEquals(header.ToHash) ? segment : null;
    }

    // Every block of entries against its check in the directory, which the
    // header's hash covers (ADR 0213): a blob with any byte damaged is never
    // loaded.
    private static bool BlocksMatch(IReadableBlob blob, long count, long blocks, ReadOnlySpan<byte> checks)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CommitSegment.BlockEntries * CommitEntry.Size);

        try
        {
            for (long i = 0; i < blocks; i++)
            {
                int entries = (int)Math.Min(CommitSegment.BlockEntries, count - (i * CommitSegment.BlockEntries));
                Span<byte> block = buffer.AsSpan(0, entries * CommitEntry.Size);

                if (blob.Read(new ByteOffset(i * CommitSegment.BlockEntries * CommitEntry.Size), block) != block.Length
                    || BlockCheck.Of(block) != BinaryPrimitives.ReadUInt64LittleEndian(checks[(int)(i * 8)..]))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The blob name of the entries after <paramref name="from"/> up to <paramref name="to"/>.</summary>
    internal static BlobName Name(long from, long to, long sequence) =>
        new(Prefix + from.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "-" + to.ToString("D20", System.Globalization.CultureInfo.InvariantCulture)
            + "." + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// A commit index blob written entry by entry, as a scan passes the log, so
/// nothing the size of the log is held (ADR 0089): the entries end to end,
/// then the directory of each block's first timestamp, then the derived
/// header, whose end hash is the last entry's.
/// </summary>
internal sealed class CommitIndexWriter : IAsyncDisposable
{
    // A whole number of blocks, so a block's check is taken over one span.
    private const int PerBuffer = CommitSegment.BlockEntries * 6;

    private readonly IBlobWriter _writer;
    private readonly DatasetId _dataset;
    private readonly long _from;
    private readonly long _to;
    private readonly ushort _version;
    private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(PerBuffer * CommitEntry.Size);
    private readonly List<long> _fences = [];
    private readonly List<ulong> _checks = [];
    private CommitEntry _last;
    private long _added;
    private int _filled;

    private CommitIndexWriter(IBlobWriter writer, DatasetId dataset, long from, long to, ushort version)
    {
        _writer = writer;
        _dataset = dataset;
        _from = from;
        _to = to;
        _version = version;
    }

    /// <summary>
    /// Starts a blob in <paramref name="version"/>: 6, each block of 128
    /// entries checked (ADR 0213), unless the blob is a set's and a reader of
    /// version 4 is announced, which reads 4.
    /// </summary>
    internal static async ValueTask<CommitIndexWriter> StartAsync(
        IDerivedStore store, BlobName name, DatasetId dataset, long from, long to, CancellationToken cancellationToken, ushort version = DerivedFormat.CheckedVersion)
    {
        if (to <= from)
        {
            throw new ArgumentException("A commit index blob holds at least one entry.", nameof(to));
        }

        if (version is not (DerivedFormat.Version or DerivedFormat.CheckedVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "A commit index blob is written in derived format 4 or 6.");
        }

        return new CommitIndexWriter(await store.CreateAsync(name, cancellationToken).ConfigureAwait(false), dataset, from, to, version);
    }

    /// <summary>The next position's entry.</summary>
    internal async ValueTask AddAsync(CommitEntry entry, CancellationToken cancellationToken)
    {
        if (_added == _to - _from)
        {
            throw new InvalidOperationException("The commit index blob already holds every entry it was started for.");
        }

        if (_added % CommitSegment.BlockEntries == 0)
        {
            _fences.Add(entry.TimestampTicks);
        }

        entry.Write(_buffer.AsSpan(_filled * CommitEntry.Size, CommitEntry.Size));
        _last = entry;
        _added++;

        if (++_filled == PerBuffer)
        {
            await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Writes what is buffered, taking each block's check; only the last
    // write of a blob holds a block of fewer than 128 entries.
    private async ValueTask FlushBufferAsync(CancellationToken cancellationToken)
    {
        for (int at = 0; at < _filled; at += CommitSegment.BlockEntries)
        {
            int entries = Math.Min(CommitSegment.BlockEntries, _filled - at);
            _checks.Add(BlockCheck.Of(_buffer.AsSpan(at * CommitEntry.Size, entries * CommitEntry.Size)));
        }

        await _writer.WriteAsync(_buffer.AsMemory(0, _filled * CommitEntry.Size), cancellationToken).ConfigureAwait(false);
        _filled = 0;
    }

    /// <summary>Writes the directory and the header, and publishes the blob; it must hold every entry.</summary>
    internal async ValueTask PublishAsync(CancellationToken cancellationToken)
    {
        if (_added != _to - _from)
        {
            throw new InvalidOperationException("The commit index blob holds " + _added + " of its " + (_to - _from) + " entries.");
        }

        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        bool checks = _version >= DerivedFormat.CheckedVersion;
        byte[] directory = new byte[4 + 8 + (_fences.Count * (checks ? 16 : 8))];
        BinaryPrimitives.WriteUInt32LittleEndian(directory, CommitSegment.BlockEntries);
        BinaryPrimitives.WriteInt64LittleEndian(directory.AsSpan(4), _to - _from);

        for (int i = 0; i < _fences.Count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(directory.AsSpan(12 + (i * 8)), _fences[i]);

            if (checks)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(directory.AsSpan(12 + (_fences.Count * 8) + (i * 8)), _checks[i]);
            }
        }

        await _writer.WriteAsync(directory, cancellationToken).ConfigureAwait(false);
        byte[] header = DerivedFormat.EncodeHeader(
            _version, DerivedFormat.KindCommits, _dataset, _from, _to, _last.HeaderHash(), (_to - _from) * CommitEntry.Size, directory.Length, SHA256.HashData(directory));
        await _writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await _writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
