// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Varve.Site.Tests;

/// <summary>An element of a page, its attributes and its child elements. Text is not kept: the contract is the markup.</summary>
internal sealed class Element(string tag, Dictionary<string, string> attributes, Element? parent)
{
    public string Tag { get; } = tag;

    public Dictionary<string, string> Attributes { get; } = attributes;

    public Element? Parent { get; } = parent;

    public List<Element> Children { get; } = [];

    public string? this[string attribute] => Attributes.GetValueOrDefault(attribute);

    public IReadOnlyList<string> Classes => this["class"] is { } c ? c.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];

    public bool Has(string cls) => Classes.Contains(cls);

    public IEnumerable<Element> Descendants()
    {
        foreach (Element child in Children)
        {
            yield return child;

            foreach (Element descendant in child.Descendants())
            {
                yield return descendant;
            }
        }
    }

    public Element? First(string tag, string? cls = null) =>
        Descendants().FirstOrDefault(e => e.Tag == tag && (cls is null || e.Has(cls)));

    public IEnumerable<Element> All(string tag, string? cls = null) =>
        Descendants().Where(e => e.Tag == tag && (cls is null || e.Has(cls)));

    /// <summary>
    /// The element's name for the contract: its tag, its classes in order
    /// (a state's tone, vw-state--…, is the ledger's answer, not the markup's),
    /// and whether it carries aria-current, aria-label, aria-hidden, role and id.
    /// </summary>
    public string Name()
    {
        StringBuilder name = new(Tag);

        foreach (string cls in Classes.Where(static c => !c.StartsWith("vw-state--", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            name.Append('.').Append(cls);
        }

        foreach (string attribute in (string[])["aria-current", "aria-label", "aria-hidden", "role", "id"])
        {
            if (Attributes.ContainsKey(attribute))
            {
                name.Append('[').Append(attribute).Append(']');
            }
        }

        return name.ToString();
    }

    /// <summary>
    /// The element's shape: its name and its children's shapes. Inline text
    /// formatting (code, strong, em) is the page's text and is skipped, and a
    /// span with no class is text and is not entered.
    /// </summary>
    public string Shape()
    {
        IEnumerable<Element> children = Tag == "span" && Classes.Count == 0 ? [] : Children.Where(static c => c.Tag is not ("code" or "strong" or "em" or "br" or "i"));
        string inner = string.Join(",", children.Select(static c => c.Shape()));
        return inner.Length == 0 ? Name() : Name() + "(" + inner + ")";
    }

    public override string ToString() => Name();
}

/// <summary>
/// Reads a page's elements. The pages under test are the generator's and the
/// design's reference pages, both well formed and with no raw text elements,
/// so a tag scanner is all the contract needs.
/// </summary>
internal static partial class Markup
{
    private static readonly HashSet<string> Void = new(StringComparer.Ordinal) { "meta", "link", "img", "br", "hr", "input", "source", "col", "wbr" };

    public static Element Parse(string html)
    {
        Element document = new("#document", [], null);
        Element current = document;

        foreach (Match tag in Tag().Matches(html))
        {
            string name = tag.Groups["name"].Value.ToLowerInvariant();

            if (tag.Groups["close"].Success)
            {
                for (Element? open = current; open is not null && open.Tag != "#document"; open = open.Parent)
                {
                    if (open.Tag == name)
                    {
                        current = open.Parent!;
                        break;
                    }
                }

                continue;
            }

            Dictionary<string, string> attributes = new(StringComparer.Ordinal);

            foreach (Match attribute in Attribute().Matches(tag.Groups["attributes"].Value))
            {
                attributes[attribute.Groups[1].Value.ToLowerInvariant()] = WebUtility.HtmlDecode(attribute.Groups[2].Success ? attribute.Groups[2].Value : "");
            }

            Element element = new(name, attributes, current);
            current.Children.Add(element);

            if (!Void.Contains(name) && !tag.Groups["self"].Success)
            {
                current = element;
            }
        }

        return document;
    }

    [GeneratedRegex(@"<!--.*?-->|<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attributes>(?:\s+[^\s=>/]+(?:\s*=\s*""[^""]*"")?)*)\s*(?<self>/)?>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex(@"([^\s=>/]+)(?:\s*=\s*""([^""]*)"")?", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();
}
