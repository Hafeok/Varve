// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>
/// The built site's own check fails on what its policy would refuse without
/// a word (ADR 0247 amended 2026-11-19): an inline style, a resource loaded
/// from another origin, and a stylesheet that imports or references a file.
/// Each failure path is proven on a page of its own, beside a page that passes.
/// </summary>
public sealed class OutputCheckTests : IDisposable
{
    private readonly string _site = Path.Combine(Path.GetTempPath(), "varve-output-check-" + Guid.NewGuid().ToString("N"));

    public OutputCheckTests() => Directory.CreateDirectory(_site);

    [Theory]
    [InlineData("<td style=\"text-align: right;\">1</td>", "holds an inline style")]
    [InlineData("<style>p { color: red; }</style>", "holds an inline style")]
    [InlineData("<img src=\"https://example.org/a.png\" alt=\"\">", "loads https://example.org/a.png from another origin")]
    [InlineData("<link rel=\"stylesheet\" href=\"//example.org/a.css\">", "loads //example.org/a.css from another origin")]
    public void a_page_the_policy_would_quietly_break_fails(string markup, string finding)
    {
        Page("page.html", markup);
        Assert.Contains(SiteBuild.OutputCheck(_site), f => f.Contains(finding, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("@import url(\"other.css\");")]
    [InlineData("a { background: url(https://example.org/a.png); }")]
    public void a_stylesheet_that_loads_anything_fails(string css)
    {
        File.WriteAllText(Path.Combine(_site, "site.css"), css);
        Assert.Contains(SiteBuild.OutputCheck(_site), static f => f.Contains("site.css imports or references a file", StringComparison.Ordinal));
    }

    [Fact]
    public void a_page_that_links_another_origin_and_aligns_by_attribute_passes()
    {
        Page("page.html", "<a href=\"https://github.com/Hafeok/Varve\">GitHub</a><td align=\"right\">1</td><svg><path fill=\"url(#g)\"/></svg>");
        File.WriteAllText(Path.Combine(_site, "site.css"), "a { fill: url(#g); }");
        Assert.Empty(SiteBuild.OutputCheck(_site));
    }

    private void Page(string name, string body) =>
        File.WriteAllText(Path.Combine(_site, name), "<!doctype html>\n<html><head><title>t</title></head><body>" + body + "</body></html>\n");

    public void Dispose() => Directory.Delete(_site, recursive: true);
}
