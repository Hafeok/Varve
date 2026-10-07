// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>Where a commit's first record is.</summary>
internal readonly struct CommitLocation
{
    internal CommitLocation(int segment, long offset)
    {
        Segment = segment;
        Offset = offset;
    }

    internal int Segment { get; }

    internal long Offset { get; }
}

/// <summary>A closed commit, read back and verified, body and all.</summary>
internal sealed class LoggedCommit
{
    internal LoggedCommit(CommitHeader header, byte[] headerHash, DecodedBody body, CommitLocation location)
    {
        Header = header;
        HeaderHash = headerHash;
        Allocations = body.Allocations;
        Asserted = body.Asserted;
        Retracted = body.Retracted;
        Location = location;
    }

    internal CommitHeader Header { get; }

    internal byte[] HeaderHash { get; }

    internal Allocation[] Allocations { get; }

    internal Quad[] Asserted { get; }

    internal Quad[] Retracted { get; }

    internal CommitLocation Location { get; }
}

/// <summary>
/// A closed commit as the scan found it: its header always, its body only
/// when the scan read bodies at its position.
/// </summary>
internal sealed class ScannedCommit
{
    internal ScannedCommit(CommitHeader header, byte[] headerHash, CommitLocation location, LoggedCommit? full, long bytes)
    {
        Bytes = bytes;
        Header = header;
        HeaderHash = headerHash;
        Location = location;
        Full = full;
    }

    internal CommitHeader Header { get; }

    internal byte[] HeaderHash { get; }

    internal CommitLocation Location { get; }

    /// <summary>The whole commit, when the scan read its body.</summary>
    internal LoggedCommit? Full { get; }

    /// <summary>The bytes of the commit's records, headers and bodies.</summary>
    internal long Bytes { get; }
}

/// <summary>What opening a log found.</summary>
internal sealed class LogScan
{
    /// <summary>Every closed commit, when the scan keeps them; null when they are handed to a callback instead.</summary>
    internal List<ScannedCommit>? Commits { get; init; } = [];

    /// <summary>Called with each closed commit as the scan passes it, when the scan does not keep them (ADR 0089).</summary>
    internal Func<ScannedCommit, ValueTask>? OnCommit { get; init; }

    /// <summary>Whether a torn or unclosed tail was ignored (ADR 0072).</summary>
    internal bool DiscardedTail { get; set; }

    internal IReadOnlyList<SegmentInfo> Segments { get; set; } = [];

    /// <summary>The ids of segments the walk passed over as beyond a copy point.</summary>
    internal List<int> Abandoned { get; } = [];

    /// <summary>The index in <see cref="Segments"/> of the last segment the chain ran through, or -1.</summary>
    internal int End { get; set; } = -1;

    /// <summary>Whether that segment ends with a trailer.</summary>
    internal bool EndSealed { get; set; }

    /// <summary>Whether that segment ends with bytes the walk discarded: torn, or an unclosed commit.</summary>
    internal bool EndHasTail { get; set; }

    internal long Head { get; private set; }

    internal byte[] HeadHash { get; private set; } = LogFormat.Genesis();

    internal async ValueTask AddAsync(ScannedCommit commit)
    {
        Head++;
        HeadHash = commit.HeaderHash;
        Commits?.Add(commit);

        if (OnCommit is not null)
        {
            await OnCommit(commit).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Reads the log's records back into commits and verifies them, walking the
/// chain across segments as <c>docs/spec/storage-format.md</c> §5 says.
/// </summary>
/// <remarks>
/// A store opens a log it can verify, or it does not open (ADR 0014). What is
/// torn — never completely written — is ignored, never parsed as data; what
/// verifies and breaks the chain refuses.
/// </remarks>
internal static class LogReader
{
    /// <summary>
    /// Walks the whole log. Commits at positions above <paramref name="bodiesAfter"/>
    /// are read whole and verified against their content hash; those at or
    /// below it are read header by header — the chain is still verified — so
    /// that opening reads the log since the newest checkpoint (ADR 0072).
    /// </summary>
    internal static ValueTask<LogScan> ScanAsync(ISegmentStore store, DatasetId dataset, long bodiesAfter, CancellationToken cancellationToken) =>
        ScanAsync(store, dataset, bodiesAfter, ScanRetains, null, cancellationToken);

    /// <summary>
    /// The scan, handing each closed commit to <paramref name="onCommit"/> as
    /// it passes rather than keeping them: what opening needs of a log of any
    /// length is then bounded by what the callback keeps (ADR 0089).
    /// </summary>
    internal static ValueTask<LogScan> ScanAsync(ISegmentStore store, DatasetId dataset, long bodiesAfter, Func<ScannedCommit, ValueTask> onCommit, CancellationToken cancellationToken) =>
        ScanAsync(store, dataset, bodiesAfter, ScanRetains, onCommit, cancellationToken);

    /// <summary>The scan, holding at most <paramref name="retain"/> bytes of any one commit's body.</summary>
    internal static ValueTask<LogScan> ScanAsync(ISegmentStore store, DatasetId dataset, long bodiesAfter, long retain, CancellationToken cancellationToken) =>
        ScanAsync(store, dataset, bodiesAfter, retain, null, cancellationToken);

    /// <summary>
    /// What a scan holds of a commit it reads whole, for replay: past this,
    /// the payloads are let go and the body is hashed as it passes, so a
    /// scan's memory is bounded by it and one record whatever the size of a
    /// commit — a bulk load's, closed or torn (ADR 0081). A commit let go is
    /// read again if replay needs it.
    /// </summary>
    internal const long ScanRetains = 64L << 20;

    private static async ValueTask<LogScan> ScanAsync(
        ISegmentStore store, DatasetId dataset, long bodiesAfter, long retain, Func<ScannedCommit, ValueTask>? onCommit, CancellationToken cancellationToken)
    {
        LogScan scan = new()
        {
            Segments = await store.ListSegmentsAsync(cancellationToken).ConfigureAwait(false),
            Commits = onCommit is null ? [] : null,
            OnCommit = onCommit,
        };
        Pending pending = new(retain);
        byte[] headHash = LogFormat.Genesis();
        bool openEnded = false;
        int previousId = -1;

        for (int s = 0; s < scan.Segments.Count; s++)
        {
            SegmentInfo info = scan.Segments[s];
            int id = info.Id.Value;
            long expected = scan.Head + 1;
            SegmentReader segment = await SegmentReader.OpenAsync(store, info, dataset, cancellationToken).ConfigureAwait(false);

            if (!openEnded && id != previousId + 1)
            {
                throw new LogVerificationException(expected, "Segment " + id + " follows segment " + previousId + "; the segments between them are missing.");
            }

            bool continues = segment.HeaderValid && segment.FirstPosition == expected && segment.Previous.AsSpan().SequenceEqual(headHash);

            if (openEnded && !continues)
            {
                if (segment.HeaderValid && segment.FirstPosition <= expected)
                {
                    throw new LogVerificationException(expected, "Segment " + id + " starts a different history at position " + segment.FirstPosition + ".");
                }

                if (!segment.HeaderValid)
                {
                    await RefuseIfWrittenAfterAsync(segment, 1, 0, expected, "after a segment header that does not verify", cancellationToken).ConfigureAwait(false);
                }

                // Written beyond the point at which this copy of the log was taken.
                scan.Abandoned.Add(id);
                continue;
            }

            if (segment.HeaderValid && !continues)
            {
                throw new LogVerificationException(expected, "Segment " + id + " does not continue the chain: it starts at position " + segment.FirstPosition + " where " + expected + " was due.");
            }

            previousId = id;
            scan.End = s;
            scan.EndSealed = segment.Trailer is not null;
            scan.EndHasTail = false;

            bool torn = !segment.HeaderValid;

            if (segment.HeaderValid)
            {
                long brokeAt = await ReadRecordsAsync(segment, scan, pending, bodiesAfter, headHash, cancellationToken).ConfigureAwait(false);
                headHash = scan.HeadHash;
                torn = brokeAt >= 0;

                // A sealed segment's trailer, changed: what is left after the
                // last record is this segment's trailer and does not verify.
                // A later segment exists only once the trailer was flushed, so
                // no crash leaves it so, and a copy takes it whole or as a prefix.
                if (torn && segment.Trailer is null && s < scan.Segments.Count - 1 && segment.RecordsEnd - brokeAt == LogFormat.TrailerLength)
                {
                    ReadOnlyMemory<byte> rest = await segment.ReadAsync(brokeAt, LogFormat.TrailerLength, cancellationToken).ConfigureAwait(false);

                    if (LogFormat.IsDamagedTrailer(rest.Span, dataset, id))
                    {
                        throw new LogVerificationException(expected, "The trailer of segment " + id + " does not verify, and a later segment follows it: the log is damaged, not torn.");
                    }
                }
            }
            else
            {
                await RefuseIfWrittenAfterAsync(segment, 1, 0, expected, "after a segment header that does not verify", cancellationToken).ConfigureAwait(false);
            }

            if (torn)
            {
                scan.DiscardedTail = true;
                scan.EndHasTail = true;
                pending.Clear();
            }

            if (segment.Trailer is { } trailer)
            {
                if (torn && trailer.Status == LogFormat.TrailerClosed)
                {
                    throw new LogVerificationException(scan.Head + 1, "Segment " + id + " is sealed closed and holds bytes that do not verify.");
                }

                if (trailer.Head != scan.Head || !trailer.HeadHash.AsSpan().SequenceEqual(headHash))
                {
                    throw new LogVerificationException(scan.Head + 1, "The trailer of segment " + id + " names position " + trailer.Head + " where the chain reaches " + scan.Head + ".");
                }

                if (trailer.Status == LogFormat.TrailerAbandoned && pending.Count > 0)
                {
                    scan.DiscardedTail = true;
                    pending.Clear();
                }

                openEnded = false;
            }
            else
            {
                openEnded = true;
            }
        }

        if (pending.Count > 0)
        {
            scan.DiscardedTail = true;
            scan.EndHasTail = true;
        }

        return scan;
    }

    // Reads one segment's records. Where they break, or -1 when they do not.
    private static async ValueTask<long> ReadRecordsAsync(
        SegmentReader segment, LogScan scan, Pending pending, long bodiesAfter, byte[] headHash, CancellationToken cancellationToken)
    {
        long at = LogFormat.SegmentHeaderLength;
        bool first = true;

        while (at < segment.RecordsEnd)
        {
            long expected = scan.Head + 1;

            if (segment.RecordsEnd - at < LogFormat.RecordHeaderLength)
            {
                return at;
            }

            ReadOnlyMemory<byte> headerBytes = await segment.ReadAsync(at, LogFormat.RecordHeaderLength, cancellationToken).ConfigureAwait(false);
            RecordHeader? record = LogFormat.TryReadRecordHeader(headerBytes.Span, expected);

            if (record is null || at + LogFormat.RecordHeaderLength + record.BodyLength > segment.RecordsEnd)
            {
                await RefuseIfWrittenAfterAsync(segment, at + 1, expected, expected, "after a record header at position " + expected + " that does not verify", cancellationToken).ConfigureAwait(false);
                return at;
            }

            if (record.Position != expected)
            {
                throw new LogVerificationException(expected, "A record claims position " + record.Position + " where " + expected + " was due.");
            }

            if (!record.Previous.AsSpan().SequenceEqual(headHash))
            {
                throw new LogVerificationException(expected, "The chain breaks at position " + expected + ": a record's prev is not the hash of the header before it.");
            }

            if (record.Index == 0)
            {
                if (pending.Count > 0 && !first)
                {
                    throw new LogVerificationException(expected, "Position " + expected + " restarts in the middle of a segment.");
                }

                // At a segment's start, a record 0 restarts a commit left pending
                // across a seal: recovery abandoned it and began again here.
                scan.DiscardedTail |= pending.Count > 0;
                pending.Start(record.Kind, new CommitLocation(segment.Id, at));
            }
            else if (record.Index != pending.Count || record.Kind != pending.Kind)
            {
                throw new LogVerificationException(expected, "Record " + record.Index + " of position " + expected + " is out of order.");
            }

            long bodyAt = at + LogFormat.RecordHeaderLength;
            bool full = expected > bodiesAfter;
            pending.Bytes += LogFormat.RecordHeaderLength + record.BodyLength;

            if (full)
            {
                ReadOnlyMemory<byte> body = await segment.ReadAsync(bodyAt, record.BodyLength, cancellationToken).ConfigureAwait(false);

                if (!LogFormat.HashMatches(body.Span, record.Content))
                {
                    await RefuseIfWrittenAfterAsync(segment, at + 1, expected, expected, "after a record at position " + expected + " whose body does not match its hash", cancellationToken).ConfigureAwait(false);
                    return at;
                }

                pending.Add(body);
            }
            else
            {
                pending.Skip();
            }

            if (record.Closing)
            {
                ReadOnlyMemory<byte> tail = full
                    ? pending.Last
                    : await ReadClosingTailAsync(segment, bodyAt, record.BodyLength, expected, cancellationToken).ConfigureAwait(false);
                await scan.AddAsync(Close(pending, tail, expected, headHash, full)).ConfigureAwait(false);
                headHash = scan.HeadHash;
                pending.Clear();
            }

            at = bodyAt + record.BodyLength;
            first = false;
        }

        return -1;
    }

    // Bytes that do not verify are a torn tail only when nothing written after
    // them, in the same file, verifies: a record of a later position than the
    // break belongs to (or, after a segment header that does not verify, any
    // record) was written after a flush that made the broken bytes durable, so
    // no crash leaves it, and a copy takes a file as a prefix (ADR 0072, I6).
    // A later segment cannot vouch for an earlier one, because a copy may take
    // them at different moments. Every offset is looked at, since a broken
    // length says nothing about where the next record starts; the position is
    // checked before the hash, so the look costs a hash only where one could be.
    private static async ValueTask RefuseIfWrittenAfterAsync(
        SegmentReader segment, long from, long above, long expected, string after, CancellationToken cancellationToken)
    {
        const int Window = 1 << 20;
        long end = segment.RecordsEnd;
        long most = expected + ((end - from) / LogFormat.RecordHeaderLength) + 1;

        for (long start = from; end - start >= LogFormat.RecordHeaderLength; start += Window)
        {
            long length = Math.Min(end - start, Window + LogFormat.RecordHeaderLength - 1);
            ReadOnlyMemory<byte> bytes = await segment.ReadAsync(start, length, cancellationToken).ConfigureAwait(false);
            int offsets = (int)Math.Min(Window, length - LogFormat.RecordHeaderLength + 1);

            for (int o = 0; o < offsets; o++)
            {
                ReadOnlySpan<byte> candidate = bytes.Span[o..];
                long position = (long)BinaryPrimitives.ReadUInt64LittleEndian(candidate[8..]);

                if (position > above && position <= most && LogFormat.IsRecordHeader(candidate, out _))
                {
                    throw new LogVerificationException(expected, "Segment " + segment.Id + " holds a record of position " + position + " " + after + ": the log is damaged, not torn.");
                }
            }
        }
    }

    // The closing record's commit header, read from the end of its body alone.
    private static async ValueTask<ReadOnlyMemory<byte>> ReadClosingTailAsync(SegmentReader segment, long bodyAt, uint bodyLength, long position, CancellationToken cancellationToken)
    {
        if (bodyLength < 4)
        {
            throw new LogVerificationException(position, "The closing record of position " + position + " is too short.");
        }

        ReadOnlyMemory<byte> lengthBytes = await segment.ReadAsync(bodyAt + bodyLength - 4, 4, cancellationToken).ConfigureAwait(false);
        uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes.Span);

        if (headerLength > bodyLength - 4)
        {
            throw new LogVerificationException(position, "The header of position " + position + " runs past its record.");
        }

        return await segment.ReadAsync(bodyAt + bodyLength - 4 - headerLength, headerLength + 4, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a closed commit's records end: the segment of its closing record
    /// and the offset just past it. Headers only; the chain is checked.
    /// </summary>
    internal static async ValueTask<CommitLocation> EndAsync(
        ISegmentStore store, DatasetId dataset, CommitLocation location, long position, byte[] previous, CancellationToken cancellationToken)
    {
        int segmentId = location.Segment;
        long offset = location.Offset;
        int index = 0;
        IReadOnlyList<SegmentInfo>? segments = null;

        while (true)
        {
            ReadOnlyMemory<byte> headerBytes = await store.ReadRangeAsync(new SegmentId(segmentId), new ByteOffset(offset), new ByteCount(LogFormat.RecordHeaderLength), cancellationToken).ConfigureAwait(false);
            RecordHeader? record = headerBytes.Length == LogFormat.RecordHeaderLength ? LogFormat.TryReadRecordHeader(headerBytes.Span, position) : null;

            if (record is null || record.Position != position || (index == 0 && record.Index != 0))
            {
                segments ??= await store.ListSegmentsAsync(cancellationToken).ConfigureAwait(false);
                segmentId = await NextContinuingAsync(store, dataset, segments, segmentId, position, previous, cancellationToken).ConfigureAwait(false);
                offset = LogFormat.SegmentHeaderLength;
                continue;
            }

            if (record.Index != index || !record.Previous.AsSpan().SequenceEqual(previous))
            {
                throw new LogVerificationException(position, "Position " + position + " no longer reads as it did when the log was opened.");
            }

            offset += LogFormat.RecordHeaderLength + record.BodyLength;
            index++;

            if (record.Closing)
            {
                return new CommitLocation(segmentId, offset);
            }
        }
    }

    /// <summary>Reads one closed commit, whole, from where its first record is, and verifies it.</summary>
    internal static async ValueTask<LoggedCommit> ReadAsync(
        ISegmentStore store, DatasetId dataset, CommitLocation location, long position, byte[] previous, CancellationToken cancellationToken)
    {
        Pending pending = new(long.MaxValue);
        int segmentId = location.Segment;
        long offset = location.Offset;
        IReadOnlyList<SegmentInfo>? segments = null;

        while (true)
        {
            ReadOnlyMemory<byte> headerBytes = await store.ReadRangeAsync(new SegmentId(segmentId), new ByteOffset(offset), new ByteCount(LogFormat.RecordHeaderLength), cancellationToken).ConfigureAwait(false);
            RecordHeader? record = headerBytes.Length == LogFormat.RecordHeaderLength ? LogFormat.TryReadRecordHeader(headerBytes.Span, position) : null;

            if (record is null || record.Position != position || (pending.Count == 0 && record.Index != 0))
            {
                // The commit continues in the next segment that continues the chain
                // here — past any segment abandoned as beyond a copy point.
                segments ??= await store.ListSegmentsAsync(cancellationToken).ConfigureAwait(false);
                segmentId = await NextContinuingAsync(store, dataset, segments, segmentId, position, previous, cancellationToken).ConfigureAwait(false);
                offset = LogFormat.SegmentHeaderLength;
                continue;
            }

            if (record.Index != pending.Count || !record.Previous.AsSpan().SequenceEqual(previous))
            {
                throw new LogVerificationException(position, "Position " + position + " no longer reads as it did when the log was opened.");
            }

            if (record.Index == 0)
            {
                pending.Start(record.Kind, new CommitLocation(segmentId, offset));
            }

            ReadOnlyMemory<byte> body = await store.ReadRangeAsync(
                new SegmentId(segmentId), new ByteOffset(offset + LogFormat.RecordHeaderLength), new ByteCount(record.BodyLength), cancellationToken).ConfigureAwait(false);

            if (!LogFormat.HashMatches(body.Span, record.Content))
            {
                throw new LogVerificationException(position, "A record of position " + position + " does not match its hash.");
            }

            pending.Add(body);
            offset += LogFormat.RecordHeaderLength + record.BodyLength;

            if (record.Closing)
            {
                return Close(pending, pending.Last, position, previous, full: true).Full!;
            }
        }
    }

    private static async ValueTask<int> NextContinuingAsync(
        ISegmentStore store, DatasetId dataset, IReadOnlyList<SegmentInfo> segments, int after, long position, byte[] previous, CancellationToken cancellationToken)
    {
        foreach (SegmentInfo info in segments)
        {
            if (info.Id.Value <= after)
            {
                continue;
            }

            ReadOnlyMemory<byte> header = await store.ReadRangeAsync(info.Id, new ByteOffset(0), new ByteCount(LogFormat.SegmentHeaderLength), cancellationToken).ConfigureAwait(false);

            if (LogFormat.TryDecodeSegmentHeader(header.Span, dataset, info.Id.Value, out long first, out byte[] prev)
                && first == position
                && prev.AsSpan().SequenceEqual(previous))
            {
                return info.Id.Value;
            }
        }

        throw new LogVerificationException(position, "The records of position " + position + " end before the commit closes.");
    }

    private static ScannedCommit Close(Pending pending, ReadOnlyMemory<byte> closingTail, long position, byte[] previous, bool full)
    {
        ReadOnlySpan<byte> tail = closingTail.Span;

        if (tail.Length < 4)
        {
            throw new LogVerificationException(position, "The closing record of position " + position + " is too short.");
        }

        uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(tail[^4..]);

        if (headerLength > (uint)(tail.Length - 4))
        {
            throw new LogVerificationException(position, "The header of position " + position + " runs past its record.");
        }

        int headerStart = tail.Length - 4 - (int)headerLength;
        ReadOnlySpan<byte> headerBytes = tail.Slice(headerStart, (int)headerLength);
        byte[] headerHash = SHA256.HashData(headerBytes);
        CommitHeader header = LogFormat.DecodeHeader(headerBytes, position);

        if (header.Position != position || header.Kind != pending.Kind)
        {
            throw new LogVerificationException(position, "The header of position " + position + " disagrees with its records.");
        }

        if (!header.Previous.AsSpan().SequenceEqual(previous))
        {
            throw new LogVerificationException(position, "The chain breaks at position " + position + ": prev is not the hash of the header before it.");
        }

        if (!full)
        {
            return new ScannedCommit(header, headerHash, pending.Location, null, pending.Bytes);
        }

        if (!pending.BodyHash(headerStart).AsSpan().SequenceEqual(header.Content))
        {
            throw new LogVerificationException(position, "The body of position " + position + " does not match its content hash.");
        }

        // Verified as it passed; too large to hold, it is read again if replay needs it.
        if (!pending.Whole)
        {
            return new ScannedCommit(header, headerHash, pending.Location, null, pending.Bytes);
        }

        byte[] body = pending.Body(headerStart);

        LoggedCommit commit = new(header, headerHash, LogFormat.DecodeBody(body, position), pending.Location);
        return new ScannedCommit(header, headerHash, pending.Location, commit, pending.Bytes);
    }

    /// <summary>One segment's header and trailer, and reads of what lies between.</summary>
    private sealed class SegmentReader
    {
        private readonly ISegmentStore _store;
        private readonly SegmentId _id;

        private SegmentReader(ISegmentStore store, SegmentId id) =>
            (_store, _id) = (store, id);

        internal int Id => _id.Value;

        internal bool HeaderValid { get; private set; }

        internal long FirstPosition { get; private set; }

        internal byte[] Previous { get; private set; } = [];

        internal Trailer? Trailer { get; private set; }

        /// <summary>Where the records end: the trailer, or the end of the segment.</summary>
        internal long RecordsEnd { get; private set; }

        internal static async ValueTask<SegmentReader> OpenAsync(ISegmentStore store, SegmentInfo info, DatasetId dataset, CancellationToken cancellationToken)
        {
            SegmentReader reader = new(store, info.Id);
            long length = info.Length.Value;
            reader.RecordsEnd = length;

            if (length >= LogFormat.TrailerLength)
            {
                ReadOnlyMemory<byte> end = await reader.ReadAsync(length - LogFormat.TrailerLength, LogFormat.TrailerLength, cancellationToken).ConfigureAwait(false);

                if (LogFormat.TryDecodeTrailer(end.Span, dataset, info.Id.Value, out byte status, out long head, out byte[] hash))
                {
                    reader.Trailer = new Trailer(status, head, hash);
                    reader.RecordsEnd = length - LogFormat.TrailerLength;
                }
            }

            if (reader.RecordsEnd >= LogFormat.SegmentHeaderLength)
            {
                ReadOnlyMemory<byte> header = await reader.ReadAsync(0, LogFormat.SegmentHeaderLength, cancellationToken).ConfigureAwait(false);
                reader.HeaderValid = LogFormat.TryDecodeSegmentHeader(header.Span, dataset, info.Id.Value, out long first, out byte[] previous);
                reader.FirstPosition = first;
                reader.Previous = previous;
            }

            return reader;
        }

        internal async ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, long length, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> bytes = await _store.ReadRangeAsync(_id, new ByteOffset(offset), new ByteCount(length), cancellationToken).ConfigureAwait(false);

            if (bytes.Length != length)
            {
                throw new LogVerificationException(0, "Segment " + _id + " is shorter than it was listed.");
            }

            return bytes;
        }
    }

    private sealed record Trailer(byte Status, long Head, byte[] HeadHash);

    private sealed class Pending(long retain)
    {
        private readonly List<ReadOnlyMemory<byte>> _payloads = [];
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _held;
        private bool _dropped;

        internal int Count { get; private set; }

        internal CommitKind Kind { get; private set; }

        internal CommitLocation Location { get; private set; }

        internal long Bytes { get; set; }

        internal ReadOnlyMemory<byte> Last { get; private set; }

        /// <summary>Whether every payload is still held, so the body can be built.</summary>
        internal bool Whole => !_dropped;

        internal void Start(CommitKind kind, CommitLocation location)
        {
            Clear();
            Kind = kind;
            Location = location;
        }

        internal void Add(ReadOnlyMemory<byte> payload)
        {
            if (Count > 0 && !Last.IsEmpty)
            {
                _hash.AppendData(Last.Span);
            }

            Last = payload;
            _held += payload.Length;

            if (!_dropped && _held > retain)
            {
                _dropped = true;
                _payloads.Clear();
            }

            if (!_dropped)
            {
                _payloads.Add(payload);
            }

            Count++;
        }

        /// <summary>A record counted but not read: its commit is below the bodies the scan reads.</summary>
        internal void Skip() => Count++;

        internal void Clear()
        {
            _payloads.Clear();
            _hash.GetHashAndReset();
            Last = default;
            _held = 0;
            _dropped = false;
            Count = 0;
            Bytes = 0;
        }

        /// <summary>The body's hash: every payload, the last one up to where its header starts.</summary>
        internal byte[] BodyHash(int lastBodyLength)
        {
            _hash.AppendData(Last.Span[..lastBodyLength]);
            return _hash.GetHashAndReset();
        }

        /// <summary>The body: every payload, the last one up to where its header starts.</summary>
        internal byte[] Body(int lastBodyLength)
        {
            int length = lastBodyLength;

            for (int i = 0; i < _payloads.Count - 1; i++)
            {
                length += _payloads[i].Length;
            }

            byte[] body = new byte[length];
            int at = 0;

            for (int i = 0; i < _payloads.Count - 1; i++)
            {
                _payloads[i].Span.CopyTo(body.AsSpan(at));
                at += _payloads[i].Length;
            }

            _payloads[^1].Span[..lastBodyLength].CopyTo(body.AsSpan(at));
            return body;
        }
    }
}

/// <summary>
/// Appends commits as records: the body split at the record limit, the commit
/// header in the closing record, a sealed segment and a new one when the
/// active one would pass the segment size (ADRs 0013, 0040, 0072).
/// </summary>
internal sealed class LogWriter
{
    private readonly ISegmentStore _store;
    private readonly DatasetId _dataset;
    private readonly long _segmentBytes;
    private readonly int _maxRecordBytes;
    private int _active;
    private long _activeLength;

    private LogWriter(ISegmentStore store, DatasetId dataset, long segmentBytes, int maxRecordBytes, int active, long activeLength)
    {
        _store = store;
        _dataset = dataset;
        _segmentBytes = segmentBytes;
        _maxRecordBytes = maxRecordBytes;
        _active = active;
        _activeLength = activeLength;
    }

    /// <summary>
    /// A writer that continues the log, after recovery (storage format §5):
    /// a newest segment holding a discarded tail is given an abandoned trailer
    /// and sealed, a newest segment the walk did not reach is sealed, and the
    /// next commit starts a new segment. Nothing is rewritten.
    /// </summary>
    internal static async ValueTask<LogWriter> OpenAsync(
        ISegmentStore store, DatasetId dataset, LogScan scan, long segmentBytes, int maxRecordBytes, CancellationToken cancellationToken)
    {
        int active = -1;
        long length = 0;

        if (scan.Segments.Count > 0)
        {
            SegmentInfo newest = scan.Segments[^1];
            bool endIsNewest = scan.End == scan.Segments.Count - 1;

            if (endIsNewest && !scan.EndSealed && !scan.EndHasTail && !newest.IsSealed)
            {
                active = newest.Id.Value;
                length = newest.Length.Value;
            }
            else if (!newest.IsSealed)
            {
                if (endIsNewest && !scan.EndSealed)
                {
                    byte[] trailer = LogFormat.EncodeTrailer(dataset, newest.Id.Value, LogFormat.TrailerAbandoned, scan.Head, scan.HeadHash);
                    await store.AppendAsync(newest.Id, trailer, cancellationToken).ConfigureAwait(false);
                    await store.FlushAsync(newest.Id, cancellationToken).ConfigureAwait(false);
                }

                await store.SealAsync(newest.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return new LogWriter(store, dataset, segmentBytes, maxRecordBytes, active, length);
    }

    /// <summary>The bytes of the last commit's records, headers and bodies.</summary>
    internal long LastCommitBytes { get; private set; }

    /// <summary>
    /// Appends one commit's records and makes them durable. <paramref name="previous"/>
    /// is the header hash of the commit before it. Returns where it starts.
    /// </summary>
    internal async ValueTask<CommitLocation> AppendAsync(
        CommitKind kind, long position, byte[] previous, byte[] body, byte[] header, CancellationToken cancellationToken)
    {
        int closingExtra = header.Length + 4;
        LastCommitBytes = 0;
        int bodyOffset = 0;
        int index = 0;
        CommitLocation? start = null;

        while (true)
        {
            int take = Math.Min(body.Length - bodyOffset, _maxRecordBytes);
            bool closing = bodyOffset + take == body.Length;
            int payloadLength = take + (closing ? closingExtra : 0);
            byte[] record = new byte[LogFormat.RecordHeaderLength + payloadLength];
            Span<byte> payload = record.AsSpan(LogFormat.RecordHeaderLength);

            body.AsSpan(bodyOffset, take).CopyTo(payload);

            if (closing)
            {
                header.CopyTo(payload[take..]);
                BinaryPrimitives.WriteUInt32LittleEndian(payload[(take + header.Length)..], (uint)header.Length);
            }

            LogFormat.WriteRecordHeader(record, payload, closing, kind, position, index, previous);
            await EnsureRoomAsync(record.Length, position, previous, cancellationToken).ConfigureAwait(false);
            start ??= new CommitLocation(_active, _activeLength);
            await _store.AppendAsync(new SegmentId(_active), record, cancellationToken).ConfigureAwait(false);
            _activeLength += record.Length;
            LastCommitBytes += record.Length;
            bodyOffset += take;
            index++;

            if (closing)
            {
                break;
            }
        }

        // Every record before the closing one is durable no later than it is:
        // one flush after the last append orders them (ADR 0013).
        await _store.FlushAsync(new SegmentId(_active), cancellationToken).ConfigureAwait(false);
        return start.Value;
    }

    /// <summary>
    /// Appends one commit whose body arrives in parts, never whole: records of
    /// at most the record limit are cut from the parts as they come, and the
    /// closing record carries the rest and the commit header. One flush, after
    /// the closing record (ADR 0013, ADR 0076). Returns where it starts.
    /// </summary>
    internal async ValueTask<CommitLocation> AppendStreamAsync(
        CommitKind kind, long position, byte[] previous, IAsyncEnumerable<ReadOnlyMemory<byte>> body, byte[] header, CancellationToken cancellationToken)
    {
        LastCommitBytes = 0;
        byte[] payload = new byte[_maxRecordBytes + header.Length + 4];
        int filled = 0;
        int index = 0;
        CommitLocation? start = null;

        async ValueTask EmitAsync(bool closing)
        {
            int length = filled;

            if (closing)
            {
                header.CopyTo(payload.AsSpan(length));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(length + header.Length), (uint)header.Length);
                length += header.Length + 4;
            }

            byte[] record = new byte[LogFormat.RecordHeaderLength + length];
            payload.AsSpan(0, length).CopyTo(record.AsSpan(LogFormat.RecordHeaderLength));
            LogFormat.WriteRecordHeader(record, record.AsSpan(LogFormat.RecordHeaderLength), closing, kind, position, index, previous);
            await EnsureRoomAsync(record.Length, position, previous, cancellationToken).ConfigureAwait(false);
            start ??= new CommitLocation(_active, _activeLength);
            await _store.AppendAsync(new SegmentId(_active), record, cancellationToken).ConfigureAwait(false);
            _activeLength += record.Length;
            LastCommitBytes += record.Length;
            index++;
            filled = 0;
        }

        await foreach (ReadOnlyMemory<byte> part in body.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            ReadOnlyMemory<byte> rest = part;

            while (!rest.IsEmpty)
            {
                if (filled == _maxRecordBytes)
                {
                    await EmitAsync(closing: false).ConfigureAwait(false);
                }

                int take = Math.Min(rest.Length, _maxRecordBytes - filled);
                rest.Span[..take].CopyTo(payload.AsSpan(filled));
                filled += take;
                rest = rest[take..];
            }
        }

        await EmitAsync(closing: true).ConfigureAwait(false);
        await _store.FlushAsync(new SegmentId(_active), cancellationToken).ConfigureAwait(false);
        return start!.Value;
    }

    // Seals the active segment, with a closed trailer naming the readable head,
    // and starts the next, whose header continues from it.
    private async ValueTask EnsureRoomAsync(int recordLength, long position, byte[] previous, CancellationToken cancellationToken)
    {
        if (_active >= 0
            && (_activeLength + recordLength + LogFormat.TrailerLength <= _segmentBytes || _activeLength == LogFormat.SegmentHeaderLength))
        {
            return;
        }

        if (_active >= 0)
        {
            // The records already in the segment are flushed before the trailer
            // that vouches for them is written: in one flush window a power loss
            // can keep the trailer and tear a record before it, and a closed
            // trailer over torn bytes reads as damage (found by the power-loss
            // suite, seeds 6201 and 6202, operation 21).
            SegmentId sealing = new(_active);
            await _store.FlushAsync(sealing, cancellationToken).ConfigureAwait(false);
            byte[] trailer = LogFormat.EncodeTrailer(_dataset, _active, LogFormat.TrailerClosed, position - 1, previous);
            await _store.AppendAsync(sealing, trailer, cancellationToken).ConfigureAwait(false);
            await _store.FlushAsync(sealing, cancellationToken).ConfigureAwait(false);
            await _store.SealAsync(sealing, cancellationToken).ConfigureAwait(false);
        }

        _active = (await _store.CreateSegmentAsync(cancellationToken).ConfigureAwait(false)).Value;
        byte[] header = LogFormat.EncodeSegmentHeader(_dataset, _active, position, previous);
        await _store.AppendAsync(new SegmentId(_active), header, cancellationToken).ConfigureAwait(false);
        await _store.FlushAsync(new SegmentId(_active), cancellationToken).ConfigureAwait(false);
        _activeLength = LogFormat.SegmentHeaderLength;
    }
}
