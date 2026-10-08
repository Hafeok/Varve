// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// CHANGELOG.md, the projection of releases/ (ADR 0102).
//
//   dotnet run eng/changelog.cs                          write CHANGELOG.md from releases/
//   dotnet run eng/changelog.cs -- --release <version>   fold releases/<version>.yaml in, and write it
//   dotnet run eng/changelog.cs -- --check               CHANGELOG.md is exactly the projection
//   dotnet run eng/changelog.cs -- --unreleased          what is not released yet, from the commits, to stdout
//   dotnet run eng/changelog.cs -- --output -            the projection to stdout
//
// --- the projection -----------------------------------------------------------
//
// A version's notes are written once, in its descriptor (releases/<version>.yaml:
// the date, the title, the summary), and reviewed in the pull request that
// proposes the release. CHANGELOG.md is rendered from releases/ and nothing
// else, newest version first by SemVer precedence, so it can be checked byte
// for byte: `--check` fails on any hand edit, and on a descriptor not folded
// in. One place to write. v0.1.0-preview.1 was cut before descriptors
// existed (ADR 0085); its descriptor was recorded afterwards, pinned to the
// tagged commit, with its changelog section as the summary.
//
// `--release <version>` is the same rendering, after checking that the
// descriptor exists and is well formed; it is the step a milestone's close-out
// runs. [Unreleased] holds no entries: what is unreleased is a view of the
// commits, not something written, so it is not committed.
//
// --- --unreleased ------------------------------------------------------------------
//
// The commits after the newest v* tag reachable from HEAD, by milestone and by
// Keep a Changelog section: the aid for writing a summary, and the view
// CHANGELOG.md used to carry. eng/changelog-sections.txt names the first commit
// of each milestone; a commit belongs to the last section that started at or
// before it, in the history's own order. The mapping:
//
//   feat                                        -> Added
//   fix                                         -> Fixed
//   perf refactor build ci chore docs test bench -> Changed
//
// and a breaking commit ("feat!:" or a BREAKING CHANGE: trailer) is called out
// wherever it lands (ADR 0035).
//
// Exit codes: 0 written or up to date, 1 --check found it stale or a
// descriptor malformed, 2 could not run.
//
// See docs/releases.md and docs/adr/0102-a-release-is-a-descriptor.md.

// The descriptor reader is shared with eng/release.cs. CA2266 is off for this
// file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Releases.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);

string changelogPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
string releasesPath = Path.Combine(repositoryRoot, ReleaseFormat.Directory);
string? outputPath = null;
bool check = false;
bool unreleasedView = false;
bool toStandardOut = false;
string? releaseVersion = null;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    switch (arguments[i])
    {
        case "--check":
            check = true;
            break;
        case "--unreleased":
            unreleasedView = true;
            break;
        case "--release" when i + 1 < arguments.Length:
            releaseVersion = arguments[++i];
            break;
        case "--releases" when i + 1 < arguments.Length:
            releasesPath = Path.GetFullPath(arguments[++i]);
            break;
        case "--changelog" when i + 1 < arguments.Length:
            changelogPath = Path.GetFullPath(arguments[++i]);
            break;
        case "--output" when i + 1 < arguments.Length:
            string value = arguments[++i];
            toStandardOut = value == "-";
            outputPath = toStandardOut ? null : Path.GetFullPath(value);
            break;
        default:
            Console.Error.WriteLine($"changelog: '{arguments[i]}' is not an option (see the header of eng/changelog.cs).");
            return 2;
    }
}

if (unreleasedView)
{
    return Unreleased(repositoryRoot);
}

string? projectUrl = ReleaseFormat.ReadProjectUrl(repositoryRoot);

if (projectUrl is null)
{
    Console.Error.WriteLine("changelog: no PackageProjectUrl in Directory.Build.targets, which the compare links are built from (ADR 0029).");
    return 2;
}

List<string> problems = [];
List<Descriptor> descriptors = ReleaseFormat.ReadDirectory(Relative(repositoryRoot, releasesPath), problems);

foreach (Descriptor descriptor in descriptors)
{
    ReleaseFormat.CheckShape(descriptor, problems);
}

if (releaseVersion is not null)
{
    string version = releaseVersion.StartsWith('v') ? releaseVersion : "v" + releaseVersion;

    if (!descriptors.Any(descriptor => descriptor.Version == version))
    {
        Console.Error.WriteLine($"changelog: there is no {Relative(repositoryRoot, Path.Combine(releasesPath, version + ".yaml"))} to fold in.");
        Console.Error.WriteLine("           A release is proposed by its descriptor (docs/releases.md); `dotnet run eng/release.cs -- --draft <version>` starts one.");
        return 2;
    }
}

if (problems.Count > 0)
{
    foreach (string problem in problems)
    {
        Console.Error.WriteLine($"  {problem}");
    }

    Console.Error.WriteLine($"changelog: {problems.Count} problem(s) in {Relative(repositoryRoot, releasesPath)}; CHANGELOG.md cannot be projected from it.");
    return check ? 1 : 2;
}

string rendered = ReleaseFormat.RenderChangelog(descriptors, projectUrl);

if (check)
{
    string committed = File.Exists(changelogPath) ? File.ReadAllText(changelogPath).ReplaceLineEndings("\n") : string.Empty;

    if (committed == rendered)
    {
        Console.WriteLine($"ok  {Relative(repositoryRoot, changelogPath)} is the projection of {Relative(repositoryRoot, releasesPath)} ({descriptors.Count} descriptor(s))");
        return 0;
    }

    string[] have = committed.Split('\n');
    string[] want = rendered.Split('\n');
    int line = 0;

    while (line < have.Length && line < want.Length && have[line] == want[line])
    {
        line++;
    }

    Console.Error.WriteLine($"FAIL: {Relative(repositoryRoot, changelogPath)} is not the projection of {Relative(repositoryRoot, releasesPath)}; it differs from line {line + 1}:");
    Console.Error.WriteLine($"      has:  {(line < have.Length ? have[line] : "(end of file)")}");
    Console.Error.WriteLine($"      want: {(line < want.Length ? want[line] : "(end of file)")}");
    Console.Error.WriteLine("      CHANGELOG.md is not edited by hand. Write the notes in the descriptor and run");
    Console.Error.WriteLine("      dotnet run eng/changelog.cs (ADR 0102).");
    return 1;
}

if (toStandardOut)
{
    Console.Write(rendered);
    return 0;
}

File.WriteAllText(outputPath ?? changelogPath, rendered);

Console.WriteLine(releaseVersion is null
    ? $"ok  wrote {Relative(repositoryRoot, outputPath ?? changelogPath)} from {descriptors.Count} descriptor(s)"
    : $"ok  folded {releaseVersion} into {Relative(repositoryRoot, outputPath ?? changelogPath)}; it is cut when the pull request lands at its approved head (docs/releases.md)");
return 0;

// --- --unreleased ---------------------------------------------------------------

static int Unreleased(string root)
{
    string sectionsPath = Path.Combine(root, "eng", "changelog-sections.txt");

    if (!File.Exists(sectionsPath))
    {
        Console.Error.WriteLine($"changelog: no section map at '{Relative(root, sectionsPath)}'.");
        return 2;
    }

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

        (int resolved, string sha, string error) = Git(root, "rev-parse", "--verify", trimmed[..space] + "^{commit}");

        if (resolved != 0)
        {
            Console.Error.WriteLine($"changelog: '{trimmed[..space]}' does not resolve: {error.Trim()}");
            return 2;
        }

        sections.Add((sha.Trim(), trimmed[(space + 1)..].Trim()));
    }

    (int describeExit, string described, _) = Git(root, "describe", "--tags", "--abbrev=0", "--match", "v*", "HEAD");
    string? newest = describeExit == 0 ? described.Trim() : null;

    (int logExit, string log, string logError) = Git(root, "log", "--reverse", "--format=%x01%H%x02%P%x02%s%x02%b");

    if (logExit != 0)
    {
        Console.Error.WriteLine($"changelog: could not read the history: {logError.Trim()}");
        return 2;
    }

    HashSet<string>? after = null;

    if (newest is not null)
    {
        (_, string listed, _) = Git(root, "rev-list", $"{newest}..HEAD");
        after = [.. listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    Regex conventional = new(
        @"^(?<type>[a-z]+)(?:\((?<scope>[^)]*)\))?(?<breaking>!)?:\s*(?<description>.+)$",
        RegexOptions.CultureInvariant);

    Dictionary<string, int> sectionOfSha = new(StringComparer.Ordinal);

    for (int i = 0; i < sections.Count; i++)
    {
        sectionOfSha[sections[i].Sha] = i;
    }

    List<(Entry Entry, int Section)> entries = [];
    int current = -1;

    foreach (string record in log.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] parts = record.Split('\u0002');

        if (parts.Length < 3)
        {
            continue;
        }

        string sha = parts[0].Trim();

        if (sectionOfSha.TryGetValue(sha, out int starts))
        {
            current = starts;
        }

        // A merge's subject describes the merge, and the commits it brings
        // are in this list already.
        if (parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1 || current < 0 || (after is not null && !after.Contains(sha)))
        {
            continue;
        }

        string subject = parts[2].Trim();
        string body = parts.Length > 3 ? parts[3] : string.Empty;
        Match match = conventional.Match(subject);

        entries.Add((match.Success
            ? new Entry(sha, match.Groups["type"].Value, match.Groups["scope"].Success ? match.Groups["scope"].Value : null,
                match.Groups["breaking"].Success || body.Contains("BREAKING CHANGE:", StringComparison.Ordinal), match.Groups["description"].Value.Trim())
            : new Entry(sha, "chore", null, false, subject), current));
    }

    StringBuilder builder = new();
    builder.Append(CultureInfo.InvariantCulture, $"## Unreleased, after {newest ?? "the first commit"} ({entries.Count} commit(s))\n");

    // Newest milestone first, which is the order a reader wants.
    for (int i = sections.Count - 1; i >= 0; i--)
    {
        List<Entry> inSection = [.. entries.Where(entry => entry.Section == i).Select(entry => entry.Entry)];

        if (inSection.Count == 0)
        {
            continue;
        }

        builder.Append(CultureInfo.InvariantCulture, $"\n### {sections[i].Title}\n");

        foreach (string heading in (string[])["Added", "Changed", "Fixed"])
        {
            List<Entry> matching = [.. inSection.Where(entry => Heading(entry.Type) == heading)];

            if (matching.Count == 0)
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture, $"\n#### {heading}\n\n");

            foreach (Entry entry in matching)
            {
                string scope = entry.Scope is null ? string.Empty : $"**{entry.Scope}**: ";
                string breaking = entry.Breaking ? " **(breaking)**" : string.Empty;
                builder.Append(CultureInfo.InvariantCulture, $"- {scope}{entry.Description}{breaking} ({entry.Sha[..8]})\n");
            }
        }
    }

    Console.Write(builder.ToString());
    return 0;
}

static string Heading(string type) => type switch
{
    "feat" => "Added",
    "fix" => "Fixed",
    _ => "Changed",
};

// --- helpers ---------------------------------------------------------------

static string Relative(string root, string path) =>
    path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

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
