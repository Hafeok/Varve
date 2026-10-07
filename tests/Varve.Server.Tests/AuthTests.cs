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
/// Authentication and authorisation (ADRs 0037, 0100 layer (a)): every
/// endpoint against every way a token can be wrong and every permission,
/// with tokens from an issuer in the test.
/// </summary>
public sealed class AuthTests : IAsyncLifetime
{
    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    /// <summary>Each endpoint, and the permission it needs (ADR 0093's table).</summary>
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
    public async Task each_role_reaches_exactly_its_permission_and_those_below_it(string role, string granted)
    {
        string[] order = ["read", "write", "admin"];
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

    private static Dictionary<string, object> Claims(string role, string subject = "subject-1") =>
        new() { ["sub"] = subject, ["roles"] = new[] { role } };

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        HttpRequestMessage request = new(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (method == "POST")
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
