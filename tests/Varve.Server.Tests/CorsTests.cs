// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// CORS (ADR 0132): a browser-origin request and its preflight, with an
/// origin configured and without; with none, not one <c>Access-Control-*</c>
/// header; never credentials; and a preflight and a problem that need no
/// token to be read by the browser.
/// </summary>
public sealed class CorsTests
{
    private const string App = "https://app.example";

    [Fact]
    public async Task Unconfigured_the_server_sends_no_access_control_header_and_a_preflight_is_an_ordinary_options()
    {
        await using RunningServer server = await RunningServer.StartAsync(Anonymous());

        using HttpResponseMessage read = await server.Client.SendAsync(FromOrigin(HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", App), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Empty(AccessControl(read));

        using HttpResponseMessage preflight = await server.Client.SendAsync(Preflight("datasets/d/sparql", App, "POST", "authorization, if-match"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, preflight.StatusCode);
        Assert.Empty(AccessControl(preflight));
    }

    [Fact]
    public async Task A_configured_origin_may_read_the_position_and_the_tag_and_is_never_given_credentials()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:Cors:AllowedOrigins:0"] = App;
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage read = await server.Client.SendAsync(FromOrigin(HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", App), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(App, Single(read, "Access-Control-Allow-Origin"));
        string[] exposed = Listed(read, "Access-Control-Expose-Headers");

        foreach (string header in (string[])["Varve-Position", "ETag", "Varve-Request-Id", "Link", "Location", "Retry-After", "WWW-Authenticate"])
        {
            Assert.Contains(header, exposed, StringComparer.OrdinalIgnoreCase);
        }

        Assert.False(read.Headers.Contains("Access-Control-Allow-Credentials"));

        using HttpResponseMessage preflight = await server.Client.SendAsync(Preflight("datasets/d/sparql", App, "POST", "authorization, if-match, varve-if-graphs-match, varve-as-of, last-event-id, varve-entailment"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal(App, Single(preflight, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Listed(preflight, "Access-Control-Allow-Methods"));
        string[] allowed = Listed(preflight, "Access-Control-Allow-Headers");

        foreach (string header in (string[])["Authorization", "If-Match", "Varve-If-Graphs-Match", "Varve-As-Of", "Last-Event-ID", "Varve-Entailment"])
        {
            Assert.Contains(header, allowed, StringComparer.OrdinalIgnoreCase);
        }

        Assert.Equal("600", Single(preflight, "Access-Control-Max-Age"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task An_origin_not_configured_is_given_no_access_control_header()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:Cors:AllowedOrigins:0"] = App;
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage read = await server.Client.SendAsync(FromOrigin(HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", "https://elsewhere.example"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Empty(AccessControl(read));
    }

    [Fact]
    public async Task Under_oidc_a_preflight_needs_no_token_and_a_401_is_readable_by_the_origin()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = issuer.Issuer,
            ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
            ["Varve:Auth:RequireHttpsMetadata"] = "false",
            ["Varve:Auth:Datasets:d:Read:0"] = "reader",
            ["Varve:Datasets:d:Storage"] = "Memory",
            ["Varve:Cors:AllowedOrigins:0"] = App,
            ["Varve:Cors:MaxAge"] = "00:02:00",
        });

        using HttpResponseMessage preflight = await server.Client.SendAsync(Preflight("datasets/d/sparql", App, "POST", "authorization"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal("120", Single(preflight, "Access-Control-Max-Age"));

        using HttpResponseMessage refused = await server.Client.SendAsync(FromOrigin(HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", App), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(App, Single(refused, "Access-Control-Allow-Origin"));
        Assert.Contains("WWW-Authenticate", Listed(refused, "Access-Control-Expose-Headers"), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Any_origin_is_star_and_still_never_credentials()
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:Cors:AllowedOrigins:0"] = "*";
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage read = await server.Client.SendAsync(FromOrigin(HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", "https://any.example"), TestContext.Current.CancellationToken);
        Assert.Equal("*", Single(read, "Access-Control-Allow-Origin"));
        Assert.False(read.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("https://app.example/")]
    [InlineData("https://app.example/ui")]
    [InlineData("app.example")]
    [InlineData("ftp://app.example")]
    [InlineData("https://*.example")]
    public async Task A_malformed_origin_refuses_to_start(string origin)
    {
        Dictionary<string, string> settings = Anonymous();
        settings["Varve:Cors:AllowedOrigins:0"] = origin;
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => RunningServer.StartAsync(settings));
        Assert.Contains("exit 2", refused.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Anonymous() => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:d:Storage"] = "Memory",
    };

    private static HttpRequestMessage FromOrigin(HttpMethod method, string path, string origin)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
        return request;
    }

    private static HttpRequestMessage Preflight(string path, string origin, string method, string headers)
    {
        HttpRequestMessage request = new(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static List<string> AccessControl(HttpResponseMessage response) =>
        [.. response.Headers.Concat(response.Content.Headers).Select(header => header.Key).Where(name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))];

    private static string Single(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));

    private static string[] Listed(HttpResponseMessage response, string name) =>
        [.. response.Headers.GetValues(name).SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))];
}
