// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace Varve.Aspire.Sample.Tests;

/// <summary>
/// The sample AppHost end to end (ADR 0117): the server comes up healthy
/// beside the issuer, a writer's token writes, a reader's reads and may not
/// write. The issuer names itself by the host it was asked for, so the token
/// request carries the host the server sees it by on the container network.
/// </summary>
public class SampleTests
{
    private const string IssuerHost = "mock-oauth2:8080";

    [Fact]
    public async Task The_server_comes_up_behind_the_issuer_and_takes_its_tokens()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Varve_Aspire_Sample>(cancellationToken);
        await using DistributedApplication app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("varve", cancellationToken).WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);

        Uri issuer = app.GetEndpoint("mock-oauth2", "http");
        using HttpClient provider = new(new HttpClientHandler { UseProxy = false });
        string writer = await TokenAsync(provider, issuer, "varve-writer", cancellationToken);
        string reader = await TokenAsync(provider, issuer, "varve-cli", cancellationToken);

        using HttpClient varve = app.CreateHttpClient("varve", "http");
        using HttpResponseMessage unauthenticated = await varve.GetAsync("datasets/people/sparql?query=ASK%7B%7D", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using HttpResponseMessage written = await SendAsync(varve, HttpMethod.Post, "datasets/people/sparql", writer,
            new StringContent("INSERT DATA { <http://ex/s> <http://ex/p> \"from the sample\" }", Encoding.UTF8, "application/sparql-update"), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        using HttpResponseMessage asked = await SendAsync(varve, HttpMethod.Get, "datasets/people/sparql?query=" + Uri.EscapeDataString("ASK { ?s ?p \"from the sample\" }"), reader, null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Contains("\"boolean\":true", await asked.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

        using HttpResponseMessage refused = await SendAsync(varve, HttpMethod.Post, "datasets/people/sparql", reader,
            new StringContent("INSERT DATA { <http://ex/s> <http://ex/p> \"not allowed\" }", Encoding.UTF8, "application/sparql-update"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    private static async Task<string> TokenAsync(HttpClient provider, Uri issuer, string client, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(issuer, "varve/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = client,
                ["client_secret"] = "not-checked-by-the-mock",
                ["scope"] = "api://varve",
            }),
        };
        request.Headers.Host = IssuerHost;
        using HttpResponseMessage response = await provider.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " from the issuer: " + body);
        return (string)JsonNode.Parse(body)!["access_token"]!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient varve, HttpMethod method, string path, string token, HttpContent? content, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await varve.SendAsync(request, cancellationToken);
    }
}
