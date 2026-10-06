// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Browser.Model;
using Varve.Store.Log;

namespace Varve.Store.Browser;

/// <summary>
/// Storage in one IndexedDB database per dataset, for where synchronous
/// access handles do not exist (ADR 0084). Declares
/// <see cref="Durability.Committed"/>: a flush returns when a
/// <c>durability: 'strict'</c> transaction has completed.
/// </summary>
/// <remarks>
/// <para>
/// Three object stores. <c>chunks</c> holds a segment's bytes, one record per
/// flush, keyed by segment and offset (ADR 0018's "one IndexedDB record per
/// chunk"); <c>meta</c> holds each segment's length and seal, written in the
/// same transaction as its chunk, and the manifest; <c>blobs</c> holds each
/// derived blob whole, so a put is the atomic replacement.
/// </para>
/// <para>
/// **The synchronous read is served from memory.** IndexedDB has no
/// synchronous read, so opening a blob reads it whole into an array, and every
/// read after that is a copy from it (ADR 0071 lets an open fetch). An open
/// blob costs its length in managed memory for as long as it is open; that is
/// this backend's bound, and why it is the fallback. Bytes appended and not
/// yet flushed are held in memory too, until the flush the store makes after
/// each commit.
/// </para>
/// </remarks>
internal sealed class IndexedDbStorage : BrowserStorage
{
    private readonly JSObject _database;
    private readonly JSObject _lease;
    private readonly IndexedDbSegmentStore _log;
    private readonly IndexedDbDerivedStore _derived;

    private IndexedDbStorage(BrowserDatasetName name, JSObject database, JSObject lease, List<Segment> segments)
        : base(name)
    {
        _database = database;
        _lease = lease;
        _log = new IndexedDbSegmentStore(database, segments);
        _derived = new IndexedDbDerivedStore(database);
    }

    public override BrowserBackend Backend => BrowserBackend.IndexedDb;

    public override ISegmentStore Log => _log;

    public override IDerivedStore Derived => _derived;

    public static async ValueTask<IndexedDbStorage> OpenHereAsync(BrowserDatasetName name, CancellationToken cancellationToken)
    {
        JSObject lease = await Interop.LockAcquire("varve/" + name.Value).ConfigureAwait(false)
            ?? throw new DatasetLeasedException(
                "The dataset '" + name + "' is open elsewhere in this origin. One opener at a time: the lease is a Web Lock named after the dataset, which the browser releases when the page or worker that holds it closes it or ends.");

        try
        {
            JSObject database = await Interop.IdbOpen("varve/" + name.Value).ConfigureAwait(false);
            List<Segment> segments = [];

            foreach (string line in Lines(await Interop.IdbSegments(database).ConfigureAwait(false)))
            {
                string[] fields = line.Split(',');
                segments.Add(new Segment(
                    int.Parse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture),
                    long.Parse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture),
                    fields[2] == "1"));
            }

            segments.Sort(static (left, right) => left.Id.CompareTo(right.Id));
            return new IndexedDbStorage(name, database, lease, segments);
        }
        catch
        {
            Interop.LockRelease(lease);
            throw;
        }
    }

    public override ValueTask DisposeAsync()
    {
        _log.Dispose();
        Interop.IdbClose(_database);
        _database.Dispose();
        Interop.LockRelease(_lease);
        _lease.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Split('\n');

    private sealed class Segment(int id, long persisted, bool isSealed)
    {
        public int Id { get; } = id;

        /// <summary>Bytes in committed chunks.</summary>
        public long Persisted { get; set; } = persisted;

        /// <summary>Bytes appended since the last flush.</summary>
        public ArrayBufferWriter<byte> Pending { get; set; } = new();

        public bool Sealed { get; set; } = isSealed;

        public long Length => Persisted + Pending.WrittenCount;
    }

    private sealed class IndexedDbSegmentStore(JSObject database, List<Segment> segments) : ISegmentStore, IDisposable
    {
        private const string ManifestKey = "manifest";

        private readonly SemaphoreSlim _flushing = new(1, 1);
        private byte[]? _manifest;

        public Durability Durability => Durability.Committed;

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
        {
            SegmentInfo[] list = new SegmentInfo[segments.Count];

            for (int i = 0; i < list.Length; i++)
            {
                Segment segment = segments[i];
                SegmentId id = new(segment.Id);
                ByteCount length = new(segment.Length);
                list[i] = segment.Sealed || i < list.Length - 1 ? SegmentInfo.Sealed(id, length) : SegmentInfo.Open(id, length);
            }

            return new ValueTask<IReadOnlyList<SegmentInfo>>(list);
        }

        public async ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
        {
            if (segments.Count > 0 && !segments[^1].Sealed)
            {
                throw new InvalidOperationException("The newest segment is not sealed; only one segment may be open.");
            }

            Segment segment = new(segments.Count == 0 ? 0 : segments[^1].Id + 1, 0, false);
            segments.Add(segment);
            using JSObject empty = Interop.Stage(ReadOnlyMemory<byte>.Empty);
            await Interop.IdbAppend(database, segment.Id, 0, empty, 0, false).ConfigureAwait(false);
            return new SegmentId(segment.Id);
        }

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            Segment target = Get(segment);

            if (target.Sealed || target != segments[^1])
            {
                throw new InvalidOperationException("Segment " + segment.Value + " is sealed and never changes again.");
            }

            target.Pending.Write(bytes.Span);
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) =>
            CommitAsync(Get(segment), seal: false, cancellationToken);

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) =>
            CommitAsync(Get(segment), seal: true, cancellationToken);

        public async ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            Segment source = Get(segment);

            // What is pending now, and where it starts: a flush that completes
            // during the read below moves bytes from one to the other.
            long persisted = source.Persisted;
            ReadOnlyMemory<byte> pending = source.Pending.WrittenMemory;
            long end = Math.Min(persisted + pending.Length, offset.Value + Math.Min(length.Value, int.MaxValue));

            if (offset.Value >= end)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            // A fresh array, the caller's to hold (ADR 0040).
            byte[] bytes = new byte[end - offset.Value];
            int filled = 0;

            if (offset.Value < persisted)
            {
                JSObject stored = await Interop.IdbReadRange(database, segment.Value, offset.Value, Math.Min(end, persisted) - offset.Value).ConfigureAwait(false);
                filled = Interop.ByteLength(stored);
                Interop.CopyOut(stored, 0, bytes.AsSpan(0, filled));
                stored.Dispose();
            }

            if (end > persisted)
            {
                long from = Math.Max(offset.Value, persisted);
                pending.Span.Slice((int)(from - persisted), (int)(end - from)).CopyTo(bytes.AsSpan((int)(from - offset.Value)));
                filled = bytes.Length;
            }

            return bytes.AsMemory(0, filled);
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken)
        {
            if (_manifest is null)
            {
                JSObject? stored = await Interop.IdbGetMeta(database, ManifestKey).ConfigureAwait(false);

                if (stored is null)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                _manifest = Interop.ToArray(stored);
                stored.Dispose();
            }

            return _manifest;
        }

        public async ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
        {
            if (segments.Count > 0)
            {
                throw new InvalidOperationException("The manifest is written before any segment.");
            }

            using JSObject staged = Interop.Stage(manifest);

            if (!await Interop.IdbPutMetaOnce(database, ManifestKey, staged).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The log already has a manifest; it is written once.");
            }

            _manifest = manifest.ToArray();
        }

        // One transaction: the pending bytes as one chunk, and the segment's
        // length and seal. Complete means committed with strict durability.
        private async ValueTask CommitAsync(Segment segment, bool seal, CancellationToken cancellationToken)
        {
            await _flushing.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                int count = segment.Pending.WrittenCount;

                if (count == 0 && (!seal || segment.Sealed))
                {
                    return;
                }

                long offset = segment.Persisted;
                using JSObject staged = Interop.Stage(segment.Pending.WrittenMemory);
                await Interop.IdbAppend(database, segment.Id, offset, staged, offset + count, seal || segment.Sealed).ConfigureAwait(false);

                // Appends made while the transaction ran stay pending.
                ArrayBufferWriter<byte> rest = new();
                rest.Write(segment.Pending.WrittenSpan[count..]);
                segment.Pending = rest;
                segment.Persisted = offset + count;
                segment.Sealed |= seal;
            }
            finally
            {
                _flushing.Release();
            }
        }

        public void Dispose() => _flushing.Dispose();

        private Segment Get(SegmentId segment)
        {
            foreach (Segment candidate in segments)
            {
                if (candidate.Id == segment.Value)
                {
                    return candidate;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(segment), segment.Value, "No such segment.");
        }
    }

    private sealed class IndexedDbDerivedStore(JSObject database) : IDerivedStore
    {
        public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken) =>
            new(new Writer(database, name));

        public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
        {
            JSObject stored = await Interop.IdbGetBlob(database, name.Value).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("No derived blob named '" + name + "'.");

            // The whole blob, now: every read after this is synchronous.
            byte[] bytes = Interop.ToArray(stored);
            stored.Dispose();
            return new MemoryBlob(bytes);
        }

        public async ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) =>
            await Interop.IdbDeleteBlob(database, name.Value).ConfigureAwait(false);

        public async ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
        {
            List<string> names = [.. Lines(await Interop.IdbBlobNames(database).ConfigureAwait(false))];
            names.Sort(StringComparer.Ordinal);
            BlobName[] result = new BlobName[names.Count];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = new BlobName(names[i]);
            }

            return result;
        }

        private sealed class Writer(JSObject database, BlobName name) : IBlobWriter
        {
            private ArrayBufferWriter<byte>? _bytes = new();

            public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                (_bytes ?? throw new InvalidOperationException("The blob is already published.")).Write(bytes.Span);
                return ValueTask.CompletedTask;
            }

            public async ValueTask PublishAsync(CancellationToken cancellationToken)
            {
                ArrayBufferWriter<byte> bytes = _bytes ?? throw new InvalidOperationException("The blob is already published.");
                _bytes = null;
                using JSObject staged = Interop.Stage(bytes.WrittenMemory);
                await Interop.IdbPutBlob(database, name.Value, staged).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync()
            {
                _bytes = null;
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>A blob read whole at open: an array nobody writes again.</summary>
    private sealed class MemoryBlob(byte[] bytes) : IReadableBlob
    {
        public ByteCount Length => new(bytes.Length);

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public int Read(ByteOffset offset, Span<byte> destination)
        {
            if (offset.Value >= bytes.Length)
            {
                return 0;
            }

            int start = (int)offset.Value;
            int count = Math.Min(destination.Length, bytes.Length - start);
            bytes.AsSpan(start, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }
}
