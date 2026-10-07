// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The agent review tamper test.
//
//   dotnet run eng/agent-review-tamper.cs
//
// ADR 0087: the script that judges a pull request is always main's. This
// proves it for the one attack that rule exists for — a pull request that
// edits eng/agent-review.cs to always pass — in three parts:
//
//   1. the tampered copy really would pass: run on its own, with no
//      approval anywhere, it exits 0 and posts nothing;
//   2. main's copy, given the same pull request, fails it: the pull request
//      is served by a stand-in for the GitHub API (HttpListener, BCL only),
//      and the check run main's copy posts is read back. The stand-in also
//      covers the approval form: 12 characters of the head pass, 11 fail, an
//      earlier head fails, a stranger fails, a review submission is not
//      consulted;
//   3. the workflow cannot run the tampered copy: the job that posts the
//      check in .github/workflows/agent-review.yml checks out main by name,
//      never a pull request ref; the workflow runs on no trigger whose
//      workflow comes from the pull request (pull_request,
//      pull_request_review), and no job in it is itself named `agent review`.
//
// Exit codes: 0 every part holds, 1 one did not, 2 could not run.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

string root = FindRepositoryRoot();
List<string> failures = [];

const string Head = "1234567890abcdef1234567890abcdef12345678";
const string Earlier = "fedcba0987654321fedcba0987654321fedcba09";
string identities = File.ReadAllText(Path.Combine(root, "eng", "identities.json"));

// --- 3. the workflow -----------------------------------------------------------

string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "agent-review.yml"));
string judge = JobBlock(workflow, "judge");

if (judge.Length == 0)
{
    failures.Add("agent-review.yml has no `judge` job");
}
else
{
    Require(judge.Contains("ref: ${{ github.event.repository.default_branch }}", StringComparison.Ordinal),
        "the judge job checks out main by name");
    Require(!judge.Contains("pull_request.head", StringComparison.Ordinal)
            && !judge.Contains("refs/pull/", StringComparison.Ordinal)
            && !judge.Contains("github.sha", StringComparison.Ordinal)
            && !judge.Contains("github.ref }}", StringComparison.Ordinal),
        "the judge job names no pull request ref, merge ref or pushed sha");
    Require(judge.Contains("github.event_name == 'pull_request_target'", StringComparison.Ordinal)
            && !judge.Contains("github.event_name == 'pull_request'", StringComparison.Ordinal),
        "the judge job runs on pull_request_target, never on pull_request");
    Require(judge.Contains("environment: gates", StringComparison.Ordinal),
        "the judge job mints its token from the gates environment");
    Require(judge.Contains("run: dotnet run eng/agent-review.cs", StringComparison.Ordinal),
        "the judge job runs the checked-out (main's) eng/agent-review.cs");
}

string triggers = TopLevelBlock(workflow, "on");
Require(triggers.Length > 0 && !triggers.Contains("pull_request_review", StringComparison.Ordinal),
    "pull_request_review is not a trigger: its workflow would come from the pull request");
Require(triggers.Length > 0 && !Regex.IsMatch(triggers, @"^\s+pull_request\s*:", RegexOptions.Multiline),
    "pull_request is not a trigger: its workflow would come from the pull request");
Require(!Regex.IsMatch(workflow, @"^    name:\s*[""']?agent review[""']?\s*$", RegexOptions.Multiline),
    "no job is named `agent review`: that name is the gates App's check, posted, never a job's");

// --- 1. the tampered copy --------------------------------------------------------

string scratch = Directory.CreateTempSubdirectory("agent-review-tamper-").FullName;

try
{
    Directory.CreateDirectory(Path.Combine(scratch, "eng"));
    File.WriteAllText(Path.Combine(scratch, "Varve.slnx"), "<Solution />\n");
    File.Copy(Path.Combine(root, "eng", "Directory.Build.props"), Path.Combine(scratch, "eng", "Directory.Build.props"));
    File.Copy(Path.Combine(root, "global.json"), Path.Combine(scratch, "global.json"));
    File.WriteAllText(Path.Combine(scratch, "eng", "agent-review.cs"),
        "// This Source Code Form is subject to the terms of the Mozilla Public\n"
        + "// License, v. 2.0. If a copy of the MPL was not distributed with this\n"
        + "// file, You can obtain one at https://mozilla.org/MPL/2.0/.\n\n"
        + "// The pull request's edit: always pass.\n"
        + "return 0;\n");

    using StandIn tamperedServer = StandIn.Start(identities, Head, author: "Hafeok", comments: [], reviews: []);
    (int tamperedExit, _) = RunJudge(scratch, tamperedServer);
    Require(tamperedExit == 0 && tamperedServer.Posted.Count == 0,
        $"the tampered copy passes on its own (exit {tamperedExit}), which is the attack");

    // --- 2. main's copy ----------------------------------------------------------

    (string Name, JsonArray Comments, JsonArray Reviews, string Expected)[] cases =
    [
        ("no approval", [], [], "failure"),
        ("approve with 12 characters of the head", [Comment("Hafeok", $"approve {Head[..12]}")], [], "success"),
        ("approve with the full head", [Comment("Hafeok", $"lgtm\napprove {Head}")], [], "success"),
        ("approve with 11 characters of the head", [Comment("Hafeok", $"approve {Head[..11]}")], [], "failure"),
        ("approve naming an earlier head", [Comment("Hafeok", $"approve {Earlier}")], [], "failure"),
        ("approve by a stranger", [Comment("mallory", $"approve {Head}")], [], "failure"),
        ("an approving review submission only", [], [Review("Hafeok", "APPROVED", Head)], "failure"),
    ];

    foreach ((string name, JsonArray comments, JsonArray reviews, string expected) in cases)
    {
        using StandIn server = StandIn.Start(identities, Head, author: "Hafeok", comments, reviews);
        (int exit, string output) = RunJudge(root, server);
        string posted = server.Posted.Count == 1 ? server.Posted[0] : $"{server.Posted.Count} posts";

        Require(exit == 0 && posted == expected,
            $"main's copy, {name}: posts {expected} (exit {exit}, posted {posted})" + (exit == 0 ? "" : $"\n{output}"));
    }

    // The other trusted paths: a new comment, and a push to land/**.
    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [Comment("Hafeok", $"approve {Head[..12]}")], []))
    {
        (int exit, _) = RunJudge(root, server, "issue_comment", """{"issue":{"number":7,"pull_request":{}}}""");
        Require(exit == 0 && server.Posted is ["success"], "main's copy, on a new approving comment: posts success");
    }

    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [], []))
    {
        (int exit, _) = RunJudge(root, server, "push", "{}", "refs/heads/land/first");
        Require(exit == 0 && server.Posted is ["failure"], "main's copy, a land/ push with an agent's commit: posts failure");
    }

    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [], [], commitEmail: "emil@okkels-klein.dk"))
    {
        (int exit, _) = RunJudge(root, server, "push", "{}", "refs/heads/land/first");
        Require(exit == 0 && server.Posted is ["success"], "main's copy, a land/ push of the maintainer's own commits: posts success");
    }
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

if (failures.Count == 0)
{
    Console.WriteLine("ok  a pull request that edits eng/agent-review.cs to always pass still fails under main's copy");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: {failures.Count} part(s) of the tamper test do not hold (ADR 0087):");

foreach (string failure in failures)
{
    Console.Error.WriteLine($"  {failure}");
}

return 1;

// --- helpers ---------------------------------------------------------------

void Require(bool holds, string what)
{
    Console.WriteLine($"{(holds ? "ok  " : "FAIL")} {what}");

    if (!holds)
    {
        failures.Add(what);
    }
}

static JsonObject Comment(string login, string body) => new()
{
    ["user"] = new JsonObject { ["login"] = login },
    ["body"] = body,
};

static JsonObject Review(string login, string state, string commit) => new()
{
    ["user"] = new JsonObject { ["login"] = login },
    ["state"] = state,
    ["commit_id"] = commit,
    ["body"] = "",
};

// Runs <directory>/eng/agent-review.cs as the judge job runs it, on
// pull_request_target, against the stand-in.
static (int Exit, string Output) RunJudge(
    string directory, StandIn server, string eventName = "pull_request_target", string eventJson = """{"pull_request":{"number":7}}""", string? reference = null)
{
    string payload = Path.Combine(Path.GetTempPath(), $"agent-review-event-{Guid.NewGuid():N}.json");
    File.WriteAllText(payload, eventJson);

    ProcessStartInfo start = new("dotnet")
    {
        WorkingDirectory = directory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    start.ArgumentList.Add("run");
    start.ArgumentList.Add("eng/agent-review.cs");
    start.Environment["GITHUB_EVENT_NAME"] = eventName;
    start.Environment["GITHUB_SHA"] = "1234567890abcdef1234567890abcdef12345678";

    if (reference is not null)
    {
        start.Environment["GITHUB_REF"] = reference;
    }
    start.Environment["GITHUB_EVENT_PATH"] = payload;
    start.Environment["GITHUB_REPOSITORY"] = "owner/repo";
    start.Environment["GITHUB_API_URL"] = server.Url;
    start.Environment["GITHUB_TOKEN"] = "read-token";
    start.Environment["GATES_TOKEN"] = "gates-token";
    start.Environment["GATES_CHECK_NAME"] = "agent review";

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
    Task<string> error = process.StandardError.ReadToEndAsync();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    File.Delete(payload);

    return (process.ExitCode, output + error.Result);
}

// The text of a top-level key's block (`on:` and what it holds), comments out.
static string TopLevelBlock(string workflow, string key)
{
    StringBuilder block = new();
    bool inside = false;

    foreach (string line in workflow.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
    {
        if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
        {
            inside = line.StartsWith(key + ":", StringComparison.Ordinal);
        }

        if (inside && !line.TrimStart().StartsWith('#'))
        {
            block.AppendLine(line);
        }
    }

    return block.ToString();
}

// The text of one job, from its key to the next job's key.
static string JobBlock(string workflow, string id)
{
    string[] lines = workflow.Replace("\r", "", StringComparison.Ordinal).Split('\n');
    StringBuilder block = new();
    bool inside = false;

    foreach (string line in lines)
    {
        bool jobKey = line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal) && line[2] != ' ' && line[2] != '#' && line.TrimEnd().EndsWith(':');

        if (jobKey)
        {
            inside = line.Trim() == id + ":";
        }

        if (inside)
        {
            block.AppendLine(line);
        }
    }

    return block.ToString();
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

// A stand-in for the parts of the GitHub REST API eng/agent-review.cs reads,
// for one pull request, #7, whose one commit an agent authored.
sealed class StandIn : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly string identities;
    private readonly string head;
    private readonly string author;
    private readonly JsonArray comments;
    private readonly JsonArray reviews;
    private readonly string commitEmail;

    public List<string> Posted { get; } = [];

    public string Url { get; }

    private StandIn(string identities, string head, string author, JsonArray comments, JsonArray reviews, string commitEmail)
    {
        this.commitEmail = commitEmail;
        this.identities = identities;
        this.head = head;
        this.author = author;
        this.comments = comments;
        this.reviews = reviews;

        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(Url + "/");
    }

    public static StandIn Start(
        string identities, string head, string author, JsonArray comments, JsonArray reviews, string commitEmail = "noreply@anthropic.com")
    {
        StandIn server = new(identities, head, author, comments, reviews, commitEmail);
        server.listener.Start();
        _ = Task.Run(server.Serve);
        return server;
    }

    private async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            string path = context.Request.Url!.AbsolutePath;
            bool firstPage = context.Request.QueryString["page"] is null or "1";
            string body;
            int status = 200;

            if (context.Request.HttpMethod == "POST" && path.EndsWith("/check-runs", StringComparison.Ordinal))
            {
                using StreamReader reader = new(context.Request.InputStream);
                JsonNode? posted = JsonNode.Parse(await reader.ReadToEndAsync());
                Posted.Add(posted?["conclusion"]?.GetValue<string>() ?? "?");
                body = "{}";
            }
            else if (path.EndsWith("/pulls/7", StringComparison.Ordinal))
            {
                body = new JsonObject
                {
                    ["head"] = new JsonObject { ["sha"] = head },
                    ["user"] = new JsonObject { ["login"] = author },
                }.ToJsonString();
            }
            else if (path.EndsWith("/pulls/7/commits", StringComparison.Ordinal))
            {
                body = firstPage ? Commits().ToJsonString() : "[]";
            }
            else if (path.Contains("/compare/main...", StringComparison.Ordinal))
            {
                body = new JsonObject { ["commits"] = Commits() }.ToJsonString();
            }
            else if (path.EndsWith("/issues/7/comments", StringComparison.Ordinal))
            {
                body = firstPage ? comments.ToJsonString() : "[]";
            }
            else if (path.EndsWith("/pulls/7/reviews", StringComparison.Ordinal))
            {
                body = firstPage ? reviews.ToJsonString() : "[]";
            }
            else if (path.EndsWith("/contents/eng/identities.json", StringComparison.Ordinal))
            {
                body = identities;
            }
            else if (path.Contains("/check-runs", StringComparison.Ordinal))
            {
                body = """{"total_count":0,"check_runs":[]}""";
            }
            else
            {
                status = 404;
                body = """{"message":"Not Found"}""";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private JsonArray Commits() =>
    [
        new JsonObject
        {
            ["commit"] = new JsonObject { ["author"] = new JsonObject { ["email"] = commitEmail } },
            ["parents"] = new JsonArray(new JsonObject { ["sha"] = "0" }),
        },
    ];

    public void Dispose()
    {
        stop.Cancel();
        listener.Close();
    }
}
