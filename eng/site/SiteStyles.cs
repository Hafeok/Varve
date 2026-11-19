// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Varve.Site;

/// <summary>
/// The site's stylesheets and mark, all from the design source, docs/ui/
/// (ADR 0247 amended 2026-11-19, ruling SiteStyledFromDesignSource; ADR
/// 0206). The generator embeds no stylesheet of its own, so a change to the
/// design system is on the site at its next build:
/// <list type="bullet">
///   <item><c>site-tokens.css</c> is written from <c>docs/ui/tokens.json</c>:
///   every colour and shadow token on <c>:root</c> with its light value, the
///   dark values under <c>@media (prefers-color-scheme: dark)</c>, then the
///   spacing, radius and font tokens. The site has no script to set
///   <c>data-theme</c>, as the Web UI does, so the reader's system decides
///   the scheme;</item>
///   <item><c>docs/ui/bundle.css</c> and <c>docs/ui/site/site.css</c> are copied
///   byte for byte, and so is the mark, <c>docs/ui/assets/varve-core.svg</c>.</item>
/// </list>
/// </summary>
internal static partial class SiteStyles
{
    /// <summary>What every page links, in this order: the tokens first, then the atoms, then the site's own design.</summary>
    public static readonly IReadOnlyList<string> Stylesheets = ["site-tokens.css", "bundle.css", "site.css"];

    public const string Mark = "varve-core.svg";

    /// <summary>Copied verbatim: the published name, and where it comes from in the repository.</summary>
    public static readonly IReadOnlyList<(string Output, string Source)> Copied =
    [
        ("bundle.css", "docs/ui/bundle.css"),
        ("site.css", "docs/ui/site/site.css"),
        (Mark, "docs/ui/assets/varve-core.svg"),
    ];

    public static void Write(Repository repository, string output)
    {
        if (!repository.IsFile("docs/ui/tokens.json"))
        {
            throw new SiteException("docs/ui/tokens.json is missing: the site's tokens are generated from it.");
        }

        SiteBuild.WriteText(Path.Combine(output, "site-tokens.css"), Tokens(File.ReadAllBytes(repository.Full("docs/ui/tokens.json"))));

        foreach ((string published, string source) in Copied)
        {
            if (!repository.IsFile(source))
            {
                throw new SiteException($"{source} is missing: the site copies it from the design source.");
            }

            File.Copy(repository.Full(source), Path.Combine(output, published), overwrite: true);
        }
    }

    /// <summary>
    /// <c>site-tokens.css</c> from <c>tokens.json</c>. The first theme is the
    /// light one, on <c>:root</c>; the second is the dark one, under the media
    /// query. A value written <c>{name}</c> is <c>var(--name)</c>, in both.
    /// </summary>
    public static string Tokens(ReadOnlySpan<byte> tokensJson)
    {
        using JsonDocument document = JsonDocument.Parse(tokensJson.ToArray());
        JsonElement root = document.RootElement;
        JsonElement color = root.GetProperty("color");
        string[] themes = [.. color.GetProperty("themes").EnumerateArray().Select(static t => t.GetProperty("id").GetString()!)];

        if (themes is not ["light", "dark"])
        {
            throw new SiteException($"tokens.json's themes are [{string.Join(", ", themes)}]; the site expects light, then dark.");
        }

        StringBuilder css = new();
        css.Append("/* ").Append(root.GetProperty("name").GetString())
            .Append(" docs site — generated from docs/ui/tokens.json. Light by default, dark by the reader's preference:\n")
            .Append("   the site has no script, so the scheme is a media query. */\n");
        css.Append(":root {\n");
        Properties(css, color.GetProperty("tokens"), "light", "  ");
        Properties(css, root.GetProperty("shadow").GetProperty("tokens"), "light", "  ");
        css.Append("}\n@media (prefers-color-scheme: dark) {\n  :root {\n");
        Properties(css, color.GetProperty("tokens"), "dark", "    ");
        Properties(css, root.GetProperty("shadow").GetProperty("tokens"), "dark", "    ");
        css.Append("  }\n}\n:root {\n");
        Properties(css, root.GetProperty("spacing").GetProperty("tokens"), theme: null, "  ");
        Properties(css, root.GetProperty("radius").GetProperty("tokens"), theme: null, "  ");

        foreach (JsonProperty family in root.GetProperty("type").GetProperty("families").EnumerateObject())
        {
            css.Append("  --font-").Append(family.Name).Append(": ").Append(family.Value.GetString()).Append(";\n");
        }

        css.Append("}\n");
        return css.ToString();
    }

    private static void Properties(StringBuilder css, JsonElement tokens, string? theme, string indent)
    {
        foreach (JsonElement token in tokens.EnumerateArray())
        {
            JsonElement value = token.GetProperty("value");
            string text = value.ValueKind == JsonValueKind.Object ? value.GetProperty(theme!).GetString()! : value.GetString()!;

            if (text.StartsWith('{') && text.EndsWith('}'))
            {
                text = "var(--" + text[1..^1] + ")";
            }

            css.Append(indent).Append("--").Append(token.GetProperty("name").GetString()).Append(": ").Append(text).Append(";\n");
        }
    }

    /// <summary>
    /// The licence the foot names: the SPDX expression the build already
    /// states for every package (<c>PackageLicenseExpression</c> in
    /// <c>Directory.Build.targets</c>), never typed here.
    /// </summary>
    public static string Licence(Repository repository)
    {
        if (repository.IsFile("Directory.Build.targets") && LicenceExpression().Match(repository.Read("Directory.Build.targets")) is { Success: true } match)
        {
            return match.Groups[1].Value.Trim();
        }

        throw new SiteException("Directory.Build.targets states no PackageLicenseExpression: the foot names the repository's licence from it.");
    }

    [GeneratedRegex("<PackageLicenseExpression>([^<]+)</PackageLicenseExpression>", RegexOptions.CultureInvariant)]
    private static partial Regex LicenceExpression();
}
