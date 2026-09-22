// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>
/// <c>eng/native-assets.cs</c>, run for real, on both outcomes.
/// </summary>
/// <remarks>
/// <para>
/// Neither case uses a fixture. A hand-written <c>project.assets.json</c> is a
/// file somebody wrote to make the gate say what they expected, which tests the
/// expectation rather than the gate. These point it at two restore graphs the
/// repository already produces: the benchmark harness, whose BenchmarkDotNet
/// dependency pulls <c>Gee.External.Capstone</c> and its nine native
/// libraries, and <c>Varve.Turtle</c>, which is a shipped package and must be
/// clean.
/// </para>
/// <para>
/// The failing one is also the amendment's own subject. ADR 0009 originally
/// banned native assets outright; the benchmark harness is what forced the
/// scope down to shipped artifacts, and it is the thing the gate must still
/// detect when asked about it directly.
/// </para>
/// </remarks>
public class NativeAssetGateTests
{
    [Fact]
    public async Task The_shipped_packages_carry_no_native_asset()
    {
        // Restoring the top of the layer stack restores Varve.Rdf and Varve.Iri with
        // it, which is every packable project the gate will look at.
        await EnsureRestoredAsync(Path.Combine("src", "Varve.Turtle"));

        GateResult result = await RunAsync();

        Assert.True(result.ExitCode == 0, "The packable projects must be clean." + Environment.NewLine + result.Output);
        Assert.Contains("src/Varve.Iri", result.Output.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("src/Varve.Rdf", result.Output.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("src/Varve.Turtle", result.Output.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_restore_graph_passes()
    {
        await EnsureRestoredAsync(Path.Combine("src", "Varve.Turtle"));

        GateResult result = await RunAsync(
            "--assets", Path.Combine("src", "Varve.Turtle", "obj", "project.assets.json"));

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task A_restore_graph_carrying_native_assets_fails_and_names_them()
    {
        await EnsureRestoredAsync(Path.Combine("tests", "Varve.Benchmarks"));

        GateResult result = await RunAsync(
            "--assets", Path.Combine("tests", "Varve.Benchmarks", "obj", "project.assets.json"));

        Assert.True(
            result.ExitCode == 1,
            "The benchmark harness pulls Gee.External.Capstone and must be detected."
            + Environment.NewLine + result.Output);

        Assert.Contains("Gee.External.Capstone", result.Output, StringComparison.Ordinal);
        Assert.Contains("libcapstone.so", result.Output, StringComparison.Ordinal);

        // The message has to say which decision it is enforcing, or the reader
        // of a red build has to go looking for it.
        Assert.Contains("ADR 0009", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assets_file_that_is_not_there_could_not_run_rather_than_passing()
    {
        GateResult result = await RunAsync("--assets", "no/such/project.assets.json");

        // Exit 2, not 0: a gate that cannot read its input has not found the
        // code clean.
        Assert.Equal(2, result.ExitCode);
    }

    /// <summary>
    /// A managed RID-specific asset is not a native one. System.Diagnostics.EventLog
    /// ships <c>runtimes/win/lib/…</c> into the same benchmark graph, and a gate
    /// that matched on the path rather than on <c>assetType</c> would report it.
    /// </summary>
    [Fact]
    public async Task A_managed_runtime_specific_asset_is_not_reported()
    {
        await EnsureRestoredAsync(Path.Combine("tests", "Varve.Benchmarks"));

        GateResult result = await RunAsync(
            "--assets", Path.Combine("tests", "Varve.Benchmarks", "obj", "project.assets.json"));

        Assert.DoesNotContain("System.Diagnostics.EventLog", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("TraceEvent", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Restores a project whose restore graph the gate is about to read.
    /// </summary>
    /// <remarks>
    /// <c>Varve.Benchmarks</c> is deliberately not a member of
    /// <c>Varve.slnx</c> — it is not in CI and does not gate — so nothing in a
    /// normal build produces its <c>project.assets.json</c>. A test that
    /// silently passed because the file was missing would prove nothing, and a
    /// test that told CI to restore it would work only in CI.
    /// </remarks>
    private static async Task EnsureRestoredAsync(string projectDirectory)
    {
        string root = FindRepositoryRoot();

        if (File.Exists(Path.Combine(root, projectDirectory, "obj", "project.assets.json")))
        {
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectDirectory);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the restore.");

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(
            File.Exists(Path.Combine(root, projectDirectory, "obj", "project.assets.json")),
            "Restoring " + projectDirectory + " produced no restore graph, so the gate has nothing to read.");
    }

    private static async Task<GateResult> RunAsync(params string[] gateArguments)
    {
        string repositoryRoot = FindRepositoryRoot();

        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(Path.Combine("eng", "native-assets.cs"));

        if (gateArguments.Length > 0)
        {
            startInfo.ArgumentList.Add("--");

            foreach (string argument in gateArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

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
