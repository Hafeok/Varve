// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Site;

/// <summary>The repository the site is built from, by paths relative to its root, with '/'.</summary>
internal sealed class Repository(string root)
{
    public string Root { get; } = root;

    public string Full(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

    public bool IsFile(string path) => File.Exists(Full(path));

    public bool IsDirectory(string path) => Directory.Exists(Full(path));

    public string Read(string path) => File.ReadAllText(Full(path));

    /// <summary>A path relative to the root, normalised: no '.', no '..', '/' between segments; null when it leaves the repository.</summary>
    public static string? Normalise(string path)
    {
        List<string> segments = [];

        foreach (string segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    public static string DirectoryOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}

/// <summary>A part of the site: a directory of the repository's Markdown, every page directly in it.</summary>
internal sealed record Section(string Title, string Source, string Output, string Summary);

/// <summary>One page the site carries, from a Markdown file of the repository.</summary>
internal sealed record Page(string Source, string Output, Section? Section);

/// <summary>What the site carries (ADR 0247, and the maintainer's answer 4 on #129): which Markdown becomes which page.</summary>
internal sealed class SiteMap
{
    public static readonly IReadOnlyList<Section> Sections =
    [
        new("Operator guide", "docs/operator", "operator", "Running, configuring, securing and upgrading Varve."),
        new("Problem types", "docs/problems", "problems", "Every RFC 9457 problem type Varve answers, and what to do about it."),
        new("Architecture decisions", "docs/adr", "adr", "Every ADR, with its context, decision and consequences."),
        new("Decision sets", "docs/decisions", "decisions", "Each ADR's rulings, as the analyzers read them."),
        new("Specifications", "docs/spec", "spec", "The specifications Varve implements and its own formats."),
        new("Rules", "docs/rules", "rules", "Varve's analyzer rules."),
        new("Formats", "docs/formats", "formats", "Every format Varve writes, its version and its rule."),
    ];

    /// <summary>Single files the site carries outside the sections, and where they go.</summary>
    public static readonly IReadOnlyList<(string Source, string Output, string Title, string Summary)> Files =
    [
        ("docs/vocabulary.md", "vocabulary.html", "Vocabulary", "The varve: vocabulary."),
        ("docs/auth-providers.md", "auth-providers.html", "Authentication providers", "How CI's Entra ID and Google legs are set up."),
    ];

    private readonly Dictionary<string, Page> _bySource = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Page> Pages => _bySource.Values;

    public static SiteMap Of(Repository repository)
    {
        SiteMap map = new();

        foreach (Section section in Sections)
        {
            if (!repository.IsDirectory(section.Source))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(repository.Full(section.Source), "*.md").Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);
                string source = section.Source + "/" + name;
                string output = section.Output + "/" + (name == "README.md" ? "index.html" : Path.ChangeExtension(name, ".html"));
                map._bySource[source] = new Page(source, output, section);
            }
        }

        foreach ((string source, string output, _, _) in Files.Where(f => repository.IsFile(f.Source)))
        {
            map._bySource[source] = new Page(source, output, null);
        }

        return map;
    }

    public Page? PageOf(string source) => _bySource.GetValueOrDefault(source);

    /// <summary>The sections and single files this site carries, in order, and the two generated parts.</summary>
    public IReadOnlyList<(string Title, string Output, string Summary)> Contents()
    {
        List<(string, string, string)> contents = [];

        foreach (Section section in Sections.Where(s => _bySource.ContainsKey(s.Source + "/README.md")))
        {
            contents.Add((section.Title, section.Output + "/index.html", section.Summary));
        }

        foreach ((string source, string output, string title, string summary) in Files.Where(f => _bySource.ContainsKey(f.Source)))
        {
            contents.Add((title, output, summary));
        }

        contents.Add(("API reference", "api/index.html", "Each package's public surface, from its API baselines."));
        contents.Add(("Learning track", "learning/index.html", "Not written yet."));
        return contents;
    }

    /// <summary>The page a repository directory stands for on the site: a section's index.</summary>
    public string? OutputOfDirectory(string directory) =>
        Sections.FirstOrDefault(s => s.Source == directory) is { } section ? section.Output + "/index.html" : null;
}
