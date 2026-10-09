// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Native AOT server, run as an operator runs it (ADR 0101).
//
//   dotnet run eng/server-smoke.cs -- <path to the published Varve.Server binary>
//
// First the command line (ADR 0105): create, update, query, export, checkpoint
// and feed against a directory in a temporary root. Then the binary serves that
// root in anonymous mode; the smoke waits for GET /ready, makes an update, a
// query and a feed read over HTTP, runs one CLI query against the server's URL,
// and stops it. On Linux and macOS the stop is SIGTERM and the exit code must
// be 0; Windows has no SIGTERM to send a console process, so there the process
// is killed and the graceful stop is the in-process tests' alone.
//
// The interesting AOT failures are at run time and silent — a trimmed type, a
// missing generic instantiation — so every path that serialises or binds runs
// here: the configuration binder, the JSON of /ready, a SPARQL JSON result, the
// delta format, the command line's parser and its HTTP client. Exit 1 is a
// failure, 2 a smoke run that could not start.
//
// It writes the binary's size and its time to ready to the step summary when
// there is one. A shared runner is a noisy machine (ADR 0027): the time is an
// order of magnitude.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;

if (args.Length != 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("usage: dotnet run eng/server-smoke.cs -- <Varve.Server binary>");
    return 2;
}

string binary = Path.GetFullPath(args[0]);
string root = Directory.CreateTempSubdirectory("varve-smoke-").FullName;
string directory = Path.Combine(root, "smoke");

async Task<(int Exit, string Output, string Error)> VarveAsync(params string[] arguments)
{
    ProcessStartInfo command = new(binary) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

    foreach (string argument in arguments)
    {
        command.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(command)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, await output, await error);
}

async Task<string?> CliAsync(string expected, params string[] arguments)
{
    (int exit, string output, string error) = await VarveAsync(arguments);

    if (exit != 0)
    {
        return "`varve " + string.Join(' ', arguments) + "` exited " + exit + ": " + error.Trim();
    }

    if (!output.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(expected, StringComparison.Ordinal))
    {
        return "`varve " + string.Join(' ', arguments) + "` wrote " + output.Trim() + " and not " + expected.Trim();
    }

    return null;
}

try
{
    string? failed = await CliAsync("created ", "create", directory)
        ?? await CliAsync("committed, position 1", "update", directory, "-u", "INSERT DATA { <http://ex/a> <http://ex/p> \"cli\" }")
        ?? await CliAsync("o\ncli\n", "query", directory, "-q", "SELECT ?o WHERE { <http://ex/a> <http://ex/p> ?o }", "-f", "csv")
        ?? await CliAsync("<http://ex/a> <http://ex/p> \"cli\" .\n", "export", directory)
        ?? await CliAsync("checkpoint at 1", "checkpoint", directory)
        ?? await CliAsync("commit 1 Data ", "feed", directory, "--to", "1")
        ?? await CliAsync("\"head\": 1", "info", directory);

    if (failed is not null)
    {
        Console.Error.WriteLine("server smoke: " + failed);
        return 1;
    }
}
catch (Exception error) when (error is IOException or InvalidOperationException)
{
    Console.Error.WriteLine("server smoke: the command line could not run: " + error.Message);
    return 2;
}

ProcessStartInfo start = new(binary)
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
};

foreach (string arg in (string[])["--urls=http://127.0.0.1:0", "--Varve:Auth:Mode=Anonymous", "--Varve:DatasetsRoot=" + root, "--Varve:Datasets:smoke:Storage=File"])
{
    start.ArgumentList.Add(arg);
}

Stopwatch clock = Stopwatch.StartNew();
using Process server = Process.Start(start)!;
TaskCompletionSource<string> address = new(TaskCreationOptions.RunContinuationsAsynchronously);
StringBuilder log = new();
server.OutputDataReceived += (_, line) => Watch(line.Data);
server.ErrorDataReceived += (_, line) => Watch(line.Data);
server.BeginOutputReadLine();
server.BeginErrorReadLine();

void Watch(string? line)
{
    if (line is null)
    {
        return;
    }

    lock (log)
    {
        log.AppendLine(line);
    }

    const string Listening = "Now listening on: ";
    int at = line.IndexOf(Listening, StringComparison.Ordinal);

    if (at >= 0)
    {
        address.TrySetResult(line[(at + Listening.Length)..].Trim() + "/");
    }
}

int Fail(string reason)
{
    Console.Error.WriteLine("server smoke: " + reason);

    lock (log)
    {
        Console.Error.WriteLine(log.ToString());
    }

    if (!server.HasExited)
    {
        server.Kill();
    }

    return 1;
}

try
{
    Task first = await Task.WhenAny(address.Task, server.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(60)));

    if (first != address.Task)
    {
        return Fail(server.HasExited ? "the server exited with " + server.ExitCode + " before listening" : "the server did not listen within 60 s");
    }

    using HttpClient client = new() { BaseAddress = new Uri(await address.Task), Timeout = TimeSpan.FromSeconds(30) };
    HttpResponseMessage ready;

    do
    {
        ready = await client.GetAsync("ready");
    }
    while (ready.StatusCode != HttpStatusCode.OK && clock.Elapsed < TimeSpan.FromSeconds(60));

    TimeSpan toReady = clock.Elapsed;

    if (ready.StatusCode != HttpStatusCode.OK)
    {
        return Fail("/ready is " + (int)ready.StatusCode);
    }

    HttpResponseMessage update = await client.PostAsync("datasets/smoke/sparql", new StringContent("INSERT DATA { <http://ex/a> <http://ex/p> \"smoke\" }", Encoding.UTF8, "application/sparql-update"));

    if (update.StatusCode != HttpStatusCode.NoContent)
    {
        return Fail("the update is " + (int)update.StatusCode + ": " + await update.Content.ReadAsStringAsync());
    }

    string query = await client.GetStringAsync("datasets/smoke/sparql?query=" + Uri.EscapeDataString("SELECT ?o WHERE { <http://ex/a> <http://ex/p> ?o }"));

    if (!query.Contains("\"value\":\"smoke\"", StringComparison.Ordinal))
    {
        return Fail("the query answered " + query);
    }

    string feed = await client.GetStringAsync("datasets/smoke/feed?from=1&to=2");

    if (!feed.StartsWith("commit 2 Data ", StringComparison.Ordinal) || !feed.Contains("+ <http://ex/a> <http://ex/p> \"smoke\"\n", StringComparison.Ordinal))
    {
        return Fail("the feed answered " + feed);
    }

    // The command line against the server: the HTTP client and the results
    // reader under AOT, with no token in anonymous mode.
    string? remote = await CliAsync("o\ncli\nsmoke\n", "query", await address.Task + "datasets/smoke/", "-q", "SELECT ?o WHERE { <http://ex/a> <http://ex/p> ?o } ORDER BY ?o", "-f", "csv");

    if (remote is not null)
    {
        return Fail(remote);
    }

    if (OperatingSystem.IsWindows())
    {
        server.Kill();
        await server.WaitForExitAsync();
    }
    else
    {
        using Process kill = Process.Start("kill", ["-TERM", server.Id.ToString(CultureInfo.InvariantCulture)])!;
        await kill.WaitForExitAsync();

        if (!server.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            return Fail("the server did not stop within 30 s of SIGTERM");
        }

        if (server.ExitCode != 0)
        {
            return Fail("the server exited with " + server.ExitCode + " after SIGTERM");
        }
    }

    long size = new FileInfo(binary).Length;
    string table = string.Create(CultureInfo.InvariantCulture, $"""
        | Varve.Server, Native AOT | {Environment.OSVersion.Platform} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} |
        |---|---:|
        | binary | {size:N0} bytes ({size / 1048576.0:F1} MiB) |
        | process start to `/ready` 200 | {toReady.TotalMilliseconds:F0} ms |

        """);
    Console.WriteLine(table);
    string? summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

    if (!string.IsNullOrEmpty(summary))
    {
        await File.AppendAllTextAsync(summary, table);
    }

    return 0;
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch (IOException)
    {
        // A killed server on Windows may hold its lease file for a moment; the
        // runner's temporary directory is discarded with it.
    }
}
