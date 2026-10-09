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
/// The admin API (ADR 0106) through the real host: create, list, status,
/// settings, checkpoint, close, delete; discovery under the root, a directory
/// that fails to open listed as failed; and readiness reporting it.
/// </summary>
public class AdminTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Open = "{\"state\":\"open\"}";
    private const string Closed = "{\"state\":\"closed\"}";

    [Fact]
    public async Task A_dataset_is_created_listed_administered_closed_and_deleted()
    {
        string root = Directory.CreateTempSubdirectory("varve-admin-").FullName;

        try
        {
            await using RunningServer server = await Start(root, ("Varve:Datasets:m:Storage", "Memory"));

            // Create, and it serves at once.
            Assert.Equal(HttpStatusCode.Created, (await Send(server, HttpMethod.Put, "datasets/people", "{\"storage\":\"File\"}")).StatusCode);

            // The same request again is idempotent; a different body is the conflict (ADR 0118).
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people", "{\"storage\":\"File\"}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await Send(server, HttpMethod.Put, "datasets/people", "{\"storage\":\"Memory\"}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(server, HttpMethod.Put, "datasets/-bad")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "people", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            Assert.True(Directory.Exists(Path.Combine(root, "people", "log")));

            // Listed with the configured one.
            JsonDocument list = await Json(await Send(server, HttpMethod.Get, "datasets"));
            Dictionary<string, JsonElement> byName = [];

            foreach (JsonElement entry in list.RootElement.GetProperty("datasets").EnumerateArray())
            {
                byName[entry.GetProperty("name").GetString()!] = entry;
            }

            Assert.Equal(["m", "people"], byName.Keys);
            Assert.Equal("open", byName["people"].GetProperty("state").GetString());
            Assert.Equal("File", byName["people"].GetProperty("storage").GetString());
            Assert.Equal("created", byName["people"].GetProperty("origin").GetString());
            Assert.Equal(1, byName["people"].GetProperty("head").GetInt64());
            Assert.Equal("configured", byName["m"].GetProperty("origin").GetString());

            // Status carries the projection.
            JsonDocument status = await Json(await Send(server, HttpMethod.Get, "datasets/people/status"));
            Assert.Equal("open", status.RootElement.GetProperty("state").GetString());
            Assert.Equal(1, status.RootElement.GetProperty("projection").GetProperty("position").GetInt64());
            Assert.Equal(0, status.RootElement.GetProperty("projection").GetProperty("lag").GetInt64());
            Assert.False(status.RootElement.GetProperty("projection").GetProperty("failed").GetBoolean());

            // A checkpoint at the head, then one at a position, then a stale If-Match on settings.
            HttpResponseMessage checkpoint = await Send(server, HttpMethod.Post, "datasets/people/checkpoints");
            Assert.Equal(HttpStatusCode.Created, checkpoint.StatusCode);
            Assert.Equal("1", checkpoint.Headers.GetValues("Varve-Position").Single());
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Post, "datasets/people/checkpoints?at=9")).StatusCode);
            status = await Json(await Send(server, HttpMethod.Get, "datasets/people/status"));
            Assert.Equal(1, status.RootElement.GetProperty("checkpoints")[0].GetInt64());

            // The settings resource: read with the position as ETag, then a stale If-Match on a PUT (ADR 0118).
            HttpResponseMessage settings = await Send(server, HttpMethod.Get, "datasets/people/settings");
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
            Assert.Equal("\"1\"", settings.Headers.ETag?.Tag);
            Assert.Equal("AllHistory", (await Json(settings)).RootElement.GetProperty("defaultAccessScope").GetString());

            using HttpRequestMessage stale = new(HttpMethod.Put, new Uri("datasets/people/settings", UriKind.Relative))
            {
                Content = new StringContent("{\"defaultAccessScope\":\"Current\"}", Encoding.UTF8, "application/json"),
            };
            stale.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await server.Client.SendAsync(stale, Ct)).StatusCode);

            // Anonymous mode names no agent, and a settings commit needs one (ADR 0094).
            HttpResponseMessage anonymous = await Send(server, HttpMethod.Put, "datasets/people/settings", "{\"defaultAccessScope\":\"Current\"}");
            Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
            Assert.Contains("agent-required", await anonymous.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

            // Delete refuses an open dataset; close, then delete, and the directory is gone.
            Assert.Equal(HttpStatusCode.Conflict, (await Send(server, HttpMethod.Delete, "datasets/people")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Get, "datasets/people/status")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people/state", Closed)).StatusCode);
            Assert.Equal("closed", (await Json(await Send(server, HttpMethod.Get, "datasets/people/state"))).RootElement.GetProperty("state").GetString());
            list = await Json(await Send(server, HttpMethod.Get, "datasets"));
            Assert.Contains(list.RootElement.GetProperty("datasets").EnumerateArray(), e => e.GetProperty("name").GetString() == "people" && e.GetProperty("state").GetString() == "closed");

            // Reopened, it serves what it held.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people/state", Open)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people/state", Open)).StatusCode);
            Assert.Equal("open", (await Json(await Send(server, HttpMethod.Get, "datasets/people/state"))).RootElement.GetProperty("state").GetString());
            status = await Json(await Send(server, HttpMethod.Get, "datasets/people/status"));
            Assert.Equal(1, status.RootElement.GetProperty("head").GetInt64());

            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/people/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Delete, "datasets/people")).StatusCode);
            Assert.False(Directory.Exists(Path.Combine(root, "people")));
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Delete, "datasets/people")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Put, "datasets/people/state", Open)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(server, HttpMethod.Get, "datasets/people/state")).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_directory_under_the_root_is_served_after_a_restart_and_one_that_fails_to_open_is_listed_failed()
    {
        string root = Directory.CreateTempSubdirectory("varve-admin-").FullName;

        try
        {
            await using (RunningServer first = await Start(root))
            {
                Assert.Equal(HttpStatusCode.Created, (await Send(first, HttpMethod.Put, "datasets/kept")).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await Update(first, "kept", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            }

            // A second directory, held open by another process's lease: ours.
            string held = Path.Combine(root, "held");
            FileStorage holder = await FileStorage.OpenAsync(new DatasetDirectory(held), new FileStorageOptions { Clock = TimeProvider.System }, Ct);
            await using Dataset holding = await Dataset.CreateAsync(holder, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, Ct);

            await using (RunningServer second = await Start(root))
            {
                JsonDocument list = await Json(await Send(second, HttpMethod.Get, "datasets"));
                Dictionary<string, JsonElement> byName = [];

                foreach (JsonElement entry in list.RootElement.GetProperty("datasets").EnumerateArray())
                {
                    byName[entry.GetProperty("name").GetString()!] = entry;
                }

                Assert.Equal("open", byName["kept"].GetProperty("state").GetString());
                Assert.Equal("discovered", byName["kept"].GetProperty("origin").GetString());
                Assert.Equal(1, byName["kept"].GetProperty("head").GetInt64());
                Assert.Equal("failed", byName["held"].GetProperty("state").GetString());
                Assert.Contains("held", byName["held"].GetProperty("reason").GetString(), StringComparison.Ordinal);

                // Readiness is a not-ready problem listing the dataset that fails and why (ADR 0113).
                HttpResponseMessage ready = await Send(second, HttpMethod.Get, "health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                Assert.Equal("application/problem+json", ready.Content.Headers.ContentType?.MediaType);
                JsonDocument readiness = await Json(ready);
                Assert.Equal("https://w3id.org/varve/problems/not-ready", readiness.RootElement.GetProperty("type").GetString());
                JsonElement failing = Assert.Single(readiness.RootElement.GetProperty("datasets").EnumerateArray());
                Assert.Equal("held", failing.GetProperty("name").GetString());
                Assert.Equal("failed", failing.GetProperty("state").GetString());
                Assert.Contains("held", failing.GetProperty("reason").GetString(), StringComparison.Ordinal);

                // Released by its holder, it opens on request.
                await holding.DisposeAsync();
                await holder.DisposeAsync();
                Assert.Equal(HttpStatusCode.NoContent, (await Send(second, HttpMethod.Put, "datasets/held/state", Open)).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await Send(second, HttpMethod.Get, "health/ready")).StatusCode);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_server_with_no_dataset_but_a_root_starts_and_a_memory_dataset_cannot_be_created_without_one()
    {
        string root = Directory.CreateTempSubdirectory("varve-admin-").FullName;

        try
        {
            await using RunningServer server = await Start(root);
            JsonDocument list = await Json(await Send(server, HttpMethod.Get, "datasets"));
            Assert.Empty(list.RootElement.GetProperty("datasets").EnumerateArray());
            Assert.Equal(HttpStatusCode.Created, (await Send(server, HttpMethod.Put, "datasets/scratch", "{\"storage\":\"Memory\"}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "scratch", "INSERT DATA { <http://ex/s> <http://ex/p> \"1\" }")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "datasets/scratch/state", Closed)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Delete, "datasets/scratch")).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
