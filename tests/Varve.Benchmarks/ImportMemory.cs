// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Varve.Benchmarks;

/// <summary>
/// What a served dataset needs to be written and to be reopened (ADR 0241,
/// #227): an import in the shape of a package catalog — one commit a package,
/// one small named graph a version, then small commits that drop and replace
/// a few graphs each — over HTTP against the Varve.Server binary, then a
/// checkpoint, an idle minute and a restart, with the server's resident
/// memory read from the operating system at each point.
///
///   --import-memory &lt;Varve.Server binary&gt; [packages] [cgroup directory]
///
/// 3,000 packages by default: 512,390 graphs and 3.06 million quads, the
/// size #227 measured. With a cgroup directory the server is put in it once
/// started (v1 memory controller or v2), so a memory limit set there holds
/// the server and not this process. Generated from a fixed seed: the same
/// requests every run.
/// </summary>
internal static class ImportMemory
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Schema = "http://schema.nuget.org/schema#";
    private const string Term = "urn:varve-demo:term:";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    private static readonly System.Collections.Immutable.ImmutableArray<string> Words =
    [
        "Microsoft", "Extensions", "DependencyInjection", "Abstractions", "System", "Text", "Json", "Azure", "Core", "Identity", "Storage", "Blobs",
        "Newtonsoft", "Serilog", "Sinks", "Console", "AspNetCore", "Authentication", "JwtBearer", "EntityFrameworkCore", "SqlServer", "Tools", "Design",
        "Logging", "Configuration", "Binder", "Hosting", "Http", "Polly", "AutoMapper", "FluentValidation", "MediatR", "Swashbuckle", "Swagger", "xunit",
        "runner", "visualstudio", "NUnit", "Moq", "Castle", "Grpc", "Net", "Client", "Google", "Protobuf", "AWSSDK", "S3", "Runtime", "Security", "Cryptography",
    ];

    internal static async Task<int> RunAsync(string binary, int packages, string? cgroup)
    {
        string root = Directory.CreateTempSubdirectory("varve-import-").FullName;
        Uri address = new("http://127.0.0.1:18081/");
        Random random = new(227);
        (string Name, int Versions)[] catalog = Catalog(packages, random);
        Process server = Start(binary, root, cgroup);
        int exit = 0;

        try
        {
            await ReadyAsync(address);
            using HttpClient client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(10) };
            using (StringContent trig = new(
                "<urn:import> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> \"import\" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#File> .",
                Encoding.UTF8,
                "application/trig"))
            {
                (await client.PutAsync(new Uri(address, "manage/datasets/import"), trig)).EnsureSuccessStatusCode();
            }

            Console.WriteLine("| Point | Resident | Peak so far |");
            Console.WriteLine("|---|---:|---:|");
            Row("start", server);

            Uri sparql = new(address, "datasets/import/sparql");
            Stopwatch clock = Stopwatch.StartNew();
            long quads = 0;
            int commits = 0;
            string? refused = null;

            // The base: a package a commit, a graph a version.
            foreach ((string name, int versions) in catalog)
            {
                StringBuilder update = new(64 * 1024);

                for (int v = 0; v < versions; v++)
                {
                    string version = Version(v, random);
                    update.Append("INSERT DATA { GRAPH <urn:varve-demo:pkg/").Append(Encode(name)).Append('/').Append(Encode(version)).Append("> {\n");
                    quads += Leaf(update, name, version, random, whole: false);
                    update.Append("} } ;\n");
                }

                update.Append("INSERT DATA { GRAPH <urn:varve-demo:catalog-log> {\n<urn:varve-demo:base:").Append(Encode(name)).Append("> <").Append(Term).Append("basePackage> \"")
                    .Append(name).Append("\" ;\n  <").Append(Term).Append("baseAsOf> \"2026-10-20T00:00:00Z\"^^<").Append(Xsd).Append("dateTime> .\n} }");
                quads += 2;

                if ((refused = await PostAsync(client, sparql, update.ToString())) is not null)
                {
                    break;
                }

                commits++;
            }

            // The window: a few graphs dropped and written again in each commit.
            for (int c = 0; refused is null && c < packages * 1731 / 3000; c++)
            {
                StringBuilder update = new(4096);

                for (int leaf = random.NextDouble() < 0.97 ? 3 : 5; leaf > 0; leaf--)
                {
                    (string name, int versions) = catalog[random.Next(catalog.Length)];
                    string version = Version(random.Next(versions + 3), random);
                    string graph = "<urn:varve-demo:pkg/" + Encode(name) + "/" + Encode(version) + ">";
                    update.Append("DROP SILENT GRAPH ").Append(graph).Append(" ;\nINSERT DATA { GRAPH ").Append(graph).Append(" {\n");
                    quads += Leaf(update, name, version, random, whole: true);
                    update.Append("} } ;\n");
                }

                string id = random.NextInt64().ToString("x16", CultureInfo.InvariantCulture) + random.NextInt64().ToString("x16", CultureInfo.InvariantCulture);
                update.Append("INSERT DATA { GRAPH <urn:varve-demo:catalog-log> {\n<urn:varve-demo:catalog-commit:").Append(id).Append("> <http://schema.nuget.org/catalog#commitId> \"")
                    .Append(id).Append("\" ;\n  <http://schema.nuget.org/catalog#commitTimeStamp> \"2026-10-21T00:00:00Z\"^^<").Append(Xsd).Append("dateTime> .\n} }");
                quads += 2;

                if ((refused = await PostAsync(client, sparql, update.ToString())) is not null)
                {
                    break;
                }

                commits++;
            }

            Row("end of the import", server);
            double seconds = clock.Elapsed.TotalSeconds;

            if (refused is not null)
            {
                Console.WriteLine();
                Console.WriteLine("Commit " + (commits + 1).ToString(CultureInfo.InvariantCulture) + " was refused: " + refused);
                exit = 1;
            }
            else
            {
                Stopwatch checkpoint = Stopwatch.StartNew();
                (await client.PostAsync(new Uri(address, "manage/datasets/import/checkpoints"), null)).EnsureSuccessStatusCode();
                checkpoint.Stop();
                Row("after the checkpoint", server);
                await Task.Delay(TimeSpan.FromSeconds(60));
                Row("60 s later, idle", server);
                Console.WriteLine();
                Console.WriteLine(commits.ToString("N0", CultureInfo.InvariantCulture) + " commits, " + quads.ToString("N0", CultureInfo.InvariantCulture) + " quads in "
                    + seconds.ToString("F1", CultureInfo.InvariantCulture) + " s; the checkpoint took " + checkpoint.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s.");
            }

            // The restart: the same files, opened again under the same limit.
            Stop(server);
            server = Start(binary, root, cgroup);
            Stopwatch reopening = Stopwatch.StartNew();
            string? answered = await ReopenedAsync(client, sparql, server);
            Console.WriteLine();

            if (answered is null)
            {
                Console.WriteLine("| after a restart | " + Mb(server.HasExited ? 0 : Resident(server)) + " | " + Mb(server.HasExited ? 0 : Peak(server)) + " |");
                Console.WriteLine();
                Console.WriteLine("Reopened and answered in " + reopening.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s.");
            }
            else
            {
                Console.WriteLine("The dataset did not reopen: " + answered);
                exit = 1;
            }
        }
        finally
        {
            Stop(server);
            Directory.Delete(root, recursive: true);
        }

        return exit;
    }

    // Null when the update was committed; otherwise the server's answer.
    private static async Task<string?> PostAsync(HttpClient client, Uri sparql, string update)
    {
        using StringContent content = new(update, Encoding.UTF8, "application/sparql-update");

        try
        {
            using HttpResponseMessage response = await client.PostAsync(sparql, content);
            return response.IsSuccessStatusCode ? null : ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException error)
        {
            return "the server went away: " + error.Message;
        }
    }

    // Null once a query is answered; otherwise why it never was, within two minutes.
    private static async Task<string?> ReopenedAsync(HttpClient client, Uri sparql, Process server)
    {
        string last = "no answer";

        for (int attempt = 0; attempt < 240; attempt++)
        {
            if (server.HasExited)
            {
                return "the server exited with code " + server.ExitCode.ToString(CultureInfo.InvariantCulture) + " while opening it";
            }

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, new Uri(sparql + "?query=" + Uri.EscapeDataString("ASK { GRAPH <urn:varve-demo:catalog-log> { ?s ?p ?o } }")));
                request.Headers.Accept.ParseAdd("application/sparql-results+json");
                using HttpResponseMessage response = await client.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return null;
                }

                last = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException error)
            {
                last = error.Message;
            }

            await Task.Delay(500);
        }

        return last;
    }

    private static (string Name, int Versions)[] Catalog(int packages, Random random)
    {
        (string Name, int Versions)[] catalog = new (string, int)[packages];
        double[] weights = new double[packages];
        double total = 0;

        for (int i = 0; i < packages; i++)
        {
            StringBuilder name = new();

            for (int word = random.Next(1, 5); word > 0; word--)
            {
                name.Append(name.Length > 0 ? "." : "").Append(Words[random.Next(Words.Length)]);
            }

            // Log-normal, as download counts are: a few packages with thousands of versions.
            double normal = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            weights[i] = Math.Exp(normal);
            total += weights[i];
            catalog[i] = (name.Append(i.ToString(CultureInfo.InvariantCulture)).ToString(), 0);
        }

        for (int i = 0; i < packages; i++)
        {
            catalog[i].Versions = Math.Max(1, (int)Math.Round(weights[i] / total * (512_390.0 * packages / 3000)));
        }

        return catalog;
    }

    private static string Version(int number, Random random) =>
        (number / 400).ToString(CultureInfo.InvariantCulture) + "." + (number / 20 % 20).ToString(CultureInfo.InvariantCulture) + "." + (number % 20).ToString(CultureInfo.InvariantCulture)
        + (random.NextDouble() < 0.3 ? "-preview." + random.Next(1, 10).ToString(CultureInfo.InvariantCulture) + "." + random.Next(20000, 26000).ToString(CultureInfo.InvariantCulture) : "");

    // A version's quads, about six of them; returns how many.
    private static int Leaf(StringBuilder update, string package, string version, Random random, bool whole)
    {
        string subject = "<https://api.nuget.org/v3/catalog0/data/20" + random.Next(15, 27).ToString(CultureInfo.InvariantCulture) + "." + random.Next(1, 13).ToString("D2", CultureInfo.InvariantCulture)
            + "." + random.Next(1, 29).ToString("D2", CultureInfo.InvariantCulture) + "." + random.Next(0, 24).ToString("D2", CultureInfo.InvariantCulture) + "."
            + random.Next(0, 60).ToString("D2", CultureInfo.InvariantCulture) + "." + random.Next(0, 60).ToString("D2", CultureInfo.InvariantCulture) + "/"
            + package.ToLowerInvariant() + "." + version.ToLowerInvariant() + ".json>";
        update.Append(subject).Append(" <").Append(Rdf).Append("> <").Append(Schema).Append("PackageDetails> .\n");
        update.Append(subject).Append(" <").Append(Schema).Append("id> \"").Append(package).Append("\" .\n");
        update.Append(subject).Append(" <").Append(Schema).Append("version> \"").Append(version).Append("\" .\n");
        update.Append(subject).Append(" <").Append(Term).Append("versionKey> \"").Append(version.Split('-')[0].Replace('.', ':').PadLeft(12, '0')).Append("\" ;\n  <")
            .Append(Term).Append("packageId> \"").Append(package.ToLowerInvariant()).Append("\" .\n");
        int quads = 5;

        if (whole || random.NextDouble() < 0.88)
        {
            update.Append(subject).Append(" <").Append(Schema).Append("listed> \"true\"^^<").Append(Xsd).Append("boolean> .\n");
            quads++;
        }

        if (whole)
        {
            update.Append(subject).Append(" <").Append(Rdf).Append("> <http://schema.nuget.org/catalog#Permalink> .\n");
            quads++;
        }

        return quads;
    }

    private static string Encode(string text) => Uri.EscapeDataString(text.ToLowerInvariant());

    private static Process Start(string binary, string root, string? cgroup)
    {
        ProcessStartInfo start = new(binary) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string arg in (string[])[
            "--urls=http://127.0.0.1:18081",
            "--Logging:LogLevel:Default=Warning",
            "--Varve:Auth:Mode=Anonymous",
            "--Varve:DatasetsRoot=" + root])
        {
            start.ArgumentList.Add(arg);
        }

        // More of the server's own settings for one run, space-separated, as --http takes them.
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

        if (cgroup is not null)
        {
            File.WriteAllText(Path.Combine(cgroup, "cgroup.procs"), process.Id.ToString(CultureInfo.InvariantCulture));
        }

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

        for (int attempt = 0; attempt < 120; attempt++)
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

    private static void Row(string point, Process server) =>
        Console.WriteLine("| " + point + " | " + Mb(Resident(server)) + " | " + Mb(Peak(server)) + " |");

    private static long Resident(Process server)
    {
        server.Refresh();
        return server.WorkingSet64;
    }

    private static long Peak(Process server)
    {
        server.Refresh();
        return server.PeakWorkingSet64;
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F0", CultureInfo.InvariantCulture) + " MB";
}
