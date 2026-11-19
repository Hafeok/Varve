// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Xunit;

namespace Varve.Site.Tests;

/// <summary>
/// <c>site-tokens.css</c> is held to <c>docs/ui/tokens.json</c>, value by
/// value: every colour and shadow token on <c>:root</c> with its light value
/// and under <c>prefers-color-scheme: dark</c> with its dark one, and every
/// spacing, radius and font token once. The check reads the CSS back rather
/// than comparing it with what the generator would write, so a generator
/// that drops or crosses a value fails here.
/// </summary>
[Contract(typeof(DocsSite.SiteStyledFromDesignSource), Role = "the check that holds the site's stylesheets to the design source")]
public partial class SiteTokensTests
{
    [Fact]
    public void site_tokens_hold_every_value_of_tokens_json_in_both_schemes()
    {
        byte[] json = File.ReadAllBytes(Path.Combine(Paths.Root, "docs", "ui", "tokens.json"));
        string css = SiteStyles.Tokens(json);

        int media = css.IndexOf("@media (prefers-color-scheme: dark)", StringComparison.Ordinal);
        Assert.True(media > 0, "The dark values are under a prefers-color-scheme media query.");
        int last = css.LastIndexOf(":root {", StringComparison.Ordinal);
        Dictionary<string, string> light = Declarations(css[..media]);
        Dictionary<string, string> dark = Declarations(css[media..last]);
        Dictionary<string, string> fixedTokens = Declarations(css[last..]);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        foreach (string group in (string[])["color", "shadow"])
        {
            foreach (JsonElement token in root.GetProperty(group).GetProperty("tokens").EnumerateArray())
            {
                string name = token.GetProperty("name").GetString()!;
                JsonElement value = token.GetProperty("value");
                Assert.Equal(Css(value.ValueKind == JsonValueKind.Object ? value.GetProperty("light").GetString()! : value.GetString()!), light[name]);
                Assert.Equal(Css(value.ValueKind == JsonValueKind.Object ? value.GetProperty("dark").GetString()! : value.GetString()!), dark[name]);
            }
        }

        foreach (string group in (string[])["spacing", "radius"])
        {
            foreach (JsonElement token in root.GetProperty(group).GetProperty("tokens").EnumerateArray())
            {
                Assert.Equal(token.GetProperty("value").GetString(), fixedTokens[token.GetProperty("name").GetString()!]);
            }
        }

        foreach (JsonProperty family in root.GetProperty("type").GetProperty("families").EnumerateObject())
        {
            Assert.Equal(family.Value.GetString(), fixedTokens["font-" + family.Name]);
        }
    }

    [Fact]
    public void the_site_sets_no_theme_attribute_and_unresolved_references_never_reach_the_css()
    {
        string css = SiteStyles.Tokens(File.ReadAllBytes(Path.Combine(Paths.Root, "docs", "ui", "tokens.json")));
        Assert.DoesNotContain("data-theme", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(@":\s*\{", css);
    }

    [Fact]
    public void the_foot_names_the_licence_the_build_states()
    {
        Assert.Equal("MPL-2.0", SiteStyles.Licence(new Repository(Paths.Root)));
        Assert.Contains("Mozilla Public License Version 2.0", File.ReadAllText(Path.Combine(Paths.Root, "LICENSE")), StringComparison.Ordinal);
    }

    private static string Css(string value) =>
        value.StartsWith('{') && value.EndsWith('}') ? "var(--" + value[1..^1] + ")" : value;

    private static Dictionary<string, string> Declarations(string css)
    {
        Dictionary<string, string> declarations = new(StringComparer.Ordinal);

        foreach (Match match in Declaration().Matches(css))
        {
            Assert.True(declarations.TryAdd(match.Groups[1].Value, match.Groups[2].Value.Trim()), $"--{match.Groups[1].Value} is declared twice in one block.");
        }

        return declarations;
    }

    [GeneratedRegex(@"--([a-z0-9-]+):\s*([^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();
}
