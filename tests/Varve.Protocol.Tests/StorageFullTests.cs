// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// A full disk over HTTP (ADR 0214 point 2): the write that met it is
/// answered <c>507 storage-full</c> and not committed, reads go on, and once
/// the space is back the next write commits, with no reopen by anyone.
/// </summary>
public sealed class StorageFullTests
{
    [Fact]
    public async Task a_write_on_a_full_disk_is_507_and_the_next_write_after_space_returns_commits()
    {
        FullStorage storage = new(new MemoryStorage());
        Dataset dataset = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);

        await using (dataset)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct))
        {
            const string Sparql = "datasets/d/sparql";
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct)).StatusCode);

            // The disk fills: the write is refused, and nothing of it is there.
            storage.Full = true;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                HttpResponseMessage refused = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);
                Assert.Equal((HttpStatusCode)507, refused.StatusCode);
                using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
                Assert.Equal(ProblemType.StorageFull.Value, problem.RootElement.GetProperty("type").GetString());
                Assert.Equal(1, dataset.Head.Value);
                Assert.True(dataset.StorageFull);
            }

            // Reads go on.
            HttpResponseMessage read = await host.Client.SendAsync(P.Query(Sparql, "ASK { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Contains("true", await read.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);

            // The space returns: the next write commits, and nobody reopened anything.
            storage.Full = false;
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct)).StatusCode);
            Assert.Equal(2, dataset.Head.Value);
            Assert.False(dataset.StorageFull);
            HttpResponseMessage both = await host.Client.SendAsync(P.Query(Sparql, "ASK { <http://ex/a> <http://ex/p> 1 . <http://ex/b> <http://ex/p> 2 }"), P.Ct);
            Assert.Contains("true", await both.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        }

        // What the log holds reopens to the same two commits.
        storage.Full = false;
        await using Dataset reopened = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        Assert.Equal(2, reopened.Head.Value);
    }
}

/// <summary>
/// A write closed in the log whose flush fails on a full disk (ADR 0214 point
/// 2): never <c>507</c>, since its record may survive, but <c>503
/// commit-outcome-unknown</c> with its position and request id; the next write
/// once the space is back fences it off and commits at that position, so the
/// commits resource settles it: the commit there is the next write's.
/// </summary>
public sealed class CommitOutcomeUnknownTests
{
    [Fact]
    public async Task a_write_whose_flush_fails_is_503_unknown_and_the_commits_resource_settles_it()
    {
        FullStorage storage = new(new MemoryStorage());
        Dataset dataset = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);

        await using (dataset)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct))
        {
            const string Sparql = "datasets/d/sparql";
            Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct)).StatusCode);

            storage.FlushFails = true;
            HttpResponseMessage unknown = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unknown.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(P.Ct));
            Assert.Equal(ProblemType.CommitOutcomeUnknown.Value, problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(2, problem.RootElement.GetProperty("position").GetInt64());
            string requestId = problem.RootElement.GetProperty("requestId").GetString()!;
            Assert.Equal(requestId, unknown.Headers.GetValues("Varve-Request-Id").Single());

            // Not committed in the open dataset: the head stays where the last
            // flush that returned left it, and reads go on.
            Assert.Equal(1, dataset.Head.Value);
            HttpResponseMessage read = await host.Client.SendAsync(P.Query(Sparql, "ASK { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            // The space returns: the next write fences the unknown commit off
            // and is committed at its position, with its own cause.
            storage.FlushFails = false;
            HttpResponseMessage next = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/c> <http://ex/p> 3 }"), P.Ct);
            Assert.Equal(HttpStatusCode.NoContent, next.StatusCode);
            Assert.Equal(2, dataset.Head.Value);
            string nextId = next.Headers.GetValues("Varve-Request-Id").Single();
            Assert.NotEqual(requestId, nextId);

            HttpResponseMessage settled = await host.Client.GetAsync("datasets/d/commits/2", P.Ct);
            Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
            string body = await settled.Content.ReadAsStringAsync(P.Ct);
            Assert.Contains(nextId, body, StringComparison.Ordinal);
            Assert.DoesNotContain(requestId, body, StringComparison.Ordinal);
            HttpResponseMessage gone = await host.Client.SendAsync(P.Query(Sparql, "ASK { <http://ex/b> <http://ex/p> 2 }"), P.Ct);
            Assert.Contains("false", await gone.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        }

        // The fence holds across a reopen: the unknown write is not in the log.
        await using Dataset reopened = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        Assert.Equal(2, reopened.Head.Value);
    }
}

/// <summary>
/// A storage whose log refuses every append and every new segment, as a
/// full disk does, while <see cref="Full"/> is set: the platform's disk-full
/// <see cref="IOException"/>, before any byte is written; and whose every
/// flush fails so while <see cref="FlushFails"/> is set, after the bytes were
/// appended. Its derived store is the inner one's.
/// </summary>
internal sealed class FullStorage(MemoryStorage inner) : IStorage
{
    private readonly FullLog _log = new(inner.Log);

    public bool Full
    {
        get => _log.Full;
        set => _log.Full = value;
    }

    /// <summary>Whether every flush of the log fails as a full disk's can, after the bytes were appended.</summary>
    public bool FlushFails
    {
        get => _log.FlushFails;
        set => _log.FlushFails = value;
    }

    public ISegmentStore Log => _log;

    public IDerivedStore Derived => inner.Derived;

    private sealed class FullLog(ISegmentStore log) : ISegmentStore
    {
        public volatile bool Full;

        public volatile bool FlushFails;

        public Durability Durability => log.Durability;

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) => log.ListSegmentsAsync(cancellationToken);

        public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken) =>
            Full ? throw DiskFull() : log.CreateSegmentAsync(cancellationToken);

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
            Full ? throw DiskFull() : log.AppendAsync(segment, bytes, cancellationToken);

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) =>
            FlushFails ? throw DiskFull() : log.FlushAsync(segment, cancellationToken);

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) => log.SealAsync(segment, cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken) =>
            log.ReadRangeAsync(segment, offset, length, cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => log.ReadManifestAsync(cancellationToken);

        public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken) => log.WriteManifestAsync(manifest, cancellationToken);

        // ERROR_DISK_FULL, as the file backend sees it on Windows.
        private static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));
    }
}
