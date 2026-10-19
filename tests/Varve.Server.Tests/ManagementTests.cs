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
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The management plane (ADR 0163) through the real host: a dataset declared,
/// listed, administered, declared closed, reopened and deleted; a declared
/// dataset served again after a restart, and one that fails to open listed as
/// failed, in readiness too, and opened once its holder lets go; the settings
/// converged by the writer.
/// </summary>
public class ManagementTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Open = "{\"state\":\"open\"}";
    private const string Closed = "{\"state\":\"closed\"}";

    [Fact]
    public async Task A_dataset_is_declared_listed_administered_closed_and_deleted()
    {
        string root = Directory.CreateTempSubdirectory("varve-manage-").FullName;

        try
        {
            await using RunningServer server = await Start(root, ("Varve:Datasets:m:Storage", "Memory"));

            // Declared, and it serves at once.
            Assert.Equal(HttpStatusCode.Created, (await Declare(server, "people", "File")).StatusCode);

            // The same declaration again commits nothing; an invalid one is refused, naming its finding.
            Assert.Equal(HttpStatusCode.NoContent, (await Declare(server, "people", "File")).StatusCode);
            HttpResponseMessage invalid = await Trig(server, HttpMethod.Put, "manage/datasets/people", "<urn:people> <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#Tape> .");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
            Assert.Contains("declaration-invalid", await invalid.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.BadRequest, (await Declare(server, "-bad", "File")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            Assert.True(Directory.Exists(Path.Combine(root, "people", "log")));

            // Listed with the other, each declared, written here.
            JsonDocument list = await Json(await Send(server, HttpMethod.Get, "manage/datasets"));
            Dictionary<string, JsonElement> byName = [];

            foreach (JsonElement entry in list.RootElement.GetProperty("datasets").EnumerateArray())
            {
                byName[entry.GetProperty("name").GetString()!] = entry;
            }

            Assert.Equal(["m", "people"], byName.Keys);
            Assert.Equal("open", byName["people"].GetProperty("state").GetString());
            Assert.Equal("File", byName["people"].GetProperty("storage").GetString());
            Assert.Equal("declared", byName["people"].GetProperty("origin").GetString());
            Assert.Equal("writer", byName["people"].GetProperty("role").GetString());
            Assert.Equal(1, byName["people"].GetProperty("head").GetInt64());

            // The declaration reads back as TriG, its graph's version the ETag.
            HttpResponseMessage declared = await Send(server, HttpMethod.Get, "manage/datasets/people");
            Assert.Equal("application/trig", declared.Content.Headers.ContentType?.MediaType);
            Assert.Contains("\"people\"", await declared.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
            string version = declared.Headers.ETag!.Tag;

            // Status carries the projection.
            JsonDocument status = await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/status"));
            Assert.Equal("open", status.RootElement.GetProperty("state").GetString());
            Assert.Equal(1, status.RootElement.GetProperty("projection").GetProperty("position").GetInt64());
            Assert.Equal(0, status.RootElement.GetProperty("projection").GetProperty("lag").GetInt64());

            // A checkpoint at the head, then one at a position the dataset has not reached.
            HttpResponseMessage checkpoint = await Send(server, HttpMethod.Post, "manage/datasets/people/checkpoints");
            Assert.Equal(HttpStatusCode.Created, checkpoint.StatusCode);
            Assert.Equal("1", checkpoint.Headers.GetValues("Varve-Position").Single());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Post, "manage/datasets/people/checkpoints?at=9")).StatusCode);

            // The settings: declared, read with the graph's version, refused
            // under a stale If-Match, and converged by the writer: a Settings
            // commit at the dataset's position 2 (ADR 0163 point 10).
            HttpResponseMessage settings = await Send(server, HttpMethod.Get, "manage/datasets/people/settings");
            Assert.Equal(version, settings.Headers.ETag?.Tag);
            Assert.Equal("AllHistory", (await Json(settings)).RootElement.GetProperty("defaultAccessScope").GetString());
            const string Current = "<urn:varve:from-config:people> <https://w3id.org/varve/ns#defaultAccessScope> <https://w3id.org/varve/ns#Current> .";
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Trig(server, HttpMethod.Put, "manage/datasets/people/settings", Current, ifMatch: "\"0\"")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Trig(server, HttpMethod.Put, "manage/datasets/people/settings", Current, ifMatch: version)).StatusCode);
            Assert.Equal("Current", (await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/settings"))).RootElement.GetProperty("defaultAccessScope").GetString());
            status = await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/status"));
            Assert.Equal("Current", status.RootElement.GetProperty("settings").GetProperty("defaultAccessScope").GetString());
            Assert.Equal(2, status.RootElement.GetProperty("head").GetInt64());

            // Delete refuses a dataset declared open; declared closed, it closes everywhere.
            Assert.Equal(HttpStatusCode.Conflict, (await Send(server, HttpMethod.Delete, "manage/datasets/people")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Get, "manage/datasets/people/status")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
            Assert.Equal("closed", (await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/state"))).RootElement.GetProperty("state").GetString());

            // Declared open again, it serves what it held.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Open)).StatusCode);
            Assert.Equal("open", (await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/state"))).RootElement.GetProperty("state").GetString());
            status = await Json(await Send(server, HttpMethod.Get, "manage/datasets/people/status"));
            Assert.Equal(2, status.RootElement.GetProperty("head").GetInt64());

            // Closed, then deleted: the declaration is gone, and the writer deleted the directory.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Delete, "manage/datasets/people")).StatusCode);
            Assert.False(Directory.Exists(Path.Combine(root, "people")));
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Delete, "manage/datasets/people")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Open)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Get, "manage/datasets/people/state")).StatusCode);

            // Every declaration is a commit of the system dataset, at a position the management plane names.
            JsonDocument system = await Json(await Send(server, HttpMethod.Get, "manage/system"));
            Assert.Equal("writer", system.RootElement.GetProperty("role").GetString());
            Assert.True(system.RootElement.GetProperty("position").GetInt64() >= 6);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_declared_dataset_is_served_after_a_restart_and_one_that_fails_to_open_is_listed_failed()
    {
        string root = Directory.CreateTempSubdirectory("varve-manage-").FullName;

        try
        {
            await using (RunningServer first = await Start(root))
            {
                Assert.Equal(HttpStatusCode.Created, (await Declare(first, "kept", "File")).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await Update(first, "kept", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            }

            // A directory held open by another process's lease: ours.
            string held = Path.Combine(root, "held");
            FileStorage holder = await FileStorage.OpenAsync(new DatasetDirectory(held), new FileStorageOptions { Clock = TimeProvider.System }, Ct);
            Dataset holding = await Dataset.CreateAsync(holder, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, Ct);

            try
            {
                // The lease is waited for one second (ADR 0116), then the dataset is listed failed with its holder.
                await using RunningServer second = await Start(root, ("Varve:Lease:WaitFor", "00:00:01"));
                Assert.Equal(HttpStatusCode.Created, (await Declare(second, "held", "File")).StatusCode);
                JsonDocument list = await Json(await Send(second, HttpMethod.Get, "manage/datasets"));
                Dictionary<string, JsonElement> byName = [];

                foreach (JsonElement entry in list.RootElement.GetProperty("datasets").EnumerateArray())
                {
                    byName[entry.GetProperty("name").GetString()!] = entry;
                }

                Assert.Equal("open", byName["kept"].GetProperty("state").GetString());
                Assert.Equal(1, byName["kept"].GetProperty("head").GetInt64());
                Assert.Equal("failed", byName["held"].GetProperty("state").GetString());
                Assert.Contains("held", byName["held"].GetProperty("reason").GetString(), StringComparison.Ordinal);

                // Readiness is a not-ready problem listing the dataset that fails and why (ADR 0113).
                HttpResponseMessage ready = await Send(second, HttpMethod.Get, "health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                JsonDocument readiness = await Json(ready);
                Assert.Equal("https://w3id.org/varve/problems/not-ready", readiness.RootElement.GetProperty("type").GetString());
                JsonElement failing = Assert.Single(readiness.RootElement.GetProperty("datasets").EnumerateArray());
                Assert.Equal("held", failing.GetProperty("name").GetString());
                Assert.Equal("failed", failing.GetProperty("state").GetString());

                // Released by its holder, it opens on the fleet's next round.
                await holding.DisposeAsync();
                await holder.DisposeAsync();
                HttpStatusCode state = HttpStatusCode.ServiceUnavailable;

                for (int attempt = 0; attempt < 100 && state != HttpStatusCode.OK; attempt++)
                {
                    await Task.Delay(100, Ct);
                    state = (await Send(second, HttpMethod.Get, "health/ready")).StatusCode;
                }

                Assert.Equal(HttpStatusCode.OK, state);
            }
            finally
            {
                await holding.DisposeAsync();
                await holder.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_server_with_only_a_root_starts_with_no_dataset_and_a_memory_dataset_is_declared_and_deleted()
    {
        string root = Directory.CreateTempSubdirectory("varve-manage-").FullName;

        try
        {
            await using RunningServer server = await Start(root);
            JsonDocument list = await Json(await Send(server, HttpMethod.Get, "manage/datasets"));
            Assert.Empty(list.RootElement.GetProperty("datasets").EnumerateArray());
            Assert.Equal(HttpStatusCode.Created, (await Declare(server, "scratch", "Memory")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "scratch", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/scratch/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Delete, "manage/datasets/scratch")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Update(server, "scratch", "INSERT DATA { <http://ex/s> <http://ex/p> \"2\" }")).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A dataset's declaration, as TriG in its graph.</summary>
    internal static string Declaration(string name, string storage) =>
        "<urn:varve:from-config:" + name + "> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"" + name + "\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#" + storage + "> .";

    private static Task<HttpResponseMessage> Declare(RunningServer server, string name, string storage) =>
        Trig(server, HttpMethod.Put, "manage/datasets/" + name, Declaration(name, storage));

    private static async Task<HttpResponseMessage> Trig(RunningServer server, HttpMethod method, string path, string trig, string? ifMatch = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await server.Client.SendAsync(request, Ct);
    }

    private static Task<RunningServer> Start(string root, params (string Key, string Value)[] more)
    {
        Dictionary<string, string> settings = new()
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:DatasetsRoot"] = root,
        };

        foreach ((string key, string value) in more)
        {
            settings[key] = value;
        }

        return RunningServer.StartAsync(settings);
    }

    private static async Task<HttpResponseMessage> Send(RunningServer server, HttpMethod method, string path, string? json = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await server.Client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> Update(RunningServer server, string dataset, string update)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/" + dataset + "/sparql", UriKind.Relative))
        {
            Content = new StringContent(update, Encoding.UTF8, "application/sparql-update"),
        };
        return await server.Client.SendAsync(request, Ct);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.ServiceUnavailable, response.StatusCode + ": " + body);
        return JsonDocument.Parse(body);
    }
}
