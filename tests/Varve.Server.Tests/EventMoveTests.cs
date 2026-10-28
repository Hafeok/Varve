// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The move of a dataset's event declarations to the management plane (ADR
/// 0167) through the real host, a fleet of one on the file backend: the
/// events of every commit render the same bytes across the move; a catalogue
/// write is refused after it, at the store and over HTTP; the catalogue
/// graphs' history reads the same as of any position; a declaration changed
/// on the management plane mints an <c>m{p}</c> revision, and an
/// incompatible one is refused as before; and a move stopped at each step,
/// by a stop of the server, finishes on its own or on the next request, with
/// nothing lost or doubled.
/// </summary>
public sealed class EventMoveTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Catalogue = Varve + "events-catalogue/default";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Declaration(bool note = false) =>
        "INSERT DATA { GRAPH <" + Catalogue + "> { "
        + "<https://example.org/types/Paid> a <" + Varve + "EventType> ; <" + Varve + "eventName> \"Paid\" ; "
        + "<" + Varve + "eventClass> <https://example.org/ns#Paid> ; <" + Varve + "eventVersion> 1 ; "
        + "<" + Varve + "eventField> <https://example.org/types/Paid/total> ; "
        + "<" + Varve + "eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
        + "<https://example.org/types/Paid/total> a <" + Varve + "EventField> ; <" + Varve + "fieldName> \"total\" ; "
        + "<" + Varve + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; <" + Varve + "fieldRequired> true } }";

    private static string Event(int n, string total) =>
        "INSERT DATA { <https://example.org/e/" + n + "> a <https://example.org/ns#Paid> ; <https://example.org/ns#total> " + total + " }";

    [Fact]
    public async Task Events_render_the_same_bytes_across_the_move_and_the_catalogue_is_frozen_after_it()
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;

        try
        {
            await using RunningServer server = await StartAsync(root);
            await DeclareAsync(server);
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Declaration())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Event(1, "19.99"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Event(2, "5"))).StatusCode);
            string before = await EventsAsync(server);
            string catalogueBefore = await CatalogueAsOfAsync(server, 3);
            JsonDocument where = await JsonAsync(await server.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct));
            Assert.Equal("in-dataset", where.RootElement.GetProperty("state").GetString());

            // The move.
            HttpResponseMessage moved = await MoveAsync(server);
            JsonDocument answer = await JsonAsync(moved);
            Assert.Equal("moved", answer.RootElement.GetProperty("state").GetString());
            Assert.Equal(1, answer.RootElement.GetProperty("types").GetInt32());

            // Again: nothing to do, the same answer.
            Assert.Equal(answer.RootElement.GetProperty("position").GetInt64(), (await JsonAsync(await MoveAsync(server))).RootElement.GetProperty("position").GetInt64());

            // Every commit before it renders the same bytes; one after it renders from the system dataset, with the same revision.
            Assert.Equal(before, await EventsAsync(server));
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Event(3, "7"))).StatusCode);
            JsonArray after = JsonNode.Parse(await EventsAsync(server))!.AsArray();
            Assert.Equal(3, after.Count);
            Assert.Equal(before, new JsonArray([.. after.Take(2).Select(e => e!.DeepClone())]).ToJsonString());
            Assert.EndsWith("/types/Paid/versions/1/revisions/1", after[2]!["dataschema"]!.GetValue<string>(), StringComparison.Ordinal);

            // The catalogue is frozen: a write to it is refused, naming the new home.
            HttpResponseMessage refused = await UpdateAsync(server, "INSERT DATA { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"no\" } }");
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            JsonDocument problem = await JsonAsync(refused, allowFailure: true);
            Assert.Equal("https://w3id.org/varve/problems/event-catalogue-moved", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("/manage/datasets/d/events", problem.RootElement.GetProperty("manage").GetString());

            // And at the store, by the validator a write authorised there carries.
            Assert.True(server.Started!.Fleet.Datasets.TryResolve(new DatasetName("d"), out Dataset? dataset));
            CommitResult direct = await dataset.CommitAsync(new CommitRequestBuilder
            {
                Validators = [server.Started.Fleet.OrderOf(new DatasetName("d"), server.Started.Fleet.Applied)],
            }.Assert(RdfTerm.Iri("https://example.org/x"u8), RdfTerm.Iri("https://example.org/y"u8), RdfTerm.Iri("https://example.org/z"u8), RdfTerm.Iri(Encoding.UTF8.GetBytes(Catalogue))), Ct);
            Assert.Equal(CommitOutcome.Rejected, direct.Outcome);
            Assert.Equal("https://w3id.org/varve/problems/event-catalogue-moved", Encoding.UTF8.GetString(direct.Report[0].Lexical));

            // The catalogue's history reads the same as of a position before the move.
            Assert.Equal(catalogueBefore, await CatalogueAsOfAsync(server, 3));

            // The declarations' new home: the state, and the event nodes with their source graph.
            where = await JsonAsync(await server.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct));
            Assert.Equal("moved", where.RootElement.GetProperty("state").GetString());
            using HttpRequestMessage trig = new(HttpMethod.Get, new Uri("manage/datasets/d/events", UriKind.Relative));
            trig.Headers.TryAddWithoutValidation("Accept", "application/trig");
            string nodes = await (await server.Client.SendAsync(trig, Ct)).Content.ReadAsStringAsync(Ct);
            Assert.Contains("sourceGraph", nodes, StringComparison.Ordinal);
            Assert.Contains("defaultGraph", nodes, StringComparison.Ordinal);

            // A compatible change on the management plane mints an m{p} revision, served at its URL.
            HttpResponseMessage current = await server.Client.GetAsync(new Uri("manage/datasets/d", UriKind.Relative), Ct);
            string graph = await current.Content.ReadAsStringAsync(Ct);
            string withNote = graph.TrimEnd().TrimEnd('}') + " <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"A payment.\" . }";
            Assert.Equal(HttpStatusCode.NoContent, (await TrigAsync(server, "manage/datasets/d", withNote)).StatusCode);
            string comment = await (await server.Client.GetAsync(new Uri("datasets/d/events/types/Paid", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);
            Assert.Contains("A payment.", comment, StringComparison.Ordinal);
            Assert.Contains("/revisions/m", comment, StringComparison.Ordinal);
            string id = JsonNode.Parse(comment)!["$id"]!.GetValue<string>();
            HttpResponseMessage revision = await server.Client.GetAsync(new Uri(id[id.IndexOf("datasets/", StringComparison.Ordinal)..], UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
            Assert.Equal(comment, await revision.Content.ReadAsStringAsync(Ct));

            // An incompatible change at the same version is refused in the new home as in the old.
            string decimalToString = withNote.Replace("XMLSchema#decimal", "XMLSchema#string", StringComparison.Ordinal);
            HttpResponseMessage incompatible = await TrigAsync(server, "manage/datasets/d", decimalToString);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, incompatible.StatusCode);
            Assert.Contains("event-schema-incompatible", await incompatible.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

            // And the move cannot be undone.
            string unmoved = withNote.Replace("<" + Varve + "Moved>", "<" + Varve + "Moving>", StringComparison.Ordinal).Replace("varve:Moved", "varve:Moving", StringComparison.Ordinal);
            HttpResponseMessage back = await TrigAsync(server, "manage/datasets/d", unmoved);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, back.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A move stopped by a stop of the server, at each step: before the
    /// freeze, after it (the barrier leaves nothing durable, so a stop during
    /// it is this case too), and after the switch. The next server finishes it,
    /// by itself on its interval or on the next request, and the result is
    /// the one an unstopped move gives: every type copied once, and every
    /// event's bytes as before.
    /// </summary>
    [Theory]
    [InlineData("none", false)]
    [InlineData("frozen", false)]
    [InlineData("frozen", true)]
    [InlineData("moved", false)]
    public async Task A_move_stopped_at_any_step_finishes_with_nothing_lost_or_doubled(string stoppedAfter, bool byItself)
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;

        try
        {
            string before;
            string expected;

            // The oracle: the same history, moved without a stop.
            string oracleRoot = Directory.CreateTempSubdirectory("varve-move-oracle-").FullName;

            try
            {
                await using RunningServer oracle = await StartAsync(oracleRoot);
                await SeedAsync(oracle);
                await MoveAsync(oracle);
                expected = await EventNodesAsync(oracle);
            }
            finally
            {
                Directory.Delete(oracleRoot, recursive: true);
            }

            await using (RunningServer first = await StartAsync(root))
            {
                await SeedAsync(first);
                before = await EventsAsync(first);

                switch (stoppedAfter)
                {
                    case "frozen":
                        // The freeze committed, and the server stopped before the copy.
                        HttpResponseMessage graph = await first.Client.GetAsync(new Uri("manage/datasets/d", UriKind.Relative), Ct);
                        string trig = await graph.Content.ReadAsStringAsync(Ct);
                        string frozen = trig.TrimEnd().TrimEnd('}') + " <urn:fleet:d> <" + Varve + "eventDeclarations> <" + Varve + "Moving> . }";
                        Assert.Equal(HttpStatusCode.NoContent, (await TrigAsync(first, "manage/datasets/d", frozen)).StatusCode);
                        break;
                    case "moved":
                        // The switch committed, and its answer was lost with the server.
                        Assert.Equal(HttpStatusCode.OK, (await MoveAsync(first)).StatusCode);
                        break;
                }
            }

            await using RunningServer second = await StartAsync(root);

            if (byItself)
            {
                // The system writer writes the dataset too: it finishes the move on its interval.
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    JsonDocument state = await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct));

                    if (state.RootElement.GetProperty("state").GetString() == "moved")
                    {
                        break;
                    }

                    await Task.Delay(100, Ct);
                }
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, (await MoveAsync(second)).StatusCode);
            }

            Assert.Equal("moved", (await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct))).RootElement.GetProperty("state").GetString());
            Assert.Equal(expected, await EventNodesAsync(second));
            Assert.Equal(before, await EventsAsync(second));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A move finished with no request, by the system writer that also writes
    /// the dataset, is the act of the agent who authorised the freeze: its
    /// commit names the agent the freeze commit's header names, never the
    /// instance (ADR 0167 point 3).
    /// </summary>
    [Fact]
    public async Task A_switch_finished_by_itself_names_the_agent_who_authorised_the_freeze()
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;
        RdfTerm alice = RdfTerm.Iri("https://issuer.example/#alice"u8);

        try
        {
            await using (RunningServer first = await StartAsync(root, interval: "00:01:00"))
            {
                await SeedAsync(first);

                // The freeze, committed by alice; the server stops before the copy.
                Fleet fleet = first.Started!.Fleet;
                fleet.Resume = null;
                CommitResult frozen = await fleet.System.CommitAsync(new CommitRequestBuilder
                {
                    Metadata = new CommitMetadata { Agent = alice },
                    Validators = [fleet.Validator],
                }.Assert(RdfTerm.Iri("urn:fleet:d"u8), RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + "eventDeclarations")), RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + "Moving")), RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + "manage/dataset/d"))), Ct);
                Assert.Equal(CommitOutcome.Committed, frozen.Outcome);
            }

            await using RunningServer second = await StartAsync(root);
            await WaitStateAsync(second, "moved");

            // The switch: the system commit that made the graph say Moved.
            RdfTerm? agent = null;
            Dataset system = second.Started!.Fleet.System;
            long head = system.Head.Value;

            await foreach (Commit commit in system.Subscribe(new Position(0), SubscriptionFilter.All, Ct))
            {
                foreach (Quad quad in commit.Delta.Asserted)
                {
                    if (commit.TryExternalise(quad.Object, out RdfTerm? o) && o.Equals(RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve + "Moved"))))
                    {
                        Assert.True(commit.TryExternalise(commit.Agent, out agent));
                    }
                }

                // A subscription tails the log: read it to the head, and no further.
                if (commit.Position.Value >= head)
                {
                    break;
                }
            }

            Assert.Equal(alice, agent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A move left Moving, by a stop between the freeze and a cancel, on an
    /// instance that does not finish it by itself: status and readiness say
    /// `moving since m₁` and stay ready, the catalogue is frozen, and the
    /// cancel, a commit of its own at m₃, reopens it for writes authorised from
    /// m₃ on, with every event as before.
    /// </summary>
    [Fact]
    public async Task A_move_left_moving_is_reported_and_a_cancel_after_a_stop_reopens_the_catalogue()
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;

        try
        {
            string before;
            long since;

            await using (RunningServer first = await StartAsync(root))
            {
                await SeedAsync(first);
                before = await EventsAsync(first);
                HttpResponseMessage graph = await first.Client.GetAsync(new Uri("manage/datasets/d", UriKind.Relative), Ct);
                string trig = await graph.Content.ReadAsStringAsync(Ct);
                first.Started!.Fleet.Resume = null;
                HttpResponseMessage freeze = await TrigAsync(first, "manage/datasets/d", trig.TrimEnd().TrimEnd('}') + " <urn:fleet:d> <" + Varve + "eventDeclarations> <" + Varve + "Moving> . }");
                Assert.Equal(HttpStatusCode.NoContent, freeze.StatusCode);
                since = long.Parse(freeze.Headers.GetValues("Varve-Position").Single(), System.Globalization.CultureInfo.InvariantCulture);
            }

            // The next instance finishes nothing by itself here: it stands for one that writes the dataset but not the system dataset.
            await using RunningServer second = await StartAsync(root, interval: "00:01:00");
            second.Started!.Fleet.Resume = null;

            JsonElement status = (await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/status", UriKind.Relative), Ct))).RootElement;
            Assert.Equal("moving", status.GetProperty("events").GetProperty("state").GetString());
            Assert.Equal(since, status.GetProperty("events").GetProperty("since").GetInt64());
            HttpResponseMessage ready = await second.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
            string readiness = await ready.Content.ReadAsStringAsync(Ct);
            Assert.True(ready.StatusCode == HttpStatusCode.OK, readiness);
            Assert.Equal(since, JsonDocument.Parse(readiness).RootElement.GetProperty("datasets").GetProperty("d").GetProperty("events").GetProperty("since").GetInt64());
            Assert.Equal(since, (await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct))).RootElement.GetProperty("since").GetInt64());

            const string Comment = "INSERT DATA { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"after the cancel\" } }";
            Assert.Equal(HttpStatusCode.Conflict, (await UpdateAsync(second, Comment)).StatusCode);

            // The cancel: varve:InDataset, its own commit.
            string current = await (await second.Client.GetAsync(new Uri("manage/datasets/d", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);
            Assert.Contains("Moving", current, StringComparison.Ordinal);
            string cancelled = current.Replace("<" + Varve + "Moving>", "<" + Varve + "InDataset>", StringComparison.Ordinal).Replace("varve:Moving", "varve:InDataset", StringComparison.Ordinal);
            HttpResponseMessage cancel = await TrigAsync(second, "manage/datasets/d", cancelled);
            Assert.True(cancel.StatusCode == HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync(Ct));
            Assert.True(long.Parse(cancel.Headers.GetValues("Varve-Position").Single(), System.Globalization.CultureInfo.InvariantCulture) > since);
            Assert.Equal("in-dataset", (await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct))).RootElement.GetProperty("state").GetString());
            Assert.False((await JsonAsync(await second.Client.GetAsync(new Uri("manage/datasets/d/status", UriKind.Relative), Ct))).RootElement.TryGetProperty("events", out _));

            Assert.Equal(before, await EventsAsync(second));
            HttpResponseMessage reopened = await UpdateAsync(second, Comment);
            Assert.True(reopened.StatusCode == HttpStatusCode.NoContent, await reopened.Content.ReadAsStringAsync(Ct));

            // And the dataset can be moved again from there.
            Assert.Equal(HttpStatusCode.OK, (await MoveAsync(second)).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitStateAsync(RunningServer server, string state)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if ((await JsonAsync(await server.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct))).RootElement.GetProperty("state").GetString() == state)
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail("d's event declarations did not become " + state);
    }

    [Fact]
    public async Task A_catalogue_that_does_not_translate_one_to_one_cancels_the_move_and_writes_reopen()
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;

        try
        {
            await using RunningServer server = await StartAsync(root);
            await SeedAsync(server);

            // One field node in two catalogue graphs, named by a type in each: valid in each graph, and no one node of the dataset's graph.
            const string Other = Varve + "events-catalogue/https%3A%2F%2Fexample.org%2Fother";
            string refund = "GRAPH <" + Other + "> { "
                + "<https://example.org/types/Refund> a <" + Varve + "EventType> ; <" + Varve + "eventName> \"Refund\" ; "
                + "<" + Varve + "eventClass> <https://example.org/ns#Refund> ; <" + Varve + "eventVersion> 1 ; "
                + "<" + Varve + "eventField> <https://example.org/types/Paid/total> ; "
                + "<" + Varve + "eventSelect> \"SELECT ?total WHERE { ?event <https://example.org/ns#total> ?total }\" . "
                + "<https://example.org/types/Paid/total> a <" + Varve + "EventField> ; <" + Varve + "fieldName> \"total\" ; "
                + "<" + Varve + "fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; <" + Varve + "fieldRequired> true }";
            HttpResponseMessage declared = await UpdateAsync(server, "INSERT DATA { " + refund + " }");
            Assert.True(declared.StatusCode == HttpStatusCode.NoContent, await declared.Content.ReadAsStringAsync(Ct));

            HttpResponseMessage move = await MoveAsync(server);
            string body = await move.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, move.StatusCode);
            Assert.Contains("event-declaration-invalid", body, StringComparison.Ordinal);
            Assert.Contains("cancelled", body, StringComparison.Ordinal);
            Assert.Equal("in-dataset", (await JsonAsync(await server.Client.GetAsync(new Uri("manage/datasets/d/events", UriKind.Relative), Ct))).RootElement.GetProperty("state").GetString());

            // The freeze is lifted: the catalogue takes writes again.
            HttpResponseMessage reopened = await UpdateAsync(server, "INSERT DATA { GRAPH <" + Catalogue + "> { <https://example.org/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"open again\" } }");
            Assert.True(reopened.StatusCode == HttpStatusCode.NoContent, await reopened.Content.ReadAsStringAsync(Ct));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_move_is_the_dataset_admin_s_or_an_operator_s_and_a_dataset_not_declared_has_none()
    {
        string root = Directory.CreateTempSubdirectory("varve-move-").FullName;

        try
        {
            await using RunningServer server = await StartAsync(root);
            Assert.Equal(HttpStatusCode.NotFound, (await server.Client.PostAsync(new Uri("manage/datasets/nowhere/events/move", UriKind.Relative), null, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await server.Client.GetAsync(new Uri("manage/datasets/nowhere/events/move", UriKind.Relative), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await server.Client.GetAsync(new Uri("manage/datasets/nowhere/events", UriKind.Relative), Ct)).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SeedAsync(RunningServer server)
    {
        await DeclareAsync(server);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Declaration())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, Event(1, "19.99"))).StatusCode);
    }

    private static Task<RunningServer> StartAsync(string root, string interval = "00:00:00.100") => RunningServer.StartAsync(new Dictionary<string, string>
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:DatasetsRoot"] = root,
        ["Varve:Fleet:FollowInterval"] = interval,
        ["Varve:Events:Source"] = "https://varve.example.org/",
    });

    private static async Task DeclareAsync(RunningServer server)
    {
        HttpResponseMessage response = await TrigAsync(server, "manage/datasets/d", ManagementTests.Declaration("d", "File").Replace("urn:varve:from-config:d", "urn:fleet:d", StringComparison.Ordinal));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static Task<HttpResponseMessage> MoveAsync(RunningServer server) =>
        server.Client.PostAsync(new Uri("manage/datasets/d/events/move", UriKind.Relative), null, Ct);

    private static async Task<HttpResponseMessage> UpdateAsync(RunningServer server, string update) =>
        await server.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

    private static async Task<HttpResponseMessage> TrigAsync(RunningServer server, string path, string trig) =>
        await server.Client.PutAsync(new Uri(path, UriKind.Relative), new StringContent(trig, Encoding.UTF8, "application/trig"), Ct);

    private static async Task<string> EventsAsync(RunningServer server)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("datasets/d/events?from=0", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept", "application/cloudevents-batch+json");
        HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return body;
    }

    private static async Task<string> EventNodesAsync(RunningServer server)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("manage/datasets/d/events", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept", "application/trig");
        HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static async Task<string> CatalogueAsOfAsync(RunningServer server, long position)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(Catalogue), UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept", "application/n-triples");
        request.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + position);
        HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return string.Join('\n', body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response, bool allowFailure = false)
    {
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(allowFailure || response.IsSuccessStatusCode, (int)response.StatusCode + ": " + body);
        return JsonDocument.Parse(body);
    }
}
