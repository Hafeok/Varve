// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The winget manifests of a release (ADR 0226 point 4).
//
//   dotnet run eng/winget.cs -- --version <v> --tag <tag> --assets <dir> --out <dir>
//       [--repository <owner/name>]
//
// Writes the three files of a multi-file manifest, schema 1.10.0, for the
// package Hafeok.Varve at <version>:
//
//   Hafeok.Varve.yaml                 the version manifest
//   Hafeok.Varve.installer.yaml       one installer per Windows RID: the zip
//                                     the release carries, its SHA-256, and
//                                     the portable varve.exe inside it
//   Hafeok.Varve.locale.en-US.yaml    the default locale
//
// The installers' URLs are the release's download URLs, so the manifests are
// what `wingetcreate submit` sends to microsoft/winget-pkgs once the release
// exists. Nothing here submits: the maintainer does, signed in interactively
// (ADR 0226 point 4), because a GitHub App installation token cannot open a
// pull request on a repository outside its installation.
//
// The SHA-256 of each zip is computed from <assets>, where the release's
// archives are, never taken from elsewhere. Every Windows RID the CLI ships
// (VarveNativeRuntimeIdentifiers in Directory.Build.props) must have its zip
// there, or nothing is written.
//
// Exit codes: 0 written, 1 a zip is missing, 2 could not run.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

string? version = null, tag = null, assets = null, output = null;
string repository = "Hafeok/Varve";

for (int i = 0; i < args.Length; i++)
{
    string? next = i + 1 < args.Length ? args[i + 1] : null;

    switch (args[i])
    {
        case "--version" when next is not null: version = next; i++; break;
        case "--tag" when next is not null: tag = next; i++; break;
        case "--assets" when next is not null: assets = next; i++; break;
        case "--out" when next is not null: output = next; i++; break;
        case "--repository" when next is not null: repository = next; i++; break;
        default:
            Console.Error.WriteLine($"winget: unknown argument '{args[i]}'. See the header of eng/winget.cs.");
            return 2;
    }
}

if (version is null || tag is null || assets is null || output is null)
{
    Console.Error.WriteLine("usage: dotnet run eng/winget.cs -- --version <v> --tag <tag> --assets <dir> --out <dir>");
    return 2;
}

string root = FindRoot();
Match rids = Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")), "<VarveNativeRuntimeIdentifiers>([^<]*)</VarveNativeRuntimeIdentifiers>");

if (!rids.Success)
{
    Console.Error.WriteLine("winget: Directory.Build.props has no VarveNativeRuntimeIdentifiers.");
    return 2;
}

// winget's architecture names for the Windows RIDs.
Dictionary<string, string> architectures = new(StringComparer.Ordinal) { ["win-x64"] = "x64", ["win-arm64"] = "arm64", ["win-x86"] = "x86" };
List<string> windows = [.. rids.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(static rid => rid.StartsWith("win-", StringComparison.Ordinal))];

StringBuilder installers = new();
bool missing = false;

foreach (string rid in windows)
{
    if (!architectures.TryGetValue(rid, out string? architecture))
    {
        Console.Error.WriteLine($"winget: no winget architecture for {rid}.");
        return 2;
    }

    string asset = $"varve-{version}-{rid}.zip";
    string path = Path.Combine(assets, asset);

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"FAIL: {asset} is not in {assets}; the release carries one zip per Windows RID (ADR 0226).");
        missing = true;
        continue;
    }

    string sha256;

    using (FileStream stream = File.OpenRead(path))
    {
        sha256 = Convert.ToHexString(SHA256.HashData(stream));
    }

    installers.Append($"""
      - Architecture: {architecture}
        InstallerUrl: https://github.com/{repository}/releases/download/{tag}/{asset}
        InstallerSha256: {sha256}

    """);
}

if (missing)
{
    return 1;
}

const string Identifier = "Hafeok.Varve";
const string Schema = "1.10.0";
string header = $"# Written by eng/winget.cs for {tag} (ADR 0226). Submit with: wingetcreate submit <this directory>\n";

Directory.CreateDirectory(output);

Write($"{Identifier}.yaml", $"""
    # yaml-language-server: $schema=https://aka.ms/winget-manifest.version.{Schema}.schema.json
    PackageIdentifier: {Identifier}
    PackageVersion: {version}
    DefaultLocale: en-US
    ManifestType: version
    ManifestVersion: {Schema}

    """);

Write($"{Identifier}.installer.yaml", $"""
    # yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.{Schema}.schema.json
    PackageIdentifier: {Identifier}
    PackageVersion: {version}
    InstallerType: zip
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: varve.exe
        PortableCommandAlias: varve
    Commands:
      - varve
    Installers:
    {installers}ManifestType: installer
    ManifestVersion: {Schema}

    """);

Write($"{Identifier}.locale.en-US.yaml", $"""
    # yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.{Schema}.schema.json
    PackageIdentifier: {Identifier}
    PackageVersion: {version}
    PackageLocale: en-US
    Publisher: decision-driven-design
    PublisherUrl: https://github.com/{repository}
    PackageName: Varve
    PackageUrl: https://github.com/{repository}
    License: MPL-2.0
    LicenseUrl: https://github.com/{repository}/blob/{tag}/LICENSE
    ShortDescription: An event-sourced RDF store and SPARQL 1.1/1.2 engine, as a server and a command line.
    Moniker: varve
    Tags:
      - rdf
      - sparql
      - shacl
      - triplestore
    ReleaseNotesUrl: https://github.com/{repository}/releases/tag/{tag}
    ManifestType: defaultLocale
    ManifestVersion: {Schema}

    """);

Console.WriteLine($"ok  winget manifests for {Identifier} {version}, {windows.Count} installer(s), in {output}");
return 0;

void Write(string name, string body) =>
    File.WriteAllText(Path.Combine(output, name), header + body.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

static string FindRoot()
{
    for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("winget: run from inside the repository.");
}
