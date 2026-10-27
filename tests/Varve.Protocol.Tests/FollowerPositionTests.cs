// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Every data-plane response of a follower that touched the dataset names
/// the position it answered at (ADRs 0094, 0096, 0164; Replication part 1,
/// #165). That covers a success, a problem, a refused write and a read
/// behind the position it asked for, so a client of any instance can read its
/// own writes with <c>Varve-Min-Position</c>. An answer refused before the
/// dataset was read names none: a query that does not parse, a format it
/// cannot answer, an unknown dataset's <c>404</c>.
/// </summary>
public sealed class FollowerPositionTests
{
    [Fact]
    public async Task every_data_plane_response_of_a_follower_names_its_position()
    {
        MemoryStorage storage = new();

        await using (Dataset writer = await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct))
        {
            await writer.CommitAsync(new CommitRequest().Assert(RdfTerm.Iri("http://ex/a"u8), RdfTerm.Iri("http://ex/p"u8), RdfTerm.Iri("http://ex/o"u8)), P.Ct);
            await writer.CommitAsync(new CommitRequest().Assert(RdfTerm.Iri("http://ex/b"u8), RdfTerm.Iri("http://ex/p"u8), RdfTerm.Iri("http://ex/o"u8), RdfTerm.Iri("http://ex/g"u8)), P.Ct);
        }

        await using Dataset follower = await Dataset.OpenFollowerAsync(new Tailing(storage), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        Assert.True(follower.IsFollower);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("d", follower);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, stopping: P.Ct);

        // What each answers with: the head; the position of the resource it
        // names (a commit, a range's start, a diff's start: ADRs 0118, 0128); or
        // nothing, for an answer refused before the dataset was read (ADR 0096
        // point 4: every response that touched a dataset names a position).
        const string Head = "2", Resource = "the resource's", None = "none";
        List<(string What, HttpRequestMessage Request, string Expected)> requests =
        [
            ("the service description", P.Get("datasets/d/", "text/turtle"), Head),
            ("a query by GET", P.Query("datasets/d/sparql", "ASK { ?s ?p ?o }"), Head),
            ("a query by POST", new HttpRequestMessage(HttpMethod.Post, new Uri("datasets/d/sparql", UriKind.Relative)) { Content = new StringContent("SELECT * { ?s ?p ?o }", Encoding.UTF8, "application/sparql-query") }, Head),
            ("an as-of query", P.Query("datasets/d/sparql", "ASK { ?s ?p ?o }", asOf: "position:1"), "1"),
            ("an as-of query past the head", P.Query("datasets/d/sparql", "ASK { ?s ?p ?o }", asOf: "position:9"), Head),
            ("a graph", P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/g"), "application/n-triples"), Head),
            ("the default graph", P.Get("datasets/d/graphs?default", "application/n-triples"), Head),
            ("a graph that does not exist", P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString("http://ex/none"), "application/n-triples"), Head),
            ("the commits", P.Get("datasets/d/commits"), Resource),
            ("one commit", P.Get("datasets/d/commits/1"), "1"),
            ("a diff", P.Get("datasets/d/diff?from=0&to=2"), Resource),
            ("the events", P.Get("datasets/d/events?from=0"), Resource),
            ("a write, which a follower refuses", P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/c> <http://ex/p> 1 }"), Head),
            ("a query that does not parse", P.Query("datasets/d/sparql", "ASK {"), None),
            ("a query in a format it cannot answer", P.Query("datasets/d/sparql", "ASK {}", accept: "image/png"), None),
        ];

        HttpRequestMessage behind = P.Query("datasets/d/sparql", "ASK {}");
        behind.Headers.TryAddWithoutValidation("Varve-Min-Position", "9");
        requests.Add(("a read behind the position it asks for", behind, Head));

        List<string> wrong = [];

        foreach ((string what, HttpRequestMessage request, string expected) in requests)
        {
            using (request)

            // Headers only: a feed's body may tail live, and the position is a header.
            using (HttpResponseMessage response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, P.Ct))
            {
                string? position = P.Header(response, "Varve-Position");
                bool right = expected switch
                {
                    None => position is null,
                    Resource => position is not null,
                    _ => position == expected,
                };

                if (!right)
                {
                    wrong.Add(what + " (" + (int)response.StatusCode + "): " + (position ?? "none") + ", not " + expected);
                }
            }
        }

        Assert.True(wrong.Count == 0, "These responses name the wrong position:\n" + string.Join("\n", wrong));

        // Before a dataset is resolved there is no position to name.
        using HttpResponseMessage unknown = await host.Client.SendAsync(P.Query("datasets/nope/sparql", "ASK {}"), P.Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Null(P.Header(unknown, "Varve-Position"));
    }

    // The writer's storage, read as a follower reads it; nothing more is appended.
    private sealed class Tailing(MemoryStorage storage) : ITailingStorage
    {
        public ISegmentStore Log => storage.Log;

        public IDerivedStore Derived => storage.Derived;

        public ValueTask<bool> RefreshAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
