// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The public API freeze gate (ADR 0227, which amends ADR 0035; #129).
//
//   dotnet run eng/api-freeze.cs -- [--baseline <tag>] [--package <id>]...
//   dotnet run eng/api-freeze.cs -- --fixtures tests/fixtures/api-freeze
//   dotnet run eng/api-freeze.cs -- --test-only [--configuration <c>] [--report <file>]
//
// Every library package is compared with its own published nupkg at the
// newest release tag that is an ancestor of HEAD, by the SDK's package
// validation (ApiCompat in Microsoft.NET.Sdk, no package of its own). Each
// package is packed alone, with VarveApiFreezeProject naming it:
// Directory.Build.props turns validation on for that project only, because a
// baseline version given as a global property reaches every project it
// references, whose restore then asks nuget.org for a version that package
// never shipped (NU1102). The findings are written as a suppression file,
// never thrown, and read here: `CP0001` a type removed, `CP0002` a member
// removed, and the rest of ApiCompat's diagnostics, each with the member's
// documentation id.
//
// Before 1.0 (the baseline's major is 0) every finding is reported, and a
// finding fails only when nothing marks it breaking (ADR 0227 point 3, the
// maintainer's F1 on #119):
//
//   - a commit since the baseline tag that touches src/<package>/, whose
//     subject is `type(scope)!: …` or which has a `BREAKING CHANGE:` footer;
//   - or a proposed descriptor (a releases/<version>.yaml whose tag does not
//     exist yet) whose summary has a paragraph or list item naming the package
//     and saying `(breaking)`.
//
// A marking commit covers every finding in its package over the range: which
// member a commit broke is not in the commit. From 1.0, every finding fails
// unless a proposed descriptor's major is above the baseline's. A package
// that has no published baseline is reported as new, and a package that packs
// as a .NET tool has no library surface to compare: both are named, never
// skipped silently.
//
// --fixtures runs the cases in tests/fixtures/api-freeze/cases.txt against
// the fixture package's three surfaces, with a baseline packed from its v1
// surface into a temporary directory, so they need no network.
//
// --test-only reads the built assemblies (dotnet build Varve.slnx first) with
// System.Reflection.Metadata, which is in the shared framework, and lists
// every public type and member of every library package that a test assembly
// references and no other assembly does. It reports; it never fails.
//
// Exit codes: 0 conformant, 1 an unmarked breaking change, 2 could not run.
//
// See docs/adr/0227-the-public-api-freeze-gate.md.

// The descriptor reader is shared with eng/release.cs. CA2266 asks a file
// with directives to begin with a shebang, and the licence-header gate
// requires the notice on line 1 (ADR 0031); the notice wins, and CA2266 is
// off for this file only, as in eng/release-pending.cs.
#:include lib/Releases.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string root = FindRepositoryRoot();

string? baselineArgument = null;
string? fixtures = null;
bool testOnly = false;
string configuration = "Release";
string? report = null;
List<string> only = [];

for (int i = 0; i < args.Length; i++)
{
    string? next = i + 1 < args.Length ? args[i + 1] : null;

    switch (args[i])
    {
        case "--baseline" when next is not null: baselineArgument = next; i++; break;
        case "--package" when next is not null: only.Add(next); i++; break;
        case "--fixtures" when next is not null: fixtures = Path.GetFullPath(next); i++; break;
        case "--test-only": testOnly = true; break;
        case "--configuration" when next is not null: configuration = next; i++; break;
        case "--report" when next is not null: report = Path.GetFullPath(next); i++; break;
        default:
            Console.Error.WriteLine($"api-freeze: unknown argument '{args[i]}'. See the header of eng/api-freeze.cs.");
            return 2;
    }
}

try
{
    if (fixtures is not null)
    {
        return Fixtures.Run(root, fixtures);
    }

    if (testOnly)
    {
        return TestOnly.Run(root, configuration, report);
    }

    return await Gate.RunAsync(root, baselineArgument, only);
}
catch (CouldNotRun e)
{
    Console.Error.WriteLine($"api-freeze: {e.Message}");
    return 2;
}

static string FindRepositoryRoot()
{
    for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("api-freeze: run from inside the repository.");
}

sealed class CouldNotRun(string message) : Exception(message);

// One ApiCompat finding: its diagnostic, the documentation id of the type or
// member, and the assembly it was found in.
sealed record Finding(string Id, string Target, string Assembly);

// A commit since the baseline, as the gate reads it: what it touches and its
// whole message.
sealed record Commit(string Sha, IReadOnlyList<string> Paths, string Message);

// What the gate decided for one package.
sealed record Verdict(string Package, string State, IReadOnlyList<Finding> Findings, IReadOnlyList<string> Marks, bool Fails, string Reason);

static class Rule
{
    private static readonly Regex BangSubject = new(@"^[a-z]+(\([^)]*\))?!:", RegexOptions.CultureInvariant);

    private static readonly Regex BreakingFooter = new(@"^BREAKING[ -]CHANGE:", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    // ApiCompat's diagnostics are all breaking by construction: it reports a
    // removal, a narrowing, or a change a caller can observe at compile or
    // bind time (https://learn.microsoft.com/dotnet/fundamentals/apicompat/diagnostic-ids).
    public static Verdict Judge(
        string package,
        string sourceDirectory,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Commit> commits,
        IReadOnlyList<Descriptor> proposed,
        string baselineVersion)
    {
        if (findings.Count == 0)
        {
            return new Verdict(package, "unchanged or grown", findings, [], false, "no breaking change");
        }

        List<string> marks = [];

        foreach (Commit commit in commits)
        {
            bool touches = commit.Paths.Any(path => path.StartsWith(sourceDirectory, StringComparison.Ordinal));
            string subject = commit.Message.Split('\n', 2)[0].Trim();

            if (touches && (BangSubject.IsMatch(subject) || BreakingFooter.IsMatch(commit.Message)))
            {
                marks.Add($"commit {commit.Sha[..Math.Min(12, commit.Sha.Length)]} ({subject})");
            }
        }

        foreach (Descriptor descriptor in proposed)
        {
            if (Names(descriptor.Summary ?? "", package))
            {
                marks.Add($"descriptor {descriptor.Version} (\"(breaking)\" names {package})");
            }
        }

        int baselineMajor = Major(baselineVersion);

        if (baselineMajor >= 1)
        {
            int? proposedMajor = proposed.Select(static d => d.Version).Where(static v => v is not null).Select(static v => (int?)Major(v!)).Max();

            return proposedMajor > baselineMajor
                ? new Verdict(package, "breaking", findings, marks, false, $"the major moves from {baselineMajor} to {proposedMajor}")
                : new Verdict(package, "breaking", findings, marks, true, $"from 1.0 a breaking change needs a new major; the baseline's is {baselineMajor} and no proposed release moves it");
        }

        return marks.Count > 0
            ? new Verdict(package, "breaking", findings, marks, false, "marked breaking")
            : new Verdict(package, "breaking", findings, marks, true, $"no commit since the baseline that touches {sourceDirectory} is marked `!` or `BREAKING CHANGE:`, and no proposed descriptor says `(breaking)` naming {package}");
    }

    // A paragraph or list item of the summary that names the package, as a
    // whole name (Varve.Store is not Varve.Store.Azure), and says (breaking).
    public static bool Names(string summary, string package)
    {
        Regex name = new($@"(?<![\w.]){Regex.Escape(package)}(?![\w]|\.\w)", RegexOptions.CultureInvariant);
        List<string> blocks = [];
        StringBuilder block = new();

        foreach (string line in summary.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();
            bool item = trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);

            if (trimmed.Length == 0 || item)
            {
                blocks.Add(block.ToString());
                block.Clear();
            }

            block.Append(line).Append('\n');
        }

        blocks.Add(block.ToString());
        return blocks.Any(b => name.IsMatch(b) && b.Contains("(breaking)", StringComparison.Ordinal));
    }

    public static int Major(string version)
    {
        Match match = ReleaseFormat.Version.Match(version.StartsWith('v') ? version : "v" + version);
        return match.Success ? int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture) : throw new CouldNotRun($"'{version}' is not a release version");
    }

    public static IReadOnlyList<Finding> ReadFindings(string path)
    {
        if (!File.Exists(path))
        {
            // ApiCompat writes the file only when it has something to say.
            return [];
        }

        XNamespace none = XNamespace.None;
        return [.. XDocument.Load(path).Root!.Elements(none + "Suppression").Select(static s => new Finding(
            (string?)s.Element("DiagnosticId") ?? "?",
            (string?)s.Element("Target") ?? "?",
            (string?)s.Element("Left") ?? (string?)s.Element("Right") ?? "?"))];
    }

    // The commits file of a fixture case: records separated by a line `---`,
    // each beginning `touches: <path> <path>…`, then the message.
    public static IReadOnlyList<Commit> ReadCommits(string path)
    {
        List<Commit> commits = [];
        int n = 0;

        foreach (string record in File.ReadAllText(path).ReplaceLineEndings("\n").Split("\n---\n"))
        {
            string[] lines = record.Trim('\n').Split('\n');

            if (lines.Length == 0 || !lines[0].StartsWith("touches:", StringComparison.Ordinal))
            {
                continue;
            }

            string[] paths = lines[0]["touches:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new Commit($"fixture{++n:D2}", paths, string.Join('\n', lines.Skip(1)).Trim('\n')));
        }

        return commits;
    }
}

static class Gate
{
    public static async Task<int> RunAsync(string root, string? baselineArgument, IReadOnlyList<string> only)
    {
        string? baseline = baselineArgument ?? NewestReleaseTag(root);

        if (baseline is null)
        {
            Console.WriteLine("ok  no release tag is an ancestor of HEAD, so nothing has been published to compare with");
            return 0;
        }

        string bare = ReleaseFormat.Bare(baseline);
        List<(string Name, string Directory, bool Tool)> packages = Packages(root);

        if (only.Count > 0)
        {
            packages = [.. packages.Where(p => only.Contains(p.Name, StringComparer.Ordinal))];
        }

        IReadOnlyList<Commit> commits = Commits(root, baseline);
        IReadOnlyList<Descriptor> proposed = Proposed(root);
        string work = Path.Combine(root, "artifacts", "api-freeze");

        if (Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }

        Directory.CreateDirectory(work);

        Console.WriteLine($"note baseline {baseline}; {commits.Count} commit(s) since it; proposed descriptor(s): {(proposed.Count == 0 ? "none" : string.Join(", ", proposed.Select(static d => d.Version)))}");

        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
        List<Verdict> verdicts = [];

        foreach ((string name, string directory, bool tool) in packages)
        {
            if (tool)
            {
                verdicts.Add(new Verdict(name, "not a library", [], [], false, "a .NET tool has no lib/ surface; its contract is its command line (ADR 0105)"));
                continue;
            }

            if (!await PublishedAsync(http, name, bare))
            {
                verdicts.Add(new Verdict(name, "new", [], [], false, $"not published at {bare}, so there is nothing to compare with"));
                continue;
            }

            string findings = Path.Combine(work, name + ".xml");
            Stopwatch clock = Stopwatch.StartNew();
            (int exit, string output) = Dotnet(root,
                "pack", Path.Combine(directory, name + ".csproj"),
                "--configuration", "Release",
                "--output", Path.Combine(work, "packages"),
                $"-p:VarveApiFreezeProject={name}",
                $"-p:VarveApiFreezeBaseline={bare}",
                $"-p:VarveApiFreezeFindings={findings}");

            if (exit != 0)
            {
                throw new CouldNotRun($"packing {name} against {bare} failed (exit {exit}):\n{Tail(output)}");
            }

            string source = Path.GetRelativePath(root, directory).Replace('\\', '/') + "/";
            Verdict verdict = Rule.Judge(name, source, Rule.ReadFindings(findings), commits, proposed, bare);
            verdicts.Add(verdict);
            Console.WriteLine($"     {name}: {verdict.Findings.Count} finding(s), {clock.Elapsed.TotalSeconds:F0} s");
        }

        return Report(verdicts, baseline);
    }

    public static int Report(IReadOnlyList<Verdict> verdicts, string baseline)
    {
        StringBuilder summary = new();
        summary.AppendLine($"## Public API freeze, against {baseline}");
        summary.AppendLine();
        summary.AppendLine("| Package | State | Findings | Verdict |");
        summary.AppendLine("|---|---|---:|---|");

        foreach (Verdict v in verdicts)
        {
            summary.AppendLine($"| `{v.Package}` | {v.State} | {v.Findings.Count} | {(v.Fails ? "**fails**: " : "")}{v.Reason} |");
        }

        foreach (Verdict v in verdicts.Where(static v => v.Findings.Count > 0))
        {
            summary.AppendLine();
            summary.AppendLine($"### `{v.Package}`");
            summary.AppendLine();

            foreach (Finding f in v.Findings)
            {
                summary.AppendLine($"- `{f.Id}` `{f.Target}` ({f.Assembly})");
            }

            foreach (string mark in v.Marks)
            {
                summary.AppendLine($"- marked by {mark}");
            }
        }

        string text = summary.ToString();
        Console.WriteLine();
        Console.WriteLine(text);

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
        {
            File.AppendAllText(stepSummary, text + "\n");
        }

        List<Verdict> failing = [.. verdicts.Where(static v => v.Fails)];

        foreach (Verdict v in failing)
        {
            foreach (Finding f in v.Findings)
            {
                Console.WriteLine($"FAIL: {v.Package}: {f.Id} {f.Target}: {v.Reason}.");
            }
        }

        if (failing.Count == 0)
        {
            int found = verdicts.Sum(static v => v.Findings.Count);
            Console.WriteLine($"ok  {verdicts.Count} package(s) against {baseline}: {found} breaking change(s), every one marked");
            return 0;
        }

        return 1;
    }

    public static IEnumerable<string> Libraries(string root) =>
        Packages(root).Where(static p => !p.Tool).Select(static p => p.Name);

    // The library packages and the tool, from src/: a project that sets
    // IsPackable true (Directory.Build.props makes nothing packable by
    // default), and whether it packs as a tool.
    private static List<(string Name, string Directory, bool Tool)> Packages(string root) =>
        [.. Directory.GetDirectories(Path.Combine(root, "src"))
            .Select(static d => (Directory: d, Project: Path.Combine(d, Path.GetFileName(d) + ".csproj")))
            .Where(static p => File.Exists(p.Project))
            .Select(static p => (p.Directory, Text: File.ReadAllText(p.Project)))
            .Where(static p => p.Text.Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            .Select(static p => (Path.GetFileName(p.Directory), p.Directory, p.Text.Contains("<PackAsTool>true</PackAsTool>", StringComparison.Ordinal)))
            .OrderBy(static p => p.Item1, StringComparer.Ordinal)];

    // The newest v* tag that is an ancestor of HEAD.
    private static string? NewestReleaseTag(string root)
    {
        (int exit, string output) = Git(root, "tag", "--merged", "HEAD", "--list", "v*");

        if (exit != 0)
        {
            throw new CouldNotRun($"git tag --merged failed: {output.Trim()}");
        }

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static t => ReleaseFormat.Version.IsMatch(t))
            .OrderBy(static t => t, Comparer<string>.Create(ReleaseFormat.Compare))
            .LastOrDefault();
    }

    private static IReadOnlyList<Commit> Commits(string root, string baseline)
    {
        // A record per commit: its sha, its message, then the paths it touches.
        (int exit, string output) = Git(root, "log", "--format=%x1e%H%x1f%B%x1f", "--name-only", $"{baseline}..HEAD");

        if (exit != 0)
        {
            throw new CouldNotRun($"git log {baseline}..HEAD failed: {output.Trim()}");
        }

        List<Commit> commits = [];

        foreach (string record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = record.Split('\x1f');

            if (parts.Length < 3)
            {
                continue;
            }

            commits.Add(new Commit(parts[0].Trim(), [.. parts[2].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], parts[1].Trim()));
        }

        return commits;
    }

    // Descriptors whose tag does not exist: the release this change proposes.
    private static IReadOnlyList<Descriptor> Proposed(string root)
    {
        List<string> problems = [];
        List<Descriptor> descriptors = ReleaseFormat.ReadDirectory(Path.Combine(root, ReleaseFormat.Directory), problems);
        (int exit, string tags) = Git(root, "tag", "--list", "v*");

        if (exit != 0)
        {
            throw new CouldNotRun($"git tag failed: {tags.Trim()}");
        }

        HashSet<string> existing = [.. tags.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        return [.. descriptors.Where(d => d.Version is not null && !existing.Contains(d.Version))];
    }

    private static async Task<bool> PublishedAsync(HttpClient http, string id, string version)
    {
        string url = $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json";

        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(url));

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            using JsonDocument index = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return index.RootElement.GetProperty("versions").EnumerateArray()
                .Any(v => string.Equals(v.GetString(), version, StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException e)
        {
            throw new CouldNotRun($"could not read {url}: {e.Message}");
        }
    }

    public static string Tail(string output) =>
        string.Join('\n', output.ReplaceLineEndings("\n").Split('\n').Where(static l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Distinct().Take(20));

    public static (int Exit, string Output) Dotnet(string root, params string[] arguments) => Run(root, Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", arguments);

    public static (int Exit, string Output) Git(string root, params string[] arguments) => Run(root, "git", arguments);

    private static (int Exit, string Output) Run(string root, string file, string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = file,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new CouldNotRun($"could not start {file}");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }
}

static class Fixtures
{
    private const string Package = "Varve.Fixture.ApiFreeze";

    // The source directory the fixture package stands for in a case's commits.
    private const string Source = "src/Varve.Fixture.ApiFreeze/";

    // tests/fixtures/api-freeze/cases.txt: one case a line,
    //   <name> | <surface> | <baseline version or none> | <commits file or -> | <descriptor file or -> | <pass, fail or new>
    public static int Run(string root, string directory)
    {
        string cases = Path.Combine(directory, "cases.txt");

        if (!File.Exists(cases))
        {
            throw new CouldNotRun($"{cases} does not exist");
        }

        string scratch = Path.Combine(Path.GetTempPath(), "varve-api-freeze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            string baseline = Pack(root, directory, scratch, "v1", baselinePath: null, findings: null);
            Dictionary<string, IReadOnlyList<Finding>> surfaces = new(StringComparer.Ordinal);
            int failures = 0;

            foreach (string raw in File.ReadAllLines(cases))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                string[] fields = [.. line.Split('|').Select(static f => f.Trim())];

                if (fields.Length != 6)
                {
                    throw new CouldNotRun($"cases.txt: '{line}' has {fields.Length} fields, not 6");
                }

                (string name, string surface, string version, string commitsFile, string descriptorFile, string expected) = (fields[0], fields[1], fields[2], fields[3], fields[4], fields[5]);
                IReadOnlyList<Commit> commits = commitsFile == "-" ? [] : Rule.ReadCommits(Path.Combine(directory, commitsFile));
                List<Descriptor> proposed = [];

                if (descriptorFile != "-")
                {
                    List<string> problems = [];
                    string path = Path.Combine(directory, descriptorFile);
                    proposed.Add(ReleaseFormat.Parse(path, File.ReadAllText(path), problems));
                }

                Verdict verdict;

                if (version == "none")
                {
                    verdict = new Verdict(Package, "new", [], [], false, "not published, so there is nothing to compare with");
                }
                else
                {
                    if (!surfaces.TryGetValue(surface, out IReadOnlyList<Finding>? findings))
                    {
                        string file = Path.Combine(scratch, surface + ".xml");
                        Pack(root, directory, scratch, surface, baseline, file);
                        findings = Rule.ReadFindings(file);
                        surfaces[surface] = findings;
                    }

                    verdict = Rule.Judge(Package, Source, findings, commits, proposed, version);
                }

                string got = verdict.State == "new" ? "new" : verdict.Fails ? "fail" : "pass";
                string detail = $"{verdict.Findings.Count} finding(s){string.Concat(verdict.Findings.Select(static f => $" {f.Id} {f.Target}"))}; {verdict.Reason}";

                if (got == expected)
                {
                    Console.WriteLine($"ok   {name}: {got} ({detail})");
                }
                else
                {
                    Console.WriteLine($"FAIL {name}: expected {expected}, got {got} ({detail})");
                    failures++;
                }
            }

            return failures == 0 ? 0 : 1;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (IOException)
            {
                // A leftover scratch directory is not worth failing over.
            }
        }
    }

    // Packs one surface of the fixture package into the scratch directory,
    // with every build output there too, so the working tree stays clean.
    private static string Pack(string root, string directory, string scratch, string surface, string? baselinePath, string? findings)
    {
        string project = Path.Combine(directory, surface, Package + ".csproj");

        if (!File.Exists(project))
        {
            throw new CouldNotRun($"{project} does not exist");
        }

        List<string> arguments =
        [
            "pack", project,
            "--configuration", "Release",
            "--output", Path.Combine(scratch, "out-" + surface),
            $"-p:ArtifactsPath={Path.Combine(scratch, "artifacts-" + surface)}",
            $"-p:MinVerVersionOverride={(baselinePath is null ? "0.0.1" : "0.0.2")}",
        ];

        if (baselinePath is not null)
        {
            arguments.Add($"-p:VarveApiFreezeProject={Package}");
            arguments.Add($"-p:VarveApiFreezeBaselinePath={baselinePath}");
            arguments.Add($"-p:VarveApiFreezeFindings={findings}");
        }

        (int exit, string output) = Gate.Dotnet(root, [.. arguments]);

        if (exit != 0)
        {
            throw new CouldNotRun($"packing the fixture's {surface} surface failed (exit {exit}):\n{Gate.Tail(output)}");
        }

        return Path.Combine(scratch, "out-" + surface, $"{Package}.{(baselinePath is null ? "0.0.1" : "0.0.2")}.nupkg");
    }
}

// --test-only: every public type and member of a library package that a test
// assembly references and no other assembly does.
//
// The surface is read from each package's built assembly: the types visible
// outside it (public, and nested public or protected inside a visible type),
// and their visible methods, constructors, properties, events and fields.
// Compiler-generated members are left out (a record's EqualityContract,
// Deconstruct, operators). Three kinds are counted apart, never listed as
// test-only:
//
//   - by contract: an override, or a method whose name and arity match a
//     member of an interface its type implements. A caller reaches it through
//     the base or the interface, so no reference names it;
//   - inlined: a constant or an enum value, which the compiler copies into
//     its caller, so no reference names it either;
//   - unreferenced: nothing outside the package names it at all.
//
// A reference is a row of a consumer's TypeRef or MemberRef table that
// resolves into the package's assembly, matched by its owner, name and
// signature, decoded the same way on both sides. A consumer is any other
// assembly the repository builds: a test is one under tests/ or a project
// named *.Tests (the benchmarks and the smoke hosts under tests/ among them);
// everything else (another package, a host, a sample) is a product consumer.
// Reflection, configuration binding and dependency injection leave no
// reference, which is why this list is for a person's decision, not a gate.
static class TestOnly
{
    private const string CompilerGenerated = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

    public static int Run(string root, string configuration, string? report)
    {
        List<(string Name, string Path)> packages = [];
        List<(string Name, string Path, bool Test)> consumers = [];
        List<string> unbuilt = [];
        HashSet<string> packageNames = [.. Gate.Libraries(root)];

        foreach (string project in Directory.GetDirectories(Path.Combine(root, "src"))
            .Concat(Directory.GetDirectories(Path.Combine(root, "tests")))
            .Concat(Directory.GetDirectories(Path.Combine(root, "samples")))
            .Where(static d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".csproj")))
            .Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(project);
            string? assembly = Built(project, name, configuration);

            if (assembly is null)
            {
                unbuilt.Add(Path.GetRelativePath(root, project).Replace('\\', '/'));
                continue;
            }

            bool test = Path.GetRelativePath(root, project).Replace('\\', '/').StartsWith("tests/", StringComparison.Ordinal)
                || name.EndsWith(".Tests", StringComparison.Ordinal);

            if (packageNames.Contains(name))
            {
                packages.Add((name, assembly));
            }

            consumers.Add((name, assembly, test));
        }

        List<string> missing = [.. packageNames.Where(p => !packages.Any(q => q.Name == p))];

        if (missing.Count > 0)
        {
            throw new CouldNotRun($"no {configuration} build of {string.Join(", ", missing)}; run `dotnet build Varve.slnx -c {configuration}` first");
        }

        // Who references what: the key of a type or member, and the consumers
        // that name it.
        Dictionary<string, HashSet<string>> referencedBy = new(StringComparer.Ordinal);
        Dictionary<string, bool> isTest = consumers.ToDictionary(static c => c.Name, static c => c.Test, StringComparer.Ordinal);

        foreach ((string name, string path, bool _) in consumers)
        {
            foreach (string key in References(path, packageNames, name))
            {
                if (!referencedBy.TryGetValue(key, out HashSet<string>? by))
                {
                    referencedBy[key] = by = new HashSet<string>(StringComparer.Ordinal);
                }

                by.Add(name);
            }
        }

        StringBuilder text = new();
        text.AppendLine("## Public members whose only consumer is a test");
        text.AppendLine();
        text.AppendLine("Read from the built assemblies by `eng/api-freeze.cs --test-only` (ADR 0227 point 6). A test consumer is an assembly under `tests/` or a project named `*.Tests`. Nothing here is a finding of the gate: each entry is for the maintainer's decision, justified or made internal before the freeze.");
        text.AppendLine();
        text.AppendLine("| Package | Visible | Test-only types | Test-only members | Unreferenced types | Unreferenced members | By contract | Inlined |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

        StringBuilder details = new();
        (int types, int members) total = (0, 0);

        foreach ((string package, string path) in packages.OrderBy(static p => p.Name, StringComparer.Ordinal))
        {
            Interfaces.Add(Path.GetDirectoryName(path)!);
            Surface surface = Surface.Read(path);
            string Class(string key)
            {
                if (!referencedBy.TryGetValue(key, out HashSet<string>? by) || by.Count == 0)
                {
                    return "none";
                }

                return by.Any(c => !isTest[c]) ? "product" : "test";
            }

            List<SurfaceType> testTypes = [];
            List<SurfaceType> noneTypes = [];
            List<(SurfaceType Type, SurfaceMember Member, string Consumers)> testMembers = [];
            List<(SurfaceType Type, SurfaceMember Member)> noneMembers = [];
            int contract = 0, inlined = 0;

            foreach (SurfaceType type in surface.Types)
            {
                // A type is named by a TypeRef, or by the owner of a member reference.
                string typeClass = Combine(Class(type.Key), Class("O:" + type.FullName));

                if (typeClass == "test")
                {
                    testTypes.Add(type);
                    continue;
                }

                if (typeClass == "none")
                {
                    noneTypes.Add(type);
                    continue;
                }

                foreach (SurfaceMember member in type.Members)
                {
                    if (member.Inlined)
                    {
                        inlined++;
                        continue;
                    }

                    if (member.Contract)
                    {
                        contract++;
                        continue;
                    }

                    string memberClass = member.Keys.Select(Class).Aggregate("none", Combine);

                    if (memberClass == "test")
                    {
                        string by = string.Join(", ", member.Keys.SelectMany(k => referencedBy.TryGetValue(k, out HashSet<string>? s) ? s : []).Distinct().Order(StringComparer.Ordinal));
                        testMembers.Add((type, member, by));
                    }
                    else if (memberClass == "none")
                    {
                        noneMembers.Add((type, member));
                    }
                }
            }

            int visible = surface.Types.Count + surface.Types.Sum(static t => t.Members.Count);
            text.AppendLine($"| `{package}` | {visible} | {testTypes.Count} | {testMembers.Count} | {noneTypes.Count} | {noneMembers.Count} | {contract} | {inlined} |");
            total = (total.types + testTypes.Count, total.members + testMembers.Count);

            if (testTypes.Count + testMembers.Count + noneTypes.Count + noneMembers.Count == 0)
            {
                continue;
            }

            details.AppendLine();
            details.AppendLine($"### `{package}`");

            if (testTypes.Count + testMembers.Count > 0)
            {
                details.AppendLine();
                details.AppendLine("Referenced by tests only:");
                details.AppendLine();

                foreach (SurfaceType type in testTypes)
                {
                    details.AppendLine($"- type `{type.Display}` ({string.Join(", ", (referencedBy.GetValueOrDefault(type.Key) ?? []).Concat(referencedBy.GetValueOrDefault("O:" + type.FullName) ?? []).Distinct().Order(StringComparer.Ordinal))})");
                }

                foreach ((SurfaceType type, SurfaceMember member, string by) in testMembers)
                {
                    details.AppendLine($"- `{type.Display}.{member.Display}` ({by})");
                }
            }

            if (noneTypes.Count + noneMembers.Count > 0)
            {
                details.AppendLine();
                details.AppendLine("Referenced by no other assembly:");
                details.AppendLine();

                foreach (SurfaceType type in noneTypes)
                {
                    details.AppendLine($"- type `{type.Display}`");
                }

                foreach ((SurfaceType type, SurfaceMember member) in noneMembers)
                {
                    details.AppendLine($"- `{type.Display}.{member.Display}`");
                }
            }
        }

        text.AppendLine();
        text.AppendLine($"Assemblies read: {consumers.Count}, {consumers.Count(static c => c.Test)} of them tests. Not built, so not read: {(unbuilt.Count == 0 ? "none" : string.Join(", ", unbuilt.Select(static u => $"`{u}`")))}.");
        text.Append(details);

        string output = text.ToString();

        if (report is not null)
        {
            File.WriteAllText(report, output);
            Console.WriteLine($"ok  {total.types} test-only type(s) and {total.members} test-only member(s) across {packages.Count} package(s); the list is in {report}");
        }
        else
        {
            Console.WriteLine(output);
        }

        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
        {
            File.AppendAllText(stepSummary, output + "\n");
        }

        return 0;
    }

    // product beats test beats none.
    private static string Combine(string a, string b) =>
        a == "product" || b == "product" ? "product" : a == "test" || b == "test" ? "test" : "none";

    private static string? Built(string project, string name, string configuration)
    {
        string bin = Path.Combine(project, "bin", configuration);

        if (!Directory.Exists(bin))
        {
            return null;
        }

        string? assemblyName = Regex.Match(File.ReadAllText(Path.Combine(project, name + ".csproj")), "<AssemblyName>([^<$]+)</AssemblyName>") is { Success: true } m ? m.Groups[1].Value : name;
        return Directory.EnumerateFiles(bin, assemblyName + ".dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    // The keys a consumer's references resolve to, for references into one of
    // the packages: T:<type> for a TypeRef, O:<type> for the owner of a member
    // reference, and the member's own key.
    private static IEnumerable<string> References(string path, HashSet<string> packages, string self)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);

        if (!pe.HasMetadata)
        {
            yield break;
        }

        MetadataReader reader = pe.GetMetadataReader();
        Names names = new(reader);

        foreach (TypeReferenceHandle handle in reader.TypeReferences)
        {
            if (names.AssemblyOf(handle) is { } assembly && assembly != self && packages.Contains(assembly))
            {
                yield return "T:" + names.Of(handle);
            }
        }

        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            (string? owner, string? assembly) = names.Owner(member.Parent);

            if (owner is null || assembly is null || assembly == self || !packages.Contains(assembly))
            {
                continue;
            }

            yield return "O:" + owner;
            string name = reader.GetString(member.Name);

            if (member.GetKind() == MemberReferenceKind.Method)
            {
                MethodSignature<string> signature = member.DecodeMethodSignature(names, null);
                yield return Surface.MethodKey(owner, name, signature);
            }
            else
            {
                yield return $"F:{owner}.{name}";
            }
        }
    }
}

// Every interface type in the shared frameworks this runtime has
// (Microsoft.NETCore.App and its siblings) and in each package's own output
// directory, which holds its dependencies, by full name, so that an
// implementer's member can be matched to the interface member it implements.
static class Interfaces
{
    private static readonly Dictionary<string, (MetadataReader Reader, TypeDefinitionHandle Handle)> Index = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal);

    static Interfaces()
    {
        string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar);
        string shared = Path.GetDirectoryName(Path.GetDirectoryName(runtime)!)!;

        foreach (string framework in Directory.GetDirectories(shared))
        {
            string version = Path.Combine(framework, Path.GetFileName(runtime));

            if (Directory.Exists(version))
            {
                Add(version);
            }
        }
    }

    public static void Add(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            if (!Read.Add(Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                // Kept open for the run: the readers point into these images.
                PEReader pe = new(File.OpenRead(file));

                if (!pe.HasMetadata)
                {
                    continue;
                }

                MetadataReader reader = pe.GetMetadataReader();
                Names names = new(reader);

                foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                {
                    if ((reader.GetTypeDefinition(handle).Attributes & TypeAttributes.Interface) != 0)
                    {
                        Index.TryAdd(names.Of(handle), (reader, handle));
                    }
                }
            }
            catch (BadImageFormatException)
            {
                // A native library beside the managed ones.
            }
        }
    }

    // Adds the member names of the interface and of every interface it
    // extends. False when the interface is in no assembly read.
    public static bool MemberNames(string fullName, HashSet<string> names)
    {
        if (!Index.TryGetValue(fullName, out (MetadataReader Reader, TypeDefinitionHandle Handle) found))
        {
            return false;
        }

        TypeDefinition type = found.Reader.GetTypeDefinition(found.Handle);

        foreach (MethodDefinitionHandle m in type.GetMethods())
        {
            names.Add(found.Reader.GetString(found.Reader.GetMethodDefinition(m).Name));
        }

        Names resolver = new(found.Reader);

        foreach (InterfaceImplementationHandle i in type.GetInterfaceImplementations())
        {
            EntityHandle target = found.Reader.GetInterfaceImplementation(i).Interface;

            if (target.Kind == HandleKind.TypeSpecification)
            {
                target = resolver.GenericOf((TypeSpecificationHandle)target);
            }

            string? baseName = target.Kind switch
            {
                HandleKind.TypeDefinition => resolver.Of((TypeDefinitionHandle)target),
                HandleKind.TypeReference => resolver.Of((TypeReferenceHandle)target),
                _ => null,
            };

            if (baseName is not null && baseName != fullName)
            {
                MemberNames(baseName, names);
            }
        }

        return true;
    }
}

sealed record SurfaceMember(string Display, IReadOnlyList<string> Keys, bool Contract, bool Inlined);

sealed record SurfaceType(string FullName, string Display, IReadOnlyList<SurfaceMember> Members)
{
    public string Key => "T:" + FullName;
}

sealed class Surface
{
    public required IReadOnlyList<SurfaceType> Types { get; init; }

    public static string MethodKey(string owner, string name, MethodSignature<string> signature) =>
        $"M:{owner}.{name}`{signature.GenericParameterCount}({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";

    public static Surface Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        Names names = new(reader);
        List<SurfaceType> types = [];

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);

            if (!Visible(reader, handle) || Generated(reader, type.GetCustomAttributes()) || reader.GetString(type.Name).Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            string owner = names.Of(handle);
            HashSet<string> contractNames = ContractNames(reader, names, type);
            bool isEnum = names.BaseOf(type) == "System.Enum";
            List<SurfaceMember> members = [];
            HashSet<MethodDefinitionHandle> accessors = [];

            foreach (PropertyDefinitionHandle p in type.GetProperties())
            {
                PropertyDefinition property = reader.GetPropertyDefinition(p);
                PropertyAccessors a = property.GetAccessors();
                List<MethodDefinitionHandle> visible = [.. new[] { a.Getter, a.Setter }.Where(h => !h.IsNil && Visible(reader.GetMethodDefinition(h).Attributes))];
                accessors.UnionWith(new[] { a.Getter, a.Setter }.Where(static h => !h.IsNil));

                if (visible.Count > 0)
                {
                    members.Add(new SurfaceMember(
                        reader.GetString(property.Name),
                        [.. visible.Select(h => Key(reader, names, owner, h))],
                        visible.Any(h => Contract(reader, h, contractNames)),
                        false));
                }
            }

            foreach (EventDefinitionHandle e in type.GetEvents())
            {
                EventDefinition @event = reader.GetEventDefinition(e);
                EventAccessors a = @event.GetAccessors();
                List<MethodDefinitionHandle> visible = [.. new[] { a.Adder, a.Remover }.Where(h => !h.IsNil && Visible(reader.GetMethodDefinition(h).Attributes))];
                accessors.UnionWith(new[] { a.Adder, a.Remover, a.Raiser }.Where(static h => !h.IsNil));

                if (visible.Count > 0)
                {
                    members.Add(new SurfaceMember(
                        "event " + reader.GetString(@event.Name),
                        [.. visible.Select(h => Key(reader, names, owner, h))],
                        visible.Any(h => Contract(reader, h, contractNames)),
                        false));
                }
            }

            foreach (MethodDefinitionHandle m in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(m);
                string name = reader.GetString(method.Name);

                if (accessors.Contains(m) || !Visible(method.Attributes) || name.Contains('<', StringComparison.Ordinal) || Generated(reader, method.GetCustomAttributes()))
                {
                    continue;
                }

                MethodSignature<string> signature = method.DecodeSignature(names, null);
                string display = $"{(name == ".ctor" ? reader.GetString(type.Name).Split('`')[0] : name)}({string.Join(", ", signature.ParameterTypes.Select(Short))})";
                members.Add(new SurfaceMember(display, [MethodKey(owner, name, signature)], Contract(reader, m, contractNames), false));
            }

            foreach (FieldDefinitionHandle f in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(f);
                FieldAttributes access = field.Attributes & FieldAttributes.FieldAccessMask;
                string name = reader.GetString(field.Name);

                if (access is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem) || (field.Attributes & FieldAttributes.SpecialName) != 0)
                {
                    continue;
                }

                bool literal = (field.Attributes & FieldAttributes.Literal) != 0 || isEnum;
                members.Add(new SurfaceMember(name, [$"F:{owner}.{name}"], false, literal));
            }

            types.Add(new SurfaceType(owner, Short(owner), members));
        }

        return new Surface { Types = types };
    }

    private static string Key(MetadataReader reader, Names names, string owner, MethodDefinitionHandle handle)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        return MethodKey(owner, reader.GetString(method.Name), method.DecodeSignature(names, null));
    }

    // An override, or a method named as a member of an implemented interface is.
    private static bool Contract(MetadataReader reader, MethodDefinitionHandle handle, HashSet<string> contractNames)
    {
        MethodDefinition method = reader.GetMethodDefinition(handle);
        bool overrides = (method.Attributes & MethodAttributes.Virtual) != 0 && (method.Attributes & MethodAttributes.NewSlot) == 0;
        return overrides || contractNames.Contains(reader.GetString(method.Name));
    }

    // The member names of every interface the type implements, read from this
    // assembly for its own interfaces and from the interface index for the
    // rest.
    private static HashSet<string> ContractNames(MetadataReader reader, Names names, TypeDefinition type)
    {
        HashSet<string> result = new(StringComparer.Ordinal);

        foreach (InterfaceImplementationHandle i in type.GetInterfaceImplementations())
        {
            EntityHandle target = reader.GetInterfaceImplementation(i).Interface;

            if (target.Kind == HandleKind.TypeSpecification)
            {
                target = names.GenericOf((TypeSpecificationHandle)target);
            }

            if (target.Kind == HandleKind.TypeDefinition)
            {
                foreach (MethodDefinitionHandle m in reader.GetTypeDefinition((TypeDefinitionHandle)target).GetMethods())
                {
                    result.Add(reader.GetString(reader.GetMethodDefinition(m).Name));
                }
            }
            else if (target.Kind == HandleKind.TypeReference)
            {
                string fullName = names.Of((TypeReferenceHandle)target);

                if (!Interfaces.MemberNames(fullName, result))
                {
                    // An interface this run could not find: its implementers'
                    // members of any name are taken as reachable through it,
                    // never as test-only.
                    result.Add("*");
                }
            }
        }

        // An unread interface: every method of the type is taken as reachable
        // through it, never as test-only.
        if (result.Contains("*"))
        {
            foreach (MethodDefinitionHandle m in type.GetMethods())
            {
                result.Add(reader.GetString(reader.GetMethodDefinition(m).Name));
            }
        }

        return result;
    }

    private static bool Visible(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static bool Visible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;

        return visibility switch
        {
            TypeAttributes.Public => true,
            TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem => Visible(reader, type.GetDeclaringType()),
            _ => false,
        };
    }

    private static bool Generated(MetadataReader reader, CustomAttributeHandleCollection attributes)
    {
        foreach (CustomAttributeHandle h in attributes)
        {
            EntityHandle constructor = reader.GetCustomAttribute(h).Constructor;

            if (constructor.Kind == HandleKind.MemberReference
                && reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent
                && new Names(reader).Of((TypeReferenceHandle)parent) == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            {
                return true;
            }
        }

        return false;
    }

    // A type name without its namespaces, for reading: Varve.Store.Log.Commit
    // is Commit, System.Collections.Generic.List`1<System.Int32> is List<Int32>.
    public static string Short(string name) =>
        Regex.Replace(Regex.Replace(name, @"(?:[A-Za-z_][\w]*\.)+(?=[A-Za-z_])", ""), @"`\d+", "").Replace('/', '.');
}

// Type names, and the signature decoder both sides share, so that a member's
// key is the same whether it is read from its definition or from a reference.
sealed class Names(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    public string Of(TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        return declaring.IsNil ? Qualify(reader.GetString(type.Namespace), name) : Of(declaring) + "/" + name;
    }

    public string Of(TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? Of((TypeReferenceHandle)type.ResolutionScope) + "/" + name
            : Qualify(reader.GetString(type.Namespace), name);
    }

    public string? AssemblyOf(TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);

        return type.ResolutionScope.Kind switch
        {
            HandleKind.TypeReference => AssemblyOf((TypeReferenceHandle)type.ResolutionScope),
            HandleKind.AssemblyReference => reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name),
            _ => null,
        };
    }

    // The type a member reference belongs to, and its assembly: a TypeRef, or
    // the generic type of a TypeSpec instantiation.
    public (string? Owner, string? Assembly) Owner(EntityHandle parent)
    {
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            parent = GenericOf((TypeSpecificationHandle)parent);
        }

        return parent.Kind == HandleKind.TypeReference
            ? (Of((TypeReferenceHandle)parent), AssemblyOf((TypeReferenceHandle)parent))
            : (null, null);
    }

    public EntityHandle GenericOf(TypeSpecificationHandle handle)
    {
        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);

        // GENERICINST (0x15), CLASS or VALUETYPE, then the generic type's handle.
        if (blob.ReadSignatureHeader().RawValue == (byte)SignatureTypeCode.GenericTypeInstance)
        {
            blob.ReadByte();
            return blob.ReadTypeHandle();
        }

        return default;
    }

    public string BaseOf(TypeDefinition type) => type.BaseType.IsNil ? "" : type.BaseType.Kind switch
    {
        HandleKind.TypeReference => Of((TypeReferenceHandle)type.BaseType),
        HandleKind.TypeDefinition => Of((TypeDefinitionHandle)type.BaseType),
        _ => "",
    };

    private static string Qualify(string space, string name) => space.Length == 0 ? name : space + "." + name;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode switch
    {
        PrimitiveTypeCode.IntPtr => "IntPtr",
        PrimitiveTypeCode.UIntPtr => "UIntPtr",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind) => new Names(r).Of(handle);

    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind) => new Names(r).Of(handle);

    public string GetTypeFromSpecification(MetadataReader r, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        r.GetTypeSpecification(handle).DecodeSignature(new Names(r), genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr(" + string.Join(",", signature.ParameterTypes) + "):" + signature.ReturnType;
}
