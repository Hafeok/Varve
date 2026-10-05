// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Benchmarks;

/// <summary>
/// A dataset as a crash at one byte of its log would leave it, without
/// copying it: the real log read up to a cut — segments after it absent, the
/// one it falls in ending there, unsealed — and the real <c>derived/</c> of an
/// earlier moment. Whatever recovery writes goes to memory. Built on the
/// storage contract's public members only, for the bulk gate's crash check
/// at every record boundary of a load too large to copy once per boundary.
/// </summary>
internal sealed class CutStorage : IStorage, ISegmentStore, IDerivedStore
{
    private readonly IStorage _log;
    private readonly IStorage _derived;
    private readonly int _cutSegment;
    private readonly long _cutLength;
    private readonly Dictionary<int, List<byte>> _appended = [];
    private readonly HashSet<int> _sealed = [];
    private readonly Dictionary<string, byte[]> _written = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);

    internal CutStorage(IStorage log, IStorage derived, int cutSegment, long cutLength)
    {
        _log = log;
        _derived = derived;
        _cutSegment = cutSegment;
        _cutLength = cutLength;
    }

    /// <summary>A blob as an earlier moment left it, over the derived store's own.</summary>
    internal void Replace(BlobName name, byte[] bytes) => _written[name.Value] = bytes;

    /// <summary>A blob the earlier moment did not have.</summary>
    internal void Remove(BlobName name)
    {
        _written.Remove(name.Value);
        _deleted.Add(name.Value);
    }

    public ISegmentStore Log => this;

    public IDerivedStore Derived => this;

    public Durability Durability => _log.Log.Durability;

    public async ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
    {
        List<SegmentInfo> segments = [];

        foreach (SegmentInfo real in await _log.Log.ListSegmentsAsync(cancellationToken))
        {
            int id = real.Id.Value;

            if (id > _cutSegment)
            {
                break;
            }

            long length = (id == _cutSegment ? _cutLength : real.Length.Value) + Appended(id).Count;
            bool isSealed = (id < _cutSegment && real.IsSealed) || _sealed.Contains(id);
            segments.Add(isSealed ? SegmentInfo.Sealed(real.Id, new ByteCount(length)) : SegmentInfo.Open(real.Id, new ByteCount(length)));
        }

        foreach (int id in _appended.Keys.Where(k => k > _cutSegment).Order())
        {
            ByteCount length = new(_appended[id].Count);
            segments.Add(_sealed.Contains(id) ? SegmentInfo.Sealed(new SegmentId(id), length) : SegmentInfo.Open(new SegmentId(id), length));
        }

        return segments;
    }

    public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
    {
        int id = Math.Max(_cutSegment, _appended.Keys.DefaultIfEmpty(-1).Max()) + 1;
        _appended[id] = [];
        return new ValueTask<SegmentId>(new SegmentId(id));
    }

    public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        Appended(segment.Value).AddRange(bytes.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken)
    {
        _sealed.Add(segment.Value);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        int id = segment.Value;
        long realLength = id > _cutSegment ? 0 : id == _cutSegment ? _cutLength : long.MaxValue;
        List<byte> extra = Appended(id);

        if (realLength == long.MaxValue)
        {
            return await _log.Log.ReadRangeAsync(segment, offset, length, cancellationToken);
        }

        byte[] result = new byte[Math.Max(0, Math.Min(length.Value, realLength + extra.Count - offset.Value))];
        int filled = 0;

        if (offset.Value < realLength)
        {
            ReadOnlyMemory<byte> real = await _log.Log.ReadRangeAsync(segment, offset, new ByteCount(Math.Min(result.Length, realLength - offset.Value)), cancellationToken);
            real.Span.CopyTo(result);
            filled = real.Length;
        }

        for (long at = Math.Max(0, offset.Value - realLength); filled < result.Length; at++)
        {
            result[filled++] = extra[(int)at];
        }

        return result;
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => _log.Log.ReadManifestAsync(cancellationToken);

    public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The cut log already has its manifest.");

    private List<byte> Appended(int segment)
    {
        if (!_appended.TryGetValue(segment, out List<byte>? bytes))
        {
            bytes = [];
            _appended[segment] = bytes;
        }

        return bytes;
    }

    // derived/: the earlier moment's blobs, read through; writes and deletes in memory.
    public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken) =>
        new(new Writer(this, name.Value));

    public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (_written.TryGetValue(name.Value, out byte[]? bytes))
        {
            return new Blob(bytes);
        }

        if (_deleted.Contains(name.Value))
        {
            throw new KeyNotFoundException(name.Value);
        }

        return await _derived.Derived.OpenAsync(name, cancellationToken);
    }

    public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) =>
        new(_written.Remove(name.Value) | _deleted.Add(name.Value));

    public async ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
    {
        SortedSet<string> names = new(StringComparer.Ordinal);

        foreach (BlobName name in await _derived.Derived.ListAsync(cancellationToken))
        {
            if (!_deleted.Contains(name.Value))
            {
                names.Add(name.Value);
            }
        }

        names.UnionWith(_written.Keys);
        return [.. names.Select(n => new BlobName(n))];
    }

    private sealed class Writer(CutStorage owner, string name) : IBlobWriter
    {
        private readonly List<byte> _bytes = [];

        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            _bytes.AddRange(bytes.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(CancellationToken cancellationToken)
        {
            owner._written[name] = [.. _bytes];
            owner._deleted.Remove(name);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Blob(byte[] bytes) : IReadableBlob
    {
        public ByteCount Length => new(bytes.Length);

        public int Read(ByteOffset offset, Span<byte> destination)
        {
            int count = (int)Math.Max(0, Math.Min(destination.Length, bytes.Length - offset.Value));
            bytes.AsSpan((int)offset.Value, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }
}
