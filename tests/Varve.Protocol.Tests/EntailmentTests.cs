// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// <c>Varve-Entailment</c> (ADR 0192): a query answered under the regime asked
/// or the dataset's default, against the TBox of the binding that covers the
/// graph read (ADR 0193); the regime echoed, varied on and in the tag; an
/// unknown regime, and any but simple on an update, refused; the service
/// description's regimes.
/// </summary>
public class EntailmentTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string Ex = "http://ex/";
    private const string Animals = "SELECT ?x WHERE { GRAPH <http://ex/g> { ?x a <http://ex/Animal> } }";

    [Fact]
    public async Task A_query_under_rdfs_is_answered_against_the_TBox_and_under_simple_as_written()
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Simple);

        HttpResponseMessage simple = await host.Client.SendAsync(Query(Animals, null), P.Ct);
        Assert.Equal("x\r\n", await simple.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("simple", P.Header(simple, "Varve-Entailment"));
        Assert.Contains("Varve-Entailment", simple.Headers.Vary);
        Assert.Equal("\"2\"", simple.Headers.ETag!.Tag);

        HttpResponseMessage rdfs = await host.Client.SendAsync(Query(Animals, "rdfs"), P.Ct);
        Assert.Equal("x\r\nhttp://ex/rex\r\n", await rdfs.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("rdfs", P.Header(rdfs, "Varve-Entailment"));
        Assert.Equal("\"2.rdfs\"", rdfs.Headers.ETag!.Tag);

        HttpResponseMessage byIri = await host.Client.SendAsync(Query(Animals, "<http://www.w3.org/ns/entailment/RDFS>"), P.Ct);
        Assert.Equal("x\r\nhttp://ex/rex\r\n", await byIri.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("rdfs", P.Header(byIri, "Varve-Entailment"));

        // A graph no binding covers is read over its own triples (ADR 0192 point 5).
        HttpResponseMessage uncovered = await host.Client.SendAsync(Query("SELECT ?x WHERE { GRAPH <http://ex/other> { ?x a <http://ex/Animal> } }", "rdfs"), P.Ct);
        Assert.Equal("x\r\n", await uncovered.Content.ReadAsStringAsync(P.Ct));
    }

    [Fact]
    public async Task Without_the_header_a_query_is_answered_under_the_datasets_default()
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Rdfs);

        HttpResponseMessage answered = await host.Client.SendAsync(Query(Animals, null), P.Ct);
        Assert.Equal("x\r\nhttp://ex/rex\r\n", await answered.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("rdfs", P.Header(answered, "Varve-Entailment"));

        HttpResponseMessage simple = await host.Client.SendAsync(Query(Animals, "simple"), P.Ct);
        Assert.Equal("x\r\n", await simple.Content.ReadAsStringAsync(P.Ct));
    }

    [Fact]
    public async Task A_regimes_tag_is_its_own()
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Simple);

        HttpRequestMessage same = Query(Animals, "rdfs");
        same.Headers.TryAddWithoutValidation("If-None-Match", "\"2.rdfs\"");
        HttpResponseMessage notModified = await host.Client.SendAsync(same, P.Ct);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal("\"2.rdfs\"", notModified.Headers.ETag!.Tag);

        HttpRequestMessage other = Query(Animals, "rdfs");
        other.Headers.TryAddWithoutValidation("If-None-Match", "\"2\"");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(other, P.Ct)).StatusCode);

        HttpRequestMessage simple = Query(Animals, null);
        simple.Headers.TryAddWithoutValidation("If-None-Match", "\"2.rdfs\"");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(simple, P.Ct)).StatusCode);
    }

    [Theory]
    [InlineData("owl2-rl")]
    [InlineData("owl2-ql")]
    [InlineData("RDFS")]
    [InlineData("rdfs, d")]
    [InlineData("<http://www.w3.org/ns/entailment/OWL-RDF-Based>")]
    public async Task A_regime_the_server_does_not_serve_is_entailment_unsupported(string value)
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Simple);

        HttpResponseMessage refused = await host.Client.SendAsync(Query(Animals, value), P.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("https://w3id.org/varve/problems/entailment-unsupported", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(value, problem.RootElement.GetProperty("value").GetString());
        Assert.Equal("simple rdf rdfs d", string.Join(' ', JsonSerializerElements(problem.RootElement.GetProperty("supported"))));
    }

    [Fact]
    public async Task An_update_is_evaluated_under_simple_entailment_and_refuses_any_other()
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Rdfs);

        HttpRequestMessage rdfs = P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/b> <http://ex/c> }");
        rdfs.Headers.TryAddWithoutValidation("Varve-Entailment", "rdfs");
        HttpResponseMessage refused = await host.Client.SendAsync(rdfs, P.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("entailment-unsupported", await refused.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);

        HttpRequestMessage simple = P.Update(Sparql, "INSERT DATA { <http://ex/a> <http://ex/b> <http://ex/c> }");
        simple.Headers.TryAddWithoutValidation("Varve-Entailment", "simple");
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(simple, P.Ct)).StatusCode);

        // Without the header, the dataset's default is not the update's: the WHERE reads asserted triples.
        HttpResponseMessage copied = await host.Client.SendAsync(P.Update(Sparql, "INSERT { GRAPH <http://ex/copy> { ?x a <http://ex/Animal> } } WHERE { GRAPH <http://ex/g> { ?x a <http://ex/Animal> } }"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, copied.StatusCode);
        HttpResponseMessage read = await host.Client.SendAsync(Query("SELECT ?x WHERE { GRAPH <http://ex/copy> { ?x ?p ?o } }", "simple"), P.Ct);
        Assert.Equal("x\r\n", await read.Content.ReadAsStringAsync(P.Ct));
    }

    [Fact]
    public async Task The_service_description_names_the_default_and_each_covered_graphs_regimes()
    {
        await using Dataset dataset = await FilledAsync();
        await using ProtocolTestHost host = await StartAsync(dataset, EntailmentRegime.Rdfs);

        string description = await (await host.Client.SendAsync(P.Get(Sparql, "application/n-triples"), P.Ct)).Content.ReadAsStringAsync(P.Ct);
        Assert.Contains("<http://www.w3.org/ns/sparql-service-description#defaultEntailmentRegime> <http://www.w3.org/ns/entailment/RDFS>", description, StringComparison.Ordinal);
        Assert.Contains("<http://www.w3.org/ns/sparql-service-description#feature> <https://w3id.org/varve/ns#EntailmentHeader>", description, StringComparison.Ordinal);
        Assert.Equal(4, Count(description, "#entailmentRegime>"));
    }

    private static int Count(string text, string of) => (text.Length - text.Replace(of, string.Empty, StringComparison.Ordinal).Length) / of.Length;

    private static string?[] JsonSerializerElements(JsonElement array)
    {
        string?[] values = new string?[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement value in array.EnumerateArray())
        {
            values[i++] = value.GetString();
        }

        return values;
    }

    private static HttpRequestMessage Query(string text, string? regime)
    {
        HttpRequestMessage request = P.Query(Sparql, text, "text/csv");
        if (regime is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-Entailment", regime);
        }

        return request;
    }

    /// <summary>A TBox, a data graph it covers and one it does not: positions 1 and 2.</summary>
    private static async Task<Dataset> FilledAsync()
    {
        Dataset dataset = await P.NewDatasetAsync();
        foreach (string text in (string[])[
            "INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/Dog> <http://www.w3.org/2000/01/rdf-schema#subClassOf> <http://ex/Animal> } }",
            "INSERT DATA { GRAPH <http://ex/g> { <http://ex/rex> a <http://ex/Dog> } GRAPH <http://ex/other> { <http://ex/tom> a <http://ex/Dog> } }",
        ])
        {
            await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
        }

        return dataset;
    }

    private static Task<ProtocolTestHost> StartAsync(Dataset dataset, EntailmentRegime defaultRegime) =>
        ProtocolTestHost.StartAsync(new OneNamed(dataset), stopping: P.Ct, entailment: new Bindings(new EntailmentBindings(
            [new TBoxCover([RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + "tbox"))], GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + "g"))], [], DefaultGraphAccess.Excluded))],
            defaultRegime)));

    private sealed class Bindings(EntailmentBindings bindings) : IEntailmentBindings
    {
        public EntailmentBindings Of(DatasetName dataset) => bindings;
    }

    private sealed class OneNamed(Dataset dataset) : IDatasetResolver
    {
        public bool TryResolve(DatasetName name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dataset? resolved)
        {
            resolved = name.Value == "d" ? dataset : null;
            return resolved is not null;
        }
    }
}
