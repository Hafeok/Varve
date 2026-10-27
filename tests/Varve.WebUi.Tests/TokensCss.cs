// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Varve.WebUi.Tests;

/// <summary>
/// The generator of <c>docs/ui/tokens.css</c> from <c>docs/ui/tokens.json</c>
/// (ADR 0206 point 2): <c>tokens.json</c> is canonical, and this is the one
/// place its CSS is written. Where the two ever differ, this generator is
/// authoritative.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Each theme is a block of custom properties: light on <c>:root</c>
///   and <c>[data-theme="light"]</c>, dark on <c>[data-theme="dark"]</c>. Each
///   holds every colour token, then every shadow token. A value written
///   <c>{name}</c> is <c>var(--name)</c>, the same in both themes.</item>
///   <item>Spacing, radius and the font families do not vary by theme and
///   are one <c>:root</c> block.</item>
///   <item>Each type style is one class, in the order the groups list
///   them.</item>
/// </list>
/// </remarks>
internal static class TokensCss
{
    internal static string Generate(ReadOnlySpan<byte> tokensJson)
    {
        using JsonDocument document = JsonDocument.Parse(tokensJson.ToArray());
        JsonElement root = document.RootElement;
        JsonElement color = root.GetProperty("color");
        List<string> themes = [];

        foreach (JsonElement theme in color.GetProperty("themes").EnumerateArray())
        {
            themes.Add(theme.GetProperty("id").GetString()!);
        }

        StringBuilder css = new();
        css.Append("/* ").Append(root.GetProperty("name").GetString()).Append(" — generated from tokens.json */\n");

        foreach (string theme in themes)
        {
            css.Append(theme == themes[0] ? ":root, [data-theme=\"" + theme + "\"] {\n" : "[data-theme=\"" + theme + "\"] {\n");
            Properties(css, color.GetProperty("tokens"), theme);
            Properties(css, root.GetProperty("shadow").GetProperty("tokens"), theme);
            css.Append("}\n");
        }

        css.Append(":root {\n");
        Properties(css, root.GetProperty("spacing").GetProperty("tokens"), theme: null);
        Properties(css, root.GetProperty("radius").GetProperty("tokens"), theme: null);
        JsonElement type = root.GetProperty("type");

        foreach (JsonProperty family in type.GetProperty("families").EnumerateObject())
        {
            css.Append("  --font-").Append(family.Name).Append(": ").Append(family.Value.GetString()).Append(";\n");
        }

        css.Append("}\n");

        foreach (JsonElement group in type.GetProperty("groups").EnumerateArray())
        {
            string family = group.GetProperty("family").GetString()!;

            foreach (JsonElement style in group.GetProperty("styles").EnumerateArray())
            {
                css.Append('.').Append(style.GetProperty("name").GetString())
                    .Append(" { font-family: var(--font-").Append(family).Append(");")
                    .Append(" font-size: ").Append(style.GetProperty("fontSize").GetString()).Append(';')
                    .Append(" line-height: ").Append(style.GetProperty("lineHeight").GetString()).Append(';')
                    .Append(" font-weight: ").Append(style.GetProperty("fontWeight").GetInt32().ToString(CultureInfo.InvariantCulture)).Append(';');

                if (style.TryGetProperty("letterSpacing", out JsonElement spacing))
                {
                    css.Append(" letter-spacing: ").Append(spacing.GetString()).Append(';');
                }

                css.Append(" }\n");
            }
        }

        return css.ToString();
    }

    // One custom property per token: its value in this theme, or its one
    // value; a reference {name} is var(--name).
    private static void Properties(StringBuilder css, JsonElement tokens, string? theme)
    {
        foreach (JsonElement token in tokens.EnumerateArray())
        {
            JsonElement value = token.GetProperty("value");
            string text = value.ValueKind == JsonValueKind.Object ? value.GetProperty(theme!).GetString()! : value.GetString()!;

            if (text.StartsWith('{') && text.EndsWith('}'))
            {
                text = "var(--" + text[1..^1] + ")";
            }

            css.Append("  --").Append(token.GetProperty("name").GetString()).Append(": ").Append(text).Append(";\n");
        }
    }
}
