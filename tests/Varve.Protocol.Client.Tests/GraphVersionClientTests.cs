// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Protocol.Client.Model;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// The client exposes a graph's version and the conditional headers (ADR
/// 0126), so the client milestone builds on them: read a graph and its
/// version, write under it, poll with it, and name graphs on an update.
/// </summary>
public class GraphVersionClientTests
{
    [Fact]
    public async Task a_graph_is_read_written_and_polled_by_its_version()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        // Graph g was written at 1, with the rest of the dataset.
        using HttpResponseMessage read = await client.GetGraphAsync("http://ex/g", "text/turtle", C.Ct);
        Assert.Equal(1, SparqlHttpClient.VersionOf(read));

        // Another graph moves the head; g's version stays, and a write under it lands.
        using HttpResponseMessage other = await client.PutGraphAsync("http://ex/h", GraphContent.FromText("<http://ex/x> <http://ex/p> 1 .", "text/turtle"), GraphCondition.IfAbsent, C.Ct);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutGraphAsync("http://ex/h", GraphContent.FromText("<http://ex/x> <http://ex/p> 2 .", "text/turtle"), GraphCondition.IfAbsent, C.Ct)).StatusCode);

        using HttpResponseMessage replaced = await client.PutGraphAsync("http://ex/g", GraphContent.FromText("<http://ex/t> <http://ex/p> 4 .", "text/turtle"), GraphCondition.IfMatch(1), C.Ct);
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        Assert.Equal(3, SparqlHttpClient.VersionOf(replaced));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.PutGraphAsync("http://ex/g", GraphContent.FromText("<http://ex/t> <http://ex/p> 5 .", "text/turtle"), GraphCondition.IfMatch(1), C.Ct)).StatusCode);

        // A poll with the version held is 304; as of 1, g's version is 1.
        Assert.Equal(HttpStatusCode.NotModified, (await client.GetGraphAsync("http://ex/g", "text/turtle", 3, null, C.Ct)).StatusCode);
        Assert.Equal(1, SparqlHttpClient.VersionOf(await client.GetGraphAsync("http://ex/g", "text/turtle", null, "position:1", C.Ct)));

        // An update naming the graphs it read: g at 3 and the default graph at 1.
        using HttpResponseMessage update = await client.UpdateAsync(
            "INSERT { GRAPH <http://ex/g> { <http://ex/u> <http://ex/p> ?o } } WHERE { <http://ex/s> <http://ex/p> ?o }",
            [new KeyValuePair<string?, long>("http://ex/g", 3), new KeyValuePair<string?, long>(null, 1)],
            C.Ct);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.UpdateAsync("INSERT DATA { <http://ex/z> <http://ex/p> 1 }", [new KeyValuePair<string?, long>("http://ex/g", 3)], C.Ct)).StatusCode);

        using HttpResponseMessage deleted = await client.DeleteGraphAsync("http://ex/h", GraphCondition.IfMatch(2), C.Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
