// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// <c>SERVICE</c> and <c>LOAD</c> over HTTP in the server (ADR 0104): the
/// configured policy and limits reach the evaluator and the update executor,
/// and with no section configured both are refused.
/// </summary>
public class OutboundTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_federates_to_an_allowed_endpoint_and_an_update_loads_from_an_allowed_source()
    {
        // The server reaches its own loopback address: the prefix matches any port.
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:a:Storage"] = "Memory",
            ["Varve:Datasets:b:Storage"] = "Memory",
            ["Varve:Federation:AllowedEndpoints:0"] = "http://127.0.0.1:",
            ["Varve:Federation:AllowPrivateAddresses"] = "true",
            ["Varve:Load:AllowedSources:0"] = "http://127.0.0.1:",
            ["Varve:Load:AllowPrivateAddresses"] = "true",
        });

        await UpdateAsync(server, "b", "INSERT DATA { <http://ex/s> <http://ex/p> \"remote\" }", HttpStatusCode.NoContent);

        string federated = await QueryAsync(server, "a", "SELECT ?o WHERE { SERVICE <" + server.Address + "datasets/b/sparql> { <http://ex/s> <http://ex/p> ?o } }");
        Assert.Contains("remote", federated, StringComparison.Ordinal);

        await UpdateAsync(server, "a", "LOAD <" + server.Address + "datasets/b/graphs?default>", HttpStatusCode.NoContent);
        string loaded = await QueryAsync(server, "a", "SELECT ?o WHERE { <http://ex/s> <http://ex/p> ?o }");
        Assert.Contains("remote", loaded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_section_configured_service_and_load_are_refused_and_silent_is_honoured()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:a:Storage"] = "Memory",
        });

        using HttpResponseMessage refused = await server.Client.SendAsync(Query("a", "SELECT ?o WHERE { SERVICE <https://example.org/sparql> { <http://ex/s> <http://ex/p> ?o } }"), Ct);
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);

        string silent = await QueryAsync(server, "a", "SELECT ?o WHERE { SERVICE SILENT <https://example.org/sparql> { <http://ex/s> <http://ex/p> ?o } }");
        Assert.Contains("\"bindings\"", silent, StringComparison.Ordinal);

        await UpdateAsync(server, "a", "LOAD <https://example.org/data.ttl>", HttpStatusCode.BadRequest);
        await UpdateAsync(server, "a", "LOAD SILENT <https://example.org/data.ttl>", HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_endpoint_outside_the_allowed_prefixes_is_refused_even_when_private_addresses_are_allowed()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:Datasets:a:Storage"] = "Memory",
            ["Varve:Federation:AllowedEndpoints:0"] = "https://query.wikidata.org/",
            ["Varve:Federation:AllowPrivateAddresses"] = "true",
        });

        using HttpResponseMessage refused = await server.Client.SendAsync(Query("a", "SELECT ?o WHERE { SERVICE <" + server.Address + "datasets/a/sparql> { <http://ex/s> <http://ex/p> ?o } }"), Ct);
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("not under an allowed prefix", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_that_is_not_an_http_iri_is_a_listed_configuration_error()
    {
        ServerSettings settings = new()
        {
            Datasets = { ["a"] = new DatasetSettings { Storage = "Memory" } },
            Auth = { Mode = "Anonymous" },
            Federation = { AllowedEndpoints = { "wikidata.org" }, Timeout = TimeSpan.Zero },
            Load = { AllowedSources = { "ftp://example.org/" } },
        };

        List<string> errors = SettingsCheck.Errors(settings);
        Assert.Contains(errors, e => e.StartsWith("Varve:Federation:AllowedEndpoints holds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Varve:Federation has a positive", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Varve:Load:AllowedSources holds", StringComparison.Ordinal));
        Assert.Equal(3, errors.Count);
    }

    private static HttpRequestMessage Query(string dataset, string query)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri("datasets/" + dataset + "/sparql?query=" + Uri.EscapeDataString(query), UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
        return request;
    }

    private static async Task<string> QueryAsync(RunningServer server, string dataset, string query)
    {
        using HttpResponseMessage response = await server.Client.SendAsync(Query(dataset, query), Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, response.StatusCode + ": " + body);
        return body;
    }

    private static async Task UpdateAsync(RunningServer server, string dataset, string update, HttpStatusCode expected)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/" + dataset + "/sparql", UriKind.Relative))
        {
            Content = new StringContent(update, Encoding.UTF8, "application/sparql-update"),
        };
        using HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        Assert.True(response.StatusCode == expected, expected + " expected, " + response.StatusCode + ": " + await response.Content.ReadAsStringAsync(Ct));
    }
}
