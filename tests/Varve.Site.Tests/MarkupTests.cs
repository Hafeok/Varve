// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Markdig.Syntax;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>The markup the generator writes from Markdown: the diagram's alt, page order, tables and the ADR index's Notes.</summary>
public class MarkupTests
{
    [Fact]
    public void a_diagrams_alt_names_each_node_once_in_the_order_it_is_defined()
    {
        const string Source = """
            flowchart LR
              md["docs/**/*.md"] --> parse["Markdig: CommonMark"]
              parse --> gate{"links and anchors"}
              gate -- broken --> fail["build fails"]
              gate -- whole --> rewrite(rewrite<br>links)
              md --> parse
              db[(the log)] -->|appends| gate
              %% a comment[that is not a node]
            """;

        Assert.Equal(
            "Diagram: docs/**/*.md; Markdig: CommonMark; links and anchors; build fails; rewrite links; the log",
            DiagramAlt.Of(Source));
    }

    [Fact]
    public void a_sequence_diagrams_alt_names_its_participants()
    {
        const string Source = """
            sequenceDiagram
              participant C as The client
              participant S
              C->>S: write
            """;

        Assert.Equal("Diagram: The client; S", DiagramAlt.Of(Source));
    }

    [Fact]
    public void a_diagram_with_no_label_keeps_the_text_the_site_always_gave_it()
    {
        Assert.Equal(DiagramAlt.NoLabels, DiagramAlt.Of("flowchart LR\n  a --> b\n"));
    }

    [Fact]
    public void adr_0247s_diagram_is_named_by_all_twelve_of_its_nodes()
    {
        string adr = File.ReadAllText(Path.Combine(Paths.Root, "docs", "adr", "0247-the-docs-site.md"));
        int start = adr.IndexOf("```mermaid\n", System.StringComparison.Ordinal) + "```mermaid\n".Length;
        string source = adr[start..adr.IndexOf("```", start, System.StringComparison.Ordinal)];
        Assert.Equal(12, DiagramAlt.Labels(source).Count);
    }

    [Fact]
    public void a_sections_pages_are_in_the_order_its_readme_links_them_then_by_name()
    {
        Repository repository = new(Paths.Fixture("order"));
        SiteMap map = SiteMap.Of(repository);
        List<(Page, MarkdownDocument)> pages = [.. map.Pages.Select(p => (p, MarkdownReading.Parse(repository.Read(p.Source))))];
        Dictionary<Section, List<PageEntry>> lists = SiteBuild.PageLists(repository, map, pages);

        List<PageEntry> operatorPages = lists[SiteMap.Sections.Single(s => s.Source == "docs/operator")];
        Assert.Equal(["operator/index.html", "operator/b.html", "operator/a.html", "operator/c.html"], operatorPages.Select(e => e.Output));
        Assert.Equal(["Overview", "B", "A", "C"], operatorPages.Select(e => e.Label));
    }

    [Fact]
    public void the_bar_is_the_site_maps_sections_in_order_so_a_new_section_needs_no_change_to_the_frame()
    {
        SiteMap map = SiteMap.Of(new Repository(Paths.Root));
        Assert.Equal(["Operator guide", "Specifications", "Decisions", "Problems", "API"], map.Bar().Select(s => s.Bar));
    }

    [Fact]
    public void a_table_is_wrapped_and_one_whose_header_cells_are_all_empty_is_a_key_value_table()
    {
        string html = SiteBuild.Tables(
            "<table>\n<thead>\n<tr>\n<th></th>\n<th></th>\n</tr>\n</thead>\n<tbody></tbody>\n</table>\n"
            + "<table>\n<thead>\n<tr>\n<th style=\"text-align: right;\">#</th>\n<th>Title</th>\n</tr>\n</thead>\n</table>\n",
            index: false);

        Assert.Equal(
            "<div class=\"vd-table vd-table--kv\" tabindex=\"0\" role=\"region\" aria-label=\"Table\"><table>\n<thead>\n<tr>\n<th></th>\n<th></th>\n</tr>\n</thead>\n<tbody></tbody>\n</table></div>\n"
            + "<div class=\"vd-table\" tabindex=\"0\" role=\"region\" aria-label=\"Table: #, Title\"><table>\n<thead>\n<tr>\n<th align=\"right\">#</th>\n<th>Title</th>\n</tr>\n</thead>\n</table></div>\n",
            html);
    }

    [Fact]
    public void a_code_block_is_a_named_region_in_the_tab_order()
    {
        Assert.Equal(
            "<pre tabindex=\"0\" role=\"region\" aria-label=\"Code: sh\"><code class=\"language-sh\">x</code></pre>\n"
            + "<pre tabindex=\"0\" role=\"region\" aria-label=\"Code\"><code>y</code></pre>\n"
            + "<pre tabindex=\"0\" role=\"region\" aria-label=\"Code\" class=\"front-matter\"><code>z</code></pre>\n",
            SiteBuild.Scrollable(
                "<pre><code class=\"language-sh\">x</code></pre>\n<pre><code>y</code></pre>\n<pre class=\"front-matter\"><code>z</code></pre>\n"));
    }

    [Fact]
    public void the_adr_index_splits_each_status_into_the_ledgers_badge_and_the_readmes_notes()
    {
        Ledger ledger = Ledger.Read(new Repository(Paths.Fixture("ledger")));
        string html = SiteBuild.AdrIndex(
            "<th>Status</th>\n"
            + "<tr>\n<td align=\"right\"><a href=\"0003-x.html\">0003</a></td>\n<td>T</td>\n<td><strong>Superseded by 0004</strong></td>\n</tr>\n"
            + "<tr>\n<td align=\"right\"><a href=\"0005-x.html\">0005</a></td>\n<td>T</td>\n<td>Accepted; <strong>amended by 0006</strong></td>\n</tr>\n"
            + "<tr>\n<td align=\"right\"><a href=\"0002-x.html\">0002</a></td>\n<td>T</td>\n<td>Filed unaccepted (#1)</td>\n</tr>\n",
            ledger);

        Assert.Contains("<th>Status</th><th>Notes</th>", html, System.StringComparison.Ordinal);
        Assert.Contains("<td class=\"vd-state-cell\"><span class=\"vw-state vw-state--neutral\">Superseded</span></td><td>by 0004</td>", html, System.StringComparison.Ordinal);
        Assert.Contains("<td class=\"vd-state-cell\"><span class=\"vw-state vw-state--ok\">Accepted</span></td><td><strong>amended by 0006</strong></td>", html, System.StringComparison.Ordinal);
        Assert.Contains("<td class=\"vd-state-cell\"><span class=\"vw-state vw-state--warn\">Accepted &#183; 1 pending</span></td><td>Filed unaccepted (#1)</td>", html, System.StringComparison.Ordinal);
    }
}
