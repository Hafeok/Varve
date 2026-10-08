// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Authentication and authorisation (ADRs 0037, 0100 layer (a), 0106): every
/// endpoint against every way a token can be wrong and every permission,
/// with tokens from an issuer in the test; the admin API's endpoints under
/// the dataset and server-admin grants.
/// </summary>
public sealed class AuthTests : IAsyncLifetime
{
    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    /// <summary>
    /// Each endpoint, and the permission it needs (ADR 0093's table, ADR
    /// 0106's). The list answers any authenticated caller, filtered, so it
    /// needs no more than read; the server-admin rows run in an order that
    /// succeeds for the one role that may: create, open, close, delete.
    /// </summary>
    public static TheoryData<string, string, string> Endpoints() => new()
    {
        { "GET", "datasets/d/", "read" },
        { "GET", "datasets/d/sparql?query=ASK%7B%7D", "read" },
        { "POST", "datasets/d/sparql", "write" },
        { "GET", "datasets/d/graphs?default", "read" },
        { "PUT", "datasets/d/graphs?default", "write" },
        { "GET", "datasets/d/feed?to=0", "read" },
        { "GET", "datasets/d/diff?from=0&to=0", "read" },
        { "GET", "datasets/d/status", "admin" },
        { "POST", "datasets/d/settings", "admin" },
        { "POST", "datasets/d/checkpoints", "admin" },
        { "GET", "datasets", "read" },
        { "PUT", "datasets/x", "server-admin" },
        { "POST", "datasets/x/open", "server-admin" },
        { "POST", "datasets/x/close", "server-admin" },
        { "DELETE", "datasets/x", "server-admin" },
    };

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = _issuer.Issuer,
            ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
            ["Varve:Auth:RequireHttpsMetadata"] = "false",
            ["Varve:Auth:Datasets:d:Read:0"] = "reader",
            ["Varve:Auth:Datasets:d:Write:0"] = "writer",
            ["Varve:Auth:Datasets:d:Admin:0"] = "administrator",
            ["Varve:Auth:Datasets:e:Admin:0"] = "reader",
            ["Varve:Auth:Server:Admin:0"] = "operator",
            ["Varve:Auth:Datasets:d:Grants:0:Claim"] = "people",
            ["Varve:Auth:Datasets:d:Grants:0:Permission"] = "write",
            ["Varve:Auth:Datasets:d:Grants:0:Graphs:0"] = "default",
            ["Varve:Auth:Datasets:d:Grants:0:Graphs:1"] = "http://ex/g/people",
            ["Varve:Auth:Datasets:d:Grants:1:Claim"] = "public",
            ["Varve:Auth:Datasets:d:Grants:1:Permission"] = "read",
            ["Varve:Auth:Datasets:d:Grants:1:GraphPrefixes:0"] = "http://ex/g/public/",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Datasets:e:Storage"] = "Memory",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task without_a_token_every_endpoint_is_401_and_says_nothing(string method, string path, string permission)
    {
        _ = permission;
        HttpResponseMessage response = await SendAsync(method, path, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(response.Headers.WwwAuthenticate.Single().Parameter);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(TokenFlaw.Expired)]
    [InlineData(TokenFlaw.NotYetValid)]
    [InlineData(TokenFlaw.WrongAudience)]
    [InlineData(TokenFlaw.WrongIssuer)]
    [InlineData(TokenFlaw.BadSignature)]
    [InlineData(TokenFlaw.UnknownKey)]
    [InlineData(TokenFlaw.Unsigned)]
    public async Task a_token_that_does_not_validate_is_401_with_no_reason_given(TokenFlaw flaw)
    {
        foreach ((string method, string path, string _) in Endpoints().Select(r => r.Data))
        {
            string token = _issuer.Mint(Claims("administrator"), flaw);
            HttpResponseMessage response = await SendAsync(method, path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            AuthenticationHeaderValue challenge = response.Headers.WwwAuthenticate.Single();
            Assert.Equal("Bearer", challenge.Scheme);
            Assert.Null(challenge.Parameter);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData("reader", "read")]
    [InlineData("writer", "write")]
    [InlineData("administrator", "admin")]
    [InlineData("operator", "server-admin")]
    public async Task each_role_reaches_exactly_its_permission_and_those_below_it(string role, string granted)
    {
        string[] order = ["read", "write", "admin", "server-admin"];
        string token = _issuer.Mint(Claims(role));

        foreach ((string method, string path, string needs) in Endpoints().Select(r => r.Data))
        {
            HttpResponseMessage response = await SendAsync(method, path, token);
            bool allowed = Array.IndexOf(order, needs) <= Array.IndexOf(order, granted);
            Assert.True(
                allowed ? (int)response.StatusCode < 400 : response.StatusCode == HttpStatusCode.Forbidden,
                role + " on " + method + " " + path + ": " + response.StatusCode + " " + await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task a_permission_on_one_dataset_is_not_a_permission_on_another()
    {
        // "reader" administers e and only reads d; an unknown dataset grants nothing.
        string token = _issuer.Mint(Claims("reader"));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "datasets/e/status", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "datasets/d/status", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "datasets/nowhere/sparql?query=ASK%7B%7D", token)).StatusCode);

        // The list shows what the caller administers: e for "reader", both for a server admin (ADR 0106).
        string listed = await (await SendAsync("GET", "datasets", token)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"name\":\"e\"", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"d\"", listed, StringComparison.Ordinal);
        string all = await (await SendAsync("GET", "datasets", _issuer.Mint(Claims("operator")))).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"name\":\"d\"", all, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"e\"", all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_role_claim_that_is_an_object_grants_by_its_property_names()
    {
        // Zitadel's project roles: {"writer": {"<org id>": "<org domain>"}}.
        System.Text.Json.Nodes.JsonObject roles = new() { ["writer"] = new System.Text.Json.Nodes.JsonObject { ["1234"] = "org.example" } };
        string token = _issuer.Mint(new Dictionary<string, object> { ["sub"] = "subject-1", ["roles"] = roles });
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync("POST", "datasets/d/sparql", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "datasets/d/status", token)).StatusCode);
    }

    [Fact]
    public async Task a_commit_names_the_issuer_and_the_percent_encoded_subject_as_its_agent()
    {
        string token = _issuer.Mint(Claims("writer", subject: "alice@example.org/ü"));
        HttpResponseMessage response = await SendAsync("POST", "datasets/d/sparql", token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        HttpResponseMessage feed = await SendAsync("GET", "datasets/d/feed?to=1", _issuer.Mint(Claims("reader")));
        string body = await feed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("agent <" + _issuer.Issuer + "#alice%40example.org%2F%C3%BC>", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A graph-scoped user (ADR 0107): "people" writes the default graph and
    /// one named graph; "public" reads by prefix. Each passes the dataset's
    /// policy for its permission and is then bounded by its scope: the
    /// query shows its graphs, the Graph Store answers 404 for a graph it
    /// cannot read and 403 for one it cannot write, an update outside the
    /// scope commits nothing, and the feed is filtered.
    /// </summary>
    [Fact]
    public async Task a_graph_scoped_user_sees_and_changes_its_graphs_alone()
    {
        string admin = _issuer.Mint(Claims("administrator"));
        string people = _issuer.Mint(Claims("people"));
        string @public = _issuer.Mint(Claims("public"));
        await SendUpdateAsync(admin, "INSERT DATA { GRAPH <http://ex/g/secret> { <http://ex/s> <http://ex/p> \"secret\" } . GRAPH <http://ex/g/public/1> { <http://ex/u> <http://ex/p> \"public\" } . GRAPH <http://ex/g/people> { <http://ex/a> <http://ex/p> \"people\" } }");

        // The query: each caller's graphs, and no error for an unreadable FROM.
        string graphs = await BodyAsync("GET", "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?g WHERE { GRAPH ?g {} }"), people);
        Assert.Contains("http://ex/g/people", graphs, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", graphs, StringComparison.Ordinal);
        Assert.DoesNotContain("public", graphs, StringComparison.Ordinal);
        string prefixed = await BodyAsync("GET", "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?g WHERE { GRAPH ?g {} }"), @public);
        Assert.Contains("http://ex/g/public/1", prefixed, StringComparison.Ordinal);
        Assert.DoesNotContain("people", prefixed, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", prefixed, StringComparison.Ordinal);

        // The Graph Store: 404 unreadable, 403 unwritable, 204 within the scope.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("GET", "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g/secret"), people)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("PUT", "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g/secret"), people)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("PUT", "datasets/d/graphs?default", @public)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("PUT", "datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g/public/1"), @public)).StatusCode);
        Assert.True((await SendAsync("PUT", "datasets/d/graphs?default", people)).StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent);

        // An update: one quad outside the writable scope fails the request.
        HttpResponseMessage refused = await SendUpdateAsync(people, "INSERT DATA { <http://ex/x> <http://ex/p> 1 . GRAPH <http://ex/g/secret> { <http://ex/y> <http://ex/p> 1 } }");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("graph-not-writable", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendUpdateAsync(@public, "INSERT DATA { GRAPH <http://ex/g/public/1> { <http://ex/x> <http://ex/p> 1 } }")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendUpdateAsync(people, "INSERT DATA { GRAPH <http://ex/g/people> { <http://ex/b> <http://ex/p> 2 } }")).StatusCode);

        // CLEAR ALL clears what the caller reads, and the secret graph is
        // not that: it survives, unseen (ADR 0107).
        Assert.Equal(HttpStatusCode.NoContent, (await SendUpdateAsync(people, "CLEAR ALL")).StatusCode);
        Assert.Contains("secret", await BodyAsync("GET", "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?o WHERE { GRAPH ?g { ?s ?p ?o } }"), admin), StringComparison.Ordinal);
        Assert.DoesNotContain("people", await BodyAsync("GET", "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?o WHERE { GRAPH ?g { ?s ?p ?o } }"), admin), StringComparison.Ordinal);

        // The feed: the caller's graphs alone, and the secret never named.
        // Four commits so far: the fill, the PUT, the insert, the CLEAR.
        string feed = await BodyAsync("GET", "datasets/d/feed?from=0&to=4", people);
        Assert.Contains("http://ex/g/people", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("http://ex/g/public", feed, StringComparison.Ordinal);

        // Admin and status stay dataset-wide: a scoped user holds neither.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "datasets/d/status", people)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "datasets/d/status", admin)).StatusCode);
    }

    private async Task<HttpResponseMessage> SendUpdateAsync(string token, string update)
    {
        HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/d/sparql", UriKind.Relative))
        {
            Content = new StringContent(update, Encoding.UTF8, "application/sparql-update"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _server.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<string> BodyAsync(string method, string path, string token)
    {
        HttpResponseMessage response = await SendAsync(method, path, token);
        Assert.True(response.StatusCode < HttpStatusCode.BadRequest, method + " " + path + ": " + response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static Dictionary<string, object> Claims(string role, string subject = "subject-1") =>
        new() { ["sub"] = subject, ["roles"] = new[] { role } };

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        HttpRequestMessage request = new(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (path.EndsWith("/settings", StringComparison.Ordinal))
        {
            request.Content = new StringContent("{\"defaultAccessScope\":\"AllHistory\"}", Encoding.UTF8, "application/json");
        }
        else if (method == "PUT" && path == "datasets/x")
        {
            request.Content = new StringContent("{\"storage\":\"Memory\"}", Encoding.UTF8, "application/json");
        }
        else if (method == "POST" && path.EndsWith("/sparql", StringComparison.Ordinal))
        {
            request.Content = new StringContent("INSERT DATA { <http://ex/a> <http://ex/p> 1 }", Encoding.UTF8, "application/sparql-update");
        }
        else if (method == "PUT")
        {
            request.Content = new StringContent("<http://ex/a> <http://ex/p> <http://ex/o> .\n", Encoding.UTF8, "application/n-triples");
        }

        return await _server.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
