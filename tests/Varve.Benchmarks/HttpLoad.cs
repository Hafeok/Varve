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
                await WaitAsync(address, "ready");

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
    }

    // Closed loop: each client sends its next request when the last one is
    // answered. Two seconds of warm-up are run and discarded first.
    private static async Task<(double PerSecond, double P50, double P99)> RunAsync(int clients, TimeSpan duration, Func<HttpClient, Random, Task> operation)
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
                    await operation(client, random);
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
            "--Varve:Datasets:memory:Storage=Memory",
            "--Varve:Datasets:file:Storage=File",
            "--Varve:Limits:MaxRequestBody=1073741824"])
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
