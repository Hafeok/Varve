// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Graph versions over HTTP (ADR 0126), one rule each, over a real socket: a
/// graph's tag is its version, at the head and as of a position; If-Match on
/// a Graph Store write names it, an empty graph matches no tag, and
/// If-None-Match: * creates only; writers of other graphs do not conflict;
/// Varve-If-Graphs-Match on an update; a conflict lists what caused it.
/// </summary>
public sealed class GraphVersionTests
{
    private const string Sparql = "datasets/d/sparql";

    private static string Graph(string local) => "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/" + local);

    private static HttpRequestMessage Put(string path, string body, string? ifMatch = null, string? ifNoneMatch = null)
    {
        HttpRequestMessage request = new(HttpMethod.Put, path) { Content = new StringContent(body, Encoding.UTF8, "text/turtle") };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return request;
    }

    private static string? Tag(HttpResponseMessage response) => response.Headers.ETag?.Tag;

    [Fact]
    public async Task a_graph_read_is_tagged_with_its_version_and_polled_by_it()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 1 ."), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(Put(Graph("b"), "<http://ex/s> <http://ex/p> 2 ."), P.Ct)).StatusCode);

        // Graph a was last touched at 1; the head is 2.
        HttpResponseMessage read = await host.Client.SendAsync(P.Get(Graph("a"), "application/n-triples"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("\"1\"", Tag(read));
        Assert.Equal("2", P.Header(read, "Varve-Position"));

        // A poll with the graph's version is answered 304, though the head moved on.
        HttpRequestMessage poll = P.Get(Graph("a"), "application/n-triples");
        poll.Headers.TryAddWithoutValidation("If-None-Match", "\"1\"");
        HttpResponseMessage unchanged = await host.Client.SendAsync(poll, P.Ct);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal("\"1\"", Tag(unchanged));

        // As of position 2 and as of 1, graph a has the same version, and the same tag.
        foreach (string asOf in new[] { "position:1", "position:2" })
        {
            HttpRequestMessage past = P.Get(Graph("a"), "application/n-triples");
            past.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
            HttpResponseMessage then = await host.Client.SendAsync(past, P.Ct);
            Assert.Equal(HttpStatusCode.OK, then.StatusCode);
            Assert.Equal("\"1\"", Tag(then));
        }

        // A query is still tagged with the head.
        HttpResponseMessage query = await host.Client.SendAsync(P.Query(Sparql, "ASK { ?s ?p ?o }"), P.Ct);
        Assert.Equal("\"2\"", Tag(query));
    }

    [Fact]
    public async Task if_match_on_a_graph_names_its_version_and_writers_of_other_graphs_do_not_conflict()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage a = await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 1 ."), P.Ct);
        Assert.Equal("\"1\"", Tag(a));
        await host.Client.SendAsync(Put(Graph("b"), "<http://ex/s> <http://ex/p> 1 ."), P.Ct);
        await host.Client.SendAsync(Put(Graph("b"), "<http://ex/s> <http://ex/p> 2 ."), P.Ct);

        // Graph a is still at 1, though the head is 3: a's version matches.
        HttpResponseMessage replaced = await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 9 .", ifMatch: "\"1\""), P.Ct);
        Assert.True(replaced.StatusCode == HttpStatusCode.NoContent, await replaced.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("\"4\"", Tag(replaced));

        // The head's position is not graph a's version: 412, with the version it has.
        HttpResponseMessage stale = await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 10 .", ifMatch: "\"3\""), P.Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync(P.Ct));
        JsonElement entry = problem.RootElement.GetProperty("graphs")[0];
        Assert.Equal("http://ex/a", entry.GetProperty("graph").GetString());
        Assert.Equal(4, entry.GetProperty("version").GetInt64());
        Assert.Equal(new Position(4), dataset.Head);
    }

    [Fact]
    public async Task an_empty_graph_matches_no_tag_and_if_none_match_star_creates_only()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        // A graph with no quads has no representation: If-Match is 412, even *.
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(Put(Graph("new"), "<http://ex/s> <http://ex/p> 1 .", ifMatch: "\"0\""), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(Put(Graph("new"), "<http://ex/s> <http://ex/p> 1 .", ifMatch: "*"), P.Ct)).StatusCode);

        // If-None-Match: * creates it; a second create-only PUT is 412.
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(Put(Graph("new"), "<http://ex/s> <http://ex/p> 1 .", ifNoneMatch: "*"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(Put(Graph("new"), "<http://ex/s> <http://ex/p> 2 .", ifNoneMatch: "*"), P.Ct)).StatusCode);

        // Emptied, it is gone; its version still counts: a writer that read it before conflicts.
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(new Uri(Graph("new"), UriKind.Relative), P.Ct)).StatusCode);
        Assert.Equal(new Position(2), dataset.GraphVersion(Varve.Rdf.RdfTerm.Iri("http://ex/new"u8)));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await host.Client.SendAsync(Put(Graph("new"), "<http://ex/s> <http://ex/p> 3 .", ifMatch: "\"1\""), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task an_update_names_the_graphs_it_read_with_varve_if_graphs_match()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> 1 } }"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/b> { <http://ex/c> <http://ex/v> 1 } }"), P.Ct);

        const string increment = "DELETE { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> ?x } } INSERT { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> ?y } } WHERE { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> ?x } BIND (?x + 1 AS ?y) }";

        // Graph a is at 1 though the head is 2: the precondition holds.
        HttpRequestMessage fresh = P.Update(Sparql, increment);
        fresh.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", "<http://ex/a>=1");
        HttpResponseMessage committed = await host.Client.SendAsync(fresh, P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, committed.StatusCode);
        Assert.Equal(new Position(3), dataset.Head);

        // Now it is at 3: naming 1 again is 412, with each graph that moved.
        HttpRequestMessage stale = P.Update(Sparql, increment);
        stale.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", "<http://ex/a>=1, <http://ex/b>=2, default=0");
        HttpResponseMessage refused = await host.Client.SendAsync(stale, P.Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(["http://ex/a"], problem.RootElement.GetProperty("graphs").EnumerateArray().Select(g => g.GetProperty("graph").GetString()));
        Assert.Equal(new Position(3), dataset.Head);

        // Malformed, or with If-Match too: 400.
        foreach ((string header, string? ifMatch) in new[] { ("<http://ex/a>", (string?)null), ("<a>=1", null), ("<http://ex/a>=1, <http://ex/a>=2", null), ("<http://ex/a>=3", "\"3\"") })
        {
            HttpRequestMessage bad = P.Update(Sparql, increment, ifMatch);
            bad.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", header);
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(bad, P.Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task a_conflict_lists_the_commits_that_touched_the_scope()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 1 ."), P.Ct);

        // A writer that read graph a at 1, and commits after two writers:
        // one of graph a, one of graph b. The store answers Conflict; the
        // protocol names the commit that touched a, and only a.
        CommitRequest late = new CommitRequest { ExpectedPosition = new Position(1), ExpectedGraphs = [Varve.Rdf.RdfTerm.Iri("http://ex/a"u8)] }
            .Assert(Varve.Rdf.RdfTerm.Iri("http://ex/s"u8), Varve.Rdf.RdfTerm.Iri("http://ex/p"u8), Varve.Rdf.RdfTerm.Iri("http://ex/o"u8), Varve.Rdf.RdfTerm.Iri("http://ex/a"u8));
        await host.Client.SendAsync(Put(Graph("b"), "<http://ex/s> <http://ex/p> 1 ."), P.Ct);
        await host.Client.SendAsync(Put(Graph("a"), "<http://ex/s> <http://ex/p> 2 ."), P.Ct);
        Assert.Equal(CommitOutcome.Conflict, (await dataset.CommitAsync(late, P.Ct)).Outcome);

        CommitConflicts conflicts = await dataset.ConflictsAsync(new Position(1), [Varve.Rdf.RdfTerm.Iri("http://ex/a"u8)], 100, 10_000, P.Ct);
        Assert.Equal([3L], conflicts.Commits.Select(c => c.Position.Value));
        Assert.False(conflicts.Truncated);

        // Over HTTP: a PUT with If-Match at a's old version loses the race
        // inside the sequencer when another writer lands between its check
        // and its commit; the 409 names the commit and graph a.
        HttpRequestMessage put = Put(Graph("a"), "<http://ex/s> <http://ex/p> 3 .", ifMatch: "\"3\"");
        HttpResponseMessage response = await host.Client.SendAsync(put, P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        CommitConflicts bounded = await dataset.ConflictsAsync(new Position(0), null, 1, 10_000, P.Ct);
        Assert.Single(bounded.Commits);
        Assert.True(bounded.Truncated);
    }

    /// <summary>
    /// A SERVICE that, the first times it is called, commits to a graph of
    /// the dataset: a writer landing between an update's pin and its commit,
    /// made deterministic.
    /// </summary>
    private sealed class Racing(Dataset dataset, string graph, int races) : IServiceHandler
    {
        private int _calls;

        internal int Calls => _calls;

        public ServiceResult Execute(ServiceRequest request, System.Threading.CancellationToken cancellationToken)
        {
            int call = System.Threading.Interlocked.Increment(ref _calls);

            if (call <= races)
            {
                CommitRequest write = new CommitRequest().Assert(RdfTerm.Iri("http://ex/race"u8), RdfTerm.Iri("http://ex/n"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes("r" + call)), RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/" + graph)));
                dataset.CommitAsync(write, cancellationToken).AsTask().GetAwaiter().GetResult();
            }

            return ServiceResult.FromSolutions([], [[]]);
        }
    }

    private const string ReadsA = "INSERT { GRAPH <http://ex/a> { <http://ex/seen> <http://ex/p> ?x } } WHERE { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> ?x } SERVICE <http://svc/> { } }";

    [Fact]
    public async Task an_update_with_no_precondition_from_the_client_is_retried_when_its_inferred_scope_was_touched()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        Racing racing = new(dataset, "a", races: 1);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, serviceHandler: racing, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> 1 } }"), P.Ct);

        // The first evaluation is overtaken by a commit to graph a, which it
        // read; the server re-pins and evaluates again (ADR 0057, amended).
        HttpResponseMessage response = await host.Client.SendAsync(P.Update(Sparql, ReadsA), P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(2, racing.Calls);
        Assert.Equal(new Position(3), dataset.Head);
    }

    [Fact]
    public async Task an_update_whose_client_named_its_graphs_is_never_retried_and_its_conflict_names_the_commit()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        Racing racing = new(dataset, "a", races: 1);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, serviceHandler: racing, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> 1 } }"), P.Ct);

        HttpRequestMessage named = P.Update(Sparql, ReadsA);
        named.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", "<http://ex/a>=1");
        HttpResponseMessage response = await host.Client.SendAsync(named, P.Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, racing.Calls);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(P.Ct));
        JsonElement commit = problem.RootElement.GetProperty("graphs")[0];
        Assert.Equal(2, commit.GetProperty("position").GetInt64());
        Assert.Equal(["http://ex/a"], commit.GetProperty("graphs").EnumerateArray().Select(g => g.GetString()));
        Assert.False(problem.RootElement.GetProperty("graphsTruncated").GetBoolean());
    }

    [Fact]
    public async Task a_commit_to_a_graph_the_update_did_not_read_is_no_conflict()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        Racing racing = new(dataset, "b", races: 1);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, serviceHandler: racing, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/a> { <http://ex/c> <http://ex/v> 1 } }"), P.Ct);

        HttpRequestMessage named = P.Update(Sparql, ReadsA);
        named.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", "<http://ex/a>=1");
        HttpResponseMessage response = await host.Client.SendAsync(named, P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(1, racing.Calls);
    }

    [Fact]
    public async Task the_service_description_advertises_graph_versions()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        string description = await (await host.Client.SendAsync(P.Get("datasets/d/sparql", "text/turtle"), P.Ct)).Content.ReadAsStringAsync(P.Ct);
        Assert.Contains("Varve-If-Graphs-Match", description, StringComparison.Ordinal);
        Assert.Contains("graphVersionETag", description, StringComparison.Ordinal);
    }
}
