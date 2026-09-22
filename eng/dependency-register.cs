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
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0009-dependency-policy-and-register.md.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repositoryRoot = FindRepositoryRoot();

string propsPath = Path.Combine(repositoryRoot, "Directory.Packages.props");
string adrDirectory = Path.Combine(repositoryRoot, "docs", "adr");

string? baseRef = null;

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
    else if (!arguments[i].StartsWith("--", StringComparison.Ordinal))
    {
        propsPath = arguments[i];
    }
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
