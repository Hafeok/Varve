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
/// <c>varve apply</c> (ADRs 0130, 0131) against the server's own composition
/// root, under OIDC with the issuer in the test: the first apply creates the
/// dataset and commits its settings, its baseline and its migrations; the
/// second commits nothing; a file outside the subset exits 2 with its line and
/// column; a refusal exits 1 with the problem.
/// </summary>
public sealed class ApplyTests : IDisposable
{
    private static readonly string[] Operator = ["operator"];
    private static readonly string[] Writer = ["writer"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "varve-apply-" + Guid.NewGuid().ToString("N"));

    public ApplyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Apply_creates_the_dataset_then_finds_it_as_declared()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string setup = Write("setup.yaml", """
            dataset: orders
            storage: Memory
            settings: |
              {"defaultAccessScope": "Current"}
            baseline:
              - file: vocabulary.ttl
                graph: https://shop.example/vocabulary
            migrations:
              - id: 2026-10-01-first
                update: |
                  INSERT DATA { <https://shop.example/orders/1> <https://shop.example/ns#status> "placed" }
            """);
        Write("vocabulary.ttl", "<https://shop.example/ns#Order> a <http://www.w3.org/2000/01/rdf-schema#Class> .\n");
        string token = issuer.Mint(new Dictionary<string, object> { ["sub"] = "deployer", ["roles"] = Operator });

        Run first = await RunAsync("apply", setup, server.Address.ToString(), "--token", token);
        Assert.True(first.Exit == 0, first.Error);
        Assert.Contains("orders: created; settings changed; baseline vocabulary.ttl committed; migration 2026-10-01-first applied; at position 3", first.Out, StringComparison.Ordinal);

        Run second = await RunAsync("apply", setup, server.Address + "datasets/orders/", "--token", token);
        Assert.Equal(0, second.Exit);
        Assert.Contains("orders: as declared already, at position 3; nothing committed", second.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_outside_the_subset_exits_2_with_its_line_and_column()
    {
        string setup = Write("setup.yaml", "dataset: orders\nbaseline: [vocabulary.ttl]\n");
        Run outside = await RunAsync("apply", setup, "http://127.0.0.1:9/", "--token", "unused");
        Assert.Equal(2, outside.Exit);
        Assert.Contains("setup.yaml:2:11: a flow collection ('[') is outside the subset", outside.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refusal_exits_1_with_the_problem()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string setup = Write("setup.yaml", "dataset: elsewhere\nstorage: Memory\n");

        // A writer of d is no server admin: the dataset cannot be created.
        Run refused = await RunAsync("apply", setup, server.Address.ToString(), "--token", issuer.Mint(new Dictionary<string, object> { ["sub"] = "w", ["roles"] = Writer }));
        Assert.Equal(1, refused.Exit);
        Assert.Contains("dataset: the server refused (403", refused.Error, StringComparison.Ordinal);
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
        ["Varve:Auth:Datasets:d:Write:0"] = "writer",
        ["Varve:Datasets:d:Storage"] = "Memory",
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
