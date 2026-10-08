// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>Worked examples of the protocol's own rules (ADRs 0093–0098, 0101), one rule each, over a real socket.</summary>
public class BehaviourTests
{
    private const string Sparql = "datasets/d/sparql";

    // --- writes (ADR 0094) -------------------------------------------------------

    [Fact]
    public async Task a_stale_if_match_is_412_with_the_head_and_commits_nothing()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);

        HttpResponseMessage stale = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }", "\"0\""), P.Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("1", P.Header(stale, "Varve-Position"));
        Assert.Contains("precondition-failed", await stale.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal(new Position(1), dataset.Head);

        HttpResponseMessage matched = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }", "\"1\""), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, matched.StatusCode);
        Assert.Equal("2", P.Header(matched, "Varve-Position"));
    }

    [Fact]
    public async Task a_write_with_no_change_is_204_at_the_unchanged_head()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Update(Sparql, "DELETE DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("0", P.Header(response, "Varve-Position"));
        Assert.Equal(new Position(0), dataset.Head);
    }

    [Fact]
    public async Task the_agent_is_the_callers_and_the_cause_is_the_request_id()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct, identity: new Fixed("https://issuer.example/#alice"));

        HttpResponseMessage response = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        string? id = P.Header(response, "Varve-Request-Id");
        Assert.NotNull(id);

        List<FeedRecord> feed = await P.ReadFeedAsync(await host.Client.SendAsync(P.Get("datasets/d/feed?to=1"), P.Ct));
        FeedRecord commit = Assert.Single(feed);
        Assert.Equal("<https://issuer.example/#alice>", P.TermText(commit.Agent!));
        Assert.Equal("\"" + id + "\"", P.TermText(commit.Cause!));
    }

    [Fact]
    public async Task a_rejected_commit_is_422_with_the_validators_report()
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = TimeProvider.System, Validators = [new Refuses()] }, P.Ct);
        await using (dataset)
        {
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
            HttpResponseMessage response = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(P.Ct);
            Assert.Contains("rejected", body, StringComparison.Ordinal);
            Assert.Contains("refused", body, StringComparison.Ordinal);
            Assert.Equal(new Position(0), dataset.Head);
        }
    }

    [Fact]
    public async Task a_failing_operation_is_400_and_commits_nothing()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 } ; LOAD <http://ex/nowhere>"), P.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("operation-failed", await response.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal(new Position(0), dataset.Head);
    }

    // --- time travel (ADR 0096) --------------------------------------------------

    [Fact]
    public async Task an_as_of_read_answers_the_past_and_names_its_position()
    {
        ManualClock clock = ManualClock.Epoch();
        await using Dataset dataset = await P.NewDatasetAsync(clock);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, clock: clock, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        clock.Now = clock.Now.AddMinutes(1);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);

        HttpResponseMessage byPosition = await host.Client.SendAsync(P.Query(Sparql, "SELECT (COUNT(*) AS ?n) { ?s ?p ?o }", "text/csv", "position:1"), P.Ct);
        Assert.Equal("n\r\n1\r\n", await byPosition.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("1", P.Header(byPosition, "Varve-Position"));
        Assert.Equal("\"1\"", byPosition.Headers.ETag?.ToString());

        // 30 seconds after the first commit, written with an offset: it resolves to 1 (I5).
        HttpResponseMessage byTime = await host.Client.SendAsync(P.Query(Sparql, "SELECT (COUNT(*) AS ?n) { ?s ?p ?o }", "text/csv", "time:2026-10-07T14:00:30+02:00"), P.Ct);
        Assert.Equal("n\r\n1\r\n", await byTime.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("1", P.Header(byTime, "Varve-Position"));

        HttpResponseMessage head = await host.Client.SendAsync(P.Query(Sparql, "SELECT (COUNT(*) AS ?n) { ?s ?p ?o }", "text/csv"), P.Ct);
        Assert.Equal("n\r\n2\r\n", await head.Content.ReadAsStringAsync(P.Ct));
    }

    [Theory]
    [InlineData("position:3", HttpStatusCode.NotFound, "position-not-reached")]
    [InlineData("time:2026-10-07T11:00:00Z", HttpStatusCode.NotFound, "before-first-commit")]
    [InlineData("position:x", HttpStatusCode.BadRequest, "bad-request")]
    [InlineData("time:2026-10-07T12:00:00.12345678Z", HttpStatusCode.BadRequest, "bad-request")]
    public async Task an_as_of_that_resolves_to_nothing_is_a_distinct_problem(string asOf, HttpStatusCode status, string problem)
    {
        ManualClock clock = ManualClock.Epoch();
        await using Dataset dataset = await P.NewDatasetAsync(clock);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, clock: clock, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: asOf), P.Ct);
        Assert.Equal(status, response.StatusCode);
        Assert.Contains("/problems/" + problem, await response.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_write_with_an_as_of_is_refused()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        HttpRequestMessage request = P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }");
        request.Headers.TryAddWithoutValidation("Varve-As-Of", "position:0");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(request, P.Ct)).StatusCode);
        Assert.Equal(new Position(0), dataset.Head);
    }

    // --- the Graph Store (ADRs 0092, 0093) -----------------------------------------

    [Fact]
    public async Task a_graph_is_put_read_replaced_and_deleted_one_commit_each()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        string graph = "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g");

        HttpResponseMessage created = await host.Client.PutAsync(new Uri(graph, UriKind.Relative), new StringContent("<http://ex/s> <http://ex/p> _:x .\n_:x <http://ex/q> \"1\" .\n", Encoding.UTF8, "application/n-triples"), P.Ct);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(new Position(1), dataset.Head);

        HttpResponseMessage read = await host.Client.SendAsync(P.Get(graph, "application/n-triples"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(2, (await read.Content.ReadAsStringAsync(P.Ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        HttpResponseMessage replaced = await host.Client.PutAsync(new Uri(graph, UriKind.Relative), new StringContent("<http://ex/s> <http://ex/p> 2 .", Encoding.UTF8, "text/turtle"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        Assert.Equal(new Position(2), dataset.Head);

        HttpResponseMessage deleted = await host.Client.DeleteAsync(new Uri(graph, UriKind.Relative), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(new Position(3), dataset.Head);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(P.Get(graph), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync(new Uri(graph, UriKind.Relative), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task a_post_to_the_store_makes_a_graph_at_its_location()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage created = await host.Client.PostAsync(new Uri("datasets/d/graphs", UriKind.Relative), new StringContent("<http://ex/s> <http://ex/p> 1 .", Encoding.UTF8, "text/turtle"), P.Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Uri location = created.Headers.Location!;
        Assert.StartsWith(host.Address + "datasets/d/graphs/", location.ToString(), StringComparison.Ordinal);

        HttpResponseMessage read = await host.Client.SendAsync(P.Get(location.PathAndQuery.TrimStart('/'), "text/turtle"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("<http://ex/s>", await read.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_graph_store_body_with_a_named_graph_is_refused()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        HttpResponseMessage response = await host.Client.PutAsync(new Uri("datasets/d/graphs?default", UriKind.Relative),
            new StringContent("<http://ex/s> <http://ex/p> 1 <http://ex/g> .", Encoding.UTF8, "application/n-quads"), P.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new Position(0), dataset.Head);
    }

    // --- the feed and the diff (ADR 0097) ------------------------------------------

    [Fact]
    public async Task a_bounded_feed_is_finite_and_filters_by_graph_with_settings_always_delivered()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/g> { <http://ex/a> <http://ex/p> 1 } }"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);
        await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = AccessScope.Current },
            new CommitMetadata { Agent = RdfTerm.Iri("http://ex/admin"u8), Cause = RdfTerm.Literal("test"u8) }, cancellationToken: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/g> { <http://ex/c> <http://ex/p> 3 } }"), P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/feed?from=0&to=4&graph=" + Uri.EscapeDataString("http://ex/g")), P.Ct);
        Assert.Equal("application/vnd.varve.delta", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0", P.Header(response, "Varve-Position"));
        List<FeedRecord> records = await P.ReadFeedAsync(response);
        Assert.Equal([1L, 3, 4], records.Select(r => r.Position.Value));
        Assert.Equal([CommitKind.Data, CommitKind.Settings, CommitKind.Data], records.Select(r => r.CommitKind));
        Assert.Empty(records[1].Changes);
    }

    [Fact]
    public async Task a_pattern_term_the_dataset_does_not_know_matches_once_a_commit_allocates_it()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/new> \"x y\" ; <http://ex/p> 2 }"), P.Ct);

        string pattern = Uri.EscapeDataString("?s <http://ex/new> \"x y\"");
        List<FeedRecord> records = await P.ReadFeedAsync(await host.Client.SendAsync(P.Get("datasets/d/feed?to=2&pattern=" + pattern), P.Ct));
        FeedRecord record = Assert.Single(records);
        Assert.Equal(new Position(2), record.Position);
        Assert.Equal("<http://ex/a> <http://ex/new> \"x y\"", P.Line(Assert.Single(record.Changes)));
    }

    [Fact]
    public async Task a_live_feed_streams_server_sent_events_that_resume_by_last_event_id()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);

        HttpRequestMessage request = P.Get("datasets/d/feed", "text/event-stream");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "1");
        using HttpResponseMessage response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, P.Ct);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1", P.Header(response, "Varve-Position"));
        using System.IO.StreamReader reader = new(await response.Content.ReadAsStreamAsync(P.Ct));

        Assert.Equal("id: 2", await reader.ReadLineAsync(P.Ct));
        Assert.Equal("event: commit", await reader.ReadLineAsync(P.Ct));
        Assert.StartsWith("data: commit 2 Data ", await reader.ReadLineAsync(P.Ct), StringComparison.Ordinal);
        Assert.StartsWith("data: cause ", await reader.ReadLineAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal("data: + <http://ex/b> <http://ex/p> \"2\"^^<http://www.w3.org/2001/XMLSchema#integer>", await reader.ReadLineAsync(P.Ct));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(P.Ct));

        // Live: the next commit arrives as it closes.
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/c> <http://ex/p> 3 }"), P.Ct);
        Assert.Equal("id: 3", await reader.ReadLineAsync(P.Ct));
    }

    [Fact]
    public async Task the_diff_is_r3_between_two_positions_and_its_inverse_backwards()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "DELETE DATA { <http://ex/a> <http://ex/p> 1 } ; INSERT DATA { <http://ex/b> <http://ex/p> 2 }"), P.Ct);

        FeedRecord forward = Assert.Single(await P.ReadFeedAsync(await host.Client.SendAsync(P.Get("datasets/d/diff?from=0&to=2"), P.Ct)));
        Assert.Equal(FeedRecordKind.Diff, forward.Kind);
        Assert.Equal(["+ <http://ex/b> <http://ex/p> \"2\"^^<http://www.w3.org/2001/XMLSchema#integer>"],
            forward.Changes.Select(c => (c.Kind == FeedChangeKind.Assert ? "+ " : "- ") + P.Line(c)));

        FeedRecord backward = Assert.Single(await P.ReadFeedAsync(await host.Client.SendAsync(P.Get("datasets/d/diff?from=2&to=0"), P.Ct)));
        Assert.All(backward.Changes, c => Assert.Equal(FeedChangeKind.Retract, c.Kind));
    }

    // --- limits (ADR 0095) ---------------------------------------------------------

    [Fact]
    public async Task a_read_cut_before_it_started_is_a_503_problem()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        ProtocolLimits limits = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(100), new ByteCount(1 << 20), TimeSpan.FromSeconds(15));
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: limits, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 50).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"), P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, "SELECT * { ?s ?p ?o }"), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("read-limit-exceeded", await response.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_read_cut_after_it_started_is_never_a_whole_looking_document()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        ProtocolLimits limits = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(200_000), new ByteCount(1 << 20), TimeSpan.FromSeconds(15));
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: limits, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 5000).Select(i => "<http://ex/s" + i + "> <http://ex/p> " + i + " . ")) + "}"), P.Ct);

        // Over HTTP/1.1 there are no trailers: the connection is aborted, and
        // the client sees a transfer that did not complete.
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, "SELECT * { ?s ?p ?o }"), P.Ct);
            await response.Content.ReadAsByteArrayAsync(P.Ct);
        });
    }

    // --- status (ADR 0101) and names (ADR 0093) ------------------------------------

    [Fact]
    public async Task the_status_names_the_dataset_its_head_and_its_settings()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/status"), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"d\"", body, StringComparison.Ordinal);
        Assert.Contains("\"head\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"defaultAccessScope\":\"AllHistory\"", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("datasets/..%2F/sparql")]
    [InlineData("datasets/-x/sparql")]
    [InlineData("datasets/a%20b/sparql")]
    public async Task a_name_that_is_not_a_path_segment_is_the_same_404_as_an_unknown_one(string path)
    {
        ProtocolTestHost.Datasets datasets = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, stopping: P.Ct);

        HttpResponseMessage invalid = await host.Client.SendAsync(P.Get(path), P.Ct);
        HttpResponseMessage unknown = await host.Client.SendAsync(P.Get("datasets/nothere/sparql"), P.Ct);
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(P.Ct), await invalid.Content.ReadAsStringAsync(P.Ct));
    }

    private sealed class Fixed(string agent) : ICallerIdentity
    {
        public RequestTerm AgentOf(ClaimsPrincipal caller) => RdfTerm.Iri(Encoding.UTF8.GetBytes(agent));
    }

    private sealed class Refuses : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) =>
            ValidationVerdict.Reject([RdfTerm.Literal("refused"u8)]);
    }
}
