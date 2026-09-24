// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Declaration;
using RepoStandard.Engine;
using RepoStandard.GitHub;
using RepoStandard.Json;
using RepoStandard.Resources;
using RepoStandard.Yaml;

namespace RepoStandard.Cli;

/// <summary>What the process needs from its surroundings, injected so tests supply their own.</summary>
/// <param name="Output">stdout.</param>
/// <param name="Error">stderr.</param>
/// <param name="Environment">Reads an environment variable.</param>
/// <param name="GitHubHandler">The handler under the GitHub client; a replay or a fake in tests.</param>
/// <param name="FetchHandler">The handler that fetches an https <c>extends</c>, with no credentials.</param>
/// <param name="Delay">Waits between retries.</param>
/// <param name="Time">The clock.</param>
internal sealed record Host(
    TextWriter Output,
    TextWriter Error,
    Func<string, string?> Environment,
    HttpMessageHandler GitHubHandler,
    HttpMessageHandler FetchHandler,
    IDelay Delay,
    TimeProvider Time);

/// <summary>The commands.</summary>
internal static class App
{
    /// <summary>Exit code: success, or no drift.</summary>
    public const int Ok = 0;

    /// <summary>Exit code: drift found, or a write failed, or something is left that repo-standard cannot write.</summary>
    public const int Differences = 1;

    /// <summary>Exit code: could not run — usage, a declaration error, a read that failed.</summary>
    public const int CouldNotRun = 2;

    private const string Usage = """
        repo-standard — apply a declared set of GitHub repository settings.

        Usage:
          repo-standard export --repo OWNER/NAME [--file PATH] [--force]
          repo-standard plan   --repo OWNER/NAME [--file PATH]
          repo-standard apply  --repo OWNER/NAME [--file PATH] [--allow-status-reset]
          repo-standard check  --repo OWNER/NAME [--file PATH]

        Commands:
          export   Read the repository's live settings and write them as a declaration.
          plan     Show what apply would create, update and delete. Changes nothing.
          apply    Make the repository match the declaration, writing only what differs.
                   Stops at the first failed write and prints what was not applied.
          check    Like plan, but any difference, in either direction, exits 1, and the
                   report is also written to GITHUB_STEP_SUMMARY when that is set.

        Options:
          --repo OWNER/NAME     The repository. Defaults to GITHUB_REPOSITORY.
          --file PATH           The declaration. Default: repo-standard.yaml.
          --token TOKEN         A GitHub token. Defaults to GITHUB_TOKEN, which is better:
                                a command-line argument is visible to other processes.
          --api-url URL         The REST API root. Defaults to GITHUB_API_URL, then
                                https://api.github.com.
          --graphql-url URL     The GraphQL endpoint. Defaults to GITHUB_GRAPHQL_URL, then
                                derived from the API root.
          --allow-status-reset  apply: allow rewriting the Status options of a Projects
                                board that has items, which clears their Status.
          --force               export: overwrite an existing file.
          --version             Print the version.

        Exit codes: 0 no differences (or done), 1 differences or a failed write, 2 could not run.
        """;

    /// <summary>Runs one command.</summary>
    public static async Task<int> RunAsync(string[] args, Host host, CancellationToken cancellationToken)
    {
        Options options;
        try
        {
            options = Options.Parse(args, host.Environment);
        }
        catch (UsageException exception)
        {
            await host.Error.WriteLineAsync($"error: {exception.Message}\n\n{Usage}").ConfigureAwait(false);
            return CouldNotRun;
        }

        if (options.Command is "help")
        {
            await host.Output.WriteLineAsync(Usage).ConfigureAwait(false);
            return Ok;
        }

        if (options.Command is "version")
        {
            await host.Output.WriteLineAsync(Version).ConfigureAwait(false);
            return Ok;
        }

        // From here the token is known, and every line written goes through a
        // writer that would mask it. Nothing is meant to write it; this is the
        // second line of defence, not the first.
        TextWriter output = new RedactingWriter(host.Output, options.Token);
        TextWriter error = new RedactingWriter(host.Error, options.Token);

        using HttpClient http = new(host.GitHubHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(100) };
        GitHubClient client = new(http, new GitHubOptions(options.ApiUrl, options.GraphQlUrl, options.Token, $"repo-standard/{Version}"),
            host.Delay, host.Time, error);
        RepoContext context = new(options.Owner, options.Name, client, options.AllowStatusReset);

        try
        {
            return options.Command switch
            {
                "export" => await ExportAsync(options, context, output, error, cancellationToken).ConfigureAwait(false),
                "plan" => await PlanAsync(options, context, host, output, error, cancellationToken).ConfigureAwait(false),
                "check" => await CheckAsync(options, context, host, output, error, cancellationToken).ConfigureAwait(false),
                "apply" => await ApplyAsync(options, context, host, output, error, cancellationToken).ConfigureAwait(false),
                _ => CouldNotRun,
            };
        }
        catch (DeclarationException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return CouldNotRun;
        }
        catch (GitHubException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return CouldNotRun;
        }
        finally
        {
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await error.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The tool's version.</summary>
    public static string Version =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static async Task<int> ExportAsync(Options options, RepoContext context, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (File.Exists(options.File) && !options.Force)
        {
            await error.WriteLineAsync($"error: {options.File} exists; pass --force to overwrite it.").ConfigureAwait(false);
            return CouldNotRun;
        }

        LiveState live = await Planner.ReadAsync(Planner.Kinds(), context, tolerant: true, error, cancellationToken).ConfigureAwait(false);
        JsonObject state = live.State;

        // Categories of Discussions that are switched off are not a setting anyone manages.
        if (JsonTree.Bool(state["repository"]?["features"], "discussions") == false)
        {
            state.Remove("discussions");
        }

        StringBuilder text = new();
        text.Append("# repo-standard declaration for ").Append(context.Owner).Append('/').Append(context.Name).Append('\n');
        text.Append("# Written by `repo-standard export` ").Append(Version).Append(". Secrets are names only; values are never read.\n");
        text.Append("# Remove a top-level key to stop managing that kind of resource.\n");
        foreach (string reason in live.Unreadable)
        {
            text.Append("# Left out, could not be read: ").Append(reason.ReplaceLineEndings(" ")).Append('\n');
        }

        text.Append(YamlJson.Write(state));
        // What GitHub said about an unreadable kind is quoted in the header, and
        // GitHub's words are not ours to vouch for.
        await File.WriteAllTextAsync(options.File, RedactingWriter.Redact(text.ToString(), options.Token), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync($"wrote {options.File}: {string.Join(", ", state.Select(p => p.Key))}").ConfigureAwait(false);
        return Ok;
    }

    private static async Task<int> PlanAsync(Options options, RepoContext context, Host host, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<Change> changes = await LoadAndPlanAsync(options, context, host, error, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(Report.Text(changes)).ConfigureAwait(false);
        return Ok;
    }

    private static async Task<int> CheckAsync(Options options, RepoContext context, Host host, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<Change> changes = await LoadAndPlanAsync(options, context, host, error, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(Report.Text(changes)).ConfigureAwait(false);

        string? summary = host.Environment("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summary))
        {
            string markdown = Report.Markdown($"{context.Owner}/{context.Name}", changes);
            await File.AppendAllTextAsync(summary, RedactingWriter.Redact(markdown, options.Token), cancellationToken).ConfigureAwait(false);
        }

        return changes.Count == 0 ? Ok : Differences;
    }

    private static async Task<int> ApplyAsync(Options options, RepoContext context, Host host, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        List<Change> changes = await LoadAndPlanAsync(options, context, host, error, cancellationToken).ConfigureAwait(false);
        if (changes.Count == 0)
        {
            await output.WriteLineAsync(Report.Summary(changes)).ConfigureAwait(false);
            return Ok;
        }

        (Change? failed, Exception? exception, List<Change> notApplied) =
            await Planner.ApplyAsync(changes, output, cancellationToken).ConfigureAwait(false);

        List<Change> unfixable = [.. changes.Where(c => c.Apply is null)];

        if (failed is not null)
        {
            await error.WriteLineAsync($"FAILED   {Report.Line(failed)}: {exception!.Message}").ConfigureAwait(false);
            await error.WriteLineAsync("Stopped. Not applied:").ConfigureAwait(false);
            await error.WriteAsync(Report.Text(notApplied)).ConfigureAwait(false);
        }

        if (unfixable.Count > 0)
        {
            await output.WriteLineAsync("Not converged; repo-standard cannot write these:").ConfigureAwait(false);
            await output.WriteAsync(Report.Text(unfixable)).ConfigureAwait(false);
        }

        return failed is null && unfixable.Count == 0 ? Ok : Differences;
    }

    private static async Task<List<Change>> LoadAndPlanAsync(
        Options options, RepoContext context, Host host, TextWriter error, CancellationToken cancellationToken)
    {
        using HttpClient fetch = new(host.FetchHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        DeclarationLoader loader = new(async (uri, ct) =>
        {
            using HttpResponseMessage response = await fetch.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new DeclarationException($"{uri}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        });

        JsonObject declaration = await loader.LoadAsync(options.File, context.Owner, context.Name, cancellationToken).ConfigureAwait(false);
        return await Planner.PlanAsync(declaration, context, error, cancellationToken).ConfigureAwait(false);
    }
}
