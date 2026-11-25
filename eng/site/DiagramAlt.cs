// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Varve.Site;

/// <summary>
/// A diagram's <c>alt</c>, built from the labels of its nodes, read from the
/// Mermaid source with no Mermaid (the plan on #368, question 2). It is not a
/// description, but it says what the drawing names.
/// <list type="bullet">
///   <item>A node is an identifier followed by a shape's opening bracket:
///   <c>id[…]</c>, <c>id(…)</c>, <c>id{…}</c>, <c>id((…))</c>,
///   <c>id[(…)]</c>, <c>id[/…/]</c>, <c>id&gt;…]</c> and the rest. Its label is
///   the quoted text, or the text up to the shape's close.</item>
///   <item>Each node counts once, in the order it is first defined. Edge labels
///   (<c>-- text --&gt;</c>, <c>|text|</c>) are not node labels.</item>
///   <item>A sequence diagram's labels are its participants and actors, by
///   their alias when they have one.</item>
///   <item>Line breaks in a label (<c>&lt;br&gt;</c>) become spaces, and Mermaid's
///   <c>#quot;</c>-style entities and HTML entities are decoded.</item>
/// </list>
/// A diagram with no label keeps the text the site always gave it.
/// </summary>
internal static partial class DiagramAlt
{
    public const string NoLabels = "Diagram; its Mermaid source is in this page's source file.";

    public static string Of(string source)
    {
        List<string> labels = Labels(source);
        return labels.Count == 0 ? NoLabels : "Diagram: " + string.Join("; ", labels);
    }

    public static List<string> Labels(string source)
    {
        List<string> labels = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        bool sequence = lines.FirstOrDefault(static l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("%%", StringComparison.Ordinal))?.Trim()
            .StartsWith("sequenceDiagram", StringComparison.Ordinal) == true;

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (sequence)
            {
                if (Participant().Match(line) is { Success: true } p && seen.Add(p.Groups[1].Value))
                {
                    Add(labels, p.Groups[2].Success ? p.Groups[2].Value : p.Groups[1].Value);
                }

                continue;
            }

            foreach ((string id, string label) in Nodes(line))
            {
                if (seen.Add(id))
                {
                    Add(labels, label);
                }
            }
        }

        return labels;
    }

    private static void Add(List<string> labels, string label)
    {
        label = WebUtility.HtmlDecode(MermaidEntity().Replace(label, static m => "&" + m.Groups[1].Value + ";"));
        label = Whitespace().Replace(LineBreak().Replace(label, " "), " ").Trim();

        if (label.Length > 0)
        {
            labels.Add(label);
        }
    }

    /// <summary>The nodes a line of a flowchart defines, with their labels.</summary>
    private static IEnumerable<(string Id, string Label)> Nodes(string line)
    {
        int i = 0;

        while (i < line.Length)
        {
            Match match = NodeStart().Match(line, i);

            if (!match.Success)
            {
                yield break;
            }

            string id = match.Groups[1].Value;
            string open = match.Groups[2].Value;
            int start = match.Index + match.Length;
            string close = Close(open);
            string? label = null;
            int next = start;

            if (start < line.Length && line[start] == '"')
            {
                int quote = line.IndexOf('"', start + 1);

                if (quote > start)
                {
                    label = line[(start + 1)..quote];
                    int end = line.IndexOf(close, quote + 1, StringComparison.Ordinal);
                    next = end < 0 ? quote + 1 : end + close.Length;
                }
            }
            else
            {
                int end = line.IndexOf(close, start, StringComparison.Ordinal);

                if (end > start)
                {
                    label = line[start..end];
                    next = end + close.Length;
                }
            }

            if (label is null || IsKeyword(id))
            {
                i = Math.Max(start, match.Index + 1);
                continue;
            }

            yield return (id, label);
            i = next;
        }
    }

    private static bool IsKeyword(string id) => id is "click" or "style" or "classDef" or "class" or "linkStyle";

    private static string Close(string open) => open switch
    {
        "[[" => "]]",
        "[(" => ")]",
        "[/" or "[\\" => "]",
        "((" => "))",
        "(((" => ")))",
        "([" => "])",
        "{{" => "}}",
        "[" => "]",
        "(" => ")",
        "{" => "}",
        ">" => "]",
        _ => "]",
    };

    // An identifier not preceded by a word character or '-', so the text of an
    // edge label ("-- broken -->") or a CSS value never starts a node.
    [GeneratedRegex(@"(?<![\w\-|])([A-Za-z_][\w]*)\s?(\(\(\(|\[\[|\[\(|\[/|\[\\|\(\(|\(\[|\{\{|\[|\(|\{|>)", RegexOptions.CultureInvariant)]
    private static partial Regex NodeStart();

    [GeneratedRegex(@"^(?:participant|actor)\s+(\S+)(?:\s+as\s+(.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Participant();

    [GeneratedRegex(@"#(\w+);", RegexOptions.CultureInvariant)]
    private static partial Regex MermaidEntity();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
