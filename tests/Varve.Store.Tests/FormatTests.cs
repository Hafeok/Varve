// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Format version 1 (ADR 0072, <c>docs/spec/storage-format.md</c>): the
/// manifest, the version refusal, creation, the chain walk across segments,
/// and what opening reads.
/// </summary>
public sealed class FormatTests
{
    private static CommitRequest Quad(int i) =>
        new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 60)));

    private static async Task<(MemoryStorage Storage, DatasetOptions Options)> LogAsync(int commits, long segmentBytes = 64L << 20)
    {
        MemoryStorage storage = new();
        DatasetOptions options = T.Options(segmentBytes: segmentBytes);

        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        for (int i = 0; i < commits; i++)
        {
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Quad(i), T.Ct)).Outcome);
        }

        return (storage, options);
    }

    [Fact]
    public async Task a_manifest_of_a_later_version_is_refused_by_name()
    {
        byte[] manifest = T.Manifest.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(manifest.AsSpan(4), 2);

        UnsupportedFormatException refused = await Assert.ThrowsAsync<UnsupportedFormatException>(async () =>
            await Dataset.OpenAsync(MemoryStorage.FromLog(manifest, []), T.Options(), T.Ct));

        Assert.Equal(new FormatVersion(2), refused.Found);
        Assert.Equal(FormatVersion.Current, refused.Supported);
        Assert.Contains("format version 2", refused.Message, StringComparison.Ordinal);
        Assert.Contains("1 to 1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_damaged_manifest_refuses_and_segments_without_one_refuse()
    {
        byte[] manifest = T.Manifest.ToArray();
        manifest[10] ^= 1;
        await Assert.ThrowsAsync<LogVerificationException>(async () => await Dataset.OpenAsync(MemoryStorage.FromLog(manifest, []), T.Options(), T.Ct));

        (MemoryStorage storage, _) = await LogAsync(2);
        (_, List<byte[]> segments) = await T.CopyLogAsync(storage);
        await Assert.ThrowsAsync<LogVerificationException>(async () =>
            await Dataset.OpenAsync(MemoryStorage.FromLog(ReadOnlyMemory<byte>.Empty, segments.Select(s => (ReadOnlyMemory<byte>)s)), T.Options(), T.Ct));
    }

    [Fact]
    public async Task create_needs_empty_storage_and_open_needs_a_dataset()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Dataset.OpenAsync(new MemoryStorage(), T.Options(), T.Ct));

        (MemoryStorage storage, DatasetOptions options) = await LogAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Dataset.CreateAsync(storage, T.Id, options, T.Ct));

        await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
        Assert.Equal(T.Id, reopened.Id);
        Assert.Equal(new Position(1), reopened.Head);
    }

    [Fact]
    public async Task a_segment_of_another_dataset_is_not_this_logs()
    {
        (MemoryStorage storage, _) = await LogAsync(3);
        (_, List<byte[]> segments) = await T.CopyLogAsync(storage);
        byte[] foreign = LogFormatTestBytes.ManifestOf(new DatasetId(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        // The segment's header verifies and names T.Id: a foreign segment,
        // refused, never read as a torn one.
        LogVerificationException refused = await Assert.ThrowsAsync<LogVerificationException>(async () =>
            await Dataset.OpenAsync(MemoryStorage.FromLog(foreign, segments.Select(s => (ReadOnlyMemory<byte>)s)), T.Options(), T.Ct));
        Assert.Contains(T.Id.ToString(), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task opening_reads_bodies_only_after_the_newest_checkpoint()
    {
        MemoryStorage memory = new();
        CountingStorage counting = new(memory);
        DatasetOptions options = T.Options();

        await using (Dataset dataset = await Dataset.CreateAsync(counting, T.Id, options, T.Ct))
        {
            // Bodies of a realistic size: here the bytes are the quads, not the headers.
            for (int i = 0; i < 200; i++)
            {
                CommitRequest request = new();

                for (int q = 0; q < 20; q++)
                {
                    request.Assert(T.Iri("s" + i), T.Iri("p" + q), T.Literal(new string('x', 400) + i + "-" + q));
                }

                await dataset.CommitAsync(request, T.Ct);
            }

            await dataset.CheckpointAsync(new Position(190), T.Ct);
        }

        long logBytes = (await memory.Log.ListSegmentsAsync(T.Ct)).Sum(s => s.Length.Value);
        counting.Reset();

        await using (Dataset reopened = await Dataset.OpenAsync(counting, options, T.Ct))
        {
            Assert.Equal(new Position(200), reopened.Head);
            using DatasetView view = reopened.Pin();
            Assert.Equal(4000, T.All(view).Count);
        }

        Assert.True(counting.LogBytesRead < logBytes / 2, "open read " + counting.LogBytesRead + " of " + logBytes + " log bytes");

        await memory.Derived.DeleteAsync((await memory.Derived.ListAsync(T.Ct)).Single(), T.Ct);
        counting.Reset();

        await using (Dataset reopened = await Dataset.OpenAsync(counting, options, T.Ct))
        {
            Assert.Equal(new Position(200), reopened.Head);
        }

        Assert.True(counting.LogBytesRead >= logBytes - (await memory.Log.ListSegmentsAsync(T.Ct)).Count * 1024, "without a checkpoint, open reads every body");
    }

    [Fact]
    public async Task an_erasure_commit_refuses_to_open_until_erasure_mode_exists()
    {
        MemoryStorage storage = new();

        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, T.Options(), T.Ct))
        {
            await dataset.CommitAsync(Quad(0), T.Ct);
            await dataset.AppendErasureAsync(7, new CommitMetadata { Agent = T.Iri("me") }, T.Ct);
        }

        LogVerificationException refused = await Assert.ThrowsAsync<LogVerificationException>(async () => await Dataset.OpenAsync(storage, T.Options(), T.Ct));
        Assert.Contains("erasure mode", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task segments_beyond_a_copy_point_are_abandoned_and_the_copy_continues()
    {
        (MemoryStorage storage, DatasetOptions options) = await LogAsync(40, segmentBytes: 2048);
        (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(storage);
        Assert.True(segments.Count >= 4);

        // Segment 1 was copied while it was being written: no trailer, and cut
        // inside a record. Segments 2 and on were copied later, whole.
        List<ReadOnlyMemory<byte>> copied = [segments[0], segments[1].AsMemory(0, segments[1].Length - LogFormat.TrailerLength - 40), .. segments.Skip(2).Select(s => (ReadOnlyMemory<byte>)s)];
        MemoryStorage copy = MemoryStorage.FromLog(manifest, copied);

        long head;

        await using (Dataset opened = await Dataset.OpenAsync(copy, options, T.Ct))
        {
            head = opened.Head.Value;
            Assert.True(head > 0 && head < 40);
            CommitResult next = await opened.CommitAsync(Quad(1000), T.Ct);
            Assert.Equal(new Position(head + 1), next.Position);
        }

        await using Dataset again = await Dataset.OpenAsync(copy, options, T.Ct);
        Assert.Equal(new Position(head + 1), again.Head);
        using DatasetView view = again.Pin();
        Assert.Equal((int)head + 1, T.All(view).Count);
    }

    [Fact]
    public async Task a_segment_torn_inside_its_header_is_a_torn_tail()
    {
        (MemoryStorage storage, DatasetOptions options) = await LogAsync(30, segmentBytes: 2048);
        (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(storage);

        // The crash came while the newest segment's header was being written.
        int sealedHead = 0;

        for (int keep = 0; keep < LogFormat.SegmentHeaderLength; keep += 13)
        {
            List<ReadOnlyMemory<byte>> copied = [.. segments.Take(segments.Count - 1).Select(s => (ReadOnlyMemory<byte>)s), segments[^1].AsMemory(0, keep)];
            MemoryStorage copy = MemoryStorage.FromLog(manifest, copied);

            await using (Dataset opened = await Dataset.OpenAsync(copy, options, T.Ct))
            {
                sealedHead = (int)opened.Head.Value;
                Assert.Equal(CommitOutcome.Committed, (await opened.CommitAsync(Quad(500), T.Ct)).Outcome);
            }

            await using Dataset again = await Dataset.OpenAsync(copy, options, T.Ct);
            Assert.Equal(new Position(sealedHead + 1), again.Head);
        }
    }

    [Fact]
    public async Task a_segment_that_starts_a_different_history_refuses()
    {
        (MemoryStorage storage, DatasetOptions options) = await LogAsync(30, segmentBytes: 2048);
        (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(storage);

        // Segment 2 dropped from the copy: segment 1's closed trailer is then
        // followed by a segment that does not continue it.
        List<ReadOnlyMemory<byte>> missing = [.. segments.Where((_, i) => i != 2).Select(s => (ReadOnlyMemory<byte>)s)];
        await Assert.ThrowsAsync<LogVerificationException>(async () => await Dataset.OpenAsync(MemoryStorage.FromLog(manifest, missing), options, T.Ct));
    }

    [Fact]
    public async Task a_damaged_body_before_the_last_commit_refuses_rather_than_truncating()
    {
        (MemoryStorage storage, DatasetOptions options) = await LogAsync(5);
        (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(storage);
        byte[] log = segments.Single();

        // The first record's body, which a later commit follows.
        int body = LogFormat.SegmentHeaderLength + LogFormat.RecordHeaderLength;
        log[body + 3] ^= 0x40;

        await Assert.ThrowsAsync<LogVerificationException>(async () => await Dataset.OpenAsync(MemoryStorage.FromLog(manifest, [log]), options, T.Ct));
    }

    /// <summary>Storage that counts the log bytes read through it.</summary>
    private sealed class CountingStorage(IStorage inner) : IStorage, ISegmentStore
    {
        private long _read;

        public long LogBytesRead => Interlocked.Read(ref _read);

        public ISegmentStore Log => this;

        public IDerivedStore Derived => inner.Derived;

        public Durability Durability => inner.Log.Durability;

        public void Reset() => Interlocked.Exchange(ref _read, 0);

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) => inner.Log.ListSegmentsAsync(cancellationToken);

        public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken) => inner.Log.CreateSegmentAsync(cancellationToken);

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => inner.Log.AppendAsync(segment, bytes, cancellationToken);

        public ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken) => inner.Log.FlushAsync(segment, cancellationToken);

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) => inner.Log.SealAsync(segment, cancellationToken);

        public async ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> bytes = await inner.Log.ReadRangeAsync(segment, offset, length, cancellationToken);
            Interlocked.Add(ref _read, bytes.Length);
            return bytes;
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => inner.Log.ReadManifestAsync(cancellationToken);

        public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken) => inner.Log.WriteManifestAsync(manifest, cancellationToken);
    }
}

/// <summary>The manifest of another dataset, through the format's internals.</summary>
internal static class LogFormatTestBytes
{
    public static byte[] ManifestOf(DatasetId id) => LogFormat.EncodeManifest(id);
}
