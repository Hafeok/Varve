// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Maintenance of milestone 6c: the checkpoint policy (ADR 0078), runs
/// deleted only once nothing reads them (ADR 0070), and R2's cost.
/// </summary>
public class MaintenanceTests
{
    private static CommitRequest Batch(int commit, int size)
    {
        CommitRequest request = new();

        for (int i = 0; i < size; i++)
        {
            request.Assert(T.Iri("s" + commit.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal(i.ToString(CultureInfo.InvariantCulture)));
        }

        return request;
    }

    private static List<Quad> All(DatasetView view)
    {
        List<Quad> quads = [];
        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            quads.Add(cursor.Current);
        }

        return quads;
    }

    private static async Task<string[]> Runs(IStorage storage) =>
        [.. (await storage.Derived.ListAsync(T.Ct)).Select(n => n.Value).Where(n => n.StartsWith("index/runs/", StringComparison.Ordinal))];

    /// <summary>
    /// A pin taken before a flush and a merge keeps every run it reads: the
    /// merge replaces them in the projection, and their blobs are deleted only
    /// after the pin is disposed, while the pin reads what it read before.
    /// </summary>
    [Fact]
    public async Task a_pin_held_across_a_merge_keeps_its_runs_until_it_is_disposed()
    {
        await using TemporaryDirectory directory = new();

        foreach (IStorage storage in new IStorage[] { new MemoryStorage(), await directory.OpenAsync() })
        {
            DatasetOptions options = new() { Clock = ManualClock.Epoch(), MemtableLimit = new QuadCount(1), Maintenance = MaintenanceMode.Off };
            await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

            await dataset.CommitAsync(Batch(1, 40), T.Ct);
            await dataset.MaintainAsync(T.Ct);
            await dataset.CommitAsync(Batch(2, 40), T.Ct);
            await dataset.MaintainAsync(T.Ct);
            await dataset.CommitAsync(Batch(3, 20), T.Ct);

            DatasetView pin = dataset.Pin();
            List<Quad> before = All(pin);
            string[] held = await Runs(storage);
            Assert.NotEmpty(held);

            // A flush and a merge: every run the pin reads is replaced.
            await dataset.MaintainAsync(T.Ct);
            await dataset.CommitAsync(Batch(4, 200), T.Ct);
            await dataset.MaintainAsync(T.Ct);
            string[] after = await Runs(storage);
            Assert.Empty(held.Intersect(after.Except(held)));
            Assert.All(held, name => Assert.Contains(name, after));
            Assert.Equal(before, All(pin));

            pin.Dispose();
            await dataset.MaintainAsync(T.Ct);
            string[] released = await Runs(storage);
            Assert.Empty(held.Intersect(released));
        }
    }

    [Fact]
    public async Task the_policy_writes_a_checkpoint_every_so_many_commits_and_keeps_the_newest()
    {
        MemoryStorage storage = new();
        DatasetOptions options = new()
        {
            Clock = ManualClock.Epoch(),
            Maintenance = MaintenanceMode.Off,
            Checkpoints = new CheckpointPolicy { EveryCommits = 5, Keep = 2 },
        };

        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        for (int i = 1; i <= 23; i++)
        {
            await dataset.CommitAsync(Batch(i, 3), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        Assert.Equal([new Position(15), new Position(20)], dataset.Checkpoints);
    }

    [Fact]
    public async Task the_policy_writes_a_checkpoint_every_so_many_bytes_of_log_in_the_background()
    {
        MemoryStorage storage = new();
        DatasetOptions options = new()
        {
            Clock = ManualClock.Epoch(),
            Maintenance = MaintenanceMode.Background,
            Checkpoints = new CheckpointPolicy { EveryLogBytes = new ByteCount(4_000) },
        };

        Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        for (int i = 1; i <= 40; i++)
        {
            await dataset.CommitAsync(Batch(i, 10), T.Ct);
        }

        // Maintenance runs on its own; MaintainAsync waits for it and finishes.
        await dataset.MaintainAsync(T.Ct);
        IReadOnlyList<Position> written = dataset.Checkpoints;
        Assert.NotEmpty(written);
        long newest = written[^1].Value;
        Assert.True(dataset.LogBytesBetween(newest, dataset.Head.Value) < 4_000);
        await dataset.DisposeAsync();
    }

    /// <summary>
    /// R2: an as-of read reads the log from the nearest checkpoint at or below
    /// it to its position, and nothing else — the bytes read are exactly those
    /// commits' records, whatever the size of the dataset.
    /// </summary>
    [Fact]
    public async Task an_as_of_read_reads_exactly_the_log_distance_to_its_checkpoint()
    {
        await Gen.Select(Gen.Int[2, 60], Gen.Int[1, 30], Gen.Int[0, 59]).SampleAsync(
            async (commits, every, at) =>
            {
                CountingStorage storage = new(new MemoryStorage());
                DatasetOptions options = new()
                {
                    Clock = ManualClock.Epoch(),
                    Maintenance = MaintenanceMode.Off,
                    Checkpoints = new CheckpointPolicy { EveryCommits = every },
                };

                await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

                for (int i = 1; i <= commits; i++)
                {
                    await dataset.CommitAsync(Batch(i % 7, 1 + (i % 5)).Retract(T.Iri("s" + ((i + 3) % 7).ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("0")), T.Ct);
                    await dataset.MaintainAsync(T.Ct);
                }

                long position = Math.Min(at, dataset.Head.Value);
                long checkpoint = dataset.Checkpoints.Select(p => p.Value).Where(p => p <= position).DefaultIfEmpty(0).Max();

                storage.Read = 0;
                using DatasetView view = await dataset.AsOfAsync(new Position(position), T.Ct);
                Assert.Equal(dataset.LogBytesBetween(checkpoint, position), storage.Read);
            },
            iter: 60);
    }

    /// <summary>Storage that counts the bytes read from the log.</summary>
    private sealed class CountingStorage(IStorage inner) : IStorage, ISegmentStore
    {
        public long Read;

        public ISegmentStore Log => this;

        public IDerivedStore Derived => inner.Derived;

        public Durability Durability => inner.Log.Durability;

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) => inner.Log.ListSegmentsAsync(cancellationToken);

        public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken) => inner.Log.CreateSegmentAsync(cancellationToken);

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => inner.Log.AppendAsync(segment, bytes, cancellationToken);

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) => inner.Log.FlushAsync(segment, cancellationToken);

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) => inner.Log.SealAsync(segment, cancellationToken);

        public async ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> bytes = await inner.Log.ReadRangeAsync(segment, offset, length, cancellationToken);
            Interlocked.Add(ref Read, bytes.Length);
            return bytes;
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => inner.Log.ReadManifestAsync(cancellationToken);

        public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken) => inner.Log.WriteManifestAsync(manifest, cancellationToken);
    }
}
