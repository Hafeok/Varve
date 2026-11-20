// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The learning track's gate (#94, ADR 0249): a page never shows what nobody
// ran.
//
//   dotnet run eng/learn.cs -- --check [--root <directory>]
//   dotnet run eng/learn.cs -- --run [--write | --trimmed <rid>] [--packages <version>]
//   dotnet run eng/learn.cs -- --fixtures tests/fixtures/learn
//
// --check reads, and runs nothing:
//   - every fenced block of docs/learn/*.md has a marker, in ADR 0248's
//     grammar, on the line before it (blank lines aside);
//   - a marker whose note is "output <file>" quotes that file, and one whose
//     note is "source <file>#<region>" quotes that region, byte for byte
//     after line endings;
//   - a marker names jobs that exist, and a page with a `none` has a section
//     "Not run in CI";
//   - an `<!-- unshipped: #N -->` marker sits before a "Not in this release"
//     line naming #N, and #N is in no released descriptor's basis;
//   - no rec: or brick: term appears in a page or a sample before the chapter
//     docs/learn/terms.md introduces it in, and none is missing from it;
//   - a sample references Varve only by VarveReference, each naming a
//     packable project under src/, and nothing in src/ admits a sample with
//     InternalsVisibleTo.
// --run builds and runs every sample under samples/learn/NN-*/ with
// `--state`, and compares each output section and the final state with its
// expected/ files. --write writes them instead, then the pages' quoted blocks.
// --trimmed <rid> publishes each sample trimmed for that runtime, with every
// warning an error, and runs the published executable instead. --packages
// <version> builds every sample against the published Varve packages at that
// version, as a user restores them, instead of src/ (the nightly's check).
//
// Exit codes: 0 holds, 1 findings, 2 could not run.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string root = FindRepositoryRoot();
string? fixtures = null;
bool check = false;
bool run = false;
bool write = false;
string? trimmed = null;
string? packages = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check":
            check = true;
            break;
        case "--run":
            run = true;
            break;
        case "--write":
            write = true;
            break;
        case "--trimmed" when i + 1 < args.Length:
            trimmed = args[++i];
            break;
        case "--packages" when i + 1 < args.Length:
            packages = args[++i];
            break;
        case "--root" when i + 1 < args.Length:
            root = Path.GetFullPath(args[++i]);
            break;
        case "--fixtures" when i + 1 < args.Length:
            fixtures = Path.GetFullPath(args[++i]);
            break;
        default:
            Console.Error.WriteLine("learn: --check [--root <directory>] | --run [--write] | --fixtures <directory>");
            return 2;
    }
}

if (fixtures is not null)
{
    return Fixtures(fixtures);
}

if (!check && !run)
{
    Console.Error.WriteLine("learn: --check [--root <directory>] | --run [--write] | --fixtures <directory>");
    return 2;
}

if (run)
{
    int ran = RunSamples(root, write, trimmed, packages);
    if (ran != 0)
    {
        return ran;
    }

    if (write)
    {
        WritePages(root);
    }
}

List<string> findings = Check(root);
foreach (string finding in findings)
{
    Console.WriteLine("learn: " + finding);
}

Console.WriteLine(findings.Count == 0 ? "learn: the track holds." : "learn: " + findings.Count + " finding(s).");
return findings.Count == 0 ? 0 : 1;

// --- the checks ------------------------------------------------------------

static List<string> Check(string root)
{
    List<string> findings = [];
    string pages = Path.Combine(root, "docs", "learn");
    if (!Directory.Exists(pages))
    {
        findings.Add("docs/learn/ does not exist.");
        return findings;
    }

    Dictionary<string, int>? terms = ReadTerms(root, findings);
    HashSet<int> released = ReleasedIssues(root);

    foreach (string page in Directory.EnumerateFiles(pages, "*.md").Order(StringComparer.Ordinal))
    {
        CheckPage(root, page, released, findings);
    }

    if (terms is not null)
    {
        foreach ((string file, int chapter) in TermFiles(root))
        {
            CheckTerms(root, file, chapter, terms, findings);
        }
    }

    CheckReferences(root, findings);
    return findings;
}

static void CheckPage(string root, string page, HashSet<int> released, List<string> findings)
{
    string name = Relative(root, page);
    string[] lines = Lines(File.ReadAllText(page));
    bool hasNone = false;
    string? marker = null;
    int markerLine = 0;

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];

        Match unshipped = Regex.Match(line, @"^<!-- unshipped: #(\d+) -->$");
        if (unshipped.Success)
        {
            int issue = int.Parse(unshipped.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            string quote = Quote(lines, i + 1);
            if (!quote.StartsWith("> **Not in this release.**", StringComparison.Ordinal) || !Regex.IsMatch(quote, @"#" + issue + @"\b"))
            {
                findings.Add($"{name}:{i + 1}: an unshipped marker for #{issue} is not followed by a \"> **Not in this release.**\" quote naming #{issue}.");
            }

            if (released.Contains(issue))
            {
                findings.Add($"{name}:{i + 1}: #{issue} is in a released descriptor's basis, and the page still says it has not shipped.");
            }

            continue;
        }

        if (line.StartsWith("<!-- ci:", StringComparison.Ordinal))
        {
            marker = line;
            markerLine = i + 1;
            continue;
        }

        if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            string fence = line.TrimStart()[..(line.TrimStart().Length - line.TrimStart().TrimStart('`').Length)];
            int start = i + 1;
            int end = start;
            while (end < lines.Length && !lines[end].TrimStart().StartsWith(fence, StringComparison.Ordinal))
            {
                end++;
            }

            string block = string.Join('\n', lines[start..Math.Min(end, lines.Length)]);
            if (marker is null)
            {
                findings.Add($"{name}:{i + 1}: a fenced block has no marker on the line before it (ADR 0248).");
            }
            else
            {
                hasNone |= CheckMarker(root, name, markerLine, marker, block, findings);
            }

            marker = null;
            i = end;
            continue;
        }

        if (line.Trim().Length > 0)
        {
            marker = null;
        }
    }

    if (hasNone && !lines.Any(l => Regex.IsMatch(l, @"^#+ Not run in CI\s*$")))
    {
        findings.Add($"{name}: a block is marked `none`, and the page has no section \"Not run in CI\" (ADR 0248).");
    }
}

/// Checks one marker against its block; true when it is a `none`.
static bool CheckMarker(string root, string page, int line, string marker, string block, List<string> findings)
{
    Match m = Regex.Match(marker, @"^<!-- ci: (.*) -->$");
    if (!m.Success)
    {
        findings.Add($"{page}:{line}: a marker is not one line of the form <!-- ci: … -->.");
        return false;
    }

    string body = m.Groups[1].Value;
    foreach (string kind in (string[])["none", "example"])
    {
        if (body.StartsWith(kind, StringComparison.Ordinal))
        {
            string rest = body[kind.Length..].Trim();
            if (!rest.StartsWith("—", StringComparison.Ordinal) || rest.TrimStart('—').Trim().Length == 0)
            {
                findings.Add($"{page}:{line}: {(kind == "none" ? "a" : "an")} `{kind}` marker says nothing after \" — \".");
            }

            return kind == "none";
        }
    }

    int dash = body.IndexOf(" — ", StringComparison.Ordinal);
    if (dash < 0)
    {
        findings.Add($"{page}:{line}: a marker names jobs and no note after \" — \".");
        return false;
    }

    foreach (string job in body[..dash].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!JobExists(root, job))
        {
            findings.Add($"{page}:{line}: the marker names `{job}`, which is no job of eng/ci.cs or of a workflow.");
        }
    }

    string note = body[(dash + 3)..].Trim();
    Match quoted = Regex.Match(note, @"^(output|source) (\S+?)(?:#([\w-]+))?$");
    if (!quoted.Success)
    {
        return false;
    }

    string file = Path.Combine(root, quoted.Groups[2].Value);
    if (!File.Exists(file))
    {
        findings.Add($"{page}:{line}: the marker quotes {quoted.Groups[2].Value}, which does not exist.");
        return false;
    }

    string? expected = quoted.Groups[3].Success
        ? Region(File.ReadAllText(file), quoted.Groups[3].Value)
        : Trimmed(File.ReadAllText(file));
    if (expected is null)
    {
        findings.Add($"{page}:{line}: {quoted.Groups[2].Value} has no region `{quoted.Groups[3].Value}`.");
    }
    else if (expected != Trimmed(block))
    {
        findings.Add($"{page}:{line}: the block differs from {quoted.Groups[2].Value}{(quoted.Groups[3].Success ? "#" + quoted.Groups[3].Value : "")}; `dotnet run eng/learn.cs -- --run --write` writes it.");
    }

    return false;
}

static bool JobExists(string root, string job)
{
    string[] parts = job.Split(' ', 2, StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
    {
        return false;
    }

    if (parts[0] == "eng/ci.cs")
    {
        string ci = Path.Combine(root, "eng", "ci.cs");
        return File.Exists(ci) && File.ReadAllText(ci).Contains("(\"" + parts[1] + "\",", StringComparison.Ordinal);
    }

    string workflow = Path.Combine(root, ".github", "workflows", parts[0]);
    return File.Exists(workflow) && Regex.IsMatch(File.ReadAllText(workflow), @"(?m)^  " + Regex.Escape(parts[1]) + @":\s*$");
}

static Dictionary<string, int>? ReadTerms(string root, List<string> findings)
{
    string path = Path.Combine(root, "docs", "learn", "terms.md");
    if (!File.Exists(path))
    {
        findings.Add("docs/learn/terms.md does not exist.");
        return null;
    }

    Dictionary<string, int> terms = new(StringComparer.Ordinal);
    foreach (string line in Lines(File.ReadAllText(path)))
    {
        Match row = Regex.Match(line, @"^\|\s*(\d+)\s*\|(.*)\|\s*$");
        if (!row.Success)
        {
            continue;
        }

        int chapter = int.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (Match term in TermPattern().Matches(row.Groups[2].Value))
        {
            if (!terms.TryAdd(term.Value, chapter))
            {
                findings.Add($"docs/learn/terms.md: {term.Value} is introduced twice.");
            }
        }
    }

    return terms;
}

/// Every file whose terms are checked, with the chapter it belongs to.
static IEnumerable<(string File, int Chapter)> TermFiles(string root)
{
    foreach (string page in Directory.EnumerateFiles(Path.Combine(root, "docs", "learn"), "*.md").Order(StringComparer.Ordinal))
    {
        string file = Path.GetFileName(page);
        if (file == "terms.md")
        {
            continue;
        }

        int? chapter = ChapterOf(file) ?? file switch { "appendix.md" => 99, "README.md" or "index.md" => 0, _ => null };
        if (chapter is int c)
        {
            yield return (page, c);
        }
    }

    string samples = Path.Combine(root, "samples", "learn");
    if (!Directory.Exists(samples))
    {
        yield break;
    }

    string dataDirectory = Path.Combine(samples, "data");
    foreach (string data in (Directory.Exists(dataDirectory) ? Directory.EnumerateFiles(dataDirectory) : []).Order(StringComparer.Ordinal))
    {
        if (ChapterOf(Path.GetFileName(data)) is int c)
        {
            yield return (data, c);
        }
    }

    foreach (string sample in Directory.EnumerateDirectories(samples).Order(StringComparer.Ordinal))
    {
        if (ChapterOf(Path.GetFileName(sample)) is not int c)
        {
            continue;
        }

        foreach (string file in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && Path.GetExtension(f) is ".cs" or ".trig" or ".ttl" or ".ru" or ".rq" or ".txt" or ".nq")
            .Order(StringComparer.Ordinal))
        {
            yield return (file, c);
        }
    }
}

static void CheckTerms(string root, string file, int chapter, Dictionary<string, int> terms, List<string> findings)
{
    string[] lines = Lines(File.ReadAllText(file));
    HashSet<string> reported = new(StringComparer.Ordinal);
    for (int i = 0; i < lines.Length; i++)
    {
        foreach (Match m in TermPattern().Matches(lines[i]))
        {
            Report(m.Value, i);
        }

        foreach (Match m in TermIriPattern().Matches(lines[i]))
        {
            Report((m.Groups[1].Value == "w3id.org/rec" ? "rec:" : "brick:") + m.Groups[2].Value, i);
        }
    }

    void Report(string term, int line)
    {
        if (!reported.Add(term))
        {
            return;
        }

        if (!terms.TryGetValue(term, out int introduced))
        {
            findings.Add($"{Relative(root, file)}:{line + 1}: {term} is in no row of docs/learn/terms.md.");
        }
        else if (introduced > chapter)
        {
            findings.Add($"{Relative(root, file)}:{line + 1}: {term} is used in chapter {chapter} and introduced in chapter {introduced}.");
        }
    }
}

static void CheckReferences(string root, List<string> findings)
{
    string samples = Path.Combine(root, "samples", "learn");
    if (!Directory.Exists(samples))
    {
        return;
    }

    List<string> names = [];
    foreach (string project in Directory.EnumerateFiles(samples, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string name = Relative(root, project);
        names.Add(Path.GetFileNameWithoutExtension(project));
        XDocument document = XDocument.Load(project);
        foreach (XElement element in document.Descendants())
        {
            switch (element.Name.LocalName)
            {
                case "ProjectReference" or "PackageReference" or "Reference" or "FrameworkReference" or "Compile" or "Import":
                    findings.Add($"{name}: <{element.Name.LocalName} Include=\"{(string?)element.Attribute("Include") ?? (string?)element.Attribute("Project")}\"> — a sample names Varve by VarveReference and nothing else.");
                    break;
                case "VarveReference":
                    string reference = (string?)element.Attribute("Include") ?? "";
                    string target = Path.Combine(root, "src", reference, reference + ".csproj");
                    if (!File.Exists(target))
                    {
                        findings.Add($"{name}: VarveReference {reference} is no project under src/.");
                    }
                    else if (!Regex.IsMatch(File.ReadAllText(target), @"<IsPackable>\s*true\s*</IsPackable>"))
                    {
                        findings.Add($"{name}: VarveReference {reference} is not packable, so no user could reference it.");
                    }

                    break;
            }
        }
    }

    string src = Path.Combine(root, "src");
    if (!Directory.Exists(src) || names.Count == 0)
    {
        return;
    }

    foreach (string file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
        .Where(f => Path.GetExtension(f) is ".cs" or ".csproj" or ".props" or ".targets")
        .Order(StringComparer.Ordinal))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"InternalsVisibleTo[^""]*""([^"",]+)"))
        {
            if (names.Contains(m.Groups[1].Value, StringComparer.Ordinal))
            {
                findings.Add($"{Relative(root, file)}: admits the sample {m.Groups[1].Value} with InternalsVisibleTo.");
            }
        }
    }
}

static HashSet<int> ReleasedIssues(string root)
{
    HashSet<int> issues = [];
    string releases = Path.Combine(root, "releases");
    if (!Directory.Exists(releases))
    {
        return issues;
    }

    foreach (string descriptor in Directory.EnumerateFiles(releases, "*.yaml"))
    {
        bool inBasis = false;
        foreach (string line in Lines(File.ReadAllText(descriptor)))
        {
            if (Regex.IsMatch(line, @"^\S"))
            {
                inBasis = line.StartsWith("basis:", StringComparison.Ordinal);
                continue;
            }

            Match issue = Regex.Match(line, @"^\s+- issue:\s*(\d+)\s*$");
            if (inBasis && issue.Success)
            {
                issues.Add(int.Parse(issue.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    return issues;
}

// --- the samples -----------------------------------------------------------

static int RunSamples(string root, bool write, string? trimmed, string? packages)
{
    string samples = Path.Combine(root, "samples", "learn");
    List<string> mismatches = [];
    foreach (string sample in Directory.EnumerateDirectories(samples).Order(StringComparer.Ordinal))
    {
        if (ChapterOf(Path.GetFileName(sample)) is null)
        {
            continue;
        }

        string project = Directory.EnumerateFiles(sample, "*.csproj").Single();
        string name = Path.GetFileName(sample);
        Stopwatch stopwatch = Stopwatch.StartNew();
        string state = Path.Combine(Path.GetTempPath(), "varve-learn-" + name + "-" + Guid.NewGuid().ToString("N") + ".nq");
        string[] from = packages is null ? [] : ["-p:VarveFrom=package", "-p:VarvePackageVersion=" + packages];
        string output;
        if (trimmed is null)
        {
            if (Process("dotnet", ["build", project, "-c", "Release", "-nologo", "-v", "quiet", .. from], root, out string built) != 0)
            {
                Console.Error.WriteLine(built);
                Console.Error.WriteLine($"learn: {name} does not build.");
                return 1;
            }

            if (Process("dotnet", ["run", "--project", project, "-c", "Release", "--no-build", .. from, "--", "--state", state], root, out output) != 0)
            {
                Console.Error.WriteLine(output);
                Console.Error.WriteLine($"learn: {name} exited with a failure.");
                return 1;
            }
        }
        else
        {
            string published = Path.Combine(Path.GetTempPath(), "varve-learn-" + name + "-" + trimmed);
            if (Process("dotnet", ["publish", project, "-c", "Release", "-p:VarveTrimFor=" + trimmed, "-o", published, "-nologo", "-p:TreatWarningsAsErrors=true", .. from], root, out string built) != 0
                || built.Contains("warning", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(built);
                Console.Error.WriteLine($"learn: {name} does not publish trimmed for {trimmed} without a warning.");
                return 1;
            }

            string executable = Path.Combine(published, Path.GetFileNameWithoutExtension(project) + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (Process(executable, ["--state", state], root, out output) != 0)
            {
                Console.Error.WriteLine(output);
                Console.Error.WriteLine($"learn: {name}, trimmed, exited with a failure.");
                return 1;
            }
        }

        Dictionary<string, string> sections = Sections(output);
        string expected = Path.Combine(sample, "expected");
        if (write)
        {
            if (Directory.Exists(expected))
            {
                Directory.Delete(expected, recursive: true);
            }

            Directory.CreateDirectory(expected);
            foreach ((string section, string text) in sections)
            {
                File.WriteAllText(Path.Combine(expected, section + ".txt"), text + "\n");
            }

            File.Copy(state, Path.Combine(expected, "state.nq"));
        }
        else
        {
            HashSet<string> files = Directory.Exists(expected)
                ? [.. Directory.EnumerateFiles(expected, "*.txt").Select(f => Path.GetFileNameWithoutExtension(f))]
                : [];
            foreach (string missing in files.Except(sections.Keys).Order(StringComparer.Ordinal))
            {
                mismatches.Add($"{name}: printed no section `{missing}`, which expected/ holds.");
            }

            foreach ((string section, string text) in sections)
            {
                string file = Path.Combine(expected, section + ".txt");
                if (!File.Exists(file))
                {
                    mismatches.Add($"{name}: printed section `{section}`, which expected/ does not hold.");
                }
                else if (Trimmed(File.ReadAllText(file)) != text)
                {
                    mismatches.Add($"{name}: section `{section}` differs from expected/{section}.txt:\n{text}");
                }
            }

            string expectedState = Path.Combine(expected, "state.nq");
            if (!File.Exists(expectedState) || !File.ReadAllBytes(expectedState).AsSpan().SequenceEqual(File.ReadAllBytes(state)))
            {
                mismatches.Add($"{name}: its final state differs from expected/state.nq.");
            }
        }

        File.Delete(state);
        Console.WriteLine($"learn: {name} ran in {stopwatch.Elapsed.TotalSeconds:F1} s.");
    }

    foreach (string mismatch in mismatches)
    {
        Console.WriteLine("learn: " + mismatch);
    }

    return mismatches.Count == 0 ? 0 : 1;
}

static Dictionary<string, string> Sections(string output)
{
    Dictionary<string, string> sections = new(StringComparer.Ordinal);
    string? current = null;
    List<string> body = [];
    foreach (string line in Lines(output))
    {
        Match header = Regex.Match(line, @"^== ([\w-]+) ==$");
        if (header.Success)
        {
            Flush();
            current = header.Groups[1].Value;
            continue;
        }

        body.Add(line);
    }

    Flush();
    return sections;

    void Flush()
    {
        if (current is not null)
        {
            sections[current] = Trimmed(string.Join('\n', body));
        }

        body.Clear();
    }
}

static void WritePages(string root)
{
    foreach (string page in Directory.EnumerateFiles(Path.Combine(root, "docs", "learn"), "*.md").Order(StringComparer.Ordinal))
    {
        string[] lines = Lines(File.ReadAllText(page));
        List<string> result = [];
        Match? quoted = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            Match marker = Regex.Match(line, @"^<!-- ci: .* — (output|source) (\S+?)(?:#([\w-]+))? -->$");
            if (marker.Success)
            {
                quoted = marker;
                result.Add(line);
                continue;
            }

            if (quoted is not null && line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                string fence = line.TrimStart()[..(line.TrimStart().Length - line.TrimStart().TrimStart('`').Length)];
                result.Add(line);
                int end = i + 1;
                while (end < lines.Length && !lines[end].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                {
                    end++;
                }

                string file = Path.Combine(root, quoted.Groups[2].Value);
                string? text = !File.Exists(file) ? null
                    : quoted.Groups[3].Success ? Region(File.ReadAllText(file), quoted.Groups[3].Value)
                    : Trimmed(File.ReadAllText(file));
                result.AddRange(text is null ? lines[(i + 1)..end] : Lines(text));
                if (end < lines.Length)
                {
                    result.Add(lines[end]);
                }

                i = end;
                quoted = null;
                continue;
            }

            if (line.Trim().Length > 0)
            {
                quoted = null;
            }

            result.Add(line);
        }

        File.WriteAllText(page, string.Join('\n', result) + "\n");
    }
}

// --- fixtures --------------------------------------------------------------

static int Fixtures(string directory)
{
    int failures = 0;
    foreach (string fixture in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
    {
        string expect = Path.Combine(fixture, "expect.txt");
        List<string> findings = Check(fixture);
        string name = Path.GetFileName(fixture);
        if (!File.Exists(expect))
        {
            if (findings.Count != 0)
            {
                Console.WriteLine($"learn: fixture {name} should hold, and has: " + string.Join("; ", findings));
                failures++;
            }
            else
            {
                Console.WriteLine($"learn: fixture {name} holds, as it should.");
            }

            continue;
        }

        List<string> missing = [.. File.ReadAllLines(expect).Where(l => l.Length > 0)
            .Where(l => !findings.Any(f => f.Contains(l, StringComparison.Ordinal)))];
        if (findings.Count == 0 || missing.Count != 0)
        {
            Console.WriteLine($"learn: fixture {name} did not fail for its reason. Missing: " + string.Join("; ", missing) + ". Found: " + string.Join("; ", findings));
            failures++;
        }
        else
        {
            Console.WriteLine($"learn: fixture {name} fails for its reason.");
        }
    }

    return failures == 0 ? 0 : 1;
}

// --- text ------------------------------------------------------------------

static int? ChapterOf(string name) =>
    name.Length > 3 && char.IsAsciiDigit(name[0]) && char.IsAsciiDigit(name[1]) && name[2] == '-'
        ? (name[0] - '0') * 10 + (name[1] - '0')
        : null;

static string? Region(string text, string region)
{
    string[] lines = Lines(text);
    int start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*(//|#) region: " + Regex.Escape(region) + @"\s*$"));
    int end = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*(//|#) endregion: " + Regex.Escape(region) + @"\s*$"));
    if (start < 0 || end <= start)
    {
        return null;
    }

    string[] body = lines[(start + 1)..end];
    int indent = body.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
    return Trimmed(string.Join('\n', body.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())));
}

/// The block quote that starts at the first non-blank line from <paramref name="from"/>, as one line.
static string Quote(string[] lines, int from)
{
    int i = from;
    while (i < lines.Length && lines[i].Trim().Length == 0)
    {
        i++;
    }

    List<string> quote = [];
    while (i < lines.Length && lines[i].StartsWith('>'))
    {
        quote.Add(quote.Count == 0 ? lines[i] : lines[i].TrimStart('>').Trim());
        i++;
    }

    return string.Join(' ', quote);
}

static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

static string Trimmed(string text)
{
    string[] lines = Lines(text);
    int start = 0;
    int end = lines.Length;
    while (start < end && lines[start].Trim().Length == 0)
    {
        start++;
    }

    while (end > start && lines[end - 1].Trim().Length == 0)
    {
        end--;
    }

    return string.Join('\n', lines[start..end].Select(l => l.TrimEnd()));
}

static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static int Process(string file, string[] arguments, string directory, out string output)
{
    ProcessStartInfo start = new(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file + ".");
    StringBuilder captured = new();
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (captured) { captured.Append(e.Data).Append('\n'); } } };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (captured) { captured.Append(e.Data).Append('\n'); } } };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    process.WaitForExit();
    output = captured.ToString();
    return process.ExitCode;
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

    return Directory.GetCurrentDirectory();
}

static partial class Program
{
    [GeneratedRegex(@"(?<![\w/#.])(?:rec|brick):[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex TermPattern();

    [GeneratedRegex(@"https?://(w3id\.org/rec|brickschema\.org/schema/Brick)#([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TermIriPattern();
}
