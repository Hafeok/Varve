// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                Assert.Equal(Expected(root, name, "light"), light[name]);
                Assert.Equal(Expected(root, name, "dark"), dark[name]);
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

    /// <summary>
    /// The output holds values only: no "{name}" reference, and no var()
    /// standing for one (the maintainer's answer 3 on #368). The design's
    /// own preview of this file wrote "{accent}" unresolved, which a browser
    /// drops, and the focus ring with it.
    /// </summary>
    [Fact]
    public void the_site_sets_no_theme_attribute_and_no_token_reference_is_left_unresolved()
    {
        string css = SiteStyles.Tokens(File.ReadAllBytes(Path.Combine(Paths.Root, "docs", "ui", "tokens.json")));
        Assert.DoesNotContain("data-theme", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\{[A-Za-z0-9_-]+\}", css);
        Assert.DoesNotContain("var(", css, StringComparison.Ordinal);
    }

    [Fact]
    public void a_chain_of_references_resolves_to_the_value_in_each_scheme()
    {
        string css = SiteStyles.Tokens(Encoding.UTF8.GetBytes(Fixture("""{ "name": "ink", "value": { "light": "#000000", "dark": "#ffffff" } }, { "name": "accent", "value": "{ink}" }, { "name": "ring", "value": "{accent}" }""")));
        int media = css.IndexOf("@media", StringComparison.Ordinal);
        Assert.Contains("  --ring: #000000;", css[..media], StringComparison.Ordinal);
        Assert.Contains("    --ring: #ffffff;", css[media..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "name": "ring", "value": "{nothing}" }""", "refers to {nothing}, which is no token")]
    [InlineData("""{ "name": "a", "value": "{b}" }, { "name": "b", "value": "{a}" }""", "form a cycle")]
    public void a_reference_that_cannot_resolve_fails_the_build(string tokens, string message)
    {
        SiteException e = Assert.Throws<SiteException>(() => SiteStyles.Tokens(Encoding.UTF8.GetBytes(Fixture(tokens))));
        Assert.Contains(message, e.Message, StringComparison.Ordinal);
    }

    private static string Fixture(string colourTokens) =>
        "{ \"name\": \"Fixture\", \"color\": { \"themes\": [ { \"id\": \"light\" }, { \"id\": \"dark\" } ], \"tokens\": [ " + colourTokens + " ] },"
        + " \"type\": { \"families\": { \"sans\": \"system-ui\" } }, \"spacing\": { \"tokens\": [] }, \"radius\": { \"tokens\": [] }, \"shadow\": { \"tokens\": [] } }";

    /// <summary>A token's value in a scheme, read from tokens.json and followed through its references here, not by the generator.</summary>
    private static string Expected(JsonElement root, string name, string theme)
    {
        for (int hops = 0; hops < 16; hops++)
        {
            string wanted = name;
            JsonElement token = ((string[])["color", "shadow", "spacing", "radius"])
                .SelectMany(group => root.GetProperty(group).GetProperty("tokens").EnumerateArray())
                .Single(t => t.GetProperty("name").GetString() == wanted);
            JsonElement value = token.GetProperty("value");
            string text = value.ValueKind == JsonValueKind.Object ? value.GetProperty(theme).GetString()! : value.GetString()!;

            if (!(text.StartsWith('{') && text.EndsWith('}')))
            {
                return text;
            }

            name = text[1..^1];
        }

        throw new InvalidOperationException("A reference chain longer than 16 in tokens.json.");
    }

    [Fact]
    public void the_foot_names_the_licence_the_build_states()
    {
        Assert.Equal("MPL-2.0", SiteStyles.Licence(new Repository(Paths.Root)));
        Assert.Contains("Mozilla Public License Version 2.0", File.ReadAllText(Path.Combine(Paths.Root, "LICENSE")), StringComparison.Ordinal);
    }

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
