// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Release pending gate.
//
//   dotnet run eng/release-pending.cs -- [--base <ref>] [--head <ref>]
//
// ADR 0085: every milestone ends in a release, and the next milestone does not
// start until it has one. A milestone starts when its line is added to
// eng/changelog-sections.txt, so this fails a change that adds a section while
// the section before it — the base's newest — has no v* tag at or after its
// first commit. Its release is pending, and the change waits for it.
//
// A change that adds no section passes whatever the release state is: fixes
// to the milestone being released, and the release commit itself, are what
// has to land while it is pending.
//
// Why a tag and not the CHANGELOG.md section. eng/changelog.cs --release cuts
// the section, and the tag is what publishes it (ADR 0029: the tag is the
// record). A cut that was never tagged is exactly the pending state this gate
// is for, so the tag is the only thing that ends it.
//
// The base, when not given, is worked out as eng/issue-refs.cs does: a pull
// request gives GITHUB_BASE_REF, a push gives the before sha, and a local run
// falls back to origin/main. Tags are read from the clone, so it needs them:
// a full fetch has them, and a shallow clone that cannot resolve the section's
// commit is "could not run", never a pass.
//
// Exit codes: 0 conformant, 1 the previous milestone's release is pending,
// 2 could not run.
//
// See docs/adr/0085-a-release-per-milestone.md.

using System.Diagnostics;

const string SectionsPath = "eng/changelog-sections.txt";

string repositoryRoot = FindRepositoryRoot();

string? baseRef = null;
string? headRef = null;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--base" && i + 1 < arguments.Length)
    {
        baseRef = arguments[++i];
    }
    else if (arguments[i] is "--head" && i + 1 < arguments.Length)
    {
        headRef = arguments[++i];
    }
}

string head = headRef ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "HEAD";
string? resolvedBase = baseRef ?? ResolveBase(repositoryRoot);

if (resolvedBase is null)
{
    Console.WriteLine("note no base to compare against (a first push); nothing can have started");
    return 0;
}

Console.WriteLine($"note base: {resolvedBase}, head: {head}");

List<(string Reference, string Title)>? before = ReadSections(repositoryRoot, resolvedBase);
List<(string Reference, string Title)>? after = ReadSections(repositoryRoot, head);

if (before is null || after is null)
{
    return 2;
}

// What the change adds: the head's sections that the base does not have, by
// commit. A retitled line is not a new milestone.
HashSet<string> known = [.. before.Select(section => section.Reference)];
List<(string Reference, string Title)> added = [.. after.Where(section => !known.Contains(section.Reference))];

if (added.Count == 0)
{
    Console.WriteLine("ok  no milestone starts here");
    return 0;
}

if (before.Count == 0)
{
    Console.WriteLine("ok  the base has no milestone, so none is pending");
    return 0;
}

(string previousReference, string previousTitle) = before[^1];

(int resolved, string previousSha, string resolveError) = Git(repositoryRoot, "rev-parse", "--verify", previousReference + "^{commit}");

if (resolved != 0)
{
    Console.Error.WriteLine($"release-pending: '{previousReference}' does not resolve: {resolveError.Trim()}");
    Console.Error.WriteLine("release-pending: in a shallow clone it may not be present. Fetch the full history and the tags.");
    return 2;
}

previousSha = previousSha.Trim();

(int tagExit, string tags, string tagError) = Git(repositoryRoot, "tag", "--list", "v*", "--contains", previousSha);

if (tagExit != 0)
{
    Console.Error.WriteLine($"release-pending: could not read the tags: {tagError.Trim()}");
    return 2;
}

string[] releases = tags.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (releases.Length > 0)
{
    Console.WriteLine($"ok  '{previousTitle}' is released ({string.Join(", ", releases)}); '{added[0].Title}' may start");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: release pending. '{added[0].Title}' starts here, and '{previousTitle}'");
Console.Error.WriteLine($"      (from {previousSha[..8]}) has no v* tag at or after its first commit.");
Console.Error.WriteLine();
Console.Error.WriteLine("Release it first (ADR 0085): dotnet run eng/changelog.cs -- --release <version>,");
Console.Error.WriteLine("commit, tag that commit v<version>, and let publish.yml run. Then this change can land.");
return 1;

// --- helpers ---------------------------------------------------------------

static string? ResolveBase(string root)
{
    string? prBase = Environment.GetEnvironmentVariable("GITHUB_BASE_REF");

    if (!string.IsNullOrEmpty(prBase))
    {
        return $"origin/{prBase}";
    }

    // GITHUB_EVENT_BEFORE is all zeroes for a branch's first push.
    string? before = Environment.GetEnvironmentVariable("GITHUB_EVENT_BEFORE");

    // A push to main is compared with the before sha. A push to any other
    // branch is compared with main: its head is what main will be
    // fast-forwarded to (ADR 0088).
    bool toMain = Environment.GetEnvironmentVariable("GITHUB_REF") == "refs/heads/main";

    if (toMain && !string.IsNullOrEmpty(before))
    {
        return before.Trim('0').Length > 0 && Exists(root, before) ? before : null;
    }

    return Exists(root, "origin/main") ? "origin/main" : "main";
}

static List<(string Reference, string Title)>? ReadSections(string root, string revision)
{
    (int exitCode, string text, string error) = Git(root, "show", $"{revision}:{SectionsPath}");

    if (exitCode != 0)
    {
        // A revision from before the file existed has no milestones.
        if (error.Contains("does not exist", StringComparison.Ordinal) || error.Contains("exists on disk, but not in", StringComparison.Ordinal))
        {
            return [];
        }

        Console.Error.WriteLine($"release-pending: could not read {SectionsPath} at '{revision}': {error.Trim()}");
        return null;
    }

    List<(string Reference, string Title)> sections = [];

    foreach (string line in text.Split('\n'))
    {
        string trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        int space = trimmed.IndexOf(' ');

        sections.Add(space < 0 ? (trimmed, trimmed) : (trimmed[..space], trimmed[(space + 1)..].Trim()));
    }

    return sections;
}

static bool Exists(string root, string reference) =>
    Git(root, "rev-parse", "--verify", "--quiet", reference + "^{commit}").ExitCode == 0;

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
