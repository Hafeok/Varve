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
/// The cases themselves are <see cref="StorageContractCases"/>, which the
/// browser test app runs against the browser backends (ADR 0084).
/// </remarks>
public abstract class StorageContractTests
{
    protected abstract IStorage Create();

    protected abstract Durability Expected { get; }

    private ValueTask<IStorage> CreateAsync() => new(Create());

    [Fact]
    public Task appended_bytes_read_back_and_a_read_past_the_end_is_short() =>
        StorageContractCases.AppendedBytesReadBack(CreateAsync, T.Ct);

    [Fact]
    public Task a_sealed_segment_refuses_appends_and_only_the_newest_may_be_open() =>
        StorageContractCases.SealedSegmentRefusesAppends(CreateAsync, T.Ct);

    [Fact]
    public Task bytes_already_read_never_change() =>
        StorageContractCases.BytesAlreadyReadNeverChange(CreateAsync, T.Ct);

    [Fact]
    public Task derived_blobs_are_published_replaced_listed_in_ordinal_order_and_deleted() =>
        StorageContractCases.DerivedBlobsLifecycle(CreateAsync, T.Ct);

    [Fact]
    public Task a_blob_is_invisible_until_published_and_an_unpublished_writer_leaves_nothing() =>
        StorageContractCases.UnpublishedLeavesNothing(CreateAsync, T.Ct);

    [Fact]
    public Task an_open_blob_keeps_its_bytes_when_its_name_is_replaced() =>
        StorageContractCases.OpenBlobKeepsItsBytes(CreateAsync, T.Ct);

    [Fact]
    public Task the_manifest_is_empty_until_written_and_is_written_once_before_any_segment() =>
        StorageContractCases.ManifestWrittenOnce(CreateAsync, T.Ct);

    [Fact]
    public Task the_backend_declares_its_durability() =>
        StorageContractCases.BackendDeclaresDurability(CreateAsync, Expected, T.Ct);

    [Fact]
    public Task a_dataset_commits_checkpoints_reopens_and_reads_as_of_over_the_backend() =>
        StorageContractCases.DatasetOverTheBackend(CreateAsync, T.Ct);
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
/// One lock around everything: the store calls a backend from its sequencer
/// and from background maintenance at once (ADR 0070), and the commit
/// index's paging made this test's maintenance run, which found the backend
/// unsafe for it (ADR 0085).
/// </summary>
internal sealed class ListStorage : IStorage, ISegmentStore, IDerivedStore
{
    private readonly Lock _gate = new();
    private readonly List<(List<byte> Bytes, bool Sealed)> _segments = [];
    private readonly SortedDictionary<BlobName, byte[]> _blobs = [];
    private byte[]? _manifest;

    public ISegmentStore Log => this;

    public IDerivedStore Derived => this;

    public Durability Durability => Durability.Committed;

    public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return new(_segments.Select((s, i) => s.Sealed
                ? SegmentInfo.Sealed(new SegmentId(i), new ByteCount(s.Bytes.Count))
                : SegmentInfo.Open(new SegmentId(i), new ByteCount(s.Bytes.Count))).ToArray());
        }
    }

    public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_segments.Count > 0 && !_segments[^1].Sealed)
            {
                throw new InvalidOperationException("The newest segment is open.");
            }

            _segments.Add(([], false));
            return new(new SegmentId(_segments.Count - 1));
        }
    }

    public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_segments[segment.Value].Sealed)
            {
                throw new InvalidOperationException("Sealed.");
            }

            _segments[segment.Value].Bytes.AddRange(bytes.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _segments[segment.Value] = (_segments[segment.Value].Bytes, true);
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            List<byte> bytes = _segments[segment.Value].Bytes;
            int start = (int)Math.Min(offset.Value, bytes.Count);
            return new(bytes.GetRange(start, (int)Math.Min(length.Value, bytes.Count - start)).ToArray());
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return new(_manifest ?? ReadOnlyMemory<byte>.Empty);
        }
    }

    public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_manifest is not null || _segments.Count > 0)
            {
                throw new InvalidOperationException("The manifest is written once, before any segment.");
            }

            _manifest = manifest.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken) => new(new ListWriter(this, name));

    public ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _blobs.TryGetValue(name, out byte[]? blob) ? new(new ListBlob(blob)) : throw new KeyNotFoundException(name.Value);
        }
    }

    public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return new(_blobs.Remove(name));
        }
    }

    public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return new(_blobs.Keys.ToArray());
        }
    }

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
            lock (storage._gate)
            {
                storage._blobs[name] = [.. _bytes];
            }

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
