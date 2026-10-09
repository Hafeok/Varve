// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The <c>varve</c> command line (ADR 0105): the embedded flow over a
/// directory with no authentication, the same commands against a running
/// server with a token from each of the three ways a token is had, and the
/// credential file's refresh-first path and its mode check.
/// </summary>
public sealed class CliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "varve-cli-" + Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task the_embedded_flow_creates_loads_queries_updates_exports_checkpoints_and_feeds()
    {
        string dataset = Path.Combine(_root, "ds");
        string file = Path.Combine(_root, "d.nq");
        await File.WriteAllTextAsync(file, "<http://ex/a> <http://ex/p> \"x\" .\n<http://ex/b> <http://ex/p> <http://ex/c> <http://ex/g> .\n", TestContext.Current.CancellationToken);

        Run created = await RunAsync("create", dataset);
        Assert.Equal(0, created.Exit);
        Assert.StartsWith("created ", created.Out, StringComparison.Ordinal);

        Run loaded = await RunAsync("load", dataset, file);
        Assert.Equal(0, loaded.Exit);
        Assert.Contains("2 quads", loaded.Out, StringComparison.Ordinal);
        Assert.Contains("committed, position 1", loaded.Out, StringComparison.Ordinal);

        Run queried = await RunAsync("query", dataset, "-q", "SELECT * WHERE { GRAPH ?g { ?s ?p ?o } }", "-f", "csv");
        Assert.Equal(0, queried.Exit);
        Assert.Equal("g,s,p,o\r\nhttp://ex/g,http://ex/b,http://ex/p,http://ex/c\r\n", queried.Data);

        Run asked = await RunAsync("query", dataset, "-q", "ASK { <http://ex/a> ?p ?o }");
        Assert.Equal(0, asked.Exit);
        Assert.Contains("\"boolean\":true", asked.Data, StringComparison.Ordinal);

        Run constructed = await RunAsync("query", dataset, "-q", "CONSTRUCT WHERE { <http://ex/a> ?p ?o }");
        Assert.Equal("<http://ex/a> <http://ex/p> \"x\" .\n", constructed.Data);

        Run updated = await RunAsync("update", dataset, "-u", "INSERT DATA { <http://ex/d> <http://ex/p> 1 }");
        Assert.Equal(0, updated.Exit);
        Assert.Equal("committed, position 2\n", updated.Out);

        Run conflicted = await RunAsync("update", dataset, "-u", "INSERT DATA { <http://ex/e> <http://ex/p> 1 }", "--if-match", "1");
        Assert.Equal(1, conflicted.Exit);
        Assert.StartsWith("conflict:", conflicted.Out, StringComparison.Ordinal);

        Run exported = await RunAsync("export", dataset);
        Assert.Equal(0, exported.Exit);
        Assert.Equal(3, exported.Data.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("<http://ex/b> <http://ex/p> <http://ex/c> <http://ex/g> .", exported.Data, StringComparison.Ordinal);

        Run graph = await RunAsync("export", dataset, "--graph", "http://ex/g", "-f", "ttl");
        Assert.Equal("<http://ex/b> <http://ex/p> <http://ex/c> .\n", graph.Data);

        Run past = await RunAsync("export", dataset, "--graph", "default", "--as-of", "position:1");
        Assert.Equal("<http://ex/a> <http://ex/p> \"x\" .\n", past.Data);

        Run checkpointed = await RunAsync("checkpoint", dataset);
        Assert.Equal("checkpoint at 2\n", checkpointed.Out);

        Run info = await RunAsync("info", dataset);
        Assert.Equal(0, info.Exit);
        Assert.Contains("\"head\": 2", info.Data, StringComparison.Ordinal);
        Assert.Contains("\"checkpoints\": [\n    2\n  ]", info.Data.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

        Run feed = await RunAsync("feed", dataset, "--graph", "default");
        Assert.Equal(0, feed.Exit);
        Assert.Contains("commit 1 Data", feed.Data, StringComparison.Ordinal);
        Assert.Contains("+ <http://ex/a> <http://ex/p> \"x\"\n", feed.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("<http://ex/g>", feed.Data, StringComparison.Ordinal);
        Assert.Contains("commit 2 Data", feed.Data, StringComparison.Ordinal);

        Run bounded = await RunAsync("feed", dataset, "--from", "1", "--to", "2");
        Assert.DoesNotContain("commit 1", bounded.Data, StringComparison.Ordinal);
        Assert.Contains("commit 2", bounded.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_failure_is_one_line_on_standard_error_and_exit_1_and_a_usage_error_exit_2()
    {
        Run missing = await RunAsync("query", Path.Combine(_root, "nowhere"), "-q", "ASK {}");
        Assert.Equal(1, missing.Exit);
        Assert.StartsWith("varve: ", missing.Error, StringComparison.Ordinal);
        Assert.Contains("varve create", missing.Error, StringComparison.Ordinal);

        Run unknown = await RunAsync("frobnicate");
        Assert.Equal(Cli.UsageError, unknown.Exit);

        Run noQuery = await RunAsync("query", _root);
        Assert.Equal(1, noQuery.Exit);
        Assert.Contains("--query", noQuery.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task against_a_server_each_token_source_works_and_no_token_is_401()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        issuer.IssuedClaims = new Dictionary<string, object> { ["sub"] = "cli", ["roles"] = new[] { "writer", "administrator" } };
        await using RunningServer server = await StartServerAsync(issuer);
        string url = server.Address + "datasets/d/";
        string credentials = Path.Combine(_root, "credentials.json");

        Run anonymous = await RunAsync("query", url, "-q", "ASK {}");
        Assert.Equal(1, anonymous.Exit);
        Assert.Contains("401", anonymous.Error, StringComparison.Ordinal);

        // --token: used as given, nothing stored.
        Run given = await RunAsync("update", url, "-u", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }", "--token", issuer.Mint(Claims("writer")));
        Assert.Equal(0, given.Exit);
        Assert.Contains("position 1", given.Out, StringComparison.Ordinal);
        Assert.False(File.Exists(credentials));

        // Client credentials: the secret from the option; no refresh token is issued, so nothing is stored.
        Run confidential = await RunAsync("query", url, "-q", "SELECT (COUNT(*) AS ?n) WHERE { ?s ?p ?o }", "-f", "csv", "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--client-secret", TestIssuer.ClientSecret, "--credentials", credentials);
        Assert.Equal(0, confidential.Exit);
        Assert.Equal("n\r\n1\r\n", confidential.Data);
        Assert.False(File.Exists(credentials));

        Run wrongSecret = await RunAsync("query", url, "-q", "ASK {}", "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--client-secret", "nope", "--credentials", credentials);
        Assert.Equal(1, wrongSecret.Exit);
        Assert.Contains("invalid_client", wrongSecret.Error, StringComparison.Ordinal);

        // The device code flow: the prompt names the page and the code; the
        // test approves as the person would; the refresh token lands in the file.
        Task<Run> device = RunAsync("export", url, "--graph", "default", "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--credentials", credentials);

        while (issuer.DeviceCodesIssued == 0)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        issuer.Approve();
        Run approved = await device;
        Assert.Equal(0, approved.Exit);
        Assert.Contains("Open " + issuer.Issuer + "activate and enter the code ABCD-EFGH.", approved.Out, StringComparison.Ordinal);
        Assert.Contains("<http://ex/a> <http://ex/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> .", approved.Data, StringComparison.Ordinal);
        Assert.True(File.Exists(credentials));
        // Read back through the file's own reader: on Windows the bytes are DPAPI-protected (ADR 0105).
        CredentialFile file = new(credentials);
        string? stored = file.TryRead(new Uri(url, UriKind.Absolute), issuer.Issuer, TestIssuer.ClientId);
        Assert.NotNull(stored);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(credentials));
        }

        // Next time the refresh token is used first: no device code is issued, the file is rotated.
        Run refreshed = await RunAsync("query", url, "-q", "ASK { <http://ex/a> ?p ?o }", "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--credentials", credentials);
        Assert.Equal(0, refreshed.Exit);
        Assert.Equal(1, issuer.DeviceCodesIssued);
        Assert.NotEqual(stored, file.TryRead(new Uri(url, UriKind.Absolute), issuer.Issuer, TestIssuer.ClientId));

        // --no-store keeps nothing: a second device code flow, and the file is as it was.
        string before = await File.ReadAllTextAsync(credentials, TestContext.Current.CancellationToken);
        Task<Run> ci = RunAsync("info", url, "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--credentials", Path.Combine(_root, "other.json"), "--no-store");

        while (issuer.DeviceCodesIssued < 2)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        issuer.Approve();
        Run kept = await ci;
        Assert.True(kept.Exit == 0, kept.Error);
        Assert.False(File.Exists(Path.Combine(_root, "other.json")));
        Assert.Equal(before, await File.ReadAllTextAsync(credentials, TestContext.Current.CancellationToken));

        // A refresh token the issuer no longer knows is dropped and the flow starts over.
        await File.WriteAllTextAsync(credentials, before.Replace(ExtractToken(before), "stale", StringComparison.Ordinal), TestContext.Current.CancellationToken);
        Task<Run> again = RunAsync("info", url, "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--credentials", credentials);

        while (issuer.DeviceCodesIssued < 3)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        issuer.Approve();
        Run recovered = await again;
        Assert.True(recovered.Exit == 0, recovered.Error);
        Assert.DoesNotContain("stale", await File.ReadAllTextAsync(credentials, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // The remote feed and checkpoint go through the server's endpoints.
        string token = issuer.Mint(Claims("administrator"));
        Run feed = await RunAsync("feed", url, "--token", token);
        Assert.Equal(0, feed.Exit);
        Assert.Contains("commit 1 Data", feed.Data, StringComparison.Ordinal);
        Run checkpoint = await RunAsync("checkpoint", url, "--token", token);
        Assert.Equal(0, checkpoint.Exit);
        Run forbidden = await RunAsync("checkpoint", url, "--token", issuer.Mint(Claims("writer")));
        Assert.Equal(1, forbidden.Exit);
        Assert.Contains("403", forbidden.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_credential_file_readable_by_others_is_refused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string credentials = Path.Combine(_root, "loose.json");
        await File.WriteAllTextAsync(credentials, "{\"refreshTokens\":[]}", TestContext.Current.CancellationToken);
        File.SetUnixFileMode(credentials, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Run run = await RunAsync("info", "http://127.0.0.1:9/datasets/d/", "--authority", "http://127.0.0.1:9/", "--client-id", "c", "--credentials", credentials);
        Assert.Equal(1, run.Exit);
        Assert.Contains("readable by others", run.Error, StringComparison.Ordinal);
    }

    private static Dictionary<string, object> Claims(string role) => new() { ["sub"] = "x", ["roles"] = new[] { role } };

    private static string ExtractToken(string json)
    {
        const string Key = "\"refreshToken\":\"";
        int start = json.IndexOf(Key, StringComparison.Ordinal) + Key.Length;
        return json[start..json.IndexOf('"', start)];
    }

    private static Task<RunningServer> StartServerAsync(TestIssuer issuer) => RunningServer.StartAsync(new Dictionary<string, string>
    {
        ["Varve:Auth:Mode"] = "Oidc",
        ["Varve:Auth:Authority"] = issuer.Issuer,
        ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
        ["Varve:Auth:RequireHttpsMetadata"] = "false",
        ["Varve:Auth:Datasets:d:Write:0"] = "writer",
        ["Varve:Auth:Datasets:d:Admin:0"] = "administrator",
        ["Varve:Datasets:d:Storage"] = "Memory",
    });

    private sealed record Run(int Exit, string Out, string Error, string Data);

    private static async Task<Run> RunAsync(params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        MemoryStream data = new();
        int exit = await Cli.RunAsync(args, new Io(output, error, data), TestContext.Current.CancellationToken);
        return new Run(exit, output.ToString(), error.ToString(), Encoding.UTF8.GetString(data.ToArray()));
    }
}
