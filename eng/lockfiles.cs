// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The lockfiles and the SDK that wrote them (ADR 0224 point 5, amended
// 2026-10-28).
//
//   dotnet run eng/lockfiles.cs              regenerate every lockfile
//   dotnet run eng/lockfiles.cs -- --check   were they written with global.json's SDK?
//   dotnet run eng/lockfiles.cs -- --check --lockfiles <dir>
//                                            the same, over another tree's lockfiles:
//                                            how tests/fixtures/lockfiles/ proves it fails
//
// The SDK references two packages by itself, at the version of the runtime it
// bundles: Microsoft.NET.ILLink.Tasks and Microsoft.DotNet.ILCompiler. Every
// lockfile records them, so a lockfile is the SDK patch's as much as the
// register's. global.json pins the patch; CI installs exactly it
// (.github/actions/setup-dotnet); and a lockfile written with another patch
// fails a locked restore with NuGet's NU1004, which does not say why.
//
// --check says why, before the restore does. It reads the version of the
// runtime the SDK in use bundles (BundledNETCoreAppPackageVersion, in the
// SDK's Microsoft.NETCoreSdk.BundledVersions.props) and every lockfile's
// implicit packages, and fails on a difference, naming this script. With an
// SDK other than global.json's it can say nothing about the pinned one: in CI
// that is a failure, since CI installs exactly global.json's; on a
// developer's machine, where global.json rolls forward to the latest patch, it
// is a note.
//
// Regenerating needs global.json's SDK itself, and refuses any other:
//   - it brings the devcontainer's pinned SDK to global.json's version;
//   - it restores, unlocked and re-evaluated, the solution and every project
//     with a lockfile outside it (the browser apps need the WebAssembly
//     workloads, and it says so rather than skipping them);
//   - then it runs --check.
// A Dependabot pull request that moves global.json (the dotnet-sdk ecosystem)
// is completed by running this: its lockfile diff shows ILLink and ILCompiler
// moving with the SDK.
//
// Exit codes: 0 conformant (or regenerated), 1 findings, 2 could not run.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string Script = "dotnet run eng/lockfiles.cs";
string[] implicitPackages = ["Microsoft.NET.ILLink.Tasks", "Microsoft.DotNet.ILCompiler"];

string root = FindRepositoryRoot();
bool check = args.Contains("--check");
int lockfilesAt = Array.IndexOf(args, "--lockfiles");
string scanRoot = lockfilesAt >= 0 && lockfilesAt + 1 < args.Length ? Path.GetFullPath(args[lockfilesAt + 1]) : root;
bool isRepository = string.Equals(scanRoot, root, StringComparison.Ordinal);

string? pinned = PinnedSdk(root);

if (pinned is null)
{
    Console.Error.WriteLine("lockfiles: global.json names no sdk version.");
    return 2;
}

(int versionExit, string running) = Capture(root, "dotnet", "--version");
running = running.Trim();

if (versionExit != 0 || running.Length == 0)
{
    Console.Error.WriteLine("lockfiles: could not ask dotnet for its version.");
    return 2;
}

bool inCi = string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.Ordinal);

if (!string.Equals(running, pinned, StringComparison.Ordinal))
{
    if (check && !inCi)
    {
        Console.WriteLine(
            $"note  the SDK in use is {running}, and global.json pins {pinned}: the lockfiles are global.json's SDK's, "
            + "and this machine cannot check them. CI installs exactly the pinned SDK and checks them there.");
        return 0;
    }

    Console.Error.WriteLine(
        $"lockfiles: the SDK in use is {running}, and global.json pins {pinned}. "
        + (check
            ? "CI installs exactly global.json's SDK through .github/actions/setup-dotnet; a job that set up .NET another way runs a different patch."
            : $"Regenerating writes the SDK's own package versions into every lockfile, so it needs {pinned} itself: install it, and run {Script} again."));
    return check ? 1 : 2;
}

string? bundled = BundledRuntime(root, running);

if (bundled is null)
{
    Console.Error.WriteLine($"lockfiles: could not read the runtime version SDK {running} bundles (Microsoft.NETCoreSdk.BundledVersions.props).");
    return 2;
}

if (!check)
{
    int regenerated = Regenerate(root, pinned);

    if (regenerated != 0)
    {
        return regenerated;
    }
}

// --- the check -----------------------------------------------------------------

SortedDictionary<string, SortedSet<string>> recorded = new(StringComparer.Ordinal);
int lockfiles = 0;

foreach (string path in Directory.EnumerateFiles(scanRoot, "packages.lock.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    string relative = Path.GetRelativePath(scanRoot, path).Replace('\\', '/');

    if ((isRepository && relative.StartsWith("tests/fixtures/", StringComparison.Ordinal)) || relative.Split('/').Any(static s => s is "bin" or "obj" or ".git"))
    {
        continue;
    }

    lockfiles++;
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

    foreach (JsonProperty target in document.RootElement.GetProperty("dependencies").EnumerateObject())
    {
        foreach (JsonProperty package in target.Value.EnumerateObject())
        {
            if (implicitPackages.Contains(package.Name, StringComparer.OrdinalIgnoreCase)
                && package.Value.TryGetProperty("resolved", out JsonElement resolved)
                && resolved.GetString() is string version
                && !string.Equals(version, bundled, StringComparison.OrdinalIgnoreCase))
            {
                string key = $"{package.Name} {version}";

                if (!recorded.TryGetValue(key, out SortedSet<string>? files))
                {
                    files = new SortedSet<string>(StringComparer.Ordinal);
                    recorded[key] = files;
                }

                files.Add(relative);
            }
        }
    }
}

if (recorded.Count == 0)
{
    Console.WriteLine($"ok  {lockfiles} lockfile(s) written with SDK {running}, whose own packages are {bundled}, as global.json pins it");
    return 0;
}

Console.Error.WriteLine($"FAIL: lockfiles written with another SDK patch than global.json's {pinned}, whose own packages are {bundled} (ADR 0224):");

foreach ((string package, SortedSet<string> files) in recorded)
{
    string direction = CompareVersions(package[(package.LastIndexOf(' ') + 1)..], bundled) > 0 ? "a newer" : "an older";
    Console.Error.WriteLine($"  {package}, from {direction} patch, in {files.Count} lockfile(s): {string.Join(", ", files.Take(5))}{(files.Count > 5 ? ", …" : "")}");
}

Console.Error.WriteLine();
Console.Error.WriteLine($"A locked restore fails on these (NU1004). With SDK {pinned} installed, regenerate every lockfile:");
Console.Error.WriteLine($"  {Script}");
return 1;

// --- regenerate ----------------------------------------------------------------------

static int Regenerate(string root, string pinned)
{
    // The devcontainer installs its SDK by version; it follows global.json.
    string devcontainer = Path.Combine(root, ".devcontainer", "devcontainer.json");

    if (File.Exists(devcontainer))
    {
        string text = File.ReadAllText(devcontainer);
        string updated = Regex.Replace(
            text,
            @"(""ghcr\.io/devcontainers/features/dotnet:\d+""\s*:\s*\{\s*""version""\s*:\s*"")[^""]*("")",
            "${1}" + pinned + "${2}",
            RegexOptions.CultureInvariant);

        if (!string.Equals(text, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(devcontainer, updated);
            Console.WriteLine($"ok  .devcontainer/devcontainer.json installs SDK {pinned}");
        }
    }

    (int workloadExit, string workloads) = Capture(root, "dotnet", "workload", "list");

    if (workloadExit != 0 || !workloads.Contains("wasm-tools", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("lockfiles: the browser apps' lockfiles need the WebAssembly workloads: dotnet workload install wasm-tools wasm-experimental, then run this again.");
        return 2;
    }

    List<string> targets = ["Varve.slnx", .. OutsideTheSolution(root)];

    foreach (string target in targets)
    {
        Console.WriteLine($"$ dotnet restore {target} --force-evaluate");
        (int exit, string output) = Capture(root, "dotnet", "restore", target, "--force-evaluate", "-p:RestoreLockedMode=false");

        if (exit != 0)
        {
            Console.Error.WriteLine(output);
            Console.Error.WriteLine($"lockfiles: restoring {target} failed.");
            return 1;
        }
    }

    Console.WriteLine($"ok  {targets.Count} restore(s), every lockfile written with SDK {pinned}");
    return 0;
}

// Every project with a lockfile that Varve.slnx does not restore, and a
// solution of its own where it has one (tools/repo-standard).
static List<string> OutsideTheSolution(string root)
{
    HashSet<string> members = new(XDocument.Load(Path.Combine(root, "Varve.slnx"))
        .Descendants("Project")
        .Select(static e => (e.Attribute("Path")?.Value ?? "").Replace('\\', '/')), StringComparer.Ordinal);

    SortedSet<string> targets = new(StringComparer.Ordinal);

    foreach (string lockfile in Directory.EnumerateFiles(root, "packages.lock.json", SearchOption.AllDirectories))
    {
        string directory = Path.GetRelativePath(root, Path.GetDirectoryName(lockfile)!).Replace('\\', '/');

        if (directory.StartsWith("tests/fixtures/", StringComparison.Ordinal) || directory.Split('/').Any(static s => s is "bin" or "obj"))
        {
            continue;
        }

        string project = Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj")
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Single();

        if (!members.Contains(project))
        {
            targets.Add(project);
        }
    }

    return [.. targets];
}

// --- helpers -------------------------------------------------------------------------

static string? PinnedSdk(string root)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
    return document.RootElement.TryGetProperty("sdk", out JsonElement sdk) && sdk.TryGetProperty("version", out JsonElement version)
        ? version.GetString()
        : null;
}

static string? BundledRuntime(string root, string sdk)
{
    (int exit, string list) = Capture(root, "dotnet", "--list-sdks");

    if (exit != 0)
    {
        return null;
    }

    Match line = Regex.Match(list, "^" + Regex.Escape(sdk) + @" \[(?<dir>[^\]]+)\]\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    if (!line.Success)
    {
        return null;
    }

    string props = Path.Combine(line.Groups["dir"].Value, sdk, "Microsoft.NETCoreSdk.BundledVersions.props");

    if (!File.Exists(props))
    {
        return null;
    }

    return XDocument.Load(props).Descendants().FirstOrDefault(static e => e.Name.LocalName == "BundledNETCoreAppPackageVersion")?.Value.Trim();
}

static int CompareVersions(string left, string right)
{
    int[] a = [.. left.Split('-')[0].Split('.').Select(static p => int.TryParse(p, out int n) ? n : 0)];
    int[] b = [.. right.Split('-')[0].Split('.').Select(static p => int.TryParse(p, out int n) ? n : 0)];

    for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
    {
        int compared = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);

        if (compared != 0)
        {
            return compared;
        }
    }

    return 0;
}

static (int ExitCode, string Output) Capture(string root, string fileName, params string[] arguments)
{
    ProcessStartInfo start = new()
    {
        FileName = fileName,
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {fileName}.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();

    return (process.ExitCode, stdout.Result + stderr.Result);
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
