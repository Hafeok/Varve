// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>
/// The site's markup is the design's (docs/ui/site/README.md): a page of each
/// kind, built from the current Markdown, against its page under
/// <c>docs/ui/site/reference/</c>. Structure only. The reference pages are
/// pinned to c754bcb and the docs move on, so text and heading identifiers
/// are ignored, and a page list is compared by its shape, its current page
/// and the order of the pages both still have. <c>docs/ui/site/reference/</c>
/// belongs to the design: when the design changes, its side replaces those
/// pages, and this test is how they stay true with eng/site the one renderer.
/// </summary>
public class ContractTests(BuiltSite site)
{
    public static TheoryData<string, string> Kinds => new()
    {
        { "index.html", "index.html" },                                                       // home
        { "operator/index.html", "operator/index.html" },                                     // a section index
        { "adr/0247-the-docs-site.html", "adr/0247-the-docs-site.html" },                     // an ADR, with a diagram
        { "problems/as-of-distance-exceeded.html", "problems/as-of-distance-exceeded.html" }, // a problem type
        { "operator/configure.html", "operator/configure.html" },                             // an operator page, with tables
        { "adr/index.html", "adr/index.html" },                                               // the ADR index, its badges and Notes
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task a_page_has_the_markup_of_its_reference_page(string built, string reference)
    {
        Element b = await PageAsync(built);
        Element r = Reference(reference);

        HeadLinks(b, r);
        Assert.Equal(Names(Body(r).Children), Names(Body(b).Children));
        Bar(b, r, built, reference);
        Frame(b, r);
        Article(b, r, built);
        PageList(b, r, built, reference);
        Toc(b, r);
        Prose(b, r);
        Foot(b, r);
    }

    /// <summary>
    /// The design has no API reference page (#372), so a package page is held
    /// to the frame of an article and to ul.vd-api (the maintainer's answer 5
    /// on #368).
    /// </summary>
    [Fact]
    public async Task an_api_reference_page_has_the_frame_of_an_article_and_its_members_in_ul_vd_api()
    {
        Element b = await PageAsync("api/Varve.Rdf.html");
        Element r = Reference("operator/configure.html");

        HeadLinks(b, r);
        Assert.Equal(Names(Body(r).Children), Names(Body(b).Children));
        Assert.Equal(Without(r.First("header", "vd-bar")!.Shape(), "[aria-current]"), Without(b.First("header", "vd-bar")!.Shape(), "[aria-current]"));
        Assert.Equal("api/index.html", Current(b, "api/Varve.Rdf.html"));

        // The frame of an article; its page list's length is the API's own.
        Element bf = b.First("div", "vd-frame")!;
        Element rf = r.First("div", "vd-frame")!;
        Assert.Equal(rf.Classes.Order(StringComparer.Ordinal), bf.Classes.Order(StringComparer.Ordinal));
        Assert.Equal(Names(rf.Children), Names(bf.Children).Select(static n => Without(n, ".vd-pages--long")));
        Assert.Equal(Names(r.First("main")!.Children), Names(b.First("main")!.Children));
        Assert.Equal(r.First("p", "vd-crumbs")!.Shape(), b.First("p", "vd-crumbs")!.Shape());
        Assert.Equal(r.First("nav", "vd-pager")!.Shape(), b.First("nav", "vd-pager")!.Shape());
        Toc(b, r);
        Foot(b, r);

        Element list = b.First("ul", "vd-api") ?? throw new Xunit.Sdk.XunitException("No ul.vd-api.");
        Assert.All(list.Children, static li => Assert.Equal("li(code)", "li(" + string.Join(",", li.Children.Select(static c => c.Tag)) + ")"));
        Assert.DoesNotContain(b.All("ul"), static ul => ul.Has("api"));
        Anchors(b.First("div", "vd-prose")!);
    }

    private static bool LearningTrackWritten => File.Exists(Path.Combine(Paths.Root, "docs", "learn", "README.md"));

    /// <summary>
    /// The learning track's pages, which the design has no reference page for,
    /// take the design: each against the reference page of its kind (the
    /// section's index against operator/index, a chapter against
    /// operator/configure) on the head links, the body, the bar, the frame's
    /// areas, the crumbs, the head, the prose's vocabulary and anchors, the
    /// diagrams, "On this page", the page list, the pager and the foot. Every
    /// finding on every page is collected, so one run names them all.
    /// </summary>
    [Fact]
    public async Task every_learning_track_page_takes_the_design()
    {
        if (!LearningTrackWritten)
        {
            Assert.Skip("docs/learn/ is not on this branch.");
        }

        string output = await site.OutputAsync();
        List<string> pages = [.. Directory.EnumerateFiles(Path.Combine(output, "learning"), "*.html").Select(f => "learning/" + Path.GetFileName(f)).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(pages);
        List<string> findings = [];

        foreach (string page in pages)
        {
            bool index = page == "learning/index.html";
            string reference = index ? "operator/index.html" : "operator/configure.html";
            Element b = await PageAsync(page);
            Element r = Reference(reference);

            void Check(string what, Action check)
            {
                try
                {
                    check();
                }
                catch (Exception e) when (e is not Xunit.Sdk.SkipException)
                {
                    findings.Add($"{page}, {what}: {e.Message.ReplaceLineEndings(" ")}");
                }
            }

            Check("head links", () => HeadLinks(b, r));
            // Whether a page has "On this page", and so a wide frame, is its
            // text's: the frame's classes are the "frame" check's.
            Check("body", () => Assert.Equal(
                Names(Body(r).Children).Select(static n => Without(n, ".vd-frame--wide")),
                Names(Body(b).Children).Select(static n => Without(n, ".vd-frame--wide"))));
            Check("bar", () => Assert.Equal(Without(r.First("header", "vd-bar")!.Shape(), "[aria-current]"), Without(b.First("header", "vd-bar")!.Shape(), "[aria-current]")));
            Check("frame", () =>
            {
                Element bf = b.First("div", "vd-frame")!;
                Assert.Contains(string.Join(" ", bf.Classes.Order(StringComparer.Ordinal)), (string[])["vd-frame", "vd-frame vd-frame--wide"]);
                Assert.Equal(bf.Has("vd-frame--wide") ? ["main.vd-article[id]", "nav.vd-pages[aria-label][id]"] : ["main.vd-article[id]", "nav.vd-pages[aria-label][id]", "aside.vd-toc[aria-label]"], Names(bf.Children));
            });
            Check("crumbs", () => Assert.Equal(r.First("p", "vd-crumbs")!.Shape(), b.First("p", "vd-crumbs")!.Shape()));
            Check("head", () => Head(b.First("header", "vd-head")!, r.First("header", "vd-head")!));
            Check("one h1, in the head", () => Assert.True(Assert.Single(b.All("h1")).Parent!.Has("vd-head")));
            Check("prose", () =>
            {
                Element prose = b.First("div", "vd-prose")!;
                Assert.All(prose.All("table"), static t => Assert.True(t.Parent!.Has("vd-table"), "a table outside a div.vd-table"));
                Assert.All(prose.All("div", "vd-table").Select(static d => string.Join(" ", d.Classes.Order(StringComparer.Ordinal))), static w => Assert.Contains(w, Vocabulary.Value.Wrappers));
                Anchors(prose);
                Assert.All(prose.All("figure", "vd-diagram"), static f => Assert.StartsWith("Diagram: ", f.First("img")!["alt"]!, StringComparison.Ordinal));
            });
            Check("contents", () =>
            {
                if (b.First("aside", "vd-toc") is { } toc)
                {
                    Assert.All(toc.All("li"), static li => Assert.Contains(li.Shape(), Vocabulary.Value.TocEntries));
                }
            });
            Check("page list", () =>
            {
                Element list = b.First("nav", "vd-pages")!;
                Assert.Equal(r.First("nav", "vd-pages")!.Name(), list.Name());
                Assert.Equal(page, Resolve(page, Assert.Single(list.All("a"), static a => a["aria-current"] == "page")["href"]!));
            });
            Check("pager", () =>
            {
                if (!index)
                {
                    Assert.Equal(r.First("nav", "vd-pager")!.Name(), b.First("nav", "vd-pager")!.Name());
                }
            });
            Check("foot", () => Foot(b, r));
        }

        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    private async Task<Element> PageAsync(string page) =>
        Markup.Parse(await File.ReadAllTextAsync(Path.Combine(await site.OutputAsync(), page), TestContext.Current.CancellationToken));

    private static Element Reference(string page) => Markup.Parse(File.ReadAllText(Paths.Reference(page)));

    private static Element Body(Element page) => page.First("body")!;

    private static List<string> Names(IEnumerable<Element> elements) => [.. elements.Select(static e => e.Name())];

    private static string Without(string text, string part) => text.Replace(part, "", StringComparison.Ordinal);

    /// <summary>
    /// An href as the path it names from the site's root. The reference writes
    /// every link from the root ("../operator/index.html" from operator/), the
    /// generator the shortest ("index.html"): both name the same page.
    /// </summary>
    private static string Resolve(string page, string href) =>
        href.Contains("://", StringComparison.Ordinal) ? href : Repository.Normalise(Repository.DirectoryOf(page) + "/" + href.Split('#')[0])!;

    private static string? Current(Element page, string path) =>
        page.First("nav", "vd-sections")!.Children.SingleOrDefault(static a => a["aria-current"] == "page") is { } a ? Resolve(path, a["href"]!) : null;

    /// <summary>The same metadata, the same policy, and the same icon and stylesheets, in the same order.</summary>
    private static void HeadLinks(Element b, Element r)
    {
        static List<string> Links(Element page) => [.. page.First("head")!.Children.Where(static e => e.Tag == "link").Select(static e => e["rel"] + " " + e["href"])];
        static List<string> Metas(Element page) => [.. page.First("head")!.Children.Where(static e => e.Tag == "meta").Select(static e => string.Join(" ", e.Attributes.OrderBy(static a => a.Key, StringComparer.Ordinal).Select(static a => a.Key + "=" + a.Value)))];

        Assert.Equal(Links(r), Links(b));
        Assert.Equal(Metas(r), Metas(b));
    }

    /// <summary>The bar: the mark, the name, the sections, the current one, and the repository.</summary>
    private static void Bar(Element b, Element r, string built, string reference)
    {
        Assert.Equal(r.First("header", "vd-bar")!.Shape(), b.First("header", "vd-bar")!.Shape());
        Assert.Equal(
            r.First("nav", "vd-sections")!.Children.Select(a => Resolve(reference, a["href"]!)),
            b.First("nav", "vd-sections")!.Children.Select(a => Resolve(built, a["href"]!)));
        Assert.Equal(Current(r, reference), Current(b, built));
    }

    /// <summary>The frame's areas, and their order in the document: the article, then the page list, then the contents.</summary>
    private static void Frame(Element b, Element r)
    {
        Element bf = b.First("div", "vd-frame")!;
        Element rf = r.First("div", "vd-frame")!;
        Assert.Equal(rf.Classes.Order(StringComparer.Ordinal), bf.Classes.Order(StringComparer.Ordinal));
        Assert.Equal(Names(rf.Children), Names(bf.Children));
    }

    /// <summary>The article: the crumbs, the head (eyebrow, title, lede, meta), the prose and the pager, in that order.</summary>
    private static void Article(Element b, Element r, string page)
    {
        Element bm = b.First("main")!;
        Element rm = r.First("main")!;

        if (rm.Has("vd-home"))
        {
            // The home page: the head and its three ways in, the cards, and,
            // until the learning track is written, its notice (the design:
            // "a warn notice saying it isn't written yet, until #94 lands").
            List<string> expected = Names(rm.Children);

            if (LearningTrackWritten)
            {
                Assert.Equal(["h2", "div.vd-notice[role]"], expected[^2..]);
                expected = expected[..^2];
                Assert.Null(bm.First("div", "vd-notice"));
            }

            Assert.Equal(expected, Names(bm.Children));
            Assert.Equal(rm.First("div", "vd-home-head")!.Shape(), bm.First("div", "vd-home-head")!.Shape());
            Assert.Equal(rm.First("ul", "vd-grid")!.Children.Select(static li => li.Shape()).Distinct(), bm.First("ul", "vd-grid")!.Children.Select(static li => li.Shape()).Distinct());
            if (!LearningTrackWritten)
            {
                Assert.Equal(rm.First("div", "vd-notice")!.Shape(), bm.First("div", "vd-notice")!.Shape());
            }

            return;
        }

        Assert.Equal(Names(rm.Children), Names(bm.Children));
        Assert.Equal(rm.First("p", "vd-crumbs")!.Shape(), bm.First("p", "vd-crumbs")!.Shape());
        Head(bm.First("header", "vd-head")!, rm.First("header", "vd-head")!);
        Assert.Equal(rm.First("nav", "vd-pager")?.Shape(), bm.First("nav", "vd-pager")?.Shape());
        Assert.True(bm.First("h1") is { } h1 && h1.Parent!.Has("vd-head"), $"{page}: the H1 is in the head.");
        Assert.Single(b.All("h1"));
    }

    /// <summary>
    /// The head: its parts in order (eyebrow, title, lede, meta), each part's
    /// shape, and the kinds of meta item. How many items the meta holds is the
    /// ledger's answer (an ADR with pending rulings names their amendment), so
    /// the items are compared by their distinct shapes, not their number.
    /// </summary>
    private static void Head(Element b, Element r)
    {
        Assert.Equal(Names(r.Children), Names(b.Children));

        foreach ((Element rc, Element bc) in r.Children.Zip(b.Children))
        {
            if (rc.Tag == "dl")
            {
                Assert.Equal(
                    rc.Children.Select(static d => d.Shape()).Distinct().Order(StringComparer.Ordinal),
                    bc.Children.Select(static d => d.Shape()).Distinct().Order(StringComparer.Ordinal));
            }
            else
            {
                Assert.Equal(rc.Shape(), bc.Shape());
            }
        }
    }

    /// <summary>
    /// The page list: its classes and label, the shape of its entries, one
    /// current page that is this page, and the pages both lists hold, in the
    /// same order.
    /// </summary>
    private static void PageList(Element b, Element r, string page, string reference)
    {
        Element? bl = b.First("nav", "vd-pages");
        Element? rl = r.First("nav", "vd-pages");
        Assert.Equal(rl is null, bl is null);

        if (bl is null || rl is null)
        {
            return;
        }

        Assert.Equal(rl.Name(), bl.Name());
        Assert.Equal(rl.Classes.Order(StringComparer.Ordinal), bl.Classes.Order(StringComparer.Ordinal));
        Assert.Equal(Names(rl.Children), Names(bl.Children));
        Assert.Equal(
            rl.All("li").Select(static li => Without(li.Shape(), "[aria-current]")).Distinct().Order(StringComparer.Ordinal),
            bl.All("li").Select(static li => Without(li.Shape(), "[aria-current]")).Distinct().Order(StringComparer.Ordinal));

        Element current = Assert.Single(bl.All("a"), static a => a["aria-current"] == "page");
        Assert.Equal(page, Resolve(page, current["href"]!));

        List<string> builtOrder = [.. bl.All("a").Select(a => Resolve(page, a["href"]!))];
        List<string> referenceOrder = [.. rl.All("a").Select(a => Resolve(reference, a["href"]!)).Where(builtOrder.Contains)];
        Assert.NotEmpty(referenceOrder);
        Assert.Equal(referenceOrder, builtOrder.Where(referenceOrder.Contains));
    }

    private static void Toc(Element b, Element r)
    {
        Element? bt = b.First("aside", "vd-toc");
        Element? rt = r.First("aside", "vd-toc");
        Assert.Equal(rt is null, bt is null);

        if (bt is not null && rt is not null)
        {
            // Its frame is the reference's. Which levels it lists is the
            // page's text (a guide gains an h3, and "On this page" gains an
            // entry), so each entry need only be of a kind the design draws.
            Assert.Equal(rt.Name(), bt.Name());
            Assert.Equal(Frame(rt), Frame(bt));
            Assert.All(bt.All("li"), static li => Assert.Contains(li.Shape(), Vocabulary.Value.TocEntries));
        }
    }

    /// <summary>The prose: every table in a wrapper of the design's kinds, an anchor on every h2 and h3, and a diagram where the reference has one.</summary>
    private static void Prose(Element b, Element r)
    {
        Element? bp = b.First("div", "vd-prose");
        Element? rp = r.First("div", "vd-prose");
        Assert.Equal(rp is null, bp is null);

        if (bp is null || rp is null)
        {
            return;
        }

        static IEnumerable<string> Wrappers(Element prose) => prose.All("div", "vd-table").Select(static d => string.Join(" ", d.Classes.Order(StringComparer.Ordinal))).Distinct().Order(StringComparer.Ordinal);

        // Every wrapper is of a kind the design draws. Which kinds a page
        // holds is its text: a guide that gains a key-value table gains a
        // vd-table--kv, so the page's own reference does not fix the set.
        Assert.All(Wrappers(bp), static w => Assert.Contains(w, Vocabulary.Value.Wrappers));
        Assert.All(bp.All("table"), static t => Assert.True(t.Parent!.Has("vd-table"), "Every table is in a div.vd-table."));
        Anchors(bp);
        Anchors(rp);

        foreach (Element cell in rp.All("td").Where(static td => td.Has("vd-state-cell")).Take(1))
        {
            Element built = bp.All("td").First(static td => td.Has("vd-state-cell"));
            Assert.Equal(cell.Shape(), built.Shape());
        }

        if (rp.First("figure", "vd-diagram") is { } figure)
        {
            Element built = bp.First("figure", "vd-diagram") ?? throw new Xunit.Sdk.XunitException("No figure.vd-diagram.");
            Assert.Equal(figure.Shape(), built.Shape());
            Element img = built.First("img")!;
            Assert.StartsWith("Diagram: ", img["alt"]!, StringComparison.Ordinal);
            Assert.EndsWith(".svg", img["src"]!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The design's vocabulary: every kind of entry in "On this page", and
    /// every kind of table wrapper, that any page under
    /// <c>docs/ui/site/reference/</c> shows. A built page may use these and
    /// no others; which of them it uses is decided by its text.
    /// </summary>
    private static readonly Lazy<(HashSet<string> TocEntries, HashSet<string> Wrappers)> Vocabulary = new(static () =>
    {
        HashSet<string> toc = new(StringComparer.Ordinal);
        HashSet<string> wrappers = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(Paths.Reference(""), "*.html", SearchOption.AllDirectories))
        {
            Element page = Markup.Parse(File.ReadAllText(file));
            toc.UnionWith(page.All("aside", "vd-toc").SelectMany(static a => a.All("li")).Select(static li => li.Shape()));
            wrappers.UnionWith(page.All("div", "vd-table").Select(static d => string.Join(" ", d.Classes.Order(StringComparer.Ordinal))));
        }

        return (toc, wrappers);
    });

    /// <summary>The frame of "On this page": the aside, its div, and the div's heading and list, without the list's entries.</summary>
    private static string Frame(Element toc) =>
        toc.Name() + "(" + string.Join(",", toc.Children.Select(static d => d.Name() + "(" + string.Join(",", d.Children.Select(static c => c.Name())) + ")")) + ")";

    private static void Anchors(Element prose)
    {
        foreach (Element heading in prose.Descendants().Where(static e => e.Tag is "h2" or "h3"))
        {
            Element anchor = Assert.Single(heading.Children, static c => c.Has("vd-anchor"));
            Assert.Equal("a", anchor.Tag);
            Assert.NotNull(anchor["aria-label"]);
            Assert.Equal("#" + heading["id"], anchor["href"]);
        }
    }

    /// <summary>The foot: the commit, the page's source on GitHub, and the licence.</summary>
    private static void Foot(Element b, Element r)
    {
        Element bf = b.First("footer", "vd-foot")!;
        Element rf = r.First("footer", "vd-foot")!;
        Assert.Equal(rf.Shape(), bf.Shape());

        static IEnumerable<string> Inside(Element foot) => foot.Children[0].Children.Select(static e => e.Tag + (e.Children.Count > 0 ? "(" + string.Join(",", e.Children.Select(static c => c.Tag)) + ")" : ""));

        Assert.Equal(Inside(rf), Inside(bf));
        Element commit = bf.Children[0].Children[0];
        Assert.Equal("https://github.com/Hafeok/Varve/tree/" + BuiltSite.Commit, commit["href"]);
    }
}
