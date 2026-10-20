// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// <c>varve apply</c> (ADR 0163 point 5) against the real host: the plan, the
/// commit, an idempotent second apply, the drift check's exit code, a saved
/// plan refused once its graph moved and applied while another graph moved,
/// workload identity federation for the token, and the TriG that replaces
/// the removed configuration keys.
/// </summary>
public sealed class ApplyTests : IDisposable
{
    private const string Notes = """
        @prefix varve: <https://w3id.org/varve/ns#> .
        <https://w3id.org/varve/ns#manage/dataset/notes> {
          <https://notes.example/manage/notes> a varve:Dataset ;
            varve:datasetName "notes" ; varve:storage varve:Memory .
          <https://notes.example/manage/users> a varve:Role ; varve:claimValue "Notes.User" .
          <https://notes.example/manage/users-write> a varve:Grant ;
            varve:grantee <https://notes.example/manage/users> ; varve:permission varve:write ; varve:allGraphs true .
        }
        """;

    private const string Shop = """
        @prefix varve: <https://w3id.org/varve/ns#> .
        <https://w3id.org/varve/ns#manage/dataset/shop> {
          <https://shop.example/manage/shop> a varve:Dataset ; varve:datasetName "shop" ; varve:storage varve:Memory .
        }
        """;

    private static readonly string[] NotesUser = ["Notes.User"];
    private static readonly string[] OrdersReader = ["Orders.Reader"];

    private readonly string _root = Directory.CreateTempSubdirectory("varve-apply-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_plan_applied_once_leaves_nothing_to_apply_and_the_drift_check_says_so()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string notes = Write("notes.trig", Notes);
        string token = issuer.MintManagement(Operator());

        Run drift = await RunAsync("apply", notes, "--server", server.Address.ToString(), "--plan", "--exit-code", "--token", token);
        Assert.True(drift.Exit == Apply.Drift, drift.Error);
        Assert.Contains("+ <https://notes.example/manage/notes> <https://w3id.org/varve/ns#datasetName> \"notes\"", drift.Out, StringComparison.Ordinal);

        Run applied = await RunAsync("apply", notes, "--server", server.Address.ToString(), "--token", token);
        Assert.True(applied.Exit == 0, applied.Error);
        Assert.Contains("Applied at system position", applied.Out, StringComparison.Ordinal);

        Run again = await RunAsync("apply", notes, "--server", server.Address.ToString(), "--token", token);
        Assert.Equal(0, again.Exit);
        Assert.Contains("Nothing to apply", again.Out, StringComparison.Ordinal);
        Assert.Equal(0, (await RunAsync("apply", notes, "--server", server.Address.ToString(), "--plan", "--exit-code", "--token", token)).Exit);

        // The grant is in force on the data plane, for a data-plane token.
        using HttpRequestMessage write = new(HttpMethod.Post, new Uri("datasets/notes/sparql", UriKind.Relative))
        {
            Content = new StringContent("INSERT DATA { <http://ex/a> <http://ex/p> 1 }", Encoding.UTF8, "application/sparql-update"),
        };
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Mint(new Dictionary<string, object> { ["sub"] = "u", ["roles"] = NotesUser }));
        Assert.Equal(HttpStatusCode.NoContent, (await server.Client.SendAsync(write, TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>
    /// A saved plan is applied under the versions it read: refused once its
    /// graph moved; applied when only another graph moved, since applies to
    /// disjoint graphs never conflict.
    /// </summary>
    [Fact]
    public async Task A_saved_plan_is_refused_once_its_graph_moved_and_applied_when_another_did()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string token = issuer.MintManagement(Operator());
        string url = server.Address.ToString();
        string notes = Write("notes.trig", Notes);
        string shop = Write("shop.trig", Shop);
        string notesPlan = Path.Combine(_root, "notes.plan.json");
        string shopPlan = Path.Combine(_root, "shop.plan.json");

        Assert.Equal(0, (await RunAsync("apply", notes, "--server", url, "--plan", "--out", notesPlan, "--token", token)).Exit);
        Assert.Equal(0, (await RunAsync("apply", shop, "--server", url, "--plan", "--out", shopPlan, "--token", token)).Exit);

        // shop moves first; the notes plan, of another graph, still applies.
        Assert.Equal(0, (await RunAsync("apply", "--from-plan", shopPlan, "--server", url, "--token", token)).Exit);
        Run disjoint = await RunAsync("apply", "--from-plan", notesPlan, "--server", url, "--token", token);
        Assert.True(disjoint.Exit == 0, disjoint.Error);

        // Now the notes graph moves under a plan made before: refused.
        string stale = Path.Combine(_root, "stale.plan.json");
        Write("notes.trig", Notes.Replace("varve:storage varve:Memory .", "varve:storage varve:Memory ; varve:defaultAccessScope varve:Current .", StringComparison.Ordinal));
        Assert.Equal(0, (await RunAsync("apply", notes, "--server", url, "--plan", "--out", stale, "--token", token)).Exit);
        Write("notes.trig", Notes.Replace("varve:storage varve:Memory .", "varve:storage varve:Memory ; varve:defaultAccessScope varve:AllHistory .", StringComparison.Ordinal));
        Assert.Equal(0, (await RunAsync("apply", notes, "--server", url, "--token", token)).Exit);
        Run refused = await RunAsync("apply", "--from-plan", stale, "--server", url, "--token", token);
        Assert.Equal(1, refused.Exit);
        Assert.Contains("moved since the plan read it", refused.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// CI authenticates by workload identity federation (ADR 0163 point 5):
    /// the platform's token is the client's assertion, exchanged for a token
    /// for the management audience. No secret is given.
    /// </summary>
    [Fact]
    public async Task Apply_authenticates_by_workload_identity_federation()
    {
        await using TestIssuer issuer = await TestIssuer.StartAsync();
        issuer.IssuedClaims = Operator();
        issuer.FederatedAssertion = "platform-token-" + Guid.NewGuid().ToString("N");
        await using RunningServer server = await StartServerAsync(issuer);
        string assertion = Write("federated-token", issuer.FederatedAssertion + "\n");
        string notes = Write("notes.trig", Notes);

        Run applied = await RunAsync("apply", notes, "--server", server.Address.ToString(), "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--client-assertion", assertion, "--scope", TestIssuer.ManagementAudience);
        Assert.True(applied.Exit == 0, applied.Error);

        Write("federated-token", "not-the-platforms");
        Run refused = await RunAsync("apply", notes, "--server", server.Address.ToString(), "--authority", issuer.Issuer, "--client-id", TestIssuer.ClientId, "--client-assertion", assertion, "--scope", TestIssuer.ManagementAudience);
        Assert.Equal(1, refused.Exit);
        Assert.Contains("invalid_client", refused.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// --from-config prints the TriG that replaces the removed keys (ADR
    /// 0163), and the server takes it: the dataset and its grants as they were.
    /// </summary>
    [Fact]
    public async Task From_config_prints_the_declarations_that_replace_the_removed_keys()
    {
        string config = Write("appsettings.json", """
            {"Varve":{
              "Datasets":{"orders":{"Storage":"Memory"}},
              "Auth":{"Datasets":{"orders":{"Read":["Orders.Reader"],"Grants":[{"Claim":"people","Permission":"write","Graphs":["default"]}]}},
                      "Server":{"Admin":["Varve.Operator"]}}}}
            """);
        Run printed = await RunAsync("apply", "--from-config", config);
        Assert.True(printed.Exit == 0, printed.Error);
        Assert.Contains("<https://w3id.org/varve/ns#manage/dataset/orders>", printed.Out, StringComparison.Ordinal);
        Assert.Contains("Varve:Manage:BootstrapOperators", printed.Error, StringComparison.Ordinal);

        await using TestIssuer issuer = await TestIssuer.StartAsync();
        await using RunningServer server = await StartServerAsync(issuer);
        string declarations = Write("orders.trig", printed.Out);
        Run applied = await RunAsync("apply", declarations, "--server", server.Address.ToString(), "--token", issuer.MintManagement(Operator()));
        Assert.True(applied.Exit == 0, applied.Error);

        using HttpRequestMessage read = new(HttpMethod.Get, new Uri("datasets/orders/sparql?query=ASK%7B%7D", UriKind.Relative));
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Mint(new Dictionary<string, object> { ["sub"] = "r", ["roles"] = OrdersReader }));
        Assert.Equal(HttpStatusCode.OK, (await server.Client.SendAsync(read, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Apply_and_setup_apply_are_two_commands_that_share_no_name()
    {
        Run help = await RunAsync("--help");
        Assert.Matches(@"(?m)^\s+apply\b.*management declarations", help.Out);
        Assert.Matches(@"(?m)^\s+setup\b", help.Out);

        // A setup file is not a declaration: apply refuses it, naming why.
        string setup = Write("setup.yaml", "dataset: orders\n");
        Run refused = await RunAsync("apply", setup, "--server", "http://127.0.0.1:9/", "--token", "unused");
        Assert.Equal(1, refused.Exit);
    }

    private static Dictionary<string, object> Operator() => new() { ["sub"] = "deployer", ["roles"] = new[] { "operator" } };

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
        ["Varve:Manage:BootstrapOperators:0"] = "operator",
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
