// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// A fleet of two server processes on one container or bucket (ADRs 0163,
/// 0164, 0168), on Azurite and on RustFS, which the processes reach by web
/// identity (#165): the system writer and a follower that forwards. Writes and
/// management writes through the follower are forwarded and answered by the
/// writer, and read back from the follower at the position they returned; an
/// idle followed dataset makes no request to the store; the data plane keeps
/// serving with the system writer killed; a mistaken second system writer
/// shows in readiness and in the epoch history; and a placement change
/// moves the writer with writes in flight, losing none that were answered.
/// The fleet category, which the release gate may run apart (#129).
/// </summary>
public abstract class FleetCases : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _caches = Directory.CreateTempSubdirectory("varve-fleet-").FullName;
    private readonly List<Server> _servers = [];

    private protected abstract FleetStore Store { get; }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        foreach (Server server in _servers)
        {
            await server.DisposeAsync();
        }

        Directory.Delete(_caches, recursive: true);
    }

    [Fact]
    public async Task Writes_through_the_follower_are_forwarded_and_an_idle_dataset_makes_no_requests()
    {
        FleetLocation container = await Store.NewAsync();
        await using CountingProxy proxy = await CountingProxy.StartAsync(container.Upstream);
        Server a = await StartAsync("a", container);
        long declared = await DeclareAsync(a, "d");
        declared = Math.Max(declared, await DeclareAsync(a, "e"));
        Server b = await StartAsync("b", container.Through(proxy.Address));
        await WaitDeclaredAsync(b, declared, "d", "follower");

        // A data write to the follower is forwarded, and read back there at its position.
        HttpResponseMessage written = await Update(b, "d", "INSERT DATA { <http://ex/a> <http://ex/p> \"forwarded\" }");
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        string position = written.Headers.GetValues("Varve-Position").Single();
        using HttpRequestMessage read = new(HttpMethod.Get, new Uri(b.Address, "datasets/d/sparql?query=" + Uri.EscapeDataString("ASK { ?s ?p \"forwarded\" }")));
        read.Headers.TryAddWithoutValidation("Varve-Min-Position", position);
        HttpResponseMessage asked = await b.Http.SendAsync(read, Ct);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Contains("\"boolean\":true", await asked.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A management write to the follower is forwarded to the system writer.
        HttpResponseMessage management = await Declare(b, "f");
        Assert.Equal(HttpStatusCode.Created, management.StatusCode);
        await WaitDeclaredAsync(b, long.Parse(management.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture), "f", "follower");

        // Idle: the system dataset is followed on the interval; e, read by no one, is never refreshed.
        proxy.Reset();
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        Assert.True(proxy.Count("/_fleet/") > 0, "the system dataset was not followed");
        Assert.Equal(0, proxy.Count("/e/"));
        Assert.Equal(0, proxy.Count("/d/"));

        // A read is what advances it: a probe of the next slot, and never a LIST.
        Assert.Equal(HttpStatusCode.OK, (await b.Http.GetAsync(new Uri(b.Address, "datasets/e/sparql?query=ASK%7B%7D"), Ct)).StatusCode);
        Assert.InRange(proxy.Count("/e/"), 1, 4);
        Assert.Equal(0, proxy.Lists("/e/"));
    }

    [Fact]
    public async Task The_data_plane_keeps_serving_with_the_system_writer_killed()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container);
        long declared = await DeclareAsync(a, "d");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "d", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }")).StatusCode);
        Server b = await StartAsync("b", container);
        await WaitDeclaredAsync(b, declared, "d", "follower");

        a.Kill();

        // Reads are served from what b applied; writes cannot reach a writer.
        HttpResponseMessage read = await b.Http.GetAsync(new Uri(b.Address, "datasets/d/sparql?query=" + Uri.EscapeDataString("ASK { <http://ex/a> ?p ?o }")), Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("\"boolean\":true", await read.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        HttpResponseMessage write = await Update(b, "d", "INSERT DATA { <http://ex/b> <http://ex/p> 2 }");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
        Assert.Contains("writer-unreachable", await write.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        HttpResponseMessage management = await Declare(b, "g");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, management.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Http.GetAsync(new Uri(b.Address, "health/live"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_mistaken_second_system_writer_shows_in_readiness_and_the_epoch_history()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container);
        await DeclareAsync(a, "d");

        // b is configured as the system writer too: its claim supersedes a's.
        Server b = await StartAsync("b", container, writer: "true");

        JsonDocument? management = null;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage ready = await a.Http.GetAsync(new Uri(a.Address, "health/ready"), Ct);
            using JsonDocument body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Ct));

            if (ready.StatusCode == HttpStatusCode.ServiceUnavailable && body.RootElement.GetProperty("management").GetProperty("role").GetString() == "superseded")
            {
                management = JsonDocument.Parse(body.RootElement.GetProperty("management").GetRawText());
                break;
            }

            await Task.Delay(1000, Ct);
        }

        Assert.NotNull(management);

        // b is ready once it has opened what the system dataset declares,
        // which can follow a's supersession by a reconcile round.
        await WaitReadyAsync(b, a, container);

        StringWriter output = new() { NewLine = "\n" };
        Assert.Equal(0, await Varve.Server.Commands.Cli.RunAsync(container.Lease("_fleet"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct));
        string record = output.ToString();
        Assert.Contains("epoch 1  restart  by a", record, StringComparison.Ordinal);
        Assert.Contains("epoch 2  restart  by b", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// A placement change by events (ADR 0163 point 12): writes stream through
    /// b while d is placed on b. a abdicates with the reason placement, b claims
    /// once it sees the release, and every write answered 204 is in d after;
    /// the others were refused with 503.
    /// </summary>
    [Fact]
    public async Task A_placement_change_moves_the_writer_with_writes_in_flight_and_loses_none()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, writer: "a");
        long declared = await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container, writer: "a");
        await WaitDeclaredAsync(b, declared, "d", "follower");

        ConcurrentBag<(int N, HttpStatusCode Status)> answers = [];
        using CancellationTokenSource stop = new();
        Task writing = Task.Run(async () =>
        {
            for (int n = 0; !stop.IsCancellationRequested; n++)
            {
                HttpResponseMessage response = await Update(b, "d", "INSERT DATA { <http://ex/w" + n + "> <http://ex/p> " + n + " }");
                answers.Add((n, response.StatusCode));
            }
        }, Ct);

        await Task.Delay(500, Ct);
        HttpResponseMessage placed = await a.Http.PutAsync(new Uri(a.Address, "manage/datasets/d"), Trig(Declaration("d") + " <urn:fleet:d> <https://w3id.org/varve/ns#placedOn> \"b\" ."), Ct);
        Assert.True(placed.IsSuccessStatusCode, (int)placed.StatusCode + " " + await placed.Content.ReadAsStringAsync(Ct));
        long moved = long.Parse(placed.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture);
        await WaitDeclaredAsync(b, moved, "d", "writer");
        await Task.Delay(500, Ct);
        await stop.CancelAsync();
        await writing;

        Assert.All(answers, answer => Assert.True(answer.Status is HttpStatusCode.NoContent or HttpStatusCode.ServiceUnavailable, "write " + answer.N + ": " + answer.Status));
        Assert.Contains(answers, answer => answer.Status == HttpStatusCode.NoContent);

        foreach ((int n, HttpStatusCode status) in answers)
        {
            HttpResponseMessage asked = await b.Http.GetAsync(new Uri(b.Address, "datasets/d/sparql?query=" + Uri.EscapeDataString("ASK { <http://ex/w" + n + "> ?p ?o }")), Ct);
            bool held = (await asked.Content.ReadAsStringAsync(Ct)).Contains("\"boolean\":true", StringComparison.Ordinal);
            Assert.True(status != HttpStatusCode.NoContent || held, "write " + n + " was answered 204 and is not in d");
        }

        // a now forwards to b, which writes.
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 1 }")).StatusCode);
        StringWriter output = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("d"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("placement", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The move of a dataset's event declarations (ADR 0167), sent to an
    /// instance that writes neither the dataset nor the system dataset, and
    /// forwarded to the dataset's writer: here the system writer, or the other
    /// instance, whose management writes then go to the system writer under
    /// the caller's token, each forwarded once.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Event_declarations_move_through_a_non_writer_and_render_the_same_events(bool placedOnTheFollower)
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, writer: "a");
        long declared = await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container, writer: "a");
        await WaitDeclaredAsync(b, declared, "d", "follower");
        Server dataWriter = a, other = b;

        if (placedOnTheFollower)
        {
            HttpResponseMessage placed = await a.Http.PutAsync(new Uri(a.Address, "manage/datasets/d"), Trig(Declaration("d") + " <urn:fleet:d> <https://w3id.org/varve/ns#placedOn> \"b\" ."), Ct);
            Assert.True(placed.IsSuccessStatusCode, (int)placed.StatusCode + " " + await placed.Content.ReadAsStringAsync(Ct));
            await WaitDeclaredAsync(b, long.Parse(placed.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture), "d", "writer");
            await WaitDeclaredAsync(a, long.Parse(placed.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture), "d", "follower");
            (dataWriter, other) = (b, a);
        }

        const string Catalogue = "https://w3id.org/varve/ns#events-catalogue/default";
        Assert.Equal(HttpStatusCode.NoContent, (await Update(other, "d", "INSERT DATA { GRAPH <" + Catalogue + "> { "
            + "<http://ex/types/Paid> a <https://w3id.org/varve/ns#EventType> ; <https://w3id.org/varve/ns#eventName> \"Paid\" ; "
            + "<https://w3id.org/varve/ns#eventClass> <http://ex/Paid> ; <https://w3id.org/varve/ns#eventVersion> 1 ; "
            + "<https://w3id.org/varve/ns#eventField> <http://ex/types/Paid/total> ; "
            + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?total WHERE { ?event <http://ex/total> ?total }\" . "
            + "<http://ex/types/Paid/total> a <https://w3id.org/varve/ns#EventField> ; <https://w3id.org/varve/ns#fieldName> \"total\" ; "
            + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#integer> ; <https://w3id.org/varve/ns#fieldRequired> true } }")).StatusCode);
        HttpResponseMessage emitted = await Update(other, "d", "INSERT DATA { <http://ex/e1> a <http://ex/Paid> ; <http://ex/total> 5 }");
        Assert.Equal(HttpStatusCode.NoContent, emitted.StatusCode);
        string before = await EventsAsync(dataWriter, emitted.Headers.GetValues("Varve-Position").Single());

        // Sent to the instance that writes neither the dataset nor, when placed elsewhere, the system dataset.
        HttpResponseMessage moved = await other.Http.PostAsync(new Uri(other.Address, "manage/datasets/d/events/move"), null, Ct);
        string answer = await moved.Content.ReadAsStringAsync(Ct);
        Assert.True(moved.StatusCode == HttpStatusCode.OK, answer + "\n" + a.Log + "\n" + b.Log);
        Assert.Contains("\"state\":\"moved\"", answer, StringComparison.Ordinal);

        // The same events, read on either instance; a catalogue write through the other is refused at the writer.
        Assert.Equal(before, await EventsAsync(dataWriter, emitted.Headers.GetValues("Varve-Position").Single()));
        Assert.Equal(before, await EventsAsync(other, emitted.Headers.GetValues("Varve-Position").Single()));
        HttpResponseMessage refused = await Update(other, "d", "INSERT DATA { GRAPH <" + Catalogue + "> { <http://ex/types/Paid> <http://www.w3.org/2000/01/rdf-schema#comment> \"x\" } }");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("event-catalogue-moved", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A commit after the move renders from the system dataset, on either instance, with the revision it had.
        HttpResponseMessage later = await Update(other, "d", "INSERT DATA { <http://ex/e2> a <http://ex/Paid> ; <http://ex/total> 6 }");
        Assert.Equal(HttpStatusCode.NoContent, later.StatusCode);
        string after = await EventsAsync(other, later.Headers.GetValues("Varve-Position").Single());
        Assert.Equal(after, await EventsAsync(dataWriter, later.Headers.GetValues("Varve-Position").Single()));
        Assert.StartsWith(before.TrimEnd(']'), after, StringComparison.Ordinal);
        Assert.Contains("/types/Paid/versions/1/revisions/", after[before.Length..], StringComparison.Ordinal);
    }

    /// <summary>
    /// A follower whose memtable grows past Varve:Store:MemtableLimit since its
    /// open is opened again from the writer's newest catalog by the reconcile
    /// round (ADR 0164 point 5), and answers as before: the same quads, at the
    /// writer's position.
    /// </summary>
    [Fact]
    public async Task A_follower_past_its_memtable_limit_opens_again_and_answers_the_same()
    {
        string[] small = ["--Varve:Store:MemtableLimit=40", "--Logging:LogLevel:Default=Information"];
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, "a", small);
        long declared = await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container, "a", small);
        await WaitDeclaredAsync(b, declared, "d", "follower");

        StringBuilder triples = new();

        for (int n = 0; n < 60; n++)
        {
            triples.Append("<http://ex/s").Append(n).Append("> <http://ex/p> ").Append(n).Append(" . ");
        }

        HttpResponseMessage written = await Update(a, "d", "INSERT DATA { " + triples + "}");
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        string position = written.Headers.GetValues("Varve-Position").Single();
        Assert.Equal("60", await CountAsync(b, position));

        bool reopened = false;

        for (int attempt = 0; attempt < 100 && !reopened; attempt++)
        {
            reopened = b.Log.Contains("opens again from the writer's newest catalog", StringComparison.Ordinal);
            await Task.Delay(100, Ct);
        }

        Assert.True(reopened, "b never opened its follower again:\n" + b.Log);
        Assert.Equal("60", await CountAsync(b, position));
        Assert.Equal(HttpStatusCode.NoContent, (await Update(b, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 1 }")).StatusCode);
    }

    /// <summary>
    /// A rolling restart of a dataset with a writer set (ADR 0180 point 4): a
    /// holds d and b is its standby, warm on readiness. a shuts down,
    /// releasing for shutdown; b, warm, claims d in place and writes; a,
    /// restarted, finds b holding the record, a member, and starts as b's
    /// standby, forwarding writes to it. The takeover time is reported.
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task A_rolling_restart_moves_the_writer_to_the_warm_standby_and_the_restarted_writer_stands_by()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, writer: "a");
        long declared = await DeclareAsync(a, "d", "a", "b");
        Server b = await StartAsync("b", container, writer: "a");
        await WaitDeclaredAsync(b, declared, "d", "standby");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(b, "d", "INSERT DATA { <http://ex/before> <http://ex/p> 1 }")).StatusCode);
        await WaitWarmAsync(b, "d");

        Stopwatch takeover = Stopwatch.StartNew();
        await a.StopAsync();
        await WaitDeclaredAsync(b, declared, "d", "writer");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(b, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 2 }")).StatusCode);
        takeover.Stop();
        Measured("abdication (shutdown): the writer's stop to the standby's first commit", takeover);

        Server again = await StartAsync("a", container, writer: "a");
        await WaitDeclaredAsync(again, declared, "d", "standby");
        HttpResponseMessage forwarded = await Update(again, "d", "INSERT DATA { <http://ex/forwarded> <http://ex/p> 3 }");
        Assert.Equal(HttpStatusCode.NoContent, forwarded.StatusCode);
        Assert.Equal("3", await CountAsync(again, forwarded.Headers.GetValues("Varve-Position").Single()));

        StringWriter output = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("d"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("abdication", output.ToString(), StringComparison.Ordinal);

        // The state resource: a's role, the epoch b claimed, the writer set and the history (ADR 0180 point 7).
        await WaitWarmAsync(again, "d");
        using JsonDocument state = JsonDocument.Parse(await again.Http.GetStringAsync(new Uri(again.Address, "manage/datasets/d/state"), Ct));
        JsonElement root = state.RootElement;
        Assert.Equal("standby", root.GetProperty("role").GetString());
        Assert.True(root.GetProperty("warm").GetBoolean());
        Assert.Equal(2, root.GetProperty("epoch").GetInt64());
        Assert.Equal("a", root.GetProperty("writerSet").GetProperty("placedOn").GetString());
        Assert.Equal(["b"], root.GetProperty("writerSet").GetProperty("standbyOn").EnumerateArray().Select(standby => standby.GetString()));
        JsonElement[] history = [.. root.GetProperty("history").EnumerateArray()];
        Assert.Equal(("a", "restart", "shutdown"), (history[0].GetProperty("holder").GetString(), history[0].GetProperty("trigger").GetString(), history[0].GetProperty("released").GetString()));
        Assert.Equal(("b", "abdication"), (history[^1].GetProperty("holder").GetString(), history[^1].GetProperty("trigger").GetString()));
    }

    /// <summary>
    /// The system dataset hands over too (ADR 0179 point 13): with
    /// Varve:Fleet:Writer "a,b" and automatic handover, a writes it and b is
    /// its standby. a shuts down; b, warm, claims it in place and answers
    /// management writes itself; a, restarted, finds b holding it and stands
    /// by, forwarding management writes to b.
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task The_system_dataset_moves_to_its_warm_standby_when_its_writer_shuts_down()
    {
        string[] handover = ["--Varve:Fleet:AutomaticHandover=true"];
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, "a,b", handover);
        long declared = await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container, "a,b", handover);
        await WaitDeclaredAsync(b, declared, "d", "follower");
        await WaitManagementAsync(b, "follower", warm: true);

        Stopwatch takeover = Stopwatch.StartNew();
        await a.StopAsync();
        await WaitManagementAsync(b, "writer", warm: null);
        long second = await DeclareAsync(b, "e");
        takeover.Stop();
        Measured("system dataset, abdication (shutdown): the writer's stop to the standby's first management commit", takeover);

        Server again = await StartAsync("a", container, "a,b", handover);
        await WaitManagementAsync(again, "follower", warm: true);
        await WaitDeclaredAsync(again, second, "e", "follower");
        Assert.True((await Declare(again, "f")).IsSuccessStatusCode, "a management write through the returning writer is forwarded to b");

        StringWriter output = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("_fleet"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("abdication", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The storage-error abdication (ADR 0179 point 6): a writes d through a
    /// proxy that starts failing its log writes, so a batch fails with every
    /// retry spent. a releases the epoch for storage-error and stands by; b,
    /// warm and reaching the store directly, claims in place and writes, and a
    /// forwards to it. The epoch history shows the release and the claim.
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task A_writer_whose_batch_fails_against_the_store_abdicates_and_the_warm_standby_takes_over()
    {
        FleetLocation container = await Store.NewAsync();
        await using CountingProxy proxy = await CountingProxy.StartAsync(container.Upstream);
        Server a = await StartAsync("a", container.Through(proxy.Address), writer: "a");
        long declared = await DeclareAsync(a, "d", "a", "b");
        Server b = await StartAsync("b", container, writer: "a");
        await WaitDeclaredAsync(b, declared, "d", "standby");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(a, "d", "INSERT DATA { <http://ex/before> <http://ex/p> 1 }")).StatusCode);
        await WaitWarmAsync(b, "d");

        proxy.Fail = (method, path) => method == "PUT" && path.Contains("/d/log/", StringComparison.Ordinal);
        Stopwatch takeover = Stopwatch.StartNew();
        HttpResponseMessage failed = await Update(a, "d", "INSERT DATA { <http://ex/lost> <http://ex/p> 2 }");
        Assert.NotEqual(HttpStatusCode.NoContent, failed.StatusCode);
        Stopwatch afterRefusal = Stopwatch.StartNew();
        await WaitDeclaredAsync(b, declared, "d", "writer");

        HttpResponseMessage written = await Update(a, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 3 }");
        Assert.True(written.StatusCode == HttpStatusCode.NoContent, (int)written.StatusCode + " " + await written.Content.ReadAsStringAsync(Ct) + "\n" + a.Log);
        takeover.Stop();
        afterRefusal.Stop();
        Measured("abdication (storage-error): the first failed write, retries included, to the first commit forwarded to the standby", takeover);
        Measured("abdication (storage-error): the failed write's answer to the first commit forwarded to the standby", afterRefusal);
        Assert.Equal("2", await CountAsync(b, written.Headers.GetValues("Varve-Position").Single()));

        StringWriter output = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("d"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("storage-error", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The probe trigger (ADR 0179 point 6): a holds d and is killed, so it
    /// releases nothing. A takeover request naming epoch 1, sent to b, warm,
    /// is answered by b with epoch 2, and b writes. Sent again it is a no-op,
    /// epoch-moved; to a dataset without automatic handover it is
    /// handover-off; to one whose writer set leaves b out, not-in-writer-set;
    /// to the holder itself, which is no standby, cold.
    /// The time from the kill to b's first write is reported.
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task A_takeover_request_moves_a_killed_writer_s_dataset_to_the_warm_standby_and_refuses_the_rest()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, writer: "a");
        long declared = await DeclareAsync(a, "d", "a", "b");
        await DeclareAsync(a, "plain");
        long last = await DeclareAsync(a, "other", "a", "c");
        Server b = await StartAsync("b", container, writer: "a");
        await WaitDeclaredAsync(b, last, "d", "standby");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(b, "d", "INSERT DATA { <http://ex/before> <http://ex/p> 1 }")).StatusCode);
        await WaitWarmAsync(b, "d");

        await AssertRefusedAsync(b, "d", 5, "epoch-moved", 1);
        await AssertRefusedAsync(b, "plain", 1, "handover-off", 1);
        await AssertRefusedAsync(b, "other", 1, "not-in-writer-set", 1);
        await AssertRefusedAsync(a, "d", 1, "cold", 1);

        Stopwatch downtime = Stopwatch.StartNew();
        a.Kill();
        HttpResponseMessage took = await Takeover(b, "d", 1);
        string answer = await took.Content.ReadAsStringAsync(Ct);
        Assert.True(took.StatusCode == HttpStatusCode.OK, (int)took.StatusCode + " " + answer + "\n" + b.Log);
        using (JsonDocument body = JsonDocument.Parse(answer))
        {
            Assert.Equal(2, body.RootElement.GetProperty("epoch").GetInt64());
        }

        HttpResponseMessage written = await Update(b, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 2 }");
        downtime.Stop();
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
        Assert.Equal("2", await CountAsync(b, written.Headers.GetValues("Varve-Position").Single()));
        Measured("probe: the writer's kill, then the takeover request, to the standby's first commit", downtime);

        await AssertRefusedAsync(b, "d", 1, "epoch-moved", 2);
        StringWriter output = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("d"), new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("probe", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The system dataset taken over on a probe (ADR 0179 point 13): its
    /// writer a is killed, and b, warm in its writer set, takes it over on a
    /// request to <c>_fleet</c>, then answers management writes.
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task A_takeover_request_moves_the_system_dataset_of_a_killed_writer()
    {
        string[] handover = ["--Varve:Fleet:AutomaticHandover=true"];
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container, "a,b", handover);
        await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container, "a,b", handover);
        await WaitManagementAsync(b, "follower", warm: true);

        Stopwatch downtime = Stopwatch.StartNew();
        a.Kill();
        HttpResponseMessage took = await Takeover(b, "_fleet", 1);
        Assert.True(took.StatusCode == HttpStatusCode.OK, (int)took.StatusCode + " " + await took.Content.ReadAsStringAsync(Ct) + "\n" + b.Log);
        Assert.True((await Declare(b, "e")).IsSuccessStatusCode);
        downtime.Stop();
        Measured("system dataset, probe: the writer's kill, then the takeover request, to the standby's first management commit", downtime);
        await WaitManagementAsync(b, "writer", warm: null);
    }

    /// <summary>
    /// The default two-app layout keeps restart as its recovery (#97, ADR
    /// 0181): a is the system writer and d's, with no standby, and b follows
    /// and forwards. a stops and starts again on the same cache, and the
    /// write downtime is the time from its stop to the first write b forwards
    /// and a commits, beside the takeover times. It includes the process
    /// start here; a platform's restart adds its own (#164 measures it).
    /// </summary>
    [Fact]
    [Trait("Measure", "Handover")]
    public async Task A_restart_of_the_writer_with_no_standby_answers_writes_through_the_follower_again()
    {
        FleetLocation container = await Store.NewAsync();
        Server a = await StartAsync("a", container);
        long declared = await DeclareAsync(a, "d");
        Server b = await StartAsync("b", container);
        await WaitDeclaredAsync(b, declared, "d", "follower");
        Assert.Equal(HttpStatusCode.NoContent, (await Update(b, "d", "INSERT DATA { <http://ex/before> <http://ex/p> 1 }")).StatusCode);

        Stopwatch downtime = Stopwatch.StartNew();
        await a.StopAsync();
        Server again = await StartAsync("a", container);
        HttpResponseMessage write;
        int refused = 0;

        // b refuses while no writer answers, writer-unreachable or writer-moved, and forwards once a holds d again.
        while ((write = await Update(b, "d", "INSERT DATA { <http://ex/after> <http://ex/p> 2 }")).StatusCode != HttpStatusCode.NoContent)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, write.StatusCode);
            Assert.True(++refused < 600, "b never forwarded a write to the restarted writer\n" + b.Log + "\n" + again.Log);
            await Task.Delay(50, Ct);
        }

        downtime.Stop();
        Measured("restart (two-app layout, no standby): the writer's stop, its start again, to the first write the follower forwards", downtime);
        Assert.Equal("2", await CountAsync(b, write.Headers.GetValues("Varve-Position").Single()));
    }

    private static Task<HttpResponseMessage> Takeover(Server server, string dataset, long epoch) =>
        server.Http.PutAsync(new Uri(server.Address, "manage/datasets/" + dataset + "/state"), new StringContent("{\"takeover\":{\"epoch\":" + epoch.ToString(CultureInfo.InvariantCulture) + "}}", Encoding.UTF8, "application/json"), Ct);

    private static async Task AssertRefusedAsync(Server server, string dataset, long epoch, string reason, long current)
    {
        HttpResponseMessage refused = await Takeover(server, dataset, epoch);
        string body = await refused.Content.ReadAsStringAsync(Ct);
        Assert.True(refused.StatusCode == HttpStatusCode.Conflict, (int)refused.StatusCode + " " + body);
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.EndsWith("/takeover-refused", problem.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.Equal(reason, problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(current, problem.RootElement.GetProperty("epoch").GetInt64());
    }

    // Until readiness reports the instance's system role, and as a standby whether it is warm (ADR 0179 point 13).
    private static async Task WaitManagementAsync(Server server, string role, bool? warm)
    {
        string last = string.Empty;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);
            last = await ready.Content.ReadAsStringAsync(Ct);

            using (JsonDocument body = JsonDocument.Parse(last))
            {
                if (body.RootElement.TryGetProperty("management", out JsonElement management)
                    && management.GetProperty("role").GetString() == role
                    && (warm is not { } wanted || (management.TryGetProperty("warm", out JsonElement reported) && reported.GetBoolean() == wanted)))
                {
                    return;
                }
            }

            await Task.Delay(500, Ct);
        }

        Assert.Fail(server.Instance + " never reported the system role " + role + (warm is { } w ? ", warm " + w : string.Empty) + ": " + last + "\n" + server.Log);
    }

    // A handover time: a diagnostic message, and a row of the table the
    // handover (services) job writes to its summary, when it names the file
    // (ADR 0179, #165).
    private void Measured(string what, Stopwatch elapsed)
    {
        string store = Store == FleetStore.Azurite ? "Azurite" : "RustFS";
        TestContext.Current.SendDiagnosticMessage(what + ", " + store + ": " + elapsed.ElapsedMilliseconds + " ms");

        if (Environment.GetEnvironmentVariable("VARVE_HANDOVER_TIMES") is { Length: > 0 } file)
        {
            File.AppendAllText(file, "| " + what + " | " + store + " | " + elapsed.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " |\n");
        }
    }

    // Until readiness reports the dataset's standby warm (ADR 0180 point 7).
    private static async Task WaitWarmAsync(Server server, string dataset)
    {
        string last = string.Empty;

        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);
            last = await ready.Content.ReadAsStringAsync(Ct);

            if (ready.StatusCode == HttpStatusCode.OK)
            {
                using JsonDocument body = JsonDocument.Parse(last);
                JsonElement entry = body.RootElement.GetProperty("datasets").GetProperty(dataset);

                if (entry.GetProperty("role").GetString() == "standby" && entry.GetProperty("tailing").GetBoolean() && entry.GetProperty("warm").GetBoolean())
                {
                    return;
                }
            }

            await Task.Delay(1000, Ct);
        }

        Assert.Fail(server.Instance + " never reported " + dataset + "'s standby warm: " + last + "\n" + server.Log);
    }

    private static async Task<string> CountAsync(Server server, string atLeast)
    {
        using HttpRequestMessage read = new(HttpMethod.Get, new Uri(server.Address, "datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT (COUNT(*) AS ?n) { ?s <http://ex/p> ?o }")));
        read.Headers.TryAddWithoutValidation("Varve-Min-Position", atLeast);
        read.Headers.TryAddWithoutValidation("Accept", "text/csv");
        HttpResponseMessage response = await server.Http.SendAsync(read, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, (int)response.StatusCode + " " + body);
        return body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1];
    }

    private static async Task<string> EventsAsync(Server server, string atLeast)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(server.Address, "datasets/d/events?from=0"));
        request.Headers.TryAddWithoutValidation("Accept", "application/cloudevents-batch+json");
        request.Headers.TryAddWithoutValidation("Varve-Min-Position", atLeast);
        HttpResponseMessage response = await server.Http.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, server.Instance + ": " + (int)response.StatusCode + " " + body);
        return body;
    }

    // ---- the fleet ----

    private async Task<Server> StartAsync(string instance, FleetLocation container, string writer = "a", params string[] extra)
    {
        int port = FreePort();
        Server server = Server.Start(instance, port, container, Path.Combine(_caches, instance), writer, extra);
        _servers.Add(server);
        await server.WaitLiveAsync();
        return server;
    }

    private static async Task<long> DeclareAsync(Server server, string dataset)
    {
        HttpResponseMessage response = await Declare(server, dataset);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync(Ct));
        return long.Parse(response.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture);
    }

    private static Task<HttpResponseMessage> Declare(Server server, string dataset) =>
        server.Http.PutAsync(new Uri(server.Address, "manage/datasets/" + dataset), Trig(Declaration(dataset)), Ct);

    // A dataset with a writer set (ADR 0180): placed on one instance, the others its standbys.
    private static async Task<long> DeclareAsync(Server server, string dataset, string placedOn, params string[] standbys)
    {
        string trig = Declaration(dataset)
            + " <urn:fleet:" + dataset + "> <https://w3id.org/varve/ns#placedOn> \"" + placedOn + "\" ;"
            + " <https://w3id.org/varve/ns#automaticHandover> true"
            + string.Concat(standbys.Select(standby => " ; <https://w3id.org/varve/ns#standbyOn> \"" + standby + "\""))
            + " .";
        HttpResponseMessage response = await server.Http.PutAsync(new Uri(server.Address, "manage/datasets/" + dataset), Trig(trig), Ct);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " " + await response.Content.ReadAsStringAsync(Ct));
        return long.Parse(response.Headers.GetValues("Varve-Position").Single(), CultureInfo.InvariantCulture);
    }

    private static string Declaration(string dataset) =>
        "<urn:fleet:" + dataset + "> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"" + dataset + "\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#ObjectStorage> .";

    private static StringContent Trig(string trig) => new(trig, Encoding.UTF8, "application/trig");

    // Until the server has applied the system position and holds the dataset in the role.
    private static async Task WaitDeclaredAsync(Server server, long position, string dataset, string role)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            using HttpRequestMessage list = new(HttpMethod.Get, new Uri(server.Address, "manage/datasets"));
            list.Headers.TryAddWithoutValidation("Varve-Min-Management-Position", position.ToString(CultureInfo.InvariantCulture));
            HttpResponseMessage response = await server.Http.SendAsync(list, Ct);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

                if (body.RootElement.GetProperty("datasets").EnumerateArray().Any(entry =>
                    entry.GetProperty("name").GetString() == dataset && entry.GetProperty("role").GetString() == role && entry.GetProperty("state").GetString() == "open"))
                {
                    return;
                }
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail(server.Instance + " did not hold " + dataset + " as " + role + " at system position " + position + ":\n" + server.Log);
    }

    private static async Task WaitReadyAsync(Server server, Server other, FleetLocation container)
    {
        string last = string.Empty;

        // Within the health endpoints' 60 probes a minute (ADR 0113).
        for (int attempt = 0; attempt < 40; attempt++)
        {
            HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);

            if (ready.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            last = await ready.Content.ReadAsStringAsync(Ct);
            await Task.Delay(1000, Ct);
        }

        StringWriter lease = new() { NewLine = "\n" };
        await Varve.Server.Commands.Cli.RunAsync(container.Lease("d"), new Varve.Server.Commands.Io(lease, new StringWriter(), new MemoryStream()), Ct);
        Assert.Fail(server.Instance + " did not become ready: " + last + "\nd's writer record:\n" + lease + "\n" + server.Instance + ":\n" + server.Log + "\n" + other.Instance + ":\n" + other.Log);
    }

    private static Task<HttpResponseMessage> Update(Server server, string dataset, string update) =>
        server.Http.PostAsync(new Uri(server.Address, "datasets/" + dataset + "/sparql"), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>One server process of the fleet, anonymous, on a fixed loopback port.</summary>
    private sealed class Server : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _log = new();

        private Server(string instance, Uri address, Process process)
        {
            Instance = instance;
            Address = address;
            _process = process;
        }

        internal string Instance { get; }

        internal Uri Address { get; }

        internal HttpClient Http { get; } = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };

        internal string Log
        {
            get
            {
                lock (_log)
                {
                    return _log.ToString();
                }
            }
        }

        internal static Server Start(string instance, int port, FleetLocation container, string cache, string writer, string[] extra)
        {
            string address = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

            foreach (string argument in (string[])[
                Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"),
                "--urls=" + address,
                "--Logging:LogLevel:Default=Warning",
                "--Varve:Auth:Mode=Anonymous",
                .. container.ServerArguments,
                "--Varve:ObjectStorage:CachePath=" + cache,
                "--Varve:ObjectStorage:Instance=" + instance,
                "--Varve:Fleet:Writer=" + writer,
                "--Varve:Fleet:InternalUrl=" + address,
                "--Varve:Fleet:FollowInterval=00:00:00.200",

                // One event source, so that every instance renders the same bytes (ADR 0143).
                "--Varve:Events:Source=https://varve.example.org/",
                .. extra,
            ])
            {
                start.ArgumentList.Add(argument);
            }

            foreach ((string name, string value) in container.Environment)
            {
                start.Environment[name] = value;
            }

            Server server = new(instance, new Uri(address + "/"), Process.Start(start)!);
            server._process.OutputDataReceived += (_, line) => server.Append(line.Data);
            server._process.ErrorDataReceived += (_, line) => server.Append(line.Data);
            server._process.BeginOutputReadLine();
            server._process.BeginErrorReadLine();
            return server;
        }

        internal async Task WaitLiveAsync()
        {
            for (int attempt = 0; attempt < 300; attempt++)
            {
                Assert.False(_process.HasExited, Instance + " exited: " + Log);

                try
                {
                    if ((await Http.GetAsync(new Uri(Address, "health/live"), Ct)).IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(100, Ct);
            }

            Assert.Fail(Instance + " did not come up: " + Log);
        }

        /// <summary>Stops the process as an orchestrator does, by SIGTERM, and waits for it to exit: the host drains and releases what it holds.</summary>
        internal async Task StopAsync()
        {
            if (_process.HasExited)
            {
                return;
            }

            using (Process signal = Process.Start("kill", ["-TERM", _process.Id.ToString(CultureInfo.InvariantCulture)]))
            {
                await signal.WaitForExitAsync(Ct);
            }

            using CancellationTokenSource exit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            exit.CancelAfter(TimeSpan.FromSeconds(60));
            await _process.WaitForExitAsync(exit.Token);
        }

        internal void Kill()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }
        }

        public ValueTask DisposeAsync()
        {
            Kill();
            _process.Dispose();
            Http.Dispose();
            return ValueTask.CompletedTask;
        }

        private void Append(string? line)
        {
            if (line is not null)
            {
                lock (_log)
                {
                    _log.AppendLine(line);
                }
            }
        }
    }

    /// <summary>
    /// A loopback proxy in front of the store that counts the requests it
    /// passes, by path: what one server asks of the store.
    /// </summary>
    private sealed class CountingProxy : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<(string Method, string Path, string Query)> _seen = new();

        private CountingProxy(WebApplication app, Uri address)
        {
            _app = app;
            Address = address;
        }

        internal Uri Address { get; }

        internal static async Task<CountingProxy> StartAsync(Uri store)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();
            WebApplication app = builder.Build();
            HttpClient upstream = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
            CountingProxy? proxy = null;

            app.Run(async context =>
            {
                HttpRequest request = context.Request;
                proxy!._seen.Enqueue((request.Method, request.Path.Value ?? string.Empty, request.QueryString.Value ?? string.Empty));

                // A store that fails what the test names, as a store's 500.
                if (proxy.Fail is { } fail && fail(request.Method, request.Path.Value ?? string.Empty))
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return;
                }
                using HttpRequestMessage forward = new(new HttpMethod(request.Method), new Uri(store, request.Path.Value!.TrimStart('/') + request.QueryString.Value));
                MemoryStream body = new();
                await request.Body.CopyToAsync(body, context.RequestAborted);

                if (body.Length > 0 || request.ContentLength is not null)
                {
                    forward.Content = new ByteArrayContent(body.ToArray());
                }

                foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in request.Headers)
                {
                    // The host the client signed stays the host the store sees: S3's signature covers it.
                    if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
                        forward.Headers.Host = values.ToString();
                        continue;
                    }

                    if (!forward.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values))
                    {
                        forward.Content?.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
                    }
                }

                using HttpResponseMessage answer = await upstream.SendAsync(forward, context.RequestAborted);
                context.Response.StatusCode = (int)answer.StatusCode;

                foreach ((string name, IEnumerable<string> values) in answer.Headers.Concat(answer.Content.Headers))
                {
                    if (!name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers[name] = values.ToArray();
                    }
                }

                await answer.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            });

            await app.StartAsync(Ct);
            string address = app.Urls.First();
            proxy = new CountingProxy(app, new Uri(address.TrimEnd('/') + "/"));
            return proxy;
        }

        internal void Reset() => _seen.Clear();

        /// <summary>The requests the proxy answers 500 to, by method and path, instead of passing them on; none when null.</summary>
        internal Func<string, string, bool>? Fail { get; set; }

        /// <summary>The requests whose path holds <paramref name="fragment"/>.</summary>
        internal int Count(string fragment) => _seen.Count(request => request.Path.Contains(fragment, StringComparison.Ordinal));

        /// <summary>The LISTs under a prefix holding <paramref name="fragment"/>: Azure's <c>comp=list</c>, S3's <c>list-type=2</c>.</summary>
        internal int Lists(string fragment) => _seen.Count(request =>
            (request.Query.Contains("comp=list", StringComparison.Ordinal) || request.Query.Contains("list-type=", StringComparison.Ordinal))
            && Uri.UnescapeDataString(request.Query).Contains(fragment.Trim('/'), StringComparison.Ordinal));

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

/// <summary>The fleet on one Azurite container.</summary>
[Trait("Category", "Fleet")]
public sealed class FleetTests : FleetCases
{
    private protected override FleetStore Store => FleetStore.Azurite;
}

/// <summary>The fleet on one RustFS bucket, the server processes reaching it by web identity (#165).</summary>
[Trait("Category", "Fleet")]
public sealed class RustFsFleetTests : FleetCases
{
    private protected override FleetStore Store => FleetStore.RustFs;
}
