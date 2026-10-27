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
string sbomOutput = Path.Combine(repositoryRoot, "artifacts", "sbom");

// The order is the dependency order, and it is also cheapest-first: the four
// text gates cost seconds and catch the mistakes that are easiest to make, so
// they run before anything waits on a restore.
List<(string Name, string Description, Func<int> Run)> jobs =
[
    ("register", "every package names the ADR that admits it",
        () => Run("dotnet", Args("run", "eng/dependency-register.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    // The scope check's failure path (tests/fixtures/dependency-register/README.md).
    ("register-fixture", "the register fails a samples-only package referenced from src/ and passes it in a sample",
        () => Run("dotnet", ["run", "eng/dependency-register.cs", "--", "tests/fixtures/dependency-register/failing/Directory.Packages.props"]) == 1
            ? Run("dotnet", ["run", "eng/dependency-register.cs", "--", "tests/fixtures/dependency-register/passing/Directory.Packages.props"])
            : 1),

    // ADR 0224's failure paths, one broken register per check, each copied out
    // of the repository and restored for real where it needs a restore
    // (tests/fixtures/dependency-register/README.md).
    ("register-fixtures", "the register gate fails each broken register for its own reason: unadmitted, stale, dangling, unpinned, unexempted, licence, native",
        () => Run("dotnet", ["run", "eng/dependency-register.cs", "--", "--fixtures", "tests/fixtures/dependency-register"])),

    ("licence-headers", "every .cs file carries the MPL-2.0 notice",
        () => Run("dotnet", ["run", "eng/licence-headers.cs"])),

    ("decision-sets", "every decision set file is well formed, names where it comes from, and is never accepted before it was filed",
        () => Run("dotnet", ["run", "eng/decision-sets.cs"])),

    ("banned-symbols", "every banned symbol cites the decision that bans it",
        () => Run("dotnet", ["run", "eng/banned-symbols.cs"])),

    ("issue-refs", "every commit references a tracked issue",
        () => Run("dotnet", Args("run", "eng/issue-refs.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    ("dco", "every commit is signed off by its author, or by the human responsible for its agent",
        () => Run("dotnet", Args("run", "eng/dco.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    // The gate's failure path, proven on every run: the failing fixture must
    // exit 1 and the passing one 0 (tests/fixtures/dco/README.md).
    ("dco-fixture", "the DCO gate fails its failing fixture and passes its passing one",
        () => Run("dotnet", ["run", "eng/dco.cs", "--", "--commits", "tests/fixtures/dco/failing.txt", "--identities", "tests/fixtures/dco/identities.json"]) == 1
            ? Run("dotnet", ["run", "eng/dco.cs", "--", "--commits", "tests/fixtures/dco/passing.txt", "--identities", "tests/fixtures/dco/identities.json"])
            : 1),

    ("required-checks", "every check ruleset 1 requires is a job that reports on pull requests",
        () => Run("dotnet", ["run", "eng/required-checks.cs"])),

    // Its failure path, as for the DCO gate (tests/fixtures/required-checks/README.md).
    ("required-checks-fixture", "the required-checks gate fails its fixture",
        () => Run("dotnet", ["run", "eng/required-checks.cs", "--", "--declaration", "tests/fixtures/required-checks/repo-standard.yaml", "--workflows", "tests/fixtures/required-checks/workflows"]) == 1 ? 0 : 1),

    ("ci-images", "every test container CI starts comes from the mirror at its listed digest, or is an allowed upstream reference",
        () => Run("dotnet", ["run", "eng/ci-images.cs"])),

    // Its failure path (tests/fixtures/ci-images/README.md).
    ("ci-images-fixture", "the CI image gate fails its fixture",
        () => Run("dotnet", ["run", "eng/ci-images.cs", "--", "--root", "tests/fixtures/ci-images"]) == 1 ? 0 : 1),

    ("agent-review-tamper", "a pull request that edits eng/agent-review.cs to always pass still fails under main's copy",
        () => Run("dotnet", ["run", "eng/agent-review-tamper.cs"])),

    ("release-pending", "a change that closes a milestone issue carries the release descriptor naming it",
        () => Run("dotnet", Args("run", "eng/release-pending.cs", baseRef is null ? null : "--", baseRef is null ? null : "--base", baseRef))),

    ("changelog", "CHANGELOG.md is the projection of releases/",
        () => Run("dotnet", ["run", "eng/changelog.cs", "--", "--check"])),

    // ADR 0102: every descriptor valid, and every proposed one resolved at the
    // commit under release; it prints how a proposed release lands.
    ("release", "every release descriptor is well formed, and a proposed one's basis resolves",
        () => Run("dotnet", Args("run", "eng/release.cs", "--", "--check", baseRef is null ? null : "--base", baseRef))),

    // README.md ships in every package; its Status section and the roadmap's
    // headings are a checked projection (ADR 0102).
    ("status", "README.md's status and docs/roadmap.md's headings match the releases, the baseline and the projects",
        () => Run("dotnet", ["run", "eng/status.cs", "--", "--check"])),

    ("status-fixtures", "the status check fails each of its fixtures, for its own reason",
        () => Run("dotnet", ["run", "eng/status.cs", "--", "--fixtures", "tests/fixtures/status"])),

    // Its failure paths, proven on every run: each case must fail, and say why
    // (tests/fixtures/releases/README.md).
    ("release-fixtures", "the release validator fails each of its fixtures, for its own reason",
        () => Run("dotnet", ["run", "eng/release.cs", "--", "--fixtures", "tests/fixtures/releases"])),

    // Not a gate: the maintainer's landing helper (ADR 0088). Built here so
    // that it does not rot between landings.
    ("land", "the landing helper builds and prints its usage",
        () => Run("dotnet", ["run", "eng/land.cs", "--", "--help"])),

    // The solution, and the projects outside it that have lockfiles: the
    // samples, the benchmarks, the smoke apps and repo-standard. Locked, as CI
    // always restores (ADR 0224), so a lockfile the change forgot fails here.
    ("restore", "restore the solution, and every project outside it with a lockfile, locked",
        RestoreEverything),

    // ADR 0224: every resolved package's licence for its scope, and no native
    // asset in a shipped closure (what eng/native-assets.cs was).
    ("register-packages", "every resolved package's licence is allowed for its scope, and no shipped closure carries a native asset",
        () => Run("dotnet", ["run", "eng/dependency-register.cs", "--", "--packages"])),

    // ADR 0225: an SBOM per artefact, from the restore, written twice and
    // required byte-identical. The binaries' are written for every RID the
    // CLI ships; the image's needs its base scanned, which publish.yml does.
    ("sbom", "an SBOM per shipped artefact, from the locked restore, byte-identical when written twice",
        Sbom),

    ("build", "build with warnings as errors",
        () => Run("dotnet", ["build", "Varve.slnx", "--configuration", "Release", "--no-restore"])),

    // The benchmarks are outside Varve.slnx, and not run here (ADR 0027): a
    // benchmark that gates is a flaky build. They are built, because they link
    // conformance sources and reference every package, and in 5c they broke
    // without any gate seeing it. Their own restore: dotNetRDF is theirs alone.
    ("build-benchmarks", "the benchmark project builds; it is not run",
        () => Run("dotnet", ["build", "tests/Varve.Benchmarks/Varve.Benchmarks.csproj", "--configuration", "Release"])),

    ("test-analyzers", "the analyzer rules and their fixtures",
        () => Test("Varve.Analyzers.Tests")),

    ("test-iri", "Varve.Iri",
        () => Test("Varve.Iri.Tests")),

    ("test-rdf", "Varve.Rdf",
        () => Test("Varve.Rdf.Tests")),

    ("test-turtle", "Varve.Turtle",
        () => Test("Varve.Turtle.Tests")),

    ("test-rdfxml", "Varve.RdfXml: the reader and writer, the round trip, allocation",
        () => Test("Varve.RdfXml.Tests")),

    ("test-jsonld", "Varve.JsonLd: toRdf, expand and fromRdf, the round trip, allocation",
        () => Test("Varve.JsonLd.Tests")),

    ("test-delta", "Varve.Delta: the chunk-boundary oracle, the round trip, version 1 read for ever",
        () => Test("Varve.Delta.Tests")),
    ("test-rec", "the REC portfolio generator: the vendored release, REC's terms, the same bytes for each seed and size",
        () => Test("Varve.Rec.Tests")),

    ("test-json-canonical", "Varve.Json.Canonical: RFC 8785's vectors, Node's numbers as the oracle, the writer's refusals",
        () => Test("Varve.Json.Canonical.Tests")),

    ("test-derivation", "Varve.Derivation: paths, their reverse and their anchors against a relational oracle, components and refused cycles by reachability, propagation as the least fixpoint",
        () => Test("Varve.Derivation.Tests")),

    ("test-events", "Varve.Events: rendering replayed byte for byte, the number rule, schema golden files, compatibility, the catalogue's three views, AsyncAPI 3.0, the declaration shapes' agreement with the code",
        () => Test("Varve.Events.Tests")),

    ("test-management", "Varve.Management: the vocabulary's reader and code validator, grants over a caller with templates, the plan's idempotence",
        () => Test("Varve.Management.Tests")),

    ("test-xsd", "Varve.Xsd, and its properties; the evaluation suites gate it too",
        () => Test("Varve.Xsd.Tests")),

    ("test-store", "Varve.Store, and the specification's section 10 properties",
        () => Test("Varve.Store.Tests")),

    ("test-sparql", "Varve.Sparql: the algebra, the parser, and the round-trip property",
        () => Test("Varve.Sparql.Tests")),

    ("test-sparql-results", "Varve.Sparql.Results: the four readers and writers, the chunk-boundary oracle over every suite result file, the round trip",
        () => Test("Varve.Sparql.Results.Tests")),

    ("test-sparql-evaluation", "Varve.Sparql.Evaluation: the optimiser and store properties, allocation, MD5, cancellation, SERVICE",
        () => Test("Varve.Sparql.Evaluation.Tests")),

    ("test-sparql-store", "Varve.Sparql.Store: SPARQL Update as one commit, and the reference-model property",
        () => Test("Varve.Sparql.Store.Tests")),

    ("test-shacl", "Varve.Shacl: ADR 0150's recursion examples and the greatest-fixpoint property, the report's determinism property, inputs and failures, a store left as it was, incremental validation against a full one at every position, the classification",
        () => Test("Varve.Shacl.Tests")),

    ("test-shacl-sparql", "Varve.Shacl.Sparql: the path differential against the evaluator, dotNetRDF as a differential oracle (ADR 0156), SPARQL constraints maintained incrementally, the refused functions",
        () => Test("Varve.Shacl.Sparql.Tests")),

    ("test-shacl-store", "Varve.Shacl.Store: a reject binding against a full-validation oracle over generated commits, the attachment's digest after the commit, ADR 0169's rules",
        () => Test("Varve.Shacl.Store.Tests")),

    ("test-protocol", "Varve.Protocol: the endpoints over a real socket, the protocol properties",
        () => Test("Varve.Protocol.Tests")),

    ("test-protocol-client", "Varve.Protocol.Client: the token provider, FollowAsync and FollowEventsAsync resumed from every position, setup, the client's closure",
        () => Test("Varve.Protocol.Client.Tests")),

    ("test-server", "Varve.Server: configuration, anonymous mode, readiness, shutdown, and the permission matrix against an issuer in the test (ADR 0100 layer (a))",
        () => Test("Varve.Server.Tests")),

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

    // Last, and never a failure: the report reads the Release build, and
    // prints how it moved from the committed baseline (ADR 0063).
    ("decision-report", "the decision-driven report against its baseline; never gates",
        DecisionReport),
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

int DecisionReport()
{
    int exitCode = Run("dotnet", ["run", "eng/decision-report.cs"]);

    if (exitCode != 0)
    {
        Console.WriteLine("decision-report: did not run; it does not gate, so the pipeline goes on.");
    }

    return 0;
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

    // Each package embeds its SBOM (ADR 0224 point 3), which the sbom job
    // wrote for the version MinVer gives this commit.
    int packed = Run("dotnet",
    [
        "pack", "Varve.slnx",
        "--configuration", "Release",
        "--no-build",
        "--output", packageOutput,
        $"-p:VarveSbomDirectory={sbomOutput}",
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

int RestoreEverything()
{
    int restored = Run("dotnet", ["restore", "Varve.slnx", "-p:RestoreLockedMode=true"]);

    // The browser apps need the WebAssembly workloads, which the devcontainer
    // installs and a bare machine may not have; without them their restore is
    // skipped here and the register gate names them as not restored.
    bool wasm = Capture("dotnet", ["workload", "list"]).Contains("wasm-tools", StringComparison.Ordinal);

    foreach (string project in OutsideTheSolution())
    {
        if (restored != 0)
        {
            break;
        }

        if (!wasm && (project.Contains("WasmSmoke", StringComparison.Ordinal) || project.Contains("BrowserTests", StringComparison.Ordinal)))
        {
            Console.WriteLine($"skip  {project}: the WebAssembly workloads are not installed");
            continue;
        }

        restored = Run("dotnet", ["restore", project, "-p:RestoreLockedMode=true"]);
    }

    return restored;
}

// Every project with a lockfile that Varve.slnx does not restore.
List<string> OutsideTheSolution()
{
    HashSet<string> members = new(System.Xml.Linq.XDocument.Load(Path.Combine(repositoryRoot, "Varve.slnx"))
        .Descendants("Project")
        .Select(static e => e.Attribute("Path")?.Value?.Replace('\\', '/') ?? ""), StringComparer.Ordinal);

    return [.. Directory.EnumerateFiles(repositoryRoot, "packages.lock.json", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(repositoryRoot, Path.GetDirectoryName(path)!).Replace('\\', '/'))
        .Where(static d => !d.StartsWith("tests/fixtures/", StringComparison.Ordinal) && !d.Split('/').Contains("obj"))
        .Select(d => Directory.EnumerateFiles(Path.Combine(repositoryRoot, d), "*.csproj").Select(p => Path.GetRelativePath(repositoryRoot, p).Replace('\\', '/')).Single())
        .Where(p => !members.Contains(p))
        .Order(StringComparer.Ordinal)];
}

int Sbom()
{
    // The version MinVer gives this commit, so that the packed packages and
    // the SBOMs they embed agree; the creation time is the commit's.
    string version = Capture("dotnet", ["msbuild", "src/Varve.Iri/Varve.Iri.csproj", "-t:MinVer", "-getProperty:MinVerVersion"]).Trim();
    string epoch = Capture("git", ["log", "-1", "--format=%ct"]).Trim();
    string created = DateTimeOffset.FromUnixTimeSeconds(long.Parse(epoch, System.Globalization.CultureInfo.InvariantCulture)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    string again = Path.Combine(repositoryRoot, "artifacts", "sbom-again");

    if (Directory.Exists(sbomOutput))
    {
        Directory.Delete(sbomOutput, recursive: true);
    }

    string[] binaries = ["--binary", "linux-x64", "--binary", "linux-arm64", "--binary", "win-x64", "--binary", "win-arm64", "--binary", "osx-x64", "--binary", "osx-arm64"];
    int first = Run("dotnet", ["run", "eng/sbom.cs", "--", "--version", version, "--created", created, "--out", sbomOutput, .. binaries]);

    return first != 0
        ? first
        : Run("dotnet", ["run", "eng/sbom.cs", "--", "--version", version, "--created", created, "--out", again, "--check-against", sbomOutput, .. binaries]);
}

// --- helpers ---------------------------------------------------------------

static string[] Args(params string?[] parts) =>
    parts.Where(static part => part is not null).Select(static part => part!).ToArray();

// A command's standard output, for a value a job needs; its failure is the
// job's.
string Capture(string fileName, string[] arguments)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = fileName,
        WorkingDirectory = repositoryRoot,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Could not start {fileName}.");

    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    return process.ExitCode == 0
        ? output
        : throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} exited {process.ExitCode}.");
}

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
