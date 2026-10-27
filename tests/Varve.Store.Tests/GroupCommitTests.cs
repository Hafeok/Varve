// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Group commit (ADR 0124): a batched history equals the unbatched one commit
/// for commit — outcomes, positions, deltas, the header chain and the bytes of
/// <c>log/</c> — under an injected clock, with a default projection failure
/// injected at a random request; no request is answered before the flush that
/// makes its batch durable; a failed write answers none; a request withdrawn
/// while queued is never written, and one taken into a batch is.
/// </summary>
public sealed class GroupCommitTests
{
    /// <summary>A clock that moves one tick at every reading: the same readings, in the same order, give the same timestamps.</summary>
    private sealed class TickingClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero).UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Increment(ref _ticks), TimeSpan.Zero);
    }

    /// <summary>Rejects a commit that would leave more than a dozen quads: a verdict on the staged state, not the request alone.</summary>
    private sealed class AtMostADozen : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) =>
            T.All(proposed).Count > 12 ? ValidationVerdict.Reject([T.Iri("too-many")]) : ValidationVerdict.Accept();
    }

    /// <summary>One generated request: what it asks, and how.</summary>
    private sealed record Step(Func<Dataset, CancellationToken, ValueTask<CommitResult>> Submit, string Text);

    private static readonly ICommitValidator[] Dozen = [new AtMostADozen()];

    /// <summary>
    /// A stream of requests over a small vocabulary, so that most retract
    /// something real and many are no change: new and existing terms, blank
    /// nodes, named graphs, an expected position (right or stale), a request
    /// validator that reads the proposed state, and settings commits.
    /// </summary>
    private static List<Step> Generate(Random random, int count)
    {
        List<Step> steps = [];

        for (int i = 0; i < count; i++)
        {
            int kind = random.Next(20);

            if (kind == 0)
            {
                AccessScope scope = random.Next(2) == 0 ? AccessScope.AllHistory : AccessScope.Current;
                CommitMetadata metadata = new() { Agent = T.Iri("agent"), Cause = T.Literal("settings " + i) };
                steps.Add(new Step((d, ct) => d.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = scope }, metadata, null, ct), "settings " + scope));
                continue;
            }

            long? expected = kind == 1 ? random.Next(i + 2) : null;
            bool validated = kind == 2;
            List<(bool Assert, RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)> operations = [];

            for (int op = random.Next(1, 5); op > 0; op--)
            {
                RdfTerm subject = random.Next(8) == 0 ? T.Blank("b" + random.Next(2)) : T.Iri("s" + random.Next(4));
                RdfTerm @object = random.Next(6) == 0 ? T.Iri("new" + i + "-" + op) : T.Integer(random.Next(4).ToString(CultureInfo.InvariantCulture));
                RdfTerm? graph = random.Next(3) == 0 ? T.Iri("g" + random.Next(2)) : null;
                operations.Add((random.Next(3) > 0, subject, T.Iri("p" + random.Next(2)), @object, graph));
            }

            string text = (expected is long e ? "@" + e + " " : "") + (validated ? "validated " : "")
                + string.Join("; ", operations.Select(o => (o.Assert ? "+" : "-") + T.Render(o.S) + " " + T.Render(o.P) + " " + T.Render(o.O) + (o.G is { } g ? " " + T.Render(g) : "")));
            CommitMetadata data = new() { Cause = T.Literal("request " + i) };

            steps.Add(new Step((d, ct) =>
            {
                CommitRequestBuilder request = new()
                {
                    ExpectedPosition = expected is long p ? new Position(p) : null,
                    Validators = validated ? Dozen : [],
                    Metadata = data,
                };

                foreach ((bool assert, RdfTerm s, RdfTerm p2, RdfTerm o, RdfTerm? g) in operations)
                {
                    if (assert)
                    {
                        request = g is null ? request.Assert(s, p2, o) : request.Assert(s, p2, o, g);
                    }
                    else
                    {
                        request = g is null ? request.Retract(s, p2, o) : request.Retract(s, p2, o, g);
                    }
                }

                return d.CommitAsync(request, ct);
            }, text));
        }

        return steps;
    }

    /// <summary>The outcomes, and the log's bytes, of running the steps in groups of the given sizes.</summary>
    private static async Task<(List<string> Outcomes, (ReadOnlyMemory<byte> Manifest, List<byte[]> Segments) Log, SortedSet<string> State, long Head, long Writes)> RunAsync(
        List<Step> steps, IReadOnlyList<int> groups, GroupCommitOptions group, long? faultAt)
    {
        MemoryStorage storage = new();
        DatasetOptions options = new()
        {
            Clock = new TickingClock(),
            MaxRecordBytes = new ByteCount(96),
            SegmentBytes = new ByteCount(1024),
            Maintenance = MaintenanceMode.Off,
            GroupCommit = group,
        };

        if (faultAt is long fault)
        {
            options.DefaultProjectionFault = position => position == fault;
        }

        List<string> outcomes = [];
        Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        await using (dataset)
        {
            int next = 0;

            foreach (int size in groups)
            {
                List<Task<CommitResult>> submitted = [];

                using (await dataset.HoldSequencerForTestsAsync(T.Ct))
                {
                    for (int i = 0; i < size && next < steps.Count; i++, next++)
                    {
                        submitted.Add(steps[next].Submit(dataset, T.Ct).AsTask());
                    }
                }

                foreach (Task<CommitResult> answer in submitted)
                {
                    CommitResult result = await answer;
                    outcomes.Add(result.Outcome + " " + result.Position.Value);
                }
            }

            SortedSet<string> state = dataset.IsFailed ? [] : T.Terms(dataset.Pin());
            return (outcomes, await T.CopyLogAsync(storage), state, dataset.Head.Value, dataset.WritesForTests);
        }
    }

    private static List<int> Groups(Random random, int count, int largest)
    {
        List<int> groups = [];

        for (int left = count; left > 0;)
        {
            int size = Math.Min(left, random.Next(1, largest + 1));
            groups.Add(size);
            left -= size;
        }

        return groups;
    }

    /// <summary>
    /// The property: for generated request streams submitted in generated
    /// groups — each group queued while the sequencer is held, so that it is
    /// led as one batch — the outcomes, positions, header chain and log bytes
    /// equal those of the same requests written one at a time with group
    /// commit off; and so they do with a default projection failure injected
    /// at a random position, where the requests after it are unavailable.
    /// </summary>
    [Theory]
    [InlineData(9101, false)]
    [InlineData(9102, false)]
    [InlineData(9103, true)]
    [InlineData(9104, true)]
    public async Task a_batched_history_equals_the_unbatched_one_commit_for_commit(int seed, bool fault)
    {
        int iterations = 0;
        long commits = 0, writes = 0;

        for (int round = 0; round < 40; round++)
        {
            Random random = new((seed * 1000) + round);
            List<Step> steps = Generate(random, 40);
            long? faultAt = fault ? random.Next(1, 30) : null;
            List<int> alone = [.. Enumerable.Repeat(1, steps.Count)];
            List<int> groups = Groups(random, steps.Count, 9);
            GroupCommitOptions bounds = random.Next(3) == 0
                ? new GroupCommitOptions { MaxRecords = random.Next(1, 6), MaxBytes = new ByteCount(random.Next(64, 2048)) }
                : GroupCommitOptions.Default;

            var unbatched = await RunAsync(steps, alone, GroupCommitOptions.Off, faultAt);
            var grouped = await RunAsync(steps, groups, bounds, faultAt);

            string where = "seed " + seed + ", round " + round + ", groups " + string.Join(",", groups) + (faultAt is long f ? ", fault at " + f : "");

            for (int i = 0; i < steps.Count; i++)
            {
                Assert.True(unbatched.Outcomes[i] == grouped.Outcomes[i], where + ": request " + i + " (" + steps[i].Text + ") was " + unbatched.Outcomes[i] + " alone and " + grouped.Outcomes[i] + " batched");
            }

            Assert.True(unbatched.Head == grouped.Head, where + ": heads differ");
            Assert.True(unbatched.State.SetEquals(grouped.State), where + ": states differ");
            Assert.True(unbatched.Log.Manifest.Span.SequenceEqual(grouped.Log.Manifest.Span), where + ": manifests differ");
            Assert.True(unbatched.Log.Segments.Count == grouped.Log.Segments.Count, where + ": segment counts differ");

            for (int s = 0; s < unbatched.Log.Segments.Count; s++)
            {
                Assert.True(unbatched.Log.Segments[s].AsSpan().SequenceEqual(grouped.Log.Segments[s]), where + ": segment " + s + " differs");
            }

            Assert.True(unbatched.Writes == unbatched.Head, where + ": with group commit off, a write wrote more than one commit");
            iterations++;
            commits += grouped.Head;
            writes += grouped.Writes;
        }

        // The property is about batches: they formed.
        Assert.True(writes < commits * 2 / 3, "seed " + seed + ": " + commits + " commits in " + writes + " writes; too few batches formed to test anything");
        TestContext.Current.TestOutputHelper?.WriteLine("seed " + seed + ": " + iterations + " histories of 40 requests, " + commits + " commits in " + writes + " writes");
    }

    /// <summary>A segment store whose flush waits for the test, or fails.</summary>
    private sealed class GatedStorage(MemoryStorage inner, Durability? durability = null) : IStorage, ISegmentStore
    {
        public TaskCompletionSource Gate { get; set; } = Opened();

        public TaskCompletionSource Flushing { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; set; }

        public ISegmentStore Log => this;

        public IDerivedStore Derived => inner.Derived;

        public Durability Durability => durability ?? inner.Log.Durability;

        /// <summary>How many flushes the log has had.</summary>
        public int Flushes { get; private set; }

        public static TaskCompletionSource Opened()
        {
            TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
            open.SetResult();
            return open;
        }

        public async ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken)
        {
            Flushes++;
            Flushing.TrySetResult();
            await Gate.Task.ConfigureAwait(false);

            if (Failure is not null)
            {
                throw Failure;
            }

            await inner.Log.FlushAsync(segment, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken) => inner.Log.ReadManifestAsync(cancellationToken);

        public ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken) => inner.Log.WriteManifestAsync(manifest, cancellationToken);

        public ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken) => inner.Log.ListSegmentsAsync(cancellationToken);

        public ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken) => inner.Log.CreateSegmentAsync(cancellationToken);

        public ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => inner.Log.AppendAsync(segment, bytes, cancellationToken);

        public ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken) => inner.Log.SealAsync(segment, cancellationToken);

        public ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken) =>
            inner.Log.ReadRangeAsync(segment, offset, length, cancellationToken);

    }

    private static CommitRequestBuilder Quad(int i) => new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// No request of a batch is answered while its flush has not returned —
    /// under injected flush failures as well: then every request of the
    /// batch fails, none is answered <c>Committed</c>, and the dataset refuses
    /// commits until reopened.
    /// </summary>
    [Fact]
    public async Task no_request_is_answered_before_its_batch_is_durable_and_a_failed_flush_answers_none()
    {
        int checkedBatches = 0;

        for (int round = 0; round < 20; round++)
        {
            GatedStorage storage = new(new MemoryStorage());
            await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, T.Options(groupCommit: GroupCommitOptions.Default), T.Ct);
            bool fail = round % 2 == 1;
            int size = 1 + (round % 6);
            storage.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            storage.Flushing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Task<CommitResult>> answers = [];

            using (await dataset.HoldSequencerForTestsAsync(T.Ct))
            {
                for (int i = 0; i < size; i++)
                {
                    answers.Add(dataset.CommitAsync(Quad(i), T.Ct).AsTask());
                }
            }

            await storage.Flushing.Task.WaitAsync(TimeSpan.FromSeconds(30), T.Ct);
            await Task.Delay(20, T.Ct);
            Assert.All(answers, answer => Assert.False(answer.IsCompleted, "a request was answered while its batch's flush had not returned"));
            Assert.Equal(0, dataset.Head.Value);

            storage.Failure = fail ? new System.IO.IOException("injected flush failure") : null;
            storage.Gate.SetResult();

            if (fail)
            {
                foreach (Task<CommitResult> answer in answers)
                {
                    await Assert.ThrowsAsync<System.IO.IOException>(() => answer);
                }

                Assert.Equal(0, dataset.Head.Value);
                storage.Failure = null;
                CommitResult after = await dataset.CommitAsync(Quad(99), T.Ct);
                Assert.Equal(CommitOutcome.Unavailable, after.Outcome);
            }
            else
            {
                long[] positions = [.. (await Task.WhenAll(answers)).Select(result => result.Position.Value)];
                Assert.Equal(Enumerable.Range(1, size).Select(p => (long)p), positions);
                Assert.Equal(size, dataset.Head.Value);
            }

            checkedBatches++;
        }

        Assert.Equal(20, checkedBatches);
    }

    /// <summary>A request cancelled while still queued is withdrawn: never written, and the next request takes its place.</summary>
    [Fact]
    public async Task a_request_cancelled_while_queued_is_never_written()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage(), groupCommit: GroupCommitOptions.Default);
        using CancellationTokenSource cancel = new();
        Task<CommitResult> first, withdrawn, last;

        using (await dataset.HoldSequencerForTestsAsync(T.Ct))
        {
            first = dataset.CommitAsync(Quad(1), T.Ct).AsTask();
            withdrawn = dataset.CommitAsync(Quad(2), cancel.Token).AsTask();
            last = dataset.CommitAsync(Quad(3), T.Ct).AsTask();
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withdrawn);
        }

        Assert.Equal(1, (await first).Position.Value);
        Assert.Equal(2, (await last).Position.Value);
        SortedSet<string> state = T.Terms(dataset.Pin());
        Assert.DoesNotContain(state, quad => quad.Contains("/s2>", StringComparison.Ordinal));
        Assert.Equal(2, state.Count);
    }

    /// <summary>A request cancelled after a leader took it into a batch is written with the batch; its caller only stops waiting.</summary>
    [Fact]
    public async Task a_request_cancelled_after_it_was_taken_is_written_with_its_batch()
    {
        GatedStorage storage = new(new MemoryStorage());
        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, T.Options(groupCommit: GroupCommitOptions.Default), T.Ct);
        storage.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancel = new();
        Task<CommitResult> leader, taken;

        using (await dataset.HoldSequencerForTestsAsync(T.Ct))
        {
            leader = dataset.CommitAsync(Quad(1), T.Ct).AsTask();
            taken = dataset.CommitAsync(Quad(2), cancel.Token).AsTask();
        }

        await storage.Flushing.Task.WaitAsync(TimeSpan.FromSeconds(30), T.Ct);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => taken);
        storage.Gate.SetResult();

        Assert.Equal(1, (await leader).Position.Value);
        Assert.Equal(2, dataset.Head.Value);
        Assert.Contains(T.Terms(dataset.Pin()), quad => quad.Contains("/s2>", StringComparison.Ordinal));
    }

    /// <summary>Concurrent writers with no hold: every request is answered once, positions are dense, and the state is every assertion.</summary>
    [Fact]
    public async Task concurrent_writers_are_answered_once_each_with_dense_positions()
    {
        await using Dataset dataset = await T.Open(new MemoryStorage(), groupCommit: GroupCommitOptions.Default);
        CommitResult[] results = await Task.WhenAll(Enumerable.Range(0, 400).Select(i => Task.Run(async () => await dataset.CommitAsync(Quad(i), T.Ct), T.Ct)));

        Assert.All(results, result => Assert.Equal(CommitOutcome.Committed, result.Outcome));
        Assert.Equal(Enumerable.Range(1, 400).Select(p => (long)p), results.Select(r => r.Position.Value).Order());
        Assert.Equal(400, T.Terms(dataset.Pin()).Count);
    }

    /// <summary>
    /// Unset, group commit follows the storage's durability (ADR 0124): off
    /// in memory, where there is no flush for a batch to share, on for a
    /// durable backend. Requests queued behind a held sequencer are written
    /// one write each in memory and in one write on files.
    /// </summary>
    [Fact]
    public async Task unset_group_commit_follows_the_storage_durability()
    {
        Assert.Same(GroupCommitOptions.Off, GroupCommitOptions.For(Durability.None));
        Assert.Same(GroupCommitOptions.Default, GroupCommitOptions.For(Durability.Committed));
        Assert.Same(GroupCommitOptions.Default, GroupCommitOptions.For(Durability.Synchronised));

        // Unset writes as Off does in memory and as Default does when durable;
        // and the two differ, so the comparison sees batching.
        int alone = await WritesForQueuedAsync(Durability.None, GroupCommitOptions.Off);
        int batched = await WritesForQueuedAsync(Durability.Synchronised, GroupCommitOptions.Default);
        Assert.Equal(alone, await WritesForQueuedAsync(Durability.None, null));
        Assert.Equal(batched, await WritesForQueuedAsync(Durability.Synchronised, null));
        Assert.Equal(alone - 3, batched);
    }

    // Four requests queued behind a held sequencer: how many flushes the log had meanwhile.
    private static async Task<int> WritesForQueuedAsync(Durability durability, GroupCommitOptions? group)
    {
        GatedStorage storage = new(new MemoryStorage(), durability);
        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, T.Options(groupCommit: group), T.Ct);
        int flushesAtCreation = storage.Flushes;
        List<Task<CommitResult>> answers = [];

        using (await dataset.HoldSequencerForTestsAsync(T.Ct))
        {
            for (int i = 0; i < 4; i++)
            {
                answers.Add(dataset.CommitAsync(Quad(i), T.Ct).AsTask());
            }
        }

        await Task.WhenAll(answers);
        return storage.Flushes - flushesAtCreation;
    }
}
