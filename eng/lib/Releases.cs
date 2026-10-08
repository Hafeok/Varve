// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Release descriptors, releases/<version>.yaml (ADR 0102, docs/releases.md).
// Included by eng/release.cs and eng/changelog.cs (`#:include
// lib/Releases.cs`); not a script of its own.
//
// What is here is what both need: reading a descriptor, its shape rules
// (format 1), SemVer precedence, and the projection of releases/ that
// CHANGELOG.md is. What a descriptor's basis resolves to, and when a release
// is cut, is eng/release.cs's alone.
//
// A descriptor is a strict subset of YAML, read by hand rather than by a
// package (hard constraint 4): top-level `key: value` scalars, optionally
// quoted; one list, `basis:`, of `  - kind: value` lines; and `|` block
// scalars. Whole-line `#` comments are allowed between keys. Anything else is
// refused with its line, so a descriptor never means something a reviewer did
// not read.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

sealed record BasisLine(string Kind, string Value, int Line);

sealed record Descriptor(
    string Path,
    int? Format,
    string? Version,
    string? Title,
    string? Date,
    string? Commit,
    IReadOnlyList<BasisLine> Basis,
    string? AcceptedBy,
    string? Summary);

// A version cut before descriptors existed: its CHANGELOG.md section, kept
// verbatim as releases/<version>.md (only v0.1.0-preview.1).
sealed record LegacyNotes(string Path, string Version, string Section);

static class ReleaseFormat
{
    public const string Directory = "releases";

    public const int CurrentFormat = 1;

    // SemVer 2.0.0 with its prerelease, prefixed v, without build metadata:
    // NuGet ignores build metadata, so two versions differing only there would
    // be one package. MinVer reads the tag with MinVerTagPrefix v (ADR 0085).
    public static readonly Regex Version = new(
        @"^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$",
        RegexOptions.CultureInvariant);

    private static readonly string[] Keys = ["format", "version", "title", "date", "commit", "basis", "accepted-by", "summary"];

    private static readonly string[] BasisKinds = ["issue", "adr", "storage-format"];

    public static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal);

    // The heading Keep a Changelog uses: the version without its v.
    public static string Bare(string version) => version.StartsWith('v') ? version[1..] : version;

    // --- reading -----------------------------------------------------------------

    public static Descriptor Parse(string path, string text, List<string> problems)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        Dictionary<string, string> scalars = new(StringComparer.Ordinal);
        List<BasisLine> basis = [];
        Regex topLevel = new(@"^(?<key>[a-z][a-z-]*):(?:[ ]+(?<value>.*?))?[ ]*$", RegexOptions.CultureInvariant);
        Regex basisItem = new(@"^[ ]{2}-[ ](?<kind>[a-z][a-z-]*):[ ]+(?<value>\S.*?)[ ]*$", RegexOptions.CultureInvariant);
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int number = i + 1;

            if (line.Trim().Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Contains('\t', StringComparison.Ordinal))
            {
                problems.Add($"{path}:{number}: a tab; the descriptor is indented with spaces");
                continue;
            }

            Match match = topLevel.Match(line);

            if (!match.Success)
            {
                problems.Add($"{path}:{number}: '{line.Trim()}' is not 'key: value' (docs/releases.md §1)");
                continue;
            }

            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Success ? match.Groups["value"].Value : "";

            if (!Keys.Contains(key, StringComparer.Ordinal))
            {
                problems.Add($"{path}:{number}: '{key}' is not a field of format 1 (docs/releases.md §1)");
                continue;
            }

            if (!seen.Add(key))
            {
                problems.Add($"{path}:{number}: '{key}' appears twice");
                continue;
            }

            if (key == "basis")
            {
                if (value.Length > 0)
                {
                    problems.Add($"{path}:{number}: basis is a list, one '  - kind: value' line each");
                }

                while (i + 1 < lines.Length && (lines[i + 1].StartsWith(' ') || lines[i + 1].Trim().Length == 0))
                {
                    i++;

                    if (lines[i].Trim().Length == 0)
                    {
                        continue;
                    }

                    Match item = basisItem.Match(lines[i]);

                    if (!item.Success)
                    {
                        problems.Add($"{path}:{i + 1}: '{lines[i].Trim()}' is not '  - kind: value'");
                        continue;
                    }

                    basis.Add(new BasisLine(item.Groups["kind"].Value, Unquote(item.Groups["value"].Value), i + 1));
                }

                continue;
            }

            if (value == "|")
            {
                StringBuilder block = new();
                int indent = -1;

                while (i + 1 < lines.Length && (lines[i + 1].StartsWith(' ') || lines[i + 1].Trim().Length == 0))
                {
                    i++;
                    string blockLine = lines[i];

                    if (blockLine.Trim().Length == 0)
                    {
                        block.Append('\n');
                        continue;
                    }

                    if (indent < 0)
                    {
                        indent = blockLine.Length - blockLine.TrimStart(' ').Length;
                    }

                    if (blockLine.Length - blockLine.TrimStart(' ').Length < indent)
                    {
                        problems.Add($"{path}:{i + 1}: less indented than the block it is in");
                        continue;
                    }

                    block.Append(blockLine[indent..].TrimEnd(' ')).Append('\n');
                }

                scalars[key] = block.ToString().Trim('\n');
                continue;
            }

            if (value.StartsWith('|') || value.StartsWith('>') || value.StartsWith('[') || value.StartsWith('{') || value.StartsWith('&') || value.StartsWith('*'))
            {
                problems.Add($"{path}:{number}: '{value}' is YAML this format does not read; a block is '|', a list is basis's alone");
                continue;
            }

            scalars[key] = Unquote(value);
        }

        int? format = null;

        if (scalars.TryGetValue("format", out string? formatText))
        {
            if (int.TryParse(formatText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                format = parsed;
            }
            else
            {
                problems.Add($"{path}: format '{formatText}' is not a number");
            }
        }

        return new Descriptor(
            path,
            format,
            scalars.GetValueOrDefault("version"),
            scalars.GetValueOrDefault("title"),
            scalars.GetValueOrDefault("date"),
            scalars.GetValueOrDefault("commit"),
            basis,
            scalars.GetValueOrDefault("accepted-by"),
            scalars.GetValueOrDefault("summary"));
    }

    // Double quotes take \" and \\; single quotes take ''. Nothing else is
    // interpreted.
    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return value;
    }

    // --- the shape rules (docs/releases.md §2) ----------------------------------------

    // What can be said of a descriptor from its own text and its file name.
    // What its basis resolves to needs the repository, and is eng/release.cs's.
    public static void CheckShape(Descriptor descriptor, List<string> problems)
    {
        string path = descriptor.Path;
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);

        if (descriptor.Format is null)
        {
            problems.Add($"{path}: no format; a descriptor declares the format it conforms to");
        }
        else if (descriptor.Format != CurrentFormat)
        {
            problems.Add($"{path}: format {descriptor.Format} is not one this validator knows (it knows {CurrentFormat})");
            return;
        }

        if (descriptor.Version is null)
        {
            problems.Add($"{path}: no version");
        }
        else
        {
            if (!Version.IsMatch(descriptor.Version))
            {
                problems.Add($"{path}: version '{descriptor.Version}' is not v<SemVer 2.0.0>, prerelease allowed, build metadata not (ADR 0035)");
            }

            if (descriptor.Version != stem)
            {
                problems.Add($"{path}: version '{descriptor.Version}' is not the file's name, '{stem}'");
            }
        }

        if (string.IsNullOrWhiteSpace(descriptor.Title))
        {
            problems.Add($"{path}: no title");
        }
        else if (descriptor.Title.Contains('\n', StringComparison.Ordinal))
        {
            problems.Add($"{path}: the title is one line");
        }
        else if (Regex.IsMatch(descriptor.Title, @"^v?\d+\.\d+", RegexOptions.CultureInvariant)
            || (descriptor.Version is not null && descriptor.Title.Contains(Bare(descriptor.Version), StringComparison.Ordinal)))
        {
            problems.Add($"{path}: the title carries a version; the tag message is '<version> — <title>', so it would be doubled");
        }

        if (descriptor.Date is null)
        {
            problems.Add($"{path}: no date; it is the preparation date, and CHANGELOG.md's heading");
        }
        else if (!DateOnly.TryParseExact(descriptor.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            problems.Add($"{path}: date '{descriptor.Date}' is not yyyy-mm-dd");
        }

        if (descriptor.Commit is not null && !Regex.IsMatch(descriptor.Commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
        {
            problems.Add($"{path}: commit '{descriptor.Commit}' is not a full, lower-case SHA; a retro-cut names exactly one commit");
        }

        if (descriptor.Basis.Count == 0)
        {
            problems.Add($"{path}: no basis; a release names the milestone issue, the ADRs it first ships and the storage format");
        }

        foreach (BasisLine line in descriptor.Basis)
        {
            if (!BasisKinds.Contains(line.Kind, StringComparer.Ordinal))
            {
                problems.Add($"{path}:{line.Line}: '{line.Kind}' is not a kind of basis line; they are {string.Join(", ", BasisKinds)}");
            }
            else if (line.Kind == "adr" && !Regex.IsMatch(line.Value, @"^\d{4}$", RegexOptions.CultureInvariant))
            {
                problems.Add($"{path}:{line.Line}: adr '{line.Value}' is not a four-digit ADR number");
            }
            else if (line.Kind is "issue" or "storage-format" && !Regex.IsMatch(line.Value, @"^[1-9]\d*$", RegexOptions.CultureInvariant))
            {
                problems.Add($"{path}:{line.Line}: {line.Kind} '{line.Value}' is not a positive number");
            }
        }

        foreach (string single in (string[])["issue", "storage-format"])
        {
            int count = descriptor.Basis.Count(line => line.Kind == single);

            if (count != 1)
            {
                problems.Add($"{path}: basis has {count} '{single}' line(s); it has exactly one");
            }
        }

        foreach (IGrouping<string, BasisLine> duplicate in descriptor.Basis.Where(line => line.Kind == "adr").GroupBy(line => line.Value).Where(group => group.Count() > 1))
        {
            problems.Add($"{path}: adr {duplicate.Key} is listed {duplicate.Count()} times");
        }

        if (descriptor.AcceptedBy is null)
        {
            problems.Add($"{path}: no accepted-by");
        }
        else if (!Regex.IsMatch(descriptor.AcceptedBy, @"^mailto:[^@\s]+@[^@\s]+$", RegexOptions.CultureInvariant))
        {
            problems.Add($"{path}: accepted-by '{descriptor.AcceptedBy}' is not mailto:<email>");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Summary))
        {
            problems.Add($"{path}: no summary; the release notes are written and reviewed here");
        }
    }

    // --- reading releases/ ----------------------------------------------------------------

    public static (List<Descriptor> Descriptors, List<LegacyNotes> Legacy) ReadDirectory(string directory, List<string> problems)
    {
        List<Descriptor> descriptors = [];
        List<LegacyNotes> legacy = [];

        if (!System.IO.Directory.Exists(directory))
        {
            return (descriptors, legacy);
        }

        foreach (string file in System.IO.Directory.GetFiles(directory).Order(StringComparer.Ordinal))
        {
            string name = System.IO.Path.GetFileName(file);

            if (name.EndsWith(".yaml", StringComparison.Ordinal))
            {
                descriptors.Add(Parse(file, File.ReadAllText(file), problems));
            }
            else if (name.EndsWith(".md", StringComparison.Ordinal) && name != "README.md")
            {
                string version = System.IO.Path.GetFileNameWithoutExtension(file);

                if (!Version.IsMatch(version))
                {
                    problems.Add($"{file}: '{version}' is not a version; releases/ holds <version>.yaml, and <version>.md for a release cut before descriptors");
                    continue;
                }

                legacy.Add(new LegacyNotes(file, version, File.ReadAllText(file).ReplaceLineEndings("\n").Trim('\n')));
            }
            else if (name != "README.md")
            {
                problems.Add($"{file}: releases/ holds <version>.yaml descriptors and nothing else (a .yml is refused, so that one spelling exists)");
            }
        }

        foreach (IGrouping<string, string> twice in descriptors.Select(d => d.Version ?? "").Concat(legacy.Select(l => l.Version)).Where(v => v.Length > 0).GroupBy(v => v).Where(g => g.Count() > 1))
        {
            problems.Add($"{directory}: {twice.Key} is described twice");
        }

        return (descriptors, legacy);
    }

    // --- SemVer precedence (semver.org §11) ----------------------------------------------

    public static int Compare(string left, string right)
    {
        Match a = Version.Match(left);
        Match b = Version.Match(right);

        if (!a.Success || !b.Success)
        {
            return string.CompareOrdinal(left, right);
        }

        foreach (string part in (string[])["major", "minor", "patch"])
        {
            int compared = System.Numerics.BigInteger.Parse(a.Groups[part].Value, CultureInfo.InvariantCulture)
                .CompareTo(System.Numerics.BigInteger.Parse(b.Groups[part].Value, CultureInfo.InvariantCulture));

            if (compared != 0)
            {
                return compared;
            }
        }

        bool aPre = a.Groups["pre"].Success;
        bool bPre = b.Groups["pre"].Success;

        if (!aPre || !bPre)
        {
            return aPre == bPre ? 0 : aPre ? -1 : 1;
        }

        string[] x = a.Groups["pre"].Value.Split('.');
        string[] y = b.Groups["pre"].Value.Split('.');

        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xNumber = x[i].All(char.IsAsciiDigit);
            bool yNumber = y[i].All(char.IsAsciiDigit);
            int compared = xNumber && yNumber
                ? System.Numerics.BigInteger.Parse(x[i], CultureInfo.InvariantCulture).CompareTo(System.Numerics.BigInteger.Parse(y[i], CultureInfo.InvariantCulture))
                : xNumber ? -1 : yNumber ? 1 : string.CompareOrdinal(x[i], y[i]);

            if (compared != 0)
            {
                return compared;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    // --- the release's texts -------------------------------------------------------------

    public static string TagMessage(Descriptor descriptor) => $"{descriptor.Version} — {descriptor.Title}";

    // The GitHub Release's notes: the summary, and the diff from the version
    // before it when there is one.
    public static string Notes(Descriptor descriptor, string? previous, string projectUrl)
    {
        StringBuilder notes = new();
        notes.Append(descriptor.Summary!.Trim('\n')).Append('\n');

        if (previous is not null)
        {
            notes.Append(CultureInfo.InvariantCulture, $"\n**Full diff:** [{previous}...{descriptor.Version}]({projectUrl}/compare/{previous}...{descriptor.Version})\n");
        }

        return notes.ToString();
    }

    // --- CHANGELOG.md, the projection of releases/ --------------------------------------

    // Every version, newest first by precedence: a descriptor's section is its
    // date, title and summary; a legacy version's is its notes file, verbatim.
    // Nothing else is in the file, so it can be checked byte for byte.
    public static string RenderChangelog(IReadOnlyList<Descriptor> descriptors, IReadOnlyList<LegacyNotes> legacy, string projectUrl)
    {
        List<(string Version, string Section)> sections = [];

        foreach (Descriptor descriptor in descriptors)
        {
            StringBuilder section = new();
            section.Append(CultureInfo.InvariantCulture, $"## [{Bare(descriptor.Version!)}] - {descriptor.Date}\n");
            section.Append('\n');
            section.Append(CultureInfo.InvariantCulture, $"**{descriptor.Title}**\n");
            section.Append('\n');
            section.Append(descriptor.Summary!.Trim('\n'));
            sections.Add((descriptor.Version!, section.ToString()));
        }

        foreach (LegacyNotes notes in legacy)
        {
            sections.Add((notes.Version, notes.Section));
        }

        sections.Sort((left, right) => Compare(right.Version, left.Version));

        StringBuilder builder = new();
        builder.Append("# Changelog\n");
        builder.Append('\n');
        builder.Append("Projected from `releases/` by `dotnet run eng/changelog.cs`. Do not edit by\n");
        builder.Append("hand: a version's notes are written in its descriptor, `releases/<version>.yaml`,\n");
        builder.Append("and reviewed in the pull request that proposes it\n");
        builder.Append("([ADR 0102](docs/adr/0102-a-release-is-a-descriptor.md), `docs/releases.md`).\n");
        builder.Append("What is not yet released is `dotnet run eng/changelog.cs -- --unreleased`.\n");
        builder.Append('\n');
        builder.Append("The format is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and\n");
        builder.Append("this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)\n");
        builder.Append("([ADR 0035](docs/adr/0035-semantic-versioning.md)).\n");
        builder.Append('\n');
        builder.Append("## [Unreleased]\n");

        foreach ((_, string section) in sections)
        {
            builder.Append('\n');
            builder.Append(section).Append('\n');
        }

        builder.Append('\n');

        if (sections.Count == 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"[Unreleased]: {projectUrl}/commits/main\n");
        }
        else
        {
            builder.Append(CultureInfo.InvariantCulture, $"[Unreleased]: {projectUrl}/compare/{sections[0].Version}...HEAD\n");

            for (int i = 0; i < sections.Count; i++)
            {
                builder.Append(i + 1 < sections.Count
                    ? $"[{Bare(sections[i].Version)}]: {projectUrl}/compare/{sections[i + 1].Version}...{sections[i].Version}\n"
                    : $"[{Bare(sections[i].Version)}]: {projectUrl}/releases/tag/{sections[i].Version}\n");
            }
        }

        return builder.ToString();
    }

    // PackageProjectUrl in Directory.Build.targets: the one place the
    // repository's location is written (ADR 0029).
    public static string? ReadProjectUrl(string root)
    {
        string path = System.IO.Path.Combine(root, "Directory.Build.targets");

        if (!File.Exists(path))
        {
            return null;
        }

        Match match = Regex.Match(File.ReadAllText(path), @"<PackageProjectUrl>\s*(?<url>[^<\s]+)\s*</PackageProjectUrl>", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["url"].Value.TrimEnd('/') : null;
    }
}
