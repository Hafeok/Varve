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
//      covers the approval form (#290): the approval line as a comment's
//      whole body with the full head passes, and with the delegate's suffix;
//      inside a sentence, in a code span, after another line, with 12
//      characters of the head, in an edited comment or in one with no
//      timestamps it fails; an approval deleted or edited afterwards fails
//      when judged again; an earlier head fails, a stranger fails, a review
//      submission is not consulted; and a land/ push of a landing train (#277), built for real
//      in a scratch repository: approved members pass, a member that is no
//      approved head fails, a merge edited by hand fails, a merge without the
//      trailer fails, an extra commit between the merges fails, and a train
//      built on a main that has since moved fails;
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

// A withdrawn approval starts a run (#290): the check on a head is the last
// one posted, so an edit or a deletion must post it again.
Match commentTypes = Regex.Match(triggers, @"^\s+issue_comment\s*:\s*\n\s+types:\s*\[(?<types>[^\]]*)\]", RegexOptions.Multiline);
string[] commentEvents = [.. commentTypes.Groups["types"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
Require(commentEvents.Contains("created") && commentEvents.Contains("edited") && commentEvents.Contains("deleted"),
    "issue_comment runs on created, edited and deleted: an approval edited or deleted is judged again");
Require(judge.Contains("contains(github.event.comment.body, 'approve')", StringComparison.Ordinal)
        && judge.Contains("contains(github.event.changes.body.from, 'approve')", StringComparison.Ordinal),
    "the judge job runs for a comment that holds the word now or held it before an edit");
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

        // What an approval is (lib/Approvals.cs; ADR 0087, amended 2026-11-12, #290).
        ("the approval line as the whole comment, with the full head", [Comment("Hafeok", Line(Head))], [], "success"),
        ("the same with the delegate's suffix", [Comment("Hafeok", Line(Head) + " (delegate)")], [], "success"),
        ("the same with a trailing newline", [Comment("Hafeok", Line(Head) + "\n")], [], "success"),
        ("the line inside a sentence", [Comment("Hafeok", $"New head. What agent review will ask for is {Line(Head)}, once the checks are green.")], [], "failure"),
        ("the line in a code span", [Comment("Hafeok", $"`{Line(Head)}`")], [], "failure"),
        ("the line after another line", [Comment("Hafeok", $"lgtm\n{Line(Head)}")], [], "failure"),
        ("the line with 12 characters of the head", [Comment("Hafeok", Line(Head[..12]))], [], "failure"),
        ("the line with the head in capitals", [Comment("Hafeok", Line(Head.ToUpperInvariant()))], [], "failure"),
        ("an edited comment whose body is now the line", [Comment("Hafeok", Line(Head), edited: true)], [], "failure"),
        ("a comment with no timestamps", [Bare("Hafeok", Line(Head))], [], "failure"),

        ("the line naming an earlier head", [Comment("Hafeok", Line(Earlier))], [], "failure"),
        ("the line by a stranger", [Comment("mallory", Line(Head))], [], "failure"),
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
    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [Comment("Hafeok", Line(Head))], []))
    {
        (int exit, _) = RunJudge(root, server, "issue_comment", """{"issue":{"number":7,"pull_request":{}}}""");
        Require(exit == 0 && server.Posted is ["success"], "main's copy, on a new approving comment: posts success");
    }

    // A withdrawn approval (#290). The check a pull request carries is the
    // last one posted, so the same pull request is judged twice: approved,
    // then again as the workflow runs it when the comment is deleted (GitHub
    // serves it no longer) or edited (it is served with a later updated_at).
    foreach ((string how, JsonArray after) in new (string, JsonArray)[]
    {
        ("deleted", []),
        ("edited into something else", [Comment("Hafeok", "withdrawn", edited: true)]),
        ("edited and still the line", [Comment("Hafeok", Line(Head), edited: true)]),
    })
    {
        using StandIn before = StandIn.Start(identities, Head, "Hafeok", [Comment("Hafeok", Line(Head))], []);
        (int approvedExit, _) = RunJudge(root, before, "issue_comment", """{"issue":{"number":7,"pull_request":{}}}""");
        using StandIn later = StandIn.Start(identities, Head, "Hafeok", after, []);
        (int withdrawnExit, _) = RunJudge(root, later, "issue_comment", """{"issue":{"number":7,"pull_request":{}}}""");
        Require(approvedExit == 0 && before.Posted is ["success"] && withdrawnExit == 0 && later.Posted is ["failure"],
            $"main's copy, an approving comment then {how}: posts success, then failure");
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

    // ADR 0087, amended 2026-10-08: an agent's commits land through land/ when
    // the head is an approved pull request's head, unchanged.
    const long GatesApp = 5220446;

    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [], [], approvedBy: GatesApp, pullHead: true))
    {
        (int exit, _) = RunJudge(root, server, "push", "{}", "refs/heads/land/first");
        Require(exit == 0 && server.Posted is ["success"], "main's copy, a land/ push of an approved pull request's head: posts success");
    }

    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [], [], approvedBy: 42, pullHead: true))
    {
        (int exit, _) = RunJudge(root, server, "push", "{}", "refs/heads/land/first");
        Require(exit == 0 && server.Posted is ["failure"], "main's copy, a land/ push whose 'success' is another App's run: posts failure");
    }

    using (StandIn server = StandIn.Start(identities, Head, "Hafeok", [], [], approvedBy: GatesApp, pullHead: false))
    {
        (int exit, _) = RunJudge(root, server, "push", "{}", "refs/heads/land/first");
        Require(exit == 0 && server.Posted is ["failure"], "main's copy, a land/ push of an approved sha that is no pull request's head: posts failure");
    }

    // #277, ADR 0243 point 10: a landing train of approved heads, built for
    // real in a scratch repository that GIT_DIR names, as eng/land.cs builds it.
    string trains = Path.Combine(scratch, "trains.git");
    ScratchGit(trains, null, "init", "--quiet", "--bare", trains);
    string trainMain = ScratchCommit(trains, [], new() { ["README.md"] = "Varve\n" }, "chore: main\n");
    string first = ScratchCommit(trains, [trainMain], new() { ["a.md"] = "a\n" }, "feat: a\n\nRefs #11\n");
    string second = ScratchCommit(trains, [trainMain], new() { ["b.md"] = "b\n" }, "feat: b\n\nRefs #12\n");
    string mergedFirst = ScratchMerge(trains, trainMain, first, "Merge #11 into the train for main\n\nLanding-only: merge\n", edit: false);
    string trainHead = ScratchMerge(trains, mergedFirst, second, "Merge #12 into the train for main\n\nLanding-only: merge\n", edit: false);
    string editedHead = ScratchMerge(trains, mergedFirst, second, "Merge #12 into the train for main\n\nLanding-only: merge\n", edit: true);
    string unmarkedHead = ScratchMerge(trains, mergedFirst, second, "Merge #12 into the train for main\n", edit: false);
    string extra = ScratchCommit(trains, [mergedFirst], new() { ["c.md"] = "c, which no member's head carries\n" }, "feat: c\n\nRefs #11\n");
    string extraHead = ScratchMerge(trains, extra, second, "Merge #12 into the train for main\n\nLanding-only: merge\n", edit: false);
    string movedMain = ScratchCommit(trains, [trainMain], new() { ["d.md"] = "d\n" }, "chore: main moved on\n");
    Dictionary<string, string> inScratch = new() { ["GIT_DIR"] = trains };

    using (StandIn server = StandIn.Start(identities, trainHead, "Hafeok", [], [], approvedBy: GatesApp, main: trainMain, pulls: new() { [first] = 11, [second] = 12 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", trainHead, inScratch);
        Require(exit == 0 && server.Posted is ["success"], "main's copy, a land/ push of a train of approved heads: posts success" + (server.Posted is ["success"] ? "" : $"\n{output}"));
    }

    using (StandIn server = StandIn.Start(identities, trainHead, "Hafeok", [], [], approvedBy: GatesApp, main: trainMain, pulls: new() { [first] = 11 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", trainHead, inScratch);
        Require(exit == 0 && server.Posted is ["failure"] && output.Contains("is not an approved pull request's head", StringComparison.Ordinal), "main's copy, a train with a member that is no approved pull request's head: posts failure, saying it is not an approved pull request's head" + (output.Contains("is not an approved pull request's head", StringComparison.Ordinal) ? "" : $"\n{output}"));
    }

    using (StandIn server = StandIn.Start(identities, editedHead, "Hafeok", [], [], approvedBy: GatesApp, main: trainMain, pulls: new() { [first] = 11, [second] = 12 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", editedHead, inScratch);
        Require(exit == 0 && server.Posted is ["failure"] && output.Contains("is not `git merge-tree` of its parents under the pinned settings", StringComparison.Ordinal), "main's copy, a train whose merge was edited by hand: posts failure, saying it is not `git merge-tree` of its parents under the pinned settings" + (output.Contains("is not `git merge-tree` of its parents under the pinned settings", StringComparison.Ordinal) ? "" : $"\n{output}"));
    }

    using (StandIn server = StandIn.Start(identities, unmarkedHead, "Hafeok", [], [], approvedBy: GatesApp, main: trainMain, pulls: new() { [first] = 11, [second] = 12 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", unmarkedHead, inScratch);
        Require(exit == 0 && server.Posted is ["failure"] && output.Contains("has no `Landing-only: merge` trailer", StringComparison.Ordinal), "main's copy, a train whose merge has no `Landing-only: merge` trailer: posts failure, saying it has no `Landing-only: merge` trailer" + (output.Contains("has no `Landing-only: merge` trailer", StringComparison.Ordinal) ? "" : $"\n{output}"));
    }

    using (StandIn server = StandIn.Start(identities, extraHead, "Hafeok", [], [], approvedBy: GatesApp, main: trainMain, pulls: new() { [first] = 11, [second] = 12 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", extraHead, inScratch);
        Require(exit == 0 && server.Posted is ["failure"] && output.Contains("is not a merge of two parents", StringComparison.Ordinal), "main's copy, a train with an extra commit on the first-parent line between its merges: posts failure, saying it is not a merge of two parents" + (output.Contains("is not a merge of two parents", StringComparison.Ordinal) ? "" : $"\n{output}"));
    }

    using (StandIn server = StandIn.Start(identities, trainHead, "Hafeok", [], [], approvedBy: GatesApp, main: movedMain, pulls: new() { [first] = 11, [second] = 12 }))
    {
        (int exit, string output) = RunJudge(root, server, "push", "{}", "refs/heads/land/train-11-12", trainHead, inScratch);
        Require(exit == 0 && server.Posted is ["failure"] && output.Contains("is not a merge of two parents, and main is not below it", StringComparison.Ordinal), "main's copy, a train built on a main that has since moved: posts failure, saying it is not a merge of two parents, and main is not below it" + (output.Contains("is not a merge of two parents, and main is not below it", StringComparison.Ordinal) ? "" : $"\n{output}"));
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

// The approval line for a sha. Built here and nowhere written out: a session
// never writes the line for a head (AGENTS.md, #290).
static string Line(string sha) => "approve " + sha;

// A conversation comment as GitHub serves it. An edit moves updated_at.
static JsonObject Comment(string login, string body, bool edited = false) => new()
{
    ["user"] = new JsonObject { ["login"] = login },
    ["body"] = body,
    ["created_at"] = "2026-11-12T08:30:01Z",
    ["updated_at"] = edited ? "2026-11-12T10:57:38Z" : "2026-11-12T08:30:01Z",
};

// A comment with no timestamps: unreadable as an approval.
static JsonObject Bare(string login, string body) => new()
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
    string directory, StandIn server, string eventName = "pull_request_target", string eventJson = """{"pull_request":{"number":7}}""", string? reference = null,
    string sha = "1234567890abcdef1234567890abcdef12345678", Dictionary<string, string>? environment = null)
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
    start.Environment["GITHUB_SHA"] = sha;

    foreach ((string key, string value) in environment ?? [])
    {
        start.Environment[key] = value;
    }

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

// git in a scratch repository, with standard input; the output, trimmed.
static string ScratchGit(string gitDir, string? input, params string[] arguments)
{
    ProcessStartInfo start = new("git")
    {
        RedirectStandardInput = input is not null,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    start.Environment["GIT_DIR"] = gitDir;
    start.Environment["GIT_INDEX_FILE"] = gitDir + ".index";
    start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Fixture";
    start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "fixture@example.org";

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git.");
    if (input is not null)
    {
        process.StandardInput.Write(input);
        process.StandardInput.Close();
    }

    Task<string> error = process.StandardError.ReadToEndAsync();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode is 0 or 1 ? output.Trim() : throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error.Result}");
}

// A commit of `files` over the first parent's tree.
static string ScratchCommit(string gitDir, string[] parents, Dictionary<string, string> files, string message)
{
    File.Delete(gitDir + ".index");
    if (parents.Length > 0)
    {
        ScratchGit(gitDir, null, "read-tree", $"{parents[0]}^{{tree}}");
    }

    foreach ((string path, string content) in files)
    {
        string blob = ScratchGit(gitDir, content, "hash-object", "-w", "--stdin");
        ScratchGit(gitDir, null, "update-index", "--add", "--cacheinfo", $"100644,{blob},{path}");
    }

    string tree = ScratchGit(gitDir, null, "write-tree");
    return ScratchGit(gitDir, message, ["commit-tree", tree, .. parents.SelectMany(static parent => new[] { "-p", parent }), "-F", "-"]);
}

// A merge whose tree is `git merge-tree` of its parents, or, with `edit`,
// that tree with a file added by hand.
static string ScratchMerge(string gitDir, string ours, string theirs, string message, bool edit)
{
    string tree = ScratchGit(gitDir, null, "merge-tree", "--write-tree", "--name-only", "--no-messages", ours, theirs).Split('\n')[0];
    if (edit)
    {
        File.Delete(gitDir + ".index");
        ScratchGit(gitDir, null, "read-tree", tree);
        string blob = ScratchGit(gitDir, "a line nobody reviewed\n", "hash-object", "-w", "--stdin");
        ScratchGit(gitDir, null, "update-index", "--add", "--cacheinfo", $"100644,{blob},unreviewed.txt");
        tree = ScratchGit(gitDir, null, "write-tree");
    }

    return ScratchGit(gitDir, message, "commit-tree", tree, "-p", ours, "-p", theirs, "-F", "-");
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
    private readonly long? approvedBy;
    private readonly bool pullHead;
    private readonly string main;
    private readonly Dictionary<string, int> pulls;

    public List<string> Posted { get; } = [];

    public string Url { get; }

    private StandIn(string identities, string head, string author, JsonArray comments, JsonArray reviews, string commitEmail, long? approvedBy, bool pullHead,
        string main, Dictionary<string, int> pulls)
    {
        this.main = main;
        this.pulls = pulls;
        this.commitEmail = commitEmail;
        this.approvedBy = approvedBy;
        this.pullHead = pullHead;
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
        string identities, string head, string author, JsonArray comments, JsonArray reviews, string commitEmail = "noreply@anthropic.com",
        long? approvedBy = null, bool pullHead = false, string main = "0000000000000000000000000000000000000000", Dictionary<string, int>? pulls = null)
    {
        StandIn server = new(identities, head, author, comments, reviews, commitEmail, approvedBy, pullHead, main, pulls ?? []);
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
            else if (context.Request.HttpMethod == "PATCH" && path.Contains("/check-runs/", StringComparison.Ordinal))
            {
                using StreamReader reader = new(context.Request.InputStream);
                JsonNode? patched = JsonNode.Parse(await reader.ReadToEndAsync());
                Posted.Add(patched?["conclusion"]?.GetValue<string>() ?? "?");
                body = "{}";
            }
            else if (path.Contains("/check-runs", StringComparison.Ordinal))
            {
                // The pull request's own verdict on its head, when the case has one.
                body = approvedBy is long app
                    ? new JsonObject
                    {
                        ["total_count"] = 1,
                        ["check_runs"] = new JsonArray(new JsonObject
                        {
                            ["id"] = 1,
                            ["conclusion"] = "success",
                            ["app"] = new JsonObject { ["id"] = app },
                        }),
                    }.ToJsonString()
                    : """{"total_count":0,"check_runs":[]}""";
            }
            else if (path.EndsWith("/commits/main", StringComparison.Ordinal))
            {
                body = new JsonObject { ["sha"] = main }.ToJsonString();
            }
            else if (path.EndsWith($"/commits/{head}/pulls", StringComparison.Ordinal))
            {
                body = firstPage && pullHead
                    ? new JsonArray(new JsonObject { ["number"] = 7, ["head"] = new JsonObject { ["sha"] = head } }).ToJsonString()
                    : "[]";
            }
            else if (Regex.Match(path, @"/commits/(?<sha>[0-9a-f]{40})/pulls$") is { Success: true } member)
            {
                // A train's member: its pull request, or none, as GitHub answers.
                body = firstPage && pulls.TryGetValue(member.Groups["sha"].Value, out int number)
                    ? new JsonArray(new JsonObject { ["number"] = number, ["head"] = new JsonObject { ["sha"] = member.Groups["sha"].Value } }).ToJsonString()
                    : "[]";
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
