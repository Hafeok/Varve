// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Xunit;

namespace Varve.EventHubs.Sample.Tests;

/// <summary>
/// The bridge against the Event Hubs emulator (ADRs 0143, 0146): a resumed
/// bridge delivers every event at least once, and in order within the
/// dataset's partition.
/// </summary>
/// <remarks>
/// The connection string is <c>VARVE_EVENTHUBS_CONNECTION</c>, and the hub
/// <c>VARVE_EVENTHUBS_HUB</c> (<c>varve-events</c>, as
/// <c>.github/eventhubs/Config.json</c> declares it). Without the connection
/// the test is skipped, unless <c>VARVE_EVENTHUBS_REQUIRED</c> is <c>1</c>, as
/// the samples-eventhubs job sets it.
/// </remarks>
public sealed class BridgeTests
{
    private const string ConsumerGroup = "bridge-test";
    private const string Catalogue = "https://w3id.org/varve/ns#events-catalogue/https%3A%2F%2Fshop.example%2Forders";
    private const string Orders = "https://shop.example/orders";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bridge_stopped_and_resumed_at_every_position_delivers_every_event_at_least_once_and_in_order()
    {
        string connection = Connection();
        string hub = Environment.GetEnvironmentVariable("VARVE_EVENTHUBS_HUB") is { Length: > 0 } named ? named : "varve-events";

        // A dataset of its own, so that a hub the emulator keeps between runs
        // holds this run's events under a key no other run used.
        string name = "shop" + Guid.NewGuid().ToString("N")[..8];
        int port = FreePort();
        using Process varve = StartVarve(port, name);
        string checkpointPath = Path.Combine(Path.GetTempPath(), "varve-bridge-" + name + ".checkpoint");

        try
        {
            using HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
            await WaitReadyAsync(http, port);
            SparqlHttpClient client = new(http, new Uri("http://127.0.0.1:" + port + "/datasets/" + name + "/"), ClientLimits.Default);

            List<string> expected = await WriteHistoryAsync(client, new Random(93));
            Assert.NotEmpty(expected);

            // A commit with no event is not handed over and not saved, so the
            // bridge is done when the last commit with events is saved.
            long last = long.Parse(expected[^1][..expected[^1].IndexOf(' ', StringComparison.Ordinal)], CultureInfo.InvariantCulture);

            await using EventHubProducerClient producer = new(connection, hub);
            EventBridge bridge = new(client, producer, name);
            FileFollowCheckpoint file = new(checkpointPath);

            // Each run hands over one commit and then stops: after the
            // checkpoint is saved, or, every other time, after the send and
            // before the save, as a bridge that crashed there would. The next
            // run resumes from what the file holds, so the bridge is resumed
            // from every position of the history.
            int runs = 0;

            while ((await file.LoadAsync(Ct))?.Value is not long saved || saved < last)
            {
                Assert.True(++runs <= 4 * (int)last, "The bridge does not advance.");
                using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
                StoppingCheckpoint checkpoint = new(file, stop, saveFirst: runs % 2 == 0);

                try
                {
                    await bridge.RunAsync(checkpoint, new FollowOptions(TimeProvider.System) { IdleTimeout = TimeSpan.FromSeconds(5) }, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested && !Ct.IsCancellationRequested)
                {
                }
            }

            List<(string Partition, string Id, string Body)> received = await ReadAsync(connection, hub, name, expected);

            // One partition, the dataset's.
            Assert.Single(received.Select(r => r.Partition).Distinct());

            // Every event, at least once; and some more than once, since half
            // the runs stopped before their save, so the resume was exercised.
            Assert.Empty(expected.Except(received.Select(r => r.Id)));
            Assert.True(received.Count > expected.Count, "No event was sent twice, so no run resent what it had not saved.");
            TestContext.Current.TestOutputHelper?.WriteLine(
                expected.Count + " events over " + last + " positions; " + runs + " runs of the bridge; " + received.Count + " messages received, " + (received.Count - expected.Count) + " of them resends.");

            // In order: the run that resumes sends again what the last run
            // sent and did not checkpoint, and then goes on, so each event's
            // first delivery is in the history's order, and no event is
            // received after one that follows it in the history unless it is
            // a resend of a commit after the last checkpoint.
            List<string> first = [];
            HashSet<string> seen = [];

            foreach ((string _, string id, string _) in received)
            {
                if (seen.Add(id))
                {
                    first.Add(id);
                }
            }

            Assert.Equal(expected, first);

            // Every body is a structured CloudEvent whose id is the message's,
            // and the events that could not be rendered went in their places.
            int problems = 0;

            foreach ((string _, string id, string body) in received)
            {
                using JsonDocument cloudEvent = JsonDocument.Parse(body);
                Assert.Equal(id, cloudEvent.RootElement.GetProperty("id").GetString());
                Assert.Equal("1.0", cloudEvent.RootElement.GetProperty("specversion").GetString());
                problems += cloudEvent.RootElement.GetProperty("type").GetString() == "https://w3id.org/varve/problems/event-not-rendered" ? 1 : 0;
            }

            Assert.True(problems > 0, "No problem event reached the hub.");
        }
        finally
        {
            varve.Kill(entireProcessTree: true);
            File.Delete(checkpointPath);
        }
    }

    private const int Commits = 24;

    /// <summary>
    /// The declaration, then <see cref="Commits"/> commits: some with no
    /// event, some with one, some with several, and some with an event that
    /// cannot be rendered. Answers the ids of the events in stream order.
    /// </summary>
    private static async Task<List<string>> WriteHistoryAsync(SparqlHttpClient client, Random random)
    {
        await UpdateAsync(client,
            "INSERT DATA { GRAPH <" + Catalogue + "> { "
            + "<https://shop.example/types/OrderPlaced> a <https://w3id.org/varve/ns#EventType> ; "
            + "<https://w3id.org/varve/ns#eventName> \"OrderPlaced\" ; "
            + "<https://w3id.org/varve/ns#eventClass> <https://shop.example/ns#OrderPlaced> ; "
            + "<https://w3id.org/varve/ns#eventVersion> 1 ; "
            + "<https://w3id.org/varve/ns#eventField> <https://shop.example/types/OrderPlaced/total> ; "
            + "<https://w3id.org/varve/ns#eventSelect> \"SELECT ?total WHERE { ?event <https://shop.example/ns#total> ?total }\" . "
            + "<https://shop.example/types/OrderPlaced/total> a <https://w3id.org/varve/ns#EventField> ; "
            + "<https://w3id.org/varve/ns#fieldName> \"total\" ; "
            + "<https://w3id.org/varve/ns#fieldDatatype> <http://www.w3.org/2001/XMLSchema#decimal> ; "
            + "<https://w3id.org/varve/ns#fieldRequired> true } }");

        List<string> ids = [];
        int order = 0;

        for (int commit = 2; commit <= Commits + 1; commit++)
        {
            StringBuilder update = new("INSERT DATA { GRAPH <" + Orders + "> { <https://shop.example/noise/" + commit + "> <https://shop.example/ns#seen> true . ");
            int events = random.Next(4);
            List<string> nodes = [];

            for (int e = 0; e < events; e++)
            {
                string node = "https://shop.example/orders/" + (++order).ToString(CultureInfo.InvariantCulture);
                nodes.Add(node);

                // One in five cannot be rendered: two totals for one field.
                string total = random.Next(5) == 0
                    ? "1.5, 2.5"
                    : random.Next(1, 100000).ToString(CultureInfo.InvariantCulture) + "." + random.Next(100).ToString("00", CultureInfo.InvariantCulture);
                update.Append('<').Append(node).Append("> a <https://shop.example/ns#OrderPlaced> ; <https://shop.example/ns#total> ").Append(total).Append(" . ");
            }

            update.Append("} }");
            await UpdateAsync(client, update.ToString());

            // A commit's events are in the order of their nodes' IRIs (ADR 0143).
            nodes.Sort(StringComparer.Ordinal);

            foreach (string node in nodes)
            {
                ids.Add(commit.ToString(CultureInfo.InvariantCulture) + " " + node);
            }
        }

        return ids;
    }

    private static async Task UpdateAsync(SparqlHttpClient client, string update)
    {
        using HttpResponseMessage response = await client.UpdateAsync(update, (long?)null, Ct);
        Assert.True(response.IsSuccessStatusCode, update + ": " + await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Reads the hub from its start until every expected id arrived, then a little longer for resends.</summary>
    private static async Task<List<(string Partition, string Id, string Body)>> ReadAsync(string connection, string hub, string key, List<string> expected)
    {
        await using EventHubConsumerClient consumer = new(ConsumerGroup, connection, hub);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        HashSet<string> missing = [.. expected];
        List<(string, string, string)> received = [];
        ReadEventOptions options = new() { MaximumWaitTime = TimeSpan.FromSeconds(5) };

        try
        {
            await foreach (PartitionEvent partitionEvent in consumer.ReadEventsAsync(startReadingAtEarliestEvent: true, options, timeout.Token))
            {
                if (partitionEvent.Data is not EventData data)
                {
                    // An idle wait with everything in: the hub holds no more of ours.
                    if (missing.Count == 0)
                    {
                        break;
                    }

                    continue;
                }

                if (data.PartitionKey != key)
                {
                    continue;
                }

                Assert.Equal(EventBridge.ContentType, data.ContentType);
                received.Add((partitionEvent.Partition.PartitionId, data.MessageId, Encoding.UTF8.GetString(data.EventBody.ToArray())));
                missing.Remove(data.MessageId);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !Ct.IsCancellationRequested)
        {
            Assert.Fail(missing.Count + " events never arrived, among them " + missing.First() + ".");
        }

        return received;
    }

    /// <summary>
    /// A checkpoint that stops the bridge at its first save: after saving, or
    /// before, as a bridge that stopped between the send and the save would.
    /// </summary>
    private sealed class StoppingCheckpoint(IFollowCheckpoint inner, CancellationTokenSource stop, bool saveFirst) : IFollowCheckpoint
    {
        public ValueTask<FeedPosition?> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);

        public async ValueTask SaveAsync(FeedPosition position, CancellationToken cancellationToken)
        {
            if (saveFirst)
            {
                await inner.SaveAsync(position, cancellationToken);
            }

            await stop.CancelAsync();
            stop.Token.ThrowIfCancellationRequested();
        }
    }

    private static string Connection()
    {
        string? value = Environment.GetEnvironmentVariable("VARVE_EVENTHUBS_CONNECTION");

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        Assert.False(Environment.GetEnvironmentVariable("VARVE_EVENTHUBS_REQUIRED") == "1", "The Event Hubs leg is required here and VARVE_EVENTHUBS_CONNECTION is not set.");
        Assert.Skip("VARVE_EVENTHUBS_CONNECTION is not set: the bridge runs against the Event Hubs emulator in the samples-eventhubs job.");
        return string.Empty;
    }

    private static Process StartVarve(int port, string dataset)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in (string[])[
            Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"),
            "--urls=http://127.0.0.1:" + port,
            "--Varve:Auth:Mode=Anonymous",
            "--Varve:Datasets:" + dataset + ":Storage=Memory",
        ])
        {
            start.ArgumentList.Add(argument);
        }

        Process process = Process.Start(start)!;

        // Drained, so that a full pipe never blocks the server.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitReadyAsync(HttpClient http, int port)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                using HttpResponseMessage ready = await http.GetAsync(new Uri("http://127.0.0.1:" + port + "/health/ready"), Ct);

                if (ready.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500, Ct);
        }

        Assert.Fail("Varve did not become ready.");
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
