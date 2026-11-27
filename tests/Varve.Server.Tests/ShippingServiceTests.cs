// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Xunit;
using Process = Varve.Server.Tests.FleetCases.Server;

namespace Varve.Server.Tests;

/// <summary>
/// Log shipping and promotion between two real object stores of different
/// providers (ADRs 0184 and 0185), by server processes over HTTP: the
/// <c>shipping (services)</c> job. One fleet writes a dataset on one store
/// and ships it to the other; a second fleet, over the other store, serves
/// the copy, and an operator promotes it there with <c>varve promote</c>.
/// What the in-process tests cannot show is here: a commit written under the
/// source fleet renders the same events from the copy, read through the
/// source fleet's system dataset as the shipper put it on the target store.
/// </summary>
public sealed class ShippingServiceTests : IAsyncDisposable
{
    private const string Ns = "https://w3id.org/varve/ns#";
    private const string Catalogue = Ns + "events-catalogue/default";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _caches = Directory.CreateTempSubdirectory("varve-shipping-services-").FullName;
    private readonly List<Process> _servers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (Process server in _servers)
        {
            await server.DisposeAsync();
        }

        Directory.Delete(_caches, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dataset_is_shipped_to_a_store_of_another_provider_served_there_as_a_copy_and_promoted(bool fromRustFs)
    {
        FleetStore.Azurite.RequireLeg();
        FleetStore.RustFs.RequireLeg();
        FleetLocation here = await (fromRustFs ? FleetStore.RustFs : FleetStore.Azurite).NewAsync();
        FleetLocation there = await (fromRustFs ? FleetStore.Azurite : FleetStore.RustFs).NewAsync();

        // The gate (ADR 0184 decision 8): a target store holds a verification record before anything is shipped to it.
        using (StringWriter output = new())
        using (StringWriter error = new())
        {
            Assert.True(await Cli.RunAsync([.. there.Verify(), "--rounds", "1"], new Io(output, error, new MemoryStream()), Ct) == 0, "varve storage verify: " + output + error);
        }

        // The source fleet: one instance, which writes d and ships it to the other store.
        Process a = await StartAsync("a", new Reaching(here, there));
        HttpResponseMessage declared = await a.Http.PutAsync(new Uri(a.Address, "manage/datasets/d"), Trig(
            "<urn:ship:d> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"d\" ; <" + Ns + "storage> <" + Ns + "ObjectStorage> ; <" + Ns + "placedOn> \"a\" ; <" + Ns + "shipsTo> <urn:ship:copy> . "
            + "<urn:ship:copy> a <" + Ns + "ShippingTarget> ; <" + Ns + "store> \"" + there.Address + "\" ; <" + Ns + "prefix> \"d\" ; <" + Ns + "provider> \"" + there.Provider + "\" ."), Ct);
        Assert.True(declared.IsSuccessStatusCode, (int)declared.StatusCode + " " + await declared.Content.ReadAsStringAsync(Ct) + "\n" + a.Log);
        await WaitAsync(a, "a to write d", dataset => dataset.TryGetProperty("role", out JsonElement role) && role.GetString() == "writer" && dataset.GetProperty("state").GetString() == "ready");

        // An event type in the dataset's catalogue, one event, the move of the declarations to the management
        // plane (ADR 0167), and one event after it. The second renders from the source fleet's system dataset.
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "INSERT DATA { GRAPH <" + Catalogue + "> { "
            + "<http://ex/types/Paid> a <" + Ns + "EventType> ; <" + Ns + "eventName> \"Paid\" ; "
            + "<" + Ns + "eventClass> <http://ex/Paid> ; <" + Ns + "eventVersion> 1 ; "
            + "<" + Ns + "eventField> <http://ex/types/Paid/total> ; "
            + "<" + Ns + "eventSelect> \"SELECT ?total WHERE { ?event <http://ex/total> ?total }\" . "
            + "<http://ex/types/Paid/total> a <" + Ns + "EventField> ; <" + Ns + "fieldName> \"total\" ; "
            + "<" + Ns + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#integer> ; <" + Ns + "fieldRequired> true } }")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "INSERT DATA { <http://ex/e1> a <http://ex/Paid> ; <http://ex/total> 5 }")).StatusCode);
        HttpResponseMessage moved = await a.Http.PostAsync(new Uri(a.Address, "manage/datasets/d/events/move"), null, Ct);
        Assert.True(moved.StatusCode == HttpStatusCode.OK, await moved.Content.ReadAsStringAsync(Ct) + "\n" + a.Log);
        HttpResponseMessage last = await Update(a, "INSERT DATA { <http://ex/e2> a <http://ex/Paid> ; <http://ex/total> 6 }");
        Assert.Equal(HttpStatusCode.NoContent, last.StatusCode);
        long head = long.Parse(last.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture);
        string events = await EventsAsync(a, head);
        Assert.Contains("\"total\":5", events, StringComparison.Ordinal);
        Assert.Contains("\"total\":6", events, StringComparison.Ordinal);

        // The shipper reaches the head, and readiness says so under the target's store and prefix.
        await WaitAsync(a, "the shipper to reach position " + head, dataset =>
            dataset.TryGetProperty("shipping", out JsonElement shipping)
            && shipping.EnumerateObject().Single() is { } target
            && target.Value.GetProperty("state").GetString() == "shipping"
            && target.Value.TryGetProperty("shippedThrough", out JsonElement through) && through.GetInt64() == head);

        // The target fleet: one instance over the other store, which declares d a copy of its source.
        Process t = await StartAsync("t", there);
        HttpResponseMessage copied = await t.Http.PutAsync(new Uri(t.Address, "manage/datasets/d"), Trig(
            "<urn:copy:d> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"d\" ; <" + Ns + "storage> <" + Ns + "ObjectStorage> ; <" + Ns + "placedOn> \"t\" ; <" + Ns + "copyOf> <urn:copy:source> . "
            + "<urn:copy:source> a <" + Ns + "ShippingSource> ; <" + Ns + "store> \"" + here.Address + "\" ; <" + Ns + "prefix> \"d\" ."), Ct);
        Assert.True(copied.IsSuccessStatusCode, (int)copied.StatusCode + " " + await copied.Content.ReadAsStringAsync(Ct) + "\n" + t.Log);
        await WaitAsync(t, "t to follow the copy to position " + head, dataset => dataset.TryGetProperty("position", out JsonElement position) && position.GetInt64() == head);

        // The copy answers reads, and renders the events of commits another fleet wrote, byte for byte: the one
        // from the dataset's own catalogue and the one from the source fleet's system dataset (decision 8).
        Assert.Equal(events, await EventsAsync(t, head));

        // A replica is not written (decision 3).
        HttpResponseMessage refused = await Update(t, "INSERT DATA { <http://ex/x> <http://ex/p> 1 }");
        string refusal = await refused.Content.ReadAsStringAsync(Ct);
        Assert.True(refused.StatusCode == HttpStatusCode.Conflict && refusal.Contains("dataset-is-replica", StringComparison.Ordinal), (int)refused.StatusCode + " " + refusal);

        // The operator promotes it, naming the source and the position (decision 4).
        using (StringWriter output = new())
        using (StringWriter error = new())
        {
            Assert.True(await Promote.AskAsync(t.Http, null, t.Address.ToString().TrimEnd('/'), "d", here.Address, head, new Io(output, error, new MemoryStream()), Ct), "varve promote: " + output + error + "\n" + t.Log);
            Assert.Contains("d: promoted at epoch ", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("The promotion finished at position " + head.ToString(CultureInfo.InvariantCulture) + ".", output.ToString(), StringComparison.Ordinal);
        }

        // It is written from there on, and the commits below the promotion still render as the source wrote them.
        HttpResponseMessage written = await Update(t, "INSERT DATA { <http://ex/x> <http://ex/p> 1 }");
        Assert.True(written.StatusCode == HttpStatusCode.NoContent, (int)written.StatusCode + " " + await written.Content.ReadAsStringAsync(Ct) + "\n" + t.Log);
        Assert.Equal(head + 1, long.Parse(written.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture));
        Assert.StartsWith(events.TrimEnd(']'), await EventsAsync(t, head + 1), StringComparison.Ordinal);

        // The old source still accepts writes, and its shipper stops: the copy was promoted (decision 7).
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "INSERT DATA { <http://ex/y> <http://ex/p> 1 }")).StatusCode);
        await WaitAsync(a, "the source's shipper to stop as replica-promoted", dataset =>
            dataset.TryGetProperty("shipping", out JsonElement shipping) && shipping.EnumerateObject().Single().Value.GetProperty("state").GetString() == "replica-promoted");
    }

    private async Task<Process> StartAsync(string instance, FleetLocation location)
    {
        Process server = Process.Start(instance, FleetCases.FreePort(), location, Path.Combine(_caches, instance), instance, ["--Varve:Replication:ShipInterval=00:00:00.200"]);
        _servers.Add(server);
        await server.WaitLiveAsync();
        return server;
    }

    private static StringContent Trig(string trig) => new(trig, Encoding.UTF8, "application/trig");

    private static Task<HttpResponseMessage> Update(Process server, string update) =>
        server.Http.PostAsync(new Uri(server.Address, "datasets/d/sparql"), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

    private static async Task<string> EventsAsync(Process server, long atLeast)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(server.Address, "datasets/d/events?from=0"));
        request.Headers.TryAddWithoutValidation("Accept", "application/cloudevents-batch+json");
        request.Headers.TryAddWithoutValidation("Varve-Min-Position", atLeast.ToString(CultureInfo.InvariantCulture));
        HttpResponseMessage response = await server.Http.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, server.Instance + ": " + (int)response.StatusCode + " " + body + "\n" + server.Log);
        return body;
    }

    // Until readiness shows the dataset d as wanted. Within the health endpoints' 60 probes a minute (ADR 0113).
    private static async Task WaitAsync(Process server, string what, Func<JsonElement, bool> reached)
    {
        string last = string.Empty;

        for (int attempt = 0; attempt < 55; attempt++)
        {
            HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);
            last = (int)ready.StatusCode + " " + await ready.Content.ReadAsStringAsync(Ct);

            if (ready.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true)
            {
                using JsonDocument body = JsonDocument.Parse(last[(last.IndexOf(' ', StringComparison.Ordinal) + 1)..]);

                if (body.RootElement.TryGetProperty("datasets", out JsonElement datasets) && datasets.TryGetProperty("d", out JsonElement dataset) && reached(dataset))
                {
                    return;
                }
            }

            await Task.Delay(1100, Ct);
        }

        Assert.Fail(server.Instance + " did not get as far as " + what + " in a minute. Readiness: " + last + "\n" + server.Log);
    }

    /// <summary>A fleet's own store, with what its instance needs to reach the store it ships to.</summary>
    private sealed class Reaching(FleetLocation own, FleetLocation target) : FleetLocation
    {
        public override Uri Upstream => own.Upstream;

        public override IEnumerable<string> ServerArguments => [.. own.ServerArguments, .. target.ReplicationArguments];

        public override IReadOnlyDictionary<string, string> Environment =>
            new Dictionary<string, string>([.. own.Environment, .. target.Environment.Where(pair => !own.Environment.ContainsKey(pair.Key))]);

        public override FleetLocation Through(Uri proxy) => new Reaching(own.Through(proxy), target);

        public override string[] Lease(string dataset) => own.Lease(dataset);

        public override string[] Verify() => own.Verify();

        public override string Address => own.Address;

        public override string Provider => own.Provider;

        public override IEnumerable<string> ReplicationArguments => own.ReplicationArguments;
    }
}
