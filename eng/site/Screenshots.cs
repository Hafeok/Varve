// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Playwright;

namespace Varve.Site;

/// <summary>
/// Screenshots of a built site for a design review (#368): a page of each
/// kind, in light and dark, at 1440 and 390 wide, at twice the pixel density,
/// under each page's own policy. The pages are served from the built files by
/// the browser's routing; a request for anything else is refused, as when a
/// diagram is drawn. ci.yml's docs site job uploads them with the site. They
/// are never committed.
/// </summary>
internal static class Screenshots
{
    private const string Origin = "http://docs.varve.test/";

    /// <summary>The review set: what each screenshot is named, and the page it shows.</summary>
    public static readonly IReadOnlyList<(string Name, string Page)> Pages =
    [
        ("01-home", "index.html"),
        ("02-adr-accepted-0206", "adr/0206-the-web-ui-design-source.html"),
        ("03-adr-not-yet-accepted-0247-with-diagram", "adr/0247-the-docs-site.html"),
        ("04-adr-index", "adr/index.html"),
        ("05-problem-type", "problems/as-of-distance-exceeded.html"),
        ("06-operator-page-tables", "operator/configure.html"),
        ("07-api-reference", "api/Varve.Rdf.html"),
    ];

    /// <summary>A page taller than this is shot to the viewport only: what is under review on the ADR index or a long ADR is its frame.</summary>
    private const float FullPageLimit = 6000;

    public static async Task<int> WriteAsync(string site, string output, string chrome)
    {
        Directory.CreateDirectory(output);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome, Headless = true });
        int written = 0;

        foreach (string scheme in (string[])["light", "dark"])
        {
            foreach (int width in (int[])[1440, 390])
            {
                await using IBrowserContext context = await browser.NewContextAsync(new()
                {
                    ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
                    ViewportSize = new() { Width = width, Height = width > 800 ? 900 : 844 },
                    DeviceScaleFactor = 2,
                });

                await context.RouteAsync("**/*", route => Serve(route, site));
                IPage tab = await context.NewPageAsync();

                foreach ((string name, string page) in Pages)
                {
                    if (!File.Exists(Path.Combine(site, page)))
                    {
                        throw new SiteException($"the review's page {page} is not in {site}.");
                    }

                    await tab.GotoAsync(Origin + page, new() { WaitUntil = WaitUntilState.Load });
                    float height = await tab.EvaluateAsync<float>("document.documentElement.scrollHeight");
                    await tab.ScreenshotAsync(new() { Path = Path.Combine(output, $"{name}-{scheme}-{width}.png"), FullPage = height <= FullPageLimit });
                    written++;
                }
            }
        }

        Console.WriteLine($"varve-site: {written} screenshots in {output}.");
        return 0;
    }

    private static async Task Serve(IRoute route, string site)
    {
        string url = route.Request.Url;

        if (!url.StartsWith(Origin, StringComparison.Ordinal))
        {
            await route.AbortAsync();
            return;
        }

        string root = Path.GetFullPath(site);
        string file = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(new Uri(url).AbsolutePath.TrimStart('/'))));

        if (!file.StartsWith(root, StringComparison.Ordinal) || !File.Exists(file))
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
    }
}
