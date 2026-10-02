// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// <c>eng/decision-sets.cs</c>'s filing-date rule, run for real against a
/// ledger with a history.
/// </summary>
/// <remarks>
/// These use fixtures, unlike <see cref="NativeAssetGateTests"/>, because the
/// rule reads git history and the repository's own history cannot be arranged.
/// Each fixture is a small git repository whose commits carry the dates the
/// case needs. The failing one is the defect that motivated the rule: a key
/// filed on 2026-10-01 and accepted with a date of 2026-09-29 (session 3 of
/// #43).
/// </remarks>
public sealed class DecisionSetsGateTests : IDisposable
{
    private const string Filed = "2026-10-01T14:24:15+00:00";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "varve-decision-sets-" + Guid.NewGuid().ToString("N"));

    public DecisionSetsGateTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "docs", "decisions"));
        Directory.CreateDirectory(Path.Combine(_root, "docs", "adr"));
        File.WriteAllText(Path.Combine(_root, "docs", "adr", "0001-an-adr.md"), "# 0001\n");
    }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task An_acceptance_dated_before_its_filing_fails_and_names_both_days()
    {
        await GitAsync(Filed, "init", "--quiet");
        WriteSet("origin: \"a finding\"", Decision("Filed"));
        await CommitAsync(Filed);
        WriteSet("origin: \"a finding\"", Decision("Filed", "2026-09-29T00:00:00Z"));
        await CommitAsync("2026-10-02T09:00:00+00:00");

        GateResult result = await RunAsync();

        Assert.True(result.ExitCode == 1, result.Output);
        Assert.Contains("'Filed' is accepted-at 2026-09-29, before it was filed on 2026-10-01", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_acceptance_on_the_day_of_filing_passes()
    {
        await GitAsync(Filed, "init", "--quiet");
        WriteSet("origin: \"a finding\"", Decision("Filed", "2026-10-01T00:00:00Z"));
        await CommitAsync(Filed);

        GateResult result = await RunAsync();

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task An_adr_sets_first_commit_is_transcribed_but_a_key_an_amendment_adds_is_filed()
    {
        await GitAsync(Filed, "init", "--quiet");
        WriteSet("adr: 0001", Decision("Transcribed", "2026-09-20T00:00:00Z"));
        await CommitAsync(Filed);

        Assert.True((await RunAsync()).ExitCode == 0, "A transcribed acceptance carries its ADR's date.");

        WriteSet("adr: 0001", Decision("Transcribed", "2026-09-20T00:00:00Z") + Decision("Amended", "2026-09-20T00:00:00Z"));
        await CommitAsync("2026-10-02T09:00:00+00:00");

        GateResult result = await RunAsync();

        Assert.True(result.ExitCode == 1, result.Output);
        Assert.Contains("'Amended' is accepted-at 2026-09-20, before it was filed on 2026-10-02", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("'Transcribed'", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_renamed_key_keeps_the_filing_of_the_key_it_replaces()
    {
        await GitAsync(Filed, "init", "--quiet");
        WriteSet("origin: \"a finding\"", Decision("OldName", "2026-10-01T00:00:00Z"));
        await CommitAsync(Filed);
        WriteSet("origin: \"a finding\"", Decision("NewName", "2026-10-01T00:00:00Z"));
        await CommitAsync("2026-10-05T09:00:00+00:00");

        GateResult result = await RunAsync();

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task A_shallow_clone_could_not_run_rather_than_passing()
    {
        string origin = Path.Combine(_root, "origin");
        Directory.CreateDirectory(Path.Combine(origin, "docs", "decisions"));
        Directory.Move(Path.Combine(_root, "docs", "adr"), Path.Combine(origin, "docs", "adr"));
        await GitInAsync(Filed, origin, "init", "--quiet");
        File.WriteAllText(Path.Combine(origin, "docs", "decisions", "a-set.md"), SetText("origin: \"a finding\"", Decision("Filed")));
        await GitInAsync(Filed, origin, "add", "-A");
        await GitInAsync(Filed, origin, "commit", "--quiet", "-m", "file");
        File.WriteAllText(Path.Combine(origin, "docs", "decisions", "a-set.md"), SetText("origin: \"a finding\"", Decision("Filed", "2026-10-01T00:00:00Z")));
        await GitInAsync(Filed, origin, "commit", "--quiet", "-am", "accept");

        string clone = Path.Combine(_root, "clone");
        // --depth needs a file:// URL; a Windows path is file:///C:/....
        string path = origin.Replace('\\', '/');
        await GitInAsync(Filed, _root, "clone", "--quiet", "--depth", "1", "file://" + (path.StartsWith('/') ? "" : "/") + path, clone);

        GateResult result = await RunAsync(clone);

        Assert.True(result.ExitCode == 2, result.Output);
        Assert.Contains("shallow", result.Output, StringComparison.Ordinal);
    }

    private static string Decision(string key, string? acceptedAt = null) =>
        $"  - key: {key}\n    statement: \"A ruling\"\n"
        + (acceptedAt is null ? "" : $"    accepted-by: mailto:maintainer@example.org\n    accepted-at: {acceptedAt}\n");

    private static string SetText(string source, string decisions) =>
        $"---\nset: a-set\nnamespace: varve\n{source}\ndecisions:\n{decisions}---\n\n# A set\n";

    private void WriteSet(string source, string decisions) =>
        File.WriteAllText(Path.Combine(_root, "docs", "decisions", "a-set.md"), SetText(source, decisions));

    private async Task CommitAsync(string date)
    {
        await GitAsync(date, "add", "-A");
        await GitAsync(date, "commit", "--quiet", "--allow-empty", "-m", "ledger");
    }

    private Task GitAsync(string date, params string[] arguments) => GitInAsync(date, _root, arguments);

    private static async Task GitInAsync(string date, string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Dictionary<string, string> environment = new()
        {
            ["GIT_AUTHOR_NAME"] = "Fixture",
            ["GIT_AUTHOR_EMAIL"] = "fixture@example.org",
            ["GIT_COMMITTER_NAME"] = "Fixture",
            ["GIT_COMMITTER_EMAIL"] = "fixture@example.org",
            ["GIT_AUTHOR_DATE"] = date,
            ["GIT_COMMITTER_DATE"] = date,
            ["GIT_CONFIG_GLOBAL"] = EmptyGitConfig,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
        };

        foreach (KeyValuePair<string, string> variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, "git " + string.Join(' ', arguments) + ": " + await stdout + await stderr);
    }

    // An empty global configuration, so the runner's own settings do not reach
    // the fixtures; /dev/null is not a path on Windows. Made once, before any
    // test runs, so parallel tests never race to write it.
    private static readonly string EmptyGitConfig = CreateEmptyGitConfig();

    private static string CreateEmptyGitConfig()
    {
        string file = Path.Combine(Path.GetTempPath(), "varve-decision-sets-" + Guid.NewGuid().ToString("N") + ".gitconfig");
        File.WriteAllText(file, "");
        return file;
    }

    private Task<GateResult> RunAsync() => RunAsync(_root);

    private static async Task<GateResult> RunAsync(string fixture)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine("eng", "decision-sets.cs"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--dir");
        startInfo.ArgumentList.Add(Path.Combine(fixture, "docs", "decisions"));
        startInfo.ArgumentList.Add("--adr-dir");
        startInfo.ArgumentList.Add(Path.Combine(fixture, "docs", "adr"));

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the gate.");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await process.WaitForExitAsync(timeout.Token);

        return new GateResult(process.ExitCode, await stdout + Environment.NewLine + await stderr);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed record GateResult(int ExitCode, string Output);
}
