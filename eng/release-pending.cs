// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Release pending gate.
//
//   dotnet run eng/release-pending.cs -- [--base <ref>] [--head <ref>]
//
// ADR 0102, which supersedes ADR 0085's reminder with a gate: every milestone
// ends in a release, so **a change that closes a milestone issue carries the
// release descriptor whose basis names that issue**. Closing the issue and
// proposing the release are one pull request, and landing it at its approved
// head cuts the release (docs/releases.md).
//
// A milestone issue is one in the table at the top of docs/roadmap.md, read
// at the base, so a change cannot take its own issue off the list. A change
// closes it when a commit in the range says so: a line `Closes #N`, `Fixes
// #N` or `Resolves #N` (any tense), which is what GitHub acts on when the
// commit reaches main. The descriptor is a releases/<version>.yaml the range
// adds, with `- issue: N` in its basis; eng/release.cs checks everything else
// about it.
//
// A change that closes no milestone issue passes. The reminder this gate used
// to be, a milestone held until the one before it was tagged, is gone: the
// release is no longer a step after the merge that can slip.
//
// The base, when not given, is worked out as eng/issue-refs.cs does: a pull
// request gives GITHUB_BASE_REF, a push gives the before sha, and a local run
// falls back to origin/main.
//
// Exit codes: 0 conformant, 1 a milestone issue closed without its
// descriptor, 2 could not run.
//
// See docs/adr/0102-a-release-is-a-descriptor.md.

// The descriptor reader is shared with eng/release.cs. CA2266 is off for this
// file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Releases.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text.RegularExpressions;

const string RoadmapPath = "docs/roadmap.md";

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
    Console.WriteLine("ok  no base to compare with (a first push); nothing is closed here");
    return 0;
}

Console.WriteLine($"note base: {resolvedBase}, head: {head}");

// The milestone issues, from the base's roadmap.
(int roadmapExit, string roadmap, string roadmapError) = Git(repositoryRoot, "show", $"{resolvedBase}:{RoadmapPath}");

if (roadmapExit != 0)
{
    Console.Error.WriteLine($"release-pending: could not read {RoadmapPath} at '{resolvedBase}': {roadmapError.Trim()}");
    return 2;
}

HashSet<int> milestones = MilestoneIssues(roadmap);

if (milestones.Count == 0)
{
    Console.Error.WriteLine($"release-pending: {RoadmapPath} at '{resolvedBase}' has no milestone table with issue links; nothing would ever be gated.");
    return 2;
}

// What the range closes.
(int logExit, string log, string logError) = Git(repositoryRoot, "log", "--no-merges", "--format=%x01%h%x02%B", $"{resolvedBase}..{head}");

if (logExit != 0)
{
    Console.Error.WriteLine($"release-pending: could not read {resolvedBase}..{head}: {logError.Trim()}");
    Console.Error.WriteLine("release-pending: in a shallow clone the base may not be present. Fetch the full history.");
    return 2;
}

Regex closing = new(@"(?im)^\s*(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b:?\s+(?:[\w.-]+/[\w.-]+)?#(?<issue>\d+)\b", RegexOptions.CultureInvariant);
Dictionary<int, string> closed = [];

foreach (string record in log.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
{
    string[] parts = record.Split('\u0002');

    if (parts.Length < 2)
    {
        continue;
    }

    foreach (Match match in closing.Matches(parts[1]))
    {
        int issue = int.Parse(match.Groups["issue"].Value, System.Globalization.CultureInfo.InvariantCulture);

        if (milestones.Contains(issue))
        {
            closed.TryAdd(issue, parts[0].Trim());
        }
    }
}

if (closed.Count == 0)
{
    Console.WriteLine("ok  no milestone issue is closed here, so no release is due");
    return 0;
}

// The descriptors the range adds, and the issue each names.
(int diffExit, string diff, string diffError) = Git(repositoryRoot, "diff", "--name-only", "--diff-filter=A", $"{resolvedBase}...{head}", "--", ReleaseFormat.Directory);

if (diffExit != 0)
{
    Console.Error.WriteLine($"release-pending: could not list what {resolvedBase}...{head} adds: {diffError.Trim()}");
    return 2;
}

Dictionary<int, string> carried = [];

foreach (string file in diff.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(file => file.EndsWith(".yaml", StringComparison.Ordinal)))
{
    (int showExit, string text, _) = Git(repositoryRoot, "show", $"{head}:{file}");

    if (showExit != 0)
    {
        continue;
    }

    Descriptor descriptor = ReleaseFormat.Parse(file, text, []);

    foreach (BasisLine line in descriptor.Basis.Where(line => line.Kind == "issue"))
    {
        if (int.TryParse(line.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int issue))
        {
            carried.TryAdd(issue, file);
        }
    }
}

int failures = 0;

foreach ((int issue, string commit) in closed.OrderBy(pair => pair.Key))
{
    if (carried.TryGetValue(issue, out string? file))
    {
        Console.WriteLine($"ok  #{issue}, a milestone issue, is closed by {commit} and released by {file}");
    }
    else
    {
        Console.Error.WriteLine($"FAIL: #{issue} is a milestone issue ({RoadmapPath}), and {commit} closes it, but this change adds no");
        Console.Error.WriteLine($"      releases/<version>.yaml whose basis names it ('  - issue: {issue}').");
        failures++;
    }
}

if (failures > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("A milestone ends in a release, proposed in the same pull request (ADR 0102):");
    Console.Error.WriteLine("dotnet run eng/release.cs -- --draft <version> starts the descriptor, and docs/releases.md says the rest.");
    return 1;
}

return 0;

// --- helpers ---------------------------------------------------------------

// Every issue linked from the first table under the roadmap's heading: the
// milestone table, two milestones to a row.
static HashSet<int> MilestoneIssues(string roadmap)
{
    HashSet<int> issues = [];
    bool inTable = false;

    foreach (string line in roadmap.ReplaceLineEndings("\n").Split('\n'))
    {
        if (line.StartsWith('|'))
        {
            inTable = true;

            foreach (Match match in Regex.Matches(line, @"\[#(?<issue>\d+)\]\([^)]*/issues/\k<issue>\)", RegexOptions.CultureInvariant))
            {
                issues.Add(int.Parse(match.Groups["issue"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        else if (inTable)
        {
            break;
        }
    }

    return issues;
}

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
