// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[assembly: AssemblyFixture(typeof(Varve.Site.Tests.BuiltSite))]

namespace Varve.Site.Tests;

/// <summary>
/// The site, built once for the run from this repository, in the runner's
/// Chrome (<c>VARVE_CHROME</c>), naming the commit checked out
/// (<c>VARVE_SITE_COMMIT</c>, or <c>git rev-parse HEAD</c>). Without a browser
/// the cases that need it are skipped, unless
/// <c>VARVE_SITE_TESTS_REQUIRED=1</c> says they must run, as it does in
/// ci.yml's docs site job.
/// </summary>
public sealed class BuiltSite : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _work = Path.Combine(Path.GetTempPath(), "varve-site-tests-" + Environment.ProcessId);
    private string? _output;

    public static string Chrome
    {
        get
        {
            string? chrome = Environment.GetEnvironmentVariable("VARVE_CHROME");

            if (!string.IsNullOrEmpty(chrome) && File.Exists(chrome))
            {
                return chrome;
            }

            if (Environment.GetEnvironmentVariable("VARVE_SITE_TESTS_REQUIRED") == "1")
            {
                Assert.Fail("VARVE_CHROME must name a Chrome: the site's diagrams are drawn in it, and its pages are audited in it.");
            }

            Assert.Skip("VARVE_CHROME is needed to build the site and to audit it.");
            return "";
        }
    }

    public static string Commit { get; } = Environment.GetEnvironmentVariable("VARVE_SITE_COMMIT") is { Length: 40 } commit ? commit : Git("rev-parse", "HEAD");

    /// <summary>The site's directory, built on first use.</summary>
    public async Task<string> OutputAsync()
    {
        string chrome = Chrome;
        await _gate.WaitAsync(TestContext.Current.CancellationToken);

        try
        {
            _output ??= await BuildAsync(Path.Combine(_work, "site"), chrome);
            return _output;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Another build of the same commit, into a directory of its own.</summary>
    public Task<string> BuildAgainAsync() => BuildAsync(Path.Combine(_work, "site-again"), Chrome);

    private static async Task<string> BuildAsync(string output, string chrome)
    {
        Repository repository = new(Paths.Root);
        string cache = Environment.GetEnvironmentVariable("VARVE_SITE_CACHE") is { Length: > 0 } c ? c : Path.Combine(Paths.Root, "artifacts", "site-cache");
        int exit = await SiteBuild.RunAsync(repository, SiteMap.Of(repository), output, Commit, chrome, cache);
        Assert.True(exit == 0, $"The site's build exited {exit}; its findings are in the test's output.");
        return output;
    }

    private static string Git(params string[] arguments)
    {
        ProcessStartInfo start = new("git") { WorkingDirectory = Paths.Root, RedirectStandardOutput = true, UseShellExecute = false };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process git = Process.Start(start)!;
        string text = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return text;
    }

    public void Dispose()
    {
        _gate.Dispose();

        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }
}
