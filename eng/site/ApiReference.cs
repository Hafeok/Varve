// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Varve.Site;

/// <summary>One line of a package's public API baseline.</summary>
internal sealed record ApiLine(string Signature, bool Shipped);

/// <summary>A package and its surface, read from its baselines alone.</summary>
internal sealed record ApiPackage(string Name, string Directory, SortedDictionary<string, List<ApiLine>> Types, List<ApiLine> Removed, int Count);

/// <summary>
/// Each package's API reference, generated from its PublicAPI.Shipped.txt and
/// PublicAPI.Unshipped.txt (ADR 0035), which the build already holds to the
/// compiled surface, so the reference cannot drift from it. A type is a line
/// with no member signature; a member belongs to the longest type its
/// signature starts with.
/// </summary>
internal static class ApiReference
{
    private static readonly string[] Modifiers =
        ["abstract ", "override ", "static ", "virtual ", "const ", "readonly ", "sealed ", "new ", "implicit ", "explicit ", "required "];

    public static List<ApiPackage> Read(Repository repository)
    {
        List<ApiPackage> packages = [];

        if (!repository.IsDirectory("src"))
        {
            return packages;
        }

        foreach (string directory in Directory.EnumerateDirectories(repository.Full("src")).Order(StringComparer.Ordinal))
        {
            string shipped = Path.Combine(directory, "PublicAPI.Shipped.txt");
            string unshipped = Path.Combine(directory, "PublicAPI.Unshipped.txt");

            if (!File.Exists(shipped) && !File.Exists(unshipped))
            {
                continue;
            }

            List<ApiLine> lines = [.. Lines(shipped, true), .. Lines(unshipped, false)];
            List<ApiLine> removed = [.. lines.Where(static l => l.Signature.StartsWith("*REMOVED*", StringComparison.Ordinal))
                .Select(static l => l with { Signature = l.Signature["*REMOVED*".Length..] })];
            lines = [.. lines.Where(static l => !l.Signature.StartsWith("*REMOVED*", StringComparison.Ordinal))];

            HashSet<string> typeNames = new(lines.Select(static l => Strip(l.Signature)).Where(IsType), StringComparer.Ordinal);
            SortedDictionary<string, List<ApiLine>> types = new(StringComparer.Ordinal);

            foreach (string type in typeNames)
            {
                types[type] = [];
            }

            foreach (ApiLine line in lines)
            {
                string bare = Strip(line.Signature);

                if (typeNames.Contains(bare))
                {
                    continue;
                }

                string owner = typeNames.Where(t => bare.StartsWith(t + ".", StringComparison.Ordinal)).MaxBy(static t => t.Length) ?? "(other)";

                if (!types.TryGetValue(owner, out List<ApiLine>? members))
                {
                    types[owner] = members = [];
                }

                members.Add(line);
            }

            foreach (List<ApiLine> members in types.Values)
            {
                members.Sort(static (a, b) => string.CompareOrdinal(a.Signature, b.Signature));
            }

            removed.Sort(static (a, b) => string.CompareOrdinal(a.Signature, b.Signature));
            string name = Path.GetFileName(directory);
            packages.Add(new ApiPackage(name, "src/" + name, types, removed, lines.Count));
        }

        return packages;
    }

    public static void Write(Repository repository, string output, string commit, IReadOnlyList<(string Title, string Output)> navigation)
    {
        List<ApiPackage> packages = Read(repository);
        bool anyShipped = packages.Any(static p => p.Types.Values.Any(static m => m.Any(static l => l.Shipped)));

        StringBuilder index = new();
        index.Append("<h1>API reference</h1>\n");
        index.Append("<p>Each package's public surface, generated from its <code>PublicAPI.Shipped.txt</code> and <code>PublicAPI.Unshipped.txt</code> (ADR 0035). The build fails when the compiled surface and those files differ, so this reference cannot drift from what ships.</p>\n");

        if (!anyShipped)
        {
            index.Append("<p class=\"notice\">Before 1.0 no surface is shipped: every <code>PublicAPI.Shipped.txt</code> is empty, and everything below is <em>unshipped</em>, which may change in any preview. The freeze is #129's.</p>\n");
        }

        index.Append("<table>\n<thead><tr><th>Package</th><th>Types</th><th>Lines</th></tr></thead>\n<tbody>\n");

        foreach (ApiPackage package in packages)
        {
            index.Append("<tr><td><a href=\"").Append(package.Name).Append(".html\">").Append(package.Name).Append("</a></td><td>")
                .Append(package.Types.Count).Append("</td><td>").Append(package.Count).Append("</td></tr>\n");
            Directory.CreateDirectory(Path.Combine(output, "api"));
            SiteBuild.WriteText(Path.Combine(output, "api", package.Name + ".html"),
                Layout.Page($"api/{package.Name}.html", package.Name, PackagePage(package), commit, package.Directory + "/PublicAPI.Unshipped.txt", navigation));
        }

        index.Append("</tbody>\n</table>\n");
        SiteBuild.WriteText(Path.Combine(output, "api", "index.html"), Layout.Page("api/index.html", "API reference", index.ToString(), commit, null, navigation));
    }

    private static string PackagePage(ApiPackage package)
    {
        StringBuilder html = new();
        bool anyShipped = package.Types.Values.Any(static m => m.Any(static l => l.Shipped));
        HashSet<string> anchors = new(StringComparer.Ordinal);
        html.Append("<h1>").Append(package.Name).Append("</h1>\n");
        html.Append("<p>").Append(package.Count).Append(" lines in ").Append(package.Types.Count).Append(" types, from <code>")
            .Append(package.Directory).Append("/PublicAPI.Shipped.txt</code> and <code>PublicAPI.Unshipped.txt</code>.")
            .Append(anyShipped ? " A line marked <em>unshipped</em> is not yet part of a released surface." : " None of it is shipped yet: all of it may change in any preview.")
            .Append("</p>\n");

        foreach ((string type, List<ApiLine> members) in package.Types)
        {
            string anchor = Anchor(type);

            for (int n = 1; !anchors.Add(anchor); n++)
            {
                anchor = Anchor(type) + "-" + n;
            }

            html.Append("<h2 id=\"").Append(Layout.Encode(anchor)).Append("\"><code>").Append(Layout.Encode(type)).Append("</code></h2>\n");

            if (members.Count == 0)
            {
                continue;
            }

            html.Append("<ul class=\"api\">\n");

            foreach (ApiLine member in members)
            {
                html.Append("<li><code>").Append(Layout.Encode(member.Signature)).Append("</code>");

                if (anyShipped && !member.Shipped)
                {
                    html.Append(" <em>unshipped</em>");
                }

                html.Append("</li>\n");
            }

            html.Append("</ul>\n");
        }

        if (package.Removed.Count > 0)
        {
            html.Append("<h2 id=\"removed\">Removed since the last shipped surface</h2>\n<ul class=\"api\">\n");

            foreach (ApiLine line in package.Removed)
            {
                html.Append("<li><code>").Append(Layout.Encode(line.Signature)).Append("</code></li>\n");
            }

            html.Append("</ul>\n");
        }

        return html.ToString();
    }

    private static IEnumerable<ApiLine> Lines(string path, bool shipped) =>
        File.Exists(path)
            ? File.ReadAllLines(path).Select(static l => l.Trim()).Where(static l => l.Length > 0 && !l.StartsWith('#')).Select(l => new ApiLine(l, shipped))
            : [];

    private static string Strip(string signature)
    {
        // '~' marks a line whose nullability is oblivious; it is not part of the name.
        signature = signature.TrimStart('~');
        bool stripped = true;

        while (stripped)
        {
            stripped = false;

            foreach (string modifier in Modifiers)
            {
                if (signature.StartsWith(modifier, StringComparison.Ordinal))
                {
                    signature = signature[modifier.Length..];
                    stripped = true;
                }
            }
        }

        return signature;
    }

    private static bool IsType(string signature)
    {
        if (signature.Contains(" -> ", StringComparison.Ordinal) || signature.Contains('(', StringComparison.Ordinal))
        {
            return false;
        }

        // A generic type's parameters are separated by ", ", inside its angle brackets.
        int depth = 0;

        foreach (char c in signature)
        {
            depth += c switch { '<' => 1, '>' => -1, _ => 0 };

            if (c == ' ' && depth == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string Anchor(string type) =>
        new([.. type.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
}
