// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Every test project is run by some job (#298).
//
//   dotnet run eng/test-projects.cs [-- --root <directory>]
//
// A test project is a directory tests/<name>/ holding <name>.csproj that
// references xunit. It is run when eng/ci.cs names it in Test("<name>"), or
// when eng/ci.cs or a workflow under .github/workflows/ names its path,
// tests/<name>, as a whole path segment. `Varve.Classification.Tests` was in
// the solution, built by every job, and run by none, so the classifier's own
// properties never gated a landing. A project only built by a job still
// passes this check: it reads the jobs' text, not what they do with it.
//
// Exit codes: 0 every test project is run, 1 one is not, 2 could not run.

using System.Text.RegularExpressions;

string root = ".";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length)
    {
        root = args[++i];
    }
    else
    {
        Console.Error.WriteLine("test-projects: [--root <directory>]");
        return 2;
    }
}

string tests = Path.Combine(root, "tests");
string ci = Path.Combine(root, "eng", "ci.cs");
string workflows = Path.Combine(root, ".github", "workflows");

if (!Directory.Exists(tests) || !File.Exists(ci) || !Directory.Exists(workflows))
{
    Console.Error.WriteLine($"test-projects: {root} has no tests/, eng/ci.cs or .github/workflows/.");
    return 2;
}

string ciText = File.ReadAllText(ci);
List<(string Path, string Text)> jobs = [("eng/ci.cs", ciText)];

foreach (string workflow in Directory.EnumerateFiles(workflows, "*.yml").Order(StringComparer.Ordinal))
{
    jobs.Add((".github/workflows/" + Path.GetFileName(workflow), File.ReadAllText(workflow)));
}

List<string> unrun = [];
int count = 0;

foreach (string directory in Directory.EnumerateDirectories(tests).Order(StringComparer.Ordinal))
{
    string name = Path.GetFileName(directory);
    string project = Path.Combine(directory, name + ".csproj");

    if (!File.Exists(project) || !File.ReadAllText(project).Contains("xunit", StringComparison.Ordinal))
    {
        continue;
    }

    count++;

    if (ciText.Contains($"Test(\"{name}\")", StringComparison.Ordinal))
    {
        continue;
    }

    Regex path = new($@"tests/{Regex.Escape(name)}(?![\w.-])");

    if (!jobs.Any(job => path.IsMatch(job.Text)))
    {
        unrun.Add(name);
    }
}

foreach (string name in unrun)
{
    Console.Error.WriteLine($"test-projects: tests/{name} is a test project that no job runs: name it in eng/ci.cs's Test(\"{name}\") and give it a step in .github/workflows/ci.yml's build job.");
}

Console.WriteLine($"test-projects: {count} test projects, {count - unrun.Count} run, {unrun.Count} not.");
return unrun.Count == 0 ? 0 : 1;
