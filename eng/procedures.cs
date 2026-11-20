// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Every procedure in the operator's guide names what runs it (ADR 0248, #129).
//
//   dotnet run eng/procedures.cs [-- --root <directory>] [--report]
//
// The guide is docs/operator/*.md and docs/auth-providers.md. A procedure is
// a fenced code block there: a command, a configuration to apply, a
// declaration, a request. The line before it (blank lines aside) is its
// marker, an HTML comment the rendered page does not show:
//
//   <!-- ci: ci.yml auth-zitadel; eng/ci.cs test-server — ApplyTests -->
//   <!-- ci: none — the test tenant is not provisioned (ADR 0223) -->
//   <!-- ci: example — what the commits list shows for both writes -->
//
// A run marker names one or more jobs, each a workflow file under
// .github/workflows/ and a job id of it, or eng/ci.cs and a job name of its
// list; after " — ", a note may name the test that runs it. A job that does
// not exist fails. `none` needs its reason, and a page with one has a
// section headed "Not run in CI" that lists what nothing runs. `example` is
// for a block that is no procedure at all, such as a command's output or a
// response, and says what it shows. A page whose
// markers belong to another pull request is exempt by name in
// eng/procedures-exemptions.txt, and an exemption for a page with no
// unmarked block fails, so none outlives its reason.
//
// What it does not check: that the named job runs exactly the block. That
// is review's, against the note. A step list written as prose rather than a
// code block is marked by hand where it is a procedure, and not checked.
//
// --report prints every procedure no job runs, by page, with its reason.
//
// Exit codes: 0 every procedure is marked, 1 one is not, 2 could not run.

using System.Text.RegularExpressions;

string root = ".";
bool report = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length)
    {
        root = args[++i];
    }
    else if (args[i] == "--report")
    {
        report = true;
    }
    else
    {
        Console.Error.WriteLine("procedures: [--root <directory>] [--report]");
        return 2;
    }
}

string guide = Path.Combine(root, "docs", "operator");
string workflows = Path.Combine(root, ".github", "workflows");
string ciScript = Path.Combine(root, "eng", "ci.cs");

if (!Directory.Exists(guide) || !Directory.Exists(workflows) || !File.Exists(ciScript))
{
    Console.Error.WriteLine($"procedures: {root} has no docs/operator/, .github/workflows/ or eng/ci.cs.");
    return 2;
}

// The jobs that exist: each workflow's top-level job ids, and eng/ci.cs's names.
Dictionary<string, HashSet<string>> jobs = new(StringComparer.Ordinal);

foreach (string workflow in Directory.EnumerateFiles(workflows, "*.yml"))
{
    HashSet<string> ids = new(StringComparer.Ordinal);
    bool inJobs = false;

    foreach (string line in File.ReadLines(workflow))
    {
        if (Regex.IsMatch(line, @"^jobs:\s*$"))
        {
            inJobs = true;
            continue;
        }

        if (inJobs && Regex.IsMatch(line, @"^\S"))
        {
            inJobs = false;
        }

        if (inJobs && Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*(#.*)?$") is { Success: true } job)
        {
            ids.Add(job.Groups[1].Value);
        }
    }

    jobs[Path.GetFileName(workflow)] = ids;
}

jobs["eng/ci.cs"] = new HashSet<string>(
    Regex.Matches(File.ReadAllText(ciScript), "^\\s*\\(\"([a-z0-9-]+)\",", RegexOptions.Multiline).Select(static m => m.Groups[1].Value),
    StringComparer.Ordinal);

// Pages exempt by name, each with the pull request that brings its markers.
Dictionary<string, string> exempt = new(StringComparer.Ordinal);
string exemptions = Path.Combine(root, "eng", "procedures-exemptions.txt");

if (File.Exists(exemptions))
{
    foreach (string raw in File.ReadLines(exemptions))
    {
        string line = raw.Trim();

        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            Console.Error.WriteLine($"procedures: eng/procedures-exemptions.txt: '{line}' needs the page and its reason.");
            return 2;
        }

        exempt[parts[0]] = parts[1];
    }
}

List<string> pages = [.. Directory.EnumerateFiles(guide, "*.md").Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))];

if (File.Exists(Path.Combine(root, "docs", "auth-providers.md")))
{
    pages.Add("docs/auth-providers.md");
}

pages.Sort(StringComparer.Ordinal);

Regex fence = new(@"^(\s*)(`{3,}|~{3,})(.*)$");
Regex marker = new(@"^\s*<!-- ci: (.+?) -->\s*$");
List<string> findings = [];
List<string> unrun = [];
int blocks = 0, run = 0, none = 0, examples = 0, exemptBlocks = 0;

foreach (string page in pages)
{
    string[] lines = File.ReadAllLines(Path.Combine(root, page));
    bool pageHasNone = false;
    int unmarked = 0;
    string? open = null;

    for (int i = 0; i < lines.Length; i++)
    {
        Match f = fence.Match(lines[i]);

        if (!f.Success)
        {
            continue;
        }

        if (open is not null)
        {
            // A closing fence is the same character, at least as long, and bare.
            if (f.Groups[2].Value.StartsWith(open, StringComparison.Ordinal) && f.Groups[3].Value.Trim().Length == 0)
            {
                open = null;
            }

            continue;
        }

        open = f.Groups[2].Value;
        blocks++;
        string where = $"{page}:{i + 1}";

        int previous = i - 1;

        while (previous >= 0 && lines[previous].Trim().Length == 0)
        {
            previous--;
        }

        Match m = previous >= 0 ? marker.Match(lines[previous]) : Match.Empty;

        if (!m.Success)
        {
            unmarked++;

            if (exempt.ContainsKey(page))
            {
                exemptBlocks++;
                continue;
            }

            findings.Add($"{where}: a procedure with no marker; put <!-- ci: <workflow> <job> --> or <!-- ci: none — <reason> --> on the line before it");
            continue;
        }

        string body = m.Groups[1].Value.Trim();

        if (Regex.Match(body, @"^example\s*(?:—|:|-)\s*(.*)$") is { Success: true } shows)
        {
            if (shows.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            {
                findings.Add($"{where}: 'example' says what it shows, in words");
                continue;
            }

            examples++;
            continue;
        }

        if (Regex.Match(body, @"^none(?:\s*(?:—|:|-)\s*(.*))?$") is { Success: true } reason)
        {
            string why = reason.Groups[1].Value.Trim();

            if (why.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3)
            {
                findings.Add($"{where}: 'none' needs its reason, in words: why no job can run it");
                continue;
            }

            none++;
            pageHasNone = true;
            unrun.Add($"{where}: {why}");
            continue;
        }

        string refs = body.Split(" — ", 2)[0];
        bool good = true;

        foreach (string reference in refs.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = reference.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                findings.Add($"{where}: '{reference}' is not '<workflow file> <job id>' or 'eng/ci.cs <job>'");
                good = false;
            }
            else if (!jobs.TryGetValue(parts[0], out HashSet<string>? ids))
            {
                findings.Add($"{where}: '{parts[0]}' is not a workflow under .github/workflows/, nor eng/ci.cs");
                good = false;
            }
            else if (!ids.Contains(parts[1]))
            {
                findings.Add($"{where}: {parts[0]} has no job '{parts[1]}'");
                good = false;
            }
        }

        if (good)
        {
            run++;
        }
    }

    if (pageHasNone && !lines.Any(static l => Regex.IsMatch(l, @"^#{2,3} Not run in CI\s*$")))
    {
        findings.Add($"{page}: it marks procedures no job runs, and has no section headed \"Not run in CI\" listing them");
    }

    if (exempt.ContainsKey(page) && unmarked == 0)
    {
        findings.Add($"eng/procedures-exemptions.txt: {page} has no unmarked procedure; remove its exemption");
    }
}

foreach (string page in exempt.Keys.Where(p => !pages.Contains(p)))
{
    findings.Add($"eng/procedures-exemptions.txt: {page} is not a page of the guide");
}

foreach (string finding in findings)
{
    Console.Error.WriteLine($"procedures: {finding}");
}

if (report)
{
    Console.WriteLine("Procedures no CI job runs:");

    foreach (string line in unrun)
    {
        Console.WriteLine($"  {line}");
    }
}

Console.WriteLine($"procedures: {pages.Count} pages, {blocks} blocks: {run} procedures run by a job, {none} run by none (each with its reason), {examples} examples, {exemptBlocks} on exempt pages; {findings.Count} findings.");
return findings.Count == 0 ? 0 : 1;
