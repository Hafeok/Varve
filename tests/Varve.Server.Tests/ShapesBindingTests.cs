// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Shapes bindings through the real host (ADRs 0169, 0170): declared by the
/// management plane, enforced on the data plane at the write's management
/// position, checked at apply time, and described with their classification.
/// </summary>
public class ShapesBindingTests
{
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string Varve = "https://w3id.org/varve/ns#";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Integers => "INSERT DATA { GRAPH <http://ex/shapes> { <http://ex/S> <" + Sh + "targetSubjectsOf> <http://ex/age> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/age> ; <"
        + Sh + "datatype> <http://www.w3.org/2001/XMLSchema#integer> ] } }";

    private static string FullShapes => "INSERT DATA { GRAPH <http://ex/shapes> { <http://ex/F> <" + Sh + "targetNode> <http://ex/z> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/q> ; <"
        + Sh + "values> <http://ex/k> ; <" + Sh + "node> <http://ex/T> ] . <http://ex/T> <" + Sh + "nodeKind> <" + Sh + "IRI> } }";

    private static string Declaration(string name, string mode, string extra = "") =>
        "<urn:varve:from-config:" + name + "> a <" + Varve + "Dataset> ; <" + Varve + "datasetName> \"" + name + "\" ; <" + Varve + "storage> <" + Varve + "Memory> .\n"
        + "<urn:binding:" + name + "> a <" + Varve + "ShapesBinding> ; <" + Varve + "boundGraph> <http://ex/shapes> ; <" + Varve + "validationMode> <" + Varve + mode + "> ; <"
        + Varve + "graph> <" + Varve + "defaultGraph>" + extra + " .";

    [Fact]
    public async Task A_reject_binding_gates_the_data_plane_and_is_described_with_its_classification()
    {
        string root = Directory.CreateTempSubdirectory("varve-shapes-").FullName;
        try
        {
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:DatasetsRoot"] = root });
            Assert.Equal(HttpStatusCode.Created, (await Trig(server, "manage/datasets/people", Declaration("people", "Reject"))).StatusCode);

            // The shapes are data: written to the dataset's own graph.
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", Integers)).StatusCode);

            // A new violation is refused; a conforming write lands, attached to.
            HttpResponseMessage refused = await Update(server, "people", "INSERT DATA { <http://ex/ann> <http://ex/age> \"old\" }");
            string problem = await refused.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            using (JsonDocument json = JsonDocument.Parse(problem))
            {
                Assert.Equal("https://w3id.org/varve/problems/shapes-violated", json.RootElement.GetProperty("type").GetString());
                JsonElement finding = json.RootElement.GetProperty("findings")[0];
                Assert.Equal("<http://ex/ann>", finding.GetProperty("focusNode").GetString());
                Assert.Equal("<http://ex/age>", finding.GetProperty("path").GetString());
            }

            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { <http://ex/ann> <http://ex/age> 41 }")).StatusCode);

            // Described: its declaration and the classification of its shapes.
            HttpResponseMessage described = await server.Client.GetAsync(new Uri("manage/datasets/people/bindings/" + Uri.EscapeDataString("urn:binding:people"), UriKind.Relative), Ct);
            string body = await described.Content.ReadAsStringAsync(Ct);
            Assert.True(described.StatusCode == HttpStatusCode.OK, body);
            using (JsonDocument json = JsonDocument.Parse(body))
            {
                Assert.Equal("Reject", json.RootElement.GetProperty("validationMode").GetString());
                Assert.Equal("Incremental", json.RootElement.GetProperty("maintainability").GetString());
            }

            Assert.Equal(HttpStatusCode.NotFound, (await server.Client.GetAsync(new Uri("manage/datasets/people/bindings/" + Uri.EscapeDataString("urn:none"), UriKind.Relative), Ct)).StatusCode);

            // A shapes commit that makes them full is refused, about the shapes.
            HttpResponseMessage full = await Update(server, "people", FullShapes);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, full.StatusCode);
            Assert.Contains("binding-not-incremental", await full.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_reject_binding_over_full_shapes_is_refused_at_apply_time_unless_it_allows_full_evaluation()
    {
        string root = Directory.CreateTempSubdirectory("varve-shapes-").FullName;
        try
        {
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:DatasetsRoot"] = root });
            Assert.Equal(HttpStatusCode.Created, (await Trig(server, "manage/datasets/other", Declaration("other", "Off"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "other", FullShapes)).StatusCode);

            HttpResponseMessage refused = await Trig(server, "manage/datasets/other", Declaration("other", "Reject"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Contains("binding-not-incremental", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

            Assert.Equal(HttpStatusCode.NoContent, (await Trig(server, "manage/datasets/other", Declaration("other", "Reject", " ; <" + Varve + "allowFullEvaluation> true"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Trig(server, "manage/datasets/other", Declaration("other", "Report"))).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> Trig(RunningServer server, string path, string trig)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(path, UriKind.Relative)) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };
        return await server.Client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> Update(RunningServer server, string dataset, string update)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/" + dataset + "/sparql", UriKind.Relative))
        {
            Content = new StringContent(update, Encoding.UTF8, "application/sparql-update"),
        };
        return await server.Client.SendAsync(request, Ct);
    }
}
