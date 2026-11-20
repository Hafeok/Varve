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
/// The operator guide's cold copy and restore (docs/operator/backup-and-restore.md,
/// #129): a file dataset declared closed, its directory copied — <c>log/</c>
/// alone, and <c>log/</c> with <c>derived/</c> — and declared open; then each
/// copy put back under the running server in place of the dataset, which has
/// moved on, and declared open again. The restored dataset has the source's
/// id and head, and answers at every position as the source did.
/// </summary>
public sealed class BackupRestoreTests : IDisposable
{
    private const string Open = "{\"state\":\"open\"}";
    private const string Closed = "{\"state\":\"closed\"}";
    private const string Everything = "SELECT ?g ?s ?p ?o WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } }";

    private static readonly string[] Updates =
    [
        "INSERT DATA { <http://ex/a> <http://ex/p> \"1\" }",
        "INSERT DATA { GRAPH <http://ex/g> { <http://ex/b> <http://ex/p> <http://ex/c> } }",
        "DELETE DATA { <http://ex/a> <http://ex/p> \"1\" } ; INSERT DATA { <http://ex/a> <http://ex/p> \"2\" }",
        "INSERT DATA { GRAPH <http://ex/h> { <http://ex/d> <http://ex/q> \"é\"@fr } }",
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("varve-backup-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_cold_copy_restores_the_dataset_id_and_every_position_from_the_log_alone_and_with_derived()
    {
        string data = Path.Combine(_root, "data");
        string live = Path.Combine(data, "people");
        string logOnly = Path.Combine(_root, "backup-log");
        string withDerived = Path.Combine(_root, "backup-all");

        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
        {
            ["Varve:Auth:Mode"] = "Anonymous",
            ["Varve:DatasetsRoot"] = data,
        });
        Assert.Equal(HttpStatusCode.Created, (await Trig(server, "manage/datasets/people", ManagementTests.Declaration("people", "File"))).StatusCode);

        // Four commits, a checkpoint at 2 so derived/ holds an extent set and a catalog.
        for (int i = 0; i < Updates.Length; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, Updates[i])).StatusCode);

            if (i == 1)
            {
                Assert.Equal(HttpStatusCode.Created, (await Send(server, HttpMethod.Post, "manage/datasets/people/checkpoints")).StatusCode);
            }
        }

        Dictionary<long, string> expected = [];

        for (long position = 1; position <= Updates.Length; position++)
        {
            expected[position] = await AnswerAsync(server, position);
        }

        Assert.Equal(4, expected.Values.Distinct(StringComparer.Ordinal).Count());

        // Cold: declared closed, the directory is a position.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
        (Guid id, long head) = await IdAndHeadAsync(live);
        Assert.Equal(Updates.Length, head);
        Assert.True(Directory.Exists(Path.Combine(live, "derived", "extents")) && Directory.EnumerateFiles(Path.Combine(live, "derived", "extents"), "*", SearchOption.AllDirectories).Any() && File.Exists(Path.Combine(live, "derived", "catalog")), "the checkpoint wrote no extent set");
        Copy(Path.Combine(live, "log"), Path.Combine(logOnly, "log"));
        Copy(live, withDerived);

        // Declared open again, the source moves on past the copy.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Open)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "INSERT DATA { <http://ex/later> <http://ex/p> \"5\" }")).StatusCode);
        Assert.Equal(5, await HeadAsync(server));

        foreach (string backup in (string[])[logOnly, withDerived])
        {
            // Restore under the running server: closed, replaced, open.
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
            Directory.Delete(live, recursive: true);
            Copy(backup, live);
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Open)).StatusCode);

            Assert.Equal(Updates.Length, await HeadAsync(server));

            for (long position = 1; position <= Updates.Length; position++)
            {
                Assert.Equal(expected[position], await AnswerAsync(server, position));
            }

            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Closed)).StatusCode);
            Assert.Equal((id, (long)Updates.Length), await IdAndHeadAsync(live));
            Assert.Equal(HttpStatusCode.NoContent, (await Send(server, HttpMethod.Put, "manage/datasets/people/state", Open)).StatusCode);

            // The restored dataset takes writes after the copy's head.
            Assert.Equal(HttpStatusCode.NoContent, (await Update(server, "INSERT DATA { <http://ex/after> <http://ex/p> \"5\" }")).StatusCode);
            Assert.Equal(Updates.Length + 1, await HeadAsync(server));
        }
    }

    /// <summary>The dataset's id and head, read from its directory as <c>varve info</c> does.</summary>
    private static async Task<(Guid Id, long Head)> IdAndHeadAsync(string directory)
    {
        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }, Ct);
        await using Dataset dataset = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, Ct);
        return (dataset.Id.Value, dataset.Head.Value);
    }

    /// <summary>A recursive copy, as <c>cp -a</c> makes it.</summary>
    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (string child in Directory.EnumerateDirectories(from))
        {
            Copy(child, Path.Combine(to, Path.GetFileName(child)));
        }
    }

    private static async Task<long> HeadAsync(RunningServer server)
    {
        using HttpResponseMessage response = await Send(server, HttpMethod.Get, "manage/datasets/people/status");
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, response.StatusCode + ": " + body);
        using JsonDocument status = JsonDocument.Parse(body);
        return status.RootElement.GetProperty("head").GetInt64();
    }

    /// <summary>Every quad at <paramref name="position"/>, as sorted CSV rows.</summary>
    private static async Task<string> AnswerAsync(RunningServer server, long position)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/people/sparql", UriKind.Relative))
        {
            Content = new StringContent(Everything, Encoding.UTF8, "application/sparql-query"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "text/csv");
        request.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, response.StatusCode + ": " + body);
        string[] rows = body.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        return rows[0] + "\n" + string.Join('\n', rows.Skip(1).Order(StringComparer.Ordinal));
    }

    private static async Task<HttpResponseMessage> Trig(RunningServer server, string path, string trig)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(path, UriKind.Relative)) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };
        return await server.Client.SendAsync(request, Ct);
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

    private static async Task<HttpResponseMessage> Update(RunningServer server, string update)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri("datasets/people/sparql", UriKind.Relative))
        {
            Content = new StringContent(update, Encoding.UTF8, "application/sparql-update"),
        };
        return await server.Client.SendAsync(request, Ct);
    }
}
