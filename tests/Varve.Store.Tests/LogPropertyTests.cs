// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The properties of §10 that are about the log's bytes: records (a crash at
/// any boundary recovers to the last closed commit), I8 at every position,
/// determinism, and I6.
/// </summary>
public class LogPropertyTests
{
    private const int Iterations = 40;

    internal static async Task<List<byte[]>> SegmentsAsync(IStorage storage)
    {
        List<byte[]> segments = [];

        foreach (SegmentInfo segment in await storage.Log.ListSegmentsAsync(T.Ct))
        {
            segments.Add((await storage.Log.ReadRangeAsync(segment.Id, new ByteOffset(0), segment.Length, T.Ct)).ToArray());
        }

        return segments;
    }

    /// <summary>The log cut after <paramref name="length"/> bytes, as a copy would be if taken mid-write.</summary>
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

    /// <summary>
    /// Records, and I8 at every position. The log is cut at every byte offset;
    /// each cut must open, at the last commit it holds closed, with exactly the
    /// state the model had there — which is a default projection rebuilt from
    /// position 0, compared with the one the run maintained incrementally. The
    /// readable head may only grow, one position at a time, as the cut moves
    /// on. A sample of cuts is then continued, which exercises recovery's seal
    /// and fresh segment.
    /// </summary>
    [Theory]
    [InlineData(1 << 20, 64L << 20)]
    [InlineData(64, 1024L)]
    public async Task a_log_cut_at_any_byte_recovers_to_the_last_closed_commit(int maxRecordBytes, long segmentBytes)
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync(maxRecordBytes, segmentBytes);
                await harness.RunAsync(script);
                List<byte[]> segments = await SegmentsAsync(harness.Storage);
                long total = segments.Sum(s => (long)s.Length);
                long previous = 0;

                for (long length = 0; length <= total; length++)
                {
                    MemoryStorage cut = MemoryStorage.FromLog(T.Manifest, Prefix(segments, length));
                    await using Dataset opened = await T.OpenOrCreate(cut, harness.Options);

                    if (opened.Head.Value != previous && opened.Head.Value != previous + 1)
                    {
                        throw new InvalidOperationException("Cutting at " + length + " jumped from " + previous + " to " + opened.Head + ".");
                    }

                    previous = opened.Head.Value;
                    using DatasetView view = opened.Pin();
                    Harness.Same(Harness.Rendered(harness.Model.History[(int)opened.Head.Value]), harness.Rendered(view), "cut at " + length);

                    if (length % 97 == 0 || length == total)
                    {
                        // Recovery must leave a log that can be continued and reopened.
                        CommitResult next = await opened.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("cut"), T.Integer("1")), T.Ct);
                        Assert.Equal(CommitOutcome.Committed, next.Outcome);
                        Assert.Equal(opened.Head, next.Position);

                        await using Dataset again = await T.OpenOrCreate(cut, harness.Options);
                        Assert.Equal(next.Position, again.Head);
                    }
                }

                Assert.Equal(harness.Model.Head, previous);
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// Determinism: the same request sequence, with an injected clock, gives
    /// byte-identical logs on two stores — for crash-free histories
    /// (specification 1.2, §10).
    /// </summary>
    [Fact]
    public async Task the_same_requests_give_the_same_log_bytes()
    {
        await Generators.Scripts.SampleAsync(
            async script =>
            {
                await using Harness first = await Harness.StartAsync();
                await using Harness second = await Harness.StartAsync();
                await first.RunAsync(script);
                await second.RunAsync(script);

                List<byte[]> a = await SegmentsAsync(first.Storage);
                List<byte[]> b = await SegmentsAsync(second.Storage);
                Assert.Equal(a.Count, b.Count);

                for (int i = 0; i < a.Count; i++)
                {
                    Assert.True(a[i].AsSpan().SequenceEqual(b[i]), "segment " + i + " differs");
                }
            },
            iter: ModelTests.Iterations / 4,
            print: script => script.ToString());
    }

    /// <summary>
    /// I6: every byte of every commit header, when flipped, makes the log
    /// refuse to open — except the last commit's, which reads as a torn tail
    /// and opens at the position before it. Format version 1 cannot tell a
    /// change to the last record from a write that never completed, and says
    /// so (ADR 0072); what it never does is open at the same head.
    /// </summary>
    [Fact]
    public async Task any_change_to_a_header_breaks_verification()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                byte[] log = (await SegmentsAsync(harness.Storage)).SingleOrDefault() ?? [];

                List<(int Start, int End)> ranges = [.. HeaderRanges(log)];

                for (int h = 0; h < ranges.Count; h++)
                {
                    for (int at = ranges[h].Start; at < ranges[h].End; at++)
                    {
                        byte[] changed = (byte[])log.Clone();
                        changed[at] ^= 0x01;

                        if (h < ranges.Count - 1)
                        {
                            await Assert.ThrowsAsync<LogVerificationException>(async () =>
                                await T.OpenOrCreate(MemoryStorage.FromLog(T.Manifest, [changed]), harness.Options));
                            continue;
                        }

                        try
                        {
                            await using Dataset opened = await T.OpenOrCreate(MemoryStorage.FromLog(T.Manifest, [changed]), harness.Options);
                            Assert.Equal(new Position(harness.Model.Head - 1), opened.Head);
                        }
                        catch (LogVerificationException)
                        {
                        }
                    }
                }
            },
            iter: Iterations,
            print: script => script.ToString());
    }

    /// <summary>
    /// Stronger than I6 asks: a change to any byte of the log either refuses to
    /// open or reads as a shorter log — a flipped length can make the last
    /// record look torn — and never opens at the same head with other content.
    /// </summary>
    [Fact]
    public async Task no_single_byte_change_goes_unnoticed()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                byte[] log = (await SegmentsAsync(harness.Storage)).SingleOrDefault() ?? [];

                for (int at = 0; at < log.Length; at++)
                {
                    byte[] changed = (byte[])log.Clone();
                    changed[at] ^= 0x10;

                    try
                    {
                        await using Dataset opened = await T.OpenOrCreate(MemoryStorage.FromLog(T.Manifest, [changed]), harness.Options);

                        if (opened.Head.Value >= harness.Model.Head)
                        {
                            throw new InvalidOperationException("A change at byte " + at + " went unnoticed.");
                        }

                        using DatasetView view = opened.Pin();
                        Harness.Same(Harness.Rendered(harness.Model.History[(int)opened.Head.Value]), harness.Rendered(view), "change at " + at);
                    }
                    catch (LogVerificationException)
                    {
                    }
                }
            },
            iter: Iterations / 2,
            print: script => script.ToString());
    }

    /// <summary>
    /// I6: two continuations of one prefix are reported as divergent, at the
    /// first position where they differ; a log and its own prefix are not.
    /// </summary>
    [Fact]
    public async Task two_continuations_of_one_prefix_are_divergent()
    {
        await Gen.Select(Generators.CommitsOnly, Gen.Int[1, 3]).SampleAsync(
            async (script, extra) =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                long prefix = harness.Dataset.Head.Value;
                List<byte[]> copied = await SegmentsAsync(harness.Storage);
                MemoryStorage copy = MemoryStorage.FromLog(T.Manifest, copied.Select(s => (ReadOnlyMemory<byte>)s.ToArray()));
                MemoryStorage snapshot = MemoryStorage.FromLog(T.Manifest, copied.Select(s => (ReadOnlyMemory<byte>)s.ToArray()));

                Assert.Null(await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));

                await using Dataset other = await T.OpenOrCreate(copy, harness.Options);

                for (int i = 0; i < extra; i++)
                {
                    await harness.Dataset.CommitAsync(new CommitRequest().Assert(T.Iri("here"), T.Iri("p0"), T.Integer(i.ToString(System.Globalization.CultureInfo.InvariantCulture))), T.Ct);
                    await other.CommitAsync(new CommitRequest().Assert(T.Iri("there"), T.Iri("p0"), T.Integer(i.ToString(System.Globalization.CultureInfo.InvariantCulture))), T.Ct);
                }

                Assert.Equal(new Position(prefix + 1), await LogChain.FindDivergenceAsync(harness.Storage.Log, copy.Log, T.Ct));
                Assert.Equal(new Position(prefix + 1), await LogChain.FindDivergenceAsync(copy.Log, harness.Storage.Log, T.Ct));
                Assert.Null(await LogChain.FindDivergenceAsync(harness.Storage.Log, snapshot.Log, T.Ct));
                Assert.Null(await LogChain.FindDivergenceAsync(snapshot.Log, copy.Log, T.Ct));
            },
            iter: Iterations,
            print: x => x.ToString());
    }

    /// <summary>
    /// The byte ranges of every commit header and its stored length, found by
    /// walking format version 1 (ADR 0072) through its internal constants:
    /// this test is about that encoding.
    /// </summary>
    private static IEnumerable<(int Start, int End)> HeaderRanges(byte[] log)
    {
        int at = LogFormat.SegmentHeaderLength;

        while (at + LogFormat.RecordHeaderLength <= log.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(log.AsSpan(at));
            bool closing = (log[at + 5] & LogFormat.ClosingFlag) != 0;
            int end = at + LogFormat.RecordHeaderLength + length;

            if (end > log.Length)
            {
                yield break;
            }

            if (closing)
            {
                int headerLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(log.AsSpan(end - 4));
                yield return (end - 4 - headerLength, end);
            }

            at = end;
        }
    }
}
