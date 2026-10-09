// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Landing, as two commands.
//
//   dotnet run eng/land.cs -- <pr>              push the pull request's head to land/<name>
//   dotnet run eng/land.cs -- <pr> --main       fast-forward main to it, then delete land/<name>
//   dotnet run eng/land.cs -- --commit <rev>    the same for a commit of your own
//
//   --name <name>   the land/ branch's name, in place of the one worked out
//   --print         print the git commands, run none
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
// looks for an `approve <sha>` comment on the head and says when there is
// none; the verdict stays `agent review`'s, so that is a warning.
//
// `--main` refuses unless land/<name> on origin is the same commit, so main
// moves only to the commit that was checked there.
//
// Exit codes: 0 done, 1 refused, 2 could not run.
//
// See docs/adr/0088-only-checked-commits-reach-main.md.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text.RegularExpressions;

const string Remote = "origin";

int? pullRequest = null;
string? commit = null;
string? name = null;
bool toMain = false;
bool print = false;

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
        case "--help" or "-h":
            Usage();
            return 0;
        default:
            if (int.TryParse(args[i].TrimStart('#'), out int number) && number > 0)
            {
                pullRequest = number;
                break;
            }

            Console.Error.WriteLine($"land: unknown argument '{args[i]}'");
            Usage();
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
    return 0;
}
catch (GitException exception)
{
    Console.Error.WriteLine($"land: {exception.Message}");
    return 2;
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
    (int exit, string output) = Try("gh", "api", "--paginate", $"repos/{{owner}}/{{repo}}/issues/{number}/comments", "--jq", ".[].body");
    if (exit != 0)
    {
        Console.WriteLine("land: the GitHub CLI could not read the pull request's comments; not checking for an approval.");
        return;
    }

    foreach (Match match in Regex.Matches(output, @"\bapprove\s+([0-9a-f]{12,40})\b", RegexOptions.IgnoreCase))
    {
        if (head.StartsWith(match.Groups[1].Value.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return;
        }
    }

    Console.WriteLine($"land: warning: no `approve {head[..12]}` comment on #{number}. If it has an agent's commits, `agent review` will fail on land/ until there is one.");
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
    options:
      --name <name>   the land/ branch's name, in place of the one worked out
      --print         print the git commands, run none
    """);

sealed class GitException(string message) : Exception(message);
