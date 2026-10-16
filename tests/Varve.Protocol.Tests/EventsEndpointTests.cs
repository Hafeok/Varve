// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Varve.Events;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The events endpoints (ADRs 0143–0145, docs/spec/events.md §8): ranges as a
/// batch and as server-sent events with the commits resource's positions,
/// schemas by version and revision, the three views of the catalogue under a
/// caller's grants, the server-wide list, and the validator's refusals.
/// </summary>
public sealed class EventsEndpointTests
{
    internal const int Histories = 20;

    private const string Events = "https://example.org/events";
    private const string Secret = "https://example.org/secret";

    private static string Catalogue(string graph) => "https://w3id.org/varve/ns#events-catalogue/" + Uri.EscapeDataString(graph);

    private static string Declaration(string graph, string name, int version = 1, string totalType = "decimal", bool totalRequired = true) =>
        "INSERT DATA { GRAPH <" + Catalogue(graph) + "> { "
        + "<https://example.org/types/" + name + "> a <https://w3id.org/varve/ns#EventType> ; "
        + "<https://w3id.org/varve/ns#eventName> \"" + name + "\" ; "
        + "<https://w3id.org/varve/ns#eventClass> <https://example.org/ns#" + name + "> ; "
        + "<https://w3id.org/varve/ns#eventVersion> " + version + " ; "
        + "<https://w3id.org/varve/ns#eventField> <https://example.org/types/" + name + "/total> ; "
        + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
        + "<https://example.org/types/" + name + "/total> a <https://w3id.org/varve/ns#EventField> ; "
        + "<https://w3id.org/varve/ns#fieldName> \"total\" ; "
        + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#" + totalType + "> ; "
        + "<https://w3id.org/varve/ns#fieldRequired> " + (totalRequired ? "true" : "false") + " } }";

    private static string Event(string graph, string name, int n, string total) =>
        "INSERT DATA { GRAPH <" + graph + "> { <https://example.org/e/" + name + "/" + n + "> a <https://example.org/ns#" + name + "> ; <https://example.org/ns#total> " + total + " } }";

    private static async Task<(Dataset Dataset, ProtocolTestHost Host, Switchable Scopes)> StartAsync(ProtocolLimits? limits = null, string? eventSource = null)
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()),
            new DatasetOptions { Clock = TimeProvider.System, Validators = [new EventCatalogueValidator()] }, P.Ct);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("d", dataset);
        Switchable scopes = new();
        ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets, accessScopes: scopes, limits: limits, eventSource: eventSource, stopping: P.Ct);
        return (dataset, host, scopes);
    }

    private static async Task<string> SendAsync(ProtocolTestHost host, HttpRequestMessage request, HttpStatusCode expected = HttpStatusCode.OK)
    {
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(expected == response.StatusCode, request.RequestUri + ": " + (int)response.StatusCode + " " + body);
        return body;
    }

    [Fact]
    public async Task a_range_is_a_canonical_cloudevents_batch_with_problems_in_place()
    {
        (Dataset dataset, ProtocolTestHost host, Switchable _) = await StartAsync(eventSource: "https://varve.example.org/");

        await using (dataset)
        await using (host)
        {
            await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Events, "Paid")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/d/sparql", Event(Events, "Paid", 1, "19.99")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/d/sparql", Event(Events, "Paid", 2, "9007199254740993.5")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/d/sparql", "INSERT DATA { <https://example.org/x> <https://example.org/ns#y> 1 }"), HttpStatusCode.NoContent);

            HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/events?from=0", "application/cloudevents-batch+json"), P.Ct);
            string body = await response.Content.ReadAsStringAsync(P.Ct);
            Assert.Equal("application/cloudevents-batch+json", response.Content.Headers.ContentType?.MediaType);
            JsonArray events = JsonNode.Parse(body)!.AsArray();
            Assert.Equal(2, events.Count);

            Assert.Equal(
                "{\"data\":{\"total\":19.99},\"datacontenttype\":\"application/json\","
                + "\"dataschema\":\"https://varve.example.org/datasets/d/events/types/Paid/versions/1/revisions/1\","
                + "\"id\":\"2 https://example.org/e/Paid/1\",\"sequence\":\"00000000000000000002.000000\","
                + "\"source\":\"https://varve.example.org/datasets/d\",\"specversion\":\"1.0\",",
                events[0]!.ToJsonString()[..events[0]!.ToJsonString().IndexOf("\"time\"", StringComparison.Ordinal)]);

            JsonNode problem = events[1]!;
            Assert.Equal(ProblemType.EventNotRendered.Value, problem["type"]!.GetValue<string>());
            Assert.Equal(ProblemCatalogue.Of(ProblemType.EventNotRendered).Title, problem["data"]!["title"]!.GetValue<string>());
            Assert.Equal("number", problem["data"]!["reason"]!.GetValue<string>());

            // The same range twice, the same bytes; one type alone.
            Assert.Equal(body, await SendAsync(host, P.Get("datasets/d/events?from=0", "application/cloudevents-batch+json")));
            Assert.Equal("[]", await SendAsync(host, P.Get("datasets/d/events?from=0&type=Other", "application/cloudevents-batch+json")));
            Assert.Equal(body, await SendAsync(host, P.Get("datasets/d/events?from=0&type=Paid", "application/cloudevents-batch+json")));
        }
    }

    [Fact]
    public async Task a_long_range_is_paged_and_the_pages_are_the_range()
    {
        ProtocolLimits limits = ProtocolLimits.Default with { CommitsPageSize = 2 };
        (Dataset dataset, ProtocolTestHost host, Switchable _) = await StartAsync(limits);

        await using (dataset)
        await using (host)
        {
            await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Events, "Paid")), HttpStatusCode.NoContent);

            for (int i = 1; i <= 5; i++)
            {
                await SendAsync(host, P.Update("datasets/d/sparql", Event(Events, "Paid", i, i + ".5")), HttpStatusCode.NoContent);
            }

            List<string> ids = [];
            string? next = "datasets/d/events?from=0&to=6";

            while (next is not null)
            {
                HttpResponseMessage page = await host.Client.SendAsync(P.Get(next.TrimStart('/'), "application/cloudevents-batch+json"), P.Ct);
                string text = await page.Content.ReadAsStringAsync(P.Ct);
                Assert.True(page.StatusCode == HttpStatusCode.OK, next + ": " + (int)page.StatusCode + " " + text);
                ids.AddRange(JsonNode.Parse(text)!.AsArray().Select(e => e!["id"]!.GetValue<string>()));
                string? link = page.Headers.TryGetValues("Link", out IEnumerable<string>? links)
                    ? links.SelectMany(l => l.Split(',')).FirstOrDefault(l => l.Contains("rel=\"next\"", StringComparison.Ordinal))
                    : null;
                next = link is null ? null : link[(link.IndexOf('<', StringComparison.Ordinal) + 1)..link.IndexOf('>', StringComparison.Ordinal)];
            }

            Assert.Equal(Enumerable.Range(1, 5).Select(i => (i + 1) + " https://example.org/e/Paid/" + i), ids);
        }
    }

    [Fact]
    public async Task an_open_range_streams_one_event_per_commit_and_resumes_by_last_event_id()
    {
        (Dataset dataset, ProtocolTestHost host, Switchable _) = await StartAsync();

        await using (dataset)
        await using (host)
        {
            await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Events, "Paid")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/d/sparql",
                "INSERT DATA { GRAPH <" + Events + "> { <https://example.org/e/a> a <https://example.org/ns#Paid> ; <https://example.org/ns#total> 1 . <https://example.org/e/b> a <https://example.org/ns#Paid> ; <https://example.org/ns#total> 2 } }"),
                HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/d/sparql", Event(Events, "Paid", 3, "3")), HttpStatusCode.NoContent);

            List<(string Id, JsonArray Events)> first = await ReadSseAsync(host, lastEventId: null, count: 2);
            Assert.Equal(["2", "3"], first.Select(e => e.Id));
            Assert.Equal(2, first[0].Events.Count);

            List<(string Id, JsonArray Events)> resumed = await ReadSseAsync(host, lastEventId: "2", count: 1);
            Assert.Equal("3", Assert.Single(resumed).Id);
            Assert.Equal(first[1].Events.ToJsonString(), resumed[0].Events.ToJsonString());
        }
    }

    private static async Task<List<(string Id, JsonArray Events)>> ReadSseAsync(ProtocolTestHost host, string? lastEventId, int count)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(P.Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        HttpRequestMessage request = P.Get("datasets/d/events?from=0", "text/event-stream");

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        using HttpResponseMessage response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync(timeout.Token));
        List<(string, JsonArray)> events = [];
        string? id = null, name = null;
        StringBuilder data = new();

        while (events.Count < count && await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0)
            {
                if (name == "events")
                {
                    events.Add((id!, JsonNode.Parse(data.ToString())!.AsArray()));
                }

                id = name = null;
                data.Clear();
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data.Append(line[6..]);
            }
        }

        return events;
    }

    [Fact]
    public async Task schemas_are_served_by_version_and_immutable_revision()
    {
        (Dataset dataset, ProtocolTestHost host, Switchable _) = await StartAsync(eventSource: "https://varve.example.org/");

        await using (dataset)
        await using (host)
        {
            await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Events, "Paid", totalRequired: true)), HttpStatusCode.NoContent);
            // A breaking change refused at the same version, then made at version 2.
            string refused = await SendAsync(host, P.Update("datasets/d/sparql",
                "DELETE DATA { GRAPH <" + Catalogue(Events) + "> { <https://example.org/types/Paid/total> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> } } ; "
                + "INSERT DATA { GRAPH <" + Catalogue(Events) + "> { <https://example.org/types/Paid/total> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#string> } }"),
                HttpStatusCode.UnprocessableEntity);
            JsonNode incompatible = JsonNode.Parse(refused)!;
            Assert.Equal(ProblemType.EventSchemaIncompatible.Value, incompatible["type"]!.GetValue<string>());
            Assert.Equal("[{\"change\":\"type\",\"field\":\"total\"}]", incompatible["fields"]!.ToJsonString());

            await SendAsync(host, P.Update("datasets/d/sparql",
                "DELETE DATA { GRAPH <" + Catalogue(Events) + "> { <https://example.org/types/Paid/total> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> . <https://example.org/types/Paid> <https://w3id.org/varve/ns#eventVersion> 1 } } ; "
                + "INSERT DATA { GRAPH <" + Catalogue(Events) + "> { <https://example.org/types/Paid/total> <https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#string> . <https://example.org/types/Paid> <https://w3id.org/varve/ns#eventVersion> 2 } }"),
                HttpStatusCode.NoContent);

            const string Base = "https://varve.example.org/datasets/d/events/types/Paid";
            HttpResponseMessage current = await host.Client.SendAsync(P.Get("datasets/d/events/types/Paid"), P.Ct);
            Assert.Equal(Base + "/versions/2/revisions/2", current.Content.Headers.ContentLocation?.OriginalString ?? P.Header(current, "Content-Location"));
            Assert.Equal("application/schema+json", current.Content.Headers.ContentType?.MediaType);

            string v1 = await SendAsync(host, P.Get("datasets/d/events/types/Paid/versions/1/revisions/1"));
            Assert.Contains("\"$id\":\"" + Base + "/versions/1/revisions/1\"", v1, StringComparison.Ordinal);
            Assert.Contains("\"total\":{\"type\":\"number\"}", v1, StringComparison.Ordinal);
            HttpResponseMessage revision = await host.Client.SendAsync(P.Get("datasets/d/events/types/Paid/versions/1/revisions/1"), P.Ct);
            Assert.Contains("immutable", revision.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
            Assert.Equal(v1, await SendAsync(host, P.Get("datasets/d/events/types/Paid/versions/1")));
            await SendAsync(host, P.Get("datasets/d/events/types/Paid/versions/3"), HttpStatusCode.NotFound);
            await SendAsync(host, P.Get("datasets/d/events/types/Nothing"), HttpStatusCode.NotFound);

            // As of position 1, the type was at version 1 alone.
            HttpRequestMessage asOf = P.Get("datasets/d/events/types");
            asOf.Headers.TryAddWithoutValidation("Varve-As-Of", "position:1");
            JsonNode then = JsonNode.Parse(await SendAsync(host, asOf))!;
            Assert.Equal(1, then["types"]![0]!["version"]!.GetValue<int>());
            JsonNode now = JsonNode.Parse(await SendAsync(host, P.Get("datasets/d/events/types")))!;
            Assert.Equal(2, now["types"]![0]!["versions"]!.AsArray().Count);
        }
    }

    [Fact]
    public async Task a_name_taken_in_an_unreadable_graph_is_refused_naming_nothing_of_it()
    {
        (Dataset dataset, ProtocolTestHost host, Switchable scopes) = await StartAsync();

        await using (dataset)
        await using (host)
        {
            await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Secret, "Shared")), HttpStatusCode.NoContent);

            GraphScope events = GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Events))], [], DefaultGraphAccess.Excluded).WithGovernor(EventCatalogue.TryGetSourceGraph);
            scopes.Current = new CallerScope(events, events, AdminAccess.None);
            string body = await SendAsync(host, P.Update("datasets/d/sparql", Declaration(Events, "Shared").Replace("types/Shared", "types/Mine", StringComparison.Ordinal)), HttpStatusCode.UnprocessableEntity);

            JsonNode problem = JsonNode.Parse(body)!;
            Assert.Equal(ProblemType.EventDeclarationInvalid.Value, problem["type"]!.GetValue<string>());
            Assert.Equal("name-taken", problem["reason"]!.GetValue<string>());
            Assert.Equal("https://example.org/types/Mine", problem["declaration"]!.GetValue<string>());
            Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("types/Shared", body, StringComparison.Ordinal);

            // A Graph Store write is checked too.
            HttpRequestMessage put = new(HttpMethod.Put, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(Catalogue(Events)), UriKind.Relative))
            {
                Content = new StringContent("<https://example.org/stray> <https://example.org/p> \"x\" .", Encoding.UTF8, "application/n-triples"),
            };
            Assert.Contains("event-declaration-invalid", await SendAsync(host, put, HttpStatusCode.UnprocessableEntity), StringComparison.Ordinal);
        }
    }

    /// <summary>A generated history over two source graphs and generated callers.</summary>
    private sealed record Step(int Graph, int Type, int Version, bool Emit);

    private static readonly Gen<List<Step>> Steps =
        Gen.Select(Gen.Int[0, 1], Gen.Int[0, 2], Gen.Int[1, 3], Gen.Bool, (g, t, v, e) => new Step(g, t, v, e)).List[1, 8];

    [Fact]
    public void the_list_asyncapi_and_sparql_agree_and_hide_what_the_caller_cannot_read() =>
        Steps.Sample(steps => Agree(steps).GetAwaiter().GetResult(), iter: Histories);

    private static async Task<bool> Agree(List<Step> steps)
    {
        (Dataset dataset, ProtocolTestHost host, Switchable scopes) = await StartAsync();
        string[] graphs = [Events, Secret];

        await using (dataset)
        await using (host)
        {
            Dictionary<string, int> versions = [];

            foreach (Step step in steps)
            {
                // The same short name never lives in two graphs: T{g}{t}.
                string name = "T" + step.Graph + step.Type;

                if (!versions.ContainsKey(name))
                {
                    await SendAsync(host, P.Update("datasets/d/sparql", Declaration(graphs[step.Graph], name)), HttpStatusCode.NoContent);
                    versions[name] = 1;
                }
                else if (step.Emit)
                {
                    await SendAsync(host, P.Update("datasets/d/sparql", Event(graphs[step.Graph], name, step.Version, "1.5")), HttpStatusCode.NoContent);
                }
            }

            GraphScope onlyEvents = GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Events))], [], DefaultGraphAccess.Excluded).WithGovernor(EventCatalogue.TryGetSourceGraph);
            CallerScope[] callers = [CallerScope.Everything, new CallerScope(onlyEvents, onlyEvents, AdminAccess.None), new CallerScope(GraphScope.None, GraphScope.None, AdminAccess.None)];

            for (long p = 0; p <= dataset.Head.Value; p++)
            {
                foreach (CallerScope caller in callers)
                {
                    scopes.Current = caller;
                    SortedSet<string> listed = Names(await GetAsOfAsync(host, "datasets/d/events/types", p), n => n["types"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
                    SortedSet<string> asyncApi = Names(await GetAsOfAsync(host, "datasets/d/events/asyncapi", p), n => n["channels"]!.AsObject().Select(c => c.Key));
                    HttpRequestMessage query = P.Query("datasets/d/sparql",
                        "SELECT ?n WHERE { GRAPH ?g { ?t <https://w3id.org/varve/ns#eventName> ?n } FILTER(STRSTARTS(STR(?g), \"https://w3id.org/varve/ns#events-catalogue/\")) }",
                        asOf: "position:" + p);
                    SortedSet<string> sparql = Names(await SendAsync(host, query), n => n["results"]!["bindings"]!.AsArray().Select(b => b!["n"]!["value"]!.GetValue<string>()));

                    SortedSet<string> readable = new(versions.Keys.Where(k => caller.Readable.Allows(RdfTerm.Iri(Encoding.UTF8.GetBytes(Catalogue(graphs[k[1] - '0']))))), StringComparer.Ordinal);

                    if (!listed.SetEquals(asyncApi) || !listed.SetEquals(sparql) || !listed.IsSubsetOf(readable))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    [Fact]
    public async Task the_server_wide_list_is_the_union_of_the_dataset_lists_under_each_callers_grants()
    {
        Dataset first = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new EventCatalogueValidator()] }, P.Ct);
        Dataset second = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new EventCatalogueValidator()] }, P.Ct);
        ProtocolTestHost.Datasets datasets = new();
        datasets.Add("a", first);
        datasets.Add("b", second);
        Switchable scopes = new();
        Listing administration = new(["a", "b"]);

        await using (first)
        await using (second)
        await using (ProtocolTestHost host = await ProtocolTestHost.StartAsync(datasets,
            map: (app, o) =>
            {
                app.MapGroup("/datasets/{dataset}").MapVarveDataset(o);
                app.MapVarveEventTypes("/events/types", o);
            },
            accessScopes: scopes, administration: administration, stopping: P.Ct))
        {
            await SendAsync(host, P.Update("datasets/a/sparql", Declaration(Events, "A1")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/a/sparql", Declaration(Secret, "A2")), HttpStatusCode.NoContent);
            await SendAsync(host, P.Update("datasets/b/sparql", Declaration(Events, "B1")), HttpStatusCode.NoContent);

            GraphScope onlyEvents = GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Events))], [], DefaultGraphAccess.Excluded).WithGovernor(EventCatalogue.TryGetSourceGraph);

            foreach (CallerScope caller in (CallerScope[])[CallerScope.Everything, new CallerScope(onlyEvents, onlyEvents, AdminAccess.None)])
            {
                scopes.Current = caller;
                JsonArray all = JsonNode.Parse(await SendAsync(host, P.Get("events/types")))!["types"]!.AsArray();
                List<string> union = [];

                foreach (string name in (string[])["a", "b"])
                {
                    JsonArray one = JsonNode.Parse(await SendAsync(host, P.Get("datasets/" + name + "/events/types")))!["types"]!.AsArray();
                    union.AddRange(one.Select(t => name + " " + t!["name"]!.GetValue<string>()));
                }

                Assert.Equal(union, all.Select(t => t!["dataset"]!.GetValue<string>() + " " + t!["name"]!.GetValue<string>()));
            }

            Assert.Equal(2, JsonNode.Parse(await SendAsync(host, P.Get("events/types")))!["types"]!.AsArray().Count);
        }
    }

    private static async Task<string> GetAsOfAsync(ProtocolTestHost host, string path, long position)
    {
        HttpRequestMessage request = P.Get(path);
        request.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + position);
        return await SendAsync(host, request);
    }

    private static SortedSet<string> Names(string json, Func<JsonNode, IEnumerable<string>> select) =>
        new(select(JsonNode.Parse(json)!), StringComparer.Ordinal);

    /// <summary>A caller's scope the test sets before each request; one request at a time.</summary>
    private sealed class Switchable : IAccessScopes
    {
        public CallerScope Current { get; set; } = CallerScope.Everything;

        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => Current;
    }

    /// <summary>A fixed list of open datasets, for the server-wide list.</summary>
    private sealed class Listing(string[] names) : IDatasetAdministration
    {
        public IReadOnlyList<DatasetEntry> List() =>
            [.. names.Select(n => new DatasetEntry(new DatasetName(n), DatasetState.Open, DatasetStorage.Memory, DatasetOrigin.Created, null, null, null))];

        public ValueTask<AdminOutcome> CreateAsync(DatasetName name, DatasetStorage storage, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<AdminOutcome> DeleteAsync(DatasetName name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
