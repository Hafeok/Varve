// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// What a restore resolved (ADR 0224): the lockfiles, the restore graphs, and
// the packages in the global packages folder. Included by
// eng/dependency-register.cs and eng/sbom.cs (`#:include lib/Restores.cs`);
// not a script of its own.
//
// Two closures are read from a project's obj/project.assets.json, because an
// artefact is one of two things:
//
// - **What a consumer of the package gets** (`Dependencies`): the graph from
//   the project's direct dependencies, without the edges a consumer never
//   sees. `PrivateAssets="all"` is recorded as `suppressParent: All`, and
//   every analyzer and build-time package is referenced that way. This is
//   what a library nupkg's nuspec declares.
// - **What is in the build's output** (`Shipped`): every package with an
//   asset that is deployed (runtime, native or resource), in one target.
//   That is what a tool package carries inside it and what a Native AOT
//   binary is compiled from.
//
// The lockfile is what the restore was held to; the assets file is the same
// graph with each package's assets. A component's version and SHA-512 are the
// lockfile's, and eng/sbom.cs checks the two agree.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

sealed record LockedPackage(string Id, string Version, string Type, string? ContentHash, IReadOnlyList<string> Dependencies);

sealed record Lockfile(string Path, IReadOnlyDictionary<string, IReadOnlyList<LockedPackage>> Targets)
{
    public IEnumerable<LockedPackage> Packages => Targets.Values.SelectMany(static t => t).Where(static p => p.Type != "Project");
}

sealed record DirectDependency(string Id, bool Suppressed, bool AutoReferenced);

sealed record AssetLibrary(
    string Id,
    string Version,
    string Type,
    bool Deploys,
    IReadOnlyList<string> NativeFiles,
    IReadOnlyList<string> Dependencies);

sealed record RestoreGraph(
    string Path,
    string ProjectPath,
    string PackagesFolder,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, AssetLibrary>> Targets,
    IReadOnlyDictionary<string, IReadOnlyList<DirectDependency>> Direct,
    IReadOnlyList<(string Name, string Version)> Downloads,
    IReadOnlyList<string> FrameworkReferences,
    IReadOnlyDictionary<string, string> Sha512);

sealed record Nuspec(string Id, string Version, string? LicenceExpression, string? LicenceFile, string? LicenceUrl, string? Authors);

static class Restores
{
    // --- lockfiles ---------------------------------------------------------------

    // Every packages.lock.json under the root. tests/fixtures/ is skipped when the
    // root is the repository's: those projects exist to fail, and each case is
    // run against its own root.
    public static List<string> FindLockfiles(string root, bool skipFixtures) =>
        [.. Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path, skipFixtures))
            .Order(StringComparer.Ordinal)];

    public static bool IsExcluded(string root, string path, bool skipFixtures)
    {
        string relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

        return relative.Split('/').Any(static segment => segment is "bin" or "obj" or ".git" or "node_modules")
            || (skipFixtures && relative.StartsWith("tests/fixtures/", StringComparison.Ordinal));
    }

    public static Lockfile ReadLockfile(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Dictionary<string, IReadOnlyList<LockedPackage>> targets = new(StringComparer.Ordinal);

        foreach (JsonProperty target in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            List<LockedPackage> packages = [];

            foreach (JsonProperty package in target.Value.EnumerateObject())
            {
                JsonElement value = package.Value;
                string type = value.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "";
                string version = value.TryGetProperty("resolved", out JsonElement r) ? r.GetString() ?? "" : "";
                string? hash = value.TryGetProperty("contentHash", out JsonElement h) ? h.GetString() : null;
                List<string> dependencies = value.TryGetProperty("dependencies", out JsonElement d)
                    ? [.. d.EnumerateObject().Select(static e => e.Name)]
                    : [];

                packages.Add(new LockedPackage(package.Name, version, type, hash, dependencies));
            }

            targets[target.Name] = packages;
        }

        return new Lockfile(path, targets);
    }

    // .config/dotnet-tools.json: a local tool is a package the build restores,
    // and it is not in any lockfile.
    public static List<(string Id, string Version)> ReadToolManifest(string root)
    {
        string path = System.IO.Path.Combine(root, ".config", "dotnet-tools.json");

        if (!File.Exists(path))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        List<(string, string)> tools = [];

        if (document.RootElement.TryGetProperty("tools", out JsonElement entries))
        {
            foreach (JsonProperty tool in entries.EnumerateObject())
            {
                tools.Add((tool.Name, tool.Value.GetProperty("version").GetString() ?? ""));
            }
        }

        return tools;
    }

    // --- restore graphs ----------------------------------------------------------

    public static RestoreGraph ReadGraph(string assetsPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement root = document.RootElement;
        JsonElement project = root.GetProperty("project");
        JsonElement restore = project.GetProperty("restore");

        Dictionary<string, IReadOnlyDictionary<string, AssetLibrary>> targets = new(StringComparer.Ordinal);

        foreach (JsonProperty target in root.GetProperty("targets").EnumerateObject())
        {
            Dictionary<string, AssetLibrary> libraries = new(StringComparer.OrdinalIgnoreCase);

            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                (string id, string version) = SplitKey(library.Name);
                string type = library.Value.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "";
                List<string> native = NativeFiles(library.Value);
                bool deploys = native.Count > 0
                    || HasFiles(library.Value, "runtime")
                    || HasFiles(library.Value, "resource")
                    || HasRuntimeTargets(library.Value);
                List<string> dependencies = library.Value.TryGetProperty("dependencies", out JsonElement d)
                    ? [.. d.EnumerateObject().Select(static e => e.Name)]
                    : [];

                libraries[id] = new AssetLibrary(id, version, type, deploys, native, dependencies);
            }

            targets[target.Name] = libraries;
        }

        Dictionary<string, IReadOnlyList<DirectDependency>> direct = new(StringComparer.Ordinal);
        List<(string, string)> downloads = [];
        SortedSet<string> frameworks = new(StringComparer.Ordinal);

        foreach (JsonProperty framework in project.GetProperty("frameworks").EnumerateObject())
        {
            List<DirectDependency> dependencies = [];

            if (framework.Value.TryGetProperty("dependencies", out JsonElement entries))
            {
                foreach (JsonProperty dependency in entries.EnumerateObject())
                {
                    string suppress = dependency.Value.TryGetProperty("suppressParent", out JsonElement s) ? s.GetString() ?? "" : "";
                    bool auto = dependency.Value.TryGetProperty("autoReferenced", out JsonElement a) && a.GetBoolean();
                    dependencies.Add(new DirectDependency(dependency.Name, string.Equals(suppress, "All", StringComparison.Ordinal), auto));
                }
            }

            direct[framework.Name] = dependencies;

            if (framework.Value.TryGetProperty("downloadDependencies", out JsonElement downloaded))
            {
                foreach (JsonElement item in downloaded.EnumerateArray())
                {
                    string range = item.GetProperty("version").GetString() ?? "";
                    downloads.Add((item.GetProperty("name").GetString() ?? "", range.Trim('[', ']', '(', ')').Split(',')[0].Trim()));
                }
            }

            if (framework.Value.TryGetProperty("frameworkReferences", out JsonElement references))
            {
                foreach (JsonProperty reference in references.EnumerateObject())
                {
                    frameworks.Add(reference.Name);
                }
            }
        }

        Dictionary<string, string> sha512 = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty library in root.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.TryGetProperty("sha512", out JsonElement hash))
            {
                sha512[library.Name] = hash.GetString() ?? "";
            }
        }

        return new RestoreGraph(
            assetsPath,
            restore.GetProperty("projectPath").GetString() ?? "",
            restore.TryGetProperty("packagesPath", out JsonElement packagesPath) ? packagesPath.GetString() ?? "" : DefaultPackagesFolder(),
            targets,
            direct,
            downloads,
            [.. frameworks],
            sha512);
    }

    // The graph a consumer of the package resolves: from the direct
    // dependencies whose assets flow to it, through every dependency edge.
    // Project references are kept, as `Varve.*` packages: each is a package
    // the consumer gets.
    public static List<AssetLibrary> Dependencies(RestoreGraph graph, string framework)
    {
        IReadOnlyDictionary<string, AssetLibrary> libraries = graph.Targets[framework];
        Dictionary<string, AssetLibrary> reached = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new();

        foreach (DirectDependency dependency in graph.Direct[framework].Where(static d => !d.Suppressed))
        {
            pending.Push(dependency.Id);
        }

        // Project references are not in project.frameworks.dependencies; they
        // are the target's libraries of type project, and always flow.
        foreach (AssetLibrary library in libraries.Values.Where(static l => l.Type == "project"))
        {
            pending.Push(library.Id);
        }

        while (pending.Count > 0)
        {
            string id = pending.Pop();

            if (reached.ContainsKey(id) || !libraries.TryGetValue(id, out AssetLibrary? library))
            {
                continue;
            }

            reached[id] = library;

            foreach (string next in library.Dependencies)
            {
                pending.Push(next);
            }
        }

        return [.. reached.Values.OrderBy(static l => l.Id, StringComparer.OrdinalIgnoreCase)];
    }

    // Every package that puts a file in the output of one target.
    public static List<AssetLibrary> Shipped(RestoreGraph graph, string target) =>
        [.. graph.Targets[target].Values
            .Where(static l => l.Deploys || l.Type == "project")
            .OrderBy(static l => l.Id, StringComparer.OrdinalIgnoreCase)];

    // --- packages ----------------------------------------------------------------

    public static string DefaultPackagesFolder() =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public static string PackageDirectory(string packagesFolder, string id, string version) =>
        System.IO.Path.Combine(packagesFolder, id.ToLowerInvariant(), version.ToLowerInvariant());

    public static Nuspec? ReadNuspec(string packagesFolder, string id, string version)
    {
        string path = System.IO.Path.Combine(PackageDirectory(packagesFolder, id, version), id.ToLowerInvariant() + ".nuspec");

        if (!File.Exists(path))
        {
            return null;
        }

        XElement metadata = XDocument.Load(path).Root!.Elements().First(static e => e.Name.LocalName == "metadata");
        string? Read(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        XElement? licence = metadata.Elements().FirstOrDefault(static e => e.Name.LocalName == "license");
        string? licenceType = licence?.Attribute("type")?.Value;

        return new Nuspec(
            Read("id") ?? id,
            Read("version") ?? version,
            licenceType == "expression" ? licence!.Value.Trim() : null,
            licenceType == "file" ? licence!.Value.Trim() : null,
            // A package with a license element also carries the deprecated
            // licenseUrl pointing at aka.ms/deprecateLicenseUrl; it is only the
            // declaration when there is no license element.
            licence is null ? Read("licenseUrl") : null,
            Read("authors"));
    }

    public static string? Sha256OfPackage(string packagesFolder, string id, string version)
    {
        string path = System.IO.Path.Combine(PackageDirectory(packagesFolder, id, version), $"{id.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg");

        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    // --- licences ----------------------------------------------------------------

    // The SPDX licence identifiers of an expression. `A OR B` is satisfied by
    // either side, so its alternatives are returned as separate sets; `AND`
    // needs every identifier. `A WITH X` is licence A with exception X, and the
    // exception is not a licence, so it is dropped. Parentheses are flattened,
    // which reads `(A OR B) AND C` as the alternatives A and `B AND C`: looser
    // than SPDX for that shape, which no package in this repository uses.
    public static List<List<string>> LicenceAlternatives(string expression)
    {
        string flat = Regex.Replace(expression, @"\s+WITH\s+[A-Za-z0-9.+-]+", "", RegexOptions.CultureInvariant)
            .Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal);
        List<List<string>> alternatives = [];

        foreach (string alternative in Regex.Split(flat, @"\s+OR\s+", RegexOptions.CultureInvariant))
        {
            List<string> identifiers = [.. Regex.Split(alternative.Trim(), @"\s+AND\s+", RegexOptions.CultureInvariant)
                .Select(static s => s.Trim())
                .Where(static s => s.Length > 0)];

            alternatives.Add(identifiers);
        }

        return alternatives;
    }

    // --- helpers -----------------------------------------------------------------

    public static (string Id, string Version) SplitKey(string key)
    {
        int slash = key.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? (key, "") : (key[..slash], key[(slash + 1)..]);
    }

    private static bool HasFiles(JsonElement library, string group) =>
        library.TryGetProperty(group, out JsonElement files)
        && files.EnumerateObject().Any(static f => !f.Name.EndsWith("_._", StringComparison.Ordinal));

    private static bool HasRuntimeTargets(JsonElement library) =>
        library.TryGetProperty("runtimeTargets", out JsonElement targets)
        && targets.EnumerateObject().Any(static f => !f.Name.EndsWith("_._", StringComparison.Ordinal));

    // The signal is `assetType: "native"` (or a RID-specific restore's native
    // group), never "the path contains /native/": a managed runtimes/win/lib
    // assembly and an MSBuild-time build/native file are not native assets
    // anybody deploys (ADR 0009's amendment of 2026-09-22).
    private static List<string> NativeFiles(JsonElement library)
    {
        SortedSet<string> files = new(StringComparer.Ordinal);

        if (library.TryGetProperty("runtimeTargets", out JsonElement runtimeTargets))
        {
            foreach (JsonProperty asset in runtimeTargets.EnumerateObject())
            {
                if (asset.Value.TryGetProperty("assetType", out JsonElement assetType)
                    && string.Equals(assetType.GetString(), "native", StringComparison.Ordinal)
                    && !asset.Name.EndsWith("/_._", StringComparison.Ordinal))
                {
                    files.Add(asset.Name);
                }
            }
        }

        if (library.TryGetProperty("native", out JsonElement native))
        {
            foreach (JsonProperty asset in native.EnumerateObject())
            {
                if (!asset.Name.EndsWith("_._", StringComparison.Ordinal))
                {
                    files.Add(asset.Name);
                }
            }
        }

        return [.. files];
    }
}
