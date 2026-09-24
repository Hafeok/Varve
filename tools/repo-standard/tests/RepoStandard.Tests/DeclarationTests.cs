// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RepoStandard.Declaration;
using RepoStandard.Json;
using RepoStandard.Tests.Support;
using RepoStandard.Yaml;
using Xunit;

namespace RepoStandard.Tests;

public sealed class YamlTests
{
    [Theory]
    [InlineData("a: true", "{\"a\":true}")]
    [InlineData("a: ~", "{\"a\":null}")]
    [InlineData("a: 30", "{\"a\":30}")]
    [InlineData("a: \"30\"", "{\"a\":\"30\"}")]
    [InlineData("a: 000000", "{\"a\":\"000000\"}")]
    [InlineData("a: 0e8a16", "{\"a\":\"0e8a16\"}")]
    [InlineData("a: yes", "{\"a\":\"yes\"}")]
    [InlineData("a: [x, 'y']", "{\"a\":[\"x\",\"y\"]}")]
    [InlineData("a: &x {b: 1}\nc: *x", "{\"a\":{\"b\":1},\"c\":{\"b\":1}}")]
    public void Scalars_resolve_by_the_narrowed_core_schema(string yaml, string json)
    {
        Assert.Equal(json, YamlJson.Parse(yaml, "t")!.ToJsonString());
    }

    [Theory]
    [InlineData("a: 1\na: 2", "duplicate key 'a'")]
    [InlineData("a: !!int 1", "YAML tags are not supported")]
    [InlineData("a: 1\n---\nb: 2", "more than one YAML document")]
    [InlineData("a: [", "t:")]
    public void Malformed_input_is_a_declaration_error(string yaml, string message)
    {
        DeclarationException error = Assert.Throws<DeclarationException>(() => YamlJson.Parse(yaml, "t"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("123456")]
    [InlineData("1e10")]
    [InlineData("on")]
    [InlineData("")]
    [InlineData("line one\nline two\n")]
    [InlineData("Q&A: #1, [x] {y} *z")]
    public void A_string_written_reads_back_as_the_same_string(string text)
    {
        string yaml = YamlJson.Write(new JsonObject { ["s"] = text });
        Assert.Equal(text, YamlJson.Parse(yaml, "t")!["s"]!.GetValue<string>());
    }
}

public sealed class DeclarationLoaderTests
{
    private static async Task<JsonObject> LoadAsync(Dictionary<string, string> files, string entry, Dictionary<string, string>? urls = null)
    {
        string directory = Directory.CreateTempSubdirectory("repo-standard-load-").FullName;
        try
        {
            foreach ((string name, string text) in files)
            {
                string path = Path.Combine(directory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
            }

            StaticFetch fetch = new(urls ?? []);
            using HttpClient http = new(fetch);
            DeclarationLoader loader = new((uri, ct) => http.GetStringAsync(uri, ct));
            return await loader.LoadAsync(Path.Combine(directory, entry), "octo-org", "hello-world", TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_repository_file_overrides_its_base_key_by_key_and_item_by_item()
    {
        JsonObject result = await LoadAsync(new()
        {
            ["base/standard.yaml"] = """
                repository:
                  features: {issues: true, wiki: false}
                  topics: [a, b]
                labels:
                  - {name: bug, color: "d73a4a"}
                  - {name: wontfix, color: "ffffff"}
                """,
            [".github/repo-standard.yaml"] = """
                extends: ../base/standard.yaml
                repository:
                  features: {wiki: true}
                  topics: [c]
                labels:
                  - {name: bug, color: "000000", description: Broken}
                  - {name: wontfix, absent: true}
                  - {name: question, color: "d876e3"}
                """,
        }, ".github/repo-standard.yaml");

        Assert.Equal(
            """{"repository":{"features":{"issues":true,"wiki":true},"topics":["c"]},"labels":[{"name":"bug","color":"000000","description":"Broken"},{"name":"question","color":"d876e3"}]}""",
            result.ToJsonString());
    }

    [Fact]
    public async Task A_null_in_the_override_unmanages_the_key()
    {
        JsonObject result = await LoadAsync(new()
        {
            ["base.yaml"] = "repository: {description: Base, homepage: https://example.org}\nsecrets: [A]\n",
            ["repo.yaml"] = "extends: base.yaml\nrepository: {homepage: null}\nsecrets: null\n",
        }, "repo.yaml");

        Assert.Equal("""{"repository":{"description":"Base"}}""", result.ToJsonString());
    }

    [Fact]
    public async Task Owner_and_repo_are_filled_in_and_a_doubled_dollar_is_literal()
    {
        JsonObject result = await LoadAsync(new()
        {
            ["repo.yaml"] = "projects:\n  - title: ${repo} work\n    short_description: $${not a variable} for ${owner}\n",
        }, "repo.yaml");

        Assert.Equal("hello-world work", result["projects"]![0]!["title"]!.GetValue<string>());
        Assert.Equal("${not a variable} for octo-org", result["projects"]![0]!["short_description"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_unknown_variable_is_an_error_not_text()
    {
        DeclarationException error = await Assert.ThrowsAsync<DeclarationException>(() =>
            LoadAsync(new() { ["repo.yaml"] = "repository:\n  description: ${reop}\n" }, "repo.yaml"));
        Assert.Contains("'${reop}' is not a variable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_base_is_fetched_over_https_with_no_credentials_and_its_relative_extends_resolve_against_it()
    {
        Dictionary<string, string> urls = new()
        {
            ["https://example.org/standards/team.yaml"] = "extends: org.yaml\nlabels: [{name: team, color: \"000000\"}]\n",
            ["https://example.org/standards/org.yaml"] = "repository: {features: {wiki: false}}\n",
        };

        string directory = Directory.CreateTempSubdirectory("repo-standard-url-").FullName;
        try
        {
            string file = Path.Combine(directory, "repo.yaml");
            await File.WriteAllTextAsync(file, "extends: https://example.org/standards/team.yaml\n", TestContext.Current.CancellationToken);

            TestHost host = new() { Fetch = new StaticFetch(urls) };
            int exit = await host.RunAsync(new FakeGitHub(), "plan", "--repo", "octo-org/hello-world", "--file", file);

            Assert.Equal(0, exit);
            Assert.Contains("+ labels team", host.Output.ToString(), StringComparison.Ordinal);
            Assert.All(((StaticFetch)host.Fetch).Requests, request => Assert.Null(request.Headers.Authorization));
            Assert.Equal(2, ((StaticFetch)host.Fetch).Requests.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_base_over_plain_http_is_refused()
    {
        DeclarationException error = await Assert.ThrowsAsync<DeclarationException>(() =>
            LoadAsync(new() { ["repo.yaml"] = "extends: http://example.org/base.yaml\n" }, "repo.yaml"));
        Assert.Contains("https only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cycle_is_named()
    {
        DeclarationException error = await Assert.ThrowsAsync<DeclarationException>(() => LoadAsync(new()
        {
            ["a.yaml"] = "extends: b.yaml\n",
            ["b.yaml"] = "extends: a.yaml\n",
        }, "a.yaml"));
        Assert.Contains("cycle", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("labels: [{name: bug, colour: \"d73a4a\"}]", "labels[0].colour: not a known setting")]
    [InlineData("labels: [{name: bug, color: d73a4a}, {name: Bug}]", "labels[1]: name 'Bug' appears more than once")]
    [InlineData("labels: [{name: bug, color: 123456}]", "labels[0].color: expected a six-digit hex colour, quoted")]
    [InlineData("repository: {merge: {squash: true}}", "repository.merge.squash: not a known setting")]
    [InlineData("rulesets: [{name: x, target: branch}]", "rulesets[0].enforcement: required")]
    [InlineData("environments: [{name: e, branch_policies: [{name: main}], deployment_branch_policy: null}]", "needs deployment_branch_policy.custom_branch_policies: true")]
    [InlineData("environments: [{name: e, prevent_self_review: true}]", "GitHub keeps it only while there are reviewers")]
    [InlineData("actions: {allowed_actions: all, selected_actions: {verified_allowed: true}}", "applies only when allowed_actions is selected")]
    [InlineData("colour: red", "colour: not a known setting")]
    public async Task Validation_names_the_path_and_the_problem(string yaml, string expected)
    {
        DeclarationException error = await Assert.ThrowsAsync<DeclarationException>(() => LoadAsync(new() { ["repo.yaml"] = yaml }, "repo.yaml"));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipped_baseline_is_a_valid_declaration_and_an_adopter_can_extend_it()
    {
        string baseline = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "baselines", "mom.yaml"), TestContext.Current.CancellationToken);

        JsonObject result = await LoadAsync(new()
        {
            ["mom.yaml"] = baseline,
            ["repo.yaml"] = """
                extends: mom.yaml
                repository:
                  description: An adopter's repository.
                """,
        }, "repo.yaml");

        Assert.Equal("An adopter's repository.", JsonTree.Str(result["repository"], "description"));
        Assert.NotNull(result["rulesets"]);
        Assert.DoesNotContain("${", result.ToJsonString(), StringComparison.Ordinal);
    }
}

public sealed class CommandLineTests
{
    [Theory]
    [InlineData(new[] { "plan" }, "--repo OWNER/NAME is required")]
    [InlineData(new[] { "plan", "--repo", "just-a-name" }, "--repo takes OWNER/NAME")]
    [InlineData(new[] { "deploy" }, "unknown command 'deploy'")]
    [InlineData(new[] { "plan", "--repo", "o/n", "--force" }, "'--force' is not an option of plan")]
    [InlineData(new[] { "apply", "--repo", "o/n", "--file" }, "--file needs a value")]
    public async Task Usage_errors_exit_2_and_say_what_is_wrong(string[] args, string message)
    {
        TestHost host = new();
        int exit = await host.RunAsync(new FakeGitHub(), args);

        Assert.Equal(2, exit);
        Assert.Contains(message, host.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_token_is_a_usage_error()
    {
        TestHost host = new();
        host.Environment.Remove("GITHUB_TOKEN");
        int exit = await host.RunAsync(new FakeGitHub(), "plan", "--repo", "o/n");

        Assert.Equal(2, exit);
        Assert.Contains("a token is required", host.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_will_not_overwrite_without_force()
    {
        string file = Path.GetTempFileName();
        try
        {
            TestHost host = new();
            Assert.Equal(2, await host.RunAsync(new FakeGitHub(), "export", "--repo", "octo-org/hello-world", "--file", file));
            Assert.Contains("pass --force", host.Error.ToString(), StringComparison.Ordinal);

            Assert.Equal(0, await new TestHost().RunAsync(new FakeGitHub(), "export", "--repo", "octo-org/hello-world", "--file", file, "--force"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Check_writes_its_report_to_the_step_summary()
    {
        string directory = Directory.CreateTempSubdirectory("repo-standard-summary-").FullName;
        try
        {
            string file = Path.Combine(directory, "repo-standard.yaml");
            string summary = Path.Combine(directory, "summary.md");
            await File.WriteAllTextAsync(file, "labels: [{name: bug, color: \"d73a4a\"}]\n", TestContext.Current.CancellationToken);

            TestHost host = new();
            host.Environment["GITHUB_STEP_SUMMARY"] = summary;
            int exit = await host.RunAsync(new FakeGitHub(), "check", "--repo", "octo-org/hello-world", "--file", file);

            Assert.Equal(1, exit);
            string markdown = await File.ReadAllTextAsync(summary, TestContext.Current.CancellationToken);
            Assert.Contains("## repo-standard check: `octo-org/hello-world`", markdown, StringComparison.Ordinal);
            Assert.Contains("| `+` | labels bug |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
