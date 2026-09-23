// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using RepoStandard.GitHub;
using RepoStandard.Tests.Support;
using Xunit;

namespace RepoStandard.Tests;

public sealed class GitHubClientTests
{
    private static Exchange Response(int status, JsonNode? body = null, Dictionary<string, string>? headers = null) =>
        new("GET", Endpoints.GetTopics.Path("octo-org", "hello-world"), null, null, status, headers ?? [], body);

    private static (GitHubClient Client, RecordingDelay Delay, ManualTime Time, StringWriter Log) Client(HttpMessageHandler handler)
    {
        RecordingDelay delay = new();
        ManualTime time = new();
        delay.Advance = time;
        StringWriter log = new();
        GitHubClient client = new(new HttpClient(handler),
            new GitHubOptions(new Uri("https://api.github.com"), new Uri("https://api.github.com/graphql"), TestHost.Token, "test"),
            delay, time, log);
        return (client, delay, time, log);
    }

    [Fact]
    public async Task A_primary_rate_limit_waits_until_the_reset_time()
    {
        ManualTime clock = new();
        long reset = clock.GetUtcNow().AddSeconds(90).ToUnixTimeSeconds();
        ReplayHandler replay = new(
        [
            Response(403, new JsonObject { ["message"] = "API rate limit exceeded" },
                new() { ["x-ratelimit-remaining"] = "0", ["x-ratelimit-reset"] = reset.ToString(CultureInfo.InvariantCulture) }),
            Response(200, new JsonObject { ["names"] = new JsonArray() }),
        ]);

        (GitHubClient client, RecordingDelay delay, _, _) = Client(replay);
        await client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(90)], delay.Waits);
    }

    [Fact]
    public async Task A_secondary_rate_limit_without_retry_after_backs_off_from_a_minute_doubling()
    {
        JsonObject limited = new() { ["message"] = "You have exceeded a secondary rate limit." };
        ReplayHandler replay = new(
        [
            Response(403, limited), Response(403, limited), Response(429, limited),
            Response(200, new JsonObject { ["names"] = new JsonArray() }),
        ]);

        (GitHubClient client, RecordingDelay delay, _, _) = Client(replay);
        await client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)], delay.Waits);
    }

    [Fact]
    public async Task After_the_last_retry_the_request_fails()
    {
        JsonObject limited = new() { ["message"] = "You have exceeded a secondary rate limit." };
        ReplayHandler replay = new(Enumerable.Range(0, GitHubClient.MaxRetries + 1).Select(_ => Response(403, limited, new() { ["retry-after"] = "5" })));

        (GitHubClient client, RecordingDelay delay, _, _) = Client(replay);
        GitHubException error = await Assert.ThrowsAsync<GitHubException>(() =>
            client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken));

        Assert.Equal(GitHubClient.MaxRetries, delay.Waits.Count);
        Assert.Contains("still after 5 retries", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_403_that_is_not_a_rate_limit_is_not_retried()
    {
        ReplayHandler replay = new([Response(403, new JsonObject { ["message"] = "Resource not accessible by integration" })]);

        (GitHubClient client, RecordingDelay delay, _, _) = Client(replay);
        GitHubException error = await Assert.ThrowsAsync<GitHubException>(() =>
            client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken));

        Assert.Empty(delay.Waits);
        Assert.Equal("GET /repos/octo-org/hello-world/topics: 403 Resource not accessible by integration", error.Message);
    }

    [Fact]
    public async Task Writes_are_spaced_a_second_apart_and_reads_are_not()
    {
        FakeGitHub fake = new();
        (GitHubClient client, RecordingDelay delay, ManualTime time, _) = Client(fake);

        await client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken);
        await client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken);
        await client.WriteAsync(Endpoints.ReplaceTopics, ["octo-org", "hello-world"], new JsonObject { ["names"] = new JsonArray() }, TestContext.Current.CancellationToken);
        time.Add(TimeSpan.FromMilliseconds(300));
        await client.WriteAsync(Endpoints.ReplaceTopics, ["octo-org", "hello-world"], new JsonObject { ["names"] = new JsonArray() }, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromMilliseconds(700)], delay.Waits);
    }

    [Fact]
    public async Task The_token_is_sent_as_a_bearer_header_with_the_api_version()
    {
        List<HttpRequestMessage> seen = [];
        CapturingHandler handler = new(seen);
        (GitHubClient client, _, _, _) = Client(handler);

        await client.GetAsync(Endpoints.GetTopics, ["octo-org", "hello-world"], TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(seen);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(TestHost.Token, request.Headers.Authorization.Parameter);
        Assert.Equal(GitHubClient.ApiVersion, request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Equal("application/vnd.github+json", request.Headers.Accept.Single().MediaType);
    }

    private sealed class CapturingHandler(List<HttpRequestMessage> seen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            seen.Add(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"names\":[]}") });
        }
    }
}

/// <summary>The token appears in no output, no report, no file — on success and on every failure.</summary>
public sealed class TokenTests
{
    /// <summary>A GitHub that fails every way it can, and echoes the Authorization header back in its error.</summary>
    private sealed class HostileGitHub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            string echoed = request.Headers.Authorization?.ToString() ?? string.Empty;
            JsonObject body = new() { ["message"] = "Bad credentials: " + echoed, ["errors"] = new JsonArray(new JsonObject { ["message"] = echoed }) };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized) { Content = new StringContent(body.ToJsonString()) });
        }
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("check")]
    [InlineData("apply")]
    [InlineData("export")]
    public async Task No_command_prints_the_token_even_when_GitHub_echoes_it(string command)
    {
        string directory = Directory.CreateTempSubdirectory("repo-standard-token-").FullName;
        try
        {
            string file = Path.Combine(directory, command == "export" ? "out.yaml" : "in.yaml");
            string summary = Path.Combine(directory, "summary.md");
            if (command != "export")
            {
                await File.WriteAllTextAsync(file, "labels: [{name: bug, color: \"d73a4a\"}]\n", TestContext.Current.CancellationToken);
            }

            TestHost host = new();
            host.Environment["GITHUB_STEP_SUMMARY"] = summary;
            await host.RunAsync(new HostileGitHub(), command, "--repo", "octo-org/hello-world", "--file", file, "--token", TestHost.Token);

            Assert.DoesNotContain(TestHost.Token, host.Output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(TestHost.Token, host.Error.ToString(), StringComparison.Ordinal);
            foreach (string written in Directory.EnumerateFiles(directory))
            {
                Assert.DoesNotContain(TestHost.Token, await File.ReadAllTextAsync(written, TestContext.Current.CancellationToken), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_successful_run_writes_no_token_anywhere()
    {
        string directory = Directory.CreateTempSubdirectory("repo-standard-token-").FullName;
        try
        {
            string file = Path.Combine(directory, "export.yaml");
            TestHost host = new();
            Assert.Equal(0, await host.RunAsync(new FakeGitHub(), "export", "--repo", "octo-org/hello-world", "--file", file));
            Assert.DoesNotContain(TestHost.Token, host.Output + host.Error.ToString() + await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Formatting_the_options_does_not_print_the_token()
    {
        GitHubOptions options = new(new Uri("https://api.github.com"), new Uri("https://api.github.com/graphql"), TestHost.Token, "test");
        Assert.DoesNotContain(TestHost.Token, options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TestHost.Token, $"{options}", StringComparison.Ordinal);
    }
}

public sealed class PagingTests
{
    [Fact]
    public async Task A_next_page_on_another_host_is_not_followed()
    {
        ReplayHandler replay = new(
        [
            new Exchange("GET", "/repos/octo-org/hello-world/labels?per_page=100", null, null, 200,
                new() { ["link"] = "<https://attacker.example/labels?page=2>; rel=\"next\"" }, new JsonArray()),
        ]);

        GitHubClient client = new(new HttpClient(replay),
            new GitHubOptions(new Uri("https://api.github.com"), new Uri("https://api.github.com/graphql"), TestHost.Token, "test"),
            new RecordingDelay(), new ManualTime(), new StringWriter());

        GitHubException error = await Assert.ThrowsAsync<GitHubException>(() => client.GetAllAsync(
            Endpoints.ListLabels, ["octo-org", "hello-world"], static body => body as JsonArray, TestContext.Current.CancellationToken));

        Assert.Contains("https://attacker.example, not the API's host; not followed", error.Message, StringComparison.Ordinal);
        Assert.Empty(replay.Remaining);
    }
}
