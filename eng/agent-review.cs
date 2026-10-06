// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Agent review gate.
//
//   dotnet run eng/agent-review.cs        (in GitHub Actions; reads the event)
//
// ADR 0087. Two rules about who stands behind a change, which need the pull
// request and its reviews and so run only in CI, as the `agent review` job of
// .github/workflows/agent-review.yml — not in eng/ci.cs, which runs offline.
//
//   1. A pull request containing a commit an agent authored needs an approving
//      review, on its current head, from the agent's responsible human or one
//      of their delegates (eng/identities.json). A review on an earlier head
//      does not count: what was approved is not what would merge.
//   2. A human's delegates change only in that human's own pull request: if
//      eng/identities.json changes a human's delegates, the pull request's
//      author is that human.
//
// On a push to a branch other than main, neither can be satisfied, since there
// is no pull request to review or to author: a head with an agent's commit or
// a delegate change fails, and lands through a pull request instead. A push to
// main passes, because what reaches main has already passed this on its pull
// request or its branch (ADR 0088).
//
// Outside Actions it lists what rule 1 would ask for and exits 0: approvals
// are on GitHub, and a local run cannot see them.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0087-the-identity-map.md.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Identities.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

string repositoryRoot = FindRepositoryRoot();

List<string> problems = [];
IdentityMap? headMap = IdentityMap.Load(repositoryRoot, problems);

if (headMap is null || problems.Count > 0)
{
    foreach (string problem in problems)
    {
        Console.Error.WriteLine($"agent-review: {problem}");
    }

    return 2;
}

string eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "";
string? eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");

// --- what is being checked ---------------------------------------------------

string baseRef;
string headSha;
int? pullNumber = null;
string? pullAuthor = null;

if (eventName is "pull_request" or "pull_request_review" or "pull_request_target")
{
    if (eventPath is null || !File.Exists(eventPath))
    {
        Console.Error.WriteLine("agent-review: a pull request event without GITHUB_EVENT_PATH.");
        return 2;
    }

    using JsonDocument payload = JsonDocument.Parse(File.ReadAllText(eventPath));
    JsonElement pull = payload.RootElement.GetProperty("pull_request");
    pullNumber = pull.GetProperty("number").GetInt32();
    pullAuthor = pull.GetProperty("user").GetProperty("login").GetString();
    baseRef = "origin/" + pull.GetProperty("base").GetProperty("ref").GetString();
    headSha = pull.GetProperty("head").GetProperty("sha").GetString() ?? "HEAD";
}
else if (eventName == "push")
{
    if (Environment.GetEnvironmentVariable("GITHUB_REF") == "refs/heads/main")
    {
        Console.WriteLine("ok  a push to main: what it brings passed this on its pull request or its branch (ADR 0088)");
        return 0;
    }

    baseRef = "origin/main";
    headSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "HEAD";
}
else
{
    baseRef = "origin/main";
    headSha = "HEAD";
}

Console.WriteLine($"note {(eventName.Length == 0 ? "local" : eventName)}: {baseRef}..{headSha}{(pullNumber is null ? "" : $", pull request #{pullNumber} by {pullAuthor}")}");

// --- the agents' commits -----------------------------------------------------

(int logExit, string log, string logError) = Git(repositoryRoot, "log", "--no-merges", "--format=%h%x02%ae", $"{baseRef}..{headSha}");

if (logExit != 0)
{
    Console.Error.WriteLine($"agent-review: could not read {baseRef}..{headSha}: {logError.Trim()}");
    return 2;
}

Dictionary<string, Agent> agentsInRange = new(StringComparer.Ordinal);
int agentCommits = 0;

foreach (string line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
    string[] fields = line.Split('\u0002');

    if (fields.Length == 2 && headMap.AgentByEmail(fields[1].Trim()) is Agent agent)
    {
        agentCommits++;
        agentsInRange[agent.Id] = agent;
    }
}

// --- delegate changes ----------------------------------------------------------

// The base's map decides whose delegates changed: a change cannot make itself
// legitimate by also changing the login it is checked against.
IdentityMap? baseMap = null;
(int showExit, string baseJson, _) = Git(repositoryRoot, "show", $"{baseRef}:{IdentityMap.RelativePath}");

if (showExit == 0)
{
    List<string> baseProblems = [];
    baseMap = IdentityMap.Parse(baseJson, baseProblems);
}

List<Human> delegatesChanged = [];

foreach (Human human in headMap.Humans)
{
    Human? before = baseMap?.HumanById(human.Id);
    IEnumerable<string> was = before?.Delegates ?? [];

    if (!was.Order(StringComparer.Ordinal).SequenceEqual(human.Delegates.Order(StringComparer.Ordinal), StringComparer.Ordinal))
    {
        delegatesChanged.Add(before ?? human);
    }
}

List<string> findings = [];

foreach (Human human in delegatesChanged)
{
    if (pullAuthor is null)
    {
        findings.Add($"the delegates of '{human.Id}' change, which only {human.Id}'s own pull request may do; this is not a pull request");
    }
    else if (!string.Equals(pullAuthor, human.GitHub, StringComparison.OrdinalIgnoreCase))
    {
        findings.Add($"the delegates of '{human.Id}' change in a pull request by {pullAuthor}; only {human.GitHub ?? human.Id} may change them");
    }
}

// --- approvals -------------------------------------------------------------------

foreach (Agent agent in agentsInRange.Values)
{
    // Who may approve is the base's answer, as for delegate changes: a pull
    // request that adds a delegate cannot be approved by that delegate.
    IReadOnlyList<Human> acting = baseMap?.Agents.Any(known => known.Id == agent.Id) == true
        ? baseMap.ActingFor(agent)
        : headMap.ActingFor(agent);
    string who = string.Join(", ", acting.Select(human => human.GitHub ?? human.Id));

    if (eventName.Length == 0)
    {
        Console.WriteLine($"note '{agent.Id}' authored commits here; a pull request needs an approval on its head from {who}");
        continue;
    }

    if (pullNumber is null)
    {
        findings.Add($"'{agent.Id}' authored commits on this branch; they land through a pull request approved by {who}");
        continue;
    }

    HashSet<string> approvers;

    try
    {
        approvers = await ApproversOnHead(pullNumber.Value, headSha);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
    {
        Console.Error.WriteLine($"agent-review: could not read the reviews of #{pullNumber}: {exception.Message}");
        return 2;
    }

    Human? approver = acting.FirstOrDefault(human => human.GitHub is not null && approvers.Contains(human.GitHub));

    if (approver is null)
    {
        findings.Add($"'{agent.Id}' authored commits in #{pullNumber}, which needs an approving review on {headSha[..Math.Min(8, headSha.Length)]} "
            + $"from {who}" + (approvers.Count == 0 ? "; it has none" : $"; it has approvals from {string.Join(", ", approvers)} only"));
    }
    else
    {
        Console.WriteLine($"ok  '{agent.Id}''s commits are approved on the head by {approver.GitHub}");
    }
}

Console.WriteLine($"note {agentCommits} agent commit(s), {delegatesChanged.Count} delegate change(s)");

if (findings.Count == 0)
{
    Console.WriteLine("ok  agent review");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: agent review (ADR 0087):");

foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

return 1;

// --- helpers ---------------------------------------------------------------

// The logins whose latest review state is APPROVED, on the given head.
static async Task<HashSet<string>> ApproversOnHead(int pull, string head)
{
    string api = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com";
    string repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
        ?? throw new InvalidOperationException("GITHUB_REPOSITORY is not set.");
    string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        ?? throw new InvalidOperationException("GITHUB_TOKEN is not set; the job passes github.token.");

    using HttpClient client = new();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-agent-review", "1"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

    // Reviews come oldest first; the latest decisive one per login stands.
    Dictionary<string, (string State, string Commit)> latest = new(StringComparer.OrdinalIgnoreCase);

    for (int page = 1; ; page++)
    {
        string body = await client.GetStringAsync($"{api}/repos/{repository}/pulls/{pull}/reviews?per_page=100&page={page}");
        using JsonDocument reviews = JsonDocument.Parse(body);

        if (reviews.RootElement.GetArrayLength() == 0)
        {
            break;
        }

        foreach (JsonElement review in reviews.RootElement.EnumerateArray())
        {
            string state = review.GetProperty("state").GetString() ?? "";
            string? login = review.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object
                ? user.GetProperty("login").GetString()
                : null;

            // A comment does not change a reviewer's decision.
            if (login is null || state == "COMMENTED" || state == "PENDING")
            {
                continue;
            }

            latest[login] = (state, review.TryGetProperty("commit_id", out JsonElement commit) ? commit.GetString() ?? "" : "");
        }
    }

    return [.. latest.Where(entry => entry.Value.State == "APPROVED" && entry.Value.Commit == head).Select(entry => entry.Key)];
}

static (int ExitCode, string StandardOutput, string StandardError) Git(string root, params string[] arguments)
{
    ProcessStartInfo startInfo = new()
    {
        FileName = "git",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start git.");

    Task<string> standardError = process.StandardError.ReadToEndAsync();
    string standardOutput = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    return (process.ExitCode, standardOutput, standardError.Result);
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
