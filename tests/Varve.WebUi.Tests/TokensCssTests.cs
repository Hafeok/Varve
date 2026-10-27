// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Text;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// <c>tokens.css</c> is generated from <c>tokens.json</c> and never edited
/// (ADR 0206 points 1 and 2): regenerated here, it equals the committed file
/// byte for byte.
/// </summary>
/// <remarks>
/// After a change to <c>tokens.json</c>, regenerate the file by running this
/// test with <c>VARVE_WRITE_TOKENS_CSS=1</c>: it writes <c>tokens.css</c> and
/// passes, and the diff shows what the change did.
/// </remarks>
public class TokensCssTests
{
    [Fact]
    public void tokens_css_is_generated_from_tokens_json()
    {
        string ui = Path.Combine(RepositoryRoot(), "docs", "ui");
        string generated = TokensCss.Generate(File.ReadAllBytes(Path.Combine(ui, "tokens.json")));
        string path = Path.Combine(ui, "tokens.css");

        if (Environment.GetEnvironmentVariable("VARVE_WRITE_TOKENS_CSS") == "1")
        {
            File.WriteAllText(path, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string committed = File.ReadAllText(path, Encoding.UTF8);
        Assert.True(
            committed == generated,
            "docs/ui/tokens.css is not what tokens.json generates. It is never edited by hand: run this test with "
            + "VARVE_WRITE_TOKENS_CSS=1 to regenerate it (ADR 0206).");
    }

    [Fact]
    public void a_reference_is_a_custom_property_and_a_themed_value_is_per_theme()
    {
        const string Json = """
            {
              "name": "T",
              "color": { "themes": [ { "id": "light" }, { "id": "dark" } ],
                         "tokens": [ { "name": "ink", "value": { "light": "#000", "dark": "#fff" } }, { "name": "ring", "value": "{ink}" } ] },
              "type": { "families": { "sans": "system-ui" },
                        "groups": [ { "family": "sans", "styles": [ { "name": "body", "fontSize": "14px", "lineHeight": "20px", "fontWeight": 400 } ] } ] },
              "spacing": { "tokens": [ { "name": "space-1", "value": "4px" } ] },
              "radius": { "tokens": [] },
              "shadow": { "tokens": [] }
            }
            """;

        Assert.Equal(
            "/* T — generated from tokens.json */\n"
            + ":root, [data-theme=\"light\"] {\n  --ink: #000;\n  --ring: var(--ink);\n}\n"
            + "[data-theme=\"dark\"] {\n  --ink: #fff;\n  --ring: var(--ink);\n}\n"
            + ":root {\n  --space-1: 4px;\n  --font-sans: system-ui;\n}\n"
            + ".body { font-family: var(--font-sans); font-size: 14px; line-height: 20px; font-weight: 400; }\n",
            TokensCss.Generate(Encoding.UTF8.GetBytes(Json)));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }
}
