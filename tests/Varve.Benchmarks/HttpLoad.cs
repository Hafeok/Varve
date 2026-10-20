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
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Benchmarks;

/// <summary>
/// Milestone 7a's protocol workload: a SPARQL endpoint driven over HTTP by
/// concurrent clients, point queries per second and single-quad commits per
/// second, against the Native AOT Varve.Server binary and against Oxigraph's
/// server. Both are measured from outside, through loopback, by the same
/// client code; neither is referenced.
///
///   --http varve:&lt;Varve.Server binary&gt; [seconds]
///   --http oxigraph:&lt;address, e.g. http://127.0.0.1:7878&gt; [seconds]
///
/// Varve is started here, anonymous, with a memory and a file dataset.
/// Oxigraph is started by the caller (the README gives the command), on a
/// fresh store.
/// </summary>
internal static class HttpLoad
{
    private const int Triples = 100_000;
    private const int Subjects = Triples / 10;

    internal static async Task RunAsync(string target, int seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(seconds);
        Console.WriteLine("| Server | Store | Workload | Clients | Operations/s | p50 | p99 |");
        Console.WriteLine("|---|---|---|---:|---:|---:|---:|");

        if (target.StartsWith("varve:", StringComparison.Ordinal))
        {
            string root = Directory.CreateTempSubdirectory("varve-http-").FullName;
            using Process server = StartVarve(target["varve:".Length..], root);

            try
            {
                Uri address = new("http://127.0.0.1:18080/");
                await WaitAsync(address, "health/ready");

                // The two datasets, declared in the management plane (ADR 0163).
                using (HttpClient manage = Client(1))
                {
                    foreach ((string name, string storage) in new[] { ("memory", "Memory"), ("file", "File") })
                    {
                        using StringContent trig = new(
                            "<urn:load:" + name + "> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"" + name + "\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#" + storage + "> .",
                            System.Text.Encoding.UTF8,
                            "application/trig");
                        (await manage.PutAsync(new Uri(address, "manage/datasets/" + name), trig)).EnsureSuccessStatusCode();
                    }
                }

                foreach (string store in (string[])["memory", "file"])
                {
                    Endpoint endpoint = new(
                        "Varve",
                        store,
                        new Uri(address, "datasets/" + store + "/sparql"),
                        new Uri(address, "datasets/" + store + "/sparql"),
                        new Uri(address, "datasets/" + store + "/graphs?default"),
                        HttpMethod.Put);
                    await MeasureAsync(endpoint, duration);
                }
            }
            finally
            {
                server.Kill();
                await server.WaitForExitAsync();
                Directory.Delete(root, recursive: true);
            }
        }
        else if (target.StartsWith("oxigraph:", StringComparison.Ordinal))
        {
            Uri address = new(target["oxigraph:".Length..].TrimEnd('/') + "/");
            await WaitAsync(address, "query?query=ASK%7B%7D");
            Endpoint endpoint = new("Oxigraph", "RocksDB", new Uri(address, "query"), new Uri(address, "update"), new Uri(address, "store?default"), HttpMethod.Post);
            await MeasureAsync(endpoint, duration);
        }
        else
        {
            throw new ArgumentException("--http varve:<binary> or oxigraph:<address>", nameof(target));
        }
    }

    private static async Task MeasureAsync(Endpoint endpoint, TimeSpan duration)
    {
        using HttpClient loader = Client(1);
        StringBuilder data = new(Triples * 48);

        for (int i = 0; i < Triples; i++)
        {
            data.Append(CultureInfo.InvariantCulture, $"<http://ex/s{i / 10}> <http://ex/p{i % 10}> \"o{i}\" .\n");
        }

        using HttpRequestMessage load = new(endpoint.LoadMethod, endpoint.Load) { Content = new StringContent(data.ToString(), Encoding.UTF8, "application/n-triples") };
        using HttpResponseMessage loaded = await loader.SendAsync(load);
        loaded.EnsureSuccessStatusCode();

        foreach (int clients in (int[])[1, 8])
        {
            Report(endpoint, "point query, 10 rows", clients, await RunAsync(clients, duration, async (client, random) =>
            {
                string query = "SELECT ?p ?o WHERE { <http://ex/s" + random.Next(Subjects).ToString(CultureInfo.InvariantCulture) + "> ?p ?o }";
                using HttpRequestMessage request = new(HttpMethod.Get, endpoint.Query + "?query=" + Uri.EscapeDataString(query));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/sparql-results+json"));
                using HttpResponseMessage response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                _ = await response.Content.ReadAsByteArrayAsync();
            }));
        }

        int written = 0;

        foreach (int clients in (int[])[1, 8])
        {
            // A 409 is ADR 0057's answer to an update whose pin another
            // commit overtook (ConflictRetries 0, ADR 0094). The client here
            // does what a client must: it sends the request again. Each
            // operation counted is one commit; the conflicts are reported.
            int conflicts = 0;
            (double, double, double) result = await RunAsync(clients, duration, async (client, _) =>
            {
                int n = Interlocked.Increment(ref written);
                string update = "INSERT DATA { <http://ex/w" + n.ToString(CultureInfo.InvariantCulture) + "> <http://ex/p> \"v\" }";

                while (true)
                {
                    using HttpRequestMessage request = new(HttpMethod.Post, endpoint.Update) { Content = new StringContent(update, Encoding.UTF8, "application/sparql-update") };
                    using HttpResponseMessage response = await client.SendAsync(request);

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        Interlocked.Increment(ref conflicts);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
                    }

                    return;
                }
            });
            Report(endpoint, "INSERT DATA, 1 quad" + (conflicts > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({conflicts:N0} 409s retried)") : string.Empty), clients, result);
        }

        if (endpoint.Server == "Varve")
        {
            await ConditionalAsync(endpoint, duration);
        }
    }

    // The conditional workloads with one graph per writer (ADR 0125, the
    // Commit path milestone): a read-modify-write of the writer's own graph
    // by a pattern update, and a Graph Store PUT under If-Match after a GET
    // of the graph's tag. Before per-graph concurrency every other writer's
    // commit overtook them; after, only a commit to the same graph does. A
    // 409 or 412 is retried by the client, as a client must, and counted.
    private static async Task ConditionalAsync(Endpoint endpoint, TimeSpan duration)
    {
        string graphs = endpoint.Load.ToString().Replace("?default", string.Empty, StringComparison.Ordinal);
        using HttpClient setup = Client(1);

        for (int g = 0; g < 8; g++)
        {
            using HttpRequestMessage create = new(HttpMethod.Put, graphs + "?graph=" + Uri.EscapeDataString(Writer(g))) { Content = new StringContent("<http://ex/c> <http://ex/v> \"0\" .\n", Encoding.UTF8, "application/n-triples") };
            (await setup.SendAsync(create)).EnsureSuccessStatusCode();
        }

        foreach (int clients in (int[])[1, 8])
        {
            int conflicts = 0;
            (double, double, double) result = await RunAsync(clients, duration, async (client, _, writer) =>
            {
                string g = Writer(writer);
                string update = "DELETE { GRAPH <" + g + "> { <http://ex/c> <http://ex/v> ?x } } INSERT { GRAPH <" + g + "> { <http://ex/c> <http://ex/v> ?y } } "
                    + "WHERE { OPTIONAL { GRAPH <" + g + "> { <http://ex/c> <http://ex/v> ?x } } BIND (STR(COALESCE(xsd:integer(?x) + 1, 1)) AS ?y) }";

                while (true)
                {
                    using HttpRequestMessage request = new(HttpMethod.Post, endpoint.Update) { Content = new StringContent("PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>\n" + update, Encoding.UTF8, "application/sparql-update") };
                    using HttpResponseMessage response = await client.SendAsync(request);

                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        Interlocked.Increment(ref conflicts);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException((int)response.StatusCode + " " + await response.Content.ReadAsStringAsync());
                    }

                    return;
                }
            });
            Report(endpoint, string.Create(CultureInfo.InvariantCulture, $"DELETE/INSERT WHERE, own graph ({conflicts:N0} 409s retried)"), clients, result);
        }

        foreach (int clients in (int[])[1, 8])
        {
            int refused = 0;
            (double, double, double) result = await RunAsync(clients, duration, async (client, random, writer) =>
            {
                string address = graphs + "?graph=" + Uri.EscapeDataString(Writer(writer));

                while (true)
                {
                    using HttpRequestMessage get = new(HttpMethod.Get, address);
                    get.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/n-triples"));
                    using HttpResponseMessage read = await client.SendAsync(get);
                    read.EnsureSuccessStatusCode();
                    _ = await read.Content.ReadAsByteArrayAsync();

                    using HttpRequestMessage put = new(HttpMethod.Put, address) { Content = new StringContent("<http://ex/c> <http://ex/v> \"" + random.Next().ToString(CultureInfo.InvariantCulture) + "\" .\n", Encoding.UTF8, "application/n-triples") };
                    put.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.Tag);
                    using HttpResponseMessage written = await client.SendAsync(put);

                    if (written.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
                    {
                        Interlocked.Increment(ref refused);
                        continue;
                    }

                    if (!written.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException((int)written.StatusCode + " " + await written.Content.ReadAsStringAsync());
                    }

                    return;
                }
            });
            Report(endpoint, string.Create(CultureInfo.InvariantCulture, $"GET, PUT under If-Match, own graph ({refused:N0} 412s and 409s retried)"), clients, result);
        }
    }

    private static string Writer(int writer) => "http://ex/w" + writer.ToString(CultureInfo.InvariantCulture);

    // Closed loop: each client sends its next request when the last one is
    // answered. Two seconds of warm-up are run and discarded first.
    private static Task<(double PerSecond, double P50, double P99)> RunAsync(int clients, TimeSpan duration, Func<HttpClient, Random, Task> operation) =>
        RunAsync(clients, duration, (client, random, _) => operation(client, random));

    // The same, telling each operation which client it is, for workloads with
    // a graph per writer.
    private static async Task<(double PerSecond, double P50, double P99)> RunAsync(int clients, TimeSpan duration, Func<HttpClient, Random, int, Task> operation)
    {
        using HttpClient client = Client(clients);
        await Loop(TimeSpan.FromSeconds(2));
        List<long>[] samples = await Loop(duration);
        long[] all = [.. samples.SelectMany(s => s)];
        Array.Sort(all);
        double ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        return (all.Length / duration.TotalSeconds, ms(all[all.Length / 2]), ms(all[(int)(all.Length * 0.99)]));

        async Task<List<long>[]> Loop(TimeSpan length)
        {
            long end = Stopwatch.GetTimestamp() + (long)(length.TotalSeconds * Stopwatch.Frequency);
            List<long>[] taken = [.. Enumerable.Range(0, clients).Select(_ => new List<long>())];
            await Task.WhenAll(Enumerable.Range(0, clients).Select(c => Task.Run(async () =>
            {
                Random random = new(c);

                while (Stopwatch.GetTimestamp() < end)
                {
                    long start = Stopwatch.GetTimestamp();
                    await operation(client, random, c);
                    taken[c].Add(Stopwatch.GetTimestamp() - start);
                }
            })));
            return taken;
        }
    }

    private static void Report(Endpoint endpoint, string workload, int clients, (double PerSecond, double P50, double P99) result) =>
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {endpoint.Server} | {endpoint.Store} | {workload} | {clients} | {result.PerSecond:N0} | {result.P50:F2} ms | {result.P99:F2} ms |"));

    private static HttpClient Client(int connections) =>
        new(new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = connections }) { Timeout = TimeSpan.FromMinutes(5) };

    private static Process StartVarve(string binary, string root)
    {
        ProcessStartInfo start = new(binary) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string arg in (string[])[
            "--urls=http://127.0.0.1:18080",
            "--Logging:LogLevel:Default=Warning",
            "--Varve:Auth:Mode=Anonymous",
            "--Varve:DatasetsRoot=" + root,
            "--Varve:Limits:MaxRequestBody=1073741824"])
        {
            start.ArgumentList.Add(arg);
        }

        // More of the server's own settings for one run, space-separated:
        // VARVE_HTTP_LOAD_ARGS="--Varve:Store:GroupCommit:MaxRecords=1".
        foreach (string arg in (Environment.GetEnvironmentVariable("VARVE_HTTP_LOAD_ARGS") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            start.ArgumentList.Add(arg);
        }

        Process process = Process.Start(start)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                Console.Error.WriteLine(line.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitAsync(Uri address, string path)
    {
        using HttpClient client = Client(1);

        for (int attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(new Uri(address, path));

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The server at " + address + " did not become ready.");
    }

    private sealed record Endpoint(string Server, string Store, Uri Query, Uri Update, Uri Load, HttpMethod LoadMethod);
}
