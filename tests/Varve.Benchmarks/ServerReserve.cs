// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Benchmarks;

/// <summary>
/// What a server holds beside its closures and its memtable while it
/// answers (ADR 0195 point 6): the reserve the
/// inference bindings' pool leaves for queries, caches and requests. The
/// server runs at the small-machine settings (a 5 GiB heap limit, four
/// concurrent reads, 256 MiB a query), with no inference binding and a
/// memtable held small so its share is known; it is loaded with a dataset,
/// restarted, then read by more clients than it runs reads, with queries
/// that sort, group, deduplicate and construct the whole dataset, while a
/// writer commits. The peak resident set during the load, less the
/// memtable's worst case, is the reserve's measure: an upper bound on the
/// heap, since it counts the runtime and the collector's slack too.
///
///   --server-reserve &lt;Varve.Server binary&gt; [quads] [seconds]
///
/// VARVE_RESERVE_HEAP_LIMIT sets another heap limit, as DOTNET_GCHeapHardLimit takes it, to bracket the need.
/// </summary>
internal static class ServerReserve
{
    private const int MemtableEntries = 50_000;
    private const int QuadsACommit = 2_000;

    private static readonly System.Collections.Immutable.ImmutableArray<string> Queries =
    [
        "SELECT ?s ?p ?o WHERE { GRAPH ?g { ?s ?p ?o } } ORDER BY ?o ?s",
        "SELECT ?o (COUNT(*) AS ?n) WHERE { GRAPH ?g { ?s ?p ?o } } GROUP BY ?o",
        "SELECT DISTINCT ?s ?o WHERE { GRAPH ?g { ?s ?p ?o } }",
        "CONSTRUCT { ?s ?p ?o } WHERE { GRAPH ?g { ?s ?p ?o } }",
        "SELECT ?a ?b WHERE { GRAPH ?g { ?a <urn:r:next> ?x . ?x <urn:r:next> ?b } }",
        "SELECT ?o WHERE { GRAPH ?g { <urn:r:s/17> ?p ?o } }",
    ];

    internal static async Task<int> RunAsync(string binary, int quads, int seconds)
    {
        string root = Directory.CreateTempSubdirectory("varve-reserve-").FullName;
        Uri address = new("http://127.0.0.1:18082/");
        Process server = Start(binary, root);
        try
        {
            await ReadyAsync(address);
            using HttpClient client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) };
            using (StringContent trig = new(
                "<urn:reserve> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"reserve\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#File> .",
                Encoding.UTF8,
                "application/trig"))
            {
                (await client.PutAsync(new Uri(address, "manage/datasets/reserve"), trig)).EnsureSuccessStatusCode();
            }

            Uri sparql = new(address, "datasets/reserve/sparql");
            Random random = new(1118);
            for (int written = 0, commit = 0; written < quads; commit++)
            {
                await CommitAsync(client, sparql, Insert(random, commit, QuadsACommit));
                written += QuadsACommit;
            }

            (await client.PostAsync(new Uri(address, "manage/datasets/reserve/checkpoints"), null)).EnsureSuccessStatusCode();
            Console.WriteLine("// " + quads.ToString("N0", CultureInfo.InvariantCulture) + " quads loaded; peak during the load " + Mb(Peak(server)) + " (not the measure)");

            // The measure starts from a fresh process: the same files, reopened.
            Stop(server);
            server = Start(binary, root);
            await ReadyAsync(address);
            await QueryAsync(client, sparql, "ASK { GRAPH ?g { ?s ?p ?o } }");
            long idle = Resident(server);

            ConcurrentDictionary<string, int> answers = new(StringComparer.Ordinal);
            using CancellationTokenSource stop = new(TimeSpan.FromSeconds(seconds));
            long peak = 0;
            Task sampler = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    peak = Math.Max(peak, Resident(server));
                    await Task.Delay(50);
                }
            });

            List<Task> clients = [];
            for (int c = 0; c < 8; c++)
            {
                int first = c;
                clients.Add(Task.Run(async () =>
                {
                    using HttpClient reader = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(5) };
                    for (int i = first; !stop.IsCancellationRequested; i++)
                    {
                        string status = await QueryAsync(reader, sparql, Queries[i % Queries.Length]);
                        answers.AddOrUpdate(status, 1, (_, n) => n + 1);
                    }
                }));
            }

            clients.Add(Task.Run(async () =>
            {
                Random writes = new(18);
                for (int commit = 1_000_000; !stop.IsCancellationRequested; commit++)
                {
                    string status = await TryCommitAsync(client, sparql, Insert(writes, commit, 200));
                    answers.AddOrUpdate(status, 1, (_, n) => n + 1);
                }
            }));

            await Task.WhenAll(clients);
            await sampler;
            peak = Math.Max(peak, Peak(server));
            long memtable = 4L * MemtableEntries * 215;

            Console.WriteLine("// heap limit " + (Environment.GetEnvironmentVariable("VARVE_RESERVE_HEAP_LIMIT") ?? "0x140000000 (5 GiB)") + ", MaxConcurrentReads 4, MaxQueryMemory 256 MiB, MemtableLimit " + MemtableEntries.ToString("N0", CultureInfo.InvariantCulture) + " (worst " + Mb(memtable) + ")");
            Console.WriteLine("reserve: idle after reopening " + Mb(idle) + "; peak under load " + Mb(peak) + "; less the memtable's worst " + Mb(peak - memtable) + "; " + seconds.ToString(CultureInfo.InvariantCulture) + " s");
            foreach ((string status, int count) in answers)
            {
                Console.WriteLine("//   " + status + ": " + count.ToString(CultureInfo.InvariantCulture));
            }

            Console.WriteLine("// the server " + (server.HasExited ? "exited with code " + server.ExitCode.ToString(CultureInfo.InvariantCulture) : "stayed up"));
            return server.HasExited ? 1 : 0;
        }
        finally
        {
            Stop(server);
            Directory.Delete(root, recursive: true);
        }
    }

    // About as REC's data is: subjects in chains, literals of mixed length, a graph per hundred subjects.
    private static string Insert(Random random, int commit, int count)
    {
        StringBuilder update = new(count * 96);
        update.Append("INSERT DATA { GRAPH <urn:r:g/").Append((commit % 500).ToString(CultureInfo.InvariantCulture)).Append("> {\n");
        for (int q = 0; q < count; q++)
        {
            int s = random.Next(200_000);
            update.Append("<urn:r:s/").Append(s.ToString(CultureInfo.InvariantCulture)).Append("> ");
            switch (q % 4)
            {
                case 0:
                    update.Append("<urn:r:next> <urn:r:s/").Append(random.Next(200_000).ToString(CultureInfo.InvariantCulture)).Append("> .\n");
                    break;
                case 1:
                    update.Append("<urn:r:label> \"").Append(new string((char)('a' + random.Next(26)), random.Next(4, 60))).Append(s.ToString(CultureInfo.InvariantCulture)).Append("\" .\n");
                    break;
                case 2:
                    update.Append("<urn:r:value> ").Append(random.Next(10_000).ToString(CultureInfo.InvariantCulture)).Append(" .\n");
                    break;
                default:
                    update.Append("a <urn:r:Kind").Append(random.Next(40).ToString(CultureInfo.InvariantCulture)).Append("> .\n");
                    break;
            }
        }

        return update.Append("} }").ToString();
    }

    private static async Task CommitAsync(HttpClient client, Uri sparql, string update)
    {
        using StringContent content = new(update, Encoding.UTF8, "application/sparql-update");
        using HttpResponseMessage response = await client.PostAsync(sparql, content);
        response.EnsureSuccessStatusCode();
    }

    // "commit" when it was taken; otherwise its status and the problem's type, as a writer under load sees it.
    private static async Task<string> TryCommitAsync(HttpClient client, Uri sparql, string update)
    {
        try
        {
            using StringContent content = new(update, Encoding.UTF8, "application/sparql-update");
            using HttpResponseMessage response = await client.PostAsync(sparql, content);
            if (response.IsSuccessStatusCode)
            {
                return "commit";
            }

            string body = await response.Content.ReadAsStringAsync();
            int type = body.IndexOf("problems/", StringComparison.Ordinal);
            return "commit refused " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + (type < 0 ? "" : " " + body[(type + 9)..].Split('"')[0]);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return "commit " + error.GetType().Name;
        }
    }

    // The status, with the body read to its end and dropped, as a client streaming it would.
    private static async Task<string> QueryAsync(HttpClient client, Uri sparql, string query)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, sparql) { Content = new StringContent(query, Encoding.UTF8, "application/sparql-query") };
            request.Headers.Accept.ParseAdd(query.StartsWith("CONSTRUCT", StringComparison.Ordinal) ? "application/n-triples" : "application/sparql-results+json");
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                string problem = await response.Content.ReadAsStringAsync();
                int type = problem.IndexOf("problems/", StringComparison.Ordinal);
                return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + (type < 0 ? "" : " " + problem[(type + 9)..].Split('"')[0]);
            }

            await using Stream body = await response.Content.ReadAsStreamAsync();
            await body.CopyToAsync(Stream.Null);
            return ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return error.GetType().Name;
        }
    }

    private static Process Start(string binary, string root)
    {
        ProcessStartInfo start = new(binary) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["DOTNET_GCHeapHardLimit"] = Environment.GetEnvironmentVariable("VARVE_RESERVE_HEAP_LIMIT") ?? "0x140000000";
        foreach (string arg in (string[])[
            "--urls=http://127.0.0.1:18082",
            "--Logging:LogLevel:Default=Warning",
            "--Varve:Auth:Mode=Anonymous",
            "--Varve:DatasetsRoot=" + root,
            "--Varve:Limits:MaxConcurrentReads=4",
            "--Varve:Store:MemtableLimit=" + MemtableEntries.ToString(CultureInfo.InvariantCulture)])
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

    private static void Stop(Process server)
    {
        if (!server.HasExited)
        {
            server.Kill(entireProcessTree: true);
            server.WaitForExit();
        }

        server.Dispose();
    }

    private static async Task ReadyAsync(Uri address)
    {
        using HttpClient client = new(new SocketsHttpHandler { UseProxy = false });
        for (int attempt = 0; attempt < 240; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(new Uri(address, "health/ready"));
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

    private static long Resident(Process server)
    {
        server.Refresh();
        return server.HasExited ? 0 : server.WorkingSet64;
    }

    private static long Peak(Process server)
    {
        server.Refresh();
        return server.HasExited ? 0 : server.PeakWorkingSet64;
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F0", CultureInfo.InvariantCulture) + " MiB";
}
