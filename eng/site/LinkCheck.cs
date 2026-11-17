// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Markdig.Syntax;

namespace Varve.Site;

/// <summary>What a relative link in a page names in the repository.</summary>
internal enum TargetKind
{
    External,
    SamePage,
    File,
    Directory,
    Missing,
}

internal sealed record LinkTarget(TargetKind Kind, string Path, string? Fragment);

internal static class Links
{
    /// <summary>Resolves a link written in <paramref name="source"/> against the repository.</summary>
    public static LinkTarget Resolve(Repository repository, string source, string url)
    {
        if (MarkdownReading.IsExternal(url))
        {
            return new(TargetKind.External, url, null);
        }

        int hash = url.IndexOf('#', StringComparison.Ordinal);
        string path = hash < 0 ? url : url[..hash];
        string? fragment = hash < 0 ? null : url[(hash + 1)..];

        if (path.Length == 0)
        {
            return new(TargetKind.SamePage, source, fragment);
        }

        int query = path.IndexOf('?', StringComparison.Ordinal);

        if (query >= 0)
        {
            path = path[..query];
        }

        string? resolved = Repository.Normalise(Repository.DirectoryOf(source) + "/" + Uri.UnescapeDataString(path));

        if (resolved is null)
        {
            return new(TargetKind.Missing, path, fragment);
        }

        if (repository.IsFile(resolved))
        {
            return new(TargetKind.File, resolved, fragment);
        }

        return repository.IsDirectory(resolved)
            ? new(TargetKind.Directory, resolved, fragment)
            : new(TargetKind.Missing, resolved, fragment);
    }
}

/// <summary>One link the gate exempts, and the decision that allows it.</summary>
internal sealed record Exemption(string Source, string Url, string Reason);

internal sealed class LinkReport
{
    public List<string> Broken { get; } = [];

    public List<string> Exempted { get; } = [];

    public List<string> Stale { get; } = [];

    public int Pages { get; set; }

    public int Checked { get; set; }

    public bool Failed => Broken.Count > 0 || Stale.Count > 0;

    public void Print(TextWriter output, TextWriter error)
    {
        foreach (string line in Broken)
        {
            error.WriteLine($"varve-site: broken link: {line}");
        }

        foreach (string line in Stale)
        {
            error.WriteLine($"varve-site: exemption matches no link: {line}");
        }

        foreach (string line in Exempted)
        {
            output.WriteLine($"varve-site: exempt: {line}");
        }

        output.WriteLine($"varve-site: {Pages} pages, {Checked} relative links, {Broken.Count} broken, {Exempted.Count} exempt.");
    }
}

/// <summary>
/// The link gate (ADR 0247): every relative link in every page the site
/// carries names a file or directory of the repository, and its anchor, if
/// it has one and names Markdown, is a heading or an HTML anchor of that file.
/// </summary>
internal static class LinkCheck
{
    public const string ExemptionsFile = "eng/site/link-exemptions.txt";

    public static LinkReport Run(Repository repository, SiteMap map)
    {
        LinkReport report = new() { Pages = map.Pages.Count };
        Dictionary<string, HashSet<string>> anchors = new(StringComparer.Ordinal);
        List<Exemption> exemptions = ReadExemptions(repository);
        HashSet<Exemption> used = [];

        HashSet<string> AnchorsOf(string path)
        {
            if (!anchors.TryGetValue(path, out HashSet<string>? set))
            {
                string text = repository.Read(path);
                set = MarkdownReading.Anchors(MarkdownReading.Parse(text), text);
                anchors[path] = set;
            }

            return set;
        }

        foreach (Page page in map.Pages.OrderBy(static p => p.Source, StringComparer.Ordinal))
        {
            MarkdownDocument document = MarkdownReading.Parse(repository.Read(page.Source));

            foreach ((Markdig.Syntax.Inlines.LinkInline link, int line) in MarkdownReading.Links(document))
            {
                string url = link.Url!;
                LinkTarget target = Links.Resolve(repository, page.Source, url);

                if (target.Kind == TargetKind.External)
                {
                    continue;
                }

                report.Checked++;
                string? problem = target.Kind switch
                {
                    TargetKind.Missing => $"{target.Path} does not exist",
                    TargetKind.File or TargetKind.SamePage
                        when target.Fragment is { Length: > 0 } fragment
                        && target.Path.EndsWith(".md", StringComparison.Ordinal)
                        && !AnchorsOf(target.Path).Contains(Uri.UnescapeDataString(fragment))
                        => $"{target.Path} has no anchor #{fragment}",
                    _ => null,
                };

                if (problem is null)
                {
                    continue;
                }

                string where = $"{page.Source}:{line} -> {url}: {problem}";

                if (exemptions.FirstOrDefault(e => e.Source == page.Source && e.Url == url) is { } exemption)
                {
                    used.Add(exemption);
                    report.Exempted.Add($"{where} ({exemption.Reason})");
                    continue;
                }

                report.Broken.Add(where);
            }
        }

        foreach (Exemption exemption in exemptions.Where(e => !used.Contains(e)))
        {
            report.Stale.Add($"{ExemptionsFile}: {exemption.Source} {exemption.Url}");
        }

        return report;
    }

    /// <summary>
    /// The exemptions: a line is the page, the link exactly as written, and the
    /// decision that allows it. One that no longer matches a broken link fails,
    /// so an exemption never outlives its reason.
    /// </summary>
    private static List<Exemption> ReadExemptions(Repository repository)
    {
        List<Exemption> exemptions = [];

        if (!repository.IsFile(ExemptionsFile))
        {
            return exemptions;
        }

        foreach (string raw in repository.Read(ExemptionsFile).Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                throw new SiteException($"{ExemptionsFile}: '{line}' needs the page, the link and the decision that allows it.");
            }

            exemptions.Add(new Exemption(parts[0], parts[1], parts[2]));
        }

        return exemptions;
    }
}

internal sealed class SiteException(string message) : Exception(message);
