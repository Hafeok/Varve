// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The server as an operator meets it (ADR 0101): configuration validated at
/// start, anonymous mode explicit and refused in production, readiness, and a
/// shutdown that ends live feeds and loses no commit.
/// </summary>
public sealed class HostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void An_empty_configuration_lists_every_error_at_once()
    {
        List<string> errors = SettingsCheck.Errors(new ServerSettings());
        Assert.Contains(errors, e => e.StartsWith("Varve:Auth:Mode", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Varve:Datasets names", StringComparison.Ordinal));
    }

    [Fact]
    public void A_given_value_that_is_wrong_is_an_error_and_never_a_default()
    {
        ServerSettings settings = new()
        {
            DatasetsRoot = "relative/path",
            Datasets =
            {
                ["-bad"] = new DatasetSettings { Storage = "Memory" },
                ["files"] = new DatasetSettings { Storage = "File" },
                ["odd"] = new DatasetSettings { Storage = "Tape" },
            },
            Auth =
            {
                Mode = "Oidc",
                Authority = "http://issuer.example/",
                Datasets = { ["ghost"] = new PermissionSettings() },
            },
            Limits = { QueryTimeout = TimeSpan.Zero, ResultSizeCap = -1 },
            ForwardedHeaders = { Enabled = true, KnownProxies = { "not-an-address" } },
        };

        List<string> errors = SettingsCheck.Errors(settings);
        string[] expected =
        [
            "Varve:Auth:Authority",
            "Varve:Auth:Audiences",
            "Varve:DatasetsRoot is an absolute path",
            "Varve:Datasets:-bad is not a dataset name",
            "Varve:Datasets:files is a File dataset",
            "Varve:Datasets:odd:Storage",
            "Varve:Auth:Datasets:ghost",
            "Varve:Limits durations",
            "Varve:Limits sizes",
            "Varve:ForwardedHeaders:KnownProxies has not-an-address",
        ];

        foreach (string start in expected)
        {
            Assert.Contains(errors, e => e.StartsWith(start, StringComparison.Ordinal));
        }

        Assert.Equal(expected.Length, errors.Count);
    }

    [Fact]
    public void Forwarded_headers_without_a_known_proxy_are_refused()
    {
        ServerSettings settings = Anonymous();
        settings.ForwardedHeaders.Enabled = true;
        Assert.Single(SettingsCheck.Errors(settings), e => e.StartsWith("Varve:ForwardedHeaders is enabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Anonymous_mode_warns_at_every_start()
    {
        (int _, string output, string _) = await RunProcessAsync(stopAfterReady: true, "--Varve:Auth:Mode=Anonymous", "--Varve:Datasets:d:Storage=Memory");
        Assert.Contains("Anonymous mode is on", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_mode_in_production_does_not_start()
    {
        (int exit, string _, string errors) = await RunProcessAsync(stopAfterReady: false, "--Varve:Auth:Mode=Anonymous", "--Varve:Auth:Production=true", "--Varve:Datasets:d:Storage=Memory");
        Assert.Equal(2, exit);
        Assert.Contains("Varve:Auth:Mode is Anonymous in a configuration marked Production", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_configuration_that_does_not_validate_exits_2_and_lists_its_errors()
    {
        (int exit, string _, string errors) = await RunProcessAsync(stopAfterReady: false, "--Varve:Datasets:d:Storage=Tape");
        Assert.Equal(2, exit);
        Assert.Contains("Varve:Auth:Mode is Oidc or Anonymous", errors, StringComparison.Ordinal);
        Assert.Contains("Varve:Datasets:d:Storage is File or Memory", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_address_nobody_serves_and_a_method_the_router_refuses_are_problems()
    {
        await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string> { ["Varve:Auth:Mode"] = "Anonymous", ["Varve:Datasets:d:Storage"] = "Memory" });
        HttpResponseMessage nowhere = await server.Client.GetAsync(new Uri("nowhere/at/all", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, nowhere.StatusCode);
        Assert.Equal("application/problem+json", nowhere.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/not-found\"", await nowhere.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        HttpResponseMessage method = await server.Client.PostAsync(new Uri("datasets/d", UriKind.Relative), new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);
        Assert.Equal("application/problem+json", method.Content.Headers.ContentType?.MediaType);
        Assert.Contains("method-not-allowed", await method.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_and_ready_answer_without_a_token()
    {
        await using RunningServer server = await RunningServer.StartAsync(OidcWithNoIssuer());
        HttpResponseMessage live = await server.Client.GetAsync(new Uri("live", UriKind.Relative), Ct);
        HttpResponseMessage ready = await server.Client.GetAsync(new Uri("ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("""{"status":"ready","datasets":{"d":"ready"}}""", await ready.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Shutdown_ends_a_live_feed_and_keeps_every_commit_for_the_next_start()
    {
        string root = Directory.CreateTempSubdirectory("varve-server-").FullName;

        try
        {
            Dictionary<string, string> settings = new()
            {
                ["Varve:Auth:Mode"] = "Anonymous",
                ["Varve:DatasetsRoot"] = root,
                ["Varve:Datasets:d:Storage"] = "File",
            };

            RunningServer first = await RunningServer.StartAsync(settings);
            await using (first)
            {
                HttpResponseMessage update = await first.Client.PostAsync(
                    new Uri("datasets/d/sparql", UriKind.Relative),
                    new StringContent("INSERT DATA { <http://ex/a> <http://ex/p> <http://ex/o> }", Encoding.UTF8, "application/sparql-update"),
                    Ct);
                Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

                using HttpRequestMessage tail = new(HttpMethod.Get, new Uri("datasets/d/commits", UriKind.Relative));
                tail.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using HttpResponseMessage feed = await first.Client.SendAsync(tail, HttpCompletionOption.ResponseHeadersRead, Ct);
                Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
                using StreamReader events = new(await feed.Content.ReadAsStreamAsync(Ct));
                Assert.Equal("id: 1", await events.ReadLineAsync(Ct));

                Task<int> stopped = first.StopAsync();
                string rest = await events.ReadToEndAsync(Ct);
                Assert.True(rest.EndsWith("id: 1\nevent: shutdown\ndata: error https://w3id.org/varve/problems/shutting-down\n\n", StringComparison.Ordinal), rest);
                Assert.Equal(0, await stopped);
            }

            await using RunningServer second = await RunningServer.StartAsync(settings);
            HttpResponseMessage ask = await second.Client.GetAsync(
                new Uri("datasets/d/sparql?query=" + Uri.EscapeDataString("ASK { <http://ex/a> <http://ex/p> <http://ex/o> }"), UriKind.Relative),
                Ct);
            Assert.Contains("\"boolean\":true", await ask.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServerSettings Anonymous() => new()
    {
        Datasets = { ["d"] = new DatasetSettings { Storage = "Memory" } },
        Auth = { Mode = "Anonymous" },
    };

    // An OIDC server whose issuer is never contacted: /live and /ready take no token.
    private static Dictionary<string, string> OidcWithNoIssuer() => new()
    {
        ["Varve:Auth:Mode"] = "Oidc",
        ["Varve:Auth:Authority"] = "https://issuer.invalid/",
        ["Varve:Auth:Audiences:0"] = "api://varve",
        ["Varve:Datasets:d:Storage"] = "Memory",
    };

    /// <summary>
    /// Runs the server as its own process, as an operator does, and answers
    /// its exit code, standard output and standard error. With
    /// <paramref name="stopAfterReady"/> it is killed once it listens: the
    /// graceful stop is the in-process tests' and the AOT smoke run's.
    /// </summary>
    private static async Task<(int Exit, string Output, string Errors)> RunProcessAsync(bool stopAfterReady, params string[] args)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"));
        start.ArgumentList.Add("--urls=http://127.0.0.1:0");

        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start)!;
        StringBuilder output = new();
        TaskCompletionSource listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line.Data);
            }

            if (line.Data.Contains("Now listening on", StringComparison.Ordinal))
            {
                listening.TrySetResult();
            }
        };
        process.BeginOutputReadLine();
        Task<string> errors = process.StandardError.ReadToEndAsync(Ct);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromSeconds(60));

        if (stopAfterReady)
        {
            await Task.WhenAny(listening.Task, process.WaitForExitAsync(limit.Token));

            if (!process.HasExited)
            {
                process.Kill();
            }
        }

        await process.WaitForExitAsync(limit.Token);
        string written;

        lock (output)
        {
            written = output.ToString();
        }

        return (process.ExitCode, written, await errors);
    }
}
