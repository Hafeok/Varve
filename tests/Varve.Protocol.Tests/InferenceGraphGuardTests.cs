// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// ADR 0198 point 4: the closure's views and reserved graphs are computed,
/// never written. A write to one is <c>graph-not-writable</c> on every path,
/// an update's and the Graph Store's; a write to any other graph is not.
/// </summary>
public class InferenceGraphGuardTests
{
    private const string Sparql = "datasets/d/sparql";

    [Theory]
    [InlineData("https://w3id.org/varve/ns#explicit")]
    [InlineData("https://w3id.org/varve/ns#implicit")]
    [InlineData("https://w3id.org/varve/ns#inferred/urn%3Ab/tbox")]
    [InlineData("https://w3id.org/varve/ns#inferred/urn%3Ab/0123abcd")]
    public async Task A_closure_graph_is_never_written(string graph)
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new InferenceGraphGuard()] }, P.Ct);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage refused = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + graph + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct)))
        {
            Assert.Equal("https://w3id.org/varve/problems/graph-not-writable", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(graph, problem.RootElement.GetProperty("graph").GetString());
        }

        using HttpRequestMessage put = new(HttpMethod.Put, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(graph), UriKind.Relative))
        {
            Content = new StringContent("<http://ex/a> <http://ex/b> <http://ex/c> .\n", Encoding.UTF8, "application/n-triples"),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(put, P.Ct)).StatusCode);
        Assert.Equal(new Position(0), dataset.Head);
    }

    [Theory]
    [InlineData("https://w3id.org/varve/ns#explicits")]
    [InlineData("https://w3id.org/varve/ns#inferredX")]
    [InlineData("http://ex/inferred/tbox")]
    public async Task Any_other_graph_is_written(string graph)
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new InferenceGraphGuard()] }, P.Ct);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage written = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + graph + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
    }
}
