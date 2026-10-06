// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// CHANGELOG.md, from the conventional commits.
//
//   dotnet run eng/changelog.cs
//   dotnet run eng/changelog.cs -- --release <version> [--date <yyyy-mm-dd>]
//   dotnet run eng/changelog.cs -- --check
//   dotnet run eng/changelog.cs -- --output -
//
// Keep a Changelog (keepachangelog.com/1.1.0) over the commit history, because
// the commits already say what changed and a hand-written changelog is a second
// place for the same facts to be wrong in.
//
// --- releases (ADR 0085) --------------------------------------------------
//
// `--release <version>` cuts what is under [Unreleased] into
// "## [<version>] - <date>", leaves [Unreleased] empty, and writes the compare
// links at the end. The commit that carries the cut is the release commit, and
// the tag v<version> goes on it; publish.yml attaches the section as the
// GitHub release's notes.
//
// A released section is then fixed: every later run copies the released
// sections from CHANGELOG.md as they are, because what a version contained
// does not change once it is on nuget.org. Only [Unreleased] is regenerated,
// from the commits after the newest release — after its tag once the tag
// exists, and before that after the commit that cut its section.
//
// `--check` checks the newest v* tag reachable from HEAD: CHANGELOG.md has a
// non-empty section for its version, both here and in the tagged commit, which
// is the file publish.yml reads. No tag is nothing to check. It no longer
// compares a regenerated file with the committed one: the commit that writes
// CHANGELOG.md is itself a commit the next run lists, so that comparison could
// never pass once committed, and nothing ran it.
//
// --- sections -------------------------------------------------------------
//
// Keep a Changelog groups by release, and since ADR 0085 a release closes a
// milestone. Inside a release, and under [Unreleased], the subdivision is the
// milestone. The first release holds every milestone before it, because none
// of them was released on its own (ADR 0029). eng/changelog-sections.txt names
// the first commit of each, and the rest is derived — a commit belongs to the
// last section that started at or before it, in the history's own order.
//
// It could have been derived from the merge commits instead, and for 3a, the
// 3a close-out and 3b that would work. It would not work for milestones 1 and
// 2, which predate the pull-request rule and are a single unbroken run of
// commits on main, so the boundary between them exists only in the intent.
// A file naming five commits is less clever and does not have that hole.
//
// --- the type mapping -----------------------------------------------------
//
//   feat                                        -> Added
//   fix                                         -> Fixed
//   perf refactor build ci chore docs test bench -> Changed
//
// Keep a Changelog's six sections are Added, Changed, Deprecated, Removed,
// Fixed and Security, and the mapping stays inside them rather than inventing
// a seventh for documentation. A commit marked breaking — "feat!:" or a
// BREAKING CHANGE: trailer — is called out wherever it lands, because under
// ADR 0035 that is the fact a reader most needs.
//
// Exit codes: 0 written or up to date, 1 --check found it stale, 2 could not run.
//
// See docs/adr/0035-semantic-versioning.md.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();

string changelogPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
string outputPath = changelogPath;
string sectionsPath = Path.Combine(repositoryRoot, "eng", "changelog-sections.txt");
bool check = false;
bool toStandardOut = false;
string? releaseVersion = null;
string? releaseDate = null;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--check")
    {
        check = true;
    }
    else if (arguments[i] is "--release" && i + 1 < arguments.Length)
    {
        releaseVersion = arguments[++i];
    }
    else if (arguments[i] is "--date" && i + 1 < arguments.Length)
    {
        releaseDate = arguments[++i];
    }
    else if (arguments[i] is "--output" && i + 1 < arguments.Length)
    {
        string value = arguments[++i];

        if (value == "-")
        {
            toStandardOut = true;
        }
        else
        {
            outputPath = value;
        }
    }
}

if (check)
{
    return CheckNewestTag(repositoryRoot, changelogPath);
}

// SemVer 2.0.0 (ADR 0035), without build metadata: NuGet ignores it, so two
// versions differing only there would be one package.
Regex semanticVersion = new(
    @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-(0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?$",
    RegexOptions.CultureInvariant);

if (releaseVersion is not null)
{
    if (releaseVersion.StartsWith('v'))
    {
        Console.Error.WriteLine($"changelog: '{releaseVersion}' is a tag; --release takes the version, without the v.");
        return 2;
    }

    if (!semanticVersion.IsMatch(releaseVersion))
    {
        Console.Error.WriteLine($"changelog: '{releaseVersion}' is not a SemVer 2.0.0 version (ADR 0035).");
        return 2;
    }

    releaseDate ??= DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    if (!DateOnly.TryParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
    {
        Console.Error.WriteLine($"changelog: '{releaseDate}' is not a date in the form yyyy-mm-dd.");
        return 2;
    }
}

string? projectUrl = ReadProjectUrl(repositoryRoot);

if (projectUrl is null)
{
    Console.Error.WriteLine("changelog: no PackageProjectUrl in Directory.Build.targets, which the compare links are built from (ADR 0029).");
    return 2;
}

if (!File.Exists(sectionsPath))
{
    Console.Error.WriteLine($"changelog: no section map at '{Relative(repositoryRoot, sectionsPath)}'.");
    return 2;
}

// --- the section map -------------------------------------------------------

List<(string Sha, string Title)> sections = [];

foreach (string line in File.ReadAllLines(sectionsPath))
{
    string trimmed = line.Trim();

    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
    {
        continue;
    }

    int space = trimmed.IndexOf(' ');

    if (space < 0)
    {
        Console.Error.WriteLine($"changelog: '{trimmed}' is not '<commit-ish> <title>'.");
        return 2;
    }

    string reference = trimmed[..space];
    (int resolved, string sha, string error) = Git(repositoryRoot, "rev-parse", "--verify", reference + "^{commit}");

    if (resolved != 0)
    {
        Console.Error.WriteLine($"changelog: '{reference}' does not resolve: {error.Trim()}");
        return 2;
    }

    sections.Add((sha.Trim(), trimmed[(space + 1)..].Trim()));
}

if (sections.Count == 0)
{
    Console.Error.WriteLine("changelog: the section map is empty.");
    return 2;
}

// --- the commits, oldest first ---------------------------------------------

(int logExit, string log, string logError) = Git(repositoryRoot, "log", "--reverse", "--format=%x01%H%x02%P%x02%s%x02%b");

if (logExit != 0)
{
    Console.Error.WriteLine($"changelog: could not read the history: {logError.Trim()}");
    return 2;
}

Regex conventional = new(
    @"^(?<type>[a-z]+)(?:\((?<scope>[^)]*)\))?(?<breaking>!)?:\s*(?<description>.+)$",
    RegexOptions.CultureInvariant);

Dictionary<string, string> heading = new(StringComparer.Ordinal)
{
    ["feat"] = "Added",
    ["fix"] = "Fixed",
};

List<Entry> entries = [];

foreach (string record in log.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
{
    string[] parts = record.Split('\u0002');

    if (parts.Length < 3)
    {
        continue;
    }

    string sha = parts[0].Trim();
    int parents = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    string subject = parts[2].Trim();
    string body = parts.Length > 3 ? parts[3] : string.Empty;

    // A merge's subject describes the merge, and the commits it brings are in
    // this list already.
    if (parents > 1)
    {
        continue;
    }

    Match match = conventional.Match(subject);

    if (!match.Success)
    {
        // Not a conventional commit. Everything since the repository skeleton
        // is one, so this is a pre-convention commit rather than a mistake to
        // fail over; it goes under Changed with its subject as written.
        entries.Add(new Entry(sha, "chore", null, false, subject));
        continue;
    }

    bool breaking = match.Groups["breaking"].Success
        || body.Contains("BREAKING CHANGE:", StringComparison.Ordinal);

    entries.Add(new Entry(
        sha,
        match.Groups["type"].Value,
        match.Groups["scope"].Success ? match.Groups["scope"].Value : null,
        breaking,
        match.Groups["description"].Value.Trim()));
}

// --- partition by section --------------------------------------------------

Dictionary<string, int> sectionOfSha = new(StringComparer.Ordinal);

for (int i = 0; i < sections.Count; i++)
{
    sectionOfSha[sections[i].Sha] = i;
}

Dictionary<string, int> sectionOfEntry = new(StringComparer.Ordinal);
int current = -1;

foreach (Entry entry in entries)
{
    if (sectionOfSha.TryGetValue(entry.Sha, out int starts))
    {
        current = starts;
    }

    if (current >= 0)
    {
        sectionOfEntry[entry.Sha] = current;
    }
}

if (current < 0)
{
    Console.Error.WriteLine("changelog: no commit in the history starts a section; the map does not match this branch.");
    return 2;
}

// --- what is released -------------------------------------------------------

// The released sections, copied as they are, and their versions, newest first.
string existing = File.Exists(changelogPath)
    ? File.ReadAllText(changelogPath).ReplaceLineEndings("\n")
    : string.Empty;

(List<string> released, string frozen) = ReadReleased(existing);

if (releaseVersion is not null)
{
    if (released.Contains(releaseVersion, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"changelog: CHANGELOG.md already has a section for {releaseVersion}.");
        return 2;
    }

    if (Exists(repositoryRoot, "refs/tags/v" + releaseVersion))
    {
        Console.Error.WriteLine($"changelog: the tag v{releaseVersion} exists already, and a version cannot be released twice.");
        return 2;
    }
}

// The commits under [Unreleased]: all of them before the first release, and
// after it everything past the newest release's boundary.
HashSet<string>? unreleasedShas = null;

if (released.Count > 0)
{
    string newest = released[0];
    string? boundary = null;

    if (Exists(repositoryRoot, "refs/tags/v" + newest))
    {
        boundary = "v" + newest;
    }
    else
    {
        // Pending: cut, not yet tagged. The boundary is the commit that cut it.
        (int cutExit, string cuts, _) = Git(repositoryRoot, "log", "--format=%H", "--reverse", "-S", $"## [{newest}]", "--", "CHANGELOG.md");
        string? cut = cutExit == 0 ? cuts.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;

        if (cut is not null)
        {
            boundary = cut.Trim();
        }
        else
        {
            Console.WriteLine($"note {newest} is cut but not committed; nothing is unreleased after it");
        }
    }

    unreleasedShas = new(StringComparer.Ordinal);

    if (boundary is not null)
    {
        (int listExit, string listed, string listError) = Git(repositoryRoot, "rev-list", $"{boundary}..HEAD");

        if (listExit != 0)
        {
            Console.Error.WriteLine($"changelog: could not list the commits after {boundary}: {listError.Trim()}");
            return 2;
        }

        foreach (string sha in listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            unreleasedShas.Add(sha);
        }
    }
}

List<Entry> unreleased = [.. entries.Where(entry => sectionOfEntry.ContainsKey(entry.Sha) && (unreleasedShas is null || unreleasedShas.Contains(entry.Sha)))];

if (releaseVersion is not null)
{
    if (unreleased.Count == 0)
    {
        Console.Error.WriteLine($"changelog: nothing is unreleased, so there is nothing to cut into {releaseVersion}.");
        return 2;
    }

    StringBuilder cut = new();
    cut.Append(CultureInfo.InvariantCulture, $"## [{releaseVersion}] - {releaseDate}\n");
    RenderMilestones(cut, unreleased, sections, sectionOfEntry, heading);

    frozen = frozen.Length == 0 ? cut.ToString().TrimEnd('\n') : cut.ToString() + "\n" + frozen;
    released.Insert(0, releaseVersion);
    unreleased = [];
}

// --- render ----------------------------------------------------------------

StringBuilder builder = new();

builder.AppendLine("# Changelog");
builder.AppendLine();
builder.AppendLine("Generated from the conventional commits by `dotnet run eng/changelog.cs`.");
builder.AppendLine("Do not edit by hand — the commits are the record and this is a view of them.");
builder.AppendLine();
builder.AppendLine("The format is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and");
builder.AppendLine("this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)");
builder.AppendLine("([ADR 0035](docs/adr/0035-semantic-versioning.md)).");
builder.AppendLine();
builder.AppendLine("## [Unreleased]");

if (released.Count == 0)
{
    builder.AppendLine();
    builder.AppendLine("**Nothing has been released.** No `v*` tag exists and no package has been");
    builder.AppendLine("published to nuget.org, so every change below is unreleased and the sections");
    builder.AppendLine("are milestones rather than versions. The first tag is `v0.1.0-preview.1`");
    builder.AppendLine("([ADR 0029](docs/adr/0029-publishing-and-versioning.md)).");
}

RenderMilestones(builder, unreleased, sections, sectionOfEntry, heading);

if (frozen.Length > 0)
{
    builder.AppendLine();
    builder.AppendLine(frozen);
}

// The compare links, newest first: [Unreleased] against the newest tag, each
// version against the one before it, and the first against nothing.
if (released.Count > 0)
{
    builder.AppendLine();
    builder.AppendLine($"[Unreleased]: {projectUrl}/compare/v{released[0]}...HEAD");

    for (int i = 0; i < released.Count; i++)
    {
        builder.AppendLine(i + 1 < released.Count
            ? $"[{released[i]}]: {projectUrl}/compare/v{released[i + 1]}...v{released[i]}"
            : $"[{released[i]}]: {projectUrl}/releases/tag/v{released[i]}");
    }
}

builder.AppendLine();

string rendered = builder.ToString().ReplaceLineEndings("\n");

if (toStandardOut)
{
    Console.Write(rendered);
    return 0;
}

File.WriteAllText(outputPath, rendered);

if (releaseVersion is not null)
{
    Console.WriteLine($"ok  cut [Unreleased] into [{releaseVersion}] - {releaseDate} in {Relative(repositoryRoot, outputPath)}");
    Console.WriteLine($"    next: commit it as \"chore(release): {releaseVersion}\", tag that commit v{releaseVersion}, and push the tag (ADR 0085)");
}
else
{
    Console.WriteLine($"ok  wrote {Relative(repositoryRoot, outputPath)}, {unreleased.Count} unreleased commit(s), {released.Count} release(s)");
}

return 0;

// --- helpers ---------------------------------------------------------------

static void RenderMilestones(
    StringBuilder builder,
    List<Entry> entries,
    List<(string Sha, string Title)> sections,
    Dictionary<string, int> sectionOfEntry,
    Dictionary<string, string> heading)
{
    // Newest milestone first, which is the order a reader wants.
    for (int i = sections.Count - 1; i >= 0; i--)
    {
        List<Entry> inSection = [.. entries.Where(entry => sectionOfEntry[entry.Sha] == i)];

        if (inSection.Count == 0)
        {
            continue;
        }

        builder.Append('\n');
        builder.Append(CultureInfo.InvariantCulture, $"### {sections[i].Title}\n");

        foreach (string section in (string[])["Added", "Changed", "Fixed"])
        {
            List<Entry> matching = [.. inSection.Where(entry => Section(entry.Type, heading) == section)];

            if (matching.Count == 0)
            {
                continue;
            }

            builder.Append('\n');
            builder.Append(CultureInfo.InvariantCulture, $"#### {section}\n");
            builder.Append('\n');

            foreach (Entry entry in matching)
            {
                string scope = entry.Scope is null ? string.Empty : $"**{entry.Scope}**: ";
                string breaking = entry.Breaking ? " **(breaking)**" : string.Empty;

                builder.Append(CultureInfo.InvariantCulture, $"- {scope}{entry.Description}{breaking} ({entry.Sha[..8]})\n");
            }
        }
    }
}

// The released sections of an existing CHANGELOG.md: the text from the first
// "## [<version>]" heading up to the link definitions, and the versions in it.
static (List<string> Versions, string Frozen) ReadReleased(string changelog)
{
    Regex releaseHeading = new(@"^## \[(?<version>[^\]]+)\]", RegexOptions.CultureInvariant);
    Regex linkDefinition = new(@"^\[[^\]]+\]: ", RegexOptions.CultureInvariant);

    string[] lines = changelog.Split('\n');
    List<string> versions = [];
    int first = -1;
    int end = lines.Length;

    for (int i = 0; i < lines.Length; i++)
    {
        Match match = releaseHeading.Match(lines[i]);

        if (match.Success && match.Groups["version"].Value != "Unreleased")
        {
            first = first < 0 ? i : first;
            versions.Add(match.Groups["version"].Value);
        }
        else if (first >= 0 && linkDefinition.IsMatch(lines[i]))
        {
            end = i;
            break;
        }
    }

    return first < 0
        ? ([], string.Empty)
        : (versions, string.Join('\n', lines[first..end]).TrimEnd('\n', ' '));
}

// --check: the newest v* tag reachable from HEAD has its section, here and in
// the tagged commit's CHANGELOG.md, which is the one publish.yml reads.
static int CheckNewestTag(string root, string changelogPath)
{
    (int describeExit, string described, _) = Git(root, "describe", "--tags", "--abbrev=0", "--match", "v*", "HEAD");

    if (describeExit != 0)
    {
        Console.WriteLine("ok  no v* tag is reachable from HEAD; no release to check");
        return 0;
    }

    string tag = described.Trim();
    string version = tag[1..];
    bool failed = false;

    string here = File.Exists(changelogPath) ? File.ReadAllText(changelogPath).ReplaceLineEndings("\n") : string.Empty;

    if (!HasSection(here, version))
    {
        Console.Error.WriteLine($"FAIL: CHANGELOG.md has no non-empty section headed '## [{version}]' for {tag}.");
        failed = true;
    }

    (int showExit, string tagged, string showError) = Git(root, "show", $"{tag}:CHANGELOG.md");

    if (showExit != 0)
    {
        Console.Error.WriteLine($"FAIL: {tag} has no CHANGELOG.md: {showError.Trim()}");
        failed = true;
    }
    else if (!HasSection(tagged.ReplaceLineEndings("\n"), version))
    {
        Console.Error.WriteLine($"FAIL: CHANGELOG.md at {tag} has no non-empty section headed '## [{version}]';");
        Console.Error.WriteLine("      the tag is not on the commit that cut it (eng/changelog.cs --release, ADR 0085).");
        failed = true;
    }

    if (failed)
    {
        return 1;
    }

    Console.WriteLine($"ok  {tag} has its section in CHANGELOG.md, here and at the tag");
    return 0;
}

static bool HasSection(string changelog, string version)
{
    string prefix = $"## [{version}]";
    bool inside = false;

    foreach (string line in changelog.Split('\n'))
    {
        if (inside)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) || Regex.IsMatch(line, @"^\[[^\]]+\]: "))
            {
                return false;
            }

            if (line.Trim().Length > 0)
            {
                return true;
            }
        }
        else if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            inside = true;
        }
    }

    return false;
}

static string? ReadProjectUrl(string root)
{
    string targets = Path.Combine(root, "Directory.Build.targets");

    if (!File.Exists(targets))
    {
        return null;
    }

    Match match = Regex.Match(File.ReadAllText(targets), @"<PackageProjectUrl>\s*(?<url>[^<\s]+)\s*</PackageProjectUrl>");
    return match.Success ? match.Groups["url"].Value.TrimEnd('/') : null;
}

static bool Exists(string root, string reference) =>
    Git(root, "rev-parse", "--verify", "--quiet", reference + "^{commit}").ExitCode == 0;

static string Section(string type, Dictionary<string, string> heading) =>
    heading.GetValueOrDefault(type, "Changed");

static string Relative(string root, string path) =>
    path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

static (int ExitCode, string StandardOutput, string StandardError) Git(string root, params string[] arguments)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = "git",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start git.");

    string standardOutput = process.StandardOutput.ReadToEnd();
    string standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();

    return (process.ExitCode, standardOutput, standardError);
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not find the repository root (no Varve.slnx above the current directory).");
}

sealed record Entry(string Sha, string Type, string? Scope, bool Breaking, string Description);
