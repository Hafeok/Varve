// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.Tests.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>A log copied into an object store, as a writer would have left it.</summary>
internal static class ObjectLog
{
    /// <summary>
    /// Writes a manifest and segments: every segment but the last
    /// consolidated, the last as one chunk of epoch 1, so the store opens it
    /// as the open segment.
    /// </summary>
    public static void Write(MemoryObjectStore store, ReadOnlyMemory<byte> manifest, IReadOnlyList<ReadOnlyMemory<byte>> segments)
    {
        store.Put("log/MANIFEST", manifest.ToArray());

        for (int i = 0; i < segments.Count; i++)
        {
            string number = i.ToString("D8", CultureInfo.InvariantCulture);

            if (i < segments.Count - 1)
            {
                store.Put("log/" + number + ".seg", segments[i].ToArray());
            }
            else
            {
                store.Put("log/" + number + ".chunks/00000000", ChunkHeader.Compose(ChunkFlags.Opens, 1, i, 0, 0, segments[i].Span, ChunkHeader.Nothing(), out _));
            }
        }
    }
}

/// <summary>
/// Every milestone 4 and 6a property of the file backend, re-run on the
/// object-storage backend at the same iteration counts (#92's gate).
/// </summary>
public sealed class ObjectPropertyTests
{
    private const int Iterations = 40;

    [Fact]
    public async Task the_store_in_objects_agrees_with_the_reference_model()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(storage: await objects.OpenAsync());
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations,
            print: script => script.ToString());
    }

    [Fact]
    public async Task the_store_in_objects_agrees_with_the_model_with_many_records_and_segments()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(maxRecordBytes: 64, segmentBytes: 1024, storage: await objects.OpenAsync());
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations / 3,
            print: script => script.ToString());
    }

    /// <summary>
    /// With maintenance after every step, which on this backend also
    /// consolidates every sealed segment: the reopen reads `.seg` objects.
    /// </summary>
    [Fact]
    public async Task the_store_in_objects_agrees_with_the_model_with_the_projection_in_derived_runs_and_segments_consolidated()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(maxRecordBytes: 256, segmentBytes: 2048, storage: await objects.OpenAsync(), memtableLimit: 4);
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations / 3,
            print: script => script.ToString());
    }

    /// <summary>
    /// Records, and I8 at every position, in objects: the log cut at every
    /// structural boundary of format 1 and the byte either side, laid out as
    /// objects, opens at exactly the commits whose closing record the cut
    /// holds whole; every seventh cut is continued by a new writer and
    /// reopened.
    /// </summary>
    [Theory]
    [InlineData(1 << 20, 64L << 20)]
    [InlineData(64, 1024L)]
    public async Task a_log_in_objects_cut_at_its_boundaries_recovers_to_the_last_closed_commit(int maxRecordBytes, long segmentBytes)
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(maxRecordBytes, segmentBytes, await objects.OpenAsync());
                await harness.RunAsync(script);
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);
                (List<long> cuts, List<long> closings) = FilePropertyTests.Cuts(segments);
                long last = 0;

                for (int i = 0; i < cuts.Count; i++)
                {
                    long length = cuts[i];
                    long expected = closings.Count(end => end <= length);
                    await using ObjectHarness copy = new();
                    ObjectLog.Write(copy.Store, manifest, FilePropertyTests.Prefix(segments, length));
                    Backend storage = await copy.OpenAsync();

                    await using (Dataset dataset = await Dataset.OpenAsync(storage, harness.Options, T.Ct))
                    {
                        Assert.Equal(expected, dataset.Head.Value);

                        using (DatasetView view = dataset.Pin())
                        {
                            Harness.Same(Harness.Rendered(harness.Model.History[(int)expected]), harness.Rendered(view), "cut at " + length);
                        }

                        if (i % 7 == 0 || i == cuts.Count - 1)
                        {
                            CommitResult next = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("cut"), T.Integer("1")), T.Ct);
                            Assert.Equal(CommitOutcome.Committed, next.Outcome);
                            await dataset.DisposeAsync();
                            await storage.DisposeAsync();

                            await using Dataset again = await Dataset.OpenAsync(await copy.OpenAsync(), harness.Options, T.Ct);
                            Assert.Equal(next.Position, again.Head);
                        }
                    }

                    last = expected;
                }

                Assert.Equal(harness.Model.Head, last);
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// Determinism across backends (§10, #92's gate): the same requests, with
    /// an injected clock and the same dataset id, give byte-identical logs on
    /// files and in objects, and once maintenance has consolidated them, the
    /// same segment files as `.seg` objects; and the same checkpoints as the
    /// same extents and the same catalog, byte for byte but for the slot its
    /// cursor records, which is the backend's own (ADR 0139).
    /// </summary>
    [Fact]
    public async Task the_same_requests_give_byte_identical_logs_on_files_and_in_objects()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new(flushes: false);
                await using ObjectHarness objects = new();
                List<byte[]> onFiles;
                List<byte[]> inObjects;
                List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> extentsOnFiles;

                await using (Harness files = await Harness.StartAsync(maxRecordBytes: 256, segmentBytes: 2048, storage: await directory.OpenAsync()))
                {
                    await files.RunAsync(script);
                    onFiles = (await T.CopyLogAsync(files.Storage)).Segments;
                    extentsOnFiles = await T.CheckpointBlobsAsync(files.Storage);
                }

                Backend storage = await objects.OpenAsync();

                await using (Harness harness = await Harness.StartAsync(maxRecordBytes: 256, segmentBytes: 2048, storage: storage))
                {
                    await harness.RunAsync(script);
                    inObjects = (await T.CopyLogAsync(storage)).Segments;
                    await harness.Dataset.MaintainAsync(T.Ct);
                }

                Assert.Equal(onFiles.Count, inObjects.Count);

                for (int i = 0; i < onFiles.Count; i++)
                {
                    Assert.True(onFiles[i].AsSpan().SequenceEqual(inObjects[i]), "segment " + i + " differs");
                    string name = "log/" + i.ToString("D8", CultureInfo.InvariantCulture) + ".seg";

                    if (i < onFiles.Count - 1)
                    {
                        Assert.True(onFiles[i].AsSpan().SequenceEqual(objects.Store.Get(name)), name + " differs from the file");
                    }
                }

                Assert.True((await File.ReadAllBytesAsync(Path.Combine(directory.Path, "log", "MANIFEST"), T.Ct)).AsSpan().SequenceEqual(objects.Store.Get("log/MANIFEST")), "the manifests differ");

                List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> extentsInObjects = await T.CheckpointBlobsAsync(storage);
                Assert.Equal(extentsOnFiles.Select(b => b.Name.Value).Order(StringComparer.Ordinal), extentsInObjects.Select(b => b.Name.Value).Order(StringComparer.Ordinal));

                foreach ((BlobName name, ReadOnlyMemory<byte> bytes) in extentsOnFiles)
                {
                    Assert.True(bytes.Span.SequenceEqual(extentsInObjects.Single(b => b.Name == name).Bytes.Span), name.Value + " differs between the backends");
                }
            },
            iter: ModelTests.Iterations / 4,
            print: script => script.ToString());
    }

    /// <summary>I6 in objects: a byte changed in any commit header but the last refuses to open.</summary>
    [Fact]
    public async Task a_changed_header_in_objects_refuses_to_open()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(storage: await objects.OpenAsync());
                await harness.RunAsync(script);
                harness.Options.TornWindow = 0;
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);

                if (segments.Count == 0 || harness.Model.Head < 2)
                {
                    return;
                }

                byte[] changed = (byte[])segments[0].Clone();
                int body = LogFormat.SegmentHeaderLength + LogFormat.RecordHeaderLength;
                int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(LogFormat.SegmentHeaderLength));
                int headerLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(body + length - 4));
                changed[body + length - 4 - headerLength + 9] ^= 0x01;

                await using ObjectHarness copy = new();
                ObjectLog.Write(copy.Store, manifest, [changed]);
                Backend storage = await copy.OpenAsync();
                await Assert.ThrowsAsync<LogVerificationException>(async () => await Dataset.OpenAsync(storage, harness.Options, T.Ct));
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// A chunk whose bytes or header were changed refuses the open, before
    /// the store reads a byte of the log (ADR 0135): the backend's, which
    /// reads the newest chunk, or the dataset's, which reads the rest. Only
    /// chunks of segments not consolidated are read; a consolidated segment
    /// is its one object.
    /// </summary>
    [Fact]
    public async Task a_changed_chunk_refuses_to_open()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                await using (Harness harness = await Harness.StartAsync(storage: await objects.OpenAsync()))
                {
                    await harness.RunAsync(script);
                }

                IReadOnlyList<string> names = objects.Store.Names();
                string[] chunks = [.. names.Where(n => n.Contains(".chunks/", StringComparison.Ordinal) && !names.Contains(n[..n.IndexOf(".chunks/", StringComparison.Ordinal)] + ".seg"))];
                int seed = script.ToString().Length;
                string target = chunks[seed % chunks.Length];
                byte[] bytes = objects.Store.Get(target)!;
                bytes[(seed * 7) % bytes.Length] ^= 0x10;
                objects.Store.Put(target, bytes);

                await Assert.ThrowsAsync<LogVerificationException>(async () =>
                {
                    Backend storage = await objects.OpenAsync();
                    await using Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct);
                });
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>I6 in objects: two continuations of one prefix diverge at the first position they differ.</summary>
    [Fact]
    public async Task two_continuations_in_objects_are_divergent()
    {
        await Gen.Select(Generators.CommitsOnly, Gen.Int[1, 3]).SampleAsync(
            async (script, extra) =>
            {
                await using ObjectHarness objects = new();
                await using Harness harness = await Harness.StartAsync(storage: await objects.OpenAsync());
                await harness.RunAsync(script);
                long prefix = harness.Dataset.Head.Value;
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);
                await using ObjectHarness other = new();
                ObjectLog.Write(other.Store, manifest, [.. segments.Select(s => (ReadOnlyMemory<byte>)s)]);
                Backend copy = await other.OpenAsync();

                Assert.Null(await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));

                await using (Dataset continued = await Dataset.OpenAsync(copy, harness.Options, T.Ct))
                {
                    for (int i = 0; i < extra; i++)
                    {
                        string n = i.ToString(CultureInfo.InvariantCulture);
                        await harness.Dataset.CommitAsync(new CommitRequest().Assert(T.Iri("here"), T.Iri("p0"), T.Integer(n)), T.Ct);
                        await continued.CommitAsync(new CommitRequest().Assert(T.Iri("there"), T.Iri("p0"), T.Integer(n)), T.Ct);
                    }
                }

                Assert.Equal(new Position(prefix + 1), await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));
            },
            iter: Iterations,
            print: x => x.ToString());
    }

    /// <summary>§7's failed state, in objects: a projection that throws fails the dataset until a rebuild.</summary>
    [Fact]
    public async Task the_failed_state_in_objects_holds_until_a_rebuild()
    {
        await using ObjectHarness objects = new();
        Backend storage = await objects.OpenAsync();
        DatasetOptions options = T.Options();
        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("a"), T.Iri("p"), T.Iri("o")), T.Ct);

        options.DefaultProjectionFault = position => position == 2;
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("b"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);
        Assert.True(dataset.IsFailed);
        Assert.Throws<DatasetUnavailableException>(() => dataset.Pin());
        Assert.Equal(CommitOutcome.Unavailable, (await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("c"), T.Iri("p"), T.Iri("o")), T.Ct)).Outcome);

        options.DefaultProjectionFault = null;
        await dataset.RebuildDefaultProjectionAsync(T.Ct);
        Assert.False(dataset.IsFailed);
        using DatasetView view = dataset.Pin();
        Assert.Equal(2, T.All(view).Count);
    }
}
