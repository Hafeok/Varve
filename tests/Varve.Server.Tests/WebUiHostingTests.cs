// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The Web UI served by the server (ADR 0202, variant A): under <c>/ui/</c>
/// from <c>Varve:WebUi:Path</c>, with the Content Security Policy on every
/// response, compressed siblings that are never stale, the cache split
/// between fingerprinted and named files, and deep links answered with
/// <c>index.html</c>.
/// </summary>
public sealed class WebUiHostingTests : IDisposable
{
    private const string Index = "<!DOCTYPE html><title>Varve</title>";

    private const string Csp =
        "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self'; font-src 'self'; "
        + "connect-src 'self'; manifest-src 'self'; base-uri 'self'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    private readonly string _bundle = Directory.CreateTempSubdirectory("varve-webui-").FullName;

    public WebUiHostingTests()
    {
        DateTime then = DateTime.UtcNow.AddMinutes(-10);
        Write("index.html", Index, then);
        Write("index.html.br", "brotli of index", then);
        Write("_framework/dotnet.runtime.5nc8i5dl74.js", "runtime", then);
        Write("_framework/dotnet.js", "boot", then);
        Write("appsettings.json", "{}", then);
        Write("appsettings.json.br", "stale brotli", then);
        Write(".secret", "never served", then);

        // The operator edits the settings after unpacking: the compressed copy
        // is now older than the file, and must not be served for it.
        Write("appsettings.json", "{\"Varve\":{}}", DateTime.UtcNow);
    }

    public void Dispose() => Directory.Delete(_bundle, recursive: true);

    [Fact]
    public async Task Index_is_served_under_ui_with_the_policy_and_the_isolation_headers()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());

        using HttpResponseMessage index = await server.Client.GetAsync("ui/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Index, await index.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Csp, Single(index, "Content-Security-Policy"));
        Assert.Equal("nosniff", Single(index, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Single(index, "Referrer-Policy"));
        Assert.Equal("same-origin", Single(index, "Cross-Origin-Opener-Policy"));
        Assert.True(index.Headers.CacheControl?.NoCache);
    }

    [Fact]
    public async Task Without_the_slash_the_ui_redirects_to_it()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());
        using HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = server.Address };

        using HttpResponseMessage bare = await client.GetAsync("ui?x=1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PermanentRedirect, bare.StatusCode);
        Assert.Equal("/ui/?x=1", bare.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_route_of_the_application_gets_index_and_a_missing_file_is_a_problem()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());

        using HttpResponseMessage deep = await server.Client.GetAsync("ui/datasets/notes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
        Assert.Equal(Index, await deep.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        foreach (string missing in (string[])["ui/_framework/gone.wasm", "ui/.secret"])
        {
            using HttpResponseMessage response = await server.Client.GetAsync(missing, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Csp, Single(response, "Content-Security-Policy"));
        }

        // Kestrel removes the dot segments before routing, so a climb out of
        // the bundle is a request outside /ui/, and nobody serves it.
        using HttpResponseMessage climb = await server.Client.GetAsync("ui/%2e%2e/%2e%2e/etc/hostname", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, climb.StatusCode);
    }

    [Fact]
    public async Task Brotli_is_served_when_accepted_and_a_stale_sibling_never()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());

        using HttpResponseMessage index = await server.Client.SendAsync(Get("ui/", "gzip, br"), TestContext.Current.CancellationToken);
        Assert.Equal("br", Assert.Single(index.Content.Headers.ContentEncoding));
        Assert.Equal("brotli of index", await index.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("text/html", index.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Encoding", index.Headers.Vary);

        using HttpResponseMessage refused = await server.Client.SendAsync(Get("ui/", "br;q=0"), TestContext.Current.CancellationToken);
        Assert.Empty(refused.Content.Headers.ContentEncoding);
        Assert.Equal(Index, await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using HttpResponseMessage edited = await server.Client.SendAsync(Get("ui/appsettings.json", "br"), TestContext.Current.CancellationToken);
        Assert.Empty(edited.Content.Headers.ContentEncoding);
        Assert.Equal("{\"Varve\":{}}", await edited.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("application/json", edited.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_fingerprinted_framework_file_is_immutable_and_a_named_one_revalidates()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());

        using HttpResponseMessage runtime = await server.Client.GetAsync("ui/_framework/dotnet.runtime.5nc8i5dl74.js", TestContext.Current.CancellationToken);
        Assert.Equal("public, max-age=31536000, immutable", Single(runtime, "Cache-Control"));
        Assert.Equal("text/javascript", runtime.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage boot = await server.Client.GetAsync("ui/_framework/dotnet.js", TestContext.Current.CancellationToken);
        Assert.Equal("no-cache", Single(boot, "Cache-Control"));

        using HttpRequestMessage again = new(HttpMethod.Get, "ui/_framework/dotnet.js");
        again.Headers.TryAddWithoutValidation("If-None-Match", boot.Headers.ETag!.Tag);
        using HttpResponseMessage unchanged = await server.Client.SendAsync(again, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
    }

    [Fact]
    public async Task Only_get_and_head_are_answered()
    {
        await using RunningServer server = await RunningServer.StartAsync(Settings());

        using HttpResponseMessage head = await server.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "ui/"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(Index.Length, head.Content.Headers.ContentLength);

        using HttpResponseMessage post = await server.Client.PostAsync("ui/", new StringContent("x"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task Unset_there_is_no_ui()
    {
        Dictionary<string, string> settings = Settings();
        settings.Remove("Varve:WebUi:Path");
        await using RunningServer server = await RunningServer.StartAsync(settings);

        using HttpResponseMessage response = await server.Client.GetAsync("ui/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public void With_an_issuer_the_policy_may_connect_to_its_origin_and_no_further()
    {
        string policy = ServerHost.WebUiContentSecurityPolicy("https://login.example/tenant/v2.0");
        Assert.Contains("connect-src 'self' https://login.example;", policy, StringComparison.Ordinal);
        Assert.Equal(Csp, ServerHost.WebUiContentSecurityPolicy(null));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/nowhere/varve-webui")]
    public async Task A_path_that_is_not_a_published_ui_is_refused_at_start(string path)
    {
        Dictionary<string, string> settings = Settings();
        settings["Varve:WebUi:Path"] = path;
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => RunningServer.StartAsync(settings));
        Assert.Contains("exit 2", refused.Message, StringComparison.Ordinal);
    }

    private Dictionary<string, string> Settings() => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:d:Storage"] = "Memory",
        ["Varve:WebUi:Path"] = _bundle,
    };

    private void Write(string relative, string text, DateTime modified)
    {
        string path = Path.Combine(_bundle, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, modified);
    }

    private static HttpRequestMessage Get(string path, string acceptEncoding)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        return request;
    }

    private static string Single(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : throw new InvalidOperationException("No " + name + " header.");
}
