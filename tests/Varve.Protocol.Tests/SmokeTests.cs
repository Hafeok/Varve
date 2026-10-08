// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>Worked examples of the protocol end to end, over a real socket, one rule each.</summary>
public class SmokeTests
{
    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static ValueTask<Dataset> NewDatasetAsync(TimeProvider? clock = null) =>
        Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = clock ?? TimeProvider.System }, Ct);

    internal static StringContent Update(string text) => new(text, Encoding.UTF8, "application/sparql-update");

    [Fact]
    public async Task an_update_commits_once_and_a_query_reads_it_with_the_position_as_etag()
    {
        await using Dataset dataset = await NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: Ct);

        HttpResponseMessage update = await host.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), Update("INSERT DATA { <http://ex/s> <http://ex/p> \"o\" }"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("1", Header(update, "Varve-Position"));
        Assert.Equal("\"1\"", update.Headers.ETag?.ToString());
        Assert.Equal(new Position(1), dataset.Head);

        HttpRequestMessage query = new(HttpMethod.Get, "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?o { ?s ?p ?o }"));
        query.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        HttpResponseMessage answer = await host.Client.SendAsync(query, Ct);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("text/csv", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal("o\r\no\r\n", await answer.Content.ReadAsStringAsync(Ct));
        Assert.Equal("\"1\"", answer.Headers.ETag?.ToString());

        HttpRequestMessage again = new(HttpMethod.Get, "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?o { ?s ?p ?o }"));
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"1\""));
        Assert.Equal(HttpStatusCode.NotModified, (await host.Client.SendAsync(again, Ct)).StatusCode);
    }

    [Fact]
    public async Task a_syntax_error_is_a_problem_with_its_position()
    {
        await using Dataset dataset = await NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: Ct);

        HttpResponseMessage response = await host.Client.GetAsync(new Uri("datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT * {\n ?s ?p"), UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/sparql-syntax\"", body, StringComparison.Ordinal);
        Assert.Contains("\"line\":2", body, StringComparison.Ordinal);
    }

    internal static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out System.Collections.Generic.IEnumerable<string>? values) ? string.Join(",", values) : null;
}
