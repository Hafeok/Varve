// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;

namespace Varve.Site;

/// <summary>Every page's frame, and the site's Content Security Policy (ADR 0247).</summary>
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

    public static string Page(string output, string title, string body, string commit, string? source, IReadOnlyList<(string Title, string Output)> navigation)
    {
        StringBuilder html = new();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(ContentSecurityPolicy).Append("\">\n");
        html.Append("<meta name=\"referrer\" content=\"no-referrer\">\n");
        html.Append("<title>").Append(Encode(title)).Append(" — Varve</title>\n");
        html.Append("<link rel=\"stylesheet\" href=\"").Append(Relative(output, "site.css")).Append("\">\n");
        html.Append("</head>\n<body>\n<header>\n<a class=\"home\" href=\"").Append(Relative(output, "index.html")).Append("\">Varve</a>\n<nav>\n");

        foreach ((string label, string target) in navigation)
        {
            html.Append("<a href=\"").Append(Relative(output, target)).Append("\">").Append(label).Append("</a>\n");
        }

        html.Append("</nav>\n</header>\n<main>\n").Append(body).Append("</main>\n<footer>\n");
        html.Append("Built from <a href=\"").Append(Layout.Repository).Append("/tree/").Append(commit).Append("\">")
            .Append(commit[..12]).Append("</a>");

        if (source is not null)
        {
            html.Append(". This page is <a href=\"").Append(Layout.Repository).Append("/blob/").Append(commit).Append('/')
                .Append(source).Append("\">").Append(Encode(source)).Append("</a>");
        }

        html.Append(".\n</footer>\n</body>\n</html>\n");
        return html.ToString();
    }
}
