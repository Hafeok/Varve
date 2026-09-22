// Package metadata gate.
//
//   dotnet run eng/package-metadata.cs -- <directory-of-nupkg>
//
// ADR 0029 fixes what a published Varve package must carry. Every one of those
// values is set once, in Directory.Build.targets, and every one of them can be
// lost silently: a property set outside the IsPackable condition, a file that
// stops being packed, a licence expression that becomes a URL. None of it
// fails a build, and all of it is permanent once a version is on nuget.org,
// because a pushed package cannot be edited or replaced — only unlisted.
//
// So the dry run in the build job packs on every pull request, and this opens
// each .nupkg and reads the nuspec back. It checks the artifact rather than the
// properties that were supposed to produce it.
//
// On licenceUrl: NuGet emits <licenseUrl>https://licenses.nuget.org/...</licenseUrl>
// itself, beside <license type="expression">, for clients older than the
// expression form. That is NuGet's compatibility shim and not something this
// repository set. What is checked is that the expression is there — a package
// whose only licence is a URL is the failure ADR 0029 forbids.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.

using System.IO.Compression;
using System.Xml.Linq;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run eng/package-metadata.cs -- <directory-of-nupkg>");
    return 2;
}

string directory = args[0];

if (!Directory.Exists(directory))
{
    Console.Error.WriteLine($"package-metadata: no directory at '{directory}'.");
    return 2;
}

List<string> packages = [.. Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.AllDirectories)];
packages.Sort(StringComparer.Ordinal);

if (packages.Count == 0)
{
    Console.Error.WriteLine($"package-metadata: no .nupkg under '{directory}'. Did the pack run?");
    return 2;
}

bool failed = false;

foreach (string package in packages)
{
    string name = Path.GetFileName(package);
    List<string> findings = [];

    using ZipArchive archive = ZipFile.OpenRead(package);

    ZipArchiveEntry? nuspecEntry = null;

    foreach (ZipArchiveEntry candidate in archive.Entries)
    {
        if (candidate.FullName.EndsWith(".nuspec", StringComparison.Ordinal)
            && !candidate.FullName.Contains('/', StringComparison.Ordinal))
        {
            nuspecEntry = candidate;
        }
    }

    if (nuspecEntry is null)
    {
        Console.Error.WriteLine($"FAIL {name}: no .nuspec at the package root.");
        failed = true;
        continue;
    }

    XDocument nuspec;

    using (Stream stream = nuspecEntry.Open())
    {
        nuspec = XDocument.Load(stream);
    }

    // The namespace is read off the document rather than pinned. NuGet picks
    // the oldest nuspec schema that can express the package, so Varve.Iri gets
    // 2012/06 and Varve.Rdf gets 2013/05 purely because one has a dependency
    // and the other does not. A gate with a hard-coded namespace would pass or
    // fail on that.
    XNamespace nuspecNs = nuspec.Root?.Name.Namespace ?? XNamespace.None;

    XElement? metadata = nuspec.Root?.Element(nuspecNs + "metadata");

    if (metadata is null)
    {
        Console.Error.WriteLine($"FAIL {name}: the nuspec has no metadata element.");
        failed = true;
        continue;
    }

    Require(findings, metadata, nuspecNs, "authors", "decision-driven-design");
    RequirePresent(findings, metadata, nuspecNs, "description");
    RequirePresent(findings, metadata, nuspecNs, "copyright");
    RequirePresent(findings, metadata, nuspecNs, "tags");
    Require(findings, metadata, nuspecNs, "icon", "icon.png");
    Require(findings, metadata, nuspecNs, "readme", "README.md");
    RequirePresent(findings, metadata, nuspecNs, "projectUrl");

    XElement? license = metadata.Element(nuspecNs + "license");

    if (license is null || license.Attribute("type")?.Value != "expression" || license.Value != "Apache-2.0")
    {
        findings.Add("license must be the SPDX expression Apache-2.0, not a URL or a file");
    }

    if (metadata.Element(nuspecNs + "iconUrl") is not null)
    {
        findings.Add("iconUrl is set; the icon must be embedded, and iconUrl is deprecated");
    }

    XElement? repository = metadata.Element(nuspecNs + "repository");

    if (repository is null
        || string.IsNullOrEmpty(repository.Attribute("url")?.Value)
        || string.IsNullOrEmpty(repository.Attribute("commit")?.Value))
    {
        findings.Add("repository needs a url and a commit; both come from the git remote via SourceLink");
    }

    // The two files the nuspec points at have to be in the package, or a
    // consumer sees a broken icon and no readme on nuget.org.
    RequireFile(findings, archive, "icon.png");
    RequireFile(findings, archive, "README.md");

    if (findings.Count == 0)
    {
        Console.WriteLine($"ok  {name}");
        continue;
    }

    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL {name}:");

    foreach (string finding in findings)
    {
        Console.Error.WriteLine($"  {finding}");
    }
}

return failed ? 1 : 0;

// --- helpers ---------------------------------------------------------------

static void Require(List<string> findings, XElement metadata, XNamespace ns, string element, string expected)
{
    string? actual = metadata.Element(ns + element)?.Value;

    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        findings.Add($"{element} is '{actual ?? "(absent)"}', expected '{expected}'");
    }
}

static void RequirePresent(List<string> findings, XElement metadata, XNamespace ns, string element)
{
    if (string.IsNullOrWhiteSpace(metadata.Element(ns + element)?.Value))
    {
        findings.Add($"{element} is absent or empty");
    }
}

static void RequireFile(List<string> findings, ZipArchive archive, string path)
{
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        if (string.Equals(entry.FullName, path, StringComparison.Ordinal))
        {
            return;
        }
    }

    findings.Add($"the nuspec names {path} but the package does not contain it");
}
