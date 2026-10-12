// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Resource governance (ADR 0114): every limit, when hit, produces its
/// problem type and never a timeout or a disconnect; the memory bound is
/// counted and fails before the process pays; an as-of read beyond the
/// distance bound is refused before any log is read; a client's live tails
/// are bounded; a bounded commits range is paged.
/// </summary>
public class GovernanceTests
{
    private const string Sparql = "datasets/d/sparql";

    private static ProtocolLimits Limits(long memory = 256L << 20, long asOfDistance = 10_000, int tails = 16, int page = 1_000, int heartbeatMs = 15_000) =>
        new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(1L << 30), new ByteCount(100L << 20), TimeSpan.FromMilliseconds(heartbeatMs))
        {
            MaxQueryMemory = new MemoryBytes(memory),
            MaxAsOfDistance = asOfDistance,
            MaxLiveTailsPerClient = tails,
            CommitsPageSize = page,
        };

    [Theory]
    [InlineData("SELECT ?s ?o WHERE { ?s ?p ?o } ORDER BY ?o")]
    [InlineData("SELECT ?s (COUNT(?o) AS ?n) WHERE { ?s ?p ?o } GROUP BY ?s")]
    [InlineData("SELECT ?s WHERE { ?s <http://ex/p> ?o MINUS { ?s <http://ex/q> ?o2 } }")]
    public async Task a_materialising_query_over_the_memory_bound_is_422_naming_the_limit_before_the_first_byte(string query)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await Fill(dataset, 600, literal: 8);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(memory: 4096), stopping: P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, query), P.Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal(ProblemType.MemoryLimitExceeded.Value, problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(4096, problem.RootElement.GetProperty("limit").GetInt64());
        Assert.True(problem.RootElement.GetProperty("actual").GetInt64() > 4096);
    }

    /// <summary>
    /// Over HTTP/2 a cut after the first byte is the <c>Varve-Error</c> trailer
    /// naming the type, and the stream completes (ADRs 0095, 0114). Kestrel
    /// sends no HTTP/1.1 trailers, so there the connection is aborted, as
    /// ADR 0095 says and <c>BehaviourTests</c> shows.
    /// </summary>
    [Fact]
    public async Task a_distinct_over_the_bound_after_the_first_byte_ends_in_the_trailer_over_http2()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await Fill(dataset, 2_000, literal: 200);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(memory: 64 * 1024), http2: true, stopping: P.Ct);

        HttpRequestMessage request = P.Query(Sparql, "SELECT DISTINCT ?s ?o WHERE { ?s <http://ex/p> ?o }", accept: "text/csv");
        request.Version = HttpVersion.Version20;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(body.Length > 32 * 1024, "the cut came before the first byte reached the client: " + body.Length);
        Assert.Equal(ProblemType.MemoryLimitExceeded.Value, response.TrailingHeaders.GetValues("Varve-Error").Single());
    }

    [Fact]
    public async Task an_as_of_read_beyond_the_distance_bound_is_422_and_a_checkpoint_admits_it()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(asOfDistance: 3), stopping: P.Ct);

        for (int i = 1; i <= 10; i++)
        {
            await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/s" + i + "> <http://ex/p> " + i + " }"), P.Ct);
        }

        // Eight commits above position 0, with no checkpoint: refused before the log is read.
        HttpResponseMessage far = await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: "position:8"), P.Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, far.StatusCode);
        using (JsonDocument problem = JsonDocument.Parse(await far.Content.ReadAsStringAsync(P.Ct)))
        {
            Assert.Equal(ProblemType.AsOfDistanceExceeded.Value, problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(3, problem.RootElement.GetProperty("limit").GetInt64());
            Assert.Equal(8, problem.RootElement.GetProperty("actual").GetInt64());
        }

        // Within the bound of position 0.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: "position:3"), P.Ct)).StatusCode);

        // A checkpoint at 6 brings 8 within two commits of one; 4 stays four above position 0.
        await dataset.CheckpointAsync(new Position(6), P.Ct);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: "position:8"), P.Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: "position:4"), P.Ct)).StatusCode);

        // The head needs no replay and is never refused.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Query(Sparql, "ASK {}", asOf: "position:10"), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task a_client_over_the_live_tail_bound_is_429_and_a_closed_tail_frees_the_slot()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        // The heartbeat is how the server notices a tail whose client has gone:
        // its slot is freed at the latest at the next heartbeat's failed write.
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(tails: 2, heartbeatMs: 100), stopping: P.Ct);

        HttpResponseMessage first = await host.Client.SendAsync(P.Get("datasets/d/commits", "text/event-stream"), HttpCompletionOption.ResponseHeadersRead, P.Ct);
        HttpResponseMessage second = await host.Client.SendAsync(P.Get("datasets/d/commits", "text/event-stream"), HttpCompletionOption.ResponseHeadersRead, P.Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        HttpResponseMessage third = await host.Client.SendAsync(P.Get("datasets/d/commits", "text/event-stream"), P.Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        using (JsonDocument problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync(P.Ct)))
        {
            Assert.Equal(ProblemType.TooManyLiveTails.Value, problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(2, problem.RootElement.GetProperty("limit").GetInt64());
        }

        // A bounded range is a read, not a tail, and is not counted.
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(P.Get("datasets/d/commits?to=0"), P.Ct)).StatusCode);

        first.Dispose();

        for (int attempt = 0; attempt < 100; attempt++)
        {
            HttpResponseMessage again = await host.Client.SendAsync(P.Get("datasets/d/commits", "text/event-stream"), HttpCompletionOption.ResponseHeadersRead, P.Ct);

            if (again.StatusCode == HttpStatusCode.OK)
            {
                again.Dispose();
                second.Dispose();
                return;
            }

            await Task.Delay(50, P.Ct);
        }

        Assert.Fail("the slot of the closed tail was not freed");
    }

    [Fact]
    public async Task a_bounded_commits_range_is_paged_with_a_link_to_the_next_page()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(page: 3), stopping: P.Ct);

        for (int i = 1; i <= 10; i++)
        {
            await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <http://ex/g> { <http://ex/s" + i + "> <http://ex/p> " + i + " } }"), P.Ct);
        }

        string? next = "datasets/d/commits?from=0&to=10&graph=" + Uri.EscapeDataString("http://ex/g");
        List<long> positions = [];
        int pages = 0;

        while (next is not null)
        {
            HttpResponseMessage page = await host.Client.SendAsync(P.Get(next), P.Ct);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            pages++;
            List<FeedRecord> records = await P.ReadFeedAsync(page);
            positions.AddRange(records.Select(r => r.Position.Value));
            // Two links: the service description on every response, and the next page while there is one.
            string? link = page.Headers.GetValues("Link").SingleOrDefault(l => l.EndsWith("; rel=\"next\"", StringComparison.Ordinal));
            next = link is null ? null : link[1..link.IndexOf('>', StringComparison.Ordinal)].TrimStart('/');

            if (link is not null)
            {
                Assert.Contains("graph=http%3A%2F%2Fex%2Fg", link, StringComparison.Ordinal);
                Assert.Equal(3, records.Count);
            }
        }

        Assert.Equal(4, pages);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), positions);
    }

    /// <summary>
    /// The DoD's property: a request over the memory bound fails before the
    /// process's working set has grown by more than the bound plus a stated
    /// constant, 64 MiB, the allowance for the collector and the response
    /// buffers. Counted, not collected.
    /// </summary>
    [Fact]
    public async Task a_request_over_the_memory_bound_fails_before_the_working_set_grows_by_more_than_the_bound_plus_the_constant()
    {
        const long Bound = 1L << 20;
        const long Constant = 64L << 20;
        await using Dataset dataset = await P.NewDatasetAsync();
        await Fill(dataset, 120_000, literal: 16);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: Limits(memory: Bound), stopping: P.Ct);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using Process self = Process.GetCurrentProcess();
        long before = self.WorkingSet64;

        HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, "SELECT ?s ?o WHERE { ?s <http://ex/p> ?o } ORDER BY ?o"), P.Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        self.Refresh();
        long grown = self.WorkingSet64 - before;
        TestContext.Current.TestOutputHelper?.WriteLine("working set grew " + grown.ToString(CultureInfo.InvariantCulture) + " bytes against a bound of " + Bound.ToString(CultureInfo.InvariantCulture));
        Assert.True(grown <= Bound + Constant, "the working set grew " + grown + " bytes, over the bound plus the constant");
    }

    private static async Task Fill(Dataset dataset, int rows, int literal)
    {
        string pad = new('x', literal);

        for (int batch = 0; batch < rows; batch += 5_000)
        {
            CommitRequest request = new();

            for (int i = batch; i < Math.Min(rows, batch + 5_000); i++)
            {
                RdfTerm s = RdfTerm.Iri(Encoding.UTF8.GetBytes("http://ex/s" + i.ToString(CultureInfo.InvariantCulture)));
                request.Assert(s, RdfTerm.Iri("http://ex/p"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes(pad + i.ToString(CultureInfo.InvariantCulture))));
                request.Assert(s, RdfTerm.Iri("http://ex/q"u8), RdfTerm.Literal(Encoding.UTF8.GetBytes(i.ToString(CultureInfo.InvariantCulture))));
            }

            await dataset.CommitAsync(request, P.Ct);
        }
    }
}
