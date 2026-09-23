// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Cli;
using RepoStandard.GitHub;
using Xunit;

namespace RepoStandard.Tests;

/// <summary>
/// Against a real scratch repository, through the real network. Skipped unless
/// both variables are set, which they are not in CI:
/// <list type="bullet">
/// <item><c>REPO_STANDARD_TEST_TOKEN</c> — a token with the permissions the README lists,</item>
/// <item><c>REPO_STANDARD_TEST_REPO</c> — <c>owner/name</c> of a repository whose settings this may change.</item>
/// </list>
/// It applies a declaration that touches every writable kind except Projects
/// (a board outlives the repository), exports, and requires the export and
/// the declaration both to plan clean — the round-trip property, for real.
/// Set <c>REPO_STANDARD_RECORD_DIR</c> as well to keep every exchange, in the
/// fixture format, for replacing the documented examples under
/// <c>fixtures/recorded</c> with captured ones. The Authorization header is
/// never recorded.
/// </summary>
public sealed class IntegrationTests
{
    private const string Declaration = """
        repository:
          description: repo-standard integration test. Safe to delete.
          topics: [repo-standard-test]
          features: {issues: true, wiki: false}
          merge: {allow_squash_merge: false, delete_branch_on_merge: true}
          security: {private_vulnerability_reporting: true}
        labels:
          - {name: repo-standard-test, color: "0e8a16", description: Created by the integration test}
        rulesets:
          - name: repo-standard-test
            target: branch
            enforcement: evaluate
            conditions: {ref_name: {include: ["~DEFAULT_BRANCH"], exclude: []}}
            rules: [{type: deletion}]
            bypass_actors: []
        environments:
          - name: repo-standard-test
            wait_timer: 1
            deployment_branch_policy: {protected_branches: false, custom_branch_policies: true}
            branch_policies: [{name: "v*", type: tag}]
        actions:
          workflow: {default_workflow_permissions: read}
        """;

    [Fact]
    public async Task Apply_export_plan_against_a_scratch_repository()
    {
        string? token = Environment.GetEnvironmentVariable("REPO_STANDARD_TEST_TOKEN");
        string? repo = Environment.GetEnvironmentVariable("REPO_STANDARD_TEST_REPO");
        Assert.SkipWhen(string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repo),
            "REPO_STANDARD_TEST_TOKEN and REPO_STANDARD_TEST_REPO are not both set");

        string directory = Directory.CreateTempSubdirectory("repo-standard-integration-").FullName;
        try
        {
            string declaration = Path.Combine(directory, "declaration.yaml");
            string exported = Path.Combine(directory, "exported.yaml");
            await File.WriteAllTextAsync(declaration, Declaration, TestContext.Current.CancellationToken);

            Assert.Equal(0, await RunAsync(token!, "apply", "--repo", repo!, "--file", declaration));
            Assert.Equal(0, await RunAsync(token!, "check", "--repo", repo!, "--file", declaration));
            Assert.Equal(0, await RunAsync(token!, "export", "--repo", repo!, "--file", exported));
            Assert.Equal(0, await RunAsync(token!, "check", "--repo", repo!, "--file", exported));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> RunAsync(string token, params string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        using SocketsHttpHandler network = new();
        using RecordingHandler gitHub = new(network, Environment.GetEnvironmentVariable("REPO_STANDARD_RECORD_DIR"), args[0]);
        using SocketsHttpHandler fetch = new();
        Host host = new(output, error, name => name == "GITHUB_TOKEN" ? token : null, gitHub, fetch, new RealDelay(), TimeProvider.System);

        int exit = await App.RunAsync(args, host, TestContext.Current.CancellationToken);
        await gitHub.SaveAsync();
        TestContext.Current.TestOutputHelper?.WriteLine($"$ repo-standard {string.Join(' ', args)}\n{output}{error}exit {exit}");
        return exit;
    }

    /// <summary>Passes requests through and, when given a directory, keeps them.</summary>
    private sealed class RecordingHandler(HttpMessageHandler inner, string? directory, string command) : DelegatingHandler(inner)
    {
        private readonly JsonArray _exchanges = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (directory is null)
            {
                return response;
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonObject headers = [];
            foreach (string name in (string[])["link", "retry-after", "x-ratelimit-remaining", "x-ratelimit-reset"])
            {
                if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
                {
                    headers[name] = string.Join(",", values);
                }
            }

            JsonNode? body = string.IsNullOrEmpty(requestBody) ? null : JsonNode.Parse(requestBody);
            JsonObject recorded = new() { ["method"] = request.Method.Method, ["path"] = request.RequestUri!.PathAndQuery };
            if (request.RequestUri.AbsolutePath.EndsWith("/graphql", StringComparison.Ordinal))
            {
                recorded["path"] = "/graphql";
                recorded["operation"] = System.Text.RegularExpressions.Regex.Match((string?)body?["query"] ?? string.Empty, @"^\s*(?:query|mutation)\s+(\w+)").Groups[1].Value;
                recorded["body"] = body?["variables"]?.DeepClone();
            }
            else if (body is not null)
            {
                recorded["body"] = body;
            }

            _exchanges.Add(new JsonObject
            {
                ["request"] = recorded,
                ["response"] = new JsonObject
                {
                    ["status"] = (int)response.StatusCode,
                    ["headers"] = headers,
                    ["body"] = string.IsNullOrEmpty(responseBody) ? null : JsonNode.Parse(responseBody),
                },
            });

            // The body was read to record it; hand the caller a fresh copy.
            HttpResponseMessage copy = new(response.StatusCode) { Content = new StringContent(responseBody) };
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Dispose();
            return copy;
        }

        public async Task SaveAsync()
        {
            if (directory is null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{command}.json");
            await File.WriteAllTextAsync(path, new JsonObject { ["command"] = new JsonArray(command), ["exchanges"] = _exchanges.DeepClone() }
                .ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
