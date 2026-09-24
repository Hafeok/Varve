// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RepoStandard.GitHub;
using RepoStandard.Json;
using RepoStandard.Tests.Support;
using Xunit;

namespace RepoStandard.Tests;

/// <summary>
/// Every scenario in <c>fixtures/recorded</c>: the declaration, the command,
/// the exchanges in the order they must happen, and what was printed.
/// </summary>
/// <remarks>
/// The <c>*-check</c> scenarios are the diff engine over recorded live state,
/// one per resource kind, each with something added by hand. The
/// <c>*-apply</c> scenarios are the contract tests for the writes: a request
/// that differs in method, path, query or body from the recording fails the
/// test, and so does one that is missing or extra. Where each recording comes
/// from is written in it, and <c>fixtures/README.md</c> says how they were made.
/// Set REPO_STANDARD_ACCEPT=1 to rewrite the <c>.out</c> files after a change
/// that is meant to change them, and read the diff before committing it.
/// </remarks>
public sealed class RecordedTests
{
    public static TheoryData<string> Scenarios() => [.. Recording.All().Select(r => r.Name)];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Scenario_runs_as_recorded(string name)
    {
        Recording recording = Recording.Load(Path.Combine(Recording.Directory, name + ".json"));
        (string actual, ReplayHandler replay) = await RunAsync(recording);

        Assert.True(replay.Mismatches.Count == 0, string.Join("\n", replay.Mismatches));
        Assert.True(replay.Remaining.Count == 0,
            "recorded exchanges never requested:\n" + string.Join("\n", replay.Remaining.Select(e => e.Describe())));

        if (Environment.GetEnvironmentVariable("REPO_STANDARD_ACCEPT") == "1")
        {
            await File.WriteAllTextAsync(Path.Combine(SourceDirectory(), "fixtures", "recorded", name + ".out"), actual, TestContext.Current.CancellationToken);
        }

        Assert.Equal(recording.ExpectOutput, actual);
        Assert.StartsWith($"exit: {recording.ExpectExit}\n", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(TestHost.Token, actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_endpoint_and_operation_the_tool_uses_has_a_recorded_exchange()
    {
        List<Exchange> exchanges = [.. Recording.All().SelectMany(r => r.Exchanges)];

        HashSet<Endpoint> covered = [.. exchanges.Where(e => e.Operation is null).Select(EndpointMatch.Of).OfType<Endpoint>()];
        Endpoint[] uncovered = [.. Endpoints.All.Where(e => e != Endpoints.GraphQl && !covered.Contains(e))];
        Assert.True(uncovered.Length == 0, "no recorded exchange for: " + string.Join(", ", uncovered.Select(e => e.ToString())));

        HashSet<string> operations = [.. exchanges.Select(e => e.Operation).OfType<string>()];
        string[] missing = [.. GraphQlOperations.All.Select(o => o.Name).Where(n => !operations.Contains(n))];
        Assert.True(missing.Length == 0, "no recorded exchange for GraphQL: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_recorded_request_is_one_the_tool_catalogues()
    {
        // The second page of a list is requested at the URL GitHub's Link header
        // gives, which names the repository by id; it is the same endpoint.
        string[] strays = [.. Recording.All()
            .SelectMany(r => r.Exchanges.Select(e => (r.Name, Exchange: e)))
            .Where(x => x.Exchange.Operation is null
                && EndpointMatch.Of(x.Exchange) is null
                && !x.Exchange.Path.StartsWith("/repositories/", StringComparison.Ordinal))
            .Select(x => $"{x.Name}: {x.Exchange.Describe()}")];

        Assert.True(strays.Length == 0, "recorded requests matching no catalogued endpoint:\n" + string.Join("\n", strays));
    }

    internal static async Task<(string Output, ReplayHandler Replay)> RunAsync(Recording recording)
    {
        string directory = Directory.CreateTempSubdirectory("repo-standard-recorded-").FullName;
        try
        {
            string file = Path.Combine(directory, recording.Command[0] == "export" ? "exported.yaml" : "declaration.yaml");
            if (recording.Command[0] != "export")
            {
                await File.WriteAllTextAsync(file, recording.Declaration);
            }

            ReplayHandler replay = new(recording.Exchanges);
            TestHost host = new();
            string[] args = [recording.Command[0], "--repo", Recording.Repository, "--file", file, .. recording.Command.Skip(1)];
            int exit = await host.RunAsync(replay, args);

            StringBuilder output = new();
            output.Append("exit: ").Append(exit).Append('\n');
            output.Append("--- stdout\n").Append(host.Output.ToString().Replace(file, "<file>", StringComparison.Ordinal));
            output.Append("--- stderr\n").Append(host.Error.ToString().Replace(file, "<file>", StringComparison.Ordinal));

            if (host.Delay.Waits.Count > 0)
            {
                output.Append("--- waits\n").Append(string.Join("\n", host.Delay.Waits.Select(w => w.ToString()))).Append('\n');
            }

            if (recording.Command[0] == "export" && File.Exists(file))
            {
                output.Append("--- file\n").Append(await File.ReadAllTextAsync(file));
            }

            return (output.ToString(), replay);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>
/// Keeps the in-memory GitHub honest: every field it answers with is one
/// GitHub's published response schema has, at the same path.
/// </summary>
public sealed partial class FakeConformanceTests
{
    [Fact]
    public async Task The_fake_answers_every_GET_with_fields_GitHub_documents()
    {
        JsonObject document = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "schema-keys.json"), TestContext.Current.CancellationToken))!;
        FakeGitHub fake = Rich();
        using System.Net.Http.HttpClient http = new(fake);
        List<string> problems = [];

        foreach ((string endpoint, JsonNode? keys) in (JsonObject)document["keys"]!)
        {
            HashSet<string> documented = [.. ((JsonArray)keys!).Select(k => k!.GetValue<string>())];
            string template = endpoint[(endpoint.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            string path = Placeholder().Replace(template, m => m.Value switch
            {
                "{owner}" or "{org}" => "octo-org",
                "{repo}" => "hello-world",
                "{environment_name}" => "release",
                "{ruleset_id}" => fake.Rulesets.Keys.First().ToString(System.Globalization.CultureInfo.InvariantCulture),
                "{username}" => "alice",
                "{team_slug}" => "core",
                _ => m.Value,
            });

            using System.Net.Http.HttpResponseMessage response = await http.GetAsync(new Uri("https://api.github.com" + path), TestContext.Current.CancellationToken);
            string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                problems.Add($"{endpoint}: {(int)response.StatusCode}");
                continue;
            }

            HashSet<string> answered = [];
            KeyPaths(JsonNode.Parse(text), string.Empty, answered);
            foreach (string key in answered.Where(k => !documented.Contains(k) && !UnderFreeForm(k)))
            {
                problems.Add($"{endpoint}: '{key}' is not in GitHub's response schema");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Rule parameters are GitHub's shape and the schema describes them per rule type.</summary>
    private static bool UnderFreeForm(string key) => key.Contains("rules[].parameters.", StringComparison.Ordinal);

    private static FakeGitHub Rich()
    {
        FakeGitHub fake = new();
        fake.Topics.Add("rdf");
        fake.Labels.Add(new JsonObject { ["id"] = 1, ["name"] = "bug", ["color"] = "d73a4a", ["description"] = "Broken", ["default"] = true });
        fake.Secrets.Add("NUGET_API_KEY");
        fake.ActionsPermissions["allowed_actions"] = "selected";
        fake.Rulesets[7] = (JsonObject)JsonNode.Parse("""
            {"name":"main","target":"branch","enforcement":"active","bypass_actors":[{"actor_id":5,"actor_type":"RepositoryRole","bypass_mode":"always"}],
             "conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"rules":[{"type":"deletion"}]}
            """)!;

        FakeEnvironment release = new() { WaitTimer = 5, PreventSelfReview = true };
        release.BranchPolicy = new JsonObject { ["protected_branches"] = false, ["custom_branch_policies"] = true };
        release.Policies.Add((9, "v*", "tag"));
        release.Secrets.Add("SIGNING_KEY");
        fake.Environments["release"] = release;

        using System.Net.Http.HttpClient http = new(fake, disposeHandler: false);
        long user = JsonTree.Int(JsonNode.Parse(http.GetStringAsync(new Uri("https://api.github.com/users/alice")).GetAwaiter().GetResult()), "id")!.Value;
        long team = JsonTree.Int(JsonNode.Parse(http.GetStringAsync(new Uri("https://api.github.com/orgs/octo-org/teams/core")).GetAwaiter().GetResult()), "id")!.Value;
        release.Reviewers = [("User", user), ("Team", team)];
        return fake;
    }

    private static void KeyPaths(JsonNode? node, string prefix, HashSet<string> keys)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string name, JsonNode? value) in obj)
                {
                    string path = prefix.Length == 0 ? name : prefix + "." + name;
                    keys.Add(path);
                    KeyPaths(value, path, keys);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    KeyPaths(item, prefix + "[]", keys);
                }

                break;
            default:
                break;
        }
    }

    [GeneratedRegex(@"\{[a-z_]+\}")]
    private static partial Regex Placeholder();
}
