// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// A binding's memory limit in a real process (#335; ADR 0195 point 6, as
/// amended 2026-11-18): the server runs as a child process under a hard heap
/// limit, <c>DOTNET_GCHeapHardLimit</c>, so that a closure the budget lets
/// outgrow the heap kills it.
/// </summary>
/// <remarks>
/// <para>
/// The data is a chain of <see cref="Depth"/> classes in a TBox graph and
/// individuals typed at its foot: a little to upload, a closure of about
/// <see cref="Depth"/> facts an individual.
/// </para>
/// <para>
/// <strong>The gate:</strong> a closure past its limit is refused, the
/// process stays up, a second dataset's binding keeps answering, and the
/// refused binding stays refused after a TBox commit. <strong>The
/// budget's figures:</strong> a build whose peak by the budget's count is
/// just under its limit, with the heap the least the settings allow for that
/// limit, builds; were a build to need more than the budget counts, the heap
/// would run out first and the process die.
/// </para>
/// </remarks>
public class ClosureMemoryGateTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const int Depth = 40;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_closure_too_large_is_refused_and_the_process_and_another_dataset_keep_answering()
    {
        // A 512 MiB heap; the most ClosureBytes the settings allow there, the whole pool.
        long heap = 512L << 20;
        await using ChildServer server = await ChildServer.StartAsync(heap, closureBytes: ChildServer.Pool(heap));

        await server.DeclareAsync("big", "RDFS", crossGraph: false);
        await server.DeclareAsync("small", "RDFS", crossGraph: false);
        await server.LoadAsync("big", individuals: 40_000, graphs: 1);
        await server.LoadAsync("small", individuals: 10, graphs: 1);

        JsonElement big = await server.SettledAsync("big");
        Assert.Equal("stopped (memory-limit)", big.GetProperty("state").GetString());
        Assert.True(big.GetProperty("reached").GetInt64() > big.GetProperty("limit").GetInt64());
        Assert.False(server.Exited, server.Log);

        // The refused binding answers 503 inference-unavailable, not retryable.
        HttpResponseMessage refused = await server.QueryAsync("big", "SELECT (COUNT(*) AS ?n) FROM <http://ex/g/0> FROM <" + Varve + "implicit> WHERE { ?s ?p ?o }");
        string body = await refused.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Contains("inference-unavailable", body, StringComparison.Ordinal);
        Assert.Contains("memory-limit", body, StringComparison.Ordinal);
        Assert.False(refused.Headers.Contains("Retry-After"), body);

        // The other dataset, its binding's closure included, keeps answering.
        JsonElement small = await server.SettledAsync("small");
        Assert.Equal("following", small.GetProperty("state").GetString());
        HttpResponseMessage answered = await server.QueryAsync("small", "SELECT ?c FROM <http://ex/g/0> FROM <" + Varve + "implicit> WHERE { <http://ex/x0> a ?c }");
        string answer = await answered.Content.ReadAsStringAsync(Ct);
        Assert.True(answered.StatusCode == HttpStatusCode.OK, answer);
        Assert.Contains("http://ex/C" + Depth.ToString(CultureInfo.InvariantCulture), answer, StringComparison.Ordinal);

        // A TBox commit does not try the refused binding again.
        await server.UpdateAsync("big", "INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/C" + Depth.ToString(CultureInfo.InvariantCulture) + "> <" + Rdfs + "subClassOf> <http://ex/Top> } }");
        await Task.Delay(1000, Ct);
        Assert.Equal("stopped (memory-limit)", (await server.SettledAsync("big")).GetProperty("state").GetString());
        Assert.False(server.Exited, server.Log);
    }

    [Theory]
    [InlineData("RDFS", false)]
    [InlineData("OWL2RL", false)]
    [InlineData("RDFS", true)]
    [InlineData("OWL2RL", true)]
    public async Task A_build_the_budget_admits_fits_the_heap_the_settings_allow_for_it(string profile, bool crossGraph)
    {
        const int individuals = 12_000;
        const int graphs = 3;

        // First what the build needs by the budget's count, on a heap with room to spare.
        long peak;
        await using (ChildServer probe = await ChildServer.StartAsync(heap: 2L << 30, closureBytes: 1L << 30))
        {
            await probe.DeclareAsync("d", profile, crossGraph);
            await probe.LoadAsync("d", individuals, graphs);
            JsonElement settled = await probe.SettledAsync("d");
            Assert.Equal("following", settled.GetProperty("state").GetString());
            peak = settled.GetProperty("peak").GetInt64();
            Assert.True(peak > 0);
        }

        // Then a limit a tenth over that peak, on the least heap the settings allow for it:
        // the build must finish. Needing more than the budget counts, it would exhaust the heap.
        long closureBytes = peak + (peak / 10);
        long heap = ChildServer.HeapFor(closureBytes);
        await using ChildServer server = await ChildServer.StartAsync(heap, closureBytes);
        await server.DeclareAsync("d", profile, crossGraph);
        await server.LoadAsync("d", individuals, graphs);
        JsonElement built = await server.SettledAsync("d");
        Assert.False(server.Exited, server.Log);
        Assert.Equal("following", built.GetProperty("state").GetString());
        HttpResponseMessage answered = await server.QueryAsync("d", "SELECT ?c FROM <http://ex/g/0> FROM <" + Varve + "implicit> WHERE { <http://ex/x0> a ?c }");
        Assert.True(answered.StatusCode == HttpStatusCode.OK, await answered.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// A server that declares no closure binding starts at any heap limit,
    /// whatever its pool (ADR 0195, amended 2026-11-18, point 10): with the
    /// default 2 GiB reserve, the pool is empty under 2.67 GiB and something
    /// above it, and the server starts and answers either way.
    /// </summary>
    [Theory]
    [InlineData(128L << 20)]
    [InlineData(512L << 20)]
    [InlineData(2L << 30)]
    [InlineData(4L << 30)]
    public async Task A_server_with_no_closure_binding_starts_at_any_heap_limit(long heap)
    {
        await using ChildServer server = await ChildServer.StartWithAsync(heap, []);
        await server.DeclarePlainAsync("plain");
        await server.UpdateAsync("plain", "INSERT DATA { GRAPH <http://ex/g/0> { <http://ex/a> <http://ex/p> <http://ex/b> } }");
        HttpResponseMessage answered = await server.QueryAsync("plain", "SELECT (COUNT(*) AS ?n) FROM <http://ex/g/0> WHERE { ?s ?p ?o }");
        Assert.True(answered.StatusCode == HttpStatusCode.OK, await answered.Content.ReadAsStringAsync(Ct) + server.Log);
        HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);
        Assert.True(ready.StatusCode == HttpStatusCode.OK, await ready.Content.ReadAsStringAsync(Ct));
        Assert.False(server.Exited, server.Log);
    }

    /// <summary>
    /// A server whose pool is empty and that declares a closure binding
    /// (ADR 0195, amended 2026-11-18, point 10): it starts, and the binding
    /// is stopped at its memory limit as it is declared, before it builds,
    /// with a limit of 0. A closure read answers <c>503
    /// inference-unavailable</c>, no <c>Retry-After</c>, its detail naming
    /// <c>Varve:Inference:ReserveBytes</c> and the heap limit; the log says
    /// the same; another dataset keeps answering, and a commit does not try
    /// the binding again.
    /// </summary>
    [Fact]
    public async Task A_closure_binding_with_no_pool_is_stopped_at_its_memory_limit_naming_the_reserve_and_the_heap_limit()
    {
        long heap = 1L << 30;
        await using ChildServer server = await ChildServer.StartWithAsync(heap, []);
        await server.DeclareAsync("d", "RDFS", crossGraph: false);
        await server.DeclarePlainAsync("plain");

        JsonElement refused = await server.SettledAsync("d");
        Assert.Equal("stopped (memory-limit)", refused.GetProperty("state").GetString());
        Assert.Equal(0, refused.GetProperty("limit").GetInt64());

        await server.LoadAsync("d", individuals: 10, graphs: 1);
        HttpResponseMessage read = await server.QueryAsync("d", "SELECT ?c FROM <http://ex/g/0> FROM <" + Varve + "implicit> WHERE { <http://ex/x0> a ?c }");
        string body = await read.Content.ReadAsStringAsync(Ct);
        Assert.True(read.StatusCode == HttpStatusCode.ServiceUnavailable, body);
        Assert.True(body.Contains("problems/inference-unavailable", StringComparison.Ordinal), body);
        Assert.Contains("Varve:Inference:ReserveBytes", body, StringComparison.Ordinal);
        Assert.Contains("heap limit, " + heap.ToString(CultureInfo.InvariantCulture) + " bytes", body, StringComparison.Ordinal);
        Assert.Null(read.Headers.RetryAfter);
        Assert.Contains("Varve:Inference:ReserveBytes", server.Log, StringComparison.Ordinal);

        Assert.Equal("stopped (memory-limit)", (await server.SettledAsync("d")).GetProperty("state").GetString());
        await server.UpdateAsync("plain", "INSERT DATA { GRAPH <http://ex/g/0> { <http://ex/a> <http://ex/p> <http://ex/b> } }");
        HttpResponseMessage other = await server.QueryAsync("plain", "SELECT (COUNT(*) AS ?n) FROM <http://ex/g/0> WHERE { ?s ?p ?o }");
        Assert.True(other.StatusCode == HttpStatusCode.OK, await other.Content.ReadAsStringAsync(Ct));
        Assert.False(server.Exited, server.Log);
    }

    /// <summary>The server as a child process under a hard heap limit, its datasets in memory.</summary>
    private sealed class ChildServer : IAsyncDisposable
    {
        // A small memtable, so its worst case is known, and a quarter of the heap kept for the rest of the server.
        private const int MemtableEntries = 10_000;
        private const long MemtableWorst = 4L * MemtableEntries * 215;

        /// <summary>The pool the server's settings leave under <paramref name="heap"/>: the heap less the memtable's worst case and the reserve (ADR 0195, amended 2026-11-18, point 10).</summary>
        internal static long Pool(long heap) => heap - MemtableWorst - Reserve(heap);

        /// <summary>The least heap whose pool holds <paramref name="closureBytes"/>.</summary>
        internal static long HeapFor(long closureBytes) => ((closureBytes + MemtableWorst) / 3 * 4) + (1L << 20);

        private static long Reserve(long heap) => heap / 4;

        private readonly Process _process;
        private readonly StringBuilder _log = new();
        private readonly string _root;

        private ChildServer(Process process, Uri address, string root)
        {
            _process = process;
            Address = address;
            _root = root;
        }

        internal Uri Address { get; }

        internal HttpClient Http { get; } = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(2) };

        internal bool Exited => _process.HasExited;

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

        internal static Task<ChildServer> StartAsync(long heap, long closureBytes) =>
            StartWithAsync(heap, [
                "--Varve:Inference:ClosureBytes=" + closureBytes.ToString(CultureInfo.InvariantCulture),
                "--Varve:Inference:ReserveBytes=" + Reserve(heap).ToString(CultureInfo.InvariantCulture),
                "--Varve:Store:MemtableLimit=" + MemtableEntries.ToString(CultureInfo.InvariantCulture),
            ]);

        /// <summary>The server under <paramref name="heap"/>, with <paramref name="settings"/> and the defaults for the rest.</summary>
        internal static async Task<ChildServer> StartWithAsync(long heap, string[] settings)
        {
            int port;
            using (TcpListener listener = new(IPAddress.Loopback, 0))
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }

            string address = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            string root = Directory.CreateTempSubdirectory("varve-closure-gate-").FullName;
            ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in (string[])[
                Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"),
                "--urls=" + address,
                "--Logging:LogLevel:Default=Warning",
                "--Varve:Auth:Mode=Anonymous",
                "--Varve:DatasetsRoot=" + root,
                "--Varve:Health:RateLimit=100000",
                .. settings,
            ])
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["DOTNET_GCHeapHardLimit"] = "0x" + heap.ToString("X", CultureInfo.InvariantCulture);
            ChildServer server = new(Process.Start(start)!, new Uri(address + "/"), root);
            server._process.OutputDataReceived += (_, line) => server.Append(line.Data);
            server._process.ErrorDataReceived += (_, line) => server.Append(line.Data);
            server._process.BeginOutputReadLine();
            server._process.BeginErrorReadLine();
            for (int attempt = 0; attempt < 300; attempt++)
            {
                Assert.False(server.Exited, "the server exited: " + server.Log);
                try
                {
                    if ((await server.Http.GetAsync(new Uri(server.Address, "health/live"), Ct)).IsSuccessStatusCode)
                    {
                        return server;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(100, Ct);
            }

            Assert.Fail("the server did not come up: " + server.Log);
            return server;
        }

        /// <summary>A dataset in memory with one materialising binding over <c>http://ex/g/</c>, its TBox <c>http://ex/tbox</c>.</summary>
        internal async Task DeclareAsync(string dataset, string profile, bool crossGraph)
        {
            string trig =
                "<urn:varve:from-config:" + dataset + "> a <" + Varve + "Dataset> ; <" + Varve + "datasetName> \"" + dataset + "\" ; <" + Varve + "storage> <" + Varve + "Memory> .\n"
                + "<urn:binding:" + dataset + "> a <" + Varve + "TBoxBinding> ; <" + Varve + "boundGraph> <http://ex/tbox> ; <" + Varve + "profile> <" + Varve + profile + "> ; <"
                + Varve + "graphPrefix> \"http://ex/g/\"" + (crossGraph ? " ; <" + Varve + "crossGraph> true" : string.Empty) + " .";
            using HttpRequestMessage request = new(HttpMethod.Put, new Uri(Address, "manage/datasets/" + dataset)) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };
            HttpResponseMessage declared = await Http.SendAsync(request, Ct);
            Assert.True(declared.StatusCode == HttpStatusCode.Created, await declared.Content.ReadAsStringAsync(Ct));
        }

        /// <summary>A dataset in memory with no binding.</summary>
        internal async Task DeclarePlainAsync(string dataset)
        {
            string trig = "<urn:varve:from-config:" + dataset + "> a <" + Varve + "Dataset> ; <" + Varve + "datasetName> \"" + dataset + "\" ; <" + Varve + "storage> <" + Varve + "Memory> .";
            using HttpRequestMessage request = new(HttpMethod.Put, new Uri(Address, "manage/datasets/" + dataset)) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };
            HttpResponseMessage declared = await Http.SendAsync(request, Ct);
            Assert.True(declared.StatusCode == HttpStatusCode.Created, await declared.Content.ReadAsStringAsync(Ct));
        }

        /// <summary>
        /// The chain <c>C0 ⊑ C1 ⊑ … ⊑ C{Depth}</c> in the TBox graph, and
        /// <paramref name="individuals"/> individuals typed <c>C0</c>, spread
        /// over <paramref name="graphs"/> data graphs; under a cross-graph
        /// binding each graph's individuals also link to the next graph's,
        /// so that premise sets cross.
        /// </summary>
        internal async Task LoadAsync(string dataset, int individuals, int graphs)
        {
            StringBuilder tbox = new("INSERT DATA { GRAPH <http://ex/tbox> {");
            for (int i = 0; i < Depth; i++)
            {
                tbox.Append(CultureInfo.InvariantCulture, $" <http://ex/C{i}> <{Rdfs}subClassOf> <http://ex/C{i + 1}> .");
            }

            await UpdateAsync(dataset, tbox.Append(" } }").ToString());
            for (int from = 0; from < individuals; from += 4_000)
            {
                StringBuilder data = new("INSERT DATA {");
                for (int i = from; i < Math.Min(individuals, from + 4_000); i++)
                {
                    int graph = i % graphs;
                    data.Append(CultureInfo.InvariantCulture, $" GRAPH <http://ex/g/{graph}> {{ <http://ex/x{i}> <{Rdf}type> <http://ex/C0> . <http://ex/x{i}> <http://ex/p> <http://ex/x{(i + 1) % individuals}> }}");
                }

                await UpdateAsync(dataset, data.Append(" }").ToString());
            }
        }

        internal async Task UpdateAsync(string dataset, string update)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(Address, "datasets/" + dataset + "/sparql")) { Content = new StringContent(update, Encoding.UTF8, "application/sparql-update") };
            HttpResponseMessage done = await Http.SendAsync(request, Ct);
            Assert.True(done.StatusCode == HttpStatusCode.NoContent, await done.Content.ReadAsStringAsync(Ct) + Log);
        }

        internal async Task<HttpResponseMessage> QueryAsync(string dataset, string query)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(Address, "datasets/" + dataset + "/sparql")) { Content = new StringContent(query, Encoding.UTF8, "application/sparql-query") };
            request.Headers.Accept.ParseAdd("application/sparql-results+json");
            return await Http.SendAsync(request, Ct);
        }

        /// <summary>The dataset's binding on readiness once its projection has caught up or stopped.</summary>
        internal async Task<JsonElement> SettledAsync(string dataset)
        {
            JsonElement binding = default;
            for (int attempt = 0; attempt < 600; attempt++)
            {
                Assert.False(Exited, "the server exited: " + Log);
                HttpResponseMessage ready = await Http.GetAsync(new Uri(Address, "health/ready"), Ct);
                using JsonDocument json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(Ct));
                if (json.RootElement.TryGetProperty("datasets", out JsonElement datasets)
                    && datasets.TryGetProperty(dataset, out JsonElement entry)
                    && entry.TryGetProperty("inference", out JsonElement inference)
                    && inference.TryGetProperty("urn:binding:" + dataset, out binding))
                {
                    binding = binding.Clone();
                    string? state = binding.GetProperty("state").GetString();
                    if (state != "following" || binding.GetProperty("lag").GetInt64() == 0)
                    {
                        return binding;
                    }
                }

                await Task.Delay(200, Ct);
            }

            Assert.Fail("the binding of " + dataset + " did not settle: " + binding + Log);
            return binding;
        }

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit();
            }

            _process.Dispose();
            Http.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }

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
}
