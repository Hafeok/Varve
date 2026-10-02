// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The storage contract of ADRs 0018 and 0040, run against every backend this
/// repository has: the memory backend, and a second one written in this test
/// assembly against public members only.
/// </summary>
/// <remarks>
/// The second backend is the standing proof ADR 0040 asks for: if the contract
/// ever needs an internal member to be implementable, this file stops
/// compiling. The file backend at milestone 6 is the real external proof.
/// </remarks>
public abstract class StorageContractTests
{
    protected abstract IStorage Create();

    protected abstract Durability Expected { get; }

    private static byte[] Bytes(params byte[] bytes) => bytes;

    [Fact]
    public async Task appended_bytes_read_back_and_a_read_past_the_end_is_short()
    {
        IStorage storage = Create();
        SegmentId segment = await storage.Log.CreateSegmentAsync(T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2, 3), T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(4, 5), T.Ct);
        await storage.Log.FlushAsync(segment, T.Ct);

        Assert.Equal(Bytes(1, 2, 3, 4, 5), (await storage.Log.ReadRangeAsync(segment, new ByteOffset(0), new ByteCount(100), T.Ct)).ToArray());
        Assert.Equal(Bytes(3, 4), (await storage.Log.ReadRangeAsync(segment, new ByteOffset(2), new ByteCount(2), T.Ct)).ToArray());
        Assert.Equal(0, (await storage.Log.ReadRangeAsync(segment, new ByteOffset(5), new ByteCount(10), T.Ct)).Length);
    }

    [Fact]
    public async Task a_sealed_segment_refuses_appends_and_only_the_newest_may_be_open()
    {
        IStorage storage = Create();
        SegmentId first = await storage.Log.CreateSegmentAsync(T.Ct);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await storage.Log.CreateSegmentAsync(T.Ct));

        await storage.Log.AppendAsync(first, Bytes(9), T.Ct);
        await storage.Log.SealAsync(first, T.Ct);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await storage.Log.AppendAsync(first, Bytes(1), T.Ct));

        SegmentId second = await storage.Log.CreateSegmentAsync(T.Ct);
        Assert.True(second > first);

        IReadOnlyList<SegmentInfo> segments = await storage.Log.ListSegmentsAsync(T.Ct);
        Assert.Equal([SegmentInfo.Sealed(first, new ByteCount(1)), SegmentInfo.Open(second, new ByteCount(0))], segments);
    }

    [Fact]
    public async Task bytes_already_read_never_change()
    {
        IStorage storage = Create();
        SegmentId segment = await storage.Log.CreateSegmentAsync(T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2), T.Ct);
        ReadOnlyMemory<byte> held = await storage.Log.ReadRangeAsync(segment, new ByteOffset(0), new ByteCount(2), T.Ct);

        // Enough appends to force any buffer the backend keeps to grow.
        for (int i = 0; i < 1000; i++)
        {
            await storage.Log.AppendAsync(segment, new byte[64], T.Ct);
        }

        Assert.Equal(Bytes(1, 2), held.ToArray());
    }

    private static async Task PutAsync(IStorage storage, string name, params byte[] bytes)
    {
        await using IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName(name), T.Ct);
        await writer.WriteAsync(bytes, T.Ct);
        await writer.PublishAsync(T.Ct);
    }

    private static async Task<byte[]> ReadAsync(IStorage storage, string name, long offset = 0, int length = 100)
    {
        using IReadableBlob blob = await storage.Derived.OpenAsync(new BlobName(name), T.Ct);
        byte[] buffer = new byte[length];
        int read = blob.Read(new ByteOffset(offset), buffer);
        return buffer.AsSpan(0, read).ToArray();
    }

    [Fact]
    public async Task derived_blobs_are_published_replaced_listed_in_ordinal_order_and_deleted()
    {
        IStorage storage = Create();
        await PutAsync(storage, "b", 1);
        await PutAsync(storage, "a", 2, 3);
        await PutAsync(storage, "b", 4, 5, 6);

        Assert.Equal([new BlobName("a"), new BlobName("b")], await storage.Derived.ListAsync(T.Ct));
        Assert.Equal(Bytes(5, 6), await ReadAsync(storage, "b", 1, 10));
        Assert.Equal(Bytes(4, 5, 6), await ReadAsync(storage, "b"));
        Assert.Empty(await ReadAsync(storage, "b", 3, 10));
        Assert.True(await storage.Derived.DeleteAsync(new BlobName("a"), T.Ct));
        Assert.False(await storage.Derived.DeleteAsync(new BlobName("a"), T.Ct));
        Assert.Equal([new BlobName("b")], await storage.Derived.ListAsync(T.Ct));
        await Assert.ThrowsAnyAsync<KeyNotFoundException>(async () => await storage.Derived.OpenAsync(new BlobName("a"), T.Ct));
    }

    [Fact]
    public async Task a_blob_is_invisible_until_published_and_an_unpublished_writer_leaves_nothing()
    {
        IStorage storage = Create();
        await PutAsync(storage, "kept", 1, 2);

        await using (IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName("kept"), T.Ct))
        {
            await writer.WriteAsync(Bytes(9, 9, 9), T.Ct);
            Assert.Equal(Bytes(1, 2), await ReadAsync(storage, "kept"));
        }

        await using (IBlobWriter writer = await storage.Derived.CreateAsync(new BlobName("never"), T.Ct))
        {
            await writer.WriteAsync(Bytes(7), T.Ct);
        }

        Assert.Equal(Bytes(1, 2), await ReadAsync(storage, "kept"));
        Assert.Equal([new BlobName("kept")], await storage.Derived.ListAsync(T.Ct));
    }

    [Fact]
    public async Task an_open_blob_keeps_its_bytes_when_its_name_is_replaced()
    {
        IStorage storage = Create();
        await PutAsync(storage, "run", 1, 2, 3);
        using IReadableBlob held = await storage.Derived.OpenAsync(new BlobName("run"), T.Ct);

        await PutAsync(storage, "run", 4, 5, 6, 7);

        byte[] buffer = new byte[8];
        Assert.Equal(new ByteCount(3), held.Length);
        Assert.Equal(3, held.Read(new ByteOffset(0), buffer));
        Assert.Equal(Bytes(1, 2, 3), buffer.AsSpan(0, 3).ToArray());
        Assert.Equal(Bytes(4, 5, 6, 7), await ReadAsync(storage, "run"));
    }

    [Fact]
    public async Task the_manifest_is_empty_until_written_and_is_written_once_before_any_segment()
    {
        IStorage storage = Create();
        Assert.True((await storage.Log.ReadManifestAsync(T.Ct)).IsEmpty);

        await storage.Log.WriteManifestAsync(Bytes(1, 2, 3), T.Ct);
        Assert.Equal(Bytes(1, 2, 3), (await storage.Log.ReadManifestAsync(T.Ct)).ToArray());
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await storage.Log.WriteManifestAsync(Bytes(4), T.Ct));

        IStorage other = Create();
        await other.Log.CreateSegmentAsync(T.Ct);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await other.Log.WriteManifestAsync(Bytes(4), T.Ct));
    }

    [Fact]
    public void the_backend_declares_its_durability() => Assert.Equal(Expected, Create().Log.Durability);

    [Fact]
    public async Task a_dataset_commits_checkpoints_reopens_and_reads_as_of_over_the_backend()
    {
        IStorage storage = Create();

        await using (Dataset dataset = await Dataset.OpenAsync(storage, T.Options(segmentBytes: 1024), T.Ct))
        {
            for (int i = 0; i < 20; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 40))), T.Ct);
            }

            await dataset.CheckpointAsync(new Position(10), T.Ct);
        }

        Assert.True((await storage.Log.ListSegmentsAsync(T.Ct)).Count > 1, "the small segment size should have forced several segments");

        await using Dataset reopened = await Dataset.OpenAsync(storage, T.Options(segmentBytes: 1024), T.Ct);
        Assert.Equal(new Position(20), reopened.Head);
        Assert.Equal([new Position(10)], reopened.Checkpoints);

        using DatasetView at12 = await reopened.AsOfAsync(new Position(12), T.Ct);
        Assert.Equal(12, T.All(at12).Count);
    }
}

public sealed class MemoryStorageContractTests : StorageContractTests
{
    protected override IStorage Create() => new MemoryStorage();

    protected override Durability Expected => Durability.None;
}

public sealed class ExternalBackendContractTests : StorageContractTests
{
    protected override IStorage Create() => new ListStorage();

    protected override Durability Expected => Durability.Committed;
}

/// <summary>
/// A deliberately naive backend, written outside <c>Varve.Store</c> against its
/// public members only (ADR 0040). It copies on every read, which the contract
/// allows, and declares <see cref="Durability.Committed"/> to prove that the
/// store takes the declaration from the backend rather than assuming it.
/// </summary>
internal sealed class ListStorage : IStorage, ISegmentStore, IDerivedStore
{
    private readonly List<(List<byte> Bytes, bool Sealed)> _segments = [];
    private readonly SortedDictionary<BlobName, byte[]> _blobs = [];
    private byte[]? _manifest;

    public ISegmentStore Log => this;

    public IDerivedStore Derived => this;

    public Durability Durability => Durability.Committed;

    public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) =>
        new(_segments.Select((s, i) => s.Sealed
            ? SegmentInfo.Sealed(new SegmentId(i), new ByteCount(s.Bytes.Count))
            : SegmentInfo.Open(new SegmentId(i), new ByteCount(s.Bytes.Count))).ToArray());

    public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
    {
        if (_segments.Count > 0 && !_segments[^1].Sealed)
        {
            throw new InvalidOperationException("The newest segment is open.");
        }

        _segments.Add(([], false));
        return new(new SegmentId(_segments.Count - 1));
    }

    public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (_segments[segment.Value].Sealed)
        {
            throw new InvalidOperationException("Sealed.");
        }

        _segments[segment.Value].Bytes.AddRange(bytes.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken)
    {
        _segments[segment.Value] = (_segments[segment.Value].Bytes, true);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        List<byte> bytes = _segments[segment.Value].Bytes;
        int start = (int)Math.Min(offset.Value, bytes.Count);
        return new(bytes.GetRange(start, (int)Math.Min(length.Value, bytes.Count - start)).ToArray());
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => new(_manifest ?? ReadOnlyMemory<byte>.Empty);

    public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
    {
        if (_manifest is not null || _segments.Count > 0)
        {
            throw new InvalidOperationException("The manifest is written once, before any segment.");
        }

        _manifest = manifest.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken) => new(new ListWriter(this, name));

    public ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken) =>
        _blobs.TryGetValue(name, out byte[]? blob) ? new(new ListBlob(blob)) : throw new KeyNotFoundException(name.Value);

    public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) => new(_blobs.Remove(name));

    public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken) => new(_blobs.Keys.ToArray());

    private sealed class ListWriter(ListStorage storage, BlobName name) : IBlobWriter
    {
        private readonly List<byte> _bytes = [];

        public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            _bytes.AddRange(bytes.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAsync(CancellationToken cancellationToken)
        {
            storage._blobs[name] = [.. _bytes];
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ListBlob(byte[] bytes) : IReadableBlob
    {
        public ByteCount Length => new(bytes.Length);

        public int Read(ByteOffset offset, Span<byte> destination)
        {
            int start = (int)Math.Min(offset.Value, bytes.Length);
            int count = Math.Min(destination.Length, bytes.Length - start);
            bytes.AsSpan(start, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }
}
