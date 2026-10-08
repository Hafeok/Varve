// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Project status, checked (ADR 0102, docs/releases.md §7).
//
//   dotnet run eng/status.cs -- --check [--at <commit>]
//   dotnet run eng/status.cs -- --fixtures tests/fixtures/status
//
// README.md's Status section and docs/roadmap.md's milestone headings are a
// projection of facts the repository holds: the release descriptors, the
// conformance baseline and the projects. lib/Status.cs says which four
// statements are checked and why; this runs them over the working tree, or
// over a commit with --at. eng/release.cs runs the same check at the commit
// under release, so a release never ships a README that describes another one.
//
// --fixtures runs every case under the directory: each must fail, and say what
// its expect.txt says. A case overrides README.md, roadmap.md, passing.txt or
// releases/ with its own; the projects are the repository's.
//
// Exit codes: 0 current, 1 findings, 2 could not run.
//
// See docs/adr/0102-a-release-is-a-descriptor.md.

// The descriptor reader and the status check are shared with eng/release.cs.
// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Releases.cs
#:include lib/Status.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;

string root = FindRepositoryRoot();
Directory.SetCurrentDirectory(root);

string? at = null;
string? fixtures = null;
bool check = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            check = true;
            break;
        case "--at" when i + 1 < args.Length:
            at = args[++i];
            break;
        case "--fixtures" when i + 1 < args.Length:
            fixtures = args[++i];
            break;
        default:
            Console.Error.WriteLine($"status: '{args[i]}' is not an option (see the header of eng/status.cs).");
            return 2;
    }
}

if (fixtures is not null)
{
    return Fixtures(root, fixtures);
}

if (!check)
{
    Console.Error.WriteLine("status: --check [--at <commit>], or --fixtures <dir>.");
    return 2;
}

StatusTree tree;

if (at is null)
{
    tree = WorkingTree(root);
}
else
{
    (int exit, string sha) = Git(root, "rev-parse", "--verify", "--quiet", at + "^{commit}");

    if (exit != 0)
    {
        Console.Error.WriteLine($"status: '{at}' is not a commit in this clone.");
        return 2;
    }

    tree = CommitTree(root, sha.Trim());
}

List<string> problems = [];
ProjectStatus.Check(tree, problems);
return Report(problems, at ?? "the working tree");

static int Report(List<string> problems, string where)
{
    if (problems.Count == 0)
    {
        Console.WriteLine($"ok  README.md's status and docs/roadmap.md's headings are current at {where}");
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"FAIL: {problems.Count} statement(s) about the project are stale at {where}:");

    foreach (string problem in problems)
    {
        Console.WriteLine($"  {problem}");
    }

    Console.WriteLine();
    Console.WriteLine("README.md ships in every package; it says what is true (docs/releases.md §7, ADR 0102).");
    return 1;
}

// Each directory with an expect.txt is a case; it must fail, saying each line.
static int Fixtures(string root, string directory)
{
    List<string> failures = [];
    string[] cases = [.. Directory.GetDirectories(Path.GetFullPath(directory)).Where(path => File.Exists(Path.Combine(path, "expect.txt"))).Order(StringComparer.Ordinal)];

    foreach (string path in cases)
    {
        string name = Path.GetFileName(path);
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        string Own(string file, string fallback) => File.Exists(Path.Combine(path, file)) ? $"{relative}/{file}" : fallback;

        List<string> problems = [];
        ProjectStatus.Check(
            WorkingTree(root), problems,
            readmePath: Own("README.md", StatusTree.ReadmePath),
            roadmapPath: Own("roadmap.md", StatusTree.RoadmapPath),
            passingPath: Own("passing.txt", StatusTree.PassingPath),
            releasesPath: Directory.Exists(Path.Combine(path, "releases")) ? $"{relative}/releases" : ReleaseFormat.Directory);

        string said = string.Join('\n', problems);
        string[] expected = [.. File.ReadAllLines(Path.Combine(path, "expect.txt")).Where(line => line.Trim().Length > 0 && !line.StartsWith('#'))];
        string[] missing = [.. expected.Where(line => !said.Contains(line.Trim(), StringComparison.Ordinal))];

        if (problems.Count > 0 && missing.Length == 0)
        {
            Console.WriteLine($"ok   {name}: fails, saying {string.Join("; ", expected.Select(line => $"'{line.Trim()}'"))}");
        }
        else
        {
            failures.Add(name);
            Console.WriteLine($"FAIL {name}: {problems.Count} finding(s){(missing.Length == 0 ? "" : $", without saying {string.Join("; ", missing.Select(line => $"'{line.Trim()}'"))}")}");
            problems.ForEach(problem => Console.WriteLine($"       {problem}"));
        }
    }

    if (cases.Length == 0)
    {
        Console.Error.WriteLine($"status: no cases under {directory}.");
        return 2;
    }

    Console.WriteLine(failures.Count == 0
        ? $"ok  every one of {cases.Length} failing status fixture(s) fails, for its own reason"
        : $"FAIL: {failures.Count} fixture(s) did not fail as expected: {string.Join(", ", failures)}");
    return failures.Count == 0 ? 0 : 1;
}

static StatusTree WorkingTree(string root) => new(
    path => File.Exists(Path.Combine(root, path)) ? File.ReadAllText(Path.Combine(root, path)) : null,
    directory => Directory.Exists(Path.Combine(root, directory))
        ? Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.Contains("/bin/", StringComparison.Ordinal) && !file.Contains("/obj/", StringComparison.Ordinal))
        : []);

static StatusTree CommitTree(string root, string sha) => new(
    path => Git(root, "show", $"{sha}:{path}") is (0, string text) ? text : null,
    directory => Git(root, "ls-tree", "-r", "--name-only", sha, "--", directory) is (0, string listed)
        ? listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : []);

static (int Exit, string Output) Git(string root, params string[] arguments)
{
    ProcessStartInfo start = new()
    {
        FileName = "git",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
    Task<string> error = process.StandardError.ReadToEndAsync();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    _ = error.Result;
    return (process.ExitCode, output);
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
