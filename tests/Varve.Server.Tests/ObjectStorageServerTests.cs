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
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Model;
using Varve.Store.Azure;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// A server's datasets in an object store (ADRs 0134–0139): the settings
/// that name the store and refuse a credential, readiness reporting a
/// superseded dataset, and — against Azurite, when CI starts it — a second
/// server taking a dataset over, after which the first answers writes with
/// <c>503 writer-superseded</c>, stays live, and reports itself not ready.
/// </summary>
public sealed class ObjectStorageServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void an_object_storage_dataset_needs_a_provider_and_the_provider_its_address_and_a_cache()
    {
        ServerSettings settings = new();
        settings.Auth.Mode = "Anonymous";
        settings.ObjectStorage.Provider = "S3Compatible";
        List<string> s3 = SettingsCheck.Errors(settings);
        Assert.Contains(s3, e => e.StartsWith("Varve:ObjectStorage:Endpoint", StringComparison.Ordinal));
        Assert.Contains(s3, e => e.StartsWith("Varve:ObjectStorage:Bucket", StringComparison.Ordinal));
        Assert.Contains(s3, e => e.StartsWith("Varve:ObjectStorage:Region", StringComparison.Ordinal));
        Assert.Contains(s3, e => e.StartsWith("Varve:ObjectStorage:CachePath", StringComparison.Ordinal));

        settings.ObjectStorage.Provider = "Azure";
        settings.ObjectStorage.ContainerUrl = "https://account.blob.core.windows.net/dataset";
        settings.ObjectStorage.CachePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-cache"));
        Assert.DoesNotContain(SettingsCheck.Errors(settings), e => e.Contains("ObjectStorage", StringComparison.Ordinal));

        settings.ObjectStorage.Provider = "Gcs";
        Assert.Contains(SettingsCheck.Errors(settings), e => e.StartsWith("Varve:ObjectStorage:Provider is Azure or S3Compatible", StringComparison.Ordinal));
    }

    [Fact]
    public void a_storage_credential_in_configuration_is_refused_by_name()
    {
        ServerSettings settings = new();
        settings.Auth.Mode = "Anonymous";
        settings.ObjectStorage.Provider = "Azure";
        settings.ObjectStorage.CachePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-cache"));
        settings.ObjectStorage.ContainerUrl = "https://account.blob.core.windows.net/dataset?sv=2025-11-05&sig=abc";
        Assert.Contains(SettingsCheck.Errors(settings), e => e.Contains("a SAS token or a key is refused", StringComparison.Ordinal) && e.Contains("ADR 0137", StringComparison.Ordinal));

        foreach (string key in new[] { "Varve:ObjectStorage:AccountKey", "Varve:ObjectStorage:ConnectionString", "Varve:ObjectStorage:SecretAccessKey" })
        {
            Assert.Contains("is a storage credential, and the server takes none", SettingsCheck.UnknownKeyError(key), StringComparison.Ordinal);
        }

        Assert.Contains("is not a setting the server knows", SettingsCheck.UnknownKeyError("Varve:ObjectStorage:Bukket"), StringComparison.Ordinal);
    }

    [Fact]
    public void a_superseded_dataset_is_not_ready_and_says_why()
    {
        (bool ready, List<DatasetReadiness> states) = Readiness.Evaluate(
            [new DatasetHealth("d", DatasetState.Open, null, 4, 4, Failed: false, Superseded: true), new DatasetHealth("e", DatasetState.Open, null, 2, 2, false)],
            allowedLag: 0,
            draining: false);

        Assert.False(ready);
        Assert.Equal("superseded", states[0].State);
        Assert.NotNull(states[0].Reason);
        Assert.Equal("ready", states[1].State);
    }

    [Fact]
    public async Task a_server_taken_over_by_another_answers_writes_with_writer_superseded_and_stays_live()
    {
        string account = Environment.GetEnvironmentVariable("VARVE_AZURITE") ?? string.Empty;

        if (account.Length == 0)
        {
            if ((Environment.GetEnvironmentVariable("VARVE_STORAGE_LEGS_REQUIRED") ?? string.Empty).Split(',').Contains("azurite"))
            {
                Assert.Fail("The azurite leg is required here (VARVE_STORAGE_LEGS_REQUIRED) and VARVE_AZURITE is not set.");
            }

            Assert.Skip("The azurite leg needs VARVE_AZURITE (ADR 0134).");
        }

        Uri container = new(account.TrimEnd('/') + "/s" + Guid.NewGuid().ToString("N")[..20]);
        await CreateContainerAsync(container);
        string caches = Directory.CreateTempSubdirectory("varve-server-objects-").FullName;

        try
        {
            await using RunningServer first = await RunningServer.StartAsync(Settings(container, Path.Combine(caches, "a"), "first"));
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(first, "INSERT DATA { <http://ex/a> <http://ex/p> 1 }")).StatusCode);

            await using RunningServer second = await RunningServer.StartAsync(Settings(container, Path.Combine(caches, "b"), "second"));

            // The first server's next write finds the next slot taken.
            HttpResponseMessage refused = await UpdateAsync(first, "INSERT DATA { <http://ex/b> <http://ex/p> 2 }");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Contains("writer-superseded", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

            HttpResponseMessage ready = await first.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Contains("superseded", await ready.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await first.Client.GetAsync(new Uri("health/live", UriKind.Relative), Ct)).StatusCode);

            // The second holds the dataset: it has the first's commit, and writes.
            Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(second, "INSERT DATA { <http://ex/c> <http://ex/p> 3 }")).StatusCode);
            HttpResponseMessage asked = await second.Client.PostAsync(
                new Uri("datasets/d/sparql", UriKind.Relative),
                new StringContent("ASK { <http://ex/a> <http://ex/p> 1 . <http://ex/c> <http://ex/p> 3 }", Encoding.UTF8, "application/sparql-query"),
                Ct);
            Assert.Contains("true", await asked.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync(new Uri("health/ready", UriKind.Relative), Ct)).StatusCode);

            // varve lease shows the record; a takeover names the epoch it saw.
            string dataset = container + "/d";
            (int shown, string record) = await LeaseAsync(dataset);
            Assert.Equal(0, shown);
            Assert.Contains("epoch: 2", record, StringComparison.Ordinal);
            Assert.Contains("holder: second", record, StringComparison.Ordinal);
            Assert.Contains("epoch 1  restart  by first", record, StringComparison.Ordinal);
            Assert.Contains("epoch 2  restart  by second", record, StringComparison.Ordinal);

            Assert.Equal(1, (await LeaseAsync(dataset, "--take", "--epoch", "1")).Exit);
            (int took, string after) = await LeaseAsync(dataset, "--take", "--epoch", "2");
            Assert.Equal(0, took);
            Assert.Contains("epoch: 3", after, StringComparison.Ordinal);
            Assert.Contains("operator", after, StringComparison.Ordinal);
            Assert.Contains("released: OperatorTakeover", after, StringComparison.Ordinal);

            HttpResponseMessage taken = await UpdateAsync(second, "INSERT DATA { <http://ex/d> <http://ex/p> 4 }");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, taken.StatusCode);
            Assert.Contains("writer-superseded", await taken.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(caches, recursive: true);
        }
    }

    /// <summary>
    /// Readiness reports what the store keeps for readers, per dataset with
    /// reader records, and stays ready whatever it is (ADR 0162).
    /// </summary>
    [Fact]
    public void readiness_reports_retention_and_never_turns_unready_for_it()
    {
        Varve.Store.ExtentRetention retention = new(
            new Varve.Store.Log.CatalogVersion(9), 3, 2, 4096,
            [new Varve.Store.ReaderRetention(new Varve.Store.Log.ReaderAnnouncement(new Varve.Store.Log.ReaderId("follower"), new Varve.Store.Log.CatalogVersion(4), new Varve.Store.Log.CatalogVersion(4)), 4096)]);
        DatasetHealth[] health = [new DatasetHealth("d", DatasetState.Open, null, 4, 4, false, Retention: retention), new DatasetHealth("e", DatasetState.Open, null, 2, 2, false)];
        (bool ready, List<DatasetReadiness> states) = Readiness.Evaluate(health, allowedLag: 0, draining: false);
        Assert.True(ready);
        Assert.All(states, state => Assert.Equal("ready", state.State));

        System.Buffers.ArrayBufferWriter<byte> body = new();

        using (System.Text.Json.Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            Readiness.WriteRetention(json, health);
            json.WriteEndObject();
        }

        Assert.Equal(
            """{"retention":{"d":{"catalogVersion":9,"sets":3,"unnamedSets":2,"retainedBytes":4096,"readers":[{"reader":"follower","oldest":4,"newest":4,"maxDerived":4,"retainedBytes":4096}]}}}""",
            Encoding.UTF8.GetString(body.WrittenSpan));
    }

    /// <summary>
    /// On Azurite: <c>varve info</c> reads a dataset in the store read-only,
    /// announced in a record it deletes when it ends, or unannounced on
    /// request; <c>varve storage</c> reports the sets per permutation and the
    /// reader records; <c>forget</c> acts only on the interval list showed
    /// (ADRs 0160, 0162).
    /// </summary>
    [Fact]
    public async Task the_storage_command_reports_sets_and_readers_and_forgets_only_what_it_was_shown()
    {
        string account = Environment.GetEnvironmentVariable("VARVE_AZURITE") ?? string.Empty;

        if (account.Length == 0)
        {
            if ((Environment.GetEnvironmentVariable("VARVE_STORAGE_LEGS_REQUIRED") ?? string.Empty).Split(',').Contains("azurite"))
            {
                Assert.Fail("The azurite leg is required here (VARVE_STORAGE_LEGS_REQUIRED) and VARVE_AZURITE is not set.");
            }

            Assert.Skip("The azurite leg needs VARVE_AZURITE (ADR 0134).");
        }

        Uri container = new(account.TrimEnd('/') + "/s" + Guid.NewGuid().ToString("N")[..20]);
        await CreateContainerAsync(container);
        string caches = Directory.CreateTempSubdirectory("varve-server-objects-").FullName;
        string dataset = container + "/d";

        try
        {
            await using RunningServer server = await RunningServer.StartAsync(Settings(container, Path.Combine(caches, "a"), "writer"));

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, "INSERT DATA { <http://ex/s" + i + "> <http://ex/p> " + i + " }")).StatusCode);
                Assert.True((await server.Client.PostAsync(new Uri("manage/datasets/d/checkpoints", UriKind.Relative), null, Ct)).IsSuccessStatusCode);
            }

            // A read of the store itself, announced, and its record gone after.
            (int info, string status) = await CliAsync("info", dataset);
            Assert.Equal(0, info);
            Assert.Contains("\"head\": 4", status, StringComparison.Ordinal);
            (int unannounced, string quiet) = await CliAsync("info", dataset, "--unannounced");
            Assert.Equal(0, unannounced);
            Assert.Contains("\"head\": 4", quiet, StringComparison.Ordinal);

            (int shown, string report) = await CliAsync("storage", dataset);
            Assert.Equal(0, shown);
            Assert.Contains("extent sets per permutation:", report, StringComparison.Ordinal);
            Assert.Contains("  SPOG: ", report, StringComparison.Ordinal);
            Assert.Contains("readers: 1", report, StringComparison.Ordinal);

            (int listed, string readers) = await CliAsync("storage", "readers", "list", dataset);
            Assert.Equal(0, listed);
            string line = readers.Split('\n').Single(l => l.Contains("announced", StringComparison.Ordinal));
            Assert.Contains("reads derived format 5 (maxDerived)", line, StringComparison.Ordinal);
            string id = line.Split("  ")[0];
            string[] versions = line.Split("versions ")[1].Split("  ")[0].Split(" to ");

            Assert.Equal(1, (await CliAsync("storage", "readers", "forget", dataset, "--reader", id, "--oldest", versions[0], "--newest", (long.Parse(versions[1], System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).Exit);
            (int forgot, string done) = await CliAsync("storage", "readers", "forget", dataset, "--reader", id, "--oldest", versions[0], "--newest", versions[1]);
            Assert.Equal(0, forgot);
            Assert.Contains("forgot reader " + id, done, StringComparison.Ordinal);
            Assert.Contains("readers: none", (await CliAsync("storage", "readers", "list", dataset)).Out, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(caches, recursive: true);
        }
    }

    private static async Task<(int Exit, string Out)> CliAsync(params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        MemoryStream data = new();
        int exit = await Varve.Server.Commands.Cli.RunAsync(args, new Varve.Server.Commands.Io(output, error, data), Ct);
        return (exit, output + error.ToString() + Encoding.UTF8.GetString(data.ToArray()));
    }

    private static Dictionary<string, string> Settings(Uri container, string cache, string instance) => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:d:Storage"] = "ObjectStorage",
        ["Varve:ObjectStorage:Provider"] = "Azure",
        ["Varve:ObjectStorage:ContainerUrl"] = container.ToString(),
        ["Varve:ObjectStorage:CachePath"] = cache,
        ["Varve:ObjectStorage:Instance"] = instance,

        // Both claim, as a mistaken second writer does (ADR 0136); the first
        // follows on the minute, so its next write still finds the slot taken.
        ["Varve:Fleet:InternalUrl"] = "http://127.0.0.1:9/" + instance,
        ["Varve:Fleet:FollowInterval"] = "00:01:00",
    };

    private static async Task<(int Exit, string Out)> LeaseAsync(params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        int exit = await Varve.Server.Commands.Cli.RunAsync(["lease", .. args], new Varve.Server.Commands.Io(output, error, new MemoryStream()), Ct);
        return (exit, output + error.ToString());
    }

    private static Task<HttpResponseMessage> UpdateAsync(RunningServer server, string update) =>
        server.Client.PostAsync(new Uri("datasets/d/sparql", UriKind.Relative), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

    private static async Task CreateContainerAsync(Uri container)
    {
        using HttpClient http = new();
        using DevelopmentKeySigner signer = new(TimeProvider.System);
        using HttpRequestMessage create = new(HttpMethod.Put, new Uri(container + "?restype=container"));
        create.Headers.Add("x-ms-version", "2025-11-05");
        await signer.SignAsync(create, Ct);
        using HttpResponseMessage response = await http.SendAsync(create, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
