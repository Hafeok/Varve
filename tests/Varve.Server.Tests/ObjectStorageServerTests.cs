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
        settings.Datasets["d"] = new DatasetSettings { Storage = "ObjectStorage" };
        Assert.Contains(SettingsCheck.Errors(settings), e => e.StartsWith("Varve:Datasets:d is an ObjectStorage dataset, and Varve:ObjectStorage:Provider is not set.", StringComparison.Ordinal));

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

    private static Dictionary<string, string> Settings(Uri container, string cache, string instance) => new()
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:d:Storage"] = "ObjectStorage",
        ["Varve:ObjectStorage:Provider"] = "Azure",
        ["Varve:ObjectStorage:ContainerUrl"] = container.ToString(),
        ["Varve:ObjectStorage:CachePath"] = cache,
        ["Varve:ObjectStorage:Instance"] = instance,
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
