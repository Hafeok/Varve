// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Protocol.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Health and readiness (ADR 0113): the readiness decision as a property over
/// generated dataset states, the drain that turns it false before the
/// listener closes, and the probes' rate limit.
/// </summary>
public class HealthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Gen<DatasetHealth> Health = Gen.Select(
        Gen.Int[0, 99],
        Gen.OneOfConst(DatasetState.Open, DatasetState.Open, DatasetState.Open, DatasetState.Closed, DatasetState.Failed),
        Gen.Long[0, 50],
        Gen.Long[0, 50],
        Gen.Bool,
        (n, state, head, behind, failed) => new DatasetHealth("d" + n, state, state == DatasetState.Failed ? "held elsewhere" : null, head + behind, head, state == DatasetState.Open && failed));

    /// <summary>
    /// Readiness is false exactly while a dataset is failed, a projection is
    /// behind by more than the configured lag, or the host is draining; a
    /// closed dataset never counts.
    /// </summary>
    [Fact]
    public void readiness_is_false_exactly_while_a_projection_is_behind_the_allowed_lag_or_failed_or_draining()
    {
        Gen.Select(Health.Array[0, 6], Gen.Long[0, 60], Gen.Bool).Sample((datasets, allowed, draining) =>
        {
            (bool ready, List<DatasetReadiness> states) = Readiness.Evaluate(datasets, allowed, draining);
            bool anyFailed = datasets.Any(d => d.State == DatasetState.Failed || (d.State == DatasetState.Open && d.Failed));
            bool anyBehind = datasets.Any(d => d.State == DatasetState.Open && !d.Failed && d.Head - d.Projection > allowed);
            Assert.Equal(!draining && !anyFailed && !anyBehind, ready);
            Assert.Equal(datasets.Length, states.Count);

            foreach ((DatasetHealth dataset, DatasetReadiness state) in datasets.Zip(states))
            {
                string expected = dataset.State switch
                {
                    DatasetState.Closed => "closed",
                    DatasetState.Failed => "failed",
                    _ when dataset.Failed => "failed",
                    _ when dataset.Head - dataset.Projection > allowed => "behind",
                    _ => "ready",
                };
                Assert.Equal(expected, state.State);
                Assert.Equal(expected == "behind" ? dataset.Head - dataset.Projection : null, state.Lag);
            }
        }, iter: 2_000);
    }

    [Fact]
    public async Task readiness_is_false_during_the_drain_while_liveness_and_the_listener_stay_up()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Health:StopDelay"] = "00:00:03",
        });
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct)).StatusCode);

        Task<int> stopping = server.StopAsync();
        HttpResponseMessage draining = await server.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, draining.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await draining.Content.ReadAsStringAsync(Ct));
        Assert.Equal("https://w3id.org/varve/problems/not-ready", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("The server is draining.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Empty(problem.RootElement.GetProperty("datasets").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("health/live", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(0, await stopping);
    }

    /// <summary>
    /// The concurrent-read limit (ADR 0114): with one permit and no queue,
    /// overlapping reads are refused with 503 server-busy and Retry-After,
    /// never queued without end; a write is not counted.
    /// </summary>
    [Fact]
    public async Task reads_over_the_concurrency_limit_are_503_server_busy_with_retry_after_and_writes_are_not_counted()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Limits:MaxConcurrentReads"] = "1",
            ["Varve:Limits:ReadQueueLength"] = "0",
        });
        System.Text.StringBuilder data = new("INSERT DATA { ");

        for (int i = 0; i < 3_000; i++)
        {
            data.Append("<http://ex/s").Append(i).Append("> <http://ex/p> \"").Append(new string('x', 64)).Append("\" . ");
        }

        data.Append('}');
        Assert.Equal(HttpStatusCode.NoContent, (await server.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), new StringContent(data.ToString(), System.Text.Encoding.UTF8, "application/sparql-update"), Ct)).StatusCode);

        Task<HttpResponseMessage>[] reads = [.. Enumerable.Range(0, 32).Select(_ => server.Client.GetAsync(new Uri("datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT * WHERE { ?s ?p ?o }"), UriKind.Relative), Ct))];
        Task<HttpResponseMessage> write = server.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), new StringContent("INSERT DATA { <http://ex/w> <http://ex/p> 1 }", System.Text.Encoding.UTF8, "application/sparql-update"), Ct);
        HttpResponseMessage[] answers = await Task.WhenAll(reads);
        Assert.Equal(HttpStatusCode.NoContent, (await write).StatusCode);

        int busy = 0;

        foreach (HttpResponseMessage answer in answers)
        {
            if (answer.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                busy++;
                Assert.Equal("application/problem+json", answer.Content.Headers.ContentType?.MediaType);
                Assert.NotNull(answer.Headers.RetryAfter);
                using JsonDocument problem = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(Ct));
                Assert.Equal(ProblemType.ServerBusy.Value, problem.RootElement.GetProperty("type").GetString());
                Assert.Equal(1, problem.RootElement.GetProperty("limit").GetInt64());
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            }
        }

        Assert.True(busy > 0, "no read was refused: the reads did not overlap");
        Assert.True(busy < answers.Length, "every read was refused");
    }

    [Fact]
    public async Task the_probes_are_rate_limited_per_client_address()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Health:RateLimit"] = "3",
        });

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("health/live", UriKind.Relative), Ct)).StatusCode);
        }

        HttpResponseMessage limited = await server.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Contains("too-many-requests", await limited.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A dataset endpoint is not under the probes' limit.
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("datasets/d/sparql?query=ASK%7B%7D", UriKind.Relative), Ct)).StatusCode);
    }
}
