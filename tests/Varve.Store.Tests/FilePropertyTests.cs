// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
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
        await using TemporaryDirectory copy = new(flushes: false);
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
                await using TemporaryDirectory directory = new(flushes: false);
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
                await using TemporaryDirectory directory = new(flushes: false);
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
                await using TemporaryDirectory directory = new(flushes: false);
                await using Harness harness = await Harness.StartAsync(storage: await directory.OpenAsync(), memtableLimit: 4);
                await harness.RunAsync(script);
                await harness.VerifyRunAsync(T.Ct);
            },
            iter: ModelTests.Iterations / 3,
            print: script => script.ToString());
    }

    /// <summary>
    /// Records, and I8 at every position, on files: the log cut and written to a
    /// directory of its own opens at exactly the commits whose closing record the
    /// cut holds whole, with the model's state there; every seventh cut is
    /// continued and reopened.
    /// </summary>
    /// <remarks>
    /// The cuts are every structural boundary of format version 1 and the byte
    /// either side of it — segment starts and header ends, record starts, header
    /// ends and body ends, trailer starts and ends — and 16 offsets at random.
    /// Cutting at every byte stays where it costs nothing: on the memory backend
    /// (<see cref="LogPropertyTests"/>), and through the file backend's own code
    /// at every byte of every write in the fault-injection suite. On real files a
    /// cut is a directory written, opened, recovered and deleted, and at every byte
    /// that was 220,000 directories a run: minutes on Linux, hours on Windows,
    /// where creating, flushing and deleting files costs ten times as much. The
    /// maintainer narrowed it on the 6a pull request.
    /// </remarks>
    [Theory]
    [InlineData(1 << 20, 64L << 20)]
    [InlineData(64, 1024L)]
    public async Task a_log_on_files_cut_at_its_boundaries_recovers_to_the_last_closed_commit(int maxRecordBytes, long segmentBytes)
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using TemporaryDirectory directory = new(flushes: false);
                await using Harness harness = await Harness.StartAsync(maxRecordBytes, segmentBytes, await directory.OpenAsync());
                await harness.RunAsync(script);
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(harness.Storage);
                (List<long> cuts, List<long> closings) = Cuts(segments);
                long last = 0;

                for (int i = 0; i < cuts.Count; i++)
                {
                    long length = cuts[i];
                    List<ReadOnlyMemory<byte>> cut = Prefix(segments, length);
                    long expected = closings.Count(end => end <= length);

                    long opened = await WithCopyAsync(manifest, cut, async storage =>
                    {
                        await using Dataset dataset = await Dataset.OpenAsync(storage, harness.Options, T.Ct);

                        long head = dataset.Head.Value;

                        if (head != expected)
                        {
                            throw new InvalidOperationException("Cutting at " + length + " opened at " + head + " where " + expected + " commits are closed.");
                        }

                        using (DatasetView view = dataset.Pin())
                        {
                            Harness.Same(Harness.Rendered(harness.Model.History[(int)head]), harness.Rendered(view), "cut at " + length);
                        }

                        if (i % 7 == 0 || i == cuts.Count - 1)
                        {
                            CommitResult next = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("cut"), T.Integer("1")), T.Ct);
                            Assert.Equal(CommitOutcome.Committed, next.Outcome);

                            await using Dataset again = await Dataset.OpenAsync(storage, harness.Options, T.Ct);
                            Assert.Equal(next.Position, again.Head);
                        }

                        return head;
                    });

                    last = opened;
                }

                Assert.Equal(harness.Model.Head, last);
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    // Where a cut is worth making in a log of format version 1, as offsets into
    // the segments laid end to end: every structural boundary and the byte
    // either side, and 16 offsets at random, seeded by the log's length so that
    // a run can be repeated. Also where each commit's closing record ends.
    private static (List<long> Cuts, List<long> Closings) Cuts(List<byte[]> segments)
    {
        SortedSet<long> boundaries = [];
        List<long> closings = [];
        long total = segments.Sum(s => (long)s.Length);
        long origin = 0;

        foreach (byte[] segment in segments)
        {
            boundaries.Add(origin);
            boundaries.Add(origin + Math.Min(segment.Length, LogFormat.SegmentHeaderLength));
            int at = LogFormat.SegmentHeaderLength;

            while (LogFormat.IsRecordHeader(segment.AsSpan(Math.Min(at, segment.Length)), out _))
            {
                int end = at + LogFormat.RecordHeaderLength + (int)BinaryPrimitives.ReadUInt32LittleEndian(segment.AsSpan(at));
                boundaries.Add(origin + at);
                boundaries.Add(origin + at + LogFormat.RecordHeaderLength);
                boundaries.Add(origin + end);

                if ((segment[at + 5] & LogFormat.ClosingFlag) != 0)
                {
                    closings.Add(origin + end);
                }

                at = end;
            }

            // What follows the records is the trailer, or nothing.
            boundaries.Add(origin + segment.Length);
            origin += segment.Length;
        }

        SortedSet<long> cuts = [];

        foreach (long boundary in boundaries)
        {
            for (long offset = boundary - 1; offset <= boundary + 1; offset++)
            {
                if (offset >= 0 && offset <= total)
                {
                    cuts.Add(offset);
                }
            }
        }

        Random random = new((int)(total % int.MaxValue));

        for (int i = 0; i < 16 && total > 0; i++)
        {
            cuts.Add(random.NextInt64(total + 1));
        }

        return ([.. cuts], closings);
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
                await using TemporaryDirectory first = new(flushes: false);
                await using TemporaryDirectory second = new(flushes: false);

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
                await using TemporaryDirectory directory = new(flushes: false);
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
                await using TemporaryDirectory directory = new(flushes: false);
                await using TemporaryDirectory copyDirectory = new(flushes: false);
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
        await using TemporaryDirectory directory = new(flushes: false);
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
