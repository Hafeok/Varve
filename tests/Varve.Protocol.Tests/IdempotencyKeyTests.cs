// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Varve.Protocol.Client;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Client idempotency keys (ADR 0246): a write that carries
/// <c>Idempotency-Key</c> commits once for its agent and key. A retry is
/// answered from the commit that holds the key, before any precondition; the
/// same key with another request is <c>422</c>; another agent's key never
/// matches; the key is read back from the log by a writer that reopens the
/// dataset; and over generated schedules of lost responses, retries, forwards
/// and writer changes, every logical write commits exactly once.
/// </summary>
public sealed class IdempotencyKeyTests
{
    // Sized for the land gate: each schedule is up to ten writes, each sent up to four times.
    private const int Schedules = 60;

    private const string Insert = "INSERT DATA { <http://ex/a> <http://ex/p> 1 }";

    [Fact]
    public async Task A_retried_update_commits_once_and_is_answered_as_the_original_was()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        using HttpResponseMessage first = await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);
        using HttpResponseMessage retry = await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal("1", P.Header(retry, "Varve-Position"));
        Assert.Equal("\"1\"", retry.Headers.ETag?.Tag);
        Assert.Equal(P.Header(first, "Varve-Request-Id"), P.Header(retry, "Varve-Request-Id"));
        Assert.Equal("true", P.Header(retry, "Varve-Replayed"));
        Assert.Null(P.Header(first, "Varve-Replayed"));
        Assert.Equal(1L, dataset.Head.Value);
    }

    [Fact]
    public async Task The_same_key_with_another_request_is_refused_and_commits_nothing()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct)).StatusCode);
        using HttpResponseMessage reused = await host.Client.SendAsync(Keyed(Update("INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), "alice", "k1"), P.Ct);
        string body = await reused.Content.ReadAsStringAsync(P.Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/idempotency-key-reused\"", body, StringComparison.Ordinal);
        Assert.Contains("\"position\":1", body, StringComparison.Ordinal);
        Assert.Equal(1L, dataset.Head.Value);

        // The target is part of the request: the same body to a graph is another request too.
        using HttpRequestMessage put = Keyed(new HttpRequestMessage(HttpMethod.Put, "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g")) { Content = new StringContent("<http://ex/a> <http://ex/p> \"1\" .", Encoding.UTF8, "application/n-triples") }, "alice", "g1");
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(put, P.Ct)).StatusCode);
        using HttpRequestMessage elsewhere = Keyed(new HttpRequestMessage(HttpMethod.Put, "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/h")) { Content = new StringContent("<http://ex/a> <http://ex/p> \"1\" .", Encoding.UTF8, "application/n-triples") }, "alice", "g1");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.SendAsync(elsewhere, P.Ct)).StatusCode);
        Assert.Equal(2L, dataset.Head.Value);
    }

    [Fact]
    public async Task One_agents_key_never_matches_another_agents()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Keyed(Update(Insert), "alice", "shared"), P.Ct)).StatusCode);

        // Bob's request with Alice's key is his own: it commits, and his retry replays his commit.
        using HttpResponseMessage bob = await host.Client.SendAsync(Keyed(Update("INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), "bob", "shared"), P.Ct);
        using HttpResponseMessage again = await host.Client.SendAsync(Keyed(Update("INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), "bob", "shared"), P.Ct);

        Assert.Equal(HttpStatusCode.NoContent, bob.StatusCode);
        Assert.Equal("2", P.Header(bob, "Varve-Position"));
        Assert.Equal("2", P.Header(again, "Varve-Position"));
        Assert.Equal("true", P.Header(again, "Varve-Replayed"));
        Assert.Equal(2L, dataset.Head.Value);
    }

    [Fact]
    public async Task A_retry_is_answered_from_its_commit_before_its_precondition_is_checked()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Keyed(P.Update("datasets/d/sparql", Insert, "\"0\""), "alice", "k1"), P.Ct)).StatusCode);

        // The original moved the head past its If-Match: the retry is its answer, not 412.
        using HttpResponseMessage retry = await host.Client.SendAsync(Keyed(P.Update("datasets/d/sparql", Insert, "\"0\""), "alice", "k1"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal("1", P.Header(retry, "Varve-Position"));

        // Unkeyed, the same request is 412.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(P.Update("datasets/d/sparql", Insert, "\"0\""), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task A_graph_store_write_replays_its_201_and_the_address_the_server_minted()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        HttpRequestMessage Post() => Keyed(new HttpRequestMessage(HttpMethod.Post, "datasets/d/graphs") { Content = new StringContent("<http://ex/a> <http://ex/p> \"1\" .", Encoding.UTF8, "application/n-triples") }, "alice", "post");
        using HttpResponseMessage first = await host.Client.SendAsync(Post(), P.Ct);
        using HttpResponseMessage retry = await host.Client.SendAsync(Post(), P.Ct);

        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.NotNull(first.Headers.Location);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal(1L, dataset.Head.Value);

        // A DELETE retried after it committed is its 204, not the 404 of a graph now gone.
        string graph = first.Headers.Location!.ToString();
        HttpRequestMessage Delete() => Keyed(new HttpRequestMessage(HttpMethod.Delete, "datasets/d/graphs?graph=" + Uri.EscapeDataString(graph)), "alice", "delete");
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Delete(), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Delete(), P.Ct)).StatusCode);
        Assert.Equal(2L, dataset.Head.Value);
    }

    [Theory]
    [InlineData("k1")]
    [InlineData("\"\"")]
    [InlineData("\"a\", \"b\"")]
    [InlineData("\"tab\there\"")]
    public async Task A_malformed_key_is_400_and_commits_nothing(string header)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);
        using HttpRequestMessage request = Update(Insert);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", header);

        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(request, P.Ct)).StatusCode);
        Assert.Equal(0L, dataset.Head.Value);
    }

    [Fact]
    public async Task A_write_that_committed_nothing_records_no_key()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset);

        // A 412 records nothing: the same key may then carry the write that commits.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(Keyed(P.Update("datasets/d/sparql", Insert, "\"5\""), "alice", "k1"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Keyed(P.Update("datasets/d/sparql", Insert, "\"0\""), "alice", "k1"), P.Ct)).StatusCode);
        Assert.Equal(1L, dataset.Head.Value);
    }

    [Fact]
    public async Task A_writer_that_reopens_the_dataset_reads_the_keys_back_from_the_log()
    {
        MemoryStorage storage = new();
        Dataset first = await Dataset.CreateAsync(storage, new DatasetId(new Guid("6f8c0000-0000-4000-8000-000000000246")), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        string id;

        await using (ProtocolTestHost host = await StartAsync(first))
        {
            using HttpResponseMessage original = await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);
            id = P.Header(original, "Varve-Request-Id")!;
        }

        await first.DisposeAsync();

        // A restart, or a handover: a new writer opens the same log, with an index of its own.
        await using Dataset second = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        await using ProtocolTestHost next = await StartAsync(second);
        using HttpResponseMessage retry = await next.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);

        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal("1", P.Header(retry, "Varve-Position"));
        Assert.Equal(id, P.Header(retry, "Varve-Request-Id"));
        Assert.Equal(1L, second.Head.Value);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await next.Client.SendAsync(Keyed(Update("INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), "alice", "k1"), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task A_key_whose_commit_left_the_window_is_forgotten()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, window: new CommitWindow(2));

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Keyed(Update(Insert), "alice", "old"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Update("INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct)).StatusCode);

        // Position 1 is still among the last two commits.
        Assert.Equal("true", P.Header(await host.Client.SendAsync(Keyed(Update(Insert), "alice", "old"), P.Ct), "Varve-Replayed"));

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(Update("INSERT DATA { <http://ex/c> <http://ex/p> 3 }"), P.Ct)).StatusCode);

        // Now it is not: the key is free, and the same request is evaluated afresh.
        using HttpResponseMessage late = await host.Client.SendAsync(Keyed(Update("INSERT DATA { <http://ex/d> <http://ex/p> 4 }"), "alice", "old"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode);
        Assert.Null(P.Header(late, "Varve-Replayed"));
        Assert.Equal(4L, dataset.Head.Value);
    }

    [Fact]
    public async Task A_key_a_request_holds_is_409_until_that_request_is_answered()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ProtocolTestHost host = await StartAsync(dataset, updates: inner => new Held(inner, entered, release));

        Task<HttpResponseMessage> first = host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);
        await entered.Task.WaitAsync(P.Ct);

        using HttpResponseMessage second = await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("idempotency-key-in-flight", await second.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal("1", P.Header(second, "Retry-After"));

        release.SetResult();
        using HttpResponseMessage answered = await first;
        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        Assert.Equal("true", P.Header(await host.Client.SendAsync(Keyed(Update(Insert), "alice", "k1"), P.Ct), "Varve-Replayed"));
        Assert.Equal(1L, dataset.Head.Value);
    }

    /// <summary>
    /// The gate (#132): for generated schedules of writes, each sent through
    /// the client to either instance, with responses lost after the writer
    /// answered or before the request left, and the writer moved to the other
    /// instance — which reopens the same log — at a lost response, every
    /// logical write commits exactly once. Each write increments a counter
    /// and leaves a marker in one commit, so a write committed twice would
    /// leave the counter above the markers.
    /// </summary>
    [Fact]
    public async Task Over_generated_schedules_of_lost_responses_retries_forwards_and_writer_changes_each_write_commits_once()
    {
        // Each step: which instance the client sends to, how its first
        // attempt is lost (0 none, 1 the response, 2 the request), and
        // whether the writer moves at that loss.
        Gen<(int Via, int Lose, bool Move)[]> schedules = Gen.Select(Gen.Int[0, 1], Gen.Int[0, 2], Gen.Bool).Array[1, 10];

        await schedules.SampleAsync(async schedule =>
        {
            await using Fleet fleet = await Fleet.StartAsync();
            int serial = 0;

            for (int step = 0; step < schedule.Length; step++)
            {
                (int via, int lose, bool move) = schedule[step];
                int write = ++serial;
                Lossy lossy = new(fleet, lose, move);
                using HttpClient http = new(lossy) { BaseAddress = fleet.Hosts[via].Address };
                int keys = 0;
                SparqlHttpClient client = new(http, new Uri(fleet.Hosts[via].Address, "datasets/d/"), ClientLimits.Default with
                {
                    IdempotencyKeys = () => "w" + write.ToString(CultureInfo.InvariantCulture) + "-" + Interlocked.Increment(ref keys).ToString(CultureInfo.InvariantCulture),
                    WriteRetries = 3,
                });

                using HttpResponseMessage response = await client.UpdateAsync(
                    "DELETE { <http://ex/c> <http://ex/n> ?o } INSERT { <http://ex/c> <http://ex/n> ?m . <http://ex/w" + write.ToString(CultureInfo.InvariantCulture) + "> <http://ex/p> 1 } WHERE { <http://ex/c> <http://ex/n> ?o BIND(?o + 1 AS ?m) }",
                    (long?)null,
                    P.Ct);

                Assert.True(response.StatusCode == HttpStatusCode.NoContent, "write " + write + " answered " + (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync(P.Ct));
                Assert.Equal(1, keys);
            }

            Dataset writer = fleet.Current;
            Assert.Equal(serial, Counter(writer));

            for (int write = 1; write <= serial; write++)
            {
                Assert.True(Holds(writer, "http://ex/w" + write.ToString(CultureInfo.InvariantCulture)), "write " + write + " is not in the log");
            }

            // One seed commit, then one commit per logical write.
            Assert.Equal(serial + 1L, writer.Head.Value);
        }, iter: Schedules, threads: 1, print: s => string.Join(" ", Array.ConvertAll(s, x => x.Via + "/" + x.Lose + (x.Move ? "m" : string.Empty))));
    }

    private static long Counter(Dataset dataset)
    {
        using DatasetView view = dataset.Pin();
        RdfTerm subject = RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/c"));
        Assert.True(view.TryInternalise(subject, out TermHandle handle));
        using IQuadCursor cursor = view.Match(handle, TermHandle.None, TermHandle.None, GraphPattern.Any);
        Assert.True(cursor.MoveNext());
        Assert.True(view.TryExternalise(cursor.Current.Object, out RdfTerm? value));
        return long.Parse(Encoding.UTF8.GetString(value!.Lexical), CultureInfo.InvariantCulture);
    }

    private static bool Holds(Dataset dataset, string subject)
    {
        using DatasetView view = dataset.Pin();

        if (!view.TryInternalise(RdfTerm.Iri(Encoding.UTF8.GetBytes(subject)), out TermHandle handle))
        {
            return false;
        }

        using IQuadCursor cursor = view.Match(handle, TermHandle.None, TermHandle.None, GraphPattern.Any);
        return cursor.MoveNext();
    }

    private static HttpRequestMessage Update(string text) => P.Update("datasets/d/sparql", text);

    private static HttpRequestMessage Keyed(HttpRequestMessage request, string agent, string key)
    {
        request.Headers.TryAddWithoutValidation(AgentHeader, agent);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "\"" + key + "\"");
        return request;
    }

    private const string AgentHeader = "X-Test-Agent";

    private static Task<ProtocolTestHost> StartAsync(Dataset dataset, CommitWindow? window = null, Func<ISparqlUpdateExecutor, ISparqlUpdateExecutor>? updates = null, IWriterDirectory? writers = null, ProtocolTestHost.Datasets? resolver = null)
    {
        ProtocolTestHost.Datasets datasets = resolver ?? new();

        if (resolver is null)
        {
            datasets.Add("d", dataset);
        }

        return ProtocolTestHost.StartAsync(
            datasets,
            map: (app, options) =>
            {
                // The caller is who the test header names, as a token's subject would.
                app.Use((context, next) =>
                {
                    if (context.Request.Headers[AgentHeader] is { Count: 1 } agent)
                    {
                        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", agent[0]!)], "test"));
                    }

                    return next(context);
                });
                ProtocolOptions keyed = window is { } w ? CopyWith(options, new IdempotencyKeys(w)) : options;
                app.MapGroup("/datasets/{dataset}").MapVarveDataset(keyed);
            },
            identity: new HeaderAgent(),
            updates: updates,
            writers: writers,
            stopping: P.Ct);
    }

    private static ProtocolOptions CopyWith(ProtocolOptions options, IdempotencyKeys keys) => new()
    {
        Datasets = options.Datasets,
        Updates = options.Updates,
        Identity = options.Identity,
        AccessScopes = options.AccessScopes,
        Authorization = options.Authorization,
        Clock = options.Clock,
        Limits = options.Limits,
        Evaluation = options.Evaluation,
        Stopping = options.Stopping,
        Telemetry = options.Telemetry,
        Writers = options.Writers,
        Forwarder = options.Forwarder,
        Idempotency = keys,
    };

    /// <summary>The agent is the token's subject as an IRI, as ADR 0094's agent is; none for a caller with no subject.</summary>
    private sealed class HeaderAgent : ICallerIdentity
    {
        public RequestTerm AgentOf(ClaimsPrincipal caller) =>
            caller.FindFirst("sub") is { } subject ? RequestTerm.FromTerm(RdfTerm.Iri(Encoding.UTF8.GetBytes("https://issuer.test/#" + Uri.EscapeDataString(subject.Value)))) : RequestTerm.None;
    }

    /// <summary>An executor that holds the first update until released, so that a second arrives while it is in flight.</summary>
    private sealed class Held(ISparqlUpdateExecutor inner, TaskCompletionSource entered, TaskCompletionSource release) : ISparqlUpdateExecutor
    {
        private int _calls;

        public async ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, UpdatePreconditions preconditions, CallerScope scope, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await inner.ExecuteAsync(dataset, update, metadata, preconditions, scope, cancellationToken);
        }
    }

    /// <summary>
    /// Two instances over one log: the writer holds the dataset open; the
    /// other forwards to it. Moving the writer closes the dataset and opens it
    /// again on the other instance, as a handover does.
    /// </summary>
    private sealed class Fleet : IAsyncDisposable
    {
        private readonly MemoryStorage _storage = new();
        private readonly ProtocolTestHost.Datasets[] _resolvers = [new(), new()];

        internal ProtocolTestHost[] Hosts { get; } = new ProtocolTestHost[2];

        internal volatile int Writer;

        internal Dataset Current { get; private set; } = null!;

        internal static async Task<Fleet> StartAsync()
        {
            Fleet fleet = new();
            fleet.Current = await Dataset.CreateAsync(fleet._storage, new DatasetId(new Guid("6f8c0000-0000-4000-8000-000000000132")), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
            fleet._resolvers[0].Add("d", fleet.Current);

            for (int i = 0; i < 2; i++)
            {
                fleet.Hosts[i] = await IdempotencyKeyTests.StartAsync(fleet.Current, writers: new Directory(fleet, i), resolver: fleet._resolvers[i]);
            }

            // The counter's seed, unkeyed.
            using HttpResponseMessage seed = await fleet.Hosts[0].Client.SendAsync(Update("INSERT DATA { <http://ex/c> <http://ex/n> 0 }"), P.Ct);
            Assert.Equal(HttpStatusCode.NoContent, seed.StatusCode);
            return fleet;
        }

        internal async Task MoveAsync()
        {
            int from = Writer;
            _resolvers[from].Remove("d");
            await Current.DisposeAsync();
            Current = await Dataset.OpenAsync(_storage, new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
            _resolvers[1 - from].Add("d", Current);
            Writer = 1 - from;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ProtocolTestHost host in Hosts)
            {
                await host.DisposeAsync();
            }

            await Current.DisposeAsync();
        }

        private sealed class Directory(Fleet fleet, int self) : IWriterDirectory
        {
            public WriterName Instance { get; } = new("instance-" + self.ToString(CultureInfo.InvariantCulture));

            public ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken) =>
                ValueTask.FromResult(fleet.Writer == self ? WriterRoute.Local : new WriterRoute(WriterRouteKind.Remote, fleet.Hosts[fleet.Writer].Address.AbsoluteUri, 7));

            public void Stale(WriterTarget target)
            {
            }
        }
    }

    /// <summary>
    /// The network between the client and its instance: the first attempt's
    /// response is lost after the server answered it, or its request before
    /// it left; at the loss the writer may move. Every later attempt passes.
    /// </summary>
    private sealed class Lossy(Fleet fleet, int lose, bool move) : DelegatingHandler(new SocketsHttpHandler { UseProxy = false })
    {
        private int _attempts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool first = Interlocked.Increment(ref _attempts) == 1;

            if (first && lose == 2)
            {
                await MoveAsync();
                throw new HttpRequestException("the request was lost before it left");
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            if (first && lose == 1)
            {
                // The server answered, and the answer never arrived.
                await response.Content.LoadIntoBufferAsync(cancellationToken);
                response.Dispose();
                await MoveAsync();
                throw new HttpRequestException("the response was lost");
            }

            return response;
        }

        private Task MoveAsync() => move ? fleet.MoveAsync() : Task.CompletedTask;
    }
}
