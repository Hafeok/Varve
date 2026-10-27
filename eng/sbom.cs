// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The SBOM generator (ADR 0225, under ADR 0224).
//
//   dotnet run eng/sbom.cs -- --version <v> --out <dir>
//       [--created <yyyy-mm-dd | yyyy-mm-ddThh:mm:ssZ>]
//       [--syft <syft> | --image <platform>=<syft-spdx.json>...]  [--binary <rid>]...
//       [--check-against <dir>] [--descriptor <releases/v….yaml>]
//
// One SPDX 2.3 JSON document per shipped artefact, written from the restore
// that built it and never by hand:
//
//   nupkg/<Id>.spdx.json          each packable library: what a consumer of
//                                 the package resolves (Restores.Dependencies)
//   tool/<Id>.spdx.json           a PackAsTool package: what it carries inside
//                                 it (Restores.Shipped, the framework target)
//   binary/varve-<v>-<rid>.spdx.json
//                                 a Native AOT binary: what it is compiled
//                                 from, and the .NET runtime it contains
//   image/varve.spdx.json         the container: each platform's binary, and
//                                 the base image's OS packages as Syft read
//                                 them from the base pinned by digest. With
//                                 --syft, the base is the final stage's FROM
//                                 in src/Varve.Server/container/Dockerfile,
//                                 scanned for linux/amd64 and linux/arm64;
//                                 --image gives scans made elsewhere
//   sbom.txt                      the descriptor's `sbom:` lines (ADR 0102,
//                                 amended), one per document, sorted
//
// **Deterministic.** The same commit, version and creation time give the
// same bytes on every OS: entries are sorted, the namespace is a UUIDv5 of
// the document's content, line endings are LF, and nothing that varies from
// run to run is written. `created` is --created, else SOURCE_DATE_EPOCH, and
// at a release it is the descriptor's date at 00:00Z (ADR 0224 point 2): the
// commit that lands a descriptor does not exist when its digests are written,
// so neither its time nor its id is in an SBOM. The build provenance
// attestation carries the commit.
//
// **From the locked restore.** Every component's version and SHA-512 are the
// project's packages.lock.json entry; a disagreement with the restore graph
// fails, and so does a project with no lockfile.
//
// --check-against <dir> generates as usual, then requires every file to be
// byte-identical to the one of the same name in <dir>: that is the
// determinism proof CI runs. --descriptor checks a descriptor's `sbom:` lines
// against sbom.txt.
//
// Exit codes: 0 written (and matching), 1 findings, 2 could not run.
//
// The register's PackageLicence statements are read for a component whose
// nuspec carries no expression, so its SBOM says the licence the register
// states (ADR 0224 point 4).
//
// CA2266 is off for this file only, for the reason eng/dco.cs gives.
#:include lib/Restores.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string Supplier = "Organization: decision-driven-design";
const string VarveLicence = "MPL-2.0";
const string ImageName = "ghcr.io/hafeok/varve";
const string Framework = "net10.0";

string repositoryRoot = FindRepositoryRoot();
string? version = null;
string? outDirectory = null;
string? created = null;
string? checkAgainst = null;
string? descriptorPath = null;
string? syft = null;
List<(string Platform, string Path)> images = [];
List<string> binaries = [];

for (int i = 0; i < args.Length; i++)
{
    string? next = i + 1 < args.Length ? args[i + 1] : null;

    switch (args[i])
    {
        case "--version" when next is not null: version = next.TrimStart('v'); i++; break;
        case "--out" when next is not null: outDirectory = Path.GetFullPath(next); i++; break;
        case "--created" when next is not null: created = next; i++; break;
        case "--check-against" when next is not null: checkAgainst = Path.GetFullPath(next); i++; break;
        case "--descriptor" when next is not null: descriptorPath = Path.GetFullPath(next); i++; break;
        case "--binary" when next is not null: binaries.Add(next); i++; break;
        case "--syft" when next is not null: syft = next; i++; break;
        case "--image" when next is not null && next.Contains('=', StringComparison.Ordinal):
            images.Add((next[..next.IndexOf('=', StringComparison.Ordinal)], Path.GetFullPath(next[(next.IndexOf('=', StringComparison.Ordinal) + 1)..])));
            i++;
            break;
        default:
            Console.Error.WriteLine($"sbom: unknown argument '{args[i]}'. See the header of eng/sbom.cs.");
            return 2;
    }
}

if (version is null || outDirectory is null)
{
    Console.Error.WriteLine("sbom: --version and --out are required.");
    return 2;
}

string timestamp;
try
{
    timestamp = CreationTime(created);
}
catch (FormatException exception)
{
    Console.Error.WriteLine($"sbom: {exception.Message}");
    return 2;
}

if (syft is not null)
{
    if (ScanBase(repositoryRoot, syft, images) is string problem)
    {
        Console.Error.WriteLine($"sbom: {problem}");
        return 2;
    }
}

Dictionary<string, string> statedLicences = ReadStatedLicences(Path.Combine(repositoryRoot, "Directory.Packages.props"));
List<string> findings = [];
SortedDictionary<string, string> lines = new(StringComparer.Ordinal);
SortedDictionary<string, byte[]> documents = new(StringComparer.Ordinal);

// --- the packages and the tool ------------------------------------------------

foreach (string project in PackableProjects(repositoryRoot))
{
    string id = Path.GetFileNameWithoutExtension(project);
    (RestoreGraph graph, Lockfile lockfile)? restored = Load(project, findings);

    if (restored is not { } r)
    {
        continue;
    }

    bool tool = IsTool(project);
    List<AssetLibrary> components = tool ? Restores.Shipped(r.graph, Framework) : Restores.Dependencies(r.graph, Framework);
    Root root = new(id, version, tool ? "APPLICATION" : "LIBRARY", NugetPurl(id, version), NugetDownload(id, version), null);

    JsonObject document = Document(
        name: $"{id} {version}",
        kind: tool ? "tool" : "nupkg",
        root: root,
        components: Components(components, r.graph, r.lockfile, Framework, findings),
        relationships: tool ? "CONTAINS" : "DEPENDS_ON",
        direct: DirectIds(r.graph, Framework),
        runtime: []);

    Add(tool ? "tool" : "nupkg", id, $"{(tool ? "tool" : "nupkg")}/{id}.spdx.json", document);
}

// --- the binaries ------------------------------------------------------------

string server = Path.Combine(repositoryRoot, "src", "Varve.Server", "Varve.Server.csproj");

foreach (string rid in binaries.Order(StringComparer.Ordinal))
{
    if (Load(server, findings) is not { } r)
    {
        continue;
    }

    string asset = BinaryAsset(version, rid);
    JsonObject document = BinaryDocument(asset, rid, r.graph, r.lockfile);
    Add("binary", asset, $"binary/{asset}.spdx.json", document);
}

// --- the image -----------------------------------------------------------------

if (images.Count > 0 && Load(server, findings) is { } image)
{
    JsonObject document = ImageDocument(image.graph, image.lockfile, images);
    Add("image", ImageName, "image/varve.spdx.json", document);
}

if (findings.Count > 0)
{
    foreach (string finding in findings)
    {
        Console.Error.WriteLine("FAIL: " + finding);
    }

    return 1;
}

// --- write, and check -----------------------------------------------------------

StringBuilder sbomLines = new();
foreach (string line in lines.Values)
{
    sbomLines.Append("  - ").Append(line).Append('\n');
}

documents["sbom.txt"] = Encoding.UTF8.GetBytes(sbomLines.ToString());

foreach ((string relative, byte[] bytes) in documents)
{
    string path = Path.Combine(outDirectory, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
}

Console.WriteLine($"sbom: {documents.Count - 1} SBOM(s) in {outDirectory}, created {timestamp}");
Console.Write(sbomLines.ToString());

bool failed = false;

if (checkAgainst is not null)
{
    foreach ((string relative, byte[] bytes) in documents)
    {
        string other = Path.Combine(checkAgainst, relative);

        if (!File.Exists(other))
        {
            failed = true;
            Console.Error.WriteLine($"FAIL: {relative} has no counterpart in {checkAgainst}");
        }
        else if (!File.ReadAllBytes(other).AsSpan().SequenceEqual(bytes))
        {
            failed = true;
            Console.Error.WriteLine($"FAIL: {relative} differs from {other}: the generator is not deterministic, or its inputs changed");
        }
    }

    if (!failed)
    {
        Console.WriteLine($"ok  every SBOM is byte-identical to {checkAgainst}");
    }
}

if (descriptorPath is not null)
{
    failed |= !CheckDescriptor(descriptorPath, lines);
}

return failed ? 1 : 0;

// --- documents -----------------------------------------------------------------

void Add(string kind, string name, string relative, JsonObject document)
{
    byte[] bytes = Serialise(document);
    documents[relative] = bytes;
    lines[$"{kind}: {name}"] = $"{kind}: {name} sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";
}

JsonObject BinaryDocument(string asset, string rid, RestoreGraph graph, Lockfile lockfile)
{
    string target = $"{Framework}/{rid}";

    if (!graph.Targets.ContainsKey(target))
    {
        findings.Add($"src/Varve.Server has no restore target {target}; its RuntimeIdentifiers must name {rid}");
        return new JsonObject();
    }

    Root root = new("varve", version!, "APPLICATION", null, "NOASSERTION", asset);

    return Document(
        name: $"{asset}",
        kind: "binary",
        root: root,
        components: Components(Restores.Shipped(graph, target), graph, lockfile, target, findings),
        relationships: "CONTAINS",
        direct: DirectIds(graph, Framework),
        runtime: Runtime(graph, rid));
}

// The image is the binary of each platform on the chiseled base. The base's
// packages are Syft's, read from the base image pinned by digest, and kept as
// Syft wrote them (their licence references included) under an identifier
// prefixed with the platform, since both platforms' documents use the same
// ones. What varies between Syft runs, its namespace and creation time and its
// file entries, is not carried.
JsonObject ImageDocument(RestoreGraph graph, Lockfile lockfile, List<(string Platform, string Path)> bases)
{
    Root root = new(ImageName, version!, "CONTAINER", $"pkg:oci/varve@{version}?repository_url=ghcr.io/hafeok/varve", "NOASSERTION", null);

    JsonObject document = Document(
        name: $"{ImageName} {version}",
        kind: "image",
        root: root,
        components: [],
        relationships: "CONTAINS",
        direct: [],
        runtime: []);

    JsonArray packages = document["packages"]!.AsArray();
    JsonArray relationships = document["relationships"]!.AsArray();
    SortedDictionary<string, JsonNode> extracted = new(StringComparer.Ordinal);
    List<JsonNode> addedPackages = [];
    List<JsonNode> addedRelationships = [];

    foreach ((string platform, string path) in bases.OrderBy(static b => b.Platform, StringComparer.Ordinal))
    {
        string rid = platform switch
        {
            "linux/amd64" => "linux-x64",
            "linux/arm64" => "linux-arm64",
            _ => throw new InvalidOperationException($"no RID for platform {platform}"),
        };

        string prefix = "SPDXRef-" + Sanitise(rid) + "-";

        // The binary for this platform, its closure and the runtime in it.
        JsonObject binary = BinaryDocument(BinaryAsset(version!, rid), rid, graph, lockfile);
        if (binary["packages"] is not JsonArray binaryPackages)
        {
            continue;
        }

        foreach (JsonNode? package in binaryPackages)
        {
            JsonObject copy = package!.DeepClone().AsObject();
            copy["SPDXID"] = prefix + copy["SPDXID"]!.GetValue<string>()["SPDXRef-".Length..];
            addedPackages.Add(copy);
        }

        foreach (JsonNode? relationship in binary["relationships"]!.AsArray())
        {
            JsonObject copy = relationship!.DeepClone().AsObject();

            if (copy["spdxElementId"]!.GetValue<string>() == "SPDXRef-DOCUMENT")
            {
                // The binary's own DESCRIBES becomes the image containing it.
                copy["spdxElementId"] = "SPDXRef-Root";
                copy["relationshipType"] = "CONTAINS";
            }
            else
            {
                copy["spdxElementId"] = prefix + copy["spdxElementId"]!.GetValue<string>()["SPDXRef-".Length..];
            }

            copy["relatedSpdxElement"] = prefix + copy["relatedSpdxElement"]!.GetValue<string>()["SPDXRef-".Length..];
            addedRelationships.Add(copy);
        }

        // The base image, from Syft.
        JsonObject syft = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        HashSet<string> kept = new(StringComparer.Ordinal);

        foreach (JsonNode? package in syft["packages"]!.AsArray())
        {
            JsonObject copy = package!.DeepClone().AsObject();
            string id = copy["SPDXID"]!.GetValue<string>();
            kept.Add(id);
            copy["SPDXID"] = prefix + "base-" + id["SPDXRef-".Length..];
            addedPackages.Add(copy);

            if (copy["primaryPackagePurpose"]?.GetValue<string>() == "CONTAINER")
            {
                addedRelationships.Add(Relationship("SPDXRef-Root", "DESCENDANT_OF", copy["SPDXID"]!.GetValue<string>()));
            }
        }

        foreach (JsonNode? relationship in syft["relationships"]?.AsArray() ?? [])
        {
            string from = relationship!["spdxElementId"]!.GetValue<string>();
            string to = relationship["relatedSpdxElement"]!.GetValue<string>();

            if (kept.Contains(from) && kept.Contains(to))
            {
                addedRelationships.Add(Relationship(prefix + "base-" + from["SPDXRef-".Length..], relationship["relationshipType"]!.GetValue<string>(), prefix + "base-" + to["SPDXRef-".Length..]));
            }
        }

        foreach (JsonNode? licence in syft["hasExtractedLicensingInfos"]?.AsArray() ?? [])
        {
            extracted[licence!["licenseId"]!.GetValue<string>()] = licence.DeepClone();
        }
    }

    foreach (JsonNode package in addedPackages.OrderBy(static p => p["SPDXID"]!.GetValue<string>(), StringComparer.Ordinal))
    {
        packages.Add(package);
    }

    foreach (JsonNode relationship in addedRelationships.OrderBy(RelationshipKey, StringComparer.Ordinal))
    {
        relationships.Add(relationship);
    }

    if (extracted.Count > 0)
    {
        document["hasExtractedLicensingInfos"] = new JsonArray([.. extracted.Values]);
    }

    document["documentNamespace"] = Namespace("image", ImageName, document);
    return document;
}

JsonObject Document(string name, string kind, Root root, List<JsonObject> components, string relationships, List<string> direct, List<JsonObject> runtime)
{
    JsonObject rootPackage = new()
    {
        ["name"] = root.Name,
        ["SPDXID"] = "SPDXRef-Root",
        ["versionInfo"] = root.Version,
        ["supplier"] = Supplier,
        ["downloadLocation"] = root.Download,
        ["filesAnalyzed"] = false,
        ["licenseConcluded"] = VarveLicence,
        ["licenseDeclared"] = VarveLicence,
        ["copyrightText"] = "Copyright (c) 2026 Emil Okkels Klein",
        ["primaryPackagePurpose"] = root.Purpose,
    };

    if (root.FileName is not null)
    {
        rootPackage["packageFileName"] = root.FileName;
    }

    if (root.Purl is not null)
    {
        rootPackage["externalRefs"] = new JsonArray(PurlRef(root.Purl));
    }

    JsonArray packages = [rootPackage];
    foreach (JsonObject component in components.Concat(runtime).OrderBy(static c => c["SPDXID"]!.GetValue<string>(), StringComparer.Ordinal))
    {
        packages.Add((JsonNode)component);
    }

    HashSet<string> present = [.. components.Select(static c => c["SPDXID"]!.GetValue<string>())];
    List<JsonObject> edges = [Relationship("SPDXRef-DOCUMENT", "DESCRIBES", "SPDXRef-Root")];

    if (relationships == "CONTAINS")
    {
        edges.AddRange(components.Concat(runtime).Select(static c => Relationship("SPDXRef-Root", "CONTAINS", c["SPDXID"]!.GetValue<string>())));
    }
    else
    {
        edges.AddRange(direct.Select(ComponentId).Where(present.Contains).Select(static id => Relationship("SPDXRef-Root", "DEPENDS_ON", id)));
    }

    foreach (JsonObject component in components)
    {
        foreach (JsonNode? dependency in component["__dependencies"]?.AsArray() ?? [])
        {
            string to = ComponentId(dependency!.GetValue<string>());

            if (present.Contains(to))
            {
                edges.Add(Relationship(component["SPDXID"]!.GetValue<string>(), "DEPENDS_ON", to));
            }
        }

        component.Remove("__dependencies");
    }

    JsonObject document = new()
    {
        ["spdxVersion"] = "SPDX-2.3",
        ["dataLicense"] = "CC0-1.0",
        ["SPDXID"] = "SPDXRef-DOCUMENT",
        ["name"] = name,
        ["documentNamespace"] = "",
        ["creationInfo"] = new JsonObject
        {
            ["created"] = timestamp,
            ["creators"] = new JsonArray("Organization: decision-driven-design", "Tool: Varve eng/sbom.cs (ADR 0225)"),
        },
        ["packages"] = packages,
        ["relationships"] = new JsonArray([.. edges.DistinctBy(RelationshipKey).OrderBy(RelationshipKey, StringComparer.Ordinal)]),
    };

    document["documentNamespace"] = Namespace(kind, root.Name, document);
    return document;
}

List<JsonObject> Components(List<AssetLibrary> libraries, RestoreGraph graph, Lockfile lockfile, string target, List<string> problems)
{
    Dictionary<string, LockedPackage> locked = new(StringComparer.OrdinalIgnoreCase);

    foreach (string key in new[] { Framework, target }.Distinct(StringComparer.Ordinal))
    {
        if (lockfile.Targets.TryGetValue(key, out IReadOnlyList<LockedPackage>? packages))
        {
            foreach (LockedPackage package in packages)
            {
                locked[package.Id] = package;
            }
        }
    }

    List<JsonObject> components = [];

    foreach (AssetLibrary library in libraries)
    {
        JsonArray dependencies = [.. library.Dependencies.Select(static d => (JsonNode)JsonValue.Create(d)!)];

        if (library.Type == "project")
        {
            // A Varve package: the same release, by its package URL. No checksum:
            // it is built by this release, and its bytes are its own attestation's.
            components.Add(new JsonObject
            {
                ["name"] = library.Id,
                ["SPDXID"] = ComponentId(library.Id),
                ["versionInfo"] = version,
                ["supplier"] = Supplier,
                ["downloadLocation"] = NugetDownload(library.Id, version!),
                ["filesAnalyzed"] = false,
                ["licenseConcluded"] = VarveLicence,
                ["licenseDeclared"] = VarveLicence,
                ["copyrightText"] = "NOASSERTION",
                ["externalRefs"] = new JsonArray(PurlRef(NugetPurl(library.Id, version!))),
                ["primaryPackagePurpose"] = "LIBRARY",
                ["__dependencies"] = dependencies,
            });
            continue;
        }

        if (!locked.TryGetValue(library.Id, out LockedPackage? entry))
        {
            problems.Add($"{Relative(graph.ProjectPath)}: {library.Id} {library.Version} is in the restore graph but not in its lockfile");
            continue;
        }

        if (!string.Equals(entry.Version, library.Version, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{Relative(graph.ProjectPath)}: {library.Id} is {library.Version} in the restore graph and {entry.Version} in its lockfile");
            continue;
        }

        string? sha512 = entry.ContentHash;
        if (!graph.Sha512.TryGetValue($"{library.Id}/{library.Version}", out string? restored) || !string.Equals(restored, sha512, StringComparison.Ordinal))
        {
            problems.Add($"{Relative(graph.ProjectPath)}: {library.Id} {library.Version}'s SHA-512 differs between the restore graph and its lockfile");
            continue;
        }

        Nuspec? nuspec = Restores.ReadNuspec(graph.PackagesFolder, library.Id, library.Version);
        string? sha256 = Restores.Sha256OfPackage(graph.PackagesFolder, library.Id, library.Version);

        if (nuspec is null || sha256 is null)
        {
            problems.Add($"{library.Id} {library.Version} is not in the packages folder {graph.PackagesFolder}; restore first");
            continue;
        }

        string declared = nuspec.LicenceExpression ?? "NOASSERTION";
        string concluded = nuspec.LicenceExpression
            ?? statedLicences.GetValueOrDefault($"{library.Id}/{library.Version}")
            ?? statedLicences.GetValueOrDefault(library.Id)
            ?? "NOASSERTION";

        JsonObject component = new()
        {
            ["name"] = nuspec.Id,
            ["SPDXID"] = ComponentId(library.Id),
            ["versionInfo"] = library.Version,
            ["supplier"] = string.IsNullOrWhiteSpace(nuspec.Authors) ? "NOASSERTION" : "Organization: " + nuspec.Authors,
            ["downloadLocation"] = NugetDownload(library.Id, library.Version),
            ["filesAnalyzed"] = false,
            ["checksums"] = new JsonArray(
                new JsonObject { ["algorithm"] = "SHA256", ["checksumValue"] = sha256 },
                new JsonObject { ["algorithm"] = "SHA512", ["checksumValue"] = Convert.ToHexStringLower(Convert.FromBase64String(sha512!)) }),
            ["licenseConcluded"] = concluded,
            ["licenseDeclared"] = declared,
            ["copyrightText"] = "NOASSERTION",
            ["externalRefs"] = new JsonArray(PurlRef(NugetPurl(nuspec.Id, library.Version))),
            ["primaryPackagePurpose"] = "LIBRARY",
            ["__dependencies"] = dependencies,
        };

        if (library.NativeFiles.Count > 0)
        {
            // Allowed only where ADR 0117 allows it; the register gate enforces
            // that. The SBOM says so rather than hiding it.
            component["comment"] = "Carries native assets: " + string.Join(", ", library.NativeFiles);
        }

        components.Add(component);
    }

    return components;
}

// The .NET runtime, where it is shipped: the Native AOT runtime pack and the
// ASP.NET Core one, at the version the restore downloaded for the RID.
List<JsonObject> Runtime(RestoreGraph graph, string rid) =>
    [.. graph.Downloads
        .Where(d => d.Name == $"Microsoft.NETCore.App.Runtime.NativeAOT.{rid}" || d.Name == $"Microsoft.AspNetCore.App.Runtime.{rid}")
        .OrderBy(static d => d.Name, StringComparer.Ordinal)
        .Select(d => new JsonObject
        {
            ["name"] = d.Name,
            ["SPDXID"] = ComponentId(d.Name),
            ["versionInfo"] = d.Version,
            ["supplier"] = "Organization: Microsoft",
            ["downloadLocation"] = NugetDownload(d.Name, d.Version),
            ["filesAnalyzed"] = false,
            ["licenseConcluded"] = "MIT",
            ["licenseDeclared"] = "MIT",
            ["copyrightText"] = "NOASSERTION",
            ["externalRefs"] = new JsonArray(PurlRef(NugetPurl(d.Name, d.Version))),
            ["primaryPackagePurpose"] = "FRAMEWORK",
            ["comment"] = "The .NET runtime, compiled into the binary.",
        })];

(RestoreGraph graph, Lockfile lockfile)? Load(string project, List<string> problems)
{
    string directory = Path.GetDirectoryName(project)!;
    string assets = Path.Combine(directory, "obj", "project.assets.json");
    string lockPath = Path.Combine(directory, "packages.lock.json");

    if (!File.Exists(lockPath))
    {
        problems.Add($"{Relative(project)} has no packages.lock.json; an SBOM is generated from a locked restore (ADR 0224)");
        return null;
    }

    if (!File.Exists(assets))
    {
        problems.Add($"{Relative(project)} has no restore graph at {Relative(assets)}; run 'dotnet restore' first");
        return null;
    }

    return (Restores.ReadGraph(assets), Restores.ReadLockfile(lockPath));
}

static List<string> DirectIds(RestoreGraph graph, string framework)
{
    List<string> ids = [.. graph.Direct[framework].Where(static d => !d.Suppressed).Select(static d => d.Id)];
    ids.AddRange(graph.Targets[framework].Values.Where(static l => l.Type == "project").Select(static l => l.Id));
    return ids;
}

// --- the descriptor ----------------------------------------------------------------

bool CheckDescriptor(string path, SortedDictionary<string, string> generated)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"FAIL: no descriptor at {path}");
        return false;
    }

    Regex line = new(@"^[ ]{2}-[ ](?<kind>[a-z]+):[ ]+(?<name>\S+)[ ]+(?<digest>sha256:[0-9a-f]{64})[ ]*$", RegexOptions.CultureInvariant);
    Dictionary<string, string> declared = new(StringComparer.Ordinal);
    bool inList = false;

    foreach (string text in File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n'))
    {
        if (text == "sbom:")
        {
            inList = true;
            continue;
        }

        if (inList && text.StartsWith("  - ", StringComparison.Ordinal))
        {
            Match match = line.Match(text);
            if (match.Success)
            {
                declared[$"{match.Groups["kind"].Value}: {match.Groups["name"].Value}"] = text[4..].Trim();
            }

            continue;
        }

        inList = false;
    }

    bool ok = true;

    foreach ((string key, string expected) in generated)
    {
        if (!declared.TryGetValue(key, out string? actual))
        {
            ok = false;
            Console.Error.WriteLine($"FAIL: {Relative(path)} has no sbom line for {key}; generated: {expected}");
        }
        else if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            ok = false;
            Console.Error.WriteLine($"FAIL: {Relative(path)} says '{actual}', generated '{expected}'");
        }
    }

    if (ok)
    {
        Console.WriteLine($"ok  {Relative(path)}'s sbom lines match the {generated.Count} generated SBOM(s)");
    }

    return ok;
}

// --- the base image -------------------------------------------------------------------

// The final stage's base, pinned by digest (ADR 0111, amended 2026-10-27),
// scanned by Syft once per platform the image is built for.
static string? ScanBase(string root, string syft, List<(string Platform, string Path)> images)
{
    string dockerfile = Path.Combine(root, "src", "Varve.Server", "container", "Dockerfile");
    string? from = File.ReadAllLines(dockerfile).LastOrDefault(static l => l.StartsWith("FROM ", StringComparison.Ordinal));
    Match pinned = from is null ? Match.Empty : Regex.Match(from, @"^FROM\s+(?<repository>[^\s:@]+(?:/[^\s:@]+)*)(?::[^\s@]+)?@(?<digest>sha256:[0-9a-f]{64})(?:\s|$)", RegexOptions.CultureInvariant);

    if (!pinned.Success)
    {
        return $"the final stage of {dockerfile} is not FROM <image>@sha256:<digest>; the base is pinned by digest (ADR 0111, amended)";
    }

    string reference = $"{pinned.Groups["repository"].Value}@{pinned.Groups["digest"].Value}";
    string directory = Path.Combine(Path.GetTempPath(), "varve-sbom-base-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    foreach (string platform in (string[])["linux/amd64", "linux/arm64"])
    {
        string output = Path.Combine(directory, platform.Replace('/', '-') + ".spdx.json");
        System.Diagnostics.ProcessStartInfo start = new() { FileName = syft, UseShellExecute = false, RedirectStandardError = true };

        foreach (string argument in (string[])["scan", "registry:" + reference, "--platform", platform, "-q", "-o", "spdx-json=" + output])
        {
            start.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("could not start " + syft);
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            return $"{syft} could not scan {reference} for {platform}: {error.Trim()}";
        }

        images.Add((platform, output));
    }

    Console.WriteLine($"sbom: the base, {reference}, scanned for linux/amd64 and linux/arm64");
    return null;
}

// --- helpers -------------------------------------------------------------------------

static string CreationTime(string? created)
{
    if (created is not null)
    {
        if (DateTime.TryParseExact(created, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime date))
        {
            return date.ToString("yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParseExact(created, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime instant))
        {
            return instant.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        throw new FormatException($"--created '{created}' is neither yyyy-mm-dd nor yyyy-mm-ddThh:mm:ssZ");
    }

    string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");

    if (epoch is not null && long.TryParse(epoch, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    throw new FormatException("no creation time: pass --created, or set SOURCE_DATE_EPOCH (the commit's time, `git log -1 --format=%ct`)");
}

static byte[] Serialise(JsonObject document)
{
    using MemoryStream stream = new();
    using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }))
    {
        document.WriteTo(writer);
    }

    stream.WriteByte((byte)'\n');
    return stream.ToArray();
}

// A UUIDv5 (RFC 9562 §5.5) of the document's content, without the namespace
// itself: the same content gives the same namespace, and different content a
// different one, which is what SPDX asks of it.
static string Namespace(string kind, string name, JsonObject document)
{
    JsonObject copy = document.DeepClone().AsObject();
    copy.Remove("documentNamespace");
    byte[] content = Serialise(copy);

    byte[] urlNamespace = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8").ToByteArray(bigEndian: true);
    byte[] input = [.. urlNamespace, .. content];
    byte[] hash = SHA1.HashData(input);
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
    Guid uuid = new(hash.AsSpan(0, 16), bigEndian: true);

    return $"https://github.com/Hafeok/Varve/spdx/{kind}/{Sanitise(name)}-{uuid}";
}

static JsonObject Relationship(string from, string type, string to) => new()
{
    ["spdxElementId"] = from,
    ["relatedSpdxElement"] = to,
    ["relationshipType"] = type,
};

static string RelationshipKey(JsonNode relationship) =>
    $"{relationship["spdxElementId"]!.GetValue<string>()} {relationship["relationshipType"]!.GetValue<string>()} {relationship["relatedSpdxElement"]!.GetValue<string>()}";

static JsonObject PurlRef(string purl) => new()
{
    ["referenceCategory"] = "PACKAGE-MANAGER",
    ["referenceType"] = "purl",
    ["referenceLocator"] = purl,
};

static string ComponentId(string id) => "SPDXRef-Package-nuget-" + Sanitise(id);

static string Sanitise(string value) => Regex.Replace(value, "[^A-Za-z0-9.-]", "-");

static string NugetPurl(string id, string version) => $"pkg:nuget/{id}@{version}";

static string NugetDownload(string id, string version) =>
    $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/{version.ToLowerInvariant()}/{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";

// The release asset of the CLI for one RID (ADR 0226).
static string BinaryAsset(string version, string rid) =>
    $"varve-{version}-{rid}{(rid.StartsWith("win", StringComparison.Ordinal) ? ".zip" : ".tar.gz")}";

static bool IsTool(string project) =>
    XDocument.Load(project).Descendants().Any(static e => e.Name.LocalName == "PackAsTool" && string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

// <PackageLicence Include="Fractions" Licence="BSD-2-Clause" … />, keyed by id,
// or by id/version when the statement names one.
static Dictionary<string, string> ReadStatedLicences(string props)
{
    Dictionary<string, string> stated = new(StringComparer.OrdinalIgnoreCase);

    foreach (XElement item in XDocument.Load(props).Descendants().Where(static e => e.Name.LocalName == "PackageLicence"))
    {
        string? id = item.Attribute("Include")?.Value;
        string? licence = item.Attribute("Licence")?.Value;
        string? itemVersion = item.Attribute("Version")?.Value;

        if (id is not null && licence is not null)
        {
            stated[itemVersion is null ? id : $"{id}/{itemVersion}"] = licence;
        }
    }

    return stated;
}

static List<string> PackableProjects(string repositoryRoot)
{
    List<string> projects = [];

    foreach (XElement entry in XDocument.Load(Path.Combine(repositoryRoot, "Varve.slnx")).Descendants("Project"))
    {
        string? relative = entry.Attribute("Path")?.Value;

        if (relative is null)
        {
            continue;
        }

        string project = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        if (XDocument.Load(project).Descendants("IsPackable").Any(static p => string.Equals(p.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            projects.Add(project);
        }
    }

    projects.Sort(StringComparer.Ordinal);
    return projects;
}

string Relative(string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

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

sealed record Root(string Name, string Version, string Purpose, string? Purl, string Download, string? FileName);
