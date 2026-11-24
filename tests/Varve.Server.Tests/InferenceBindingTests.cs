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
/// Materialising bindings through the real host (ADRs 0195, 0197, 0198):
/// declared by the management plane, projected by the server, reported on
/// readiness beside the dataset, and their graphs never written.
/// </summary>
public class InferenceBindingTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Declaration(string name, string profile) =>
        "<urn:varve:from-config:" + name + "> a <" + Varve + "Dataset> ; <" + Varve + "datasetName> \"" + name + "\" ; <" + Varve + "storage> <" + Varve + "Memory> .\n"
        + "<urn:binding:" + name + "> a <" + Varve + "TBoxBinding> ; <" + Varve + "boundGraph> <http://ex/tbox> ; <" + Varve + "profile> <" + Varve + profile + "> ; <"
        + Varve + "graphPrefix> \"http://ex/g/\" .";

    [Theory]
    [InlineData("RDFS")]
    [InlineData("OWL2RL")]
    public async Task A_materialising_binding_is_projected_and_reported_on_readiness(string profile)
    {
        string root = Directory.CreateTempSubdirectory("varve-inference-").FullName;
        try
        {
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:DatasetsRoot"] = root });
            Assert.Equal(HttpStatusCode.Created, (await Trig(server, "manage/datasets/people", Declaration("people", profile))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/Person> <" + Rdfs + "subClassOf> <http://ex/Agent> } }")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { GRAPH <http://ex/g/1> { <http://ex/ann> a <http://ex/Person> } }")).StatusCode);

            // Readiness reports the binding beside the dataset, and the dataset stays ready.
            JsonElement inference = default;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                HttpResponseMessage ready = await server.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
                Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
                using JsonDocument json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Ct));
                inference = json.RootElement.GetProperty("datasets").GetProperty("people").GetProperty("inference").GetProperty("urn:binding:people").Clone();
                if (inference.GetProperty("lag").GetInt64() == 0)
                {
                    break;
                }

                await Task.Delay(100, Ct);
            }

            Assert.Equal("following", inference.GetProperty("state").GetString());
            Assert.Equal(0, inference.GetProperty("lag").GetInt64());
            Assert.False(inference.GetProperty("inconsistent").GetBoolean());

            // Its graphs are computed, never written.
            HttpResponseMessage refused = await Update(server, "people", "INSERT DATA { GRAPH <" + Varve + "inferred/urn%3Abinding%3Apeople/tbox> { <http://ex/a> <http://ex/b> <http://ex/c> } }");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("graph-not-writable", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A cross-graph binding (ADR 0190 point 6) is projected: readiness counts its premise-set graphs, and a conclusion of two graphs is read in its set's graph.</summary>
    [Fact]
    public async Task A_cross_graph_binding_is_projected_and_its_premise_sets_reported_on_readiness()
    {
        string root = Directory.CreateTempSubdirectory("varve-inference-").FullName;
        try
        {
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:DatasetsRoot"] = root });
            string declaration = Declaration("people", "RDFS")[..^1] + " ; <" + Varve + "crossGraph> true .";
            HttpResponseMessage declared = await Trig(server, "manage/datasets/people", declaration);
            Assert.True(declared.StatusCode == HttpStatusCode.Created, await declared.Content.ReadAsStringAsync(Ct));
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { GRAPH <http://ex/g/1> { <http://ex/ann> a <http://ex/Person> } GRAPH <http://ex/g/2> { <http://ex/Person> <" + Rdfs + "subClassOf> <http://ex/Agent> } }")).StatusCode);

            JsonElement inference = default;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                HttpResponseMessage ready = await server.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
                using JsonDocument json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Ct));
                inference = json.RootElement.GetProperty("datasets").GetProperty("people").GetProperty("inference").GetProperty("urn:binding:people").Clone();
                if (inference.GetProperty("lag").GetInt64() == 0)
                {
                    break;
                }

                await Task.Delay(100, Ct);
            }

            Assert.Equal("following", inference.GetProperty("state").GetString());
            Assert.Equal(1, inference.GetProperty("premiseSets").GetInt32());

            string digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("<http://ex/g/1>\n<http://ex/g/2>\n"u8));
            using HttpRequestMessage query = new(HttpMethod.Post, new Uri("datasets/people/sparql", UriKind.Relative))
            {
                Content = new StringContent("SELECT ?c WHERE { GRAPH <" + Varve + "inferred/urn%3Abinding%3Apeople/" + digest + "> { <http://ex/ann> a ?c } }", Encoding.UTF8, "application/sparql-query"),
            };
            query.Headers.Accept.ParseAdd("application/sparql-results+json");
            HttpResponseMessage answered = await server.Client.SendAsync(query, Ct);
            string body = await answered.Content.ReadAsStringAsync(Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
            Assert.Contains("http://ex/Agent", body, StringComparison.Ordinal);
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
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/" + dataset + "/sparql", UriKind.Relative)) { Content = new StringContent(update, Encoding.UTF8, "application/sparql-update") };
        return await server.Client.SendAsync(request, Ct);
    }
}
