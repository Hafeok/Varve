// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The pipeline. One entry point, the same jobs CI runs.
//
//   dotnet run eng/ci.cs
//   dotnet run eng/ci.cs -- --list
//   dotnet run eng/ci.cs -- --only build --only test-turtle
//   dotnet run eng/ci.cs -- --skip conformance
//   dotnet run eng/ci.cs -- --base origin/main
//
// ADR 0036, and the Mind Over Machine stewardship item behind it: pipelines are
// containerised and fully executable from the local development environment.
// The point is not that a developer *can* reproduce CI — they always could, by
// reading the workflow and retyping it — but that there is one definition and
// the workflow calls it. Two descriptions of the same intent drift, and the
// drift is discovered by a red build on something that passed locally.
//
// So .github/workflows/ci.yml runs this file inside the devcontainer image, and
// a developer runs the same file in the same image. What CI adds on top is the
// coverage a single linux container cannot give: the Windows leg, the Native
// AOT publish and run, and the browser WASM build. Those stay outside, and
// ADR 0036 says why rather than pretending otherwise — milestone 3b's CR LF
// defect was visible on Windows alone, and dropping that leg to make a slogan
// true would remove the coverage that earned it.
//
// Jobs run in order and the run stops at the first failure, because everything
// after a failed build is noise. --only and --skip take a job name and may be
// repeated; --list prints the names.
//
// Exit codes: 0 all jobs passed, 1 a job failed, 2 could not run.
//
// See docs/adr/0036-containerised-development.md.

using System.Diagnostics;
using System.Text;

string repositoryRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);

List<string> only = [];
List<string> skip = [];
bool list = false;
string? baseRef = null;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    switch (arguments[i])
    {
        case "--list":
            list = true;
            break;
        case "--only" when i + 1 < arguments.Length:
            only.Add(arguments[++i]);
            break;
        case "--skip" when i + 1 < arguments.Length:
            skip.Add(arguments[++i]);
            break;
        case "--base" when i + 1 < arguments.Length:
            baseRef = arguments[++i];
            break;
        default:
            break;
    }
}

string conformanceResults = Path.Combine(repositoryRoot, "artifacts", "conformance");
string packageOutput = Path.Combine(repositoryRoot, "artifacts", "packages");

// The order is the dependency order, and it is also cheapest-first: the three
// text gates cost seconds and catch the mistakes that are easiest to make, so
// they run before anything waits on a restore.
List<(string Name, string Description, Func<int> Run)> jobs =
[
    ("register", "every package names the ADR that admits it",
        () => Run("dotnet", Args("run", "eng/dependency-register.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    ("licence-headers", "every .cs file carries the MPL-2.0 notice",
        () => Run("dotnet", ["run", "eng/licence-headers.cs"])),

    ("issue-refs", "every commit references a tracked issue",
        () => Run("dotnet", Args("run", "eng/issue-refs.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    ("restore", "restore the solution",
        () => Run("dotnet", ["restore", "Varve.slnx"])),

    ("native-assets", "no native asset in a shipped closure",
        () => Run("dotnet", ["run", "eng/native-assets.cs"])),

    ("build", "build with warnings as errors",
        () => Run("dotnet", ["build", "Varve.slnx", "--configuration", "Release", "--no-restore"])),

    ("test-analyzers", "the analyzer rules and their fixtures",
        () => Test("Varve.Analyzers.Tests")),

    ("test-iri", "Varve.Iri",
        () => Test("Varve.Iri.Tests")),

    ("test-rdf", "Varve.Rdf",
        () => Test("Varve.Rdf.Tests")),

    ("test-turtle", "Varve.Turtle",
        () => Test("Varve.Turtle.Tests")),

    ("test-xsd", "Varve.Xsd, and its properties; the evaluation suites gate it too",
        () => Test("Varve.Xsd.Tests")),

    ("test-store", "Varve.Store, and the specification's section 10 properties",
        () => Test("Varve.Store.Tests")),

    ("test-sparql", "Varve.Sparql: the algebra, the parser, and the round-trip property",
        () => Test("Varve.Sparql.Tests")),

    ("test-sparql-results", "Varve.Sparql.Results: the four readers, the chunk-boundary oracle over every suite result file",
        () => Test("Varve.Sparql.Results.Tests")),

    ("test-sparql-evaluation", "Varve.Sparql.Evaluation: the optimiser and store properties, allocation, MD5, cancellation, SERVICE",
        () => Test("Varve.Sparql.Evaluation.Tests")),

    // tools/repo-standard is its own solution, outside Varve.slnx, and moves to
    // its own repository (ADR 0039). Its jobs are separate so that the move
    // deletes them rather than untangling them. The Native AOT publish and the
    // Action test are in ci.yml, beside Varve's own AOT job.
    ("repo-standard-build", "the repo-standard tool, warnings as errors",
        () => Run("dotnet", ["build", "tools/repo-standard/RepoStandard.slnx", "--configuration", "Release"])),

    ("repo-standard-test", "repo-standard: recorded exchanges, round trip, the rest",
        () => Run("dotnet", ["test", "--project", "tools/repo-standard/tests/RepoStandard.Tests/RepoStandard.Tests.csproj", "--configuration", "Release"])),

    ("conformance", "the W3C suites, gated by the ratchet",
        Conformance),

    ("pack", "the publish dry run, and the package metadata",
        Pack),
];

if (list)
{
    Console.WriteLine("Jobs, in order:");
    Console.WriteLine();

    foreach ((string name, string description, _) in jobs)
    {
        Console.WriteLine($"  {name,-16} {description}");
    }

    return 0;
}

foreach (string name in only.Concat(skip))
{
    if (!jobs.Any(job => string.Equals(job.Name, name, StringComparison.Ordinal)))
    {
        Console.Error.WriteLine($"ci: no job called '{name}'. Run with --list.");
        return 2;
    }
}

// --- run -------------------------------------------------------------------

List<(string Name, bool Passed, TimeSpan Elapsed)> results = [];
string? failedJob = null;

foreach ((string name, string description, Func<int> run) in jobs)
{
    if (only.Count > 0 && !only.Contains(name, StringComparer.Ordinal))
    {
        continue;
    }

    if (skip.Contains(name, StringComparer.Ordinal))
    {
        Console.WriteLine($"--- {name}: skipped");
        continue;
    }

    Console.WriteLine();
    Console.WriteLine($"=== {name} — {description}");
    Console.WriteLine();

    Stopwatch stopwatch = Stopwatch.StartNew();
    int exitCode = run();
    stopwatch.Stop();

    results.Add((name, exitCode == 0, stopwatch.Elapsed));

    if (exitCode != 0)
    {
        failedJob = name;
        break;
    }
}

// --- report ----------------------------------------------------------------

string summary = BuildSummary(results, failedJob);

Console.WriteLine();
Console.WriteLine(summary);

string? stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrEmpty(stepSummary))
{
    File.AppendAllText(stepSummary, summary + Environment.NewLine);
}

if (failedJob is not null)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: the '{failedJob}' job failed. Jobs after it did not run.");
    Console.Error.WriteLine($"      Re-run it alone with: dotnet run eng/ci.cs -- --only {failedJob}");
    return 1;
}

if (results.Count == 0)
{
    Console.Error.WriteLine("ci: no jobs ran. Check --only and --skip against --list.");
    return 2;
}

return 0;

// --- jobs ------------------------------------------------------------------

int Test(string project) =>
    Run("dotnet", ["test", "--project", $"tests/{project}/{project}.csproj", "--configuration", "Release"]);

int Conformance()
{
    string submodule = Path.Combine(repositoryRoot, "tests", "w3c", "rdf-tests");

    // An uninitialised submodule makes the suites pass by having nothing in
    // them, which is the failure docs/testing.md §1 pins case counts against.
    // Saying so here costs one directory check and saves an afternoon.
    if (!Directory.Exists(submodule) || !Directory.EnumerateFileSystemEntries(submodule).Any())
    {
        Console.Error.WriteLine("ci: tests/w3c/rdf-tests is empty. Run:");
        Console.Error.WriteLine("      git submodule update --init --recursive");
        return 2;
    }

    // The test run itself is not the gate and its exit code is ignored on
    // purpose (ADR 0007): an exempt case is allowed to fail, and a case outside
    // the baseline is allowed to fail while it is being worked on. The ratchet
    // is what gates, and it fails on a regression, on a case that vanished, and
    // on an exemption with no justification.
    Run("dotnet",
    [
        "test",
        "--project", "tests/Varve.Conformance.Tests/Varve.Conformance.Tests.csproj",
        "--configuration", "Release",
        "--report-trx",
        "--results-directory", conformanceResults,
    ]);

    return Run("dotnet", ["run", "eng/ratchet.cs", "--", conformanceResults, "--label", "local"]);
}

int Pack()
{
    // A local run leaves the previous run's packages behind, and the metadata
    // gate reads every .nupkg in the directory. Without this, a run checks
    // yesterday's packages as well as today's — which passes for as long as
    // yesterday's were fine and then reports a failure in a package that no
    // longer exists. CI gets a clean workspace and never sees it.
    if (Directory.Exists(packageOutput))
    {
        Directory.Delete(packageOutput, recursive: true);
    }

    int packed = Run("dotnet",
    [
        "pack", "Varve.slnx",
        "--configuration", "Release",
        "--no-build",
        "--output", packageOutput,
    ]);

    if (packed != 0)
    {
        return packed;
    }

    // --allow-missing-repository is NOT passed, here or in CI. A package
    // published without a repository link cannot be traced to the commit that
    // built it, and a clone whose origin is a local path is the one case where
    // this legitimately cannot be derived — that case passes the flag by hand.
    return Run("dotnet", ["run", "eng/package-metadata.cs", "--", packageOutput]);
}

// --- helpers ---------------------------------------------------------------

static string[] Args(params string?[] parts) =>
    parts.Where(static part => part is not null).Select(static part => part!).ToArray();

int Run(string fileName, string[] arguments)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = fileName,
        WorkingDirectory = repositoryRoot,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    Console.WriteLine($"$ {fileName} {string.Join(' ', arguments)}");

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

    process.WaitForExit();

    return process.ExitCode;
}

static string BuildSummary(List<(string Name, bool Passed, TimeSpan Elapsed)> results, string? failedJob)
{
    StringBuilder builder = new();

    builder.AppendLine("## Pipeline");
    builder.AppendLine();
    builder.AppendLine("| Job | Result | Seconds |");
    builder.AppendLine("|---|---|---:|");

    foreach ((string name, bool passed, TimeSpan elapsed) in results)
    {
        builder.AppendLine($"| `{name}` | {(passed ? "pass" : "**fail**")} | {elapsed.TotalSeconds:F1} |");
    }

    if (failedJob is not null)
    {
        builder.AppendLine();
        builder.AppendLine($"Stopped at `{failedJob}`; later jobs did not run.");
    }

    return builder.ToString().TrimEnd();
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
