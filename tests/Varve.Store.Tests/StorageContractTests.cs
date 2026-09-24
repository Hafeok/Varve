// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        int segment = await storage.Log.CreateSegmentAsync(T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2, 3), T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(4, 5), T.Ct);
        await storage.Log.FlushAsync(segment, T.Ct);

        Assert.Equal(Bytes(1, 2, 3, 4, 5), (await storage.Log.ReadRangeAsync(segment, 0, 100, T.Ct)).ToArray());
        Assert.Equal(Bytes(3, 4), (await storage.Log.ReadRangeAsync(segment, 2, 2, T.Ct)).ToArray());
        Assert.Equal(0, (await storage.Log.ReadRangeAsync(segment, 5, 10, T.Ct)).Length);
    }

    [Fact]
    public async Task a_sealed_segment_refuses_appends_and_only_the_newest_may_be_open()
    {
        IStorage storage = Create();
        int first = await storage.Log.CreateSegmentAsync(T.Ct);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await storage.Log.CreateSegmentAsync(T.Ct));

        await storage.Log.AppendAsync(first, Bytes(9), T.Ct);
        await storage.Log.SealAsync(first, T.Ct);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await storage.Log.AppendAsync(first, Bytes(1), T.Ct));

        int second = await storage.Log.CreateSegmentAsync(T.Ct);
        Assert.True(second > first);

        IReadOnlyList<SegmentInfo> segments = await storage.Log.ListSegmentsAsync(T.Ct);
        Assert.Equal([new SegmentInfo(first, 1, true), new SegmentInfo(second, 0, false)], segments);
    }

    [Fact]
    public async Task bytes_already_read_never_change()
    {
        IStorage storage = Create();
        int segment = await storage.Log.CreateSegmentAsync(T.Ct);
        await storage.Log.AppendAsync(segment, Bytes(1, 2), T.Ct);
        ReadOnlyMemory<byte> held = await storage.Log.ReadRangeAsync(segment, 0, 2, T.Ct);

        // Enough appends to force any buffer the backend keeps to grow.
        for (int i = 0; i < 1000; i++)
        {
            await storage.Log.AppendAsync(segment, new byte[64], T.Ct);
        }

        Assert.Equal(Bytes(1, 2), held.ToArray());
    }

    [Fact]
    public async Task derived_blobs_are_put_replaced_listed_in_ordinal_order_and_deleted()
    {
        IStorage storage = Create();
        await storage.Derived.PutAsync("b", Bytes(1), T.Ct);
        await storage.Derived.PutAsync("a", Bytes(2, 3), T.Ct);
        await storage.Derived.PutAsync("b", Bytes(4, 5, 6), T.Ct);

        Assert.Equal(["a", "b"], await storage.Derived.ListAsync(T.Ct));
        Assert.Equal(Bytes(5, 6), (await storage.Derived.GetRangeAsync("b", 1, 10, T.Ct)).ToArray());
        Assert.True(await storage.Derived.DeleteAsync("a", T.Ct));
        Assert.False(await storage.Derived.DeleteAsync("a", T.Ct));
        Assert.Equal(["b"], await storage.Derived.ListAsync(T.Ct));
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

            await dataset.CheckpointAsync(10, T.Ct);
        }

        Assert.True((await storage.Log.ListSegmentsAsync(T.Ct)).Count > 1, "the small segment size should have forced several segments");

        await using Dataset reopened = await Dataset.OpenAsync(storage, T.Options(segmentBytes: 1024), T.Ct);
        Assert.Equal(20, reopened.Head);
        Assert.Equal([10L], reopened.Checkpoints);

        using DatasetView at12 = await reopened.AsOfAsync(12, T.Ct);
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
    private readonly SortedDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public ISegmentStore Log => this;

    public IDerivedStore Derived => this;

    public Durability Durability => Durability.Committed;

    public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) =>
        new(_segments.Select((s, i) => new SegmentInfo(i, s.Bytes.Count, s.Sealed)).ToArray());

    public ValueTask<int> CreateSegmentAsync(CancellationToken cancellationToken)
    {
        if (_segments.Count > 0 && !_segments[^1].Sealed)
        {
            throw new InvalidOperationException("The newest segment is open.");
        }

        _segments.Add(([], false));
        return new(_segments.Count - 1);
    }

    public ValueTask AppendAsync(int segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (_segments[segment].Sealed)
        {
            throw new InvalidOperationException("Sealed.");
        }

        _segments[segment].Bytes.AddRange(bytes.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(int segment, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SealAsync(int segment, CancellationToken cancellationToken)
    {
        _segments[segment] = (_segments[segment].Bytes, true);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(int segment, long offset, int length, CancellationToken cancellationToken)
    {
        List<byte> bytes = _segments[segment].Bytes;
        int start = (int)Math.Min(offset, bytes.Count);
        return new(bytes.GetRange(start, Math.Min(length, bytes.Count - start)).ToArray());
    }

    public ValueTask PutAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        _blobs[name] = bytes.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>> GetRangeAsync(string name, long offset, int length, CancellationToken cancellationToken)
    {
        byte[] blob = _blobs[name];
        int start = (int)Math.Min(offset, blob.Length);
        return new(blob.AsMemory(start, Math.Min(length, blob.Length - start)).ToArray());
    }

    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken) => new(_blobs.Remove(name));

    public ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) => new(_blobs.Keys.ToArray());
}
