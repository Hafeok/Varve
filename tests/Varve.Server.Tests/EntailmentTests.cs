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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// A TBox binding through the real host (ADRs 0190, 0192, 0194): declared by
/// the management plane; its TBox read by every reader of the dataset and
/// written only under a write grant; a query answered under the dataset's
/// default against it, through the caller's scope, so that a conclusion
/// drawn from a graph the caller does not read is never shown.
/// </summary>
public sealed class EntailmentTests : IAsyncLifetime
{
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    private static readonly string[] Operator = ["operator"];
    private static readonly string[] Reader = ["reader"];

    private TestIssuer _issuer = null!;
    private RunningServer _server = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _issuer = await TestIssuer.StartAsync();
        _server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = _issuer.Issuer,
            ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
            ["Varve:Auth:RequireHttpsMetadata"] = "false",
            ["Varve:Manage:BootstrapOperators:0"] = "operator",
        });

        const string Trig = """
            @prefix varve: <https://w3id.org/varve/ns#> .
            <urn:zoo> a varve:Dataset ; varve:datasetName "zoo" ; varve:storage varve:Memory .
            <urn:reader> a varve:Role ; varve:claimValue "reader" .
            <urn:reads> a varve:Grant ; varve:grantee <urn:reader> ; varve:permission varve:read ; varve:graph <http://ex/data/1> .
            <urn:tbox> a varve:TBoxBinding ; varve:boundGraph <http://ex/tbox> ; varve:profile varve:Rewriting ;
              varve:graphPrefix "http://ex/data/" ; varve:defaultEntailment <http://www.w3.org/ns/entailment/RDFS> .
            """;
        using HttpRequestMessage put = new(HttpMethod.Put, new Uri("manage/datasets/zoo", UriKind.Relative)) { Content = new StringContent(Trig, Encoding.UTF8, "application/trig") };
        put.Headers.Authorization = Bearer(_issuer.MintManagement(new Dictionary<string, object> { ["sub"] = "ops", ["roles"] = Operator }));
        using HttpResponseMessage declared = await _server.Client.SendAsync(put, Ct);
        Assert.True(declared.IsSuccessStatusCode, (int)declared.StatusCode + " " + await declared.Content.ReadAsStringAsync(Ct));

        using HttpResponseMessage filled = await SendAsync(Update(
            "INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/Dog> <" + Rdfs + "subClassOf> <http://ex/Animal> } "
            + "GRAPH <http://ex/data/1> { <http://ex/rex> a <http://ex/Dog> } GRAPH <http://ex/data/2> { <http://ex/tom> a <http://ex/Dog> } }"), Operator);
        Assert.Equal(HttpStatusCode.NoContent, filled.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
        await _issuer.DisposeAsync();
    }

    [Fact]
    public async Task A_reader_of_one_data_graph_gets_its_entailments_under_the_default_and_none_from_a_graph_it_cannot_read()
    {
        using HttpResponseMessage answered = await SendAsync(Query("SELECT ?x WHERE { GRAPH ?g { ?x a <http://ex/Animal> } }", null), Reader);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal("rdfs", answered.Headers.GetValues("Varve-Entailment").Single());
        Assert.Equal("x\r\nhttp://ex/rex\r\n", await answered.Content.ReadAsStringAsync(Ct));

        // An operator reads every graph, and sees both.
        using HttpResponseMessage all = await SendAsync(Query("SELECT ?x WHERE { GRAPH ?g { ?x a <http://ex/Animal> } } ORDER BY ?x", "rdfs"), Operator);
        Assert.Equal("x\r\nhttp://ex/rex\r\nhttp://ex/tom\r\n", await all.Content.ReadAsStringAsync(Ct));

        using HttpResponseMessage simple = await SendAsync(Query("SELECT ?x WHERE { GRAPH ?g { ?x a <http://ex/Animal> } }", "simple"), Reader);
        Assert.Equal("x\r\n", await simple.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task The_TBox_is_read_by_every_reader_of_the_dataset_and_written_only_under_a_write_grant()
    {
        using HttpResponseMessage read = await SendAsync(Query("SELECT ?c WHERE { GRAPH <http://ex/tbox> { ?c <" + Rdfs + "subClassOf> ?d } }", "simple"), Reader);
        Assert.Equal("c\r\nhttp://ex/Dog\r\n", await read.Content.ReadAsStringAsync(Ct));

        using HttpResponseMessage written = await SendAsync(Update("INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/Cat> <" + Rdfs + "subClassOf> <http://ex/Animal> } }"), Reader);
        Assert.Equal(HttpStatusCode.Forbidden, written.StatusCode);
    }

    [Fact]
    public async Task The_service_description_names_the_datasets_default()
    {
        using HttpRequestMessage get = new(HttpMethod.Get, new Uri("datasets/zoo/sparql", UriKind.Relative));
        get.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/n-triples"));
        using HttpResponseMessage described = await SendAsync(get, Operator);
        string body = await described.Content.ReadAsStringAsync(Ct);
        Assert.Contains("#defaultEntailmentRegime> <http://www.w3.org/ns/entailment/RDFS>", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_data_graph_that_redefines_the_vocabulary_is_named_on_its_binding_and_counted_by_readiness()
    {
        Assert.Empty(await FullReadsAsync());
        Assert.Null(await ReadinessCountAsync());

        using HttpResponseMessage redefined = await SendAsync(Update(
            "INSERT DATA { GRAPH <http://ex/data/2> { <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <" + Rdfs + "subPropertyOf> <http://ex/classifiedAs> } }"), Operator);
        Assert.Equal(HttpStatusCode.NoContent, redefined.StatusCode);

        Assert.Equal(["http://ex/data/2"], await FullReadsAsync());
        Assert.Equal(1, await ReadinessCountAsync());

        // The axiom acts in the graph that holds it, read whole: tom's typing
        // there is a classifiedAs, rex's in data/1 is not.
        using HttpResponseMessage all = await SendAsync(Query("SELECT ?x WHERE { GRAPH ?g { ?x <http://ex/classifiedAs> <http://ex/Animal> } } ORDER BY ?x", "rdfs"), Operator);
        Assert.Equal("x\r\nhttp://ex/tom\r\n", await all.Content.ReadAsStringAsync(Ct));
    }

    // The binding's fullReads, the default graph as null.
    private async Task<List<string?>> FullReadsAsync()
    {
        using HttpRequestMessage get = new(HttpMethod.Get, new Uri("manage/datasets/zoo/bindings/" + Uri.EscapeDataString("urn:tbox"), UriKind.Relative));
        get.Headers.Authorization = Bearer(_issuer.MintManagement(new Dictionary<string, object> { ["sub"] = "ops", ["roles"] = Operator }));
        using HttpResponseMessage described = await _server.Client.SendAsync(get, Ct);
        string body = await described.Content.ReadAsStringAsync(Ct);
        Assert.True(described.StatusCode == HttpStatusCode.OK, body);
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("tbox", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("http://ex/tbox", json.RootElement.GetProperty("tboxGraphs")[0].GetString());
        return [.. json.RootElement.GetProperty("fullReads").EnumerateArray().Select(graph => graph.GetString())];
    }

    // Readiness, unauthenticated, counts the dataset's full reads and names none.
    private async Task<int?> ReadinessCountAsync()
    {
        string body = await _server.Client.GetStringAsync(new Uri("health/ready", UriKind.Relative), Ct);
        Assert.DoesNotContain("http://ex/data/2", body, StringComparison.Ordinal);
        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(body);
        return json.RootElement.GetProperty("datasets").GetProperty("zoo").TryGetProperty("fullReads", out System.Text.Json.JsonElement n) ? n.GetInt32() : null;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string[] roles)
    {
        request.Headers.Authorization = Bearer(_issuer.Mint(new Dictionary<string, object> { ["sub"] = roles[0], ["roles"] = roles }));
        return await _server.Client.SendAsync(request, Ct);
    }

    private static HttpRequestMessage Query(string text, string? regime)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri("datasets/zoo/sparql?query=" + Uri.EscapeDataString(text), UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        if (regime is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-Entailment", regime);
        }

        return request;
    }

    private static HttpRequestMessage Update(string text) =>
        new(HttpMethod.Post, new Uri("datasets/zoo/sparql", UriKind.Relative)) { Content = new StringContent(text, Encoding.UTF8, "application/sparql-update") };

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);
}
