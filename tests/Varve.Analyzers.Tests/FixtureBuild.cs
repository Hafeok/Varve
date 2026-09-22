using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Analyzers.Tests;

/// <summary>What a real <c>dotnet build</c> made of a fixture.</summary>
internal sealed record BuildResult(int ExitCode, string Output);

/// <summary>
/// Builds a project under <c>tests/fixtures/</c> in a separate process.
/// </summary>
/// <remarks>
/// A fixture inherits the repository's <c>Directory.Build.props</c> and
/// <c>Directory.Build.targets</c> exactly as a real project does, which is the
/// whole point: a unit test over a constructed compilation supplies the
/// analyzer's inputs itself and therefore cannot tell whether the MSBuild
/// wiring that would supply them for real exists at all.
/// </remarks>
internal static class FixtureBuild
{
    internal static async Task<BuildResult> RunAsync(string relativeProjectPath)
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = Path.Combine(repositoryRoot, "tests", "fixtures", relativeProjectPath);
        Assert.True(File.Exists(project), "Fixture project not found: " + project);

        // Every output, including the analyzer's own, goes to a scratch
        // directory. Writing into src/Varve.Analyzers/bin while this test host
        // has that assembly loaded would fail on Windows, and it would leave
        // the working tree dirty on every platform.
        string artifacts = Path.Combine(Path.GetTempPath(), "varve-fixture-" + Guid.NewGuid().ToString("N"));

        try
        {
            return await BuildAsync(project, artifacts);
        }
        finally
        {
            try
            {
                if (Directory.Exists(artifacts))
                {
                    Directory.Delete(artifacts, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover scratch directory is not worth failing a test over.
            }
        }
    }

    private static async Task<BuildResult> BuildAsync(string project, string artifacts)
    {
        ProcessStartInfo startInfo = new()
        {
            // The SDK sets DOTNET_HOST_PATH for the process it launches, which
            // is more reliable than hoping the right dotnet is first on PATH.
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-p:ArtifactsPath=" + artifacts);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the build.");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        await process.WaitForExitAsync(timeout.Token);

        return new BuildResult(process.ExitCode, await stdout + Environment.NewLine + await stderr);
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

        throw new InvalidOperationException(
            "Could not find the repository root (no Varve.slnx above " + AppContext.BaseDirectory + ").");
    }
}
