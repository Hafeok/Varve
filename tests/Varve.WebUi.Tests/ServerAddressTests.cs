// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The server the Web UI reads (ADR 0202): the configured address when a
/// static host serves the bundle, else the origin that served it.
/// </summary>
public class ServerAddressTests
{
    [Theory]
    [InlineData(null, "https://varve.example/ui/health", "https://varve.example/")]
    [InlineData("", "http://localhost:5080/ui/", "http://localhost:5080/")]
    [InlineData("https://api.example:8443/", "https://ui.example/", "https://api.example:8443/")]
    public void The_address_is_the_configured_one_or_the_origin_that_served_the_bundle(string? configured, string served, string expected) =>
        Assert.Equal(new Uri(expected), ServerAddress.From(configured, new Uri(served)));

    [Theory]
    [InlineData("api.example")]
    [InlineData("ftp://api.example/")]
    public void An_address_that_is_not_absolute_http_or_https_is_refused_with_the_key_named(string configured)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => ServerAddress.From(configured, new Uri("https://ui.example/")));
        Assert.Contains("Varve:Server", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_problem_page_is_linked_at_a_commit_or_main()
    {
        string page = ProblemDocs.Page("not-ready");
        Assert.Matches("^https://github\\.com/Hafeok/Varve/blob/([0-9a-f]{40}|main)/docs/problems/not-ready\\.md$", page);
    }
}
