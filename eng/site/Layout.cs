// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;

namespace Varve.Site;

/// <summary>What every page of one build shares: the commit it names, the licence its foot names, and the bar's sections.</summary>
internal sealed record SiteContext(string Commit, string Licence, IReadOnlyList<Section> Bar);

/// <summary>An entry of a section's page list: its page, its label, and an ADR's number set before the label.</summary>
internal sealed record PageEntry(string Output, string Label, string? Number = null);

/// <summary>
/// A page's head (docs/ui/site/README.md, "Page heads"): an eyebrow, the title
/// (HTML, the H1 moved out of the body), whether the title is an identifier,
/// a lede (HTML), and the meta list's items (HTML of each dt/dd pair).
/// </summary>
internal sealed record Head(string Title, string? Eyebrow = null, bool Ident = false, string? Lede = null, IReadOnlyList<(string Term, string Value)>? Meta = null);

/// <summary>One entry of "On this page": an h2 or h3 of the page.</summary>
internal sealed record TocEntry(int Level, string Id, string Text);

/// <summary>
/// One page to write: its output path, its plain title for the window, its
/// section (null for a page outside every section), its head and body, the
/// repository file it is built from, its section's page list in order, and
/// its contents. A home page is laid out on its own.
/// </summary>
internal sealed record PageModel(
    string Output,
    string WindowTitle,
    Section? Section,
    Head? Head,
    string Body,
    string? Source,
    IReadOnlyList<PageEntry> Pages,
    IReadOnlyList<TocEntry> Toc,
    bool Home = false);

/// <summary>
/// Every page's frame, and the site's Content Security Policy (ADR 0247;
/// the design is docs/ui/site/, ADR 0247 amended 2026-11-19). In the
/// document: the skip link, the bar, then the article, then the page list,
/// then "On this page", then the foot. Grid areas place them on screen, so a
/// screen reader and a phone reach the content before a list of 222 ADRs.
/// </summary>
internal static class Layout
{
    /// <summary>
    /// The policy every page carries. GitHub Pages sends no header Varve
    /// chooses, so it is a meta element: no script source at all, styles and
    /// images from the site alone, no base and no form. A meta policy cannot
    /// state frame-ancestors or a report endpoint; ADR 0247 says so.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'";

    public const string Repository = "https://github.com/Hafeok/Varve";

    /// <summary>A section's page list is long, and does not stick, past this many entries.</summary>
    public const int LongList = 24;

    /// <summary>"On this page" is written only for a page with at least this many h2 and h3 headings.</summary>
    public const int TocMinimum = 2;

    /// <summary>A link from one output page to another, relative, with '/'.</summary>
    public static string Relative(string from, string to)
    {
        string fromDirectory = Varve.Site.Repository.DirectoryOf(from);
        string[] here = fromDirectory.Length == 0 ? [] : fromDirectory.Split('/');
        string[] there = to.Split('/');
        int common = 0;

        while (common < here.Length && common < there.Length - 1 && here[common] == there[common])
        {
            common++;
        }

        return string.Concat(Enumerable.Repeat("../", here.Length - common)) + string.Join('/', there[common..]);
    }

    public static string Encode(string text) => WebUtility.HtmlEncode(text);

    public static string Page(SiteContext site, PageModel page)
    {
        string Rel(string to) => Relative(page.Output, to);
        // A section's index is its overview, and has no "On this page", as the design's reference pages show.
        bool index = page.Section is not null && page.Output == page.Section.Output + "/index.html";
        bool toc = !page.Home && !index && page.Section is not null && page.Toc.Count >= TocMinimum;
        bool pages = !page.Home && page.Section is not null && page.Pages.Count > 0;

        StringBuilder html = new();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(ContentSecurityPolicy).Append("\">\n");
        html.Append("<meta name=\"referrer\" content=\"no-referrer\">\n");
        html.Append("<meta name=\"color-scheme\" content=\"light dark\">\n");
        html.Append("<title>").Append(Encode(page.WindowTitle)).Append(" — Varve</title>\n");
        html.Append("<link rel=\"icon\" href=\"").Append(Rel(SiteStyles.Mark)).Append("\" type=\"image/svg+xml\">\n");

        foreach (string stylesheet in SiteStyles.Stylesheets)
        {
            html.Append("<link rel=\"stylesheet\" href=\"").Append(Rel(stylesheet)).Append("\">\n");
        }

        html.Append("</head>\n<body>\n<a class=\"vd-skip\" href=\"#main\">Skip to content</a>\n");

        // The bar: the mark and the name, the sections the site map puts in it,
        // the repository.
        html.Append("<header class=\"vd-bar\">\n<a class=\"vd-brand\" href=\"").Append(Rel("index.html")).Append("\"><img src=\"")
            .Append(Rel(SiteStyles.Mark)).Append("\" alt=\"\"><b>Varve</b><span>Documentation</span></a>\n");
        html.Append("<nav class=\"vd-sections\" aria-label=\"Sections\">");

        foreach (Section section in site.Bar)
        {
            html.Append("<a href=\"").Append(Rel(section.Output + "/index.html")).Append('"');

            if (section == page.Section)
            {
                html.Append(" aria-current=\"page\"");
            }

            html.Append('>').Append(Encode(section.Bar!)).Append("</a>");
        }

        html.Append("</nav>\n<a class=\"vd-repo\" href=\"").Append(Repository).Append("\">GitHub</a>\n</header>\n");

        string frame = page.Home || page.Section is null ? "vd-frame vd-frame--home" : toc ? "vd-frame" : "vd-frame vd-frame--wide";
        html.Append("<div class=\"").Append(frame).Append("\">\n");

        if (page.Home)
        {
            html.Append("<main class=\"vd-home\" id=\"main\">\n").Append(page.Body).Append("</main>\n");
        }
        else
        {
            html.Append("<main class=\"vd-article\" id=\"main\">");
            Crumbs(html, page, pages, Rel);
            HeadOf(html, page.Head);
            html.Append("<div class=\"vd-prose\">").Append(page.Body).Append("</div>");
            Pager(html, page, Rel);
            html.Append("</main>\n");
        }

        if (pages)
        {
            PageList(html, page, Rel);
        }

        if (toc)
        {
            html.Append("<aside class=\"vd-toc\" aria-label=\"On this page\"><div><h2>On this page</h2><ol>");

            foreach (TocEntry entry in page.Toc)
            {
                html.Append("<li class=\"vd-toc-").Append(entry.Level).Append("\"><a href=\"#").Append(Encode(entry.Id)).Append("\">")
                    .Append(Encode(entry.Text)).Append("</a></li>");
            }

            html.Append("</ol></div></aside>\n");
        }

        html.Append("</div>\n<footer class=\"vd-foot\"><span>Built from <a href=\"").Append(Repository).Append("/tree/").Append(site.Commit)
            .Append("\"><code>").Append(site.Commit[..12]).Append("</code></a>");

        if (page.Source is not null)
        {
            html.Append(". This page is <a href=\"").Append(Repository).Append("/blob/").Append(site.Commit).Append('/')
                .Append(page.Source).Append("\">").Append(Encode(page.Source)).Append("</a>");
        }

        html.Append(".</span><span>").Append(Encode(site.Licence)).Append("</span></footer>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static void Crumbs(StringBuilder html, PageModel page, bool pages, Func<string, string> rel)
    {
        html.Append("<p class=\"vd-crumbs\"><a href=\"").Append(rel("index.html")).Append("\">Documentation</a>");
        string index = page.Section is null ? "" : page.Section.Output + "/index.html";

        // A section's own index names no section after "Documentation": it is the section.
        if (page.Section is not null && page.Output != index)
        {
            html.Append("<span aria-hidden=\"true\">/</span><a href=\"").Append(rel(index)).Append("\">")
                .Append(Encode(page.Section.Title)).Append("</a>");
        }

        if (pages)
        {
            html.Append("<a class=\"vd-jump\" href=\"#pages\">Pages in this section ↓</a>");
        }

        html.Append("</p>");
    }

    private static void HeadOf(StringBuilder html, Head? head)
    {
        if (head is null)
        {
            return;
        }

        html.Append("<header class=\"vd-head\">");

        if (head.Eyebrow is not null)
        {
            html.Append("<span class=\"vd-eyebrow\">").Append(head.Eyebrow).Append("</span>");
        }

        html.Append(head.Ident ? "<h1 class=\"vd-ident\">" : "<h1>").Append(head.Title).Append("</h1>");

        if (head.Lede is not null)
        {
            html.Append("<p class=\"vd-lede\">").Append(head.Lede).Append("</p>");
        }

        if (head.Meta is { Count: > 0 } meta)
        {
            html.Append("<dl class=\"vd-meta\">");

            foreach ((string term, string value) in meta)
            {
                html.Append("<div><dt>").Append(Encode(term)).Append("</dt><dd>").Append(value).Append("</dd></div>");
            }

            html.Append("</dl>");
        }

        html.Append("</header>");
    }

    /// <summary>Previous and next, in the page list's order. A section's index has none.</summary>
    private static void Pager(StringBuilder html, PageModel page, Func<string, string> rel)
    {
        int at = -1;

        for (int i = 0; i < page.Pages.Count; i++)
        {
            if (page.Pages[i].Output == page.Output)
            {
                at = i;
            }
        }

        if (at <= 0)
        {
            return;
        }

        html.Append("<nav class=\"vd-pager\" aria-label=\"Previous and next\">");
        Neighbour(html, page.Pages[at - 1], "vd-prev", "Previous", rel);

        if (at + 1 < page.Pages.Count)
        {
            Neighbour(html, page.Pages[at + 1], "vd-next", "Next", rel);
        }

        html.Append("</nav>");
    }

    private static void Neighbour(StringBuilder html, PageEntry entry, string kind, string word, Func<string, string> rel) =>
        html.Append("<a class=\"").Append(kind).Append("\" href=\"").Append(rel(entry.Output)).Append("\"><small>").Append(word)
            .Append("</small><b>").Append(Encode(entry.Number is null ? entry.Label : entry.Number + " — " + entry.Label)).Append("</b></a>");

    private static void PageList(StringBuilder html, PageModel page, Func<string, string> rel)
    {
        Section section = page.Section!;
        html.Append("<nav class=\"vd-pages");

        if (page.Pages.Count > LongList)
        {
            html.Append(" vd-pages--long");
        }

        if (section.Ident)
        {
            html.Append(" vd-pages--ident");
        }

        html.Append("\" id=\"pages\" aria-label=\"").Append(Encode(section.Title)).Append("\"><h2>").Append(Encode(section.Title)).Append("</h2><ol>");

        foreach (PageEntry entry in page.Pages)
        {
            html.Append("<li><a href=\"").Append(rel(entry.Output)).Append('"');

            if (entry.Output == page.Output)
            {
                html.Append(" aria-current=\"page\"");
            }

            html.Append('>');

            if (entry.Number is not null)
            {
                html.Append("<span class=\"vd-num\">").Append(entry.Number).Append("</span>");
            }

            html.Append(Encode(entry.Label)).Append("</a></li>");
        }

        html.Append("</ol></nav>\n");
    }
}
