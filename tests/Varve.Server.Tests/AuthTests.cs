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
/// Authentication and authorisation (ADRs 0037, 0100 layer (a), 0163): every
/// endpoint against every way a token can be wrong and every permission,
/// with tokens from an issuer in the test; the management plane's endpoints
/// under the dataset-admin and operator grants, each with a token for its
/// own plane.
/// </summary>
public sealed class AuthTests : IAsyncLifetime
{
    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    /// <summary>
    /// Each endpoint, and the permission it needs (ADR 0093's table, ADR
    /// 0163's). The list answers any authenticated caller, filtered, so it
    /// needs no more than read; the operator rows run in an order that
    /// succeeds for the one role that may: declare, read the state, close,
    /// delete.
    /// </summary>
    public static TheoryData<string, string, string> Endpoints() => new()
    {
        { "GET", "datasets/d/", "read" },
        { "GET", "datasets/d/sparql?query=ASK%7B%7D", "read" },
        { "POST", "datasets/d/sparql", "write" },
        { "GET", "datasets/d/graphs?default", "read" },
        { "PUT", "datasets/d/graphs?default", "write" },
        { "GET", "datasets/d/commits?to=0", "read" },
        { "GET", "datasets/d/diff?from=0&to=0", "read" },
        { "GET", "manage/datasets/d/status", "admin" },
        { "GET", "manage/datasets/d", "admin" },
        { "GET", "manage/datasets/d/settings", "admin" },
        { "PUT", "manage/datasets/d/settings", "admin" },
        { "PATCH", "manage/datasets/d/settings", "admin" },
        { "GET", "manage/datasets/d/grants", "admin" },
        { "POST", "manage/datasets/d/checkpoints", "admin" },
        { "GET", "manage/datasets", "read" },
        { "PUT", "manage/datasets/x", "server-admin" },
        { "GET", "manage/datasets/x/state", "server-admin" },
        { "PUT", "manage/datasets/x/state", "server-admin" },
        { "DELETE", "manage/datasets/x", "server-admin" },
        { "GET", "manage/fleet", "server-admin" },
        { "GET", "manage/graphs", "server-admin" },
        { "GET", "manage/system", "server-admin" },
        { "GET", "manage/sparql?query=ASK%7B%7D", "server-admin" },
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

        // No token: no error code (RFC 6750 §3.1), and the thin problem (ADR 0119).
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Null(response.Headers.WwwAuthenticate.Single().Parameter);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"type\":\"https://w3id.org/varve/problems/unauthorized\",\"title\":\"The request carries no valid bearer token.\",\"status\":401}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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
            string token = Token(path, Claims("administrator"), flaw);
            HttpResponseMessage response = await SendAsync(method, path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

            // A token was presented and failed: the error code, never a reason (RFC 6750 §3.1; ADR 0119).
            AuthenticationHeaderValue challenge = response.Headers.WwwAuthenticate.Single();
            Assert.Equal("Bearer", challenge.Scheme);
            Assert.Equal("error=\"invalid_token\"", challenge.Parameter);
            Assert.Equal("{\"type\":\"https://w3id.org/varve/problems/unauthorized\",\"title\":\"The request carries no valid bearer token.\",\"status\":401}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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

        foreach ((string method, string path, string needs) in Endpoints().Select(r => r.Data))
        {
            HttpResponseMessage response = await SendAsync(method, path, Token(path, Claims(role)));
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
        string manage = _issuer.MintManagement(Claims("reader"));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "manage/datasets/e/status", manage)).StatusCode);
        HttpResponseMessage forbidden = await SendAsync("GET", "manage/datasets/d/status", manage);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("error=\"insufficient_scope\"", forbidden.Headers.WwwAuthenticate.Single().Parameter);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/forbidden\"", await forbidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "datasets/nowhere/sparql?query=ASK%7B%7D", token)).StatusCode);

        // The list shows what the caller administers: e for "reader", both for an operator (ADR 0163).
        string listed = await (await SendAsync("GET", "manage/datasets", manage)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"name\":\"e\"", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"d\"", listed, StringComparison.Ordinal);
        string all = await (await SendAsync("GET", "manage/datasets", _issuer.MintManagement(Claims("operator")))).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
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
        string manage = _issuer.MintManagement(new Dictionary<string, object> { ["sub"] = "subject-1", ["roles"] = roles });
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "manage/datasets/d/status", manage)).StatusCode);
    }

    [Fact]
    public async Task a_commit_names_the_issuer_and_the_percent_encoded_subject_as_its_agent()
    {
        string token = _issuer.Mint(Claims("writer", subject: "alice@example.org/ü"));
        HttpResponseMessage response = await SendAsync("POST", "datasets/d/sparql", token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        HttpResponseMessage feed = await SendAsync("GET", "datasets/d/commits?to=1", _issuer.Mint(Claims("reader")));
        string body = await feed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("agent <" + _issuer.Issuer + "#alice%40example.org%2F%C3%BC>", body, StringComparison.Ordinal);

        // The one commit, by its own address (ADR 0118), for a reader too.
        HttpResponseMessage one = await SendAsync("GET", "datasets/d/commits/1", _issuer.Mint(Claims("reader")));
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(body, await one.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
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
        string feed = await BodyAsync("GET", "datasets/d/commits?from=0&to=4", people);
        Assert.Contains("http://ex/g/people", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", feed, StringComparison.Ordinal);
        Assert.DoesNotContain("http://ex/g/public", feed, StringComparison.Ordinal);

        // Admin and status stay dataset-wide: a scoped user holds neither.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync("GET", "manage/datasets/d/status", _issuer.MintManagement(Claims("people")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "manage/datasets/d/status", _issuer.MintManagement(Claims("administrator")))).StatusCode);
    }

    /// <summary>
    /// An event catalogue graph is governed by its source graph (ADRs 0144,
    /// 0107 amended): "people" writes the catalogue of the graph it writes,
    /// "public" reads the catalogue of a graph it reads and writes none, and
    /// neither sees a catalogue whose source graph it cannot read.
    /// </summary>
    [Fact]
    public async Task a_catalogue_graph_is_read_and_written_with_its_source_graph()
    {
        string admin = _issuer.Mint(Claims("administrator"));
        string people = _issuer.Mint(Claims("people"));
        string @public = _issuer.Mint(Claims("public"));
        string peopleCatalogue = "https://w3id.org/varve/ns#events-catalogue/http%3A%2F%2Fex%2Fg%2Fpeople";
        string publicCatalogue = "https://w3id.org/varve/ns#events-catalogue/http%3A%2F%2Fex%2Fg%2Fpublic%2F1";
        string secretCatalogue = "https://w3id.org/varve/ns#events-catalogue/http%3A%2F%2Fex%2Fg%2Fsecret";

        Assert.Equal(HttpStatusCode.NoContent, (await SendUpdateAsync(people, Declaration(peopleCatalogue, "Joined"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendUpdateAsync(@public, Declaration(publicCatalogue, "Published"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendUpdateAsync(people, Declaration(secretCatalogue, "Leaked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendUpdateAsync(admin, Declaration(publicCatalogue, "Published"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendUpdateAsync(admin, Declaration(secretCatalogue, "Leaked"))).StatusCode);

        string query = "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?g ?n WHERE { GRAPH ?g { ?t <https://w3id.org/varve/ns#eventName> ?n } }");
        string seenByPeople = await BodyAsync("GET", query, people);
        Assert.Contains("Joined", seenByPeople, StringComparison.Ordinal);
        Assert.DoesNotContain("Published", seenByPeople, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaked", seenByPeople, StringComparison.Ordinal);
        string seenByPublic = await BodyAsync("GET", query, @public);
        Assert.Contains("Published", seenByPublic, StringComparison.Ordinal);
        Assert.DoesNotContain("Joined", seenByPublic, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaked", seenByPublic, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync("GET", "datasets/d/graphs?graph=" + Uri.EscapeDataString(peopleCatalogue), @public)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "datasets/d/graphs?graph=" + Uri.EscapeDataString(publicCatalogue), @public)).StatusCode);

        static string Declaration(string catalogue, string name) =>
            "INSERT DATA { GRAPH <" + catalogue + "> { "
            + "<http://ex/types/" + name + "> a <https://w3id.org/varve/ns#EventType> ; "
            + "<https://w3id.org/varve/ns#eventName> \"" + name + "\" ; "
            + "<https://w3id.org/varve/ns#eventClass> <http://ex/" + name + "> ; "
            + "<https://w3id.org/varve/ns#eventVersion> 1 ; "
            + "<https://w3id.org/varve/ns#eventField> <http://ex/types/" + name + "/at> ; "
            + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?at WHERE { ?event <http://ex/at> ?at }\" . "
            + "<http://ex/types/" + name + "/at> a <https://w3id.org/varve/ns#EventField> ; "
            + "<https://w3id.org/varve/ns#fieldName> \"at\" ; "
            + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#dateTime> ; "
            + "<https://w3id.org/varve/ns#fieldRequired> true } }";
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

    /// <summary>
    /// Each plane accepts only its own audience (ADR 0163 point 1): a data
    /// token is refused by the management plane, a management token by the
    /// data plane, and a token for both by both, whatever its roles.
    /// </summary>
    [Fact]
    public async Task each_plane_refuses_the_other_planes_tokens_and_a_token_for_both()
    {
        string data = _issuer.Mint(Claims("operator"));
        string management = _issuer.MintManagement(Claims("operator"));
        string both = _issuer.Mint(Claims("operator"), audiences: [TestIssuer.Audience, TestIssuer.ManagementAudience]);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "manage/system", management)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("GET", "manage/system", data)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("GET", "manage/system", both)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync("GET", "datasets/d/sparql?query=ASK%7B%7D", data)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("GET", "datasets/d/sparql?query=ASK%7B%7D", management)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync("GET", "datasets/d/sparql?query=ASK%7B%7D", both)).StatusCode);
    }

    // A token for the plane the path is on.
    private string Token(string path, Dictionary<string, object> claims, TokenFlaw flaw = TokenFlaw.None) =>
        path.StartsWith("manage/", StringComparison.Ordinal) ? _issuer.MintManagement(claims, flaw) : _issuer.Mint(claims, flaw);

    private static Dictionary<string, object> Claims(string role, string subject = "subject-1") =>
        new() { ["sub"] = subject, ["roles"] = new[] { role } };

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        HttpRequestMessage request = new(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (path.EndsWith("/settings", StringComparison.Ordinal) && method != "GET")
        {
            request.Content = new StringContent("<urn:varve:from-config:d> <https://w3id.org/varve/ns#defaultAccessScope> <https://w3id.org/varve/ns#AllHistory> .", Encoding.UTF8, "application/trig");
        }
        else if (path.EndsWith("/state", StringComparison.Ordinal) && method == "PUT")
        {
            // Closed, so that the delete that follows in the matrix succeeds.
            request.Content = new StringContent("{\"state\":\"closed\"}", Encoding.UTF8, "application/json");
        }
        else if (method == "PUT" && path == "manage/datasets/x")
        {
            request.Content = new StringContent(
                "<urn:x> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"x\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#Memory> .",
                Encoding.UTF8,
                "application/trig");
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
