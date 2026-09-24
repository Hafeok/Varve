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

/// <summary>A closed commit, read back and verified.</summary>
internal sealed class LoggedCommit
{
    internal LoggedCommit(CommitHeader header, byte[] headerHash, Allocation[] allocations, Quad[] asserted, Quad[] retracted, CommitLocation location)
    {
        Header = header;
        HeaderHash = headerHash;
        Allocations = allocations;
        Asserted = asserted;
        Retracted = retracted;
        Location = location;
    }

    internal CommitHeader Header { get; }

    internal byte[] HeaderHash { get; }

    internal Allocation[] Allocations { get; }

    internal Quad[] Asserted { get; }

    internal Quad[] Retracted { get; }

    internal CommitLocation Location { get; }
}

/// <summary>What opening a log found.</summary>
internal sealed class LogScan
{
    internal List<LoggedCommit> Commits { get; } = [];

    /// <summary>Whether a torn or unclosed tail was ignored (ADR 0013).</summary>
    internal bool DiscardedTail { get; set; }

    internal IReadOnlyList<SegmentInfo> Segments { get; set; } = [];
}

/// <summary>
/// Reads the log's records back into commits and verifies them: the chain, the
/// content hashes, the stored header hashes, and record order (ADR 0045).
/// </summary>
/// <remarks>
/// A torn record — one whose length runs past its segment — and the records of
/// a commit that never closed are ignored, never parsed as data. Anything else
/// out of order refuses: a store opens a log it can verify, or it does not open
/// (ADR 0014).
/// </remarks>
internal static class LogReader
{
    internal static async ValueTask<LogScan> ScanAsync(ISegmentStore store, CancellationToken cancellationToken)
    {
        LogScan scan = new() { Segments = await store.ListSegmentsAsync(cancellationToken).ConfigureAwait(false) };
        Pending pending = new();
        byte[] previous = LogFormat.Genesis;

        for (int s = 0; s < scan.Segments.Count; s++)
        {
            SegmentInfo segment = scan.Segments[s];
            ReadOnlyMemory<byte> bytes = await ReadAllAsync(store, segment, cancellationToken).ConfigureAwait(false);
            bool last = s == scan.Segments.Count - 1;

            if (bytes.Length < LogFormat.PreambleLength)
            {
                // A segment created and cut before its preamble was complete. It is
                // torn, like a cut record: ignored, and anything pending was cut
                // with it. Recovery seals it and moves on, so it need not be last.
                if (!LogFormat.Preamble.StartsWith(bytes.Span))
                {
                    throw new LogVerificationException(scan.Commits.Count + 1, "Segment " + segment.Id + " is too short to be a segment.");
                }

                scan.DiscardedTail |= bytes.Length > 0 || pending.Count > 0 || !last;
                pending.Clear();
                continue;
            }

            if (!bytes.Span[..LogFormat.PreambleLength].SequenceEqual(LogFormat.Preamble))
            {
                throw new LogVerificationException(scan.Commits.Count + 1, "Segment " + segment.Id + " does not begin with a version 0 preamble.");
            }

            int at = LogFormat.PreambleLength;

            while (at < bytes.Length)
            {
                ReadOnlySpan<byte> span = bytes.Span;

                if (bytes.Length - at < LogFormat.RecordHeaderLength)
                {
                    break;
                }

                int payloadLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(span[at..]), int.MaxValue);

                if (payloadLength > bytes.Length - at - LogFormat.RecordHeaderLength)
                {
                    break;
                }

                byte flags = span[at + 4];
                CommitKind kind = (CommitKind)span[at + 5];
                long position = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 8)..]);
                uint index = BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 16)..]);
                long expected = scan.Commits.Count + 1;

                if ((flags & ~LogFormat.ClosingFlag) != 0 || span[at + 6] != 0 || span[at + 7] != 0 || kind > CommitKind.Settings)
                {
                    throw new LogVerificationException(expected, "A record header at position " + expected + " has unknown flags, kind or reserved bits.");
                }

                if (position != expected)
                {
                    throw new LogVerificationException(expected, "A record claims position " + position + " where " + expected + " was due.");
                }

                if (index == 0)
                {
                    // A restart abandons whatever was pending for this position.
                    scan.DiscardedTail |= pending.Count > 0;
                    pending.Start(kind, new CommitLocation(segment.Id, at));
                }
                else if (index != pending.Count || kind != pending.Kind)
                {
                    throw new LogVerificationException(expected, "Record " + index + " of position " + expected + " is out of order.");
                }

                pending.Add(bytes.Slice(at + LogFormat.RecordHeaderLength, payloadLength));
                at += LogFormat.RecordHeaderLength + payloadLength;

                if ((flags & LogFormat.ClosingFlag) != 0)
                {
                    LoggedCommit commit = Close(pending, expected, previous);
                    scan.Commits.Add(commit);
                    previous = commit.HeaderHash;
                    pending.Clear();
                }
            }

            if (at < bytes.Length)
            {
                // Torn: the rest of this segment is not a record. Whatever was
                // pending was cut with it.
                scan.DiscardedTail = true;
                pending.Clear();
            }
        }

        if (pending.Count > 0)
        {
            scan.DiscardedTail = true;
        }

        return scan;
    }

    /// <summary>Reads one closed commit from where its first record is.</summary>
    internal static async ValueTask<LoggedCommit> ReadAsync(
        ISegmentStore store, CommitLocation location, long position, byte[] previous, CancellationToken cancellationToken)
    {
        Pending pending = new();
        int segment = location.Segment;
        long offset = location.Offset;

        while (true)
        {
            ReadOnlyMemory<byte> header = await store.ReadRangeAsync(segment, offset, LogFormat.RecordHeaderLength, cancellationToken).ConfigureAwait(false);

            if (header.Length < LogFormat.RecordHeaderLength)
            {
                // A commit's records continue in the next segment, after its preamble.
                segment++;
                offset = LogFormat.PreambleLength;
                continue;
            }

            ReadOnlySpan<byte> span = header.Span;
            int payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
            bool closing = (span[4] & LogFormat.ClosingFlag) != 0;
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);

            if (index == 0)
            {
                pending.Start((CommitKind)span[5], new CommitLocation(segment, offset));
            }

            ReadOnlyMemory<byte> payload = await store.ReadRangeAsync(
                segment, offset + LogFormat.RecordHeaderLength, payloadLength, cancellationToken).ConfigureAwait(false);
            pending.Add(payload);
            offset += LogFormat.RecordHeaderLength + payloadLength;

            if (closing)
            {
                return Close(pending, position, previous);
            }
        }
    }

    private static LoggedCommit Close(Pending pending, long position, byte[] previous)
    {
        ReadOnlyMemory<byte> last = pending.Last;

        if (last.Length < 4 + LogFormat.HashLength)
        {
            throw new LogVerificationException(position, "The closing record of position " + position + " is too short.");
        }

        ReadOnlySpan<byte> tail = last.Span;
        ReadOnlySpan<byte> storedHash = tail[^LogFormat.HashLength..];
        uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(tail[^(LogFormat.HashLength + 4)..]);

        if (headerLength > (uint)(tail.Length - 4 - LogFormat.HashLength))
        {
            throw new LogVerificationException(position, "The header of position " + position + " runs past its record.");
        }

        int headerStart = tail.Length - LogFormat.HashLength - 4 - (int)headerLength;
        ReadOnlySpan<byte> headerBytes = tail.Slice(headerStart, (int)headerLength);
        byte[] headerHash = SHA256.HashData(headerBytes);

        if (!headerHash.AsSpan().SequenceEqual(storedHash))
        {
            throw new LogVerificationException(position, "The header of position " + position + " does not match its stored hash.");
        }

        CommitHeader header = LogFormat.DecodeHeader(headerBytes, position);

        if (header.Position != position || header.Kind != pending.Kind)
        {
            throw new LogVerificationException(position, "The header of position " + position + " disagrees with its records.");
        }

        if (!header.Previous.AsSpan().SequenceEqual(previous))
        {
            throw new LogVerificationException(position, "The chain breaks at position " + position + ": prev is not the hash of the header before it.");
        }

        byte[] body = pending.Body(headerStart);

        if (!SHA256.HashData(body).AsSpan().SequenceEqual(header.Content))
        {
            throw new LogVerificationException(position, "The body of position " + position + " does not match its content hash.");
        }

        (Allocation[] allocations, Quad[] asserted, Quad[] retracted) = LogFormat.DecodeBody(body, position);
        return new LoggedCommit(header, headerHash, allocations, asserted, retracted, pending.Location);
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadAllAsync(ISegmentStore store, SegmentInfo segment, CancellationToken cancellationToken)
    {
        if (segment.Length > int.MaxValue)
        {
            throw new LogVerificationException(0, "Segment " + segment.Id + " is larger than one read can hold.");
        }

        return await store.ReadRangeAsync(segment.Id, 0, (int)segment.Length, cancellationToken).ConfigureAwait(false);
    }

    private sealed class Pending
    {
        private readonly List<ReadOnlyMemory<byte>> _payloads = [];

        internal int Count => _payloads.Count;

        internal CommitKind Kind { get; private set; }

        internal CommitLocation Location { get; private set; }

        internal ReadOnlyMemory<byte> Last => _payloads[^1];

        internal void Start(CommitKind kind, CommitLocation location)
        {
            _payloads.Clear();
            Kind = kind;
            Location = location;
        }

        internal void Add(ReadOnlyMemory<byte> payload) => _payloads.Add(payload);

        internal void Clear() => _payloads.Clear();

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
/// Appends commits as records: the body split at the record limit, the header
/// and its hash in the closing record, a new segment when the active one would
/// pass the segment size (ADRs 0013, 0040, 0045).
/// </summary>
internal sealed class LogWriter
{
    private readonly ISegmentStore _store;
    private readonly long _segmentBytes;
    private readonly int _maxRecordBytes;
    private int _active;
    private long _activeLength;

    private LogWriter(ISegmentStore store, long segmentBytes, int maxRecordBytes, int active, long activeLength)
    {
        _store = store;
        _segmentBytes = segmentBytes;
        _maxRecordBytes = maxRecordBytes;
        _active = active;
        _activeLength = activeLength;
    }

    /// <summary>
    /// A writer that continues the log. When recovery ignored a tail, the
    /// segment holding it is sealed and the next commit starts a new one: the
    /// contract has no truncate, so the ignored bytes stay where they are.
    /// </summary>
    internal static async ValueTask<LogWriter> OpenAsync(
        ISegmentStore store, LogScan scan, long segmentBytes, int maxRecordBytes, CancellationToken cancellationToken)
    {
        int active = -1;
        long length = 0;

        if (scan.Segments.Count > 0)
        {
            SegmentInfo newest = scan.Segments[^1];

            if (scan.DiscardedTail || newest.IsSealed || newest.Length < LogFormat.PreambleLength)
            {
                if (!newest.IsSealed)
                {
                    await store.SealAsync(newest.Id, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                active = newest.Id;
                length = newest.Length;
            }
        }

        return new LogWriter(store, segmentBytes, maxRecordBytes, active, length);
    }

    /// <summary>Appends one commit's records and makes them durable. Returns where it starts.</summary>
    internal async ValueTask<CommitLocation> AppendAsync(
        CommitKind kind, long position, byte[] body, byte[] header, byte[] headerHash, CancellationToken cancellationToken)
    {
        int closingExtra = header.Length + 4 + LogFormat.HashLength;
        int bodyOffset = 0;
        int index = 0;
        CommitLocation? start = null;

        while (true)
        {
            int take = Math.Min(body.Length - bodyOffset, _maxRecordBytes);
            bool closing = bodyOffset + take == body.Length;
            int payloadLength = take + (closing ? closingExtra : 0);
            byte[] record = new byte[LogFormat.RecordHeaderLength + payloadLength];

            LogFormat.WriteRecordHeader(record, payloadLength, closing, kind, position, index);
            body.AsSpan(bodyOffset, take).CopyTo(record.AsSpan(LogFormat.RecordHeaderLength));

            if (closing)
            {
                Span<byte> tail = record.AsSpan(LogFormat.RecordHeaderLength + take);
                header.CopyTo(tail);
                BinaryPrimitives.WriteUInt32LittleEndian(tail[header.Length..], (uint)header.Length);
                headerHash.CopyTo(tail[(header.Length + 4)..]);
            }

            await EnsureRoomAsync(record.Length, cancellationToken).ConfigureAwait(false);
            start ??= new CommitLocation(_active, _activeLength);
            await _store.AppendAsync(_active, record, cancellationToken).ConfigureAwait(false);
            _activeLength += record.Length;
            bodyOffset += take;
            index++;

            if (closing)
            {
                break;
            }
        }

        // Every record before the closing one is durable no later than it is:
        // one flush after the last append orders them (ADR 0013).
        await _store.FlushAsync(_active, cancellationToken).ConfigureAwait(false);
        return start.Value;
    }

    private async ValueTask EnsureRoomAsync(int recordLength, CancellationToken cancellationToken)
    {
        if (_active >= 0 && (_activeLength + recordLength <= _segmentBytes || _activeLength == LogFormat.PreambleLength))
        {
            return;
        }

        if (_active >= 0)
        {
            await _store.FlushAsync(_active, cancellationToken).ConfigureAwait(false);
            await _store.SealAsync(_active, cancellationToken).ConfigureAwait(false);
        }

        _active = await _store.CreateSegmentAsync(cancellationToken).ConfigureAwait(false);
        await _store.AppendAsync(_active, LogFormat.Preamble.ToArray(), cancellationToken).ConfigureAwait(false);
        _activeLength = LogFormat.PreambleLength;
    }
}
