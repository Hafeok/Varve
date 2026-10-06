// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// DCO sign-off gate.
//
//   dotnet run eng/dco.cs -- [--base <ref>] [--head <ref>]
//   dotnet run eng/dco.cs -- --range <a>..<b>
//   dotnet run eng/dco.cs -- --commits <file> [--identities <file>]
//
// ADR 0034 point 3: every commit carries a DCO sign-off, human and AI session
// alike. Until this gate it was enforced by nobody (ADR 0034, amendment of
// 2026-10-06). For every non-merge commit in the range:
//
//   - a human's commit carries "Signed-off-by: <author name> <author email>",
//     name exactly and email ignoring case: the person who wrote it is the
//     person who asserts the right to contribute it;
//   - an agent's commit (an author email the identity map lists under agents)
//     carries a sign-off naming its responsible human or one of that human's
//     delegates, by their name and one of their emails: a session cannot
//     assert anything, so the human directing it does (ADR 0034, ADR 0087);
//   - an author the map lists as exempt is skipped, as eng/issue-refs.cs skips
//     it: automation whose messages cannot be configured.
//
// An author the map does not know is held to the human rule.
//
// Merge commits are exempt by parent count. A merge's message is generated, and
// the commits it brings are each checked on their own.
//
// The range, when not given, is worked out from the environment: a pull
// request gives GITHUB_BASE_REF; a push to main gives the before sha; a push to
// any other branch is checked from origin/main, because what a required check
// on that branch's head vouches for is everything it would bring to main
// (ADR 0088); a local run uses origin/main..HEAD.
//
// --commits reads commits from a file instead of git, which is how the failure
// path is proven: tests/fixtures/dco/ holds a failing and a passing set, with
// their own identity map (--identities).
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0034-commit-signing-and-the-sandbox-exception.md and
// docs/adr/0087-the-identity-map.md.

// The identity map's loader is shared with the other gates that read it. CA2266
// asks a file with directives to begin with a shebang, and the licence-header
// gate requires the notice on line 1 (ADR 0031); the notice wins, and CA2266
// is off for this file only, as in eng/browser-tests.cs (ADR 0087).
#:include lib/Identities.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text;

string repositoryRoot = FindRepositoryRoot();

string? baseRef = null;
string? headRef = null;
string? explicitRange = null;
string? commitsFile = null;
string? identitiesFile = null;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    switch (arguments[i])
    {
        case "--base" when i + 1 < arguments.Length:
            baseRef = arguments[++i];
            break;
        case "--head" when i + 1 < arguments.Length:
            headRef = arguments[++i];
            break;
        case "--range" when i + 1 < arguments.Length:
            explicitRange = arguments[++i];
            break;
        case "--commits" when i + 1 < arguments.Length:
            commitsFile = arguments[++i];
            break;
        case "--identities" when i + 1 < arguments.Length:
            identitiesFile = arguments[++i];
            break;
        default:
            break;
    }
}

// --- the identity map ------------------------------------------------------

List<string> problems = [];
IdentityMap? map = identitiesFile is null
    ? IdentityMap.Load(repositoryRoot, problems)
    : File.Exists(identitiesFile) ? IdentityMap.Parse(File.ReadAllText(identitiesFile), problems) : null;

if (map is null || problems.Count > 0)
{
    foreach (string problem in problems)
    {
        Console.Error.WriteLine($"dco: {problem}");
    }

    if (map is null && identitiesFile is not null && problems.Count == 0)
    {
        Console.Error.WriteLine($"dco: no identity map at '{identitiesFile}'.");
    }

    return 2;
}

// --- the commits -------------------------------------------------------------

List<Commit> commits;

if (commitsFile is not null)
{
    if (!File.Exists(commitsFile))
    {
        Console.Error.WriteLine($"dco: no commits file at '{commitsFile}'.");
        return 2;
    }

    Console.WriteLine($"note commits: {commitsFile}");
    commits = ParseCommitsFile(File.ReadAllText(commitsFile));
}
else
{
    string range = explicitRange ?? ResolveRange(repositoryRoot, baseRef, headRef);
    Console.WriteLine($"note range: {range}");

    // \u0001 opens a record, \u0002 separates the fields, so a message
    // containing anything at all cannot be confused for a delimiter.
    (int exitCode, string stdout, string stderr) = Git(repositoryRoot, "log", "--format=%x01%H%x02%P%x02%an%x02%ae%x02%B", range);

    if (exitCode != 0)
    {
        Console.Error.WriteLine($"dco: could not read '{range}': {stderr.Trim()}");
        Console.Error.WriteLine("dco: in a shallow clone the base may not be present. Fetch it, or pass --range.");
        return 2;
    }

    commits = [];

    foreach (string record in stdout.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] fields = record.Split('\u0002');

        if (fields.Length < 5)
        {
            Console.Error.WriteLine("dco: git log returned a record with too few fields.");
            return 2;
        }

        commits.Add(new Commit(
            fields[0].Trim(),
            fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            fields[2].Trim(),
            fields[3].Trim(),
            fields[4]));
    }
}

// --- the rule ----------------------------------------------------------------

List<string> findings = [];
int checkedCommits = 0;
int merges = 0;
int exempt = 0;
int byAgents = 0;

foreach (Commit commit in commits)
{
    if (commit.Parents > 1)
    {
        merges++;
        continue;
    }

    if (map.IsExempt(commit.AuthorName))
    {
        exempt++;
        continue;
    }

    checkedCommits++;
    List<SignOff> signOffs = SignOff.FromMessage(commit.Message);
    string subject = commit.Message.Split('\n')[0].Trim();
    string label = $"{commit.Sha[..Math.Min(8, commit.Sha.Length)]} {subject}";

    if (map.AgentByEmail(commit.AuthorEmail) is Agent agent)
    {
        byAgents++;
        IReadOnlyList<Human> acting = map.ActingFor(agent);

        bool signed = signOffs.Any(signOff => acting.Any(human =>
            human.Emails.Any(email => signOff.Is(human.Name, email))));

        if (!signed)
        {
            string who = string.Join(", ", acting.Select(human => $"{human.Name} <{human.Emails.FirstOrDefault()}>"));
            findings.Add($"{label}: authored by the agent '{agent.Id}', so it needs a sign-off by its responsible human or a delegate ({who}); "
                + (signOffs.Count == 0 ? "it has none" : $"it has {Describe(signOffs)}"));
        }

        continue;
    }

    if (!signOffs.Any(signOff => signOff.Is(commit.AuthorName, commit.AuthorEmail)))
    {
        findings.Add($"{label}: needs 'Signed-off-by: {commit.AuthorName} <{commit.AuthorEmail}>', matching its author; "
            + (signOffs.Count == 0 ? "it has none" : $"it has {Describe(signOffs)}"));
    }
}

Console.WriteLine($"note checked {checkedCommits} commit(s), {byAgents} by an agent; skipped {merges} merge(s) and {exempt} exempt");

if (findings.Count == 0)
{
    Console.WriteLine("ok  every commit carries a sign-off its author or its responsible human makes");
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"FAIL: {findings.Count} commit(s) without a valid DCO sign-off (ADR 0034, ADR 0087):");

foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

Console.Error.WriteLine();
Console.Error.WriteLine("`git commit -s` adds the author's sign-off. An agent's commit is signed off by the");
Console.Error.WriteLine($"human the identity map ({IdentityMap.RelativePath}) makes responsible for it.");
return 1;

// --- helpers ---------------------------------------------------------------

static string Describe(List<SignOff> signOffs) =>
    string.Join(", ", signOffs.Select(signOff => $"'{signOff.Name} <{signOff.Email}>'"));

// The fixture format: blocks of
//
//   commit <sha>
//   parents <n>
//   author <name> <email>
//   message
//   <the message, any number of lines>
//   end
//
// with '#' lines outside a message ignored.
static List<Commit> ParseCommitsFile(string text)
{
    List<Commit> parsed = [];
    string sha = "";
    int parents = 1;
    string name = "";
    string email = "";
    StringBuilder? message = null;

    foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
    {
        if (message is not null)
        {
            if (raw == "end")
            {
                parsed.Add(new Commit(sha, parents, name, email, message.ToString()));
                message = null;
                parents = 1;
            }
            else
            {
                message.Append(raw).Append('\n');
            }

            continue;
        }

        string line = raw.Trim();

        if (line.StartsWith("commit ", StringComparison.Ordinal))
        {
            sha = line["commit ".Length..].Trim();
        }
        else if (line.StartsWith("parents ", StringComparison.Ordinal))
        {
            parents = int.Parse(line["parents ".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (line.StartsWith("author ", StringComparison.Ordinal))
        {
            string value = line["author ".Length..];
            int open = value.LastIndexOf('<');
            name = value[..open].Trim();
            email = value[(open + 1)..value.LastIndexOf('>')].Trim();
        }
        else if (line == "message")
        {
            message = new StringBuilder();
        }
    }

    return parsed;
}

static string ResolveRange(string root, string? baseRef, string? headRef)
{
    string head = headRef ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "HEAD";

    if (baseRef is not null)
    {
        return $"{baseRef}..{head}";
    }

    string? prBase = Environment.GetEnvironmentVariable("GITHUB_BASE_REF");

    if (!string.IsNullOrEmpty(prBase))
    {
        return $"origin/{prBase}..{head}";
    }

    // A push to main is checked from the before sha. A push to any other
    // branch is checked from main, whatever was pushed before: its head is
    // what main will be fast-forwarded to (ADR 0088).
    string? before = Environment.GetEnvironmentVariable("GITHUB_EVENT_BEFORE");
    bool toMain = Environment.GetEnvironmentVariable("GITHUB_REF") == "refs/heads/main";

    if (toMain && !string.IsNullOrEmpty(before) && before.Trim('0').Length > 0 && Exists(root, before))
    {
        return $"{before}..{head}";
    }

    return Exists(root, "origin/main") ? $"origin/main..{head}" : $"main..{head}";
}

static bool Exists(string root, string reference) =>
    Git(root, "rev-parse", "--verify", "--quiet", reference + "^{commit}").ExitCode == 0;

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

sealed record Commit(string Sha, int Parents, string AuthorName, string AuthorEmail, string Message);
