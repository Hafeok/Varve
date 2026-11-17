// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Playwright;

namespace Varve.Site;

/// <summary>
/// Mermaid's own archive, as the register pins it (ADR 0247): fetched by its
/// exact version, its SHA-512 checked before a byte of it is read, and only
/// <c>dist/mermaid.min.js</c> taken from it. Nothing it depends on is fetched;
/// that file is the bundle, and it draws with no request of its own.
/// </summary>
internal sealed class MermaidArchive
{
    private MermaidArchive(string version, string script)
    {
        Version = version;
        Script = script;
    }

    public string Version { get; }

    public string Script { get; }

    public static async Task<MermaidArchive> LoadAsync(Repository repository, string cache)
    {
        XElement entry = XDocument.Load(repository.Full("Directory.Packages.props"))
            .Descendants()
            .SingleOrDefault(static e => e.Name.LocalName == "BuildArchive" && (string?)e.Attribute("Include") == "mermaid")
            ?? throw new SiteException("Directory.Packages.props has no BuildArchive for mermaid.");

        string version = Required(entry, "Version");
        string url = Required(entry, "Url");
        string integrity = Required(entry, "Integrity");
        byte[] expected = Convert.FromBase64String(integrity["sha512-".Length..]);

        Directory.CreateDirectory(cache);
        string archive = Path.Combine(cache, $"mermaid-{version}.tgz");

        if (!File.Exists(archive) || !SHA512.HashData(await File.ReadAllBytesAsync(archive)).AsSpan().SequenceEqual(expected))
        {
            using HttpClient http = new();
            byte[] fetched = await http.GetByteArrayAsync(new Uri(url));

            if (!SHA512.HashData(fetched).AsSpan().SequenceEqual(expected))
            {
                throw new SiteException($"{url} does not have the SHA-512 the register states ({integrity}); it was not read.");
            }

            await File.WriteAllBytesAsync(archive, fetched);
        }

        await using FileStream file = File.OpenRead(archive);
        await using GZipStream gzip = new(file, CompressionMode.Decompress);
        await using TarReader tar = new(gzip);

        while (await tar.GetNextEntryAsync() is { } item)
        {
            if (item.Name == "package/dist/mermaid.min.js" && item.DataStream is not null)
            {
                using StreamReader reader = new(item.DataStream);
                return new MermaidArchive(version, await reader.ReadToEndAsync());
            }
        }

        throw new SiteException($"mermaid {version}'s archive has no package/dist/mermaid.min.js.");
    }

    private static string Required(XElement entry, string name) =>
        (string?)entry.Attribute(name) ?? throw new SiteException($"the mermaid BuildArchive has no {name}.");
}

/// <summary>Draws diagrams in Chrome, with every request but the blank page refused.</summary>
internal static class MermaidRenderer
{
    private const string Blank = "https://render.invalid/";

    public static async Task<Dictionary<string, string>> RenderAsync(MermaidArchive mermaid, string chrome, IReadOnlyDictionary<string, string> diagrams)
    {
        Dictionary<string, string> svgs = new(StringComparer.Ordinal);

        if (diagrams.Count == 0)
        {
            return svgs;
        }

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome, Headless = true });
        IBrowserContext context = await browser.NewContextAsync(new() { Locale = "en-US", TimezoneId = "UTC" });
        List<string> refused = [];

        await context.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url == Blank)
            {
                await route.FulfillAsync(new() { ContentType = "text/html", Body = "<!doctype html><html><body></body></html>" });
                return;
            }

            lock (refused)
            {
                refused.Add(route.Request.Url);
            }

            await route.AbortAsync();
        });

        IPage page = await context.NewPageAsync();
        await page.GotoAsync(Blank);
        await page.AddScriptTagAsync(new() { Content = mermaid.Script });
        await page.EvaluateAsync(
            """
            () => mermaid.initialize({
              startOnLoad: false,
              securityLevel: 'strict',
              htmlLabels: false,
              flowchart: { htmlLabels: false },
              deterministicIds: true,
              theme: 'neutral',
            })
            """);

        foreach ((string id, string source) in diagrams.OrderBy(static d => d.Key, StringComparer.Ordinal))
        {
            try
            {
                svgs[id] = await page.EvaluateAsync<string>(
                    "async ([id, source]) => (await mermaid.render(id, source)).svg",
                    new[] { id, source });
            }
            catch (PlaywrightException e)
            {
                throw new SiteException($"diagram {id} does not render: {e.Message.Split('\n')[0]}");
            }
        }

        if (refused.Count > 0)
        {
            throw new SiteException($"rendering asked for {string.Join(", ", refused.Distinct().Order(StringComparer.Ordinal))}; Mermaid must draw with no request of its own.");
        }

        return svgs;
    }

    /// <summary>
    /// What an SVG the site publishes may hold (the maintainer's answer 2 on
    /// #129): no script, no event handler, no foreign object, and no reference
    /// to anything but its own fragments. The only absolute URIs allowed are
    /// namespace names.
    /// </summary>
    public static IEnumerable<string> Findings(string svg)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(svg);
        }
        catch (System.Xml.XmlException e)
        {
            return [$"is not well-formed XML: {e.Message}"];
        }

        List<string> findings = [];

        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName is "script" or "foreignObject" or "iframe" or "embed" or "object")
            {
                findings.Add($"holds a <{element.Name.LocalName}>");
            }

            foreach (XAttribute attribute in element.Attributes())
            {
                string name = attribute.Name.LocalName;

                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"has an event handler {name} on <{element.Name.LocalName}>");
                }

                if (name is "href" or "src" && !attribute.Value.StartsWith('#'))
                {
                    findings.Add($"refers to '{attribute.Value}' from <{element.Name.LocalName}>");
                }

                findings.AddRange(Outside(attribute.Value, $"<{element.Name.LocalName} {name}>"));
            }

            if (!element.HasElements && element.Value.Length > 0)
            {
                findings.AddRange(Outside(element.Value, $"<{element.Name.LocalName}>"));
            }
        }

        return findings;
    }

    private static IEnumerable<string> Outside(string text, string where)
    {
        if (text.Contains("@import", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"imports a stylesheet in {where}";
        }

        int at = 0;

        while ((at = text.IndexOf("url(", at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            string rest = text[(at + 4)..].TrimStart(' ', '"', '\'');

            if (!rest.StartsWith('#'))
            {
                yield return $"refers to url({rest.Split(')')[0]}) in {where}";
            }

            at += 4;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            yield return $"names an absolute URI in {where}";
        }
    }
}
