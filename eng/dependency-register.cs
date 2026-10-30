// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Dependency register gate.
//
//   dotnet run eng/dependency-register.cs -- [<Directory.Packages.props>] [--adr-dir <path>]
//   dotnet run eng/dependency-register.cs -- --base <ref>
//
// ADR 0009 makes Directory.Packages.props the register of every dependency this
// repository admits, and requires each PackageVersion to name the decision that
// admits it:
//
//   <PackageVersion Include="xunit.v3" Version="4.0.1" Adr="0009" />
//
// This fails the build when a PackageVersion carries no Adr, or cites a number
// with no matching docs/adr/NNNN-*.md. Constraint 4 in docs/brief.md says every
// third-party package needs an ADR; before this, nothing checked that, and a
// package with no ADR — or citing one that was never written — built green.
//
// What it deliberately does not do: judge whether the cited ADR actually argues
// the case. It catches the omission and the dangling citation, which are the
// failures that happen. Whether the argument is any good is review's job.
//
// --- --base, and the Dependabot amendment ----------------------------------
//
// ADR 0009's amendment of 2026-09-22 lets automated dependency updates through
// without an ADR for each one, and draws the line at the point where a version
// change can mean something the ADR did not consider:
//
//   patch or minor bump of a package that already cites an ADR  ->  passes
//   major bump                                                  ->  needs an ADR entry
//   new package                                                 ->  needs an ADR entry
//
// Without this, every Dependabot pull request would fail the gate and the
// register would be a thing people route around. With it, the gate still stops
// the two changes that are decisions rather than maintenance.
//
// "Needs an ADR entry" is made checkable rather than left to judgement: the
// ADR the package cites must itself have changed between the base and the
// working tree. Bumping a major version and citing the same untouched ADR means
// the decision that admitted 4.x is being claimed to cover 5.x without anyone
// having looked, which is exactly the claim worth interrupting.
//
// A version this cannot parse is treated as a major bump. A register entry
// whose version is an expression or a range is not something to wave through.
//
// --base compares against a git ref rather than a file, so it works the same in
// a Dependabot pull request, in a local run before pushing, and in eng/ci.cs.
// Without --base the gate behaves exactly as it did before: every entry needs a
// citation, and nothing is classified.
//
// --- what was resolved (ADR 0224) --------------------------------------------
//
//   dotnet run eng/dependency-register.cs -- [<props>] [--packages]
//   dotnet run eng/dependency-register.cs -- --assets <project.assets.json>
//   dotnet run eng/dependency-register.cs -- --fixtures tests/fixtures/dependency-register
//
// The register is still Directory.Packages.props, and the one register: a
// transitive package is admitted by the ADR of the package that brings it in,
// and the lockfile diff shows it in review. What the register is checked
// against is now what the restore resolved, every packages.lock.json under
// the register's directory, direct and transitive:
//
//   - a direct package in a lockfile that no PackageVersion or ImplicitPackage
//     admits (the SDK's own references, ILLink and ILCompiler among them, are
//     ImplicitPackage entries naming what adds them);
//   - a register entry no lockfile, and no local tool, resolves any more;
//   - an Action referenced by tag or branch rather than by commit SHA, in any
//     workflow or action.yml (ADR 0224 point 5; #89 covers permissions only);
//   - a NuGetAuditSuppress with no OpenVEX statement in
//     security/vex.openvex.json saying why Varve is not affected, or a
//     statement with no suppression (ADR 0224 point 6).
//
// These read text only, so they run before restore, in seconds.
//
// --packages adds what needs the restore: each resolved package's licence,
// from its nuspec or the register's PackageLicence statement, against its
// scope's allow-list; and no package in a shipped project's closure carrying
// a native asset, unless a NativeAssetException admits that project (this was
// eng/native-assets.cs, folded in by ADR 0224). A package is runtime when it is
// in what a consumer of a shipped package resolves, or in what a shipped
// project's output carries; otherwise it is build or test.
//
// --assets checks one restore graph for native assets and nothing else, which
// is how tests/Varve.Analyzers.Tests exercises that rule against real graphs.
//
// --fixtures runs every case under a directory: each holds a register, the
// projects and files under it, `args.txt` (the gate's arguments after the
// props path, often empty) and `expect.txt`, every line of which the gate must
// print while exiting 1. A case with `restore.txt` is restored first, each
// line a project to restore, with lockfiles written.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0009-dependency-policy-and-register.md and
// docs/adr/0224-supply-chain-transparency.md.

// The restore readers are shared with eng/sbom.cs. CA2266 is off for this file
// only, for the reason eng/dco.cs gives.
#:include lib/Restores.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repositoryRoot = FindRepositoryRoot();

string propsPath = Path.Combine(repositoryRoot, "Directory.Packages.props");
string adrDirectory = Path.Combine(repositoryRoot, "docs", "adr");

string? baseRef = null;
string? assetsOnly = null;
string? fixturesDirectory = null;
bool checkPackages = false;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--adr-dir" && i + 1 < arguments.Length)
    {
        adrDirectory = arguments[++i];
    }
    else if (arguments[i] is "--base" && i + 1 < arguments.Length)
    {
        baseRef = arguments[++i];
    }
    else if (arguments[i] is "--assets" && i + 1 < arguments.Length)
    {
        assetsOnly = arguments[++i];
    }
    else if (arguments[i] is "--fixtures" && i + 1 < arguments.Length)
    {
        fixturesDirectory = arguments[++i];
    }
    else if (arguments[i] is "--packages")
    {
        checkPackages = true;
    }
    else if (!arguments[i].StartsWith("--", StringComparison.Ordinal))
    {
        propsPath = arguments[i];
    }
}

if (fixturesDirectory is not null)
{
    return RunFixtures(repositoryRoot, Path.GetFullPath(fixturesDirectory));
}

if (assetsOnly is not null)
{
    if (!File.Exists(assetsOnly))
    {
        Console.Error.WriteLine($"dependency-register: no assets file at '{assetsOnly}'.");
        return 2;
    }

    List<(string Package, List<string> Files)> natives = NativePackages(Restores.ReadGraph(assetsOnly));

    if (natives.Count == 0)
    {
        Console.WriteLine($"ok  {assetsOnly}: no native asset");
        return 0;
    }

    ReportNatives(assetsOnly, natives);
    return 1;
}

if (!File.Exists(propsPath))
{
    Console.Error.WriteLine($"dependency-register: no register at '{propsPath}'.");
    return 2;
}

if (!Directory.Exists(adrDirectory))
{
    Console.Error.WriteLine($"dependency-register: no ADR directory at '{adrDirectory}'.");
    return 2;
}

// --- what ADRs exist -------------------------------------------------------

HashSet<string> knownAdrs = new(StringComparer.Ordinal);
Regex adrFile = new(@"^(?<number>\d{4})-", RegexOptions.CultureInvariant);

foreach (string file in Directory.EnumerateFiles(adrDirectory, "*.md"))
{
    Match match = adrFile.Match(Path.GetFileName(file));
    if (match.Success)
    {
        knownAdrs.Add(match.Groups["number"].Value);
    }
}

if (knownAdrs.Count == 0)
{
    Console.Error.WriteLine($"dependency-register: '{adrDirectory}' holds no NNNN-*.md ADRs.");
    return 2;
}

// --- read the register -----------------------------------------------------

XDocument register;
try
{
    register = XDocument.Load(propsPath);
}
catch (System.Xml.XmlException exception)
{
    Console.Error.WriteLine($"dependency-register: '{propsPath}' is not well-formed XML: {exception.Message}");
    return 2;
}

List<(string Package, string Adr)> entries = [];
List<string> missing = [];
List<(string Package, string Adr)> dangling = [];
List<(string Package, string Adr)> malformed = [];

// Kept beside the lists above for --base, which needs the version and the
// citation of every entry rather than only the ones that passed.
Dictionary<string, string> currentVersions = new(StringComparer.OrdinalIgnoreCase);
Dictionary<string, string> citedAdr = new(StringComparer.OrdinalIgnoreCase);

// Directory.Packages.props is usually written without an MSBuild namespace, but
// tolerate one rather than silently finding nothing and reporting success.
foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
{
    string package = item.Attribute("Include")?.Value ?? "(no Include)";
    string? adr = ReadMetadata(item, "Adr");

    string? version = item.Attribute("Version")?.Value ?? ReadMetadata(item, "Version");

    if (!string.IsNullOrWhiteSpace(version))
    {
        currentVersions[package] = version.Trim();
    }

    if (!string.IsNullOrWhiteSpace(adr))
    {
        citedAdr[package] = adr.Trim();
    }

    if (string.IsNullOrWhiteSpace(adr))
    {
        missing.Add(package);
        continue;
    }

    adr = adr.Trim();

    if (!Regex.IsMatch(adr, @"^\d{4}$", RegexOptions.CultureInvariant))
    {
        malformed.Add((package, adr));
        continue;
    }

    if (!knownAdrs.Contains(adr))
    {
        dangling.Add((package, adr));
        continue;
    }

    entries.Add((package, adr));
}

if (entries.Count + missing.Count + dangling.Count + malformed.Count == 0)
{
    Console.Error.WriteLine($"dependency-register: '{propsPath}' declares no packages at all.");
    return 2;
}

// --- report ----------------------------------------------------------------

Console.WriteLine(BuildSummary(entries, missing.Count + dangling.Count + malformed.Count));

string? stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrEmpty(stepSummary))
{
    File.AppendAllText(stepSummary, BuildSummary(entries, missing.Count + dangling.Count + malformed.Count)
        + Environment.NewLine);
}

bool failed = false;

if (missing.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"FAIL: {missing.Count} package(s) name no ADR. Constraint 4 requires one for every third-party "
        + "package, build-time and test-time included. Add Adr=\"NNNN\" to:");

    foreach (string package in missing.OrderBy(static p => p, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"  {package}");
    }
}

if (dangling.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {dangling.Count} package(s) cite an ADR that does not exist:");

    foreach ((string package, string adr) in dangling.OrderBy(static e => e.Package, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"  {package} cites ADR {adr}, but there is no {adr}-*.md in docs/adr/");
    }
}

if (malformed.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {malformed.Count} package(s) cite a malformed ADR number:");

    foreach ((string package, string adr) in malformed.OrderBy(static e => e.Package, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"  {package} cites '{adr}'; an ADR number is exactly four digits");
    }
}

// --- scopes (ADR 0009, amended 2026-10-13; ADR 0133) -------------------------
//
// An entry may carry Scope, a comma-separated list of directory prefixes
// relative to the register's directory: every project that references the
// package must sit under one of them. A samples-only package referenced from
// src/ is the failure this exists for. tests/fixtures/ is not scanned when the
// register is the repository's: those projects exist to fail.

Dictionary<string, string[]> scopes = new(StringComparer.OrdinalIgnoreCase);

foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
{
    string? scope = ReadMetadata(item, "Scope");

    if (!string.IsNullOrWhiteSpace(scope))
    {
        scopes[item.Attribute("Include")?.Value ?? ""] = [.. scope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(static s => s.Replace('\\', '/'))];
    }
}

string scanRoot = Path.GetDirectoryName(Path.GetFullPath(propsPath))!;
List<string> outOfScope = [];

if (scopes.Count > 0)
{
    foreach (string project in Directory.EnumerateFiles(scanRoot, "*.csproj", SearchOption.AllDirectories))
    {
        string relative = Relative(scanRoot, project).Replace(Path.DirectorySeparatorChar, '/');

        if (relative.Split('/').Any(static segment => segment is "bin" or "obj" or ".git" or "node_modules")
            || (string.Equals(scanRoot, repositoryRoot, StringComparison.Ordinal) && relative.StartsWith("tests/fixtures/", StringComparison.Ordinal)))
        {
            continue;
        }

        XDocument document;

        try
        {
            document = XDocument.Load(project);
        }
        catch (System.Xml.XmlException)
        {
            continue;
        }

        foreach (XElement reference in document.Descendants().Where(static e => e.Name.LocalName == "PackageReference"))
        {
            string package = reference.Attribute("Include")?.Value ?? "";

            if (scopes.TryGetValue(package, out string[]? allowedIn) && !allowedIn.Any(prefix => relative.StartsWith(prefix, StringComparison.Ordinal)))
            {
                outOfScope.Add($"  {relative} references {package}, which the register scopes to {string.Join(", ", allowedIn)}");
            }
        }
    }
}

if (outOfScope.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {outOfScope.Count} reference(s) outside the scope their register entry names (ADR 0133):");

    foreach (string line in outOfScope.Order(StringComparer.Ordinal))
    {
        Console.Error.WriteLine(line);
    }
}

// --- what was resolved (ADR 0224) -------------------------------------------

bool isRepository = string.Equals(scanRoot, repositoryRoot, StringComparison.Ordinal);
List<string> lockfiles = Restores.FindLockfiles(scanRoot, skipFixtures: isRepository);

// ImplicitPackage: a package the SDK, or an MSBuild SDK, references by itself,
// so that no PackageVersion can name it. Its Include may end in `*`, for the
// packages an SDK adds per host RID.
List<(string Pattern, string Adr, string Via)> implicitPackages = [];

foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "ImplicitPackage"))
{
    string pattern = item.Attribute("Include")?.Value ?? "";
    string adr = (ReadMetadata(item, "Adr") ?? "").Trim();
    string via = (ReadMetadata(item, "Via") ?? "").Trim();

    if (!knownAdrs.Contains(adr))
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: the implicit package {pattern} cites ADR '{adr}', which does not exist.");
    }

    if (via.Length == 0)
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: the implicit package {pattern} does not say what references it (Via).");
    }

    implicitPackages.Add((pattern, adr, via));
}

HashSet<string> registered = new(currentVersions.Keys.Concat(missing).Concat(dangling.Select(static d => d.Package)), StringComparer.OrdinalIgnoreCase);
bool IsImplicit(string id) => implicitPackages.Any(p => Matches(p.Pattern, id));

if (lockfiles.Count == 0)
{
    if (isRepository)
    {
        Console.Error.WriteLine("dependency-register: no packages.lock.json anywhere. Every project restores with a lockfile (ADR 0224).");
        return 2;
    }

    Console.WriteLine("note  no lockfile under this register: the resolved-graph checks do not apply to it");
}
else
{
    HashSet<string> resolved = new(StringComparer.OrdinalIgnoreCase);
    SortedSet<string> unregistered = new(StringComparer.Ordinal);

    foreach (string path in lockfiles)
    {
        Lockfile lockfile = Restores.ReadLockfile(path);

        foreach (LockedPackage package in lockfile.Packages)
        {
            resolved.Add(package.Id);

            if (package.Type is "Direct" or "CentralTransitive" && !registered.Contains(package.Id) && !IsImplicit(package.Id))
            {
                unregistered.Add($"  {Relative(scanRoot, path).Replace('\\', '/')}: {package.Id} {package.Version} ({package.Type.ToLowerInvariant()})");
            }
        }
    }

    foreach ((string id, _) in Restores.ReadToolManifest(scanRoot))
    {
        resolved.Add(id);
    }

    Console.WriteLine($"ok  {lockfiles.Count} lockfile(s), {resolved.Count} package(s) resolved, direct and transitive");

    if (unregistered.Count > 0)
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"FAIL: {unregistered.Count} direct reference(s) in a lockfile that no register entry admits. A package is "
            + "admitted by a PackageVersion naming its ADR, or, when the SDK references it by itself, by an "
            + "ImplicitPackage naming what adds it (ADR 0224):");

        foreach (string line in unregistered)
        {
            Console.Error.WriteLine(line);
        }
    }

    List<string> stale = [.. currentVersions.Keys.Where(id => !resolved.Contains(id)).Order(StringComparer.Ordinal)];
    stale.AddRange(implicitPackages.Where(p => !resolved.Any(id => Matches(p.Pattern, id))).Select(static p => p.Pattern + " (implicit)"));

    if (stale.Count > 0)
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: {stale.Count} register entr(y/ies) that no lockfile and no local tool resolves. Remove each, with its ADR's amendment if it was the reason the ADR exists:");

        foreach (string id in stale)
        {
            Console.Error.WriteLine($"  {id}");
        }
    }
}

// Actions by commit SHA. A tag or a branch can be moved under the reference,
// and what CI runs changes with no diff here.
Regex usesLine = new(@"^\s*(?:-\s*)?uses:\s*[""']?(?<action>[^""'\s#]+)", RegexOptions.CultureInvariant);
Regex pinned = new(@"@[0-9a-f]{40}$", RegexOptions.CultureInvariant);
List<string> unpinned = [];
int actionReferences = 0;

foreach (string workflow in WorkflowFiles(scanRoot, isRepository))
{
    string[] lines = File.ReadAllLines(workflow);

    for (int i = 0; i < lines.Length; i++)
    {
        Match match = usesLine.Match(lines[i]);

        if (!match.Success)
        {
            continue;
        }

        string action = match.Groups["action"].Value;

        if (action.StartsWith("./", StringComparison.Ordinal) || action.StartsWith("docker://", StringComparison.Ordinal))
        {
            continue;
        }

        actionReferences++;

        if (!pinned.IsMatch(action))
        {
            unpinned.Add($"  {Relative(scanRoot, workflow).Replace('\\', '/')}:{i + 1}: {action}");
        }
    }
}

if (unpinned.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {unpinned.Count} Action reference(s) not pinned by commit SHA (ADR 0224 point 5). Pin each to the full 40-character SHA, with the tag as a comment:");

    foreach (string line in unpinned)
    {
        Console.Error.WriteLine(line);
    }
}
else if (actionReferences > 0)
{
    Console.WriteLine($"ok  {actionReferences} Action reference(s), each pinned by commit SHA");
}

// A known vulnerability is exempted only with a statement that says why Varve
// is not affected, published with every release.
SortedSet<string> suppressions = new(StringComparer.Ordinal);

foreach (string file in new[] { "Directory.Packages.props", "Directory.Build.props" }.Select(f => Path.Combine(scanRoot, f)).Where(File.Exists))
{
    foreach (XElement item in XDocument.Load(file).Descendants().Where(static e => e.Name.LocalName == "NuGetAuditSuppress"))
    {
        suppressions.Add(item.Attribute("Include")?.Value ?? "");
    }
}

string vexPath = Path.Combine(scanRoot, "security", "vex.openvex.json");
Dictionary<string, (string Status, bool Justified)> statements = new(StringComparer.Ordinal);

if (File.Exists(vexPath))
{
    try
    {
        using JsonDocument vex = JsonDocument.Parse(File.ReadAllText(vexPath));

        foreach (JsonElement statement in vex.RootElement.GetProperty("statements").EnumerateArray())
        {
            JsonElement vulnerability = statement.GetProperty("vulnerability");
            string id = vulnerability.TryGetProperty("@id", out JsonElement at) ? at.GetString() ?? ""
                : vulnerability.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? "" : "";
            string status = statement.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? "" : "";
            bool justified = statement.TryGetProperty("justification", out _) || statement.TryGetProperty("impact_statement", out _);
            statements[id] = (status, justified);
        }
    }
    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
    {
        Console.Error.WriteLine($"dependency-register: {Relative(scanRoot, vexPath)} is not an OpenVEX document: {exception.Message}");
        return 2;
    }
}
else if (isRepository)
{
    Console.Error.WriteLine($"dependency-register: no {Relative(scanRoot, vexPath)}; it is published with every release, empty or not (ADR 0224).");
    return 2;
}

List<string> vexFindings = [];

foreach (string suppression in suppressions)
{
    if (!statements.TryGetValue(suppression, out (string Status, bool Justified) statement))
    {
        vexFindings.Add($"  NuGetAuditSuppress {suppression} has no statement in security/vex.openvex.json");
    }
    else if (statement.Status != "not_affected" || !statement.Justified)
    {
        vexFindings.Add($"  the statement for {suppression} is '{statement.Status}'{(statement.Justified ? "" : " with no justification")}; a suppression needs not_affected and a justification or impact statement");
    }
}

foreach (string id in statements.Keys.Where(id => !suppressions.Contains(id)).Order(StringComparer.Ordinal))
{
    vexFindings.Add($"  security/vex.openvex.json states {id}, which no NuGetAuditSuppress suppresses");
}

if (vexFindings.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {vexFindings.Count} vulnerability exemption(s) without their other half (ADR 0224 point 6):");

    foreach (string line in vexFindings)
    {
        Console.Error.WriteLine(line);
    }
}
else
{
    Console.WriteLine($"ok  {suppressions.Count} audit suppression(s), each with its VEX statement");
}

// --- --packages: licences and native assets ---------------------------------

if (checkPackages)
{
    int packageExit = CheckPackages(register, knownAdrs, scanRoot, repositoryRoot, isRepository, lockfiles);

    if (packageExit == 2)
    {
        return 2;
    }

    failed |= packageExit == 1;
}

// --- the Dependabot amendment ----------------------------------------------

if (baseRef is not null)
{
    string relativeProps = Relative(repositoryRoot, Path.GetFullPath(propsPath)).Replace(Path.DirectorySeparatorChar, '/');

    (int showExit, string baseProps, string showError) = Git(repositoryRoot, "show", $"{baseRef}:{relativeProps}");

    if (showExit != 0)
    {
        Console.Error.WriteLine($"dependency-register: could not read '{relativeProps}' at '{baseRef}': {showError.Trim()}");
        Console.Error.WriteLine("dependency-register: in a shallow clone the base may not be present. Fetch it.");
        return 2;
    }

    Dictionary<string, string> baseVersions;
    try
    {
        baseVersions = ReadVersions(XDocument.Parse(baseProps));
    }
    catch (System.Xml.XmlException exception)
    {
        Console.Error.WriteLine($"dependency-register: the register at '{baseRef}' is not well-formed XML: {exception.Message}");
        return 2;
    }

    // ADR files touched between the base and the working tree. `git diff <ref>`
    // rather than `<ref>..HEAD` so an uncommitted ADR counts: a local run
    // before committing should give the same answer as CI will.
    (int diffExit, string diffOutput, string diffError) = Git(repositoryRoot, "diff", "--name-only", baseRef, "--", "docs/adr/");

    if (diffExit != 0)
    {
        Console.Error.WriteLine($"dependency-register: could not diff docs/adr/ against '{baseRef}': {diffError.Trim()}");
        return 2;
    }

    HashSet<string> changedAdrNumbers = new(StringComparer.Ordinal);

    foreach (string path in diffOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Match match = adrFile.Match(Path.GetFileName(path.Trim()));

        if (match.Success)
        {
            changedAdrNumbers.Add(match.Groups["number"].Value);
        }
    }

    List<string> allowed = [];
    List<string> needsAdr = [];

    foreach ((string package, string version) in currentVersions.OrderBy(static e => e.Key, StringComparer.Ordinal))
    {
        bool isNew = !baseVersions.TryGetValue(package, out string? previous);

        if (!isNew && string.Equals(previous, version, StringComparison.Ordinal))
        {
            continue;
        }

        string adr = citedAdr.GetValueOrDefault(package, string.Empty);
        bool adrTouched = adr.Length > 0 && changedAdrNumbers.Contains(adr);
        string adrLabel = adr.Length > 0 ? adr : "(none)";

        if (isNew)
        {
            if (adrTouched)
            {
                allowed.Add($"{package} {version} — new, and ADR {adr} changed with it");
            }
            else
            {
                needsAdr.Add($"{package} {version} — a new package, citing ADR {adrLabel}, which did not change");
            }

            continue;
        }

        BumpKind kind = Classify(previous!, version);

        if (kind is BumpKind.PatchOrMinor)
        {
            allowed.Add($"{package} {previous} -> {version} — patch or minor bump under ADR {adr}");
        }
        else if (adrTouched)
        {
            allowed.Add($"{package} {previous} -> {version} — major, and ADR {adr} changed with it");
        }
        else
        {
            string why = kind is BumpKind.Unparseable
                ? "a version this gate cannot parse, so it is treated as major"
                : "a major bump";

            needsAdr.Add($"{package} {previous} -> {version} — {why}, citing ADR {adrLabel}, which did not change");
        }
    }

    foreach (string line in allowed)
    {
        Console.WriteLine($"ok  {line}");
    }

    if (allowed.Count == 0 && needsAdr.Count == 0)
    {
        Console.WriteLine($"ok  no package version changed against {baseRef}");
    }

    if (needsAdr.Count > 0)
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: {needsAdr.Count} change(s) need an ADR entry, not just a citation.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("ADR 0009's amendment lets a patch or minor bump of an already-cited package");
        Console.Error.WriteLine("through, because it is maintenance. A major bump or a new package is a");
        Console.Error.WriteLine("decision, and the ADR that admits it has to say so — so the cited ADR must");
        Console.Error.WriteLine("change in the same diff, whether that is a new ADR or a dated amendment to");
        Console.Error.WriteLine("an existing one.");
        Console.Error.WriteLine();

        foreach (string line in needsAdr)
        {
            Console.Error.WriteLine($"  {line}");
        }
    }
}

return failed ? 1 : 0;

// --- what was resolved: helpers (ADR 0224) -----------------------------------

// An ImplicitPackage pattern: exact, or a prefix ending in `*`.
static bool Matches(string pattern, string id) =>
    pattern.EndsWith('*')
        ? id.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : string.Equals(pattern, id, StringComparison.OrdinalIgnoreCase);

static IEnumerable<string> WorkflowFiles(string root, bool isRepository)
{
    string workflows = Path.Combine(root, ".github", "workflows");

    if (Directory.Exists(workflows))
    {
        foreach (string file in Directory.EnumerateFiles(workflows).Where(static f => f.EndsWith(".yml", StringComparison.Ordinal) || f.EndsWith(".yaml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            yield return file;
        }
    }

    foreach (string file in Directory.EnumerateFiles(root, "action.y*ml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        if (!Restores.IsExcluded(root, file, isRepository))
        {
            yield return file;
        }
    }
}

// Licences a runtime package may carry: what ships to a consumer (ADR 0224
// point 4). Build and test packages may also carry another OSI-approved
// licence, because nothing of theirs is shipped.
static HashSet<string> RuntimeLicences() =>
    new(["MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "MPL-2.0"], StringComparer.Ordinal);

static HashSet<string> BuildAndTestLicences()
{
    HashSet<string> licences = RuntimeLicences();

    // OSI-approved, by SPDX identifier (https://opensource.org/licenses).
    licences.UnionWith([
        "0BSD", "AFL-3.0", "AGPL-3.0-only", "AGPL-3.0-or-later", "Artistic-2.0", "BSD-1-Clause", "BSL-1.0",
        "CDDL-1.0", "EPL-1.0", "EPL-2.0", "EUPL-1.2", "GPL-2.0-only", "GPL-2.0-or-later", "GPL-3.0-only",
        "GPL-3.0-or-later", "ISC", "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later",
        "MPL-1.1", "MS-PL", "MS-RL", "NCSA", "OFL-1.1", "PostgreSQL", "Python-2.0", "UPL-1.0", "Unlicense", "Zlib",
    ]);

    return licences;
}

static int CheckPackages(XDocument register, HashSet<string> knownAdrs, string scanRoot, string repositoryRoot, bool isRepository, List<string> lockfiles)
{
    // Shipped: the packable members of Varve.slnx (ADR 0009's amendment of
    // 2026-09-22), or, for a fixture, every packable project under it.
    HashSet<string> shipped = new(ShippedProjects(scanRoot, isRepository).Select(Path.GetFullPath), StringComparer.Ordinal);

    // The restore graph beside each lockfile. A shipped project's is required:
    // without it nothing here is known about what ships. Another project's may
    // be absent where this run did not restore it (the browser apps need the
    // WebAssembly workloads), and it is named, so the gap is visible.
    List<RestoreGraph> graphs = [];
    List<string> unrestored = [];

    foreach (string lockfile in lockfiles)
    {
        string directory = Path.GetDirectoryName(lockfile)!;
        string assets = Path.Combine(directory, "obj", "project.assets.json");

        if (File.Exists(assets))
        {
            graphs.Add(Restores.ReadGraph(assets));
            continue;
        }

        bool isShippedProject = Directory.EnumerateFiles(directory, "*.csproj").Any(p => shipped.Contains(Path.GetFullPath(p)));

        if (isShippedProject)
        {
            Console.Error.WriteLine($"dependency-register: {Relative(scanRoot, lockfile)} belongs to a shipped project and has no restore graph at {Relative(scanRoot, assets)}. Run 'dotnet restore' first.");
            return 2;
        }

        unrestored.Add(Relative(scanRoot, directory).Replace('\\', '/'));
    }

    if (unrestored.Count > 0)
    {
        Console.WriteLine($"note  {unrestored.Count} project(s) not restored in this run, so their packages' licences were not read here: {string.Join(", ", unrestored)}");
    }
    Dictionary<string, string> runtime = new(StringComparer.OrdinalIgnoreCase);
    SortedDictionary<string, (string Id, string Version, string Folder)> all = new(StringComparer.OrdinalIgnoreCase);

    foreach (RestoreGraph graph in graphs)
    {
        bool isShipped = shipped.Contains(Path.GetFullPath(graph.ProjectPath));
        string project = Relative(scanRoot, graph.ProjectPath).Replace('\\', '/');

        foreach ((string target, IReadOnlyDictionary<string, AssetLibrary> libraries) in graph.Targets)
        {
            foreach (AssetLibrary library in libraries.Values.Where(static l => l.Type == "package"))
            {
                all.TryAdd($"{library.Id}/{library.Version}", (library.Id, library.Version, graph.PackagesFolder));
            }

            if (!isShipped)
            {
                continue;
            }

            foreach (AssetLibrary library in Restores.Shipped(graph, target).Where(static l => l.Type == "package"))
            {
                runtime.TryAdd($"{library.Id}/{library.Version}", project);
            }
        }

        if (isShipped)
        {
            foreach (string framework in graph.Direct.Keys)
            {
                foreach (AssetLibrary library in Restores.Dependencies(graph, framework).Where(static l => l.Type == "package"))
                {
                    runtime.TryAdd($"{library.Id}/{library.Version}", project);
                }
            }
        }
    }

    // What the register states for a package whose nuspec carries no licence
    // expression: the licence it declares by file or URL, read and stated; or a
    // named exception, for build and test only.
    List<(string Id, string? Version, string? Licence, string? Declared, string? Exception, string Adr)> statements = [];
    bool failed = false;

    foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "PackageLicence"))
    {
        string id = item.Attribute("Include")?.Value ?? "";
        string adr = (ReadMetadata(item, "Adr") ?? "").Trim();

        if (!knownAdrs.Contains(adr))
        {
            failed = true;
            Console.Error.WriteLine($"FAIL: the licence statement for {id} cites ADR '{adr}', which does not exist.");
        }

        statements.Add((id, ReadMetadata(item, "Version"), ReadMetadata(item, "Licence"), ReadMetadata(item, "Declared"), ReadMetadata(item, "Exception"), adr));
    }

    HashSet<string> runtimeAllowed = RuntimeLicences();
    HashSet<string> otherAllowed = BuildAndTestLicences();
    List<string> licenceFindings = [];
    HashSet<int> usedStatements = [];
    int checkedCount = 0;

    foreach ((string key, (string id, string version, string folder)) in all)
    {
        Nuspec? nuspec = Restores.ReadNuspec(folder, id, version);

        if (nuspec is null)
        {
            Console.Error.WriteLine($"dependency-register: {id} {version} is resolved but not in the packages folder {folder}. Restore first.");
            return 2;
        }

        checkedCount++;
        bool isRuntime = runtime.TryGetValue(key, out string? via);
        string scope = isRuntime ? $"runtime, through {via}" : "build or test";
        string declared = nuspec.LicenceExpression ?? nuspec.LicenceFile ?? nuspec.LicenceUrl ?? "(nothing)";
        int index = statements.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase) && string.Equals(s.Version, version, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = statements.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase) && s.Version is null);
        }

        string? expression = nuspec.LicenceExpression;

        if (index >= 0)
        {
            usedStatements.Add(index);
            var statement = statements[index];

            if (expression is not null)
            {
                licenceFindings.Add($"  {id} {version} declares '{expression}' itself; the register's statement for it is no longer needed. Remove it.");
                continue;
            }

            if (statement.Declared is not null && !string.Equals(statement.Declared, declared, StringComparison.Ordinal))
            {
                licenceFindings.Add($"  {id} {version} declares '{declared}', and the register's statement was made for '{statement.Declared}'. Read the new one, and state it again.");
                continue;
            }

            if (statement.Exception is not null)
            {
                if (isRuntime)
                {
                    licenceFindings.Add($"  {id} {version} ({scope}) is a {statement.Exception}-only exception under ADR {statement.Adr}, and it is in a shipped closure");
                }
                else
                {
                    Console.WriteLine($"exception  {id} {version} declares '{declared}': {statement.Exception}-only, under ADR {statement.Adr}");
                }

                continue;
            }

            expression = statement.Licence;
        }

        if (expression is null)
        {
            licenceFindings.Add($"  {id} {version} ({scope}) declares no licence expression, only '{declared}'. Read it, and state it with a PackageLicence in the register.");
            continue;
        }

        HashSet<string> allowed = isRuntime ? runtimeAllowed : otherAllowed;

        if (!Restores.LicenceAlternatives(expression).Any(alternative => alternative.All(allowed.Contains)))
        {
            licenceFindings.Add($"  {id} {version} ({scope}) is '{expression}', which is not on the {(isRuntime ? "runtime" : "build and test")} allow-list");
        }
    }

    // A statement nothing resolves is stale, but only a run that read every
    // lockfile's graph can say so: the package may be in a project this run
    // did not restore.
    for (int i = 0; i < statements.Count; i++)
    {
        if (usedStatements.Contains(i))
        {
            continue;
        }

        string statement = $"{statements[i].Id}{(statements[i].Version is null ? "" : " " + statements[i].Version)}";

        if (unrestored.Count == 0)
        {
            licenceFindings.Add($"  the register states a licence for {statement}, which nothing resolves. Remove it.");
        }
        else
        {
            Console.WriteLine($"note  the licence statement for {statement} matched nothing restored in this run");
        }
    }

    if (licenceFindings.Count > 0)
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: {licenceFindings.Count} licence finding(s) (ADR 0224 point 4). Runtime: {string.Join(", ", runtimeAllowed.Order(StringComparer.Ordinal))}; build and test may also use another OSI-approved licence:");

        foreach (string line in licenceFindings)
        {
            Console.Error.WriteLine(line);
        }
    }
    else
    {
        Console.WriteLine($"ok  {checkedCount} package(s), {runtime.Count} of them runtime, each under a licence its scope allows");
    }

    // Native assets in a shipped closure (constraint 1, as ADR 0009's
    // amendment scopes it). A NativeAssetException names a project that may
    // carry them, outside the three hosts constraint 1 is about.
    Dictionary<string, (string Adr, string Reason)> exceptions = new(StringComparer.Ordinal);

    foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "NativeAssetException"))
    {
        string project = (item.Attribute("Include")?.Value ?? "").Replace('\\', '/');
        string adr = (ReadMetadata(item, "Adr") ?? "").Trim();

        if (!knownAdrs.Contains(adr))
        {
            failed = true;
            Console.Error.WriteLine($"FAIL: the native-asset exception for {project} cites ADR '{adr}', which does not exist.");
        }

        exceptions[project] = (adr, ReadMetadata(item, "Reason") ?? "");
    }

    HashSet<string> usedExceptions = new(StringComparer.Ordinal);

    foreach (RestoreGraph graph in graphs.Where(g => shipped.Contains(Path.GetFullPath(g.ProjectPath))).OrderBy(static g => g.ProjectPath, StringComparer.Ordinal))
    {
        string project = Relative(scanRoot, graph.ProjectPath).Replace('\\', '/');
        List<(string Package, List<string> Files)> natives = NativePackages(graph);

        if (natives.Count == 0)
        {
            Console.WriteLine($"ok  {project}: no native asset");
            continue;
        }

        if (exceptions.TryGetValue(project, out (string Adr, string Reason) exception))
        {
            usedExceptions.Add(project);
            Console.WriteLine($"allowed  {project}, ADR {exception.Adr}: {exception.Reason}");

            foreach ((string package, List<string> files) in natives)
            {
                Console.WriteLine($"  {package}, {files.Count} native file(s)");
            }

            continue;
        }

        failed = true;
        ReportNatives(project, natives);
    }

    foreach (string project in exceptions.Keys.Where(p => !usedExceptions.Contains(p)).Order(StringComparer.Ordinal))
    {
        failed = true;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"FAIL: the register's native-asset exception names {project}, which ships no native asset in a shipped closure. Remove the entry.");
    }

    return failed ? 1 : 0;
}

static List<string> ShippedProjects(string root, bool isRepository)
{
    if (isRepository)
    {
        List<string> projects = [];

        foreach (XElement entry in XDocument.Load(Path.Combine(root, "Varve.slnx")).Descendants("Project"))
        {
            string? relative = entry.Attribute("Path")?.Value;

            if (relative is null)
            {
                continue;
            }

            string project = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(project))
            {
                throw new InvalidOperationException("Varve.slnx names a project that does not exist: " + relative);
            }

            if (IsPackable(project))
            {
                projects.Add(project);
            }
        }

        return projects;
    }

    return [.. Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
        .Where(p => !Restores.IsExcluded(root, p, skipFixtures: false))
        .Where(IsPackable)];
}

static bool IsPackable(string project) =>
    XDocument.Load(project).Descendants().Any(static e => e.Name.LocalName == "IsPackable" && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

static List<(string Package, List<string> Files)> NativePackages(RestoreGraph graph)
{
    SortedDictionary<string, SortedSet<string>> byPackage = new(StringComparer.Ordinal);

    foreach (IReadOnlyDictionary<string, AssetLibrary> libraries in graph.Targets.Values)
    {
        foreach (AssetLibrary library in libraries.Values.Where(static l => l.NativeFiles.Count > 0))
        {
            string key = $"{library.Id}/{library.Version}";

            if (!byPackage.TryGetValue(key, out SortedSet<string>? files))
            {
                files = new SortedSet<string>(StringComparer.Ordinal);
                byPackage[key] = files;
            }

            files.UnionWith(library.NativeFiles);
        }
    }

    return [.. byPackage.Select(static e => (e.Key, e.Value.ToList()))];
}

static void ReportNatives(string project, List<(string Package, List<string> Files)> natives)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"FAIL: {project} has {natives.Count} package(s) shipping native assets. "
        + "Constraint 1 in docs/brief.md, as scoped by ADR 0009's amendment:");

    foreach ((string package, List<string> files) in natives)
    {
        Console.Error.WriteLine($"  {package}");

        foreach (string file in files)
        {
            Console.Error.WriteLine($"    {file}");
        }
    }
}

// Each case is copied to a directory of its own outside the repository, so
// that nothing of the repository's build (its Directory.Build.props, its
// lockfile settings, its analyzers) reaches it, restored if it says so, and
// checked. It must exit 1 and print every line of its expect.txt.
static int RunFixtures(string repositoryRoot, string directory)
{
    int cases = 0;
    List<string> failures = [];

    foreach (string source in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
    {
        string expectPath = Path.Combine(source, "expect.txt");

        if (!File.Exists(expectPath))
        {
            continue;
        }

        cases++;
        string name = Path.GetFileName(source);
        string work = Path.Combine(Path.GetTempPath(), $"varve-register-{name}-{Guid.NewGuid():N}");
        CopyDirectory(source, work);

        try
        {
            string restorePath = Path.Combine(work, "restore.txt");

            if (File.Exists(restorePath))
            {
                foreach (string project in File.ReadAllLines(restorePath).Select(static l => l.Trim()).Where(static l => l.Length > 0))
                {
                    (int restoreExit, string restoreOutput) = Run(work, "dotnet", ["restore", project, "-p:RestorePackagesWithLockFile=true"]);

                    if (restoreExit != 0)
                    {
                        failures.Add($"{name}: restoring {project} failed:{Environment.NewLine}{restoreOutput}");
                    }
                }
            }

            string argsPath = Path.Combine(work, "args.txt");
            List<string> gateArguments = ["run", Path.Combine(repositoryRoot, "eng", "dependency-register.cs"), "--", Path.Combine(work, "Directory.Packages.props"), "--adr-dir", Path.Combine(repositoryRoot, "docs", "adr")];

            if (File.Exists(argsPath))
            {
                gateArguments.AddRange(File.ReadAllText(argsPath).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }

            (int exit, string output) = Run(repositoryRoot, "dotnet", gateArguments);
            List<string> absent = [.. File.ReadAllLines(expectPath).Where(static l => l.Length > 0).Where(l => !output.Contains(l, StringComparison.Ordinal))];

            if (exit != 1 || absent.Count > 0)
            {
                failures.Add($"{name}: exit {exit}, expected 1{(absent.Count == 0 ? "" : "; missing: " + string.Join(" | ", absent))}{Environment.NewLine}{output}");
            }
            else
            {
                Console.WriteLine($"ok  {name}: fails, for its own reason");
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    if (cases == 0)
    {
        Console.Error.WriteLine($"dependency-register: no case with an expect.txt under {directory}.");
        return 2;
    }

    foreach (string failure in failures)
    {
        Console.Error.WriteLine("FAIL: " + failure);
    }

    Console.WriteLine($"{cases - failures.Count} of {cases} fixture(s) fail as they should");
    return failures.Count == 0 ? 0 : 1;
}

static void CopyDirectory(string source, string destination)
{
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(source, file);

        if (relative.Split(Path.DirectorySeparatorChar).Any(static s => s is "bin" or "obj"))
        {
            continue;
        }

        string target = Path.Combine(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
}

static (int ExitCode, string Output) Run(string workingDirectory, string fileName, IEnumerable<string> arguments)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();

    return (process.ExitCode, stdout.Result + Environment.NewLine + stderr.Result);
}

// --- helpers ---------------------------------------------------------------

// MSBuild accepts metadata as an attribute or as a child element, and a
// contributor may reasonably write either.
static string? ReadMetadata(XElement item, string name) =>
    item.Attribute(name)?.Value
    ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

static string BuildSummary(List<(string Package, string Adr)> entries, int findings)
{
    StringBuilder builder = new();
    builder.AppendLine("## Dependency register");
    builder.AppendLine();
    builder.AppendLine("| Package | Admitted by |");
    builder.AppendLine("|---|---|");

    foreach ((string package, string adr) in entries.OrderBy(static e => e.Package, StringComparer.Ordinal))
    {
        builder.AppendLine($"| `{package}` | ADR {adr} |");
    }

    builder.AppendLine();
    builder.AppendLine(findings == 0
        ? $"{entries.Count} package(s), each naming the decision that admits it."
        : $"{entries.Count} package(s) conformant, {findings} finding(s).");

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

static string Relative(string root, string path) =>
    path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

static Dictionary<string, string> ReadVersions(XDocument document)
{
    Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);

    foreach (XElement item in document.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
    {
        string? package = item.Attribute("Include")?.Value;
        string? version = item.Attribute("Version")?.Value
            ?? item.Elements().FirstOrDefault(static e => e.Name.LocalName == "Version")?.Value;

        if (!string.IsNullOrWhiteSpace(package) && !string.IsNullOrWhiteSpace(version))
        {
            versions[package] = version.Trim();
        }
    }

    return versions;
}

// A NuGet version is major.minor.patch with an optional prerelease and build
// metadata. Only the major matters here, so the parse stops as soon as it has
// it, and anything that does not start with digits.digits is not classified.
static BumpKind Classify(string previous, string current)
{
    if (!TryReadMajor(previous, out int before) || !TryReadMajor(current, out int after))
    {
        return BumpKind.Unparseable;
    }

    // A downgrade is not maintenance either: something is being pinned back,
    // and the reason belongs in the ADR that admits the package.
    return after == before ? BumpKind.PatchOrMinor : BumpKind.Major;
}

static bool TryReadMajor(string version, out int major)
{
    major = 0;

    int dot = version.IndexOf('.');
    string head = dot < 0 ? version : version[..dot];

    return int.TryParse(head, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major);
}

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

enum BumpKind
{
    PatchOrMinor,
    Major,
    Unparseable,
}
