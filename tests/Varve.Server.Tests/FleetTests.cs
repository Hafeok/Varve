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
using Varve.Store.Azure;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// A fleet of two server processes on one Azurite container (ADRs 0163,
/// 0164, 0168): the system writer and a follower that forwards. Writes and
/// management writes through the follower are forwarded and answered by the
/// writer, and read back from the follower at the position they returned; an
/// idle followed dataset makes no request to the store; the data plane keeps
/// serving with the system writer killed; a mistaken second system writer
/// shows in readiness and in the epoch history; and a placement change
/// moves the writer with writes in flight, losing none that were answered.
/// The fleet category, which the release gate may run apart (#129).
/// </summary>
[Trait("Category", "Fleet")]
public sealed class FleetTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _caches = Directory.CreateTempSubdirectory("varve-fleet-").FullName;
    private readonly List<Server> _servers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (Server server in _servers)
        {
            await server.DisposeAsync();
        }

        Directory.Delete(_caches, recursive: true);
    }

    [Fact]
    public async Task Writes_through_the_follower_are_forwarded_and_an_idle_dataset_makes_no_requests()
    {
        Uri container = await ContainerAsync();
        await using CountingProxy proxy = await CountingProxy.StartAsync(container);
        Server a = await StartAsync("a", container);
        long declared = await DeclareAsync(a, "d");
        declared = Math.Max(declared, await DeclareAsync(a, "e"));
        Server b = await StartAsync("b", proxy.Container(container));
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
        Uri container = await ContainerAsync();
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
        Uri container = await ContainerAsync();
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
        Assert.Equal(0, await Varve.Server.Commands.Cli.RunAsync(["lease", container + "/_fleet"], new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct));
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
        Uri container = await ContainerAsync();
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
        await Varve.Server.Commands.Cli.RunAsync(["lease", container + "/d"], new Varve.Server.Commands.Io(output, new StringWriter(), new MemoryStream()), Ct);
        Assert.Contains("placement", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("holder: b", output.ToString(), StringComparison.Ordinal);
    }

    // ---- the fleet ----

    private async Task<Server> StartAsync(string instance, Uri container, string writer = "a")
    {
        int port = FreePort();
        Server server = Server.Start(instance, port, container, Path.Combine(_caches, instance), writer);
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

    private static async Task WaitReadyAsync(Server server, Server other, Uri container)
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
        await Varve.Server.Commands.Cli.RunAsync(["lease", container + "/d"], new Varve.Server.Commands.Io(lease, new StringWriter(), new MemoryStream()), Ct);
        Assert.Fail(server.Instance + " did not become ready: " + last + "\nd's writer record:\n" + lease + "\n" + server.Instance + ":\n" + server.Log + "\n" + other.Instance + ":\n" + other.Log);
    }

    private static Task<HttpResponseMessage> Update(Server server, string dataset, string update) =>
        server.Http.PostAsync(new Uri(server.Address, "datasets/" + dataset + "/sparql"), new StringContent(update, Encoding.UTF8, "application/sparql-update"), Ct);

    private static async Task<Uri> ContainerAsync()
    {
        string account = Environment.GetEnvironmentVariable("VARVE_AZURITE") ?? string.Empty;

        if (account.Length == 0)
        {
            if ((Environment.GetEnvironmentVariable("VARVE_STORAGE_LEGS_REQUIRED") ?? string.Empty).Split(',').Contains("azurite"))
            {
                Assert.Fail("The azurite leg is required here (VARVE_STORAGE_LEGS_REQUIRED) and VARVE_AZURITE is not set.");
            }

            Assert.Skip("The fleet runs on Azurite and needs VARVE_AZURITE (ADR 0163).");
        }

        Uri container = new(account.TrimEnd('/') + "/f" + Guid.NewGuid().ToString("N")[..20]);
        using HttpClient http = new();
        using DevelopmentKeySigner signer = new(TimeProvider.System);
        using HttpRequestMessage create = new(HttpMethod.Put, new Uri(container + "?restype=container"));
        create.Headers.Add("x-ms-version", "2025-11-05");
        await signer.SignAsync(create, Ct);
        using HttpResponseMessage response = await http.SendAsync(create, Ct);
        Assert.True(response.IsSuccessStatusCode, "The container was not created: " + response.StatusCode);
        return container;
    }

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

        internal static Server Start(string instance, int port, Uri container, string cache, string writer)
        {
            string address = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

            foreach (string argument in (string[])[
                Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"),
                "--urls=" + address,
                "--Logging:LogLevel:Default=Warning",
                "--Varve:Auth:Mode=Anonymous",
                "--Varve:ObjectStorage:Provider=Azure",
                "--Varve:ObjectStorage:ContainerUrl=" + container,
                "--Varve:ObjectStorage:CachePath=" + cache,
                "--Varve:ObjectStorage:Instance=" + instance,
                "--Varve:Fleet:Writer=" + writer,
                "--Varve:Fleet:InternalUrl=" + address,
                "--Varve:Fleet:FollowInterval=00:00:00.200",
            ])
            {
                start.ArgumentList.Add(argument);
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
    /// A loopback proxy in front of Azurite that counts the requests it
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

        internal static async Task<CountingProxy> StartAsync(Uri container)
        {
            Uri azurite = new(container.GetLeftPart(UriPartial.Authority) + "/");
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
                using HttpRequestMessage forward = new(new HttpMethod(request.Method), new Uri(azurite, request.Path.Value!.TrimStart('/') + request.QueryString.Value));
                MemoryStream body = new();
                await request.Body.CopyToAsync(body, context.RequestAborted);

                if (body.Length > 0 || request.ContentLength is not null)
                {
                    forward.Content = new ByteArrayContent(body.ToArray());
                }

                foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in request.Headers)
                {
                    if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    {
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

        /// <summary>The container's address through this proxy.</summary>
        internal Uri Container(Uri container) => new(Address, container.AbsolutePath.TrimStart('/'));

        internal void Reset() => _seen.Clear();

        /// <summary>The requests whose path holds <paramref name="fragment"/>.</summary>
        internal int Count(string fragment) => _seen.Count(request => request.Path.Contains(fragment, StringComparison.Ordinal));

        /// <summary>The LISTs under a prefix holding <paramref name="fragment"/>.</summary>
        internal int Lists(string fragment) => _seen.Count(request =>
            request.Query.Contains("comp=list", StringComparison.Ordinal) && Uri.UnescapeDataString(request.Query).Contains(fragment.Trim('/'), StringComparison.Ordinal));

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
