// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// <c>FollowEventsAsync</c> (ADRs 0129, 0143) over generated histories, paged
/// three at a time: resumed from every position, each later commit's events
/// are handled once, in order, as the bytes the events resource serves; a
/// crash between the handler and the checkpoint hands that commit's events
/// over again and no other's; the live tail delivers events committed while
/// it is open.
/// </summary>
public class FollowEventsTests
{
    internal const int Iterations = 50;

    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    private static readonly ProtocolLimits PagedByThree = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), new ByteCount(1L << 30), new ByteCount(100L << 20), TimeSpan.FromMilliseconds(200))
    {
        CommitsPageSize = 3,
    };

    private const string Declaration =
        "INSERT DATA { GRAPH <https://w3id.org/varve/ns#events-catalogue/https%3A%2F%2Fex%2Fevents> { "
        + "<https://ex/types/Paid> a <https://w3id.org/varve/ns#EventType> ; <https://w3id.org/varve/ns#eventName> \"Paid\" ; "
        + "<https://w3id.org/varve/ns#eventClass> <https://ex/Paid> ; <https://w3id.org/varve/ns#eventVersion> 1 ; "
        + "<https://w3id.org/varve/ns#eventField> <https://ex/types/Paid/n> ; "
        + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?n WHERE { ?event <https://ex/n> ?n }\" . "
        + "<https://ex/types/Paid/n> a <https://w3id.org/varve/ns#EventField> ; <https://w3id.org/varve/ns#fieldName> \"n\" ; "
        + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#integer> ; <https://w3id.org/varve/ns#fieldRequired> true } }";

    // A history: the declaration, then for each step either 1 or 2 events in one commit, or a commit with none.
    private static async Task<(List<long> EventCommits, int Events)> HistoryAsync(SparqlHttpClient client, int[] steps)
    {
        await Expect(client.UpdateAsync(Declaration, (long?)null, C.Ct));
        List<long> eventCommits = [];
        int n = 0;
        int index = 0;

        foreach (int step in steps)
        {
            index++;
            StringBuilder update = new("INSERT DATA { GRAPH <https://ex/events> { ");

            for (int i = 0; i < step; i++)
            {
                n++;
                update.Append("<https://ex/e/" + n + "> a <https://ex/Paid> ; <https://ex/n> " + n + " . ");
            }

            update.Append("<https://ex/noise/" + index + "> <https://ex/p> 1 } }");
            await Expect(client.UpdateAsync(update.ToString(), (long?)null, C.Ct));
        }

        // The positions, read back: each commit after the declaration is one step.
        eventCommits = [.. steps.Select((s, i) => (s, p: (long)i + 2)).Where(x => x.s > 0).Select(x => x.p)];
        return (eventCommits, n);
    }

    private static async Task Expect(Task<HttpResponseMessage> response)
    {
        using HttpResponseMessage answered = await response;
        Assert.True(answered.IsSuccessStatusCode, (int)answered.StatusCode + " " + await answered.Content.ReadAsStringAsync(C.Ct));
    }

    private static async Task<(Dataset, ProtocolTestHost, SparqlHttpClient)> StartAsync()
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = TimeProvider.System, Validators = [new EventCatalogueValidator()] }, C.Ct);
        ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: PagedByThree, stopping: C.Ct);
        return (dataset, host, new SparqlHttpClient(Http, new Uri(host.Address, "datasets/d/"), ClientLimits.Default));
    }

    [Fact]
    public async Task Resumed_from_every_position_each_later_commits_events_are_handled_once_in_order_as_served()
    {
        await Gen.Int[0, 2].Array[1, 7].SampleAsync(async steps =>
        {
            (Dataset dataset, ProtocolTestHost host, SparqlHttpClient client) = await StartAsync();

            await using (dataset)
            await using (host)
            {
                (List<long> eventCommits, int _) = await HistoryAsync(client, steps);

                if (eventCommits.Count == 0)
                {
                    return;
                }

                List<ReceivedEvent> served = await ServedAsync(client);
                long last = eventCommits[^1];

                for (long resume = 0; resume < last; resume++)
                {
                    MemoryCheckpoint checkpoint = new(resume == 0 ? null : new FeedPosition(resume));
                    List<CommitEvents> handled = await FollowUntilAsync(client, checkpoint, last);

                    Assert.Equal(eventCommits.Where(p => p > resume), handled.Select(c => c.Position.Value));
                    Assert.Equal(new FeedPosition(last), checkpoint.Saved);

                    // The bytes handed over are the bytes served, in order.
                    string expected = "[" + string.Join(",", served
                        .Where(e => long.Parse(e.Id[..e.Id.IndexOf(' ', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture) > resume)
                        .Select(e => Encoding.UTF8.GetString(e.Json.Span))) + "]";
                    Assert.Equal(expected, "[" + string.Join(",", handled.SelectMany(c => c.Events).Select(e => Encoding.UTF8.GetString(e.Json.Span))) + "]");
                }
            }
        }, iter: Iterations);
    }

    [Fact]
    public async Task A_crash_between_handler_and_checkpoint_hands_that_commits_events_over_again_and_no_other()
    {
        await Gen.Select(Gen.Int[1, 2].Array[2, 6], Gen.Int[0, 5]).SampleAsync(async (steps, at) =>
        {
            (Dataset dataset, ProtocolTestHost host, SparqlHttpClient client) = await StartAsync();

            await using (dataset)
            await using (host)
            {
                (List<long> eventCommits, int _) = await HistoryAsync(client, steps);
                long crashAt = eventCommits[Math.Min(at, eventCommits.Count - 1)];
                MemoryCheckpoint checkpoint = new(null) { CrashBefore = new FeedPosition(crashAt) };
                List<long> handled = [];

                await Assert.ThrowsAsync<IOException>(() => client.FollowEventsAsync(new FeedPosition(0), (commit, _) =>
                {
                    handled.Add(commit.Position.Value);
                    return ValueTask.CompletedTask;
                }, checkpoint, new FollowOptions(TimeProvider.System), C.Ct));

                checkpoint.CrashBefore = null;
                handled.AddRange((await FollowUntilAsync(client, checkpoint, eventCommits[^1])).Select(c => c.Position.Value));

                Assert.Equal(eventCommits, handled.Distinct().Order());
                Assert.Equal(2, handled.Count(p => p == crashAt));
                Assert.Equal(eventCommits.Count + 1, handled.Count);
            }
        }, iter: Iterations);
    }

    [Fact]
    public async Task The_live_tail_delivers_events_committed_while_it_is_open()
    {
        (Dataset dataset, ProtocolTestHost host, SparqlHttpClient client) = await StartAsync();

        await using (dataset)
        await using (host)
        {
            await HistoryAsync(client, [1]);
            MemoryCheckpoint checkpoint = new(null);
            ConcurrentQueue<string> handled = new();
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(C.Ct);
            TaskCompletionSource caughtUp = new(TaskCreationOptions.RunContinuationsAsynchronously);

            Task following = client.FollowEventsAsync(new FeedPosition(0), (commit, _) =>
            {
                foreach (ReceivedEvent received in commit.Events)
                {
                    handled.Enqueue(received.Id);
                }

                if (commit.Position.Value == 2)
                {
                    caughtUp.TrySetResult();
                }

                if (commit.Position.Value == 4)
                {
                    stop.Cancel();
                }

                return ValueTask.CompletedTask;
            }, checkpoint, new FollowOptions(TimeProvider.System), stop.Token);

            await caughtUp.Task.WaitAsync(TimeSpan.FromSeconds(30), C.Ct);
            await Expect(client.UpdateAsync("INSERT DATA { GRAPH <https://ex/events> { <https://ex/e/2> a <https://ex/Paid> ; <https://ex/n> 2 } }", (long?)null, C.Ct));
            await Expect(client.UpdateAsync("INSERT DATA { GRAPH <https://ex/events> { <https://ex/e/3> a <https://ex/Paid> ; <https://ex/n> 3 } }", (long?)null, C.Ct));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => following.WaitAsync(TimeSpan.FromSeconds(30), C.Ct));

            Assert.Equal(["2 https://ex/e/1", "3 https://ex/e/2", "4 https://ex/e/3"], handled);
            Assert.Equal(new FeedPosition(4), checkpoint.Saved);
        }
    }

    [Fact]
    public async Task The_event_calls_reach_their_resources()
    {
        (Dataset dataset, ProtocolTestHost host, SparqlHttpClient client) = await StartAsync();

        await using (dataset)
        await using (host)
        {
            await HistoryAsync(client, [1]);
            using HttpResponseMessage types = await client.EventTypesAsync(null, C.Ct);
            Assert.Contains("\"name\":\"Paid\"", await types.Content.ReadAsStringAsync(C.Ct), StringComparison.Ordinal);
            using HttpResponseMessage then = await client.EventTypesAsync("position:0", C.Ct);
            Assert.Equal("{\"types\":[]}", await then.Content.ReadAsStringAsync(C.Ct));
            using HttpResponseMessage schema = await client.EventSchemaAsync("Paid", 1, 1, C.Ct);
            Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
            using HttpResponseMessage asyncApi = await client.AsyncApiAsync(null, C.Ct);
            Assert.Contains("\"asyncapi\":\"3.0.0\"", await asyncApi.Content.ReadAsStringAsync(C.Ct), StringComparison.Ordinal);
            using HttpResponseMessage stream = await client.EventsAsync(0, 2, "Paid", eventStream: false, C.Ct);
            Assert.Single(SparqlHttpClient.ReadBatch(await stream.Content.ReadAsByteArrayAsync(C.Ct)));
        }
    }

    // Every event the resource serves, page after page through Link: rel="next".
    private static async Task<List<ReceivedEvent>> ServedAsync(SparqlHttpClient client)
    {
        List<ReceivedEvent> events = [];
        long from = 0;

        while (true)
        {
            using HttpResponseMessage page = await client.EventsAsync(from, null, null, eventStream: false, C.Ct);
            events.AddRange(SparqlHttpClient.ReadBatch(await page.Content.ReadAsByteArrayAsync(C.Ct)));
            string? next = page.Headers.TryGetValues("Link", out IEnumerable<string>? links)
                ? links.SelectMany(l => l.Split(',')).FirstOrDefault(l => l.Contains("rel=\"next\"", StringComparison.Ordinal))
                : null;

            if (next is null)
            {
                return events;
            }

            string query = next[(next.IndexOf("from=", StringComparison.Ordinal) + 5)..];
            from = long.Parse(new string([.. query.TakeWhile(char.IsAsciiDigit)]), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static async Task<List<CommitEvents>> FollowUntilAsync(SparqlHttpClient client, MemoryCheckpoint checkpoint, long last)
    {
        List<CommitEvents> handled = [];
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(C.Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FollowEventsAsync(new FeedPosition(0), (commit, _) =>
        {
            handled.Add(commit);

            if (commit.Position.Value == last)
            {
                checkpoint.StopAfterSave = stop;
            }

            return ValueTask.CompletedTask;
        }, checkpoint, new FollowOptions(TimeProvider.System), stop.Token));
        Assert.False(C.Ct.IsCancellationRequested);
        return handled;
    }

    private sealed class MemoryCheckpoint(FeedPosition? start) : IFollowCheckpoint
    {
        internal FeedPosition? Saved { get; private set; } = start;

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
            StopAfterSave?.Cancel();
            return ValueTask.CompletedTask;
        }
    }
}
