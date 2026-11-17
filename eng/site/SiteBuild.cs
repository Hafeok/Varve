// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.Yaml;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Varve.Site;

/// <summary>Writes the site (ADR 0247), then checks what it wrote.</summary>
internal static partial class SiteBuild
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<int> RunAsync(Repository repository, SiteMap map, string output, string commit, string? chrome, string cache)
    {
        List<(Page Page, MarkdownDocument Document)> pages = [.. map.Pages
            .OrderBy(static p => p.Source, StringComparer.Ordinal)
            .Select(p => (p, MarkdownReading.Parse(repository.Read(p.Source))))];

        // Every diagram once, named by its source's digest, so the same
        // diagram on two pages is one file and a build names it the same way.
        Dictionary<string, string> diagrams = new(StringComparer.Ordinal);

        foreach ((_, MarkdownDocument document) in pages)
        {
            foreach (FencedCodeBlock block in document.Descendants<FencedCodeBlock>().Where(IsMermaid))
            {
                string source = block.Lines.ToString();
                diagrams[DiagramId(source)] = source;
            }
        }

        // Mermaid is fetched, and Chrome started, only when a page has a diagram.
        Dictionary<string, string> svgs = new(StringComparer.Ordinal);
        string drawnBy = "no diagram";

        if (diagrams.Count > 0)
        {
            if (chrome is null)
            {
                throw new SiteException($"{diagrams.Count} diagrams need a browser: pass --chrome.");
            }

            MermaidArchive mermaid = await MermaidArchive.LoadAsync(repository, cache);
            svgs = await MermaidRenderer.RenderAsync(mermaid, chrome, diagrams);
            drawnBy = $"drawn by mermaid {mermaid.Version}";
        }

        List<string> findings = [];

        foreach ((string id, string svg) in svgs.OrderBy(static s => s.Key, StringComparer.Ordinal))
        {
            findings.AddRange(MermaidRenderer.Findings(svg).Select(f => $"diagram {id} {f}"));
        }

        if (findings.Count > 0)
        {
            findings.ForEach(f => Console.Error.WriteLine($"varve-site: {f}"));
            return 1;
        }

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);

        foreach ((string id, string svg) in svgs)
        {
            WriteText(Path.Combine(output, "diagrams", id + ".svg"), svg + "\n");
        }

        List<(string Title, string Output)> navigation = [.. map.Contents().Select(static c => (c.Title, c.Output))];

        foreach ((Page page, MarkdownDocument document) in pages)
        {
            Rewrite(repository, map, page, document, commit);
            string title = MarkdownReading.Title(document) ?? Path.GetFileNameWithoutExtension(page.Source);
            string body = Render(document);
            WriteText(Path.Combine(output, page.Output), Layout.Page(page.Output, title, body, commit, page.Source, navigation));
        }

        WriteText(Path.Combine(output, "index.html"), Layout.Page("index.html", "Varve documentation", Home(map), commit, null, navigation));
        WriteText(Path.Combine(output, "learning", "index.html"), Layout.Page("learning/index.html", "Learning track", Learning(), commit, null, navigation));
        ApiReference.Write(repository, output, commit, navigation);

        await using (Stream css = typeof(SiteBuild).Assembly.GetManifestResourceStream("site.css")!)
        await using (FileStream file = File.Create(Path.Combine(output, "site.css")))
        {
            await css.CopyToAsync(file);
        }

        // Nothing the site wrote is exempt: every page holds no script, and
        // every link from one page to another reaches a file and an anchor.
        List<string> broken = OutputCheck(output);
        broken.ForEach(f => Console.Error.WriteLine($"varve-site: in the built site: {f}"));

        int files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Count();
        Console.WriteLine($"varve-site: {files} files in {output}, {pages.Count} pages, {svgs.Count} diagrams ({drawnBy}), {broken.Count} findings.");
        return broken.Count == 0 ? 0 : 1;
    }

    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"), Utf8);
    }

    private static bool IsMermaid(FencedCodeBlock block) => string.Equals(block.Info, "mermaid", StringComparison.Ordinal);

    private static string DiagramId(string source) =>
        "m" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.ReplaceLineEndings("\n"))))[..16];

    /// <summary>
    /// Each link becomes what it names on the site: another page, a section's
    /// index, or the file on GitHub at the commit the site is built from, so a
    /// link to code never drifts from the text beside it.
    /// </summary>
    private static void Rewrite(Repository repository, SiteMap map, Page page, MarkdownDocument document, string commit)
    {
        foreach ((LinkInline link, _) in MarkdownReading.Links(document).ToList())
        {
            LinkTarget target = Links.Resolve(repository, page.Source, link.Url!);
            string fragment = target.Fragment is null ? "" : "#" + target.Fragment;

            link.Url = target.Kind switch
            {
                TargetKind.File when map.PageOf(target.Path) is { } other => Layout.Relative(page.Output, other.Output) + fragment,
                TargetKind.File or TargetKind.Missing => $"{Layout.Repository}/blob/{commit}/{target.Path}{fragment}",
                TargetKind.Directory when map.OutputOfDirectory(target.Path) is { } index => Layout.Relative(page.Output, index) + fragment,
                TargetKind.Directory => $"{Layout.Repository}/tree/{commit}/{target.Path}{fragment}",
                _ => link.Url,
            };
        }

        foreach (FencedCodeBlock block in document.Descendants<FencedCodeBlock>().Where(IsMermaid).ToList())
        {
            string id = DiagramId(block.Lines.ToString());
            Replace(block, $"<figure class=\"diagram\"><img src=\"{Layout.Relative(page.Output, "diagrams/" + id + ".svg")}\" alt=\"A diagram; its Mermaid source is in this page's source file.\"></figure>");
        }

        // A decision set's front matter is its rulings: shown as written.
        foreach (YamlFrontMatterBlock block in document.Descendants<YamlFrontMatterBlock>().ToList())
        {
            Replace(block, "<pre class=\"front-matter\"><code>" + WebUtility.HtmlEncode(block.Lines.ToString()) + "</code></pre>");
        }
    }

    private static void Replace(Block block, string html)
    {
        ContainerBlock parent = block.Parent!;
        int index = parent.IndexOf(block);
        parent.RemoveAt(index);
        parent.Insert(index, new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock, Lines = new StringLineGroup(html) });
    }

    private static string Render(MarkdownDocument document)
    {
        StringWriter writer = new() { NewLine = "\n" };
        HtmlRenderer renderer = new(writer);
        MarkdownReading.Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static string Home(SiteMap map)
    {
        StringBuilder html = new();
        html.Append("<h1>Varve documentation</h1>\n");
        html.Append("<p>Varve is a graph database and RDF/SPARQL toolkit for .NET, event-sourced: the log is the source of truth, and every index is a projection of it. These pages are built from the repository's <code>docs/</code> at the commit named at the foot of each page.</p>\n<dl>\n");

        foreach ((string title, string output, string summary) in map.Contents())
        {
            html.Append("<dt><a href=\"").Append(output).Append("\">").Append(Layout.Encode(title)).Append("</a></dt><dd>").Append(Layout.Encode(summary)).Append("</dd>\n");
        }

        html.Append("</dl>\n");
        return html.ToString();
    }

    private static string Learning() =>
        "<h1>Learning track</h1>\n"
        + "<p class=\"notice\"><strong>Stub.</strong> This page is a placeholder, marked as one: the learning track, Varve for SQL developers and DDD practitioners, is not written yet. It is milestone <a href=\"" + Layout.Repository + "/issues/94\">#94</a>, and its pages take this place on the site when they land.</p>\n";

    /// <summary>The built site's own check: no script anywhere, and every local link reaches a file and its anchor.</summary>
    private static List<string> OutputCheck(string output)
    {
        List<string> findings = [];
        Dictionary<string, HashSet<string>> ids = new(StringComparer.Ordinal);

        HashSet<string> IdsOf(string file)
        {
            if (!ids.TryGetValue(file, out HashSet<string>? set))
            {
                set = new HashSet<string>(Id().Matches(File.ReadAllText(file)).Select(static m => WebUtility.HtmlDecode(m.Groups[1].Value)), StringComparer.Ordinal);
                ids[file] = set;
            }

            return set;
        }

        foreach (string file in Directory.EnumerateFiles(output, "*.html", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(output, file).Replace('\\', '/');
            string html = File.ReadAllText(file);

            if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add($"{relative} holds a script");
            }

            foreach (Match match in Reference().Matches(html))
            {
                string url = WebUtility.HtmlDecode(match.Groups[1].Value);

                if (MarkdownReading.IsExternal(url))
                {
                    continue;
                }

                int hash = url.IndexOf('#', StringComparison.Ordinal);
                string path = hash < 0 ? url : url[..hash];
                string? fragment = hash < 0 ? null : Uri.UnescapeDataString(url[(hash + 1)..]);
                string target = path.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(path)));

                if (!File.Exists(target))
                {
                    findings.Add($"{relative} links to {url}, which the site does not have");
                }
                else if (fragment is { Length: > 0 } && target.EndsWith(".html", StringComparison.Ordinal) && !IdsOf(target).Contains(fragment))
                {
                    findings.Add($"{relative} links to {url}, whose page has no such anchor");
                }
            }
        }

        return findings;
    }

    [GeneratedRegex("(?:href|src)=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();

    [GeneratedRegex("\\sid=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Id();
}
