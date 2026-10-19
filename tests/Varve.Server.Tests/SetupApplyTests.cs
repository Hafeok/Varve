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
/// <c>varve setup apply</c> (ADRs 0130, 0131) against the server's own composition
/// root, under OIDC with the issuer in the test: the first apply creates the
/// dataset and commits its settings, its baseline and its migrations; the
/// second commits nothing; a file outside the subset exits 2 with its line and
/// column; a refusal exits 1 with the problem; and <c>varve apply</c>, the
/// Management plane's name (#105), is no command at all.
/// </summary>
public sealed class SetupApplyTests : IDisposable
{
    private static readonly string[] Writer = ["writer"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "varve-apply-" + Guid.NewGuid().ToString("N"));

    public SetupApplyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// A setup applies data to a dataset the management plane declares (ADR
    /// 0163 point 10): the baseline and the migrations, once; a second apply
    /// commits nothing.
    /// </summary>
    [Fact]
    public async Task Apply_finds_the_declared_dataset_and_applies_its_data_once()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string setup = Write("setup.yaml", """
            dataset: orders
            baseline:
              - file: vocabulary.ttl
                graph: https://shop.example/vocabulary
            migrations:
              - id: 2026-10-01-first
                update: |
                  INSERT DATA { <https://shop.example/orders/1> <https://shop.example/ns#status> "placed" }
            """);
        Write("vocabulary.ttl", "<https://shop.example/ns#Order> a <http://www.w3.org/2000/01/rdf-schema#Class> .\n");
        string token = issuer.Mint(new Dictionary<string, object> { ["sub"] = "deployer", ["roles"] = Writer });

        Run first = await RunAsync("setup", "apply", setup, server.Address.ToString(), "--token", token);
        Assert.True(first.Exit == 0, first.Error);
        Assert.Contains("orders: baseline vocabulary.ttl committed; migration 2026-10-01-first applied; at position 2", first.Out, StringComparison.Ordinal);

        Run second = await RunAsync("setup", "apply", setup, server.Address + "datasets/orders/", "--token", token);
        Assert.Equal(0, second.Exit);
        Assert.Contains("orders: as declared already, at position 2; nothing committed", second.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_outside_the_subset_exits_2_with_its_line_and_column()
    {
        string setup = Write("setup.yaml", "dataset: orders\nbaseline: [vocabulary.ttl]\n");
        Run outside = await RunAsync("setup", "apply", setup, "http://127.0.0.1:9/", "--token", "unused");
        Assert.Equal(2, outside.Exit);
        Assert.Contains("setup.yaml:2:11: a flow collection ('[') is outside the subset", outside.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_exits_1_with_the_problem()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string setup = Write("setup.yaml", "dataset: elsewhere\n");

        // A setup never creates a dataset: one the management plane does not
        // declare grants nothing, and is refused before anything is sent.
        Run refused = await RunAsync("setup", "apply", setup, server.Address.ToString(), "--token", issuer.Mint(new Dictionary<string, object> { ["sub"] = "w", ["roles"] = Writer }));
        Assert.Equal(1, refused.Exit);
        Assert.Contains("dataset: the server refused (403", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Varve_apply_is_no_command_and_no_alias()
    {
        string setup = Write("setup.yaml", "dataset: orders\nstorage: Memory\n");
        Run apply = await RunAsync("apply", setup, "http://127.0.0.1:9/", "--token", "unused");
        Assert.Equal(2, apply.Exit);
        Assert.Contains("'apply'", apply.Error, StringComparison.Ordinal);

        Run help = await RunAsync("--help");
        Assert.DoesNotMatch(@"(?m)^\s+apply\b", help.Out);
        Assert.Matches(@"(?m)^\s+setup\b", help.Out);
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static Task<RunningServer> StartServerAsync(TestIssuer issuer) => RunningServer.StartAsync(new Dictionary<string, string>
    {
        ["Varve:Auth:Mode"] = "Oidc",
        ["Varve:Auth:Authority"] = issuer.Issuer,
        ["Varve:Auth:Audiences:0"] = TestIssuer.Audience,
        ["Varve:Auth:RequireHttpsMetadata"] = "false",
        ["Varve:Auth:Server:Admin:0"] = "operator",
        ["Varve:Auth:Datasets:orders:Write:0"] = "writer",
        ["Varve:Datasets:orders:Storage"] = "Memory",
    });

    private sealed record Run(int Exit, string Out, string Error);

    private static async Task<Run> RunAsync(params string[] args)
    {
        StringWriter output = new() { NewLine = "\n" };
        StringWriter error = new() { NewLine = "\n" };
        int exit = await Cli.RunAsync(args, new Io(output, error, new MemoryStream()), TestContext.Current.CancellationToken);
        return new Run(exit, output.ToString(), error.ToString());
    }
}
