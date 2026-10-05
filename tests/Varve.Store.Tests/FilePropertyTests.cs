// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Milestone 4's properties, re-run on the file backend at their iteration
/// counts (milestone 6a): the model property — which checks I2, I3, I5, I7,
/// I8, R1–R4 and the settings fold — with and without the projection in
/// derived runs, the record property at every byte offset, determinism over
/// whole <c>log/</c> directories, I6, divergence, and the failed state.
/// </summary>
/// <remarks>
/// Every case runs in a directory of its own under the temporary directory,
/// on real files with real flushes.
/// </remarks>
public sealed class FilePropertyTests
{
    private const int Iterations = 40;

    /// <summary>A dataset directory holding a copy of a log: its manifest and its segments.</summary>
    private static void WriteLog(string root, ReadOnlyMemory<byte> manifest, IReadOnlyList<ReadOnlyMemory<byte>> segments)
    {
        string log = Path.Combine(root, "log");
        Directory.CreateDirectory(log);
        File.WriteAllBytes(Path.Combine(log, "MANIFEST"), manifest.ToArray());

        for (int i = 0; i < segments.Count; i++)
        {
            string path = Path.Combine(log, i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture) + ".seg");
            File.WriteAllBytes(path, segments[i].ToArray());

            if (i < segments.Count - 1)
            {
                File.SetAttributes(path, FileAttributes.ReadOnly);
            }
        }
    }

    private static async Task<T2> WithCopyAsync<T2>(ReadOnlyMemory<byte> manifest, IReadOnlyList<ReadOnlyMemory<byte>> segments, Func<FileStorage, Task<T2>> body)
    {
        await using TemporaryDirectory copy = new();
        WriteLog(copy.Path, manifest, segments);
        FileStorage storage = await copy.OpenAsync();
        return await body(storage);
    }

    [Fact]
    public async Task the_store_on_files_agrees_with_the_reference_model()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new();
                await using Harness harness = await Harness.StartAsync(storage: await directory.OpenAsync());
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations,
            print: script => script.ToString());
    }

    [Fact]
    public async Task the_store_on_files_agrees_with_the_model_with_many_records_and_segments()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new();
                await using Harness harness = await Harness.StartAsync(maxRecordBytes: 64, segmentBytes: 1024, storage: await directory.OpenAsync());
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations / 3,
            print: script => script.ToString());
    }

    [Fact]
    public async Task the_store_on_files_agrees_with_the_model_with_the_projection_in_derived_runs()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new();
                await using Harness harness = await Harness.StartAsync(storage: await directory.OpenAsync(), memtableLimit: 4);
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations / 3,
            print: script => script.ToString());
    }

    /// <summary>
    /// Records, and I8 at every position, on files: the log cut at every byte
    /// offset and written to a directory of its own opens at the last commit
    /// it holds closed, with the model's state there; a sample of cuts is
    /// continued and reopened.
    /// </summary>
    [Theory]
    [InlineData(1 << 20, 64L << 20)]
    [InlineData(64, 1024L)]
    public async Task a_log_on_files_cut_at_any_byte_recovers_to_the_last_closed_commit(int maxRecordBytes, long segmentBytes)
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new();
                await using Harness harness = await Harness.StartAsync(maxRecordBytes, segmentBytes, await directory.OpenAsync());
                await harness.RunAsync(script);
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);
                long total = segments.Sum(s => (long)s.Length);
                long previous = 0;

                for (long length = 0; length <= total; length++)
                {
                    List<ReadOnlyMemory<byte>> cut = Prefix(segments, length);

                    previous = await WithCopyAsync(manifest, cut, async storage =>
                    {
                        await using Dataset opened = await Dataset.OpenAsync(storage, harness.Options, T.Ct);

                        if (opened.Head.Value != previous && opened.Head.Value != previous + 1)
                        {
                            throw new InvalidOperationException("Cutting at " + length + " jumped from " + previous + " to " + opened.Head + ".");
                        }

                        long head = opened.Head.Value;

                        using (DatasetView view = opened.Pin())
                        {
                            Harness.Same(Harness.Rendered(harness.Model.History[(int)opened.Head.Value]), harness.Rendered(view), "cut at " + length);
                        }

                        if (length % 97 == 0 || length == total)
                        {
                            CommitResult next = await opened.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("cut"), T.Integer("1")), T.Ct);
                            Assert.Equal(CommitOutcome.Committed, next.Outcome);

                            await using Dataset again = await Dataset.OpenAsync(storage, harness.Options, T.Ct);
                            Assert.Equal(next.Position, again.Head);
                        }

                        return head;
                    });
                }

                Assert.Equal(harness.Model.Head, previous);
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// Determinism on files: the same requests, with an injected clock and the
    /// same dataset id, give byte-identical <c>log/</c> directories — the same
    /// file names and the same bytes in each (§10, for crash-free histories).
    /// </summary>
    [Fact]
    public async Task the_same_requests_give_byte_identical_log_directories()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory first = new();
                await using TemporaryDirectory second = new();

                foreach (TemporaryDirectory directory in new[] { first, second })
                {
                    await using Harness harness = await Harness.StartAsync(maxRecordBytes: 256, segmentBytes: 2048, storage: await directory.OpenAsync());
                    await harness.RunAsync(script);
                }

                string[] a = Files(first.Path);
                string[] b = Files(second.Path);
                Assert.Equal(a, b);

                foreach (string file in a)
                {
                    Assert.True(
                        ReadShared(Path.Combine(first.Path, "log", file)).AsSpan().SequenceEqual(ReadShared(Path.Combine(second.Path, "log", file))),
                        file + " differs");
                }
            },
            iter: ModelTests.Iterations / 4,
            print: script => script.ToString());

        // The storage still holds its active segment open for writing, and on
        // Windows a reader must share write access to open it beside a writer.
        static byte[] ReadShared(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using MemoryStream copy = new();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        static string[] Files(string root) =>
            [.. Directory.GetFiles(Path.Combine(root, "log")).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)];
    }

    /// <summary>I6 on files: a byte changed in any commit header but the last refuses to open.</summary>
    [Fact]
    public async Task a_changed_header_on_files_refuses_to_open()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new();
                await using Harness harness = await Harness.StartAsync(storage: await directory.OpenAsync());
                await harness.RunAsync(script);
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);

                if (segments.Count == 0 || harness.Model.Head < 2)
                {
                    return;
                }

                // A byte of the first commit's header, which every later one chains to.
                byte[] changed = (byte[])segments[0].Clone();
                int body = LogFormat.SegmentHeaderLength + LogFormat.RecordHeaderLength;
                int length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(LogFormat.SegmentHeaderLength));
                int headerLength = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(body + length - 4));
                changed[body + length - 4 - headerLength + 9] ^= 0x01;

                await Assert.ThrowsAsync<LogVerificationException>(() => WithCopyAsync(manifest, [changed], async storage =>
                {
                    await using Dataset opened = await Dataset.OpenAsync(storage, harness.Options, T.Ct);
                    return opened.Head.Value;
                }));
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>I6 on files: two continuations of one prefix diverge at the first position they differ.</summary>
    [Fact]
    public async Task two_continuations_on_files_are_divergent()
    {
        await Gen.Select(Generators.CommitsOnly, Gen.Int[1, 3]).SampleAsync(
            async (script, extra) =>
            {
                await using TemporaryDirectory directory = new();
                await using TemporaryDirectory copyDirectory = new();
                await using Harness harness = await Harness.StartAsync(storage: await directory.OpenAsync());
                await harness.RunAsync(script);
                long prefix = harness.Dataset.Head.Value;
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);
                WriteLog(copyDirectory.Path, manifest, [.. segments.Select(s => (ReadOnlyMemory<byte>)s)]);
                FileStorage copy = await copyDirectory.OpenAsync();

                Assert.Null(await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));

                await using (Dataset other = await Dataset.OpenAsync(copy, harness.Options, T.Ct))
                {
                    for (int i = 0; i < extra; i++)
                    {
                        string n = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        await harness.Dataset.CommitAsync(new CommitRequest().Assert(T.Iri("here"), T.Iri("p0"), T.Integer(n)), T.Ct);
                        await other.CommitAsync(new CommitRequest().Assert(T.Iri("there"), T.Iri("p0"), T.Integer(n)), T.Ct);
                    }
                }

                Assert.Equal(new Position(prefix + 1), await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));
            },
            iter: Iterations,
            print: x => x.ToString());
    }

    /// <summary>§7's failed state, on files: a projection that throws fails the dataset until a rebuild.</summary>
    [Fact]
    public async Task the_failed_state_on_files_holds_until_a_rebuild()
    {
        await using TemporaryDirectory directory = new();
        FileStorage storage = await directory.OpenAsync();
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

    private static List<ReadOnlyMemory<byte>> Prefix(List<byte[]> segments, long length)
    {
        List<ReadOnlyMemory<byte>> kept = [];

        foreach (byte[] segment in segments)
        {
            if (length <= 0)
            {
                break;
            }

            int take = (int)Math.Min(length, segment.Length);
            kept.Add(segment.AsMemory(0, take));
            length -= take;
        }

        return kept;
    }
}
