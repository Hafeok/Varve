// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Landing, as two commands.
//
//   dotnet run eng/land.cs -- <pr>              push the pull request's head to land/<name>
//   dotnet run eng/land.cs -- <pr> --main       fast-forward main to it, then delete land/<name>
//   dotnet run eng/land.cs -- --commit <rev>    the same for a commit of your own
//
//   dotnet run eng/land.cs -- --train <pr> <pr> ...         build a train of approved heads and push it to land/<name>
//   dotnet run eng/land.cs -- --train <pr> <pr> ... --main  fast-forward main to that train
//
//   --name <name>   the land/ branch's name, in place of the one worked out
//   --print         print the git commands, run none
//   --delegated-by <who>  a train's merges carry `Delegated-by: <who>` (ADR 0239 point 4)
//   --fixtures <dir>  judge the closing-trailer and train fixtures (tests/fixtures/land/)
//
// ADR 0088: main accepts only a commit whose checks have passed, and a human
// gets one there by pushing it to land/<name>, letting CI and `agent review`
// run, and fast-forwarding main to it. ADR 0087, as amended 2026-10-08:
// `agent review` admits an agent's commits on a land/ push when the head is a
// pull request's approved head. ADR 0102: a release descriptor is cut only
// when landed that way. This helper is those steps, so that nobody has to
// remember them; it decides nothing. The rulesets and the checks still judge:
// only the maintainer may push land/, and main refuses a commit whose required
// checks have not passed.
//
// The name is worked out from what lands:
// - release-<version>, when the head adds releases/<version>.yaml, so that
//   the push also dry-runs the release (release.yml, land/release-**);
// - pr-<N> for a pull request; otherwise the commit's short sha.
//
// Before the first push it checks what would make the landing fail later:
// main must be an ancestor of the head, or the fast-forward is impossible. For
// a pull request, when the GitHub CLI is installed and signed in, it also
// looks for an approval comment for the head and says when there is none:
// a comment that is the approval line and nothing else, with the full sha,
// never edited (lib/Approvals.cs, the reader `agent review` uses; #290). The
// verdict stays `agent review`'s, so that is a warning.
//
// `--main` refuses unless land/<name> on origin is the same commit, so main
// moves only to the commit that was checked there.
//
// Closing trailers (#209). At both steps it lists every issue a `Closes`,
// `Fixes` or `Resolves #N` trailer line in the pushed commits will close,
// with its title. A pull request's body must carry one line `Completes: #N,
// ...` or `Completes: none` (the pull request template); the landing is
// refused when that line is missing, malformed, or names an issue no trailer
// closes. A release descriptor's own trailer is a commit's trailer like any
// other, so it counts. A `--commit` landing has no pull request, so it is only
// listed. After `--main` has moved main, every issue a pushed trailer names
// must be closed: one still open is printed with its commit and the exit is
// non-zero. It never closes anything itself, and when GitHub cannot be read
// it says so and exits non-zero rather than reporting success.
//
// A descriptor's date (#248). A release descriptor the head adds or changes is
// refused, at both steps, when its `date:` is more than three days before the
// landing day (UTC): the date is the day its SBOM digests were last written
// (docs/releases.md rule 3, amended 2026-11-10), and land.cs prints the
// command that writes them again.
//
// Landing trains (#277, ADR 0243 point 10, `LandingTrains`). A train is one
// head that merges two or more pull request heads onto main, in the order
// given, so that every gate runs once for all of them:
// - each member's head must be approved for its exact sha (an approval
//   comment as lib/Approvals.cs reads one: the whole body, the full sha,
//   never edited; refused here rather than warned, because the train's verdict
//   rests on each member's own), and green on its own: every check ruleset 1
//   requires (.github/repo-standard.yaml) concluded on that head;
// - each member's `Completes:` line is checked against its own commits;
// - at most one member adds a release descriptor, and it goes last, so the
//   land/ branch is land/release-<version> and the release is cut from the
//   train's head as for any landing;
// - each merge is `git merge-tree --write-tree` of the train so far and the
//   member, committed with `git commit-tree` and a `Landing-only: merge`
//   trailer, dated at the newest of the commits it joins, so the same members
//   on the same main give the same head. A conflict refuses the train: a
//   merge is never resolved by hand.
// `--main` re-reads land/<name>, checks that its first-parent line down to
// main is exactly those merges, each reproducible, with the members' current
// heads as second parents, and fast-forwards main to it. When the push is
// refused, or a merge conflicts, it prints the train without its last member.
// `agent review` admits the train's head on the same terms (eng/agent-review.cs).
//
// Exit codes: 0 done, 1 refused (or an issue left open after landing), 2
// could not run (GitHub unreadable included).
//
// See docs/adr/0088-only-checked-commits-reach-main.md.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Approvals.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

const string Remote = "origin";

int? pullRequest = null;
string? commit = null;
string? name = null;
bool toMain = false;
bool print = false;
string? fixtures = null;
List<int>? train = null;
string? delegatedBy = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--main":
            toMain = true;
            break;
        case "--print":
            print = true;
            break;
        case "--commit" when i + 1 < args.Length:
            commit = args[++i];
            break;
        case "--name" when i + 1 < args.Length:
            name = args[++i];
            break;
        case "--fixtures" when i + 1 < args.Length:
            fixtures = args[++i];
            break;
        case "--train":
            train = [];
            break;
        case "--delegated-by" when i + 1 < args.Length:
            delegatedBy = args[++i];
            break;
        case "--help" or "-h":
            Usage();
            return 0;
        default:
            if (int.TryParse(args[i].TrimStart('#'), out int number) && number > 0)
            {
                if (train is not null)
                {
                    train.Add(number);
                }
                else
                {
                    pullRequest = number;
                }

                break;
            }

            Console.Error.WriteLine($"land: unknown argument '{args[i]}'");
            Usage();
            return 2;
    }
}

if (fixtures is not null)
{
    return Fixtures.Run(fixtures);
}

if (train is not null)
{
    if (commit is not null)
    {
        Console.Error.WriteLine("land: --train takes pull request numbers, not --commit");
        return 2;
    }

    try
    {
        return LandTrain(train, toMain, print, name, delegatedBy);
    }
    catch (GitException exception)
    {
        Console.Error.WriteLine($"land: {exception.Message}");
        return 2;
    }
}

if (pullRequest is null == commit is null)
{
    Console.Error.WriteLine("land: give a pull request number or --commit <rev>, one of them");
    Usage();
    return 2;
}

try
{
    Git("fetch", "--quiet", "--no-tags", Remote, "refs/heads/main");
    string main = Git("rev-parse", "FETCH_HEAD");

    string head;
    if (pullRequest is int number)
    {
        Git("fetch", "--quiet", "--no-tags", Remote, $"refs/pull/{number}/head");
        head = Git("rev-parse", "FETCH_HEAD");
    }
    else
    {
        head = Git("rev-parse", "--verify", $"{commit}^{{commit}}");
    }

    bool namedByHand = name is not null;
    name ??= ReleaseName(main, head) ?? (pullRequest is int n ? $"pr-{n}" : head[..12]);
    string land = $"refs/heads/land/{name}";
    string what = pullRequest is int pr ? $"#{pr}'s head {head[..12]}" : head[..12];

    if (main == head)
    {
        Console.WriteLine($"land: main is already at {head[..12]}; nothing to land");
        return 0;
    }

    if (!IsAncestor(main, head))
    {
        Console.Error.WriteLine($"land: main ({main[..12]}) is not an ancestor of {what}, so main cannot be fast-forwarded to it. Bring main into the branch, then approve the new head.");
        return 1;
    }

    DateOnly landingDay = DateOnly.FromDateTime(DateTime.UtcNow);
    List<string> stale = [];
    foreach ((string version, string date) in PendingDescriptors(main, head))
    {
        if (Dates.Check(version, date, landingDay) is string refusal)
        {
            stale.Add(refusal);
        }
    }

    if (stale.Count > 0)
    {
        foreach (string refusal in stale)
        {
            Console.Error.WriteLine($"land: {refusal}");
        }

        return 1;
    }

    List<Trailer> closing = Closings.Trailers(PushedCommits(main, head));
    ListClosings(closing);
    if (pullRequest is int declared)
    {
        string? body = PullRequestBody(declared);
        if (body is null)
        {
            Console.Error.WriteLine($"land: could not read #{declared}'s body with the GitHub CLI, so its Completes: line cannot be checked; refusing rather than skipping it.");
            return 2;
        }

        List<string> refusals = Closings.CheckCompletes(body, closing);
        if (refusals.Count > 0)
        {
            foreach (string refusal in refusals)
            {
                Console.Error.WriteLine($"land: {refusal}");
            }

            return 1;
        }
    }

    string? landed = RemoteHead(land);

    if (!toMain)
    {
        if (landed == head)
        {
            Console.WriteLine($"land: land/{name} is at {head[..12]} already; its checks are running or done.");
        }
        else
        {
            if (pullRequest is int approved)
            {
                WarnWithoutApproval(approved, head);
            }

            Run(print, "git", "push", Remote, $"{head}:{land}");
        }

        Console.WriteLine();
        Console.WriteLine($"Next, once the checks on land/{name} are green:");
        string target = pullRequest is int again ? again.ToString() : $"--commit {head[..12]}";
        Console.WriteLine($"  dotnet run eng/land.cs -- {target}{(namedByHand ? $" --name {name}" : "")} --main");
        return 0;
    }

    if (landed != head)
    {
        Console.Error.WriteLine(landed is null
            ? $"land: there is no land/{name} on {Remote}; push it first (without --main) and let its checks run"
            : $"land: land/{name} is at {landed[..12]}, not {what}; the head moved since it was pushed. Land it again, without --main.");
        return 1;
    }

    if (Run(print, "git", "push", Remote, $"{head}:refs/heads/main") != 0)
    {
        Console.Error.WriteLine($"land: {Remote} refused to move main. If a required check on land/{name} has not passed yet, wait for it and run this again.");
        return 1;
    }

    Run(print, "git", "push", Remote, "--delete", land);
    if (print)
    {
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"land: main is at {head[..12]}.{(name.StartsWith("release-", StringComparison.Ordinal) ? " The release workflow now cuts the release it carries." : "")}");
    (int afterExit, List<string> afterSaid) = Closings.CheckClosed(closing, IssueStates(closing));
    foreach (string line in afterSaid)
    {
        (afterExit == 0 ? Console.Out : Console.Error).WriteLine($"land: {line}");
    }

    return afterExit;
}
catch (GitException exception)
{
    Console.Error.WriteLine($"land: {exception.Message}");
    return 2;
}

// The commits main..head, each its sha and full message.
static List<(string Sha, string Message)> PushedCommits(string main, string head)
{
    List<(string, string)> commits = [];
    foreach (string record in Git("log", "--format=%H%x00%B%x01", $"{main}..{head}").Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
    {
        string trimmed = record.TrimStart('\n', '\r');
        int separator = trimmed.IndexOf('\0', StringComparison.Ordinal);
        if (separator > 0)
        {
            commits.Add((trimmed[..separator], trimmed[(separator + 1)..]));
        }
    }

    return commits;
}

static void ListClosings(List<Trailer> closing)
{
    if (closing.Count == 0)
    {
        Console.WriteLine("land: the push closes no issue.");
        return;
    }

    Console.WriteLine("land: the push closes:");
    foreach (IGrouping<int, Trailer> issue in closing.GroupBy(static trailer => trailer.Issue).OrderBy(static group => group.Key))
    {
        (int exit, string title) = Try("gh", "api", $"repos/{{owner}}/{{repo}}/issues/{issue.Key}", "--jq", ".title");
        string titled = exit == 0 ? $" {title.Trim()}" : "";
        Console.WriteLine($"  #{issue.Key}{titled} ({string.Join(", ", issue.Select(static trailer => trailer.Sha[..12]))})");
    }
}

static string? PullRequestBody(int number)
{
    (int exit, string output) = Try("gh", "api", $"repos/{{owner}}/{{repo}}/pulls/{number}", "--jq", ".body // \"\"");
    return exit == 0 ? output : null;
}

// Each issue's state, "open" or "closed", or null when GitHub cannot be read.
// GitHub closes an issue a little after the push, so an open one is asked
// again for up to a minute before it counts.
static Dictionary<int, string>? IssueStates(List<Trailer> closing)
{
    Dictionary<int, string> states = [];
    int[] issues = [.. closing.Select(static trailer => trailer.Issue).Distinct()];
    for (int attempt = 0; ; attempt++)
    {
        foreach (int issue in issues)
        {
            (int exit, string output) = Try("gh", "api", $"repos/{{owner}}/{{repo}}/issues/{issue}", "--jq", ".state");
            if (exit != 0)
            {
                return null;
            }

            states[issue] = output.Trim();
        }

        if (attempt >= 6 || states.Values.All(static state => state == "closed"))
        {
            return states;
        }

        Thread.Sleep(TimeSpan.FromSeconds(10));
    }
}

// Each release descriptor the head adds or changes over main: its version and
// its date: as written at the head.
static List<(string Version, string Date)> PendingDescriptors(string main, string head)
{
    List<(string, string)> descriptors = [];
    foreach (string path in Git("diff", "--name-only", "--diff-filter=AM", main, head, "--", "releases/").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Match name = Regex.Match(path, @"^releases/(v[^/]+)\.yaml$");
        if (!name.Success)
        {
            continue;
        }

        Match date = Regex.Match(Git("show", $"{head}:{path}"), @"^date:[ \t]*(\S*)", RegexOptions.Multiline);
        descriptors.Add((name.Groups[1].Value, date.Success ? date.Groups[1].Value : ""));
    }

    return descriptors;
}

// release-<version>, when the head adds a descriptor that main does not have.
static string? ReleaseName(string main, string head)
{
    foreach (string path in Git("diff", "--name-only", "--diff-filter=A", main, head, "--", "releases/").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Match match = Regex.Match(path, @"^releases/(v[^/]+)\.yaml$");
        if (match.Success)
        {
            return $"release-{match.Groups[1].Value}";
        }
    }

    return null;
}

static bool IsAncestor(string ancestor, string descendant) =>
    Try("git", "merge-base", "--is-ancestor", ancestor, descendant).Exit == 0;

static string? RemoteHead(string reference)
{
    string line = Git("ls-remote", Remote, reference);
    return line.Length == 0 ? null : line.Split('\t')[0];
}

// Advisory only: `agent review` on the land/ push is the verdict (ADR 0087).
static void WarnWithoutApproval(int number, string head)
{
    if (ApprovedShas(number) is not List<string> approved)
    {
        Console.WriteLine("land: the GitHub CLI could not read the pull request's comments; not checking for an approval.");
        return;
    }

    if (!approved.Contains(head, StringComparer.Ordinal))
    {
        Console.WriteLine($"land: warning: no approval comment for head {head} on #{number}: the approval line as a comment's whole body, with the full sha, never edited. If it has an agent's commits, `agent review` will fail on land/ until there is one.");
    }
}

// A train (#277): build it and push it to land/<name>, or, with --main,
// fast-forward main to the train already checked there.
static int LandTrain(List<int> numbers, bool toMain, bool print, string? name, string? delegatedBy)
{
    Git("fetch", "--quiet", "--no-tags", Remote, "refs/heads/main");
    string main = Git("rev-parse", "FETCH_HEAD");
    string[] required = Trains.RequiredChecks(File.ReadAllText(Path.Combine(Git("rev-parse", "--show-toplevel"), ".github", "repo-standard.yaml")));

    List<TrainMember> members = [];
    foreach (int number in numbers)
    {
        Git("fetch", "--quiet", "--no-tags", Remote, $"refs/pull/{number}/head");
        string head = Git("rev-parse", "FETCH_HEAD");
        string? body = PullRequestBody(number);
        List<string>? approvals = ApprovedShas(number);
        Dictionary<string, string>? checks = CheckConclusions(head);
        if (body is null || approvals is null || checks is null)
        {
            Console.Error.WriteLine($"land: could not read #{number}'s body, comments or checks with the GitHub CLI, so the train cannot be checked; refusing rather than skipping it.");
            return 2;
        }

        members.Add(new TrainMember(number, head, approvals, Trains.Unmet(required, checks), body,
            PushedCommits(main, head), Trains.AddedDescriptors(main, head)));
    }

    List<string> refusals = Trains.Check(members);
    DateOnly landingDay = DateOnly.FromDateTime(DateTime.UtcNow);
    foreach (TrainMember member in members)
    {
        foreach ((string version, string date) in PendingDescriptors(main, member.Head))
        {
            if (Dates.Check(version, date, landingDay) is string refusal)
            {
                refusals.Add($"#{member.Number}: {refusal}");
            }
        }
    }

    if (refusals.Count > 0)
    {
        foreach (string refusal in refusals)
        {
            Console.Error.WriteLine($"land: {refusal}");
        }

        return 1;
    }

    name ??= Trains.Name(members);
    string land = $"refs/heads/land/{name}";
    string order = string.Join(" ", numbers);

    if (!toMain)
    {
        List<string> trailers = [];
        if (delegatedBy is not null)
        {
            trailers.Add($"Delegated-by: {delegatedBy}");
        }

        string lander = Git("config", "user.email");
        (int signExit, string signOff) = Try("dotnet", "run", Path.Combine(Git("rev-parse", "--show-toplevel"), "eng", "dco.cs"), "--", "--sign-off-for", lander);
        if (signExit == 0 && signOff.Trim().StartsWith("Signed-off-by: ", StringComparison.Ordinal))
        {
            trailers.Add(signOff.Trim());
        }

        (string? head, string? conflict, int at) = Trains.Build(main, members, trailers);
        if (head is null)
        {
            Console.Error.WriteLine($"land: {conflict}");
            if (at >= 0)
            {
                Console.Error.WriteLine(Trains.Shorter(numbers, at));
            }
            return 1;
        }

        Console.WriteLine($"land: the train {string.Join(", ", numbers.Select(static n => $"#{n}"))} onto main at {main[..12]} is {head[..12]}.");
        ListClosings(Closings.Trailers(PushedCommits(main, head)));

        if (RemoteHead(land) == head)
        {
            Console.WriteLine($"land: land/{name} is at {head[..12]} already; its checks are running or done.");
        }
        else
        {
            Run(print, "git", "push", Remote, $"{head}:{land}");
        }

        Console.WriteLine();
        Console.WriteLine($"Next, once the checks on land/{name} are green:");
        Console.WriteLine($"  dotnet run eng/land.cs -- --train {order}{(name != Trains.Name(members) ? $" --name {name}" : "")} --main");
        return 0;
    }

    string? landed = RemoteHead(land);
    if (landed is null)
    {
        Console.Error.WriteLine($"land: there is no land/{name} on {Remote}; push the train first (without --main) and let its checks run");
        return 1;
    }

    (List<string>? joined, string? why) = Trains.Verify(main, landed);
    if (joined is null)
    {
        Console.Error.WriteLine($"land: land/{name} at {landed[..12]} is not a train onto main at {main[..12]}: {why}. Build it again, without --main.");
        return 1;
    }

    if (!joined.SequenceEqual(members.Select(static member => member.Head)))
    {
        Console.Error.WriteLine($"land: land/{name} at {landed[..12]} joins {string.Join(", ", joined.Select(static sha => sha[..12]))}, not the members' heads now; a head moved since the train was built. Build it again, without --main.");
        return 1;
    }

    List<Trailer> closing = Closings.Trailers(PushedCommits(main, landed));
    ListClosings(closing);

    if (Run(print, "git", "push", Remote, $"{landed}:refs/heads/main") != 0)
    {
        Console.Error.WriteLine($"land: {Remote} refused to move main. If a required check on land/{name} has not passed yet, wait for it and run this again. If one failed, the train lands nothing; its members stay landable on their own.");
        Console.Error.WriteLine(Trains.Shorter(numbers, numbers.Count - 1));
        return 1;
    }

    Run(print, "git", "push", Remote, "--delete", land);
    if (print)
    {
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"land: main is at {landed[..12]}, the train {string.Join(", ", numbers.Select(static n => $"#{n}"))}.{(name.StartsWith("release-", StringComparison.Ordinal) ? " The release workflow now cuts the release it carries." : "")}");
    (int afterExit, List<string> afterSaid) = Closings.CheckClosed(closing, IssueStates(closing));
    foreach (string line in afterSaid)
    {
        (afterExit == 0 ? Console.Out : Console.Error).WriteLine($"land: {line}");
    }

    return afterExit;
}

// The full shas a pull request's approval comments name, or null when GitHub
// cannot be read. What an approval comment is, is lib/Approvals.cs's to say,
// for this script and for `agent review` alike (#290): the whole body, the
// full sha, never edited. Whose comment it is stays `agent review`'s verdict.
static List<string>? ApprovedShas(int number)
{
    // One comment a line, as compact JSON, so that a body's own newlines
    // cannot split or join comments.
    (int exit, string output) = Try("gh", "api", "--paginate", $"repos/{{owner}}/{{repo}}/issues/{number}/comments",
        "--jq", ".[] | {body, created_at, updated_at} | tojson");
    if (exit != 0)
    {
        return null;
    }

    List<string> shas = [];
    foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        try
        {
            using JsonDocument comment = JsonDocument.Parse(line);
            if (Approvals.Head(comment.RootElement) is string sha)
            {
                shas.Add(sha);
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    return shas;
}

// Each check's latest conclusion on a commit, by name, or null when GitHub
// cannot be read. A run still going has no conclusion and reads "pending".
static Dictionary<string, string>? CheckConclusions(string sha)
{
    (int exit, string output) = Try("gh", "api", "--paginate", $"repos/{{owner}}/{{repo}}/commits/{sha}/check-runs?filter=latest",
        "--jq", ".check_runs[] | \"\\(.name)\\t\\(.conclusion // \"pending\")\"");
    if (exit != 0)
    {
        return null;
    }

    Dictionary<string, string> conclusions = [];
    foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] parts = line.Split('\t');
        if (parts.Length == 2 && (!conclusions.TryGetValue(parts[0], out string? seen) || seen != "success"))
        {
            conclusions[parts[0]] = parts[1];
        }
    }

    return conclusions;
}

static int Run(bool print, string file, params string[] arguments)
{
    Console.WriteLine($"$ {file} {string.Join(' ', arguments)}");
    if (print)
    {
        return 0;
    }

    ProcessStartInfo start = new() { FileName = file, UseShellExecute = false };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new GitException($"could not start {file}");
    process.WaitForExit();
    return process.ExitCode;
}

static string Git(params string[] arguments)
{
    (int exit, string output) = Try("git", arguments);
    return exit == 0 ? output.Trim() : throw new GitException($"git {string.Join(' ', arguments)} failed");
}

static (int Exit, string Output) Try(string file, params string[] arguments)
{
    ProcessStartInfo start = new()
    {
        FileName = file,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    try
    {
        using Process process = Process.Start(start) ?? throw new GitException($"could not start {file}");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = error.Result;
        return (process.ExitCode, output);
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return (127, "");
    }
}

static void Usage() => Console.Error.WriteLine("""
    usage:
      dotnet run eng/land.cs -- <pr>              push the pull request's head to land/<name>
      dotnet run eng/land.cs -- <pr> --main       fast-forward main to it, then delete land/<name>
      dotnet run eng/land.cs -- --commit <rev>    the same for a commit of your own
      dotnet run eng/land.cs -- --train <pr> <pr> ...         build a train of approved heads, push it to land/<name>
      dotnet run eng/land.cs -- --train <pr> <pr> ... --main  fast-forward main to that train
    options:
      --name <name>   the land/ branch's name, in place of the one worked out
      --print         print the git commands, run none
      --delegated-by <who>  a train's merges carry `Delegated-by: <who>` (ADR 0239 point 4)
      --fixtures <dir>  judge the closing-trailer and train fixtures
    """);

sealed class GitException(string message) : Exception(message);

sealed record Trailer(int Issue, string Sha);

// The decisions, over plain values, so that the fixtures can judge them
// without git or GitHub.
static class Closings
{
    // eng/issue-refs.cs's closingTrailer: a closing keyword and a number,
    // alone on a line. The subject line is not a trailer.
    static readonly Regex ClosingTrailer = new(@"^\s*(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?\s+(?:(?<repository>[\w.-]+/[\w.-]+))?#(?<issue>\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex CompletesLine = new(@"^[ \t]*Completes:[ \t]*(?<value>.*?)[ \t]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static List<Trailer> Trailers(IEnumerable<(string Sha, string Message)> commits)
    {
        List<Trailer> trailers = [];
        foreach ((string sha, string message) in commits)
        {
            foreach (string line in message.Split('\n').Skip(1))
            {
                Match match = ClosingTrailer.Match(line.TrimEnd('\r'));
                if (match.Success && (!match.Groups["repository"].Success || match.Groups["repository"].Value.Equals("Hafeok/Varve", StringComparison.OrdinalIgnoreCase)))
                {
                    trailers.Add(new Trailer(int.Parse(match.Groups["issue"].Value, System.Globalization.CultureInfo.InvariantCulture), sha));
                }
            }
        }

        return trailers;
    }

    // What a pull request's Completes: line refuses. Empty means it may land.
    public static List<string> CheckCompletes(string body, List<Trailer> closing)
    {
        // Text inside HTML comments is the template's guidance, not the line.
        string visible = Regex.Replace(body, "<!--.*?-->", "", RegexOptions.Singleline);
        MatchCollection lines = CompletesLine.Matches(visible);
        if (lines.Count == 0)
        {
            return ["the pull request has no `Completes:` line. Add `Completes: #N, ...` naming every issue it completes, or `Completes: none` (#209)."];
        }

        if (lines.Count > 1)
        {
            return ["the pull request has more than one `Completes:` line; keep one."];
        }

        string value = lines[0].Groups["value"].Value;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        HashSet<int> declared = [];
        foreach (string item in value.Split(',', StringSplitOptions.TrimEntries))
        {
            Match number = Regex.Match(item, @"^#(\d+)$");
            if (!number.Success)
            {
                return [$"the `Completes:` line reads '{value}'; it is `Completes: none` or issues as #N separated by commas."];
            }

            declared.Add(int.Parse(number.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        HashSet<int> closed = [.. closing.Select(static trailer => trailer.Issue)];
        return [.. declared.Where(issue => !closed.Contains(issue)).Order().Select(static issue =>
            $"the pull request completes #{issue}, and no pushed commit closes it. Add a `Closes #{issue}` trailer line to the commit that completes it.")];
    }

    // After main moved: 0 when every closed issue is closed, 1 when one is
    // still open, 2 when the states could not be read.
    public static (int Exit, List<string> Said) CheckClosed(List<Trailer> closing, Dictionary<int, string>? states)
    {
        if (closing.Count == 0)
        {
            return (0, []);
        }

        if (states is null)
        {
            return (2, ["could not read the closed issues' state from GitHub (no token, or a rate limit), so the closings are not confirmed. Check them by hand."]);
        }

        List<string> said = [];
        foreach (IGrouping<int, Trailer> issue in closing.GroupBy(static trailer => trailer.Issue).OrderBy(static group => group.Key))
        {
            if (!states.TryGetValue(issue.Key, out string? state) || state != "closed")
            {
                said.Add($"#{issue.Key} is still open, though {string.Join(", ", issue.Select(static trailer => trailer.Sha[..12]))} closes it. Close it by hand with a comment naming the commit on main.");
            }
        }

        return said.Count == 0
            ? (0, [$"every issue the push closes is closed: {string.Join(", ", closing.Select(static trailer => $"#{trailer.Issue}").Distinct())}."])
            : (1, said);
    }
}

// A descriptor's date against the landing day (#248): null when it may land.
static class Dates
{
    public const int Window = 3;

    public static string? Check(string version, string date, DateOnly landingDay)
    {
        string rewrite = $"dotnet run eng/release.cs -- --sbom {version} --syft <syft v1.54.0> --binary <rid>...";
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly day))
        {
            return $"releases/{version}.yaml has no date: as yyyy-mm-dd ('{date}'). Write its digests and date again:\n  {rewrite}";
        }

        int age = landingDay.DayNumber - day.DayNumber;
        return age <= Window
            ? null
            : $"releases/{version}.yaml is dated {date}, {age} days before the landing day {landingDay:yyyy-MM-dd}; at most {Window} are allowed, because the date is the day its SBOM digests were written (docs/releases.md rule 3). Write them again, which sets the date, then commit, push and ask for approval of the new head:\n  {rewrite}";
    }
}

// A member of a train: its pull request, head, the shas its approvals name,
// the required checks not green on its head, its body, its own commits over
// main, and the release descriptors it adds.
sealed record TrainMember(
    int Number,
    string Head,
    List<string> Approvals,
    List<string> Unmet,
    string Body,
    List<(string Sha, string Message)> Commits,
    List<string> Descriptors);

// Landing trains (#277, ADR 0243 point 10). The checks are over plain values;
// Build and Verify run git, in whatever repository GIT_DIR or the working
// directory names, so that the fixtures run them on a scratch repository.
static class Trains
{
    public const string MergeTrailer = "Landing-only: merge";

    static readonly Regex LandingOnlyMerge = new(@"^Landing-only:[ \t]*merge[ \t]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    // Every context ruleset 1 requires, read from .github/repo-standard.yaml.
    public static string[] RequiredChecks(string standard)
    {
        int start = standard.IndexOf("required_status_checks:", StringComparison.Ordinal);
        return start < 0
            ? []
            : [.. Regex.Matches(standard[start..], @"^\s*-\s*context:\s*(?<name>.+?)\s*$", RegexOptions.Multiline)
                .Select(static match => match.Groups["name"].Value.Trim('"', '\''))];
    }

    // The required checks not concluded success (or skipped or neutral) on a head.
    public static List<string> Unmet(string[] required, Dictionary<string, string> conclusions) =>
        [.. required.Where(check => !conclusions.TryGetValue(check, out string? conclusion) || conclusion is not ("success" or "skipped" or "neutral"))
            .Select(check => conclusions.TryGetValue(check, out string? conclusion) ? $"{check} ({conclusion})" : $"{check} (not run)")];

    // The versions of the release descriptors a head adds over main.
    public static List<string> AddedDescriptors(string main, string head) =>
        [.. Shell.Git("diff", "--name-only", "--diff-filter=A", main, head, "--", "releases/")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => Regex.Match(path, @"^releases/(v[^/]+)\.yaml$"))
            .Where(static match => match.Success)
            .Select(static match => match.Groups[1].Value)];

    // The settings every train merge runs under (ADR 0243 point 10, the merge
    // is pinned). eng/agent-review.cs states the same; change both together.
    // Attributes come from the train so far, never a working tree, with the
    // global and system files off; renames are fixed.
    static readonly string[] PinnedMergeSettings =
    [
        "-c", "core.attributesFile=",
        "-c", "merge.renames=true",
        "-c", "merge.directoryRenames=false",
        "-c", "merge.renameLimit=7000",
        "-c", "diff.renames=true",
        "-c", "diff.renameLimit=7000",
    ];

    public static (int Exit, string Output) MergeTree(string ours, string theirs) =>
        Shell.Try(new() { ["GIT_ATTR_NOSYSTEM"] = "1" }, null, "git",
            [.. PinnedMergeSettings, $"--attr-source={ours}", "merge-tree", "--write-tree", "--name-only", "--no-messages", ours, theirs]);

    // A repository-local info/attributes changes how a merge resolves and no
    // option switches it off, so it refuses the train. Null when there is none.
    public static string? LocalAttributes()
    {
        string path = Shell.Git("rev-parse", "--git-path", "info/attributes");
        return File.Exists(path) && File.ReadAllLines(path).Any(static line => line.Trim() is { Length: > 0 } text && !text.StartsWith('#'))
            ? $"{path} sets attributes, which change how a merge resolves; a train is built and judged without them (ADR 0243 point 10). Move them into .gitattributes or remove them."
            : null;
    }

    // What refuses the train before it is built. Empty means it may be built.
    public static List<string> Check(List<TrainMember> members)
    {
        List<string> refusals = [];
        if (members.Count < 2)
        {
            string alone = members.Count == 1 ? $" Land #{members[0].Number} alone: dotnet run eng/land.cs -- {members[0].Number}" : "";
            return [$"a train has two or more members.{alone}"];
        }

        foreach (IGrouping<int, TrainMember> twice in members.GroupBy(static member => member.Number).Where(static group => group.Count() > 1))
        {
            refusals.Add($"#{twice.Key} is in the train {twice.Count()} times; name each member once.");
        }

        foreach (TrainMember member in members)
        {
            string head = member.Head[..12];
            if (!member.Approvals.Contains(member.Head, StringComparer.Ordinal))
            {
                string others = member.Approvals.Count == 0 ? "it has none" : $"its approvals are for {string.Join(", ", member.Approvals.Distinct().Select(static sha => sha.Length > 12 ? sha[..12] : sha))}";
                refusals.Add($"#{member.Number}'s head {head} has no approval comment (the approval line as the whole body, the full sha, never edited); {others}. A train lands only heads approved as they are.");
            }

            if (member.Unmet.Count > 0)
            {
                refusals.Add($"#{member.Number}'s head {head} is not green on its own: {string.Join(", ", member.Unmet)}.");
            }

            foreach (string refusal in Closings.CheckCompletes(member.Body, Closings.Trailers(member.Commits)))
            {
                refusals.Add($"#{member.Number}: {refusal}");
            }
        }

        List<TrainMember> releasing = [.. members.Where(static member => member.Descriptors.Count > 0)];
        if (releasing.Count > 1 || releasing.Any(static member => member.Descriptors.Count > 1))
        {
            refusals.Add($"the train carries {releasing.Sum(static member => member.Descriptors.Count)} release descriptors ("
                + string.Join(", ", releasing.SelectMany(static member => member.Descriptors.Select(version => $"#{member.Number}: {version}")))
                + "); a train carries at most one.");
        }
        else if (releasing.Count == 1 && releasing[0] != members[^1])
        {
            refusals.Add($"#{releasing[0].Number} carries the release descriptor {releasing[0].Descriptors[0]}, so it goes last in the train.");
        }

        return refusals;
    }

    // land/release-<version> when the last member carries a descriptor, so
    // that the push dry-runs the release; otherwise land/train-<N>-<N>-...
    public static string Name(List<TrainMember> members) =>
        members[^1].Descriptors is [string version]
            ? $"release-{version}"
            : "train-" + string.Join("-", members.Select(static member => member.Number));

    // Merges each member's head onto the train so far, from main, by
    // `git merge-tree`. The head, or null and the conflict.
    public static (string? Head, string? Conflict, int At) Build(string main, List<TrainMember> members, List<string> trailers)
    {
        string date = members.Select(static member => member.Head).Append(main)
            .Select(static sha => long.Parse(Shell.Git("log", "-1", "--format=%ct", sha), System.Globalization.CultureInfo.InvariantCulture))
            .Max().ToString(System.Globalization.CultureInfo.InvariantCulture);
        Dictionary<string, string> dated = new() { ["GIT_AUTHOR_DATE"] = $"{date} +0000", ["GIT_COMMITTER_DATE"] = $"{date} +0000" };
        string order = string.Join(", ", members.Select(static member => $"#{member.Number}"));

        if (LocalAttributes() is string attributes)
        {
            return (null, attributes, -1);
        }

        string train = main;
        for (int i = 0; i < members.Count; i++)
        {
            TrainMember member = members[i];
            (int exit, string output) = MergeTree(train, member.Head);
            string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (exit == 1)
            {
                return (null, $"#{member.Number} conflicts with the train before it ({string.Join(", ", members.Take(i).Select(static m => $"#{m.Number}"))} on main) in {string.Join(", ", lines.Skip(1))}; a train's merge is never resolved by hand.", i);
            }

            if (exit != 0 || lines.Length == 0)
            {
                throw new GitException($"git merge-tree {train[..12]} {member.Head[..12]} failed");
            }

            string message = $"""
                Merge #{member.Number} into the train for main

                Member {i + 1} of {members.Count} of the train {order}, onto main at {main[..12]}, by
                `git merge-tree` (eng/land.cs --train).

                Refs #{member.Number}

                {MergeTrailer}
                {string.Join("\n", trailers)}
                """.TrimEnd() + "\n";
            (int commitExit, string commit) = Shell.Try(dated, message, "git", "commit-tree", lines[0], "-p", train, "-p", member.Head, "-F", "-");
            if (commitExit != 0)
            {
                throw new GitException($"git commit-tree for #{member.Number} failed");
            }

            train = commit.Trim();
        }

        return (train, null, members.Count);
    }

    // What to run without the member at `index` and those after it.
    public static string Shorter(List<int> numbers, int index) =>
        index switch
        {
            < 0 => "",
            0 => "land: no shorter train: the first member is the one at fault.",
            1 => $"land: the train without #{numbers[1]}{(numbers.Count > 2 ? " and those after it" : "")} is #{numbers[0]} alone: dotnet run eng/land.cs -- {numbers[0]}",
            _ => $"land: the train without #{numbers[index]}{(index < numbers.Count - 1 ? " and those after it" : "")}: dotnet run eng/land.cs -- --train {string.Join(" ", numbers.Take(index))}",
        };

    // A head is a train onto main when its first-parent line down to main is
    // only `Landing-only: merge` commits of two parents, each one's tree the
    // `git merge-tree` of its parents. The second parents, in order, or null
    // and why not. eng/agent-review.cs holds its own copy of this rule.
    public static (List<string>? Joined, string? Why) Verify(string main, string head)
    {
        if (LocalAttributes() is string attributes)
        {
            return (null, attributes);
        }

        List<string> joined = [];
        string at = head;
        while (at != main)
        {
            (int exit, string parents) = Shell.Try(null, null, "git", "rev-list", "--parents", "-n", "1", at);
            string[] shas = parents.Trim().Split(' ');
            if (exit != 0)
            {
                return (null, $"{at[..12]} cannot be read");
            }

            if (shas.Length != 3)
            {
                return (null, $"{at[..12]} is not a merge of two parents, and main is not below it on its first-parent line");
            }

            if (!LandingOnlyMerge.IsMatch(Shell.Git("log", "-1", "--format=%B", at)))
            {
                return (null, $"{at[..12]} has no `{MergeTrailer}` trailer");
            }

            (int mergeExit, string merged) = MergeTree(shas[1], shas[2]);
            if (mergeExit != 0 || merged.Split('\n')[0].Trim() != Shell.Git("rev-parse", $"{at}^{{tree}}"))
            {
                return (null, $"{at[..12]}'s tree is not `git merge-tree` of its parents under the pinned settings: it was resolved or edited by hand, or built by a different git. Build the train again");
            }

            joined.Insert(0, shas[2]);
            at = shas[1];
        }

        return joined.Count < 2 ? (null, "a train has two or more members") : (joined, null);
    }
}

// git and other processes, with an environment and standard input, for the
// classes above (the top-level helpers cannot be called from a class).
static class Shell
{
    public static string Git(params string[] arguments)
    {
        (int exit, string output) = Try(null, null, "git", arguments);
        return exit == 0 ? output.Trim() : throw new GitException($"git {string.Join(' ', arguments)} failed");
    }

    public static (int Exit, string Output) Try(Dictionary<string, string>? environment, string? input, string file, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = file,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in environment ?? [])
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start) ?? throw new GitException($"could not start {file}");
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = error.Result;
        return (process.ExitCode, output);
    }
}

// tests/fixtures/land/: each case a JSON file. `step` is "push" (the
// Completes: check before a push) or "landed" (the check after main moved).
// A "date" case holds a descriptor's `version`, `date` and the `landing` day,
// and no commits. `commits` are the pushed commits ({sha, message}); `body`
// the pull request's body; `states` the issues' states, null when GitHub
// cannot be read. An "approval" case holds a `head` and one `comment` (`body`,
// `created_at`, `updated_at`), and passes when lib/Approvals.cs reads the
// comment as an approval of that head; in the body `{line}` stands for the
// approval line for the head, `{line12}` for the same with 12 characters of
// it and `{lineupper}` for the same in capitals, so that no fixture file holds the line itself (#290). A
// `pass-` case must pass and a `fail-` case be refused, and `says` lists text
// the output must contain.
static class Fixtures
{
    public static int Run(string directory)
    {
        string[] cases = [.. Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal)];
        int wrong = 0;
        foreach (string file in cases)
        {
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = fixture.RootElement;
            List<Trailer> closing = root.TryGetProperty("commits", out JsonElement commits)
                ? Closings.Trailers(commits.EnumerateArray().Select(static commit => (commit.GetProperty("sha").GetString()!, commit.GetProperty("message").GetString()!)))
                : [];
            string step = root.GetProperty("step").GetString()!;
            (bool passes, List<string> said) = step switch
            {
                "push" => Push(root, closing),
                "landed" => Landed(root, closing),
                "date" => Date(root),
                "train" => Train(root),
                "approval" => Approval(root),
                _ => throw new InvalidOperationException($"{file}: unknown step '{step}'"),
            };

            string output = string.Join("\n", said);
            string[] missing = root.TryGetProperty("says", out JsonElement says)
                ? [.. says.EnumerateArray().Select(static line => line.GetString()!).Where(line => !output.Contains(line, StringComparison.Ordinal))]
                : [];
            bool expected = Path.GetFileName(file).StartsWith("pass-", StringComparison.Ordinal);
            bool right = passes == expected && missing.Length == 0;
            wrong += right ? 0 : 1;
            Console.WriteLine((right ? "ok   " : "FAIL ") + Path.GetFileName(file) + ": " + (said.Count == 0 ? "lands" : string.Join(" ", said)));
            foreach (string line in missing)
            {
                Console.WriteLine("     the output does not say: " + line);
            }
        }

        Console.WriteLine(cases.Length + " cases, " + wrong + " judged wrong");
        return cases.Length > 0 && wrong == 0 ? 0 : 1;
    }

    static (bool, List<string>) Push(JsonElement root, List<Trailer> closing)
    {
        List<string> refusals = Closings.CheckCompletes(root.GetProperty("body").GetString()!, closing);
        return (refusals.Count == 0, refusals);
    }

    // The reader both scripts share (#290), on one comment.
    static (bool, List<string>) Approval(JsonElement root)
    {
        string head = root.GetProperty("head").GetString()!;
        JsonElement comment = root.GetProperty("comment");
        string body = comment.GetProperty("body").GetString()!
            .Replace("{line12}", "approve " + head[..12], StringComparison.Ordinal)
            .Replace("{lineupper}", "approve " + head.ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{line}", "approve " + head, StringComparison.Ordinal);
        string? Text(string property) => comment.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
        string? approved = Approvals.Head(body, Text("created_at"), Text("updated_at"));
        return string.Equals(approved, head, StringComparison.Ordinal)
            ? (true, [])
            : (false, [approved is null ? "the comment approves nothing" : $"the comment approves {approved}, not the head"]);
    }

    static (bool, List<string>) Date(JsonElement root)
    {
        string? refusal = Dates.Check(root.GetProperty("version").GetString()!, root.GetProperty("date").GetString()!,
            DateOnly.ParseExact(root.GetProperty("landing").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return (refusal is null, refusal is null ? [] : [refusal]);
    }

    // A train case is built for real: a scratch bare repository holds main
    // and each member as a commit on it, and the train is checked, built by
    // `git merge-tree` and verified there. `approval` is "head", "other" (an
    // approval naming another sha) or "none"; `green` false gives the member
    // a failed required check; `edit` true replaces the last merge with one
    // whose tree was changed by hand, which Verify must refuse.
    static (bool, List<string>) Train(JsonElement root)
    {
        string scratch = Directory.CreateTempSubdirectory("land-train-").FullName;
        Dictionary<string, string?> saved = [];
        foreach ((string key, string value) in new Dictionary<string, string>
        {
            ["GIT_DIR"] = scratch,
            ["GIT_AUTHOR_NAME"] = "Fixture",
            ["GIT_AUTHOR_EMAIL"] = "fixture@example.org",
            ["GIT_COMMITTER_NAME"] = "Fixture",
            ["GIT_COMMITTER_EMAIL"] = "fixture@example.org",
        })
        {
            saved[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            Shell.Git("init", "--quiet", "--bare", scratch);
            if (root.TryGetProperty("infoAttributes", out JsonElement local))
            {
                Directory.CreateDirectory(Path.Combine(scratch, "info"));
                File.WriteAllText(Path.Combine(scratch, "info", "attributes"), local.GetString());
            }
            string main = Commit(scratch, null, root.GetProperty("main"), "chore: main\n\nRefs #1\n");
            List<TrainMember> members = [];
            foreach (JsonElement member in root.GetProperty("members").EnumerateArray())
            {
                int number = member.GetProperty("number").GetInt32();
                string message = member.TryGetProperty("message", out JsonElement written) ? written.GetString()! : $"feat: #{number}\n\nRefs #{number}\n";
                string head = Commit(scratch, main, member.GetProperty("files"), message);
                List<string> approvals = member.GetProperty("approval").GetString() switch
                {
                    "head" => [head],
                    "other" => [main],
                    _ => [],
                };
                List<string> unmet = member.GetProperty("green").GetBoolean() ? [] : ["build (ubuntu-latest) (failure)"];
                string body = member.TryGetProperty("body", out JsonElement given) ? given.GetString()! : "Completes: none\n";
                List<(string, string)> commits = [];
                foreach (string record in Shell.Git("log", "--format=%H%x00%B%x01", $"{main}..{head}").Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = record.TrimStart('\n').Split('\0', 2);
                    commits.Add((parts[0], parts[1]));
                }

                members.Add(new TrainMember(number, head, approvals, unmet, body, commits, Trains.AddedDescriptors(main, head)));
            }

            List<string> refusals = Trains.Check(members);
            if (refusals.Count > 0)
            {
                return (false, refusals);
            }

            List<int> numbers = [.. members.Select(static member => member.Number)];
            (string? train, string? conflict, int at) = Trains.Build(main, members, ["Signed-off-by: Fixture <fixture@example.org>"]);
            if (train is null)
            {
                return (false, [conflict!, .. at >= 0 ? [Trains.Shorter(numbers, at)] : Array.Empty<string>()]);
            }

            (string? again, _, _) = Trains.Build(main, members, ["Signed-off-by: Fixture <fixture@example.org>"]);
            if (again != train)
            {
                return (false, [$"the same members on the same main built {train[..12]} and then {again?[..12]}"]);
            }

            if (root.TryGetProperty("edit", out JsonElement edit) && edit.GetBoolean())
            {
                string[] parents = Shell.Git("rev-list", "--parents", "-n", "1", train).Split(' ');
                string blob = Shell.Try(null, "a line nobody reviewed\n", "git", "hash-object", "-w", "--stdin").Output.Trim();
                string index = Path.Combine(scratch, "edit-index");
                Dictionary<string, string> indexed = new() { ["GIT_INDEX_FILE"] = index };
                Shell.Try(indexed, null, "git", "read-tree", $"{train}^{{tree}}");
                Shell.Try(indexed, null, "git", "update-index", "--add", "--cacheinfo", $"100644,{blob},unreviewed.txt");
                string tree = Shell.Try(indexed, null, "git", "write-tree").Output.Trim();
                train = Shell.Try(null, Shell.Git("log", "-1", "--format=%B", train), "git", "commit-tree", tree, "-p", parents[1], "-p", parents[2], "-F", "-").Output.Trim();
            }

            (List<string>? joined, string? why) = Trains.Verify(main, train);
            if (joined is null)
            {
                return (false, [$"the train {train[..12]} does not verify: {why}"]);
            }

            if (!joined.SequenceEqual(members.Select(static member => member.Head)))
            {
                return (false, [$"the train {train[..12]} joins other heads than its members'"]);
            }

            string merges = Shell.Git("log", "--first-parent", "--format=%B%x01", $"{main}..{train}");
            int marked = merges.Split('\u0001', StringSplitOptions.RemoveEmptyEntries).Count(static message => message.Contains(Trains.MergeTrailer, StringComparison.Ordinal));
            string subjects = Shell.Git("log", "--reverse", "--first-parent", "--format=%s", $"{main}..{train}").Replace("\n", "; ", StringComparison.Ordinal);
            return (true, [$"the train {string.Join(", ", numbers.Select(static n => $"#{n}"))} is {train[..12]}, {marked} `{Trains.MergeTrailer}` merges ({subjects}), built twice the same, land/{Trains.Name(members)}"]);
        }
        finally
        {
            foreach ((string key, string? value) in saved)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            Directory.Delete(scratch, recursive: true);
        }
    }

    // A commit of `files` (path to content) over `parent`'s tree, by plumbing.
    static string Commit(string scratch, string? parent, JsonElement files, string message)
    {
        string index = Path.Combine(scratch, "fixture-index");
        File.Delete(index);
        Dictionary<string, string> indexed = new() { ["GIT_INDEX_FILE"] = index };
        if (parent is not null)
        {
            Shell.Try(indexed, null, "git", "read-tree", $"{parent}^{{tree}}");
        }

        foreach (JsonProperty file in files.EnumerateObject())
        {
            string blob = Shell.Try(null, file.Value.GetString()!, "git", "hash-object", "-w", "--stdin").Output.Trim();
            Shell.Try(indexed, null, "git", "update-index", "--add", "--cacheinfo", $"100644,{blob},{file.Name}");
        }

        string tree = Shell.Try(indexed, null, "git", "write-tree").Output.Trim();
        string[] arguments = parent is null ? ["commit-tree", tree, "-F", "-"] : ["commit-tree", tree, "-p", parent, "-F", "-"];
        return Shell.Try(null, message, "git", arguments).Output.Trim();
    }

    static (bool, List<string>) Landed(JsonElement root, List<Trailer> closing)
    {
        JsonElement states = root.GetProperty("states");
        Dictionary<int, string>? read = states.ValueKind == JsonValueKind.Null
            ? null
            : states.EnumerateObject().ToDictionary(static state => int.Parse(state.Name, System.Globalization.CultureInfo.InvariantCulture), static state => state.Value.GetString()!);
        (int exit, List<string> said) = Closings.CheckClosed(closing, read);
        return (exit == 0, said);
    }
}
