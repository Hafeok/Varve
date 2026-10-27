// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Delta.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// <c>FollowAsync</c> (ADR 0129) over generated histories, through the
/// commits resource as the server serves it, paged three at a time: resumed
/// from every position, each later commit is handled exactly once and in
/// order, and the checkpoint advances with it; a crash between the handler
/// and the checkpoint at any commit hands that commit over again on restart
/// and no other twice; the live tail delivers what is committed while it is
/// open; a refusal ends the follow, a busy server is waited out.
/// </summary>
public class FollowTests
{
    internal const int Iterations = 100;

    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    private static readonly ProtocolLimits PagedByThree = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), new ByteCount(1L << 30), new ByteCount(100L << 20), TimeSpan.FromMilliseconds(200))
    {
        CommitsPageSize = 3,
    };

    [Fact]
    public async Task Resumed_from_every_position_each_later_commit_is_handled_once_in_order()
    {
        await Gen.Int[1, 9].SampleAsync(async commits =>
        {
            await using Dataset dataset = await HistoryAsync(commits);
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: PagedByThree, stopping: C.Ct);
            SparqlHttpClient client = new(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default);

            for (long resume = 0; resume < commits; resume++)
            {
                MemoryCheckpoint checkpoint = new(resume == 0 ? null : new FeedPosition(resume));
                List<long> handled = await FollowUntilAsync(client, checkpoint, commits);

                Assert.Equal(Enumerable.Range((int)resume + 1, commits - (int)resume).Select(p => (long)p), handled);
                Assert.Equal(new FeedPosition(commits), checkpoint.Saved);
                Assert.Equal(handled, checkpoint.History);
            }
        }, iter: Iterations);
    }

    [Fact]
    public async Task A_crash_between_handler_and_checkpoint_hands_that_commit_over_again_and_no_other()
    {
        await Gen.Select(Gen.Int[2, 9], Gen.Int[1, 9]).SampleAsync(async (commits, at) =>
        {
            int crashAt = Math.Min(at, commits);
            await using Dataset dataset = await HistoryAsync(commits);
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: PagedByThree, stopping: C.Ct);
            SparqlHttpClient client = new(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default);
            MemoryCheckpoint checkpoint = new(null) { CrashBefore = new FeedPosition(crashAt) };
            List<long> handled = [];

            // The first run: the handler returns for crashAt, the save throws.
            await Assert.ThrowsAsync<IOException>(() => client.FollowAsync(new FeedPosition(0), (record, _) =>
            {
                handled.Add(record.Position.Value);
                return ValueTask.CompletedTask;
            }, checkpoint, new FollowOptions(TimeProvider.System), C.Ct));

            Assert.Equal(crashAt == 1 ? null : new FeedPosition(crashAt - 1), checkpoint.Saved);

            // The restart, from what the checkpoint holds.
            checkpoint.CrashBefore = null;
            handled.AddRange(await FollowUntilAsync(client, checkpoint, commits));

            Assert.Equal(new FeedPosition(commits), checkpoint.Saved);
            Assert.Equal(Enumerable.Range(1, commits).Select(p => (long)p), handled.Distinct().Order());
            Assert.Equal(2, handled.Count(p => p == crashAt));
            Assert.Equal(commits + 1, handled.Count);
        }, iter: Iterations);
    }

    [Fact]
    public async Task The_live_tail_delivers_what_is_committed_while_it_is_open()
    {
        await using Dataset dataset = await HistoryAsync(2);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: PagedByThree, stopping: C.Ct);
        SparqlHttpClient client = new(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default);
        MemoryCheckpoint checkpoint = new(null);
        ConcurrentQueue<long> handled = new();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(C.Ct);
        TaskCompletionSource caughtUp = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Task following = client.FollowAsync(new FeedPosition(0), (record, _) =>
        {
            handled.Enqueue(record.Position.Value);

            if (record.Position.Value == 2)
            {
                caughtUp.TrySetResult();
            }

            if (record.Position.Value == 5)
            {
                stop.Cancel();
            }

            return ValueTask.CompletedTask;
        }, checkpoint, new FollowOptions(TimeProvider.System), stop.Token);

        await caughtUp.Task.WaitAsync(TimeSpan.FromSeconds(30), C.Ct);

        for (int i = 3; i <= 5; i++)
        {
            await CommitAsync(dataset, i);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => following.WaitAsync(TimeSpan.FromSeconds(30), C.Ct));
        Assert.Equal([1L, 2, 3, 4, 5], handled);
        Assert.Equal(new FeedPosition(5), checkpoint.Saved);
    }

    [Fact]
    public async Task A_refusal_ends_the_follow_with_the_problem()
    {
        await using Dataset dataset = await HistoryAsync(1);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: C.Ct);
        SparqlHttpClient client = new(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default);

        // A graph filter that is not an IRI: 400, which waiting will not change.
        FollowRefusedException refused = await Assert.ThrowsAsync<FollowRefusedException>(() =>
            client.FollowAsync(new FeedPosition(0), (_, _) => ValueTask.CompletedTask, new MemoryCheckpoint(null), new FollowOptions(TimeProvider.System) { Graph = "not an iri" }, C.Ct));
        Assert.Equal(400, refused.Status);
        Assert.Contains("bad-request", refused.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_checkpoint_survives_a_new_follower_and_is_replaced_whole()
    {
        string path = Path.Combine(Path.GetTempPath(), "varve-follow-" + Guid.NewGuid().ToString("N") + ".position");

        try
        {
            FileFollowCheckpoint checkpoint = new(path);
            Assert.Null(await checkpoint.LoadAsync(C.Ct));
            await checkpoint.SaveAsync(new FeedPosition(41), C.Ct);
            await checkpoint.SaveAsync(new FeedPosition(42), C.Ct);
            Assert.Equal(new FeedPosition(42), await new FileFollowCheckpoint(path).LoadAsync(C.Ct));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Follows until position `last` has been handled, then stops.
    private static async Task<List<long>> FollowUntilAsync(SparqlHttpClient client, MemoryCheckpoint checkpoint, long last)
    {
        List<long> handled = [];
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(C.Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FollowAsync(new FeedPosition(0), (record, _) =>
        {
            handled.Add(record.Position.Value);

            if (record.Position.Value == last)
            {
                // Cancelled after this commit is saved: the save takes the
                // follow's token, so it is the next await that sees it.
                checkpoint.StopAfterSave = stop;
            }

            return ValueTask.CompletedTask;
        }, checkpoint, new FollowOptions(TimeProvider.System), stop.Token));

        Assert.False(C.Ct.IsCancellationRequested);
        return handled;
    }

    private static async Task<Dataset> HistoryAsync(int commits)
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, C.Ct);

        for (int i = 1; i <= commits; i++)
        {
            await CommitAsync(dataset, i);
        }

        return dataset;
    }

    private static async Task CommitAsync(Dataset dataset, int i)
    {
        CommitRequestBuilder request = new CommitRequestBuilder().Assert(
            RequestTerm.FromTerm(C.Iri("http://ex/s" + i)),
            RequestTerm.FromTerm(C.Iri("http://ex/p")),
            RequestTerm.FromTerm(RdfTerm.Literal(Encoding.UTF8.GetBytes(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, C.Ct)).Outcome);
    }

    private sealed class MemoryCheckpoint(FeedPosition? start) : IFollowCheckpoint
    {
        internal FeedPosition? Saved { get; private set; } = start;

        internal List<long> History { get; } = [];

        internal FeedPosition? CrashBefore { get; set; }

        internal CancellationTokenSource? StopAfterSave { get; set; }

        public ValueTask<FeedPosition?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Saved);

        public ValueTask SaveAsync(FeedPosition position, CancellationToken cancellationToken)
        {
            if (CrashBefore == position)
            {
                throw new IOException("A crash between the handler and the checkpoint, at " + position.ToString() + ".");
            }

            Saved = position;
            History.Add(position.Value);
            StopAfterSave?.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
