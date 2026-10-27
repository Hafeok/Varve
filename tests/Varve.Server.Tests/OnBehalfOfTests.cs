// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Varve's side of on-behalf-of (ADR 0127), against the issuer in the test:
/// a token minted as an exchange would mint it, with the user's <c>oid</c>
/// and the acting application's <c>azp</c>, is accepted as any token is; the
/// commit's agent is the user, never the application; the request span
/// carries the application as <c>varve.azp</c>. The client sends it through
/// its per-request provider, as an application acting for many users does.
/// The exchange itself is Entra's, on the providers leg (ADR 0100).
/// </summary>
[Collection(nameof(TelemetryTests))]
public sealed class OnBehalfOfTests : IAsyncLifetime
{
    private const string User = "6f1c2c3e-user-oid";
    private const string Application = "a9e4-calling-application";
    private static readonly string[] WriterRole = ["Varve.Write"];
    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = _issuer.Issuer,
            ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
            ["Varve:Auth:RequireHttpsMetadata"] = "false",
            ["Varve:Auth:SubjectClaim"] = "oid",
            ["Varve:Auth:Datasets:d:Read:0"] = "Varve.Write",
            ["Varve:Auth:Datasets:d:Write:0"] = "Varve.Write",
            ["Varve:Datasets:d:Storage"] = "Memory",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    [Fact]
    public async Task A_token_for_a_user_through_an_application_commits_as_the_user_and_the_span_names_the_application()
    {
        ConcurrentBag<Activity> spans = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Varve.Protocol",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        // As the exchange answers: the user's oid, the application's azp, and
        // the application roles the user holds in Varve's registration.
        string token = _issuer.Mint(new Dictionary<string, object>
        {
            ["oid"] = User,
            ["sub"] = "pairwise-subject-not-the-agent",
            ["azp"] = Application,
            ["scp"] = "access_as_user",
            ["roles"] = WriterRole,
        });
        using HttpClient http = OutboundHttp.CreateClient();
        SparqlHttpClient client = new(http, new Uri(_server.Address, "datasets/d/"), ClientLimits.Default, BearerTokens.Fixed(token), TimeProvider.System);

        using (HttpResponseMessage update = await client.UpdateAsync("INSERT DATA { <http://ex/order/1> <http://ex/placedBy> <http://ex/customer/7> }", ifMatch: null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        using (HttpResponseMessage query = await client.QueryAsync("ASK { <http://ex/order/1> ?p ?o }", "application/sparql-results+json", null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, query.StatusCode);
            Assert.Contains("true", await query.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        using HttpResponseMessage commits = await client.CommitsAsync(0, 1, null, eventStream: false, TestContext.Current.CancellationToken);
        List<FeedRecord> records = [];

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(await commits.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        FeedRecord commit = Assert.Single(records);
        Assert.Equal(_issuer.Issuer + "#" + User, System.Text.Encoding.UTF8.GetString(commit.Agent!.Lexical));

        // Every request this client made is a span that names the application.
        List<Activity> mine = [.. spans.Where(span => span.GetTagItem("db.namespace") is "d" && span.GetTagItem("varve.azp") is not null)];
        Assert.True(mine.Count >= 3, "the update, the query and the commits read each carry varve.azp");
        Assert.All(mine, span => Assert.Equal(Application, span.GetTagItem("varve.azp")));
    }

    [Fact]
    public async Task An_applications_own_token_commits_as_the_application()
    {
        // Client credentials: no user, so the subject is the application's
        // own object id, and the agent is the application (the attribution rule).
        string token = _issuer.Mint(new Dictionary<string, object>
        {
            ["oid"] = "service-principal-oid",
            ["azp"] = Application,
            ["roles"] = WriterRole,
        });
        using HttpClient http = OutboundHttp.CreateClient();
        SparqlHttpClient client = new(http, new Uri(_server.Address, "datasets/d/"), ClientLimits.Default, BearerTokens.Fixed(token), TimeProvider.System);

        using (HttpResponseMessage update = await client.UpdateAsync("INSERT DATA { <http://ex/import/1> <http://ex/p> 1 }", ifMatch: null, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        }

        using HttpResponseMessage last = await client.CommitsAsync(0, 1, null, eventStream: false, TestContext.Current.CancellationToken);
        List<FeedRecord> records = [];

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(await last.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(_issuer.Issuer + "#service-principal-oid", System.Text.Encoding.UTF8.GetString(Assert.Single(records).Agent!.Lexical));
    }
}
