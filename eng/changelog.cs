// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// CHANGELOG.md, from the conventional commits.
//
//   dotnet run eng/changelog.cs
//   dotnet run eng/changelog.cs -- --check
//   dotnet run eng/changelog.cs -- --output -
//
// Keep a Changelog (keepachangelog.com/1.1.0) over the commit history, because
// the commits already say what changed and a hand-written changelog is a second
// place for the same facts to be wrong in. `--check` regenerates and compares,
// so a stale CHANGELOG.md can be caught rather than noticed.
//
// --- sections -------------------------------------------------------------
//
// Keep a Changelog groups by release. This repository has no release: nothing
// is published and there is no v* tag (ADR 0029), so everything is Unreleased
// and the honest subdivision is the milestone. eng/changelog-sections.txt names
// the first commit of each, and the rest is derived — a commit belongs to the
// last section that started at or before it, in the history's own order.
//
// It could have been derived from the merge commits instead, and for 3a, the
// 3a close-out and 3b that would work. It would not work for milestones 1 and
// 2, which predate the pull-request rule and are a single unbroken run of
// commits on main, so the boundary between them exists only in the intent.
// A file naming five commits is less clever and does not have that hole.
//
// Once a v* tag exists this needs a second mode: tags become the sections, and
// the milestone subdivision applies only to what is unreleased. That is not
// written yet, because writing it now would be guessing at a shape no release
// has taken.
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
using System.Text;
using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();

string outputPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
string sectionsPath = Path.Combine(repositoryRoot, "eng", "changelog-sections.txt");
bool check = false;
bool toStandardOut = false;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--check")
    {
        check = true;
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

List<List<Entry>> bySection = [.. sections.Select(static _ => new List<Entry>())];
int current = -1;

foreach (Entry entry in entries)
{
    if (sectionOfSha.TryGetValue(entry.Sha, out int starts))
    {
        current = starts;
    }

    if (current >= 0)
    {
        bySection[current].Add(entry);
    }
}

if (current < 0)
{
    Console.Error.WriteLine("changelog: no commit in the history starts a section; the map does not match this branch.");
    return 2;
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
builder.AppendLine();
builder.AppendLine("**Nothing has been released.** No `v*` tag exists and no package has been");
builder.AppendLine("published to nuget.org, so every change below is unreleased and the sections");
builder.AppendLine("are milestones rather than versions. The first tag is `v0.1.0-preview.1`");
builder.AppendLine("([ADR 0029](docs/adr/0029-publishing-and-versioning.md)).");

// Newest milestone first, which is the order a reader wants.
for (int i = sections.Count - 1; i >= 0; i--)
{
    List<Entry> inSection = bySection[i];

    if (inSection.Count == 0)
    {
        continue;
    }

    builder.AppendLine();
    builder.AppendLine($"### {sections[i].Title}");

    foreach (string section in (string[])["Added", "Changed", "Fixed"])
    {
        List<Entry> matching = [.. inSection.Where(entry => Section(entry.Type, heading) == section)];

        if (matching.Count == 0)
        {
            continue;
        }

        builder.AppendLine();
        builder.AppendLine($"#### {section}");
        builder.AppendLine();

        foreach (Entry entry in matching)
        {
            string scope = entry.Scope is null ? string.Empty : $"**{entry.Scope}**: ";
            string breaking = entry.Breaking ? " **(breaking)**" : string.Empty;

            builder.AppendLine($"- {scope}{entry.Description}{breaking} ({entry.Sha[..8]})");
        }
    }
}

builder.AppendLine();

string rendered = builder.ToString().ReplaceLineEndings("\n");

if (toStandardOut)
{
    Console.Write(rendered);
    return 0;
}

if (check)
{
    string existing = File.Exists(outputPath)
        ? File.ReadAllText(outputPath).ReplaceLineEndings("\n")
        : string.Empty;

    if (string.Equals(existing, rendered, StringComparison.Ordinal))
    {
        Console.WriteLine($"ok  {Relative(repositoryRoot, outputPath)} is up to date");
        return 0;
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {Relative(repositoryRoot, outputPath)} is stale.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Regenerate it: dotnet run eng/changelog.cs");
    return 1;
}

File.WriteAllText(outputPath, rendered);
Console.WriteLine($"ok  wrote {Relative(repositoryRoot, outputPath)}, {entries.Count} commit(s) across {sections.Count} section(s)");

return 0;

// --- helpers ---------------------------------------------------------------

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
