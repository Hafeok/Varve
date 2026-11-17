// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Syntax;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;

namespace Varve.Site;

/// <summary>
/// How the site reads Markdown: CommonMark with the GitHub extensions the docs
/// are written for, and GitHub's heading anchors, so a link that works on
/// GitHub works on the site and the gate judges it the same way.
/// </summary>
internal static partial class MarkdownReading
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .Build();

    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    /// <summary>Every link in a document that is not an autolink, with its 1-based line.</summary>
    public static IEnumerable<(LinkInline Link, int Line)> Links(MarkdownDocument document) =>
        document.Descendants<LinkInline>()
            .Where(static l => !l.IsAutoLink && l.Url is not null)
            .Select(static l => (l, l.Line + 1));

    /// <summary>The anchors a document makes: its headings' identifiers, and any id or name in its raw HTML.</summary>
    public static HashSet<string> Anchors(MarkdownDocument document, string text)
    {
        HashSet<string> anchors = new(StringComparer.Ordinal);

        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            if (heading.GetAttributes().Id is { } id)
            {
                anchors.Add(id);
            }
        }

        foreach (Match match in HtmlAnchor().Matches(text))
        {
            anchors.Add(match.Groups[1].Value);
        }

        return anchors;
    }

    /// <summary>The text of a document's first level-one heading, or null.</summary>
    public static string? Title(MarkdownDocument document)
    {
        HeadingBlock? heading = document.Descendants<HeadingBlock>().FirstOrDefault(static h => h.Level == 1);
        return heading?.Inline is null ? null : PlainText(heading.Inline);
    }

    public static string PlainText(ContainerInline inline)
    {
        System.Text.StringBuilder text = new();

        foreach (Inline child in inline.Descendants())
        {
            switch (child)
            {
                case LiteralInline literal: text.Append(literal.Content.ToString()); break;
                case CodeInline code: text.Append(code.Content); break;
            }
        }

        return text.ToString();
    }

    /// <summary>Whether a URL names another scheme or host, which the site leaves as it is.</summary>
    public static bool IsExternal(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) || Scheme().IsMatch(url);

    [GeneratedRegex("<a\\s[^>]*?(?:id|name)=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlAnchor();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex Scheme();
}
