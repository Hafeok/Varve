// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Dependency register gate.
//
//   dotnet run eng/dependency-register.cs -- [<Directory.Packages.props>] [--adr-dir <path>]
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
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0009-dependency-policy-and-register.md.

using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repositoryRoot = FindRepositoryRoot();

string propsPath = Path.Combine(repositoryRoot, "Directory.Packages.props");
string adrDirectory = Path.Combine(repositoryRoot, "docs", "adr");

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--adr-dir" && i + 1 < arguments.Length)
    {
        adrDirectory = arguments[++i];
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

// Directory.Packages.props is usually written without an MSBuild namespace, but
// tolerate one rather than silently finding nothing and reporting success.
foreach (XElement item in register.Descendants().Where(static e => e.Name.LocalName == "PackageVersion"))
{
    string package = item.Attribute("Include")?.Value ?? "(no Include)";
    string? adr = ReadMetadata(item, "Adr");

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
