// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The project's status as a checked projection (ADR 0102, docs/releases.md
// §7). Included by eng/status.cs and eng/release.cs (`#:include
// lib/Status.cs`, after lib/Releases.cs); not a script of its own.
//
// README.md ships inside every package (eng/package-metadata.cs), so what it
// says about the project is published with each version, and cannot be
// corrected afterwards. v0.1.0-preview.1 shipped one two milestones stale:
// "Milestone 5c", "Nothing is published yet", 2,803 conformance cases where
// the baseline held 2,927. So four statements are checked against what they
// describe, read from one tree (the working tree, or a commit):
//
//   1. README.md's "## Status" opens with a line naming the latest release
//      (the newest descriptor in releases/, cut or being cut) and the last
//      milestone it closed (its basis issue latest in the roadmap's table).
//   2. The conformance table's total is the line count of the baseline.
//   3. The package table has a row for every packable project, with the
//      layer its project declares (<ArchLayer>, the CompilerVisibleProperty
//      the layer rules read), and no "not built" row names a project that
//      exists.
//   4. A docs/roadmap.md milestone heading says *(complete)* exactly when
//      every issue the roadmap's table gives it is in a release's basis.
//
// "Released" counts every descriptor in releases/, not only the tagged ones:
// at most one is pending, it is the one being cut, and the files must say
// "complete" at the commit that is tagged, not after it.

using System.Text.RegularExpressions;

sealed record Project(string Name, int? Layer, bool Packable);

sealed class StatusTree(Func<string, string?> read, Func<string, IEnumerable<string>> list)
{
    public const string ReadmePath = "README.md";

    public const string RoadmapPath = "docs/roadmap.md";

    public const string PassingPath = "tests/Varve.Conformance.Tests/baseline/passing.txt";

    public string? Read(string path) => read(path);

    // Every file under a directory, recursively, as repository-relative paths.
    public IEnumerable<string> List(string directory) => list(directory);

    public List<Project> Projects()
    {
        List<Project> projects = [];

        foreach (string path in List("src").Where(path => Regex.IsMatch(path, @"^src/[^/]+/[^/]+\.csproj$", RegexOptions.CultureInvariant)).Order(StringComparer.Ordinal))
        {
            string text = Read(path) ?? "";
            Match layer = Regex.Match(text, @"<ArchLayer>\s*(?<layer>\d+)\s*</ArchLayer>", RegexOptions.CultureInvariant);
            bool packable = Regex.IsMatch(text, @"<IsPackable>\s*true\s*</IsPackable>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            projects.Add(new Project(
                Path.GetFileNameWithoutExtension(path),
                layer.Success ? int.Parse(layer.Groups["layer"].Value, System.Globalization.CultureInfo.InvariantCulture) : null,
                packable));
        }

        return projects;
    }

    public List<Descriptor> Releases(string directory, List<string> problems)
    {
        List<Descriptor> descriptors = [];

        foreach (string path in List(directory).Where(path => path.EndsWith(".yaml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            descriptors.Add(ReleaseFormat.Parse(path, Read(path) ?? "", problems));
        }

        return descriptors;
    }
}

static class ProjectStatus
{
    public static void Check(
        StatusTree tree, List<string> problems,
        string readmePath = StatusTree.ReadmePath, string roadmapPath = StatusTree.RoadmapPath,
        string passingPath = StatusTree.PassingPath, string releasesPath = ReleaseFormat.Directory)
    {
        string? readme = tree.Read(readmePath);
        string? roadmap = tree.Read(roadmapPath);
        string? passing = tree.Read(passingPath);

        if (readme is null || roadmap is null || passing is null)
        {
            problems.Add($"one of {readmePath}, {roadmapPath} and {passingPath} is missing");
            return;
        }

        List<string> releaseProblems = [];
        List<Descriptor> releases = tree.Releases(releasesPath, releaseProblems);
        problems.AddRange(releaseProblems);
        List<(string Label, string Id, int Issue)> milestones = MilestoneTable(roadmap);

        if (milestones.Count == 0)
        {
            problems.Add($"{roadmapPath} has no milestone table with issue links");
            return;
        }

        string[] lines = readme.ReplaceLineEndings("\n").Split('\n');
        int status = Array.FindIndex(lines, line => line.Trim() == "## Status");

        if (status < 0)
        {
            problems.Add($"{readmePath} has no '## Status' section");
            return;
        }

        int end = Array.FindIndex(lines, status + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        string[] section = lines[(status + 1)..(end < 0 ? lines.Length : end)];

        CheckOpeningLine(readmePath, section, releases, milestones, problems);
        CheckConformanceTotal(readmePath, section, passingPath, passing, problems);
        CheckPackageTable(readmePath, section, tree.Projects(), problems);
        CheckRoadmap(roadmapPath, roadmap, milestones, releases, problems);
    }

    // 1. The opening line names the latest release and its last milestone.
    private static void CheckOpeningLine(
        string readmePath, string[] section, List<Descriptor> releases,
        List<(string Label, string Id, int Issue)> milestones, List<string> problems)
    {
        Descriptor? latest = releases
            .Where(release => release.Version is not null && ReleaseFormat.Version.IsMatch(release.Version))
            .OrderByDescending(release => release.Version!, Comparer<string>.Create(ReleaseFormat.Compare))
            .FirstOrDefault();

        if (latest is null)
        {
            return;
        }

        string opening = section.FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "";

        if (!opening.Contains(latest.Version!, StringComparison.Ordinal))
        {
            problems.Add($"{readmePath}: the Status section's opening line does not name the latest release, {latest.Version}: '{Clip(opening)}'");
        }

        HashSet<int> closed = [.. latest.Basis.Where(line => line.Kind == "issue").Select(line => int.TryParse(line.Value, out int issue) ? issue : 0)];
        (string Label, string Id, int Issue)? last = milestones.Where(milestone => closed.Contains(milestone.Issue)).OrderBy(milestone => milestone.Issue).Cast<(string, string, int)?>().LastOrDefault();

        if (last is { } milestone
            && !Regex.IsMatch(opening, @"\bmilestones?\b[^.]*?(?<![\w.])" + Regex.Escape(milestone.Id) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            problems.Add($"{readmePath}: the Status section's opening line does not name milestone {milestone.Id} (#{milestone.Issue}), "
                + $"the last one {latest.Version} closed: '{Clip(opening)}'");
        }
    }

    // 2. The conformance total is the baseline's line count.
    private static void CheckConformanceTotal(string readmePath, string[] section, string passingPath, string passing, List<string> problems)
    {
        int baseline = passing.ReplaceLineEndings("\n").Split('\n').Count(line => line.Trim().Length > 0);
        Match total = section.Select(line => Regex.Match(line, @"^\|\s*\*\*Total\*\*\s*\|\s*\*\*(?<count>[\d,]+)", RegexOptions.CultureInvariant)).FirstOrDefault(match => match.Success) ?? Match.Empty;

        if (!total.Success)
        {
            problems.Add($"{readmePath}: the conformance table has no '| **Total** | **N …' row");
            return;
        }

        int stated = int.Parse(total.Groups["count"].Value.Replace(",", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture);

        if (stated != baseline)
        {
            problems.Add($"{readmePath}: the conformance total is {stated:N0}, and {passingPath} holds {baseline:N0} lines");
        }
    }

    // 3. Every packable project has a row with its layer; no "not built" row
    // names a project that exists.
    private static void CheckPackageTable(string readmePath, string[] section, List<Project> projects, List<string> problems)
    {
        int header = Array.FindIndex(section, line => line.StartsWith('|') && line.Contains("Package", StringComparison.Ordinal) && line.Contains("Layer", StringComparison.Ordinal));

        if (header < 0)
        {
            problems.Add($"{readmePath}: the Status section has no package table ('| Package | Layer | …')");
            return;
        }

        List<string[]> rows = [];

        for (int i = header + 2; i < section.Length && section[i].StartsWith('|'); i++)
        {
            rows.Add([.. section[i].Trim().Trim('|').Split('|').Select(cell => cell.Trim())]);
        }

        foreach (Project project in projects.Where(project => project.Packable))
        {
            string[]? row = rows.FirstOrDefault(cells => cells.Length > 0 && cells[0].Contains($"`{project.Name}`", StringComparison.Ordinal));

            if (row is null)
            {
                problems.Add($"{readmePath}: {project.Name}, packable at layer {project.Layer}, has no row in the package table");
            }
            else if (row.Length < 2 || row[1] != project.Layer?.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                problems.Add($"{readmePath}: the package table gives {project.Name} layer '{(row.Length > 1 ? row[1] : "")}', and its project declares {project.Layer}");
            }
        }

        foreach (string[] row in rows.Where(cells => cells.Length > 0 && cells[^1].Contains("not built", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Project project in projects)
            {
                string shortName = project.Name[(project.Name.LastIndexOf('.') + 1)..];

                if (row[0].Contains(project.Name, StringComparison.Ordinal)
                    || Regex.IsMatch(row[0], @"(?<![\w.])" + Regex.Escape(shortName) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    problems.Add($"{readmePath}: the package table says '{row[0]}' is not built, and {project.Name} exists");
                }
            }
        }
    }

    // 4. A milestone heading says *(complete)* exactly when its issues are released.
    private static void CheckRoadmap(
        string roadmapPath, string roadmap, List<(string Label, string Id, int Issue)> milestones,
        List<Descriptor> releases, List<string> problems)
    {
        Dictionary<int, string> releasedBy = [];

        foreach (Descriptor release in releases)
        {
            foreach (BasisLine line in release.Basis.Where(line => line.Kind == "issue"))
            {
                if (int.TryParse(line.Value, out int issue))
                {
                    releasedBy.TryAdd(issue, release.Version ?? release.Path);
                }
            }
        }

        foreach (string heading in roadmap.ReplaceLineEndings("\n").Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal)))
        {
            string id = HeadingId(heading[3..]);
            List<(string Label, string Id, int Issue)> mine = [.. milestones.Where(milestone => milestone.Id == id
                || (milestone.Id.StartsWith(id, StringComparison.Ordinal) && id.Length > 0 && char.IsAsciiDigit(id[^1])
                    && milestone.Id[id.Length..].All(char.IsAsciiLetter)))];

            if (mine.Count == 0)
            {
                continue;
            }

            bool says = heading.Contains("*(complete)*", StringComparison.Ordinal);
            List<(string Label, string Id, int Issue)> open = [.. mine.Where(milestone => !releasedBy.ContainsKey(milestone.Issue))];

            if (open.Count == 0 && !says)
            {
                problems.Add($"{roadmapPath}: '{heading}' must say *(complete)*: "
                    + string.Join(", ", mine.Select(milestone => $"#{milestone.Issue} is released in {releasedBy[milestone.Issue]}")));
            }
            else if (open.Count > 0 && says)
            {
                problems.Add($"{roadmapPath}: '{heading}' says *(complete)*, and "
                    + string.Join(", ", open.Select(milestone => $"#{milestone.Issue}")) + " is in no release's basis");
            }
        }
    }

    // The roadmap's milestone table: label, id (the label before " — "), issue.
    public static List<(string Label, string Id, int Issue)> MilestoneTable(string roadmap)
    {
        List<(string Label, string Id, int Issue)> milestones = [];
        bool inTable = false;

        foreach (string line in roadmap.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!line.StartsWith('|'))
            {
                if (inTable)
                {
                    break;
                }

                continue;
            }

            inTable = true;

            foreach (Match match in Regex.Matches(line, @"\|\s*(?<label>[^|]+?)\s*\|\s*\[#(?<issue>\d+)\]\([^)]*/issues/\k<issue>\)", RegexOptions.CultureInvariant))
            {
                string label = match.Groups["label"].Value;
                milestones.Add((label, HeadingId(label), int.Parse(match.Groups["issue"].Value, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return milestones;
    }

    private static string HeadingId(string text)
    {
        int dash = text.IndexOf(" — ", StringComparison.Ordinal);
        string id = dash < 0 ? text : text[..dash];
        int marker = id.IndexOf(" *(", StringComparison.Ordinal);
        return (marker < 0 ? id : id[..marker]).Trim();
    }

    private static string Clip(string text) => text.Length <= 90 ? text : text[..87] + "...";
}
