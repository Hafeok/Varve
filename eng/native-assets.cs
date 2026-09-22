// Native-asset gate.
//
//   dotnet run eng/native-assets.cs -- [--assets <project.assets.json>]
//
// Constraint 1 in docs/brief.md: 100% managed, and a package that ships a
// native asset is out. ADR 0009's amendment of 2026-09-22 scopes that to
// **shipped artifacts** — the benchmark harness pulls BenchmarkDotNet, which
// pulls Gee.External.Capstone, which ships nine native libraries, and the
// constraint describes Varve rather than the tools that measure it.
//
// Scoping a constraint without enforcing the scoped version leaves it a
// sentence. This walks the restore graph of every packable project **that is a
// member of Varve.slnx** and fails when any package in the closure contributes
// a native runtime asset.
//
// Solution membership is the definition of "shipped" here, and it is not a
// shortcut: the solution is what CI builds and what `dotnet pack` packs. The
// packable fixtures under tests/fixtures/ are deliberately outside it — they
// exist to fail to build — and a gate that demanded a restore graph from a
// project nothing ever restores would fail on a clean clone.
//
// **The signal is `assetType: "native"`**, from a library's `runtimeTargets`
// (or a RID-specific restore's `native` group) in project.assets.json. It is
// not "the path contains /native/": System.Diagnostics.EventLog ships
// runtimes/win/lib/... which is managed, and TraceEvent ships
// build/native/... which is an MSBuild-time file nobody deploys. Both would be
// false positives, and a gate that cries wolf is a gate that gets a NoWarn.
//
// With --assets it checks one file instead of discovering projects, which is
// how the tests exercise both outcomes against real restore graphs rather than
// against a fixture somebody wrote to pass.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0009-dependency-policy-and-register.md.

using System.Text.Json;
using System.Xml.Linq;

string? assetsOverride = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--assets" && i + 1 < args.Length)
    {
        assetsOverride = args[++i];
    }
}

string repositoryRoot = FindRepositoryRoot();
List<(string Project, string Assets)> toCheck = [];

if (assetsOverride is not null)
{
    if (!File.Exists(assetsOverride))
    {
        Console.Error.WriteLine($"native-assets: no assets file at '{assetsOverride}'.");
        return 2;
    }

    toCheck.Add((assetsOverride, assetsOverride));
}
else
{
    foreach (string project in PackableProjects(repositoryRoot))
    {
        string assets = Path.Combine(Path.GetDirectoryName(project) ?? ".", "obj", "project.assets.json");

        if (!File.Exists(assets))
        {
            Console.Error.WriteLine(
                $"native-assets: {Relative(repositoryRoot, project)} has no restore graph at "
                + $"{Relative(repositoryRoot, assets)}. Run 'dotnet restore' first.");
            return 2;
        }

        toCheck.Add((project, assets));
    }

    if (toCheck.Count == 0)
    {
        Console.Error.WriteLine("native-assets: found no packable project. That is a defect in this gate.");
        return 2;
    }
}

bool failed = false;

foreach ((string project, string assets) in toCheck)
{
    List<(string Package, List<string> Files)> findings = Inspect(assets);

    if (findings.Count == 0)
    {
        Console.WriteLine($"ok  {Relative(repositoryRoot, project)}");
        continue;
    }

    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"FAIL: {Relative(repositoryRoot, project)} has {findings.Count} package(s) shipping native assets. "
        + "Constraint 1 in docs/brief.md, as scoped by ADR 0009's amendment:");

    foreach ((string package, List<string> files) in findings)
    {
        Console.Error.WriteLine($"  {package}");

        foreach (string file in files)
        {
            Console.Error.WriteLine($"    {file}");
        }
    }
}

return failed ? 1 : 0;

// --- helpers ---------------------------------------------------------------

static List<(string Package, List<string> Files)> Inspect(string assetsPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));

    SortedDictionary<string, SortedSet<string>> byPackage = new(StringComparer.Ordinal);

    if (!document.RootElement.TryGetProperty("targets", out JsonElement targets))
    {
        return [];
    }

    foreach (JsonProperty target in targets.EnumerateObject())
    {
        foreach (JsonProperty library in target.Value.EnumerateObject())
        {
            Collect(library, byPackage);
        }
    }

    List<(string, List<string>)> findings = [];

    foreach ((string package, SortedSet<string> files) in byPackage)
    {
        findings.Add((package, [.. files]));
    }

    return findings;
}

static void Collect(JsonProperty library, SortedDictionary<string, SortedSet<string>> byPackage)
{
    // The ordinary, RID-agnostic restore records every RID's assets here, each
    // tagged with what it is.
    if (library.Value.TryGetProperty("runtimeTargets", out JsonElement runtimeTargets))
    {
        foreach (JsonProperty asset in runtimeTargets.EnumerateObject())
        {
            if (asset.Value.TryGetProperty("assetType", out JsonElement assetType)
                && string.Equals(assetType.GetString(), "native", StringComparison.Ordinal))
            {
                Add(byPackage, library.Name, asset.Name);
            }
        }
    }

    // A RID-specific restore resolves the same thing into a plain group. '_._'
    // is the placeholder for "this package deliberately has none".
    if (library.Value.TryGetProperty("native", out JsonElement native))
    {
        foreach (JsonProperty asset in native.EnumerateObject())
        {
            if (!asset.Name.EndsWith("_._", StringComparison.Ordinal))
            {
                Add(byPackage, library.Name, asset.Name);
            }
        }
    }
}

static void Add(SortedDictionary<string, SortedSet<string>> byPackage, string package, string file)
{
    if (!byPackage.TryGetValue(package, out SortedSet<string>? files))
    {
        files = new SortedSet<string>(StringComparer.Ordinal);
        byPackage[package] = files;
    }

    files.Add(file);
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

        if (!File.Exists(project))
        {
            throw new InvalidOperationException("Varve.slnx names a project that does not exist: " + relative);
        }

        foreach (XElement property in XDocument.Load(project).Descendants("IsPackable"))
        {
            if (string.Equals(property.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                projects.Add(project);
                break;
            }
        }
    }

    projects.Sort(StringComparer.Ordinal);
    return projects;
}

static string Relative(string root, string path) =>
    path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

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
