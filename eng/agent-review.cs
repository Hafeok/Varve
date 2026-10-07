// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Agent review gate.
//
//   dotnet run eng/agent-review.cs        (in GitHub Actions; reads the event)
//
// ADR 0087. Two rules about who stands behind a change:
//
//   1. A pull request containing a commit an agent authored needs an approval
//      for every agent in it, from a holder of Approve for that agent: its
//      responsible human or one of their delegates (eng/identities.json).
//   2. A human's delegates change only in that human's own pull request.
//
// THE HAZARD. A check that runs the pull request's own code is judged by the
// pull request: one that edits this file to `return 0;` passes itself. So
// this file is never taken from the change it judges. The workflow
// (.github/workflows/agent-review.yml) checks out main and runs main's copy,
// on trusted triggers only, and this script reads the pull request — its
// head, its commits, its identity map, its comments — through the API as
// data. Nothing from the pull request is built or run (ADR 0087, amendments
// of 2026-10-06).
//
// THE APPROVAL. A pull-request conversation comment whose text contains
// "approve <sha>", where <sha> is at least 12 characters of the current head,
// written by a login the identity map makes a holder of Approve for the
// agent. Review submissions are not consulted. This form is NOT a valid
// workflow: it is tolerated only until the ledger's review gate exists, and the
// pull request that adopts that gate removes it (ADR 0087).
//
// The paths:
//
//   pull_request_target, issue_comment
//                 the trusted paths: main's workflow and main's copy. The
//                 verdict is a check run named by GATES_CHECK_NAME on the
//                 head sha, created or updated with the gates App's token
//                 (GATES_TOKEN), which ruleset 1 pins (ADR 0088).
//   push to land/**
//                 a branch only the maintainer may push (ruleset "land"):
//                 passes when the range from main has no agent commit and no
//                 delegate change, and posts the same check.
//   none          a local run: says what it would need and exits 0.
//
// GITHUB_API_URL points it at a stand-in, which is how eng/agent-review-tamper.cs
// tests it.
//
// Exit codes: 0 the verdict posted (or nothing to judge, or a local run): a
// failing verdict is a posted failure, not an exit code;
// 2 could not run.
//
// See docs/adr/0087-the-identity-map.md.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Identities.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const int MinimumShaPrefix = 12;

string repositoryRoot = FindRepositoryRoot();

// The base's map, from the checkout: main's. It decides who may approve and
// whose delegates changed, so a change cannot authorise itself.
List<string> problems = [];
IdentityMap? baseMap = IdentityMap.Load(repositoryRoot, problems);

if (baseMap is null || problems.Count > 0)
{
    foreach (string problem in problems)
    {
        Console.Error.WriteLine($"agent-review: {problem}");
    }

    return 2;
}

string eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "";
string? eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
string? gatesToken = Environment.GetEnvironmentVariable("GATES_TOKEN");
string checkName = Environment.GetEnvironmentVariable("GATES_CHECK_NAME") ?? "agent review";

if (eventName.Length == 0)
{
    Console.WriteLine("note local run: approvals and the pull request are on GitHub; this checks them only in Actions");
    return 0;
}

if (eventPath is null || !File.Exists(eventPath))
{
    Console.Error.WriteLine("agent-review: no GITHUB_EVENT_PATH.");
    return 2;
}

using JsonDocument payload = JsonDocument.Parse(File.ReadAllText(eventPath));

Api api;

try
{
    api = new Api(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"agent-review: {exception.Message}");
    return 2;
}

// --- what is judged ------------------------------------------------------------

int? pullNumber = null;
string headSha;
string? pullAuthor = null;
List<string> commitEmails = [];

try
{
    switch (eventName)
    {
        case "pull_request_target":
            pullNumber = payload.RootElement.GetProperty("pull_request").GetProperty("number").GetInt32();
            break;

        case "issue_comment":
            JsonElement issue = payload.RootElement.GetProperty("issue");

            if (!issue.TryGetProperty("pull_request", out _))
            {
                Console.WriteLine("ok  a comment on an issue, not a pull request");
                return 0;
            }

            pullNumber = issue.GetProperty("number").GetInt32();
            break;

        case "push":
            string reference = Environment.GetEnvironmentVariable("GITHUB_REF") ?? "";

            if (!reference.StartsWith("refs/heads/land/", StringComparison.Ordinal))
            {
                Console.WriteLine($"ok  a push to {reference}, which this gate does not judge");
                return 0;
            }

            break;

        default:
            Console.Error.WriteLine($"agent-review: '{eventName}' is not a trigger this gate trusts.");
            return 2;
    }

    if (pullNumber is int number)
    {
        using JsonDocument pull = await api.Get($"pulls/{number}");
        headSha = pull.RootElement.GetProperty("head").GetProperty("sha").GetString()!;
        pullAuthor = pull.RootElement.GetProperty("user").GetProperty("login").GetString();

        foreach (JsonElement commit in await api.GetAll($"pulls/{number}/commits"))
        {
            AddCommit(commit, commitEmails);
        }
    }
    else
    {
        headSha = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? throw new InvalidOperationException("GITHUB_SHA is not set.");

        using JsonDocument compare = await api.Get($"compare/main...{headSha}");

        foreach (JsonElement commit in compare.RootElement.GetProperty("commits").EnumerateArray())
        {
            AddCommit(commit, commitEmails);
        }
    }
}
catch (Exception exception) when (exception is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException)
{
    Console.Error.WriteLine($"agent-review: could not read what is judged: {exception.Message}");
    return 2;
}

Console.WriteLine($"note {eventName}: head {headSha}{(pullNumber is null ? "" : $", pull request #{pullNumber} by {pullAuthor}")}, {commitEmails.Count} commit(s)");

// --- the rules -------------------------------------------------------------------

List<string> findings = [];

Dictionary<string, Agent> agents = new(StringComparer.Ordinal);
int agentCommits = 0;

foreach (string email in commitEmails)
{
    if (baseMap.AgentByEmail(email) is Agent agent)
    {
        agentCommits++;
        agents[agent.Id] = agent;
    }
}

// Delegate changes: the head's map, read as data, against the base's.
List<Human> delegatesChanged = [];

try
{
    string headJson = await api.GetRaw($"contents/{IdentityMap.RelativePath}?ref={headSha}");
    List<string> headProblems = [];
    IdentityMap headMap = IdentityMap.Parse(headJson, headProblems);

    foreach (Human human in headMap.Humans)
    {
        Human? before = baseMap.HumanById(human.Id);
        IEnumerable<string> was = before?.Delegates ?? [];

        if (!was.Order(StringComparer.Ordinal).SequenceEqual(human.Delegates.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            delegatesChanged.Add(before ?? human);
        }
    }
}
catch (Exception exception) when (exception is HttpRequestException or JsonException)
{
    Console.Error.WriteLine($"agent-review: could not read the head's identity map: {exception.Message}");
    return 2;
}

foreach (Human human in delegatesChanged)
{
    if (pullAuthor is null)
    {
        findings.Add($"the delegates of '{human.Id}' change, which only {human.Id}'s own pull request may do; this is a push");
    }
    else if (!string.Equals(pullAuthor, human.GitHub, StringComparison.OrdinalIgnoreCase))
    {
        findings.Add($"the delegates of '{human.Id}' change in a pull request by {pullAuthor}; only {human.GitHub ?? human.Id} may change them");
    }
}

// Approvals: conversation comments on the pull request, nothing else.
Dictionary<string, HashSet<string>> approvedBy = new(StringComparer.OrdinalIgnoreCase);

if (pullNumber is int pr && agents.Count > 0)
{
    try
    {
        Regex approve = new(@"\bapprove\s+(?<sha>[0-9a-fA-F]{" + MinimumShaPrefix + @",40})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (JsonElement comment in await api.GetAll($"issues/{pr}/comments"))
        {
            string? login = comment.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object
                ? user.GetProperty("login").GetString()
                : null;
            string body = comment.TryGetProperty("body", out JsonElement text) ? text.GetString() ?? "" : "";

            if (login is null)
            {
                continue;
            }

            foreach (Match match in approve.Matches(body))
            {
                if (headSha.StartsWith(match.Groups["sha"].Value, StringComparison.OrdinalIgnoreCase))
                {
                    if (!approvedBy.TryGetValue(login, out HashSet<string>? shas))
                    {
                        approvedBy[login] = shas = [];
                    }

                    shas.Add(headSha);
                }
            }
        }
    }
    catch (Exception exception) when (exception is HttpRequestException or JsonException)
    {
        Console.Error.WriteLine($"agent-review: could not read the comments of #{pr}: {exception.Message}");
        return 2;
    }
}

foreach (Agent agent in agents.Values)
{
    IReadOnlyList<Human> holders = baseMap.ActingFor(agent);
    string who = string.Join(", ", holders.Select(human => human.GitHub ?? human.Id));

    if (pullNumber is null)
    {
        findings.Add($"'{agent.Id}' authored commits on this branch; they land through a pull request approved by {who}");
        continue;
    }

    Human? approver = holders.FirstOrDefault(human => human.GitHub is not null && approvedBy.ContainsKey(human.GitHub));

    if (approver is null)
    {
        findings.Add($"'{agent.Id}' authored commits in #{pullNumber}; it needs a comment 'approve {headSha}' "
            + $"(at least {MinimumShaPrefix} characters of the head) from {who}");
    }
    else
    {
        Console.WriteLine($"ok  '{agent.Id}' is approved on {headSha[..MinimumShaPrefix]} by {approver.GitHub}");
    }
}

string summary = findings.Count == 0
    ? $"{agentCommits} agent commit(s), every agent approved on the head; {delegatesChanged.Count} delegate change(s), each by its own human."
    : string.Join("\n", findings.Select(finding => "- " + finding));

Console.WriteLine($"note {agentCommits} agent commit(s), {delegatesChanged.Count} delegate change(s)");

foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

// --- the verdict ---------------------------------------------------------------

if (string.IsNullOrEmpty(gatesToken))
{
    Console.Error.WriteLine("agent-review: a trusted path without GATES_TOKEN; the verdict cannot be posted where the ruleset reads it.");
    return 2;
}

try
{
    long? pinned = PinnedIntegration(repositoryRoot, checkName);
    await PostCheck(new Api(gatesToken), checkName, headSha, findings.Count == 0, summary, pinned);
    Console.WriteLine($"ok  posted '{checkName}' = {(findings.Count == 0 ? "success" : "failure")} on {headSha}");
    return 0;
}
catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
{
    Console.Error.WriteLine($"agent-review: could not post the check run: {exception.Message}");
    return 2;
}

// --- helpers ---------------------------------------------------------------

static void AddCommit(JsonElement commit, List<string> emails)
{
    JsonElement inner = commit.GetProperty("commit");

    // A merge's author is whoever merged; the commits it brings are listed
    // on their own.
    if (commit.TryGetProperty("parents", out JsonElement parents) && parents.GetArrayLength() > 1)
    {
        return;
    }

    emails.Add(inner.GetProperty("author").GetProperty("email").GetString() ?? "");
}

// The integration id ruleset 1 pins the check to, read from main's
// .github/repo-standard.yaml; the check run of that App is the one updated.
static long? PinnedIntegration(string root, string name)
{
    string path = Path.Combine(root, ".github", "repo-standard.yaml");

    if (!File.Exists(path))
    {
        return null;
    }

    string[] lines = File.ReadAllLines(path);

    for (int i = 0; i < lines.Length; i++)
    {
        Match context = Regex.Match(lines[i], @"^\s*-\s*context:\s*(?<name>.+?)\s*$");

        if (!context.Success || context.Groups["name"].Value.Trim('"', '\'') != name)
        {
            continue;
        }

        for (int j = i + 1; j < lines.Length && !Regex.IsMatch(lines[j], @"^\s*-\s"); j++)
        {
            Match id = Regex.Match(lines[j], @"^\s*integration_id:\s*(?<id>\d+)\s*$");

            if (id.Success)
            {
                return long.Parse(id.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    return null;
}

static async Task PostCheck(Api api, string name, string head, bool success, string summary, long? integration)
{
    string conclusion = success ? "success" : "failure";
    JsonObject Output() => new()
    {
        ["title"] = success ? "Approved" : "Not approved",
        ["summary"] = summary,
    };

    // Update this App's run for the head if there is one, so the check has
    // one row; otherwise create it.
    long? existing = null;
    using (JsonDocument runs = await api.Get($"commits/{head}/check-runs?check_name={Uri.EscapeDataString(name)}&filter=latest"))
    {
        foreach (JsonElement run in runs.RootElement.GetProperty("check_runs").EnumerateArray())
        {
            long app = run.GetProperty("app").GetProperty("id").GetInt64();

            if (integration is null || app == integration)
            {
                existing = run.GetProperty("id").GetInt64();
                break;
            }
        }
    }

    if (existing is long id)
    {
        await api.Send(HttpMethod.Patch, $"check-runs/{id}", new JsonObject
        {
            ["status"] = "completed",
            ["conclusion"] = conclusion,
            ["output"] = Output(),
        });
    }
    else
    {
        await api.Send(HttpMethod.Post, "check-runs", new JsonObject
        {
            ["name"] = name,
            ["head_sha"] = head,
            ["status"] = "completed",
            ["conclusion"] = conclusion,
            ["output"] = Output(),
        });
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

// The repository's REST API, as data. Nothing it returns is executed.
sealed class Api
{
    private readonly HttpClient client = new();
    private readonly string root;

    public Api(string? token)
    {
        string api = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com";
        string repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? throw new InvalidOperationException("GITHUB_REPOSITORY is not set.");

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("no token; the job passes github.token as GITHUB_TOKEN.");
        }

        root = $"{api.TrimEnd('/')}/repos/{repository}/";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-agent-review", "2"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<JsonDocument> Get(string path) =>
        JsonDocument.Parse(await client.GetStringAsync(root + path));

    public async Task<string> GetRaw(string path)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, root + path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<List<JsonElement>> GetAll(string path)
    {
        List<JsonElement> all = [];
        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        for (int page = 1; ; page++)
        {
            using JsonDocument document = await Get($"{path}{separator}per_page=100&page={page}");

            if (document.RootElement.GetArrayLength() == 0)
            {
                return all;
            }

            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                all.Add(element.Clone());
            }
        }
    }

    public async Task Send(HttpMethod method, string path, JsonObject body)
    {
        using HttpRequestMessage request = new(method, root + path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
