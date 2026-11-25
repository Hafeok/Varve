// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>
/// The built site in the runner's Chrome (ADR 0247 amended 2026-11-19):
/// two builds of one commit are byte-identical, the pages load under their
/// own policy with nothing refused and nothing from another origin, and
/// axe-core finds no WCAG 2.2 AA violation on the home page, an ADR and an
/// operator page, in both schemes, at 1440 and 390 wide.
/// </summary>
public class BrowserTests(BuiltSite site)
{
    /// <summary>The origin the pages are served at, from the built files, by Playwright's routing: no server, no network.</summary>
    private const string Origin = "http://docs.varve.test/";

    private static readonly System.Threading.Lock ReportLock = new();

    private static readonly ImmutableArray<string> Wcag = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

    /// <summary>The three pages the audit covers: the home page, an ADR (with its badge and diagram), and an operator page (with its tables).</summary>
    public static TheoryData<string, string, int> Audited()
    {
        TheoryData<string, string, int> data = [];

        foreach (string page in (string[])["index.html", "adr/0247-the-docs-site.html", "operator/configure.html"])
        {
            foreach (string scheme in (string[])["light", "dark"])
            {
                foreach (int width in (int[])[1440, 390])
                {
                    data.Add(page, scheme, width);
                }
            }
        }

        return data;
    }

    [Fact]
    public async Task two_builds_of_one_commit_are_byte_identical()
    {
        string first = await site.OutputAsync();
        string second = await site.BuildAgainAsync();
        List<string> files = Files(first);

        Assert.Equal(files, Files(second));

        foreach (string file in files)
        {
            byte[] one = await File.ReadAllBytesAsync(Path.Combine(first, file), TestContext.Current.CancellationToken);
            byte[] two = await File.ReadAllBytesAsync(Path.Combine(second, file), TestContext.Current.CancellationToken);
            Assert.True(one.SequenceEqual(two), $"{file} differs between two builds of {BuiltSite.Commit}.");
        }
    }

    /// <summary>
    /// Under the page's own policy, with nothing bypassed: no request is
    /// refused by it, no request leaves the site's origin, and the three
    /// stylesheets and the mark load.
    /// </summary>
    [Theory]
    [InlineData("index.html")]
    [InlineData("adr/0247-the-docs-site.html")]
    [InlineData("operator/configure.html")]
    [InlineData("problems/as-of-distance-exceeded.html")]
    [InlineData("api/Varve.Rdf.html")]
    public async Task a_page_loads_under_its_own_policy_with_nothing_refused_and_nothing_from_another_origin(string page)
    {
        string output = await site.OutputAsync();
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await LaunchAsync(playwright);
        await using IBrowserContext context = await browser.NewContextAsync();
        List<string> requests = [];
        await ServeAsync(context, output, requests);
        IPage tab = await context.NewPageAsync();
        List<string> refused = [];
        tab.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                refused.Add(message.Text);
            }
        };

        IResponse? response = await tab.GotoAsync(Origin + page, new() { WaitUntil = WaitUntilState.Load });
        Assert.Equal(200, response!.Status);

        Assert.Empty(refused);
        Assert.All(requests, static url => Assert.StartsWith(Origin, url, StringComparison.Ordinal));

        foreach (string loaded in (string[])["site-tokens.css", "bundle.css", "site.css", "varve-core.svg"])
        {
            Assert.Contains(requests, url => url.EndsWith("/" + loaded, StringComparison.Ordinal));
        }

        // The tokens took: the page's ground is the design's surface, not the browser's default.
        string background = await tab.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(255, 255, 255)", background);
        string ink = await tab.EvaluateAsync<string>("getComputedStyle(document.body).color");
        Assert.Equal("rgb(17, 25, 34)", ink);
    }

    /// <summary>
    /// axe-core at WCAG 2.2 AA, as for the Web UI (ADR 0204). axe is script
    /// the test injects, and the page's policy has no script source at all, so
    /// the browser context that runs axe, and only that one, bypasses the
    /// policy. The checks that the site has no script and loads nothing from
    /// another origin read the files as written to disk, and the case above
    /// loads every audited page under its own policy, unbypassed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Audited))]
    public async Task axe_finds_no_violation(string page, string scheme, int width)
    {
        string output = await site.OutputAsync();
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await LaunchAsync(playwright);
        await using IBrowserContext context = await browser.NewContextAsync(new()
        {
            BypassCSP = true,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ViewportSize = new() { Width = width, Height = width > 800 ? 900 : 844 },
        });
        await ServeAsync(context, output, []);
        IPage tab = await context.NewPageAsync();
        await tab.GotoAsync(Origin + page, new() { WaitUntil = WaitUntilState.Load });

        // The scheme took: dark reads the dark ground.
        string background = await tab.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        Assert.Equal(scheme == "dark" ? "rgb(19, 27, 35)" : "rgb(255, 255, 255)", background);

        AxeResult result = await tab.RunAxe(new AxeRunOptions { RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Wcag] } });
        List<string> violations = [.. result.Violations.Select(v => v.Id + " (" + v.Impact + ") " + string.Join(", ", v.Nodes.Select(static n => string.Join(" ", n.Target))))];
        Report($"| `{page}` | {scheme} | {width} | {(violations.Count == 0 ? "no violation" : string.Join("; ", violations))} | {result.Passes.Length} rules passed |");
        Assert.True(violations.Count == 0, $"{page}, {scheme}, {width} wide: " + string.Join("\n", violations));
    }

    private static Task<IBrowser> LaunchAsync(IPlaywright playwright) =>
        playwright.Chromium.LaunchAsync(new() { ExecutablePath = BuiltSite.Chrome, Headless = true });

    /// <summary>Serves the built files at <see cref="Origin"/>, records every request, and refuses every request elsewhere.</summary>
    private static async Task ServeAsync(IBrowserContext context, string output, List<string> requests)
    {
        await context.RouteAsync("**/*", async route =>
        {
            string url = route.Request.Url;

            lock (requests)
            {
                requests.Add(url);
            }

            if (!url.StartsWith(Origin, StringComparison.Ordinal))
            {
                await route.AbortAsync();
                return;
            }

            string path = Uri.UnescapeDataString(new Uri(url).AbsolutePath.TrimStart('/'));
            string file = Path.GetFullPath(Path.Combine(output, path.Length == 0 ? "index.html" : path));

            if (!file.StartsWith(Path.GetFullPath(output), StringComparison.Ordinal) || !File.Exists(file))
            {
                await route.FulfillAsync(new() { Status = 404 });
                return;
            }

            await route.FulfillAsync(new()
            {
                Status = 200,
                BodyBytes = await File.ReadAllBytesAsync(file),
                ContentType = Path.GetExtension(file) switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    ".svg" => "image/svg+xml",
                    _ => "application/octet-stream",
                },
            });
        });
    }

    private static List<string> Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    /// <summary>Each audit's result, one table row each, for the close-out, where <c>VARVE_SITE_REPORT</c> names a file.</summary>
    private static void Report(string row)
    {
        TestContext.Current.SendDiagnosticMessage(row);

        if (Environment.GetEnvironmentVariable("VARVE_SITE_REPORT") is { Length: > 0 } file)
        {
            lock (ReportLock)
            {
                File.AppendAllText(file, row + "\n", Encoding.UTF8);
            }
        }
    }
}
