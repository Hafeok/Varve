// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Required-check names gate.
//
//   dotnet run eng/required-checks.cs -- [--declaration <repo-standard.yaml>] [--workflows <dir>]
//
// ADR 0088: ruleset 1 requires every gate by its job name, and a required name
// that no job reports leaves main unable to accept anything: GitHub waits for
// a check that never arrives. repo-standard's schema does not look inside a
// rule's parameters, so a misspelt or renamed name passes everything else.
//
// This reads every `context:` under required_status_checks in
// .github/repo-standard.yaml, and every job name the workflows in
// .github/workflows/ report: a job's `name:`, or its id when it has none, with
// `${{ matrix.<key> }}` expanded over the job's matrix (a list, or `include`
// entries). It fails when a required name is not reported by any job, or only
// by a workflow that does not run on pull requests, where it could never pass.
//
// The workflows are read as text, line by line, in the shape this repository
// writes them: jobs two spaces in, their keys four, a matrix's values eight
// or ten. A name it cannot resolve is "could not run", never a pass.
//
// A required check may be pinned to an App with `integration_id` (ADR 0087,
// amendment of 2026-10-06). A pinned check is not a job: it is posted by the
// gates App, which a workflow declares with `GATES_CHECK_NAME: <name>` on the
// step that posts it. So a pinned name must be posted that way by a workflow
// that runs on pull_request_target, whose definition comes from main. And a
// required name the App posts must be pinned: unpinned, any job a pull
// request adds under that name satisfies it, which is the spoofing the pin
// exists to stop.
//
// --declaration and --workflows aim it elsewhere, which is how its failure path
// is proven: tests/fixtures/required-checks/ names a job that does not exist.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0088-only-checked-commits-reach-main.md.

using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();
string declaration = Path.Combine(repositoryRoot, ".github", "repo-standard.yaml");
string workflows = Path.Combine(repositoryRoot, ".github", "workflows");

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    if (arguments[i] is "--declaration" && i + 1 < arguments.Length)
    {
        declaration = Path.GetFullPath(arguments[++i]);
    }
    else if (arguments[i] is "--workflows" && i + 1 < arguments.Length)
    {
        workflows = Path.GetFullPath(arguments[++i]);
    }
}

if (!File.Exists(declaration) || !Directory.Exists(workflows))
{
    Console.Error.WriteLine($"required-checks: need a declaration at '{declaration}' and workflows in '{workflows}'.");
    return 2;
}

// --- the required names ------------------------------------------------------

List<(string Name, long? Integration)> required = [];
Regex context = new(@"^\s*-\s*context:\s*(?<name>.+?)\s*$", RegexOptions.CultureInvariant);
Regex integration = new(@"^\s+integration_id:\s*(?<id>\d+)\s*$", RegexOptions.CultureInvariant);

foreach (string line in File.ReadAllLines(declaration))
{
    Match match = context.Match(line);

    if (match.Success)
    {
        required.Add((Unquote(match.Groups["name"].Value), null));
    }
    else if (required.Count > 0 && (match = integration.Match(line)).Success)
    {
        required[^1] = (required[^1].Name, long.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}

// --- the reported names ------------------------------------------------------

Dictionary<string, (string Workflow, bool OnPullRequest)> reported = new(StringComparer.Ordinal);
Dictionary<string, (string Workflow, bool OnTarget)> appPosted = new(StringComparer.Ordinal);
List<string> problems = [];
Regex gatesCheck = new(@"^\s+GATES_CHECK_NAME:\s*(?<name>.+?)\s*$", RegexOptions.CultureInvariant);

foreach (string file in Directory.EnumerateFiles(workflows, "*.yml").Concat(Directory.EnumerateFiles(workflows, "*.yaml")).Order(StringComparer.Ordinal))
{
    string workflow = Path.GetFileName(file);
    string[] lines = File.ReadAllLines(file);
    bool onPullRequest = RunsOnPullRequests(lines);

    bool onTarget = lines.Any(line => Regex.IsMatch(line, @"^\s+pull_request_target\s*:"));

    foreach (string line in lines)
    {
        Match posted = gatesCheck.Match(line);

        if (posted.Success && (!appPosted.TryGetValue(Unquote(posted.Groups["name"].Value), out var seen) || (!seen.OnTarget && onTarget)))
        {
            appPosted[Unquote(posted.Groups["name"].Value)] = (workflow, onTarget);
        }
    }

    foreach (string name in JobNames(lines, workflow, problems))
    {
        // A name reported by any workflow that runs on pull requests is enough.
        if (!reported.TryGetValue(name, out var known) || (!known.OnPullRequest && onPullRequest))
        {
            reported[name] = (workflow, onPullRequest);
        }
    }
}

if (problems.Count > 0)
{
    foreach (string problem in problems)
    {
        Console.Error.WriteLine($"required-checks: {problem}");
    }

    return 2;
}

// --- the comparison ------------------------------------------------------------

List<string> findings = [];

foreach ((string name, long? pin) in required)
{
    if (pin is long id)
    {
        if (!appPosted.TryGetValue(name, out var poster))
        {
            findings.Add($"'{name}' is pinned to App {id}, and no workflow posts it as the gates App (GATES_CHECK_NAME)");
        }
        else if (!poster.OnTarget)
        {
            findings.Add($"'{name}' is pinned to App {id}, and only {poster.Workflow} posts it, which does not run on pull_request_target");
        }

        continue;
    }

    if (appPosted.ContainsKey(name))
    {
        findings.Add($"'{name}' is posted by the gates App but required without integration_id, so any job named '{name}' satisfies it; pin it");
    }
    else if (!reported.TryGetValue(name, out var source))
    {
        findings.Add($"'{name}' is required, and no job in {Path.GetFileName(workflows)}/ reports it");
    }
    else if (!source.OnPullRequest)
    {
        findings.Add($"'{name}' is required, and only {source.Workflow} reports it, which does not run on pull requests");
    }
}

int pinnedCount = required.Count(check => check.Integration is not null);
Console.WriteLine($"note {required.Count} required check(s), {pinnedCount} pinned to an App; {reported.Count} job name(s) and {appPosted.Count} App-posted name(s) across the workflows");

foreach ((string name, long? pin) in required.Where(check => check.Integration is not null))
{
    Console.WriteLine($"note pinned: '{name}' to App {pin}");
}

if (findings.Count == 0)
{
    Console.WriteLine("ok  every required check is a job that reports on pull requests, or a pinned check the gates App posts");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: {findings.Count} required check(s) nothing can satisfy, or anything can (ADR 0088):");

foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

Console.Error.WriteLine();
Console.Error.WriteLine("Fix the job, the posted name or the required check in the same change, or main accepts nothing (or anything).");
return 1;

// --- helpers ---------------------------------------------------------------

static string Unquote(string value)
{
    value = value.Trim();
    return value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')
        ? value[1..^1]
        : value;
}

// Whether the top-level `on:` block names pull_request (or pull_request_target).
static bool RunsOnPullRequests(string[] lines)
{
    bool inOn = false;

    foreach (string line in lines)
    {
        if (line.StartsWith("on:", StringComparison.Ordinal))
        {
            if (line.Contains("pull_request", StringComparison.Ordinal))
            {
                return true;
            }

            inOn = true;
            continue;
        }

        if (inOn)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                return false;
            }

            if (Regex.IsMatch(line, @"^\s+-?\s*pull_request(_target)?\b"))
            {
                return true;
            }
        }
    }

    return false;
}

static IEnumerable<string> JobNames(string[] lines, string workflow, List<string> problems)
{
    Regex jobKey = new(@"^  (?<id>[A-Za-z0-9_-]+):\s*$");
    Regex nameKey = new(@"^    name:\s*(?<name>.+?)\s*$");
    Regex listMatrix = new(@"^        (?<key>[A-Za-z0-9_-]+):\s*\[(?<values>[^\]]*)\]\s*$");
    Regex includeEntry = new(@"^          -\s*(?<key>[A-Za-z0-9_-]+):\s*(?<value>.+?)\s*$");
    Regex placeholder = new(@"\$\{\{\s*matrix\.(?<key>[A-Za-z0-9_-]+)\s*\}\}");

    List<(string Id, string? Name, Dictionary<string, List<string>> Matrix)> jobs = [];
    bool inJobs = false;

    foreach (string line in lines)
    {
        if (line.StartsWith("jobs:", StringComparison.Ordinal))
        {
            inJobs = true;
            continue;
        }

        if (!inJobs)
        {
            continue;
        }

        if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
        {
            break;
        }

        Match match;

        if ((match = jobKey.Match(line)).Success)
        {
            jobs.Add((match.Groups["id"].Value, null, new Dictionary<string, List<string>>(StringComparer.Ordinal)));
        }
        else if (jobs.Count > 0 && jobs[^1].Name is null && (match = nameKey.Match(line)).Success)
        {
            jobs[^1] = (jobs[^1].Id, Unquote(match.Groups["name"].Value), jobs[^1].Matrix);
        }
        else if (jobs.Count > 0 && (match = listMatrix.Match(line)).Success)
        {
            jobs[^1].Matrix[match.Groups["key"].Value] =
                [.. match.Groups["values"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote)];
        }
        else if (jobs.Count > 0 && (match = includeEntry.Match(line)).Success)
        {
            string key = match.Groups["key"].Value;

            if (!jobs[^1].Matrix.TryGetValue(key, out List<string>? values))
            {
                jobs[^1].Matrix[key] = values = [];
            }

            values.Add(Unquote(match.Groups["value"].Value));
        }
    }

    foreach ((string id, string? name, Dictionary<string, List<string>> matrix) in jobs)
    {
        string template = name ?? id;
        MatchCollection keys = placeholder.Matches(template);

        if (keys.Count == 0)
        {
            yield return template;
            continue;
        }

        // One matrix dimension per name is all this repository writes; more is
        // not guessed at.
        string[] distinct = [.. keys.Select(k => k.Groups["key"].Value).Distinct(StringComparer.Ordinal)];

        if (distinct.Length != 1 || !matrix.TryGetValue(distinct[0], out List<string>? values) || values.Count == 0)
        {
            problems.Add($"{workflow}: the name of job '{id}', '{template}', names a matrix value this cannot expand");
            continue;
        }

        foreach (string value in values)
        {
            yield return placeholder.Replace(template, value);
        }
    }
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
