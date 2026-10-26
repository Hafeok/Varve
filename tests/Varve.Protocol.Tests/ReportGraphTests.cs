// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Sparql;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// A binding's report as its reserved graph through the protocol (ADR
/// 0174): read by query and by the graph store when the caller reads every
/// graph the binding validates and invisible otherwise (ADR 0107,
/// amended), <c>503 validation-behind</c> when the projection has not
/// reached the position read, and never written.
/// </summary>
public class ReportGraphTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string Data = "http://ex/g/data";
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string Report = "https://w3id.org/varve/ns#validation/urn%3Ab";

    private static readonly ShapesBinding Binding = new(RdfTerm.Iri("urn:b"u8), RdfTerm.Iri("http://ex/shapes"u8), GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded), ValidationMode.Report);

    [Fact]
    public async Task a_caller_who_reads_every_validated_graph_reads_the_report_and_any_other_sees_no_such_graph()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);
        ValidationProjection projection = new(dataset, Binding);
        await dataset.CatchUpAsync(projection, P.Ct);
        Reports reports = new(projection);
        const string Focus = "SELECT ?focus WHERE { GRAPH <" + Report + "> { ?r <" + Sh + "focusNode> ?focus } }";

        // Read on a prefix above the binding's: the report.
        await using (ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"])))
        {
            string csv = await ReadAsync(host, P.Query(Sparql, Focus, "text/csv"));
            Assert.Equal("focus\r\nhttp://ex/ann\r\n", csv);
            string graphs = await ReadAsync(host, P.Query(Sparql, "SELECT DISTINCT ?g WHERE { GRAPH ?g { ?s ?p ?o } } ORDER BY ?g", "text/csv"));
            Assert.Contains(Report, graphs, StringComparison.Ordinal);
        }

        // Read on one data graph only, not every graph the prefix names: no report graph at all.
        await using (ProtocolTestHost host = await StartAsync(dataset, reports, GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Data))], [], DefaultGraphAccess.Excluded)))
        {
            Assert.Equal("focus\r\n", await ReadAsync(host, P.Query(Sparql, Focus, "text/csv")));
            HttpResponseMessage missing = await host.Client.SendAsync(P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), "application/n-triples"), P.Ct);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task a_read_above_the_projection_is_validation_behind_and_a_read_that_never_needs_the_report_is_not()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);

        // Fed nothing: the projection is at position 0, the head at 2.
        Reports reports = new(new ValidationProjection(dataset, Binding));
        await using ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"]));

        HttpResponseMessage behind = await host.Client.SendAsync(P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), "application/n-triples"), P.Ct);
        string body = await behind.Content.ReadAsStringAsync(P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, behind.StatusCode);
        Assert.Equal("1", P.Header(behind, "Retry-After"));
        using (JsonDocument problem = JsonDocument.Parse(body))
        {
            Assert.Equal("https://w3id.org/varve/problems/validation-behind", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("urn:b", problem.RootElement.GetProperty("binding").GetString());
            Assert.Equal(2, problem.RootElement.GetProperty("position").GetInt64());
            Assert.Equal(0, problem.RootElement.GetProperty("projectedPosition").GetInt64());
        }

        HttpResponseMessage queried = await host.Client.SendAsync(P.Query(Sparql, "SELECT * WHERE { GRAPH ?g { ?s ?p ?o } }", "text/csv"), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, queried.StatusCode);

        // The data graphs alone never read the report.
        Assert.Contains("http://ex/ann", await ReadAsync(host, P.Query(Sparql, "SELECT ?s WHERE { GRAPH <" + Data + "> { ?s ?p ?o } }", "text/csv")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_write_to_a_report_graph_is_graph_not_writable_for_an_administrator_too()
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new ReportGraphGuard()] }, P.Ct);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage refused = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + Report + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct)))
        {
            Assert.Equal("https://w3id.org/varve/problems/graph-not-writable", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(Report, problem.RootElement.GetProperty("graph").GetString());
        }

        using HttpRequestMessage put = new(HttpMethod.Put, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), UriKind.Relative))
        {
            Content = new StringContent("<http://ex/a> <http://ex/b> <http://ex/c> .\n", Encoding.UTF8, "application/n-triples"),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(put, P.Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + Data + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct)).StatusCode);
    }

    /// <summary>The shapes, then a data graph with one violation: positions 1 and 2.</summary>
    private static async Task FillAsync(Dataset dataset)
    {
        foreach (string text in (string[])[
            "INSERT DATA { GRAPH <http://ex/shapes> { <http://ex/S> <" + Sh + "targetSubjectsOf> <http://ex/age> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/age> ; <" + Sh + "datatype> <http://www.w3.org/2001/XMLSchema#integer> ] } }",
            "INSERT DATA { GRAPH <" + Data + "> { <http://ex/ann> <http://ex/age> \"old\" } }",
        ])
        {
            await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
        }
    }

    private static GraphScope Readable(string[] prefixes) => GraphScope.Of([], prefixes, DefaultGraphAccess.Included);

    private static Task<ProtocolTestHost> StartAsync(Dataset dataset, Reports reports, GraphScope readable) =>
        ProtocolTestHost.StartAsync(new OneNamed(dataset), accessScopes: new FixedScope(new CallerScope(readable, GraphScope.None, AdminAccess.None)), stopping: P.Ct, validationReports: reports);

    private static async Task<string> ReadAsync(ProtocolTestHost host, HttpRequestMessage request)
    {
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return body;
    }

    private sealed class FixedScope(CallerScope scope) : IAccessScopes
    {
        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => scope;
    }

    private sealed class OneNamed(Dataset dataset) : IDatasetResolver
    {
        public bool TryResolve(DatasetName name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dataset? resolved)
        {
            resolved = name.Value == "d" ? dataset : null;
            return resolved is not null;
        }
    }

    /// <summary>One projection as its report graph, with no wait: a host's adapter, as the server's is.</summary>
    private sealed class Reports(ValidationProjection projection) : IValidationReports, IValidationReportGraph
    {
        public RdfTerm Binding => projection.Binding.Binding;

        public RdfTerm Graph => projection.ReportGraph;

        public GraphScope Validates => projection.Binding.Validates;

        public IReadOnlyList<IValidationReportGraph> Of(DatasetName dataset) => [this];

        public bool TryReportAt(Position position, out ValidationOutcome outcome) => projection.TryReportAt(position, out outcome);

        public ValidationOutcome ReportAt(Position position) =>
            projection.TryReportAt(position, out ValidationOutcome outcome) ? outcome : throw new ValidationBehindException(Binding, position, projection.Position);
    }
}
