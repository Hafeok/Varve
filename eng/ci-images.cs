// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// CI image gate, and the local override.
//
//   dotnet run eng/ci-images.cs -- [--root <dir>]
//   dotnet run eng/ci-images.cs -- --local <compose file> [--output <file>]
//
// #118 and ADR 0099's amendment of 2026-10-19: every third-party test
// container CI starts is pulled from this repository's private mirror,
// ghcr.io/hafeok/varve-ci/<name>, at the digest .github/ci-images.yaml pins.
// The gate reads that list and every image reference it can find, and fails
// on one that is not:
//
//   - the mirror's, `ghcr.io/hafeok/varve-ci/<name>`, for a mirrored row of
//     that name, pinned to the row's digest (and its tag, when it has one);
//   - an upstream reference at a row's digest, where the row is
//     `mirror: false` and states its `reason`;
//   - in C# outside .github/, an upstream reference to a row's image, where
//     the row's `allowances` name the file and a CI job that overrides it to
//     the mirror, and the file pins the row's digest; in that job, the mirror
//     repository without a digest is the override, and passes;
//   - ghcr.io/hafeok/varve, this repository's own image, which CI builds.
//
// It looks in every .yml, .yaml, .json and .sh file under .github/ (an
// `image:` key, a `docker pull`, a `uses: docker://`, and any token naming a
// known registry), and in every C# string literal under src/, tests/,
// samples/, eng/ and tools/ that names an image, or that is the image
// argument of AddContainer. An `image:` with no registry is Docker Hub's,
// and is caught as one. An `image:` that is a workflow expression is not a
// reference; the literal it is built from is. The files that read the list
// rather than name an image are not scanned: the list, the mirror workflow
// and the upstream comparison, and this file.
//
// --root aims it at another tree, which is how its failure path is proven:
// tests/fixtures/ci-images/ references an upstream image directly.
//
// --local writes a compose override that replaces each mirror reference in
// the compose file with the upstream reference at the same digest, for a
// local run that cannot read the private packages (CONTRIBUTING.md), and
// prints its path. The default output is
// artifacts/ci-images/<directory>.compose.local.yaml.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.

using System.Text;
using System.Text.RegularExpressions;

const string Mirror = "ghcr.io/hafeok/varve-ci/";
const string Own = "ghcr.io/hafeok/varve";

string root = FindRepositoryRoot();
string? local = null;
string? output = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--root" && i + 1 < args.Length)
    {
        root = Path.GetFullPath(args[++i]);
    }
    else if (args[i] is "--local" && i + 1 < args.Length)
    {
        local = Path.GetFullPath(args[++i]);
    }
    else if (args[i] is "--output" && i + 1 < args.Length)
    {
        output = Path.GetFullPath(args[++i]);
    }
    else
    {
        Console.Error.WriteLine($"ci-images: unknown argument '{args[i]}'.");
        return 2;
    }
}

string listFile = Path.Combine(root, ".github", "ci-images.yaml");
List<string> problems = [];
List<Row> rows = File.Exists(listFile) ? ReadList(listFile, problems) : [];

if (!File.Exists(listFile) || problems.Count > 0)
{
    Console.Error.WriteLine($"ci-images: could not read '{listFile}'.");
    problems.ForEach(problem => Console.Error.WriteLine($"  {problem}"));
    return 2;
}

if (local is not null)
{
    return WriteLocal(local, output ?? Path.Combine(root, "artifacts", "ci-images", $"{Path.GetFileName(Path.GetDirectoryName(local))}.compose.local.yaml"), rows);
}

List<string> findings = [];

// --- the list itself ---------------------------------------------------------

foreach (IGrouping<string, Row> twice in rows.GroupBy(row => row.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
{
    findings.Add($"ci-images.yaml: '{twice.Key}' is listed {twice.Count()} times");
}

foreach (Row row in rows)
{
    if (!Regex.IsMatch(row.Digest, "^sha256:[0-9a-f]{64}$"))
    {
        findings.Add($"ci-images.yaml: '{row.Name}' has digest '{row.Digest}', not sha256:<64 hex>");
    }

    if (Reference.Parse(row.Upstream) is not { Tag: not null, Digest: null })
    {
        findings.Add($"ci-images.yaml: '{row.Name}' has upstream '{row.Upstream}', not <registry>/<repository>:<tag>");
    }

    if (!row.Mirror && string.IsNullOrWhiteSpace(row.Reason))
    {
        findings.Add($"ci-images.yaml: '{row.Name}' is mirror: false without a reason");
    }

    foreach (Allowance allowance in row.Allowances)
    {
        if (!File.Exists(Path.Combine(root, allowance.File)))
        {
            findings.Add($"ci-images.yaml: '{row.Name}' allows '{allowance.File}', which does not exist");
        }
    }
}

// --- the jobs, for allowances -----------------------------------------------

Dictionary<string, string> jobs = ReadJobs(Path.Combine(root, ".github", "workflows"));

// --- the references ----------------------------------------------------------

string[] notScanned =
[
    ".github/ci-images.yaml",
    ".github/workflows/mirror-ci-images.yml",
    ".github/workflows/ci-images-upstream.yml",
    "eng/ci-images.cs",
    "tests/fixtures/ci-images/",
];

int references = 0;
HashSet<string> allowancesUsed = new(StringComparer.Ordinal);

foreach (string file in Files(Path.Combine(root, ".github"), ["*.yml", "*.yaml", "*.json", "*.sh"]))
{
    string relative = Relative(root, file);

    if (notScanned.Any(skip => relative == skip || skip.EndsWith('/') && relative.StartsWith(skip, StringComparison.Ordinal)))
    {
        continue;
    }

    string[] lines = File.ReadAllLines(file);

    for (int number = 1; number <= lines.Length; number++)
    {
        foreach (string text in InWorkflowLine(Uncomment(lines[number - 1], file)))
        {
            references++;
            Check(text, relative, number, cSharp: false, line: lines[number - 1]);
        }
    }
}

foreach (string directory in new[] { "src", "tests", "samples", "eng", "tools" })
{
    foreach (string file in Files(Path.Combine(root, directory), ["*.cs"]))
    {
        string relative = Relative(root, file);

        if (notScanned.Any(skip => relative == skip || skip.EndsWith('/') && relative.StartsWith(skip, StringComparison.Ordinal))
            || relative.Contains("/bin/", StringComparison.Ordinal)
            || relative.Contains("/obj/", StringComparison.Ordinal))
        {
            continue;
        }

        string[] lines = File.ReadAllLines(file);

        for (int number = 1; number <= lines.Length; number++)
        {
            foreach (string text in InCSharpLine(lines[number - 1]))
            {
                references++;
                Check(text, relative, number, cSharp: true, line: lines[number - 1], content: string.Join('\n', lines));
            }
        }
    }
}

foreach (Row row in rows)
{
    foreach (Allowance allowance in row.Allowances.Where(allowance => !allowancesUsed.Contains($"{row.Name}|{allowance.File}")))
    {
        findings.Add($"ci-images.yaml: '{row.Name}' allows '{allowance.File}', which names no upstream reference to it; remove the allowance");
    }
}

Console.WriteLine($"note {rows.Count} image(s) listed, {rows.Count(row => row.Mirror)} mirrored; {references} reference(s) found");

if (findings.Count == 0)
{
    Console.WriteLine($"ok  every image reference is {Mirror}<name> at its listed digest, or an allowed upstream one");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: {findings.Count} image reference(s) or list entries outside the mirror rule (#118, ADR 0099):");
findings.ForEach(finding => Console.Error.WriteLine($"  {finding}"));
Console.Error.WriteLine();
Console.Error.WriteLine($"Reference {Mirror}<name>@<digest> from .github/ci-images.yaml; see CONTRIBUTING.md, \"CI's test containers\".");
return 1;

void Check(string text, string file, int number, bool cSharp, string line, string? content = null)
{
    Reference reference = Reference.Parse(text);
    string where = $"{file}:{number}";

    if (reference.Repository == Own)
    {
        return;
    }

    if (reference.Repository.StartsWith(Mirror, StringComparison.Ordinal))
    {
        string name = reference.Repository[Mirror.Length..];
        Row? row = rows.FirstOrDefault(row => row.Name == name);

        if (row is null)
        {
            findings.Add($"{where}: {text} names '{name}', which ci-images.yaml does not list");
        }
        else if (!row.Mirror)
        {
            findings.Add($"{where}: {text} names '{name}', which ci-images.yaml keeps upstream (mirror: false)");
        }
        else if (reference.Digest is null && !(cSharp && content!.Contains(row.Digest["sha256:".Length..], StringComparison.Ordinal)) && !OverridesAllowance(row, line))
        {
            findings.Add($"{where}: {text} is not pinned by digest; pin {row.Digest}");
        }
        else if (reference.Digest is not null && reference.Digest != row.Digest)
        {
            findings.Add($"{where}: {text} is at {reference.Digest}, and ci-images.yaml pins {row.Digest}");
        }
        else if (reference.Tag is not null && reference.Tag != Reference.Parse(row.Upstream).Tag)
        {
            findings.Add($"{where}: {text} is tagged '{reference.Tag}', and ci-images.yaml's upstream is '{row.Upstream}'");
        }

        return;
    }

    Row? upstream = rows.FirstOrDefault(row => Reference.Parse(row.Upstream).Repository == reference.Repository);

    if (upstream is null)
    {
        findings.Add($"{where}: {text} is not in ci-images.yaml; list it and pull it from the mirror");
        return;
    }

    if (!upstream.Mirror)
    {
        if (reference.Digest != upstream.Digest && !(cSharp && content!.Contains(upstream.Digest["sha256:".Length..], StringComparison.Ordinal)))
        {
            findings.Add($"{where}: {text} is kept upstream, and must be pinned to {upstream.Digest}");
        }

        return;
    }

    if (!cSharp)
    {
        findings.Add($"{where}: {text} is upstream; CI pulls {Mirror}{upstream.Name}@{upstream.Digest}");
        return;
    }

    Allowance? allowance = upstream.Allowances.FirstOrDefault(allowance => allowance.File == file);

    if (allowance is null)
    {
        findings.Add($"{where}: {text} is upstream, and ci-images.yaml's '{upstream.Name}' has no allowance for this file naming the CI job that overrides it to {Mirror}{upstream.Name}");
        return;
    }

    allowancesUsed.Add($"{upstream.Name}|{file}");

    if (!content!.Contains(upstream.Digest["sha256:".Length..], StringComparison.Ordinal))
    {
        findings.Add($"{where}: {text} is allowed upstream, and this file does not pin {upstream.Digest}");
    }

    if (!jobs.TryGetValue(allowance.Job, out string? job))
    {
        findings.Add($"{where}: the allowance names job '{allowance.Job}', which no workflow has");
    }
    else if (!job.Contains($"{Mirror}{upstream.Name}", StringComparison.Ordinal))
    {
        findings.Add($"{where}: the allowance names job '{allowance.Job}', which does not set {Mirror}{upstream.Name}");
    }
}

// Whether the line is in a job one of the row's allowances names: there a
// mirror repository without a digest is the override of an allowed C#
// reference, whose file pins the digest.
bool OverridesAllowance(Row row, string line)
{
    return row.Allowances.Any(allowance => jobs.TryGetValue(allowance.Job, out string? job) && job.Contains(line, StringComparison.Ordinal));
}

// --- reading ---------------------------------------------------------------

static List<Row> ReadList(string file, List<string> problems)
{
    List<Row> rows = [];
    Dictionary<string, string>? current = null;
    List<Allowance> allowances = [];
    string? folding = null;
    bool inAllowances = false;
    Dictionary<string, string>? allowance = null;
    string[] lines = File.ReadAllLines(file);

    void Close()
    {
        if (allowance is not null)
        {
            if (allowance.TryGetValue("file", out string? path) && allowance.TryGetValue("job", out string? job))
            {
                allowances.Add(new Allowance(path, job));
            }
            else
            {
                problems.Add("an allowance needs a file and a job");
            }

            allowance = null;
        }
    }

    void Finish()
    {
        Close();

        if (current is null)
        {
            return;
        }

        foreach (string key in new[] { "name", "upstream", "digest" }.Where(key => !current.ContainsKey(key)))
        {
            problems.Add($"a row has no '{key}'");
        }

        if (current.ContainsKey("name") && current.ContainsKey("upstream") && current.ContainsKey("digest"))
        {
            bool mirror = !current.TryGetValue("mirror", out string? value) || value != "false";
            rows.Add(new Row(current["name"], current["upstream"], current["digest"], mirror, current.GetValueOrDefault("reason"), [.. allowances]));
        }

        current = null;
        allowances = [];
    }

    bool inImages = false;

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i];

        if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0)
        {
            continue;
        }

        if (line == "images:")
        {
            inImages = true;
            continue;
        }

        if (!inImages)
        {
            problems.Add($"line {i + 1}: expected 'images:' first");
            continue;
        }

        Match match;

        if (folding is not null && line.StartsWith("      ", StringComparison.Ordinal) && !inAllowances)
        {
            current![folding] = (current[folding] + " " + line.Trim()).Trim();
            continue;
        }

        folding = null;

        if ((match = Regex.Match(line, @"^  - name:\s*(?<value>\S+)\s*$")).Success)
        {
            Finish();
            current = new(StringComparer.Ordinal) { ["name"] = match.Groups["value"].Value };
            inAllowances = false;
        }
        else if (current is not null && (match = Regex.Match(line, @"^    (?<key>[a-z-]+):\s*(?<value>.*?)\s*$")).Success)
        {
            Close();
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;
            inAllowances = key == "allowances";

            if (inAllowances)
            {
                if (value.Length > 0)
                {
                    problems.Add($"line {i + 1}: allowances is a block list");
                }
            }
            else if (value is ">-" or ">" or "|" or "|-")
            {
                current[key] = "";
                folding = key;
            }
            else
            {
                current[key] = value;
            }
        }
        else if (inAllowances && (match = Regex.Match(line, @"^      - (?<key>[a-z]+):\s*(?<value>.+?)\s*$")).Success)
        {
            Close();
            allowance = new(StringComparer.Ordinal) { [match.Groups["key"].Value] = match.Groups["value"].Value };
        }
        else if (allowance is not null && (match = Regex.Match(line, @"^        (?<key>[a-z]+):\s*(?<value>.+?)\s*$")).Success)
        {
            allowance[match.Groups["key"].Value] = match.Groups["value"].Value;
        }
        else
        {
            problems.Add($"line {i + 1}: not in the shape the list is written in: '{line.Trim()}'");
        }
    }

    Finish();
    return rows;
}

// Every job of every workflow, by its name (or its id when it has none),
// to the job's text.
static Dictionary<string, string> ReadJobs(string workflows)
{
    Dictionary<string, string> jobs = new(StringComparer.Ordinal);

    if (!Directory.Exists(workflows))
    {
        return jobs;
    }

    foreach (string file in Files(workflows, ["*.yml", "*.yaml"]))
    {
        string[] lines = File.ReadAllLines(file);
        int start = Array.IndexOf(lines, "jobs:");

        if (start < 0)
        {
            continue;
        }

        string? id = null;
        string? name = null;
        StringBuilder text = new();

        void Add()
        {
            if (id is not null)
            {
                jobs[name ?? id] = text.ToString();
            }
        }

        for (int i = start + 1; i < lines.Length; i++)
        {
            Match key = Regex.Match(lines[i], @"^  (?<id>[A-Za-z0-9_-]+):\s*$");

            if (key.Success)
            {
                Add();
                id = key.Groups["id"].Value;
                name = null;
                text.Clear();
                continue;
            }

            Match named = Regex.Match(lines[i], @"^    name:\s*(?<name>.+?)\s*$");

            if (named.Success && id is not null && name is null)
            {
                name = named.Groups["name"].Value.Trim('"', '\'');
            }

            text.AppendLine(lines[i]);
        }

        Add();
    }

    return jobs;
}

// The image references on a line of a YAML, JSON or shell file.
static IEnumerable<string> InWorkflowLine(string line)
{
    HashSet<string> found = new(StringComparer.Ordinal);
    Match image = Regex.Match(line, @"^\s*(-\s+)?image:\s*(?<value>.*?)\s*$");

    if (image.Success)
    {
        string value = image.Groups["value"].Value.Trim('"', '\'');

        if (value.Length > 0 && !value.Contains("${{", StringComparison.Ordinal) && !value.StartsWith('$'))
        {
            found.Add(value);
        }
    }

    foreach (Match pull in Regex.Matches(line, @"\bdocker\s+pull\s+(?:--?[a-z-]+(?:=\S+)?\s+)*""?(?<value>[^\s""]+)"))
    {
        if (!pull.Groups["value"].Value.Contains('$'))
        {
            found.Add(pull.Groups["value"].Value);
        }
    }

    foreach (Match uses in Regex.Matches(line, @"uses:\s*docker://(?<value>\S+)"))
    {
        found.Add(uses.Groups["value"].Value);
    }

    foreach (Match token in Patterns.RegistryToken().Matches(line))
    {
        found.Add(token.Value);
    }

    return found;
}

// The image references among a line's C# string literals.
static IEnumerable<string> InCSharpLine(string line)
{
    HashSet<string> found = new(StringComparer.Ordinal);
    Match added = Regex.Match(line, @"AddContainer\(\s*""[^""]*""\s*,\s*""(?<image>[^""]+)""");

    if (added.Success)
    {
        found.Add(added.Groups["image"].Value);
    }

    foreach (Match literal in Regex.Matches(line, @"""(?<text>[^""\\]*)"""))
    {
        string text = literal.Groups["text"].Value;
        Match token = Patterns.RegistryToken().Match(text);

        if (token.Success && token.Value == text)
        {
            found.Add(text);
        }
    }

    return found;
}

static string Uncomment(string line, string file)
{
    if (file.EndsWith(".json", StringComparison.Ordinal))
    {
        return line;
    }

    if (line.TrimStart().StartsWith('#'))
    {
        return "";
    }

    int hash = line.IndexOf(" #", StringComparison.Ordinal);
    return hash < 0 ? line : line[..hash];
}

static int WriteLocal(string compose, string output, List<Row> rows)
{
    if (!File.Exists(compose))
    {
        Console.Error.WriteLine($"ci-images: no compose file at '{compose}'.");
        return 2;
    }

    StringBuilder text = new();
    text.AppendLine("# Written by eng/ci-images.cs --local: the upstream images at the digests");
    text.AppendLine("# the mirror holds, for a run that cannot read ghcr.io/hafeok/varve-ci.");
    text.AppendLine("services:");
    string? service = null;
    int replaced = 0;

    foreach (string line in File.ReadAllLines(compose))
    {
        Match key = Regex.Match(line, @"^  (?<service>[A-Za-z0-9_.-]+):\s*$");

        if (key.Success)
        {
            service = key.Groups["service"].Value;
            continue;
        }

        Match image = Regex.Match(line, @"^    image:\s*(?<value>\S+)\s*$");

        if (!image.Success || service is null)
        {
            continue;
        }

        Reference reference = Reference.Parse(image.Groups["value"].Value);

        if (!reference.Repository.StartsWith(Mirror, StringComparison.Ordinal))
        {
            continue;
        }

        Row? row = rows.FirstOrDefault(row => row.Name == reference.Repository[Mirror.Length..]);

        if (row is null)
        {
            Console.Error.WriteLine($"ci-images: '{reference.Repository}' is not in ci-images.yaml.");
            return 2;
        }

        Reference upstream = Reference.Parse(row.Upstream);
        text.AppendLine($"  {service}:");
        text.AppendLine($"    image: {upstream.Repository}:{upstream.Tag}@{row.Digest}");
        replaced++;
    }

    if (replaced == 0)
    {
        Console.Error.WriteLine($"ci-images: '{compose}' names no image from the mirror.");
        return 2;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, text.ToString());
    Console.WriteLine(output);
    return 0;
}

static IEnumerable<string> Files(string directory, string[] patterns)
{
    return Directory.Exists(directory)
        ? patterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)).Order(StringComparer.Ordinal)
        : [];
}

static string Relative(string root, string file)
{
    return Path.GetRelativePath(root, file).Replace('\\', '/');
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

internal static partial class Patterns
{
    // A reference to a known registry, not preceded by a scheme or a path.
    [GeneratedRegex(@"(?<![\w/.:@-])(?:ghcr\.io|docker\.io|index\.docker\.io|registry-1\.docker\.io|mcr\.microsoft\.com|public\.ecr\.aws|[a-z0-9-]+\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com|quay\.io|gcr\.io|[a-z0-9-]+\.gcr\.io|[a-z0-9-]+-docker\.pkg\.dev|registry\.k8s\.io|[a-z0-9-]+\.azurecr\.io|registry\.gitlab\.com|lscr\.io|cgr\.dev)/[a-z0-9][a-z0-9._/-]*(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?(?:@sha256:[0-9a-f]{64})?", RegexOptions.CultureInvariant)]
    public static partial Regex RegistryToken();
}

internal sealed record Row(string Name, string Upstream, string Digest, bool Mirror, string? Reason, List<Allowance> Allowances);

internal sealed record Allowance(string File, string Job);

// An image reference: repository (with its registry, Docker Hub's made
// explicit), tag and digest.
internal sealed record Reference(string Repository, string? Tag, string? Digest)
{
    public static Reference Parse(string text)
    {
        string? digest = null;
        int at = text.IndexOf('@', StringComparison.Ordinal);

        if (at >= 0)
        {
            digest = text[(at + 1)..];
            text = text[..at];
        }

        string? tag = null;
        int colon = text.LastIndexOf(':');

        if (colon > text.LastIndexOf('/'))
        {
            tag = text[(colon + 1)..];
            text = text[..colon];
        }

        string first = text.Split('/')[0];

        if (!first.Contains('.') && !first.Contains(':') && first != "localhost")
        {
            text = "docker.io/" + (text.Contains('/') ? text : "library/" + text);
        }
        else if (first is "index.docker.io" or "registry-1.docker.io")
        {
            text = "docker.io" + text[first.Length..];
        }

        return new Reference(text, tag, digest);
    }
}
