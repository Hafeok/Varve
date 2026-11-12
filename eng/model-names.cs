// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// A commit message never names a model; a model's identifier is allowed
// (ADR 0241, #169).
//
//   dotnet run eng/model-names.cs [-- --base <ref> | --range <a..b>]
//   dotnet run eng/model-names.cs -- --message <file>
//   dotnet run eng/model-names.cs -- --fixtures tests/fixtures/model-names
//
// A name is a family word from eng/model-families.txt, optionally after
// "Claude", followed by a version: "Opus 5.5", "Claude Sonnet 4". It is matched
// regardless of case, and only standing alone, so the API identifier
// (claude-<family>-<n>-<n>, hyphenated) and the bare "Claude" are not names.
// This file and eng/model-families.txt hold the pattern once: the commit-msg
// hook and the gate both run this script.
//
// --- the range (eng/ci.cs, every pull request and land/ push) ----------------
//
// Every commit the range adds, merges included, since a merge's message lands
// on main too. The range is worked out as eng/issue-refs.cs works it out: a
// pull request's GITHUB_BASE_REF, a push to main's before, otherwise
// origin/main..HEAD. Commits reachable from 2cad18d5, the last commit on main
// that names a model, are left out: ADR 0241 point 4 leaves history as it is
// (#169, the delegate's comment of 2026-11-11). Each finding names the commit
// and the line.
//
// --- --message (eng/hooks/commit-msg) ------------------------------------------
//
// The message being committed. A Co-Authored-By: trailer that names a model is
// rewritten to the allowed one, `Co-Authored-By: Claude
// <noreply@anthropic.com>`, and a repeat of it is folded. A name anywhere else
// is refused with the line quoted: the hook cannot know what the author meant.
// Comment lines, and everything below a scissors line, are not the message.
//
// --- --fixtures -------------------------------------------------------------------
//
// tests/fixtures/model-names/: a pass-*.txt message must be accepted unchanged,
// a fail-*.txt one refused, and each rewrite-* directory's input.txt must be
// rewritten to its expected.txt and then accepted.
//
// Exit codes: 0 conformant (or rewritten), 1 a name, 2 could not run.

// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Text.RegularExpressions;

const string LowerBound = "2cad18d53eac54e4b485ad7161916b9f17d2259b";

string root = FindRepositoryRoot();
string? baseRef = null;
string? explicitRange = null;
string? message = null;
string? fixtures = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--base" when i + 1 < args.Length:
            baseRef = args[++i];
            break;
        case "--range" when i + 1 < args.Length:
            explicitRange = args[++i];
            break;
        case "--message" when i + 1 < args.Length:
            message = args[++i];
            break;
        case "--fixtures" when i + 1 < args.Length:
            fixtures = args[++i];
            break;
        default:
            Console.Error.WriteLine($"model-names: '{args[i]}' is not an option (see the header of eng/model-names.cs).");
            return 2;
    }
}

ModelNames names;
try
{
    names = ModelNames.Load(Path.Combine(root, "eng", "model-families.txt"));
}
catch (IOException exception)
{
    Console.Error.WriteLine($"model-names: {exception.Message}");
    return 2;
}

if (fixtures is not null)
{
    return Fixtures(names, Path.GetFullPath(fixtures));
}

if (message is not null)
{
    string text = File.ReadAllText(message);
    (string rewritten, List<string> refused) = names.Rewrite(text);
    if (refused.Count > 0)
    {
        foreach (string line in refused)
        {
            Console.Error.WriteLine($"model-names: the message names a model: \"{line.Trim()}\"");
        }

        Console.Error.WriteLine("model-names: a commit message never names a model; its identifier is allowed (ADR 0241). Reword the line, or use the identifier.");
        return 1;
    }

    if (rewritten != text)
    {
        File.WriteAllText(message, rewritten);
        Console.Error.WriteLine($"model-names: a co-author trailer named a model; it now reads `{ModelNames.AllowedTrailer}` (ADR 0241).");
    }

    return 0;
}

string range = explicitRange ?? ResolveRange(root, baseRef);
List<string> log = ["log", "--format=%x01%H%x02%B", range];
if (Git(root, "rev-parse", "--verify", "--quiet", LowerBound + "^{commit}").Exit == 0)
{
    log.Add("^" + LowerBound);
}

Console.WriteLine($"note range: {range}, after {LowerBound[..8]}");
(int exit, string output, string error) = Git(root, [.. log]);
if (exit != 0)
{
    Console.Error.WriteLine($"model-names: could not read '{range}': {error.Trim()}. In a shallow clone the base may be missing; fetch it, or pass --range.");
    return 2;
}

int commits = 0;
List<string> findings = [];
foreach (string record in output.Split('\u0001', StringSplitOptions.RemoveEmptyEntries))
{
    int split = record.IndexOf('\u0002', StringComparison.Ordinal);
    if (split < 0)
    {
        continue;
    }

    commits++;
    string sha = record[..split];
    foreach (string line in names.Named(record[(split + 1)..]))
    {
        findings.Add($"{sha[..8]}: \"{line.Trim()}\"");
    }
}

if (findings.Count == 0)
{
    Console.WriteLine($"model-names: {commits} commit(s), none names a model.");
    return 0;
}

Console.Error.WriteLine($"FAIL: {findings.Count} line(s) name a model (ADR 0241: a commit message never names a model; its identifier is allowed):");
foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

Console.Error.WriteLine("model-names: eng/hooks/commit-msg rewrites a co-author trailer and refuses the rest when the commit is made; `git config core.hooksPath eng/hooks` sets it. Reword the commits, then push again.");
return 1;

static int Fixtures(ModelNames names, string directory)
{
    int cases = 0;
    int wrong = 0;
    foreach (string file in Directory.GetFiles(directory, "*.txt").Order(StringComparer.Ordinal))
    {
        cases++;
        string name = Path.GetFileName(file);
        string text = File.ReadAllText(file);
        (string rewritten, List<string> refused) = names.Rewrite(text);
        bool passes = refused.Count == 0 && rewritten == text && names.Named(text).Count == 0;
        bool right = passes == name.StartsWith("pass-", StringComparison.Ordinal);
        wrong += right ? 0 : 1;
        Console.WriteLine((right ? "ok   " : "FAIL ") + name + ": " + (passes ? "accepted" : refused.Count > 0 ? "refused: " + refused[0].Trim() : "named in a trailer the hook rewrites"));
    }

    foreach (string folder in Directory.GetDirectories(directory, "rewrite-*").Order(StringComparer.Ordinal))
    {
        cases++;
        (string rewritten, List<string> refused) = names.Rewrite(File.ReadAllText(Path.Combine(folder, "input.txt")));
        string expected = File.ReadAllText(Path.Combine(folder, "expected.txt"));
        // As committed: git strips the comment lines the hook leaves alone.
        string committed = string.Join('\n', rewritten.Split('\n').Where(static line => !line.StartsWith('#')));
        bool right = refused.Count == 0 && rewritten == expected && names.Named(committed).Count == 0;
        wrong += right ? 0 : 1;
        Console.WriteLine((right ? "ok   " : "FAIL ") + Path.GetFileName(folder) + ": " + (right ? "rewritten as expected" : "rewritten to:\n" + rewritten));
    }

    Console.WriteLine(cases + " cases, " + wrong + " judged wrong");
    return cases > 0 && wrong == 0 ? 0 : 1;
}

static string ResolveRange(string root, string? baseRef)
{
    string head = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "HEAD";
    if (baseRef is not null)
    {
        return $"{baseRef}..{head}";
    }

    string? pullRequestBase = Environment.GetEnvironmentVariable("GITHUB_BASE_REF");
    if (!string.IsNullOrEmpty(pullRequestBase))
    {
        return $"origin/{pullRequestBase}..{head}";
    }

    string? before = Environment.GetEnvironmentVariable("GITHUB_EVENT_BEFORE");
    bool toMain = Environment.GetEnvironmentVariable("GITHUB_REF") == "refs/heads/main";
    if (toMain && !string.IsNullOrEmpty(before) && before.Trim('0').Length > 0
        && Git(root, "rev-parse", "--verify", "--quiet", before + "^{commit}").Exit == 0)
    {
        return $"{before}..{head}";
    }

    return Git(root, "rev-parse", "--verify", "--quiet", "origin/main^{commit}").Exit == 0 ? $"origin/main..{head}" : $"main..{head}";
}

static (int Exit, string Output, string Error) Git(string root, params string[] arguments)
{
    ProcessStartInfo start = new()
    {
        FileName = "git",
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start) ?? throw new InvalidOperationException("could not start git");
    Task<string> error = process.StandardError.ReadToEndAsync();
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, output, error.Result);
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

// The pattern, and what the hook and the gate do with it.
sealed class ModelNames(Regex name)
{
    public const string AllowedTrailer = "Co-Authored-By: Claude <noreply@anthropic.com>";

    static readonly Regex CoAuthor = new(@"^\s*Co-Authored-By:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ModelNames Load(string path)
    {
        string[] families = [.. File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(Regex.Escape)];
        if (families.Length == 0)
        {
            throw new IOException($"{path} lists no model family");
        }

        // Standing alone: no word character or hyphen on either side, so the
        // hyphenated API identifier never matches.
        return new ModelNames(new Regex(
            $@"(?<![\w-])(?:Claude\s+)?(?:{string.Join('|', families)})\s+v?\d+(?:\.\d+)*(?![\w-]|\.\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    // The lines of a committed message that name a model.
    public List<string> Named(string message) =>
        [.. message.Split('\n').Select(static line => line.TrimEnd('\r')).Where(line => name.IsMatch(line))];

    // The hook: co-author trailers that name a model become the allowed one,
    // and any other line that names one is refused. Comment lines and what
    // follows a scissors line are not part of the message.
    public (string Rewritten, List<string> Refused) Rewrite(string message)
    {
        List<string> output = [];
        List<string> refused = [];
        bool allowedSeen = false;
        bool below = false;
        foreach (string raw in message.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (below || line.StartsWith('#'))
            {
                below |= line.StartsWith("# ------------------------ >8 ------------------------", StringComparison.Ordinal);
                output.Add(raw);
                continue;
            }

            bool coAuthor = CoAuthor.IsMatch(line);
            if (coAuthor && name.IsMatch(line))
            {
                if (!allowedSeen)
                {
                    output.Add(AllowedTrailer);
                    allowedSeen = true;
                }

                continue;
            }

            if (name.IsMatch(line))
            {
                refused.Add(line);
            }

            if (coAuthor && line.Trim().Equals(AllowedTrailer, StringComparison.OrdinalIgnoreCase))
            {
                if (allowedSeen)
                {
                    continue;
                }

                allowedSeen = true;
            }

            output.Add(raw);
        }

        return (string.Join('\n', output), refused);
    }
}
