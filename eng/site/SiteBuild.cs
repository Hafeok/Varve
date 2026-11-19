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
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
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

        Ledger ledger = Ledger.Read(repository);
        SiteContext site = new(commit, SiteStyles.Licence(repository), map.Bar());
        Dictionary<Section, List<PageEntry>> lists = PageLists(repository, map, pages);
        List<ApiPackage> packages = ApiReference.Read(repository);
        lists[SiteMap.Api] = [new("api/index.html", "Overview"), .. packages.Select(static p => new PageEntry($"api/{p.Name}.html", p.Name))];

        foreach ((Page page, MarkdownDocument document) in pages)
        {
            Rewrite(repository, map, page, document, commit);
            WriteText(Path.Combine(output, page.Output), Layout.Page(site, Article(page, document, ledger, lists)));
        }

        WriteText(Path.Combine(output, "index.html"), Layout.Page(site, new PageModel("index.html", "Varve documentation", null, null, Home(repository, map, packages.Count), null, [], [], Home: true)));
        WriteText(Path.Combine(output, "learning", "index.html"), Layout.Page(site, new PageModel(
            "learning/index.html", "Learning track", null, new Head("Learning track"), Learning(), null, [], [])));
        ApiReference.Write(output, site, packages, lists[SiteMap.Api]);
        SiteStyles.Write(repository, output);

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
            string source = block.Lines.ToString();
            string id = DiagramId(source);
            Replace(block, $"<figure class=\"vd-diagram\"><img src=\"{Layout.Relative(page.Output, "diagrams/" + id + ".svg")}\" alt=\"{Layout.Encode(DiagramAlt.Of(source))}\"></figure>");
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

    /// <summary>
    /// The home page (docs/ui/site/README.md, "Home"): the title and one
    /// sentence, three ways in, a card per part of the site with its count, and
    /// the learning track's notice while it is not written.
    /// </summary>
    private static string Home(Repository repository, SiteMap map, int packages)
    {
        StringBuilder html = new();
        html.Append("<div class=\"vd-home-head\"><div>\n<h1>Varve documentation</h1>\n");
        html.Append("<p>Varve is a graph database and RDF/SPARQL toolkit for .NET, event-sourced: the log is the source of truth, and every index is a projection of it. These pages are built from the repository's <code>docs/</code> at the commit named at the foot of each page.</p>\n");
        html.Append("<div class=\"vd-start\">");

        if (map.PageOf("docs/operator/run.md") is { } run)
        {
            html.Append("<a class=\"vd-primary\" href=\"").Append(run.Output).Append("\">Run a server</a>");
        }

        foreach ((string readme, string label) in (ReadOnlySpan<(string, string)>)[("docs/operator/README.md", "Operator guide"), ("docs/adr/README.md", "Architecture decisions")])
        {
            if (map.PageOf(readme) is { } start)
            {
                html.Append("<a href=\"").Append(start.Output).Append("\">").Append(label).Append("</a>");
            }
        }

        html.Append("</div>\n</div></div>\n");
        html.Append("<h2>Reference</h2>\n<ul class=\"vd-grid\">");

        foreach (Section section in map.CarriedSections())
        {
            int count = section.Source is null ? packages : map.CountOf(section);

            // A section that is only its README is one page, not none of its kind.
            Card(html, section.Output + "/index.html", section.Title, section.Summary, count == 0 ? "1 page" : $"{count} {(count == 1 ? section.One : section.Many)}");
        }

        foreach ((string source, string output, string title, string summary) in SiteMap.Files.Where(f => map.PageOf(f.Source) is not null))
        {
            Card(html, output, title, summary, "1 page");
        }

        html.Append("</ul>\n<h2>Learning track</h2>\n");
        html.Append("<div class=\"vd-notice\" role=\"note\"><span class=\"vw-state vw-state--warn\">Not written yet</span><span>The <a href=\"learning/index.html\">learning track</a> (<a href=\"")
            .Append(Layout.Repository).Append("/issues/94\">#94</a>) gets its pages here as it is written.</span></div>\n");
        return html.ToString();
    }

    private static void Card(StringBuilder html, string href, string title, string summary, string count) =>
        html.Append("<li><a href=\"").Append(href).Append("\"><b>").Append(Layout.Encode(title)).Append("</b><span>").Append(Layout.Encode(summary))
            .Append("</span><small>").Append(Layout.Encode(count)).Append("</small></a></li>");

    private static string Learning() =>
        "<div class=\"vd-notice\" role=\"note\"><span class=\"vw-state vw-state--warn\">Stub</span><span>This page is a placeholder, marked as one: the learning track, Varve for SQL developers and DDD practitioners, is not written yet. It is milestone <a href=\""
        + Layout.Repository + "/issues/94\">#94</a>, and its pages take this place on the site when they land.</span></div>\n";

    /// <summary>
    /// Each Markdown section's page list, in order: its README as "Overview",
    /// then the pages its README links, in the order of the links, then every
    /// page it does not link, by name. An ADR's entry carries its number.
    /// </summary>
    internal static Dictionary<Section, List<PageEntry>> PageLists(Repository repository, SiteMap map, List<(Page Page, MarkdownDocument Document)> pages)
    {
        Dictionary<Section, List<PageEntry>> lists = [];

        foreach (Section section in map.CarriedSections().Where(static s => s.Source is not null))
        {
            Dictionary<string, (Page Page, MarkdownDocument Document)> mine = pages
                .Where(p => p.Page.Section == section)
                .ToDictionary(static p => p.Page.Source, StringComparer.Ordinal);
            string readme = section.Source + "/README.md";
            List<string> order = [readme];

            foreach ((LinkInline link, _) in MarkdownReading.Links(mine[readme].Document))
            {
                LinkTarget target = Links.Resolve(repository, readme, link.Url!);

                if (target.Kind == TargetKind.File && mine.ContainsKey(target.Path) && !order.Contains(target.Path))
                {
                    order.Add(target.Path);
                }
            }

            order.AddRange(mine.Keys.Where(k => !order.Contains(k)).Order(StringComparer.Ordinal));
            lists[section] = [.. order.Select(source => Entry(section, mine[source].Page, mine[source].Document))];
        }

        return lists;
    }

    private static PageEntry Entry(Section section, Page page, MarkdownDocument document)
    {
        if (page.Source.EndsWith("/README.md", StringComparison.Ordinal))
        {
            return new PageEntry(page.Output, "Overview");
        }

        string title = MarkdownReading.Title(document) ?? Path.GetFileNameWithoutExtension(page.Source);

        if (section.Output == "adr" && Ledger.NumberOf(Path.GetFileName(page.Source)) is { } number)
        {
            return new PageEntry(page.Output, AdrNumberPrefix().Replace(title, ""), number);
        }

        return new PageEntry(page.Output, title);
    }

    /// <summary>
    /// One Markdown page in the frame: its H1 moved into the head, an anchor on
    /// each h2 and h3, its tables wrapped, and the head its kind takes.
    /// </summary>
    private static PageModel Article(Page page, MarkdownDocument document, Ledger ledger, Dictionary<Section, List<PageEntry>> lists)
    {
        HeadingBlock? h1 = document.Descendants<HeadingBlock>().FirstOrDefault(static h => h.Level == 1);
        string plain = h1?.Inline is null ? Path.GetFileNameWithoutExtension(page.Source) : MarkdownReading.PlainText(h1.Inline);
        string titleHtml = h1?.Inline is null ? Layout.Encode(plain) : RenderInline(h1.Inline);
        Section? section = page.Section;
        bool index = section is not null && page.Source == section.Source + "/README.md";
        Head head = new(titleHtml);

        if (index)
        {
            head = new Head(Layout.Encode(section!.Title), Lede: Layout.Encode(section.Summary));
        }
        else if (section?.Output == "adr" && Ledger.NumberOf(Path.GetFileName(page.Source)) is { } number)
        {
            head = AdrHead(number, titleHtml, ledger, page.Output, document.Descendants<HeadingBlock>().Select(static h => h.GetAttributes().Id).OfType<string>().ToList());
        }
        else if (section is { Ident: true } && ProblemRows(document) is { } rows)
        {
            head = new Head(
                Layout.Encode(plain),
                Eyebrow: "Problem type" + (rows.Status is null ? "" : " <code>HTTP " + Layout.Encode(rows.Status) + "</code>"),
                Ident: true,
                Lede: rows.Title);
        }

        if (h1 is not null)
        {
            h1.Parent!.Remove(h1);
        }

        List<TocEntry> toc = [];

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>().Where(static h => h.Level is 2 or 3).ToList())
        {
            if (heading.GetAttributes().Id is not { } id || heading.Inline is null)
            {
                continue;
            }

            toc.Add(new TocEntry(heading.Level, id, MarkdownReading.PlainText(heading.Inline)));
            heading.Inline.AppendChild(new LiteralInline(" "));
            heading.Inline.AppendChild(new HtmlInline(Anchor(id)));
        }

        string body = Scrollable(Tables(Render(document), index && section is { Output: "adr" or "problems" or "rules" or "decisions" }));

        if (index && section!.Output == "adr")
        {
            body = AdrIndex(body, ledger);
        }

        return new PageModel(page.Output, plain, section, head, body, page.Source, section is null ? [] : lists[section], toc);
    }

    /// <summary>The anchor link every h2 and h3 carries, shown on hover and on focus.</summary>
    public static string Anchor(string id) =>
        "<a class=\"vd-anchor\" aria-label=\"Link to this section\" href=\"#" + Layout.Encode(id) + "\">#</a>";

    /// <summary>An ADR's head: its number above the title, its badge, its decision set, and any unaccepted amendment by another ADR.</summary>
    internal static Head AdrHead(string number, string titleHtml, Ledger ledger, string output, IReadOnlyList<string> headingIds)
    {
        AdrState state = ledger.StateOf(number);
        List<(string, string)> meta = [("Status", state.Badge.Html)];
        meta.Add(("Decision set", state.Set is null
            ? "no decision set"
            : "<a href=\"" + Layout.Relative(output, "decisions/" + state.Set + ".html") + "\"><code>" + Layout.Encode(state.Set) + "</code></a>"));

        // Which amendment the pending rulings belong to, when their statements
        // date one, linked to the amendment's own section when the page has it.
        // A badge that already says nothing is accepted names only dated ones.
        bool partly = state.Badge.Word.EndsWith(" pending", StringComparison.Ordinal);

        foreach (Pending pending in state.Pending.Where(p => partly || p.Amendment is not null))
        {
            string count = pending.Count + (pending.Count == 1 ? " ruling" : " rulings");
            string? section = pending.Amendment is null ? null : headingIds.FirstOrDefault(id => id.StartsWith("amendment-" + pending.Amendment, StringComparison.Ordinal));
            string where = pending.Amendment is null ? ", not dated by an amendment"
                : section is null ? ", from the amendment of " + Layout.Encode(pending.Amendment)
                : ", from <a href=\"#" + Layout.Encode(section) + "\">the amendment of " + Layout.Encode(pending.Amendment) + "</a>";
            meta.Add(("Pending", count + where));
        }

        foreach (AmendmentNote note in state.Notes)
        {
            meta.Add(("Amended", (note.Date is null ? "" : Layout.Encode(note.Date) + ", ") + "by <a href=\"" + Layout.Relative(output, "adr/index.html")
                + "\">ADR " + note.By + "</a>, not yet accepted (" + note.Unaccepted + (note.Unaccepted == 1 ? " ruling)" : " rulings)")));
        }

        return new Head(AdrNumberPrefix().Replace(titleHtml, ""), Eyebrow: "ADR " + number, Meta: meta);
    }

    /// <summary>A problem page's Title and Status rows, from the key-value table every problem page opens with.</summary>
    private static (string? Title, string? Status)? ProblemRows(MarkdownDocument document)
    {
        if (document.Descendants<Table>().FirstOrDefault() is not { } table)
        {
            return null;
        }

        string? title = null;
        string? status = null;

        foreach (TableRow row in table.OfType<TableRow>().Where(static r => !r.IsHeader))
        {
            if (row.Count < 2 || row[0] is not TableCell { Count: > 0 } key || key[0] is not ParagraphBlock { Inline: { } keyInline }
                || row[1] is not TableCell { Count: > 0 } value || value[0] is not ParagraphBlock { Inline: { } valueInline })
            {
                continue;
            }

            switch (MarkdownReading.PlainText(keyInline).Trim())
            {
                case "Title": title = RenderInline(valueInline); break;
                case "Status": status = MarkdownReading.PlainText(valueInline).Trim(); break;
            }
        }

        return title is null && status is null ? null : (title, status);
    }

    /// <summary>
    /// Tables as the design draws them: each in a <c>div.vd-table</c> that
    /// scrolls, <c>vd-table--kv</c> when every header cell is empty, and
    /// <c>vd-index</c> on a section's index of identifiers. Markdig writes a
    /// column's alignment as a style attribute, which the page's policy
    /// blocks (no 'unsafe-inline'), so it becomes the align attribute the
    /// stylesheet reads.
    /// </summary>
    public static string Tables(string html, bool index)
    {
        html = Alignment().Replace(html, " align=\"$1\"");
        return TableOpen().Replace(html, match =>
        {
            int start = match.Index + match.Length;
            int headEnd = html.IndexOf("</thead>", start, StringComparison.Ordinal);
            int next = html.IndexOf("<table>", start, StringComparison.Ordinal);
            List<string> headers = headEnd >= 0 && (next < 0 || headEnd < next)
                ? [.. HeaderCell().Matches(html[start..headEnd]).Select(static c => WebUtility.HtmlDecode(Tag().Replace(c.Groups[1].Value, "")).Trim())]
                : [];
            bool kv = headers.Count > 0 && headers.All(static h => h.Length == 0);
            string name = headers.Any(static h => h.Length > 0) ? "Table: " + string.Join(", ", headers.Where(static h => h.Length > 0)) : "Table";
            return "<div class=\"vd-table" + (kv ? " vd-table--kv" : "") + (index ? " vd-index" : "") + "\" tabindex=\"0\" role=\"region\" aria-label=\""
                + Layout.Encode(name) + "\"><table>";
        }).Replace("</table>", "</table></div>", StringComparison.Ordinal);
    }

    /// <summary>
    /// A code block scrolls sideways and never wraps, and a table scrolls in
    /// its wrapper; a reader with a keyboard must be able to reach either to
    /// scroll it (WCAG 2.1.1, axe's scrollable-region-focusable). The site has
    /// no script to tell which ones overflow, so each is in the tab order,
    /// as a region with a name (the maintainer's answer 2 on #368): a table is
    /// named by its header cells, a code block by its language.
    /// </summary>
    public static string Scrollable(string html) => PreOpen().Replace(html, static m =>
        "<pre tabindex=\"0\" role=\"region\" aria-label=\"" + (m.Groups[3].Success ? "Code: " + m.Groups[3].Value : "Code") + "\"" + m.Groups[1].Value + ">" + m.Groups[2].Value);

    /// <summary>
    /// The ADR index: each Status cell becomes the ADR's badge, read from the
    /// ledger like its page's, and a Notes cell that keeps the README's own
    /// words after the state: "Accepted; amended by 0068" keeps "amended by
    /// 0068", "Superseded by 0031" keeps "by 0031".
    /// </summary>
    public static string AdrIndex(string html, Ledger ledger)
    {
        html = StatusHeader().Replace(html, "$0<th>Notes</th>");
        return IndexRow().Replace(html, match =>
        {
            string notes = match.Groups["status"].Value.Trim();
            notes = LeadingAccepted().Replace(notes, "");
            notes = LeadingSuperseded().Replace(notes, "$1");
            Badge badge = ledger.StateOf(match.Groups["number"].Value).Badge;
            return match.Groups["lead"].Value + "<td class=\"vd-state-cell\">" + badge.Html + "</td><td>" + notes + "</td>\n</tr>";
        });
    }

    private static string RenderInline(ContainerInline inline)
    {
        StringWriter writer = new() { NewLine = "\n" };
        HtmlRenderer renderer = new(writer);
        MarkdownReading.Pipeline.Setup(renderer);
        renderer.WriteChildren(inline);
        writer.Flush();
        return writer.ToString();
    }

    [GeneratedRegex(@"^\d{4}\s*[—–-]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex AdrNumberPrefix();

    [GeneratedRegex(@" style=""text-align: (left|right|center);""", RegexOptions.CultureInvariant)]
    private static partial Regex Alignment();

    [GeneratedRegex("<pre( [^>]*)?>(<code class=\"language-([A-Za-z0-9+#-]+)\")?", RegexOptions.CultureInvariant)]
    private static partial Regex PreOpen();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex("<table>", RegexOptions.CultureInvariant)]
    private static partial Regex TableOpen();

    [GeneratedRegex(@"<th(?:\s[^>]*)?>(.*?)</th>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderCell();

    [GeneratedRegex(@"<th>Status</th>", RegexOptions.CultureInvariant)]
    private static partial Regex StatusHeader();

    [GeneratedRegex(@"(?<lead><tr>\s*<td[^>]*><a href=""(?<number>\d{4})-[^""]*"">\d{4}</a></td>\s*<td>.*?</td>\s*)<td>(?<status>.*?)</td>\s*</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex IndexRow();

    [GeneratedRegex(@"^Accepted\b[.,;]?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingAccepted();

    [GeneratedRegex(@"^<strong>Superseded (by [^<]*)</strong>", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingSuperseded();

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

            // The policy allows styles from the site's own files alone, so an
            // inline style would be dropped by the browser without a word.
            if (InlineStyle().IsMatch(html))
            {
                findings.Add($"{relative} holds an inline style, which its Content Security Policy blocks");
            }

            // Everything a page loads is served from the site: a link to
            // another origin is a link, but a resource from one is not allowed.
            foreach (Match load in Source().Matches(html).Concat(LinkedFile().Matches(html)))
            {
                if (MarkdownReading.IsExternal(WebUtility.HtmlDecode(load.Groups[1].Value)))
                {
                    findings.Add($"{relative} loads {load.Groups[1].Value} from another origin");
                }
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

        foreach (string file in Directory.EnumerateFiles(output, "*.css", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string css = File.ReadAllText(file);

            if (css.Contains("@import", StringComparison.Ordinal) || CssUrl().Matches(css).Any(static m => !m.Groups[1].Value.StartsWith('#')))
            {
                findings.Add($"{Path.GetRelativePath(output, file).Replace('\\', '/')} imports or references a file: the site's stylesheets load nothing");
            }
        }

        return findings;
    }

    [GeneratedRegex("\\sstyle=\"|<style[\\s>]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyle();

    [GeneratedRegex("\\ssrc=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Source();

    [GeneratedRegex("<link\\s[^>]*?href=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex LinkedFile();

    [GeneratedRegex("url\\(\\s*['\"]?([^'\")]*)", RegexOptions.CultureInvariant)]
    private static partial Regex CssUrl();

    [GeneratedRegex("(?:href|src)=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Reference();

    [GeneratedRegex("\\sid=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Id();
}
