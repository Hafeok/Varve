// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The decision-driven report. Never a gate.
//
//   dotnet run eng/decision-report.cs
//   dotnet run eng/decision-report.cs -- --update
//
// ADR 0063 admits DecisionDriven.Report as a CI-only .NET tool (the local
// manifest, .config/dotnet-tools.json, pins it): the whole-graph report over
// the built assemblies — the layers' instability, each contract's use by its
// callers, model cohesion, and every citation of a decision. ADR 0062 says it
// never gates, and the report says so itself: a metric becomes a gate only by
// a decision that names its threshold and baseline.
//
// What this adds is the baseline. eng/decision-report/baseline.md is the
// report as last committed, and a run prints what moved since: a layer's
// instability, a contract a caller now uses less of, a type whose cohesion
// split, a citation added or gone. The report's own header names the tool
// version and the commit, which differ every run, and is not compared.
// citations.nt, the same citations as RDF, carries the commit of every
// citation, so it is written to artifacts/report and uploaded, not committed.
//
// Run it after a Release build of the solution. It reads the one assembly
// each shipped project builds, src/Varve.*/bin/Release/<tfm>/Varve.*.dll,
// and not the analyzers, which are not a Varve package's code. --update
// rewrites the baseline from the run, for a change whose report differences
// are meant.
//
// Exit codes: 0 whatever the report says, 2 could not run. A difference from
// the baseline is printed, and in CI written to the step summary; it is never
// a failure.

using System.Diagnostics;

string repositoryRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);

bool update = args.Contains("--update", StringComparer.Ordinal);
string output = Path.Combine(repositoryRoot, "artifacts", "report");
string baseline = Path.Combine(repositoryRoot, "eng", "decision-report", "baseline.md");

List<string> assemblies = [];

foreach (string project in Directory.GetDirectories(Path.Combine(repositoryRoot, "src"), "Varve.*").Order(StringComparer.Ordinal))
{
    string name = Path.GetFileName(project);

    if (name == "Varve.Analyzers")
    {
        continue;
    }

    string release = Path.Combine(project, "bin", "Release");
    string? built = Directory.Exists(release)
        ? Directory.GetFiles(release, name + ".dll", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
        : null;

    if (built is null)
    {
        Console.Error.WriteLine($"decision-report: {name} has no Release build. Build the solution with --configuration Release first.");
        return 2;
    }

    assemblies.Add(built);
}

if (Run("dotnet", ["tool", "restore"]) != 0)
{
    Console.Error.WriteLine("decision-report: the tool did not restore.");
    return 2;
}

List<string> reportArgs = ["decisiondriven-report", "--out", output];

foreach (string assembly in assemblies)
{
    reportArgs.Add("--assembly");
    reportArgs.Add(assembly);
}

if (Run("dotnet", reportArgs) != 0)
{
    Console.Error.WriteLine("decision-report: the report did not run.");
    return 2;
}

string report = Path.Combine(output, "report.md");

if (update)
{
    Directory.CreateDirectory(Path.GetDirectoryName(baseline)!);
    File.Copy(report, baseline, overwrite: true);
    Console.WriteLine($"decision-report: baseline rewritten from {assemblies.Count} assemblies.");
    return 0;
}

if (!File.Exists(baseline))
{
    Console.WriteLine("decision-report: no baseline is committed; run with --update to write one.");
    return 0;
}

string[] before = Comparable(File.ReadAllLines(baseline));
string[] after = Comparable(File.ReadAllLines(report));
List<string> removed = [.. before.Except(after, StringComparer.Ordinal)];
List<string> added = [.. after.Except(before, StringComparer.Ordinal)];

List<string> summary = [];

if (removed.Count == 0 && added.Count == 0)
{
    summary.Add($"The decision-driven report matches the baseline ({assemblies.Count} assemblies).");
}
else
{
    summary.Add($"The decision-driven report differs from eng/decision-report/baseline.md: {removed.Count} line(s) gone, {added.Count} new. This does not gate; if the change is meant, run `dotnet run eng/decision-report.cs -- --update` and commit the baseline.");
    summary.Add(string.Empty);
    summary.Add("```diff");
    summary.AddRange(removed.Select(line => "- " + line));
    summary.AddRange(added.Select(line => "+ " + line));
    summary.Add("```");
}

foreach (string line in summary)
{
    Console.WriteLine(line);
}

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
{
    File.AppendAllLines(stepSummary, ["## Decision-driven report", string.Empty, .. summary]);
}

return 0;

// The report without its header line, which names the tool version and the
// commit: every other line is a claim about the code.
static string[] Comparable(string[] lines) =>
    [.. lines.Where(line => !line.StartsWith("DecisionDriven.Report ", StringComparison.Ordinal))];

static int Run(string fileName, IReadOnlyList<string> arguments)
{
    ProcessStartInfo start = new(fileName) { UseShellExecute = false };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + fileName + ".");
    process.WaitForExit();
    return process.ExitCode;
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

    throw new InvalidOperationException("Not inside the Varve repository: no Varve.slnx above " + Directory.GetCurrentDirectory() + ".");
}
