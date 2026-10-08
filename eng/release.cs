// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Release descriptors: the validator, and the plan step of the release
// workflow (ADR 0102, docs/releases.md).
//
//   dotnet run eng/release.cs -- --check [--base <ref>]
//   dotnet run eng/release.cs -- --plan [--dry-run] [--landing] [--since <sha>] [--fallback <dir>] [--offline]
//   dotnet run eng/release.cs -- --draft <version>
//   dotnet run eng/release.cs -- --notes <version> --into <dir>
//   dotnet run eng/release.cs -- --fixtures tests/fixtures/releases
//
// A release is a decision, and this repository files decisions. Adding
// releases/<version>.yaml to a pull request proposes the release; landing that
// pull request at its approved head cuts it: .github/workflows/release.yml runs
// --plan, then every gate at the commit under release, then the gates App tags
// it. Nothing in the path holds a credential of a person or waits for one.
//
// --- --check (eng/ci.cs, every pull request) -----------------------------------
//
// Every descriptor's shape (lib/Releases.cs), that CHANGELOG.md is the
// projection of releases/, that a cut descriptor is not edited (it is
// immutable; corrections are a new version), and for each proposed descriptor
// — one with no tag, or one this range adds — everything its basis says,
// resolved at the commit under release (`commit:` if set, else HEAD):
//
//   version          not tagged already, and after every v* tag by precedence:
//                    never reused, which NuGet's immutability requires anyway
//   commit           resolves, and is an ancestor of main
//   issue: N         closed by a commit in the range from the previous v* tag
//                    (a Closes/Fixes/Resolves #N line); online, it exists
//   adr: NNNN        complete both ways: exactly the ADRs added since the
//                    previous v* tag, each with status Accepted
//   storage-format   equals FormatVersion.Current in Varve.Store, so ADR
//                    0072's read-forever commitment is a reviewed line
//   accepted-by      a human in eng/identities.json who may approve for every
//                    agent that authored a commit in the range (ADR 0087)
//
// and prints how the pull request lands: the maintainer pushes its approved
// head to land/<name>, and main is fast-forwarded to it.
//
// --- --plan (the release workflow) ---------------------------------------------
//
// The same, for the descriptors with no tag, at main's head, plus the
// project's status at the commit under release (lib/Status.cs: the README
// that ships in the packages names this release, and the roadmap marks what it
// closes), and two online checks: the issues exist, and the head guard. The guard is why the
// descriptor needs no other: **the commit under release carries a successful
// `agent review` check run from the gates App**, which that App posts only on
// an approved pull request's head or a land/ head (ADR 0087). A merge commit
// GitHub creates never carries one, so a descriptor landed by the merge button
// is not cut and leaves no tag; it is recovered by a retro-cut, `commit:`
// naming the approved head, which is an ancestor of main and carries the run.
//
// One release at a time: two pending descriptors are refused. --since <sha>
// (a push's before) plans only when the push changed releases/, which is what
// a workflow `paths` filter would do were the land/ dry run not on the same
// trigger. --landing is a land/release-** push: the head is not on main yet,
// so it must descend from main instead. A dry run (--dry-run, or --landing)
// does not stop at its findings: it reports them as `problems` and still
// plans, so the workflow runs the gates and shows the tagging identity, and
// its last step fails when anything stood in the way. With --fallback <dir> a
// dry run plans that directory's descriptor when releases/ has nothing
// pending, so the path is exercised before the first real release.
//
// Writes the plan to GITHUB_OUTPUT (count, problems, version, sha, message,
// prerelease, previous) and the table to GITHUB_STEP_SUMMARY when they are
// set.
//
// --- --draft, --notes, --fixtures ----------------------------------------------
//
// --draft prints a descriptor to start from, with the adr: lines and the
// storage format computed, and the ledger keys first shipped as comments
// (information, not basis). --notes writes a version's GitHub Release title
// and notes, for publish.yml. --fixtures runs every case under the directory:
// each must fail, and say what its expect.txt says (tests/fixtures/releases/).
//
// Exit codes: 0 valid (or planned), 1 findings, 2 could not run.
//
// See docs/adr/0102-a-release-is-a-descriptor.md.

// The identity map and the descriptor reader are shared with other gates.
// CA2266 is off for this file only, for the reason eng/dco.cs gives (ADR 0087).
#:include lib/Identities.cs
#:include lib/Releases.cs
#:include lib/Status.cs
#:property NoWarn=$(NoWarn);CA2266

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repositoryRoot);

string? mode = null;
string? modeArgument = null;
string? baseRef = null;
string? since = null;
string? fallback = null;
string? into = null;
string releasesPath = Path.Combine(repositoryRoot, ReleaseFormat.Directory);
string changelogPath = Path.Combine(repositoryRoot, "CHANGELOG.md");
bool dryRun = false;
bool landing = false;
bool offline = false;

string[] arguments = args;
for (int i = 0; i < arguments.Length; i++)
{
    switch (arguments[i])
    {
        case "--check" or "--plan":
            mode = arguments[i][2..];
            break;
        case "--draft" or "--notes" or "--fixtures" when i + 1 < arguments.Length:
            mode = arguments[i][2..];
            modeArgument = arguments[++i];
            break;
        case "--base" when i + 1 < arguments.Length:
            baseRef = arguments[++i];
            break;
        case "--since" when i + 1 < arguments.Length:
            since = arguments[++i];
            break;
        case "--fallback" when i + 1 < arguments.Length:
            fallback = Path.GetFullPath(arguments[++i]);
            break;
        case "--into" when i + 1 < arguments.Length:
            into = Path.GetFullPath(arguments[++i]);
            break;
        case "--releases" when i + 1 < arguments.Length:
            releasesPath = Path.GetFullPath(arguments[++i]);
            break;
        case "--changelog" when i + 1 < arguments.Length:
            changelogPath = Path.GetFullPath(arguments[++i]);
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "--landing":
            landing = true;
            break;
        case "--offline":
            offline = true;
            break;
        default:
            Console.Error.WriteLine($"release: '{arguments[i]}' is not an option (see the header of eng/release.cs).");
            return 2;
    }
}

Repository repository = new(repositoryRoot);

try
{
    return mode switch
    {
        "check" => await Check(repository, releasesPath, changelogPath, baseRef ?? ResolveBase(repository), isFixture: false, Console.Out),
        "plan" => await Plan(repository, releasesPath, since, fallback, dryRun, landing, offline),
        "draft" => Draft(repository, modeArgument!),
        "notes" => Notes(repository, releasesPath, modeArgument!, into),
        "fixtures" => await Fixtures(repository, Path.GetFullPath(modeArgument!)),
        _ => Usage(),
    };
}
catch (GitException exception)
{
    Console.Error.WriteLine($"release: {exception.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("release: one of --check, --plan, --draft <version>, --notes <version> --into <dir>, --fixtures <dir>.");
    return 2;
}

// --- --check ---------------------------------------------------------------------

static async Task<int> Check(Repository repository, string releasesPath, string changelogPath, string? baseRef, bool isFixture, TextWriter output)
{
    List<string> problems = [];
    List<Descriptor> descriptors = ReleaseFormat.ReadDirectory(repository.Relative(releasesPath), problems);
    HashSet<string> tags = repository.Tags();

    foreach (Descriptor descriptor in descriptors)
    {
        ReleaseFormat.CheckShape(descriptor, problems);
    }

    // CHANGELOG.md is the projection of releases/ (eng/changelog.cs).
    if (File.Exists(changelogPath))
    {
        string? projectUrl = ReleaseFormat.ReadProjectUrl(repository.Root);

        if (projectUrl is null)
        {
            problems.Add("Directory.Build.targets has no PackageProjectUrl, which CHANGELOG.md's links are built from (ADR 0029)");
        }
        else if (problems.Count == 0)
        {
            string committed = File.ReadAllText(changelogPath).ReplaceLineEndings("\n");

            if (committed != ReleaseFormat.RenderChangelog(descriptors, projectUrl))
            {
                problems.Add($"{repository.Relative(changelogPath)} is not the projection of {repository.Relative(releasesPath)}: it was edited by hand, "
                    + "or a descriptor was not folded in (dotnet run eng/changelog.cs)");
            }
        }
    }

    // Immutable once cut, and what this range proposes.
    HashSet<string> added = new(StringComparer.Ordinal);

    if (!isFixture && baseRef is not null && repository.Exists(baseRef))
    {
        string relative = repository.Relative(releasesPath);
        output.WriteLine($"note base {baseRef}");

        foreach ((string status, string file) in repository.Changes(baseRef, "HEAD", relative))
        {
            string version = Path.GetFileNameWithoutExtension(file);

            if (!file.EndsWith(".yaml", StringComparison.Ordinal))
            {
                continue;
            }

            if (status == "A")
            {
                added.Add(version);
            }
            else if (tags.Contains(version))
            {
                problems.Add($"{file}: {version} is cut, and a cut descriptor is immutable; a correction is a new version (docs/releases.md §2)");
            }
        }
    }

    List<Descriptor> proposed = [.. descriptors.Where(descriptor => descriptor.Version is not null
        && (isFixture || !tags.Contains(descriptor.Version) || added.Contains(descriptor.Version)))];

    foreach (Descriptor descriptor in proposed)
    {
        // A release cut before descriptors, recorded afterwards: pinned to its
        // tag's commit, it is checked for what it can be, and never cut again.
        if (IsRecord(repository, descriptor, tags))
        {
            CheckRecord(repository, descriptor, problems);
            output.WriteLine($"note {descriptor.Version} records a release cut before descriptors, at {descriptor.Commit![..12]}; the cut skips it");
            continue;
        }

        Resolution? resolution = Resolve(repository, descriptor, tags, problems, landing: false);

        if (resolution is null)
        {
            continue;
        }

        output.WriteLine($"note {descriptor.Version} would be cut at {resolution.Sha[..12]}, after {resolution.Previous ?? "no earlier release"}");

        if (!isFixture)
        {
            string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            string? name = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

            if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(name))
            {
                await CheckIssuesExist(new Api(token, name), resolution.Issues, descriptor, problems);
            }
            else
            {
                output.WriteLine($"note offline: that {string.Join(", ", resolution.Issues.Select(issue => $"#{issue}"))} exist, and the head guard, are checked by the release workflow");
            }

            output.WriteLine();
            output.WriteLine($"     {descriptor.Version} lands at its approved head, never by a merge commit (docs/releases.md §3):");
            output.WriteLine("       the maintainer pushes the approved head to land/<name>, CI and agent review run there,");
            output.WriteLine("       and main is fast-forwarded to it: git push origin <approved head>:main.");
            output.WriteLine("       Landed by the merge button instead, it is not cut; recover it with a retro-cut,");
            output.WriteLine("       `commit: <approved head>` in a new pull request (the one legitimate use of commit).");
            output.WriteLine();
        }
    }

    return Report(problems, $"{descriptors.Count} descriptor(s), {proposed.Count} proposed", output);
}

static int Report(List<string> problems, string what, TextWriter output)
{
    if (problems.Count == 0)
    {
        output.WriteLine($"ok  releases: {what}");
        return 0;
    }

    output.WriteLine();
    output.WriteLine($"FAIL: {problems.Count} problem(s) in the release descriptors ({what}):");

    foreach (string problem in problems)
    {
        output.WriteLine($"  {problem}");
    }

    output.WriteLine();
    output.WriteLine("See docs/releases.md (ADR 0102).");
    return 1;
}

// --- resolving a descriptor ------------------------------------------------------

static Resolution? Resolve(Repository repository, Descriptor descriptor, HashSet<string> tags, List<string> problems, bool landing)
{
    string path = descriptor.Path;
    string version = descriptor.Version!;

    if (!ReleaseFormat.Version.IsMatch(version))
    {
        return null;
    }

    if (tags.Contains(version))
    {
        problems.Add($"{path}: {version} is tagged already; versions are never reused, and a correction is a new version");
    }

    string? newest = tags.Where(tag => ReleaseFormat.Version.IsMatch(tag)).OrderByDescending(tag => tag, Comparer<string>.Create(ReleaseFormat.Compare)).FirstOrDefault();

    if (newest is not null && ReleaseFormat.Compare(version, newest) <= 0 && !tags.Contains(version))
    {
        problems.Add($"{path}: {version} does not come after {newest}, the newest release, by SemVer precedence");
    }

    // The commit under release.
    string main = repository.Exists("origin/main") ? "origin/main" : "main";
    string target = descriptor.Commit ?? "HEAD";
    string? sha = repository.ResolveCommit(target);

    if (sha is null)
    {
        problems.Add($"{path}: commit '{target}' is not a commit in this clone");
        return null;
    }

    if (descriptor.Commit is not null && !repository.IsAncestor(sha, main))
    {
        problems.Add($"{path}: commit {sha[..12]} is not an ancestor of {main}; a release never pins what main does not carry");
    }
    else if (landing && descriptor.Commit is null && !repository.IsAncestor(repository.ResolveCommit(main)!, sha))
    {
        problems.Add($"{path}: the land/ head {sha[..12]} does not descend from {main}, so main cannot be fast-forwarded to it");
    }

    string? previous = repository.PreviousTag(sha);
    string range = previous is null ? sha : $"{previous}..{sha}";
    List<CommitInfo> commits = repository.Commits(range);

    // issue: each closed by a commit in the range.
    List<int> issues = [];

    foreach (BasisLine issueLine in descriptor.Basis.Where(line => line.Kind == "issue"))
    {
        if (!int.TryParse(issueLine.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int issue))
        {
            continue;
        }

        issues.Add(issue);
        Regex closes = new(@"(?im)^\s*(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b:?\s+(?:[\w.-]+/[\w.-]+)?#" + issue + @"\s*$", RegexOptions.CultureInvariant);

        if (!commits.Any(commit => closes.IsMatch(commit.Message)))
        {
            problems.Add($"{path}:{issueLine.Line}: #{issue} is not closed by a commit in {range} (no 'Closes #{issue}' trailer); the release closes its milestone");
        }
    }

    // adr: complete both ways.
    Dictionary<string, string> adrsAtTarget = repository.Adrs(sha);
    Dictionary<string, string> adrsAtPrevious = previous is null ? [] : repository.Adrs(previous);
    HashSet<string> firstShipped = [.. adrsAtTarget.Keys.Where(number => !adrsAtPrevious.ContainsKey(number))];
    HashSet<string> listed = [.. descriptor.Basis.Where(line => line.Kind == "adr").Select(line => line.Value)];

    foreach (BasisLine line in descriptor.Basis.Where(line => line.Kind == "adr"))
    {
        if (!adrsAtTarget.TryGetValue(line.Value, out string? file))
        {
            problems.Add($"{path}:{line.Line}: ADR {line.Value} does not exist at {sha[..12]}");
        }
        else if (adrsAtPrevious.ContainsKey(line.Value))
        {
            problems.Add($"{path}:{line.Line}: ADR {line.Value} shipped in {previous} already; basis names what this release ships first");
        }
        else if (!repository.IsAccepted(sha, file))
        {
            problems.Add($"{path}:{line.Line}: ADR {line.Value} is not Accepted at {sha[..12]} ({file})");
        }
    }

    foreach (string missing in firstShipped.Where(number => !listed.Contains(number)).Order(StringComparer.Ordinal))
    {
        problems.Add($"{path}: ADR {missing} is first shipped by {version} and is not in basis; add '  - adr: {missing}'");
    }

    // storage-format: Varve.Store's.
    BasisLine? formatLine = descriptor.Basis.FirstOrDefault(line => line.Kind == "storage-format");
    int? storage = repository.StorageFormat(sha);

    if (storage is null)
    {
        problems.Add($"{path}: FormatVersion.Current could not be read from {Repository.FormatVersionPath} at {sha[..12]}");
    }
    else if (formatLine is not null && formatLine.Value != storage.Value.ToString(CultureInfo.InvariantCulture))
    {
        problems.Add($"{path}:{formatLine.Line}: storage format {formatLine.Value} is not Varve.Store's, {storage} (FormatVersion.Current); "
            + "a release names the format it writes, read for ever from then on (ADR 0072)");
    }

    // accepted-by: a human who may approve for every agent in the range.
    if (descriptor.AcceptedBy is not null && descriptor.AcceptedBy.StartsWith("mailto:", StringComparison.Ordinal))
    {
        List<string> mapProblems = [];
        IdentityMap map = IdentityMap.Parse(repository.Show(sha, IdentityMap.RelativePath) ?? "{}", mapProblems);
        string email = descriptor.AcceptedBy["mailto:".Length..];
        Human? human = map.HumanByEmail(email);

        if (human is null)
        {
            problems.Add($"{path}: accepted-by {email} is not a human in {IdentityMap.RelativePath}");
        }
        else
        {
            foreach (Agent agent in commits.Select(commit => map.AgentByEmail(commit.AuthorEmail)).OfType<Agent>().Distinct())
            {
                if (!map.ActingFor(agent).Contains(human))
                {
                    problems.Add($"{path}: accepted-by {email} may not approve for '{agent.Id}', which authored commits in {range} (ADR 0087)");
                }
            }
        }
    }

    return new Resolution(sha, previous, issues);
}

// A descriptor for a version tagged before descriptors existed: its tag
// exists, and its commit is that tag's commit.
static bool IsRecord(Repository repository, Descriptor descriptor, HashSet<string> tags) =>
    descriptor.Version is not null && descriptor.Commit is not null && tags.Contains(descriptor.Version)
    && repository.ResolveCommit($"refs/tags/{descriptor.Version}") == descriptor.Commit;

// What a record can be checked for: the storage format its commit writes. Its
// issues were closed by hand and its ADRs predate any basis; it is never cut.
static void CheckRecord(Repository repository, Descriptor descriptor, List<string> problems)
{
    BasisLine? formatLine = descriptor.Basis.FirstOrDefault(line => line.Kind == "storage-format");
    int? storage = repository.StorageFormat(descriptor.Commit!);

    if (formatLine is not null && storage is not null && formatLine.Value != storage.Value.ToString(CultureInfo.InvariantCulture))
    {
        problems.Add($"{descriptor.Path}:{formatLine.Line}: storage format {formatLine.Value} is not the one {descriptor.Version} wrote, {storage}");
    }
}

static async Task CheckIssuesExist(Api api, IReadOnlyList<int> issues, Descriptor descriptor, List<string> problems)
{
    foreach (int issue in issues)
    {
        using JsonDocument? found = await api.TryGet($"issues/{issue}");

        if (found is null)
        {
            problems.Add($"{descriptor.Path}: #{issue} does not exist");
        }
        else if (found.RootElement.TryGetProperty("pull_request", out _))
        {
            problems.Add($"{descriptor.Path}: #{issue} is a pull request, not a milestone's issue");
        }
    }
}

// The head guard: the gates App's `agent review` succeeded on this very commit.
static async Task<string?> HeadGuard(Api api, Repository repository, string sha)
{
    long? app = repository.PinnedIntegration("agent review");

    if (app is null)
    {
        return "'agent review' has no integration_id in .github/repo-standard.yaml, so no App's run can be told from a job's (ADR 0087)";
    }

    using JsonDocument? runs = await api.TryGet($"commits/{sha}/check-runs?check_name={Uri.EscapeDataString("agent review")}&filter=all&per_page=100");

    if (runs is null)
    {
        return $"the check runs of {sha[..12]} could not be read";
    }

    foreach (JsonElement run in runs.RootElement.GetProperty("check_runs").EnumerateArray())
    {
        if (run.GetProperty("app").GetProperty("id").GetInt64() == app
            && run.TryGetProperty("conclusion", out JsonElement conclusion) && conclusion.GetString() == "success")
        {
            return null;
        }
    }

    return $"{sha[..12]} carries no successful 'agent review' from the gates App ({app}), so it is not an approved head; "
        + "a merge commit never is. Recover with a retro-cut: a new descriptor version with `commit: <approved head>` (docs/releases.md §3)";
}

// --- --plan ------------------------------------------------------------------------

static async Task<int> Plan(Repository repository, string releasesPath, string? since, string? fallback, bool dryRun, bool landing, bool offline)
{
    string? outputFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
    string? summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    string verb = dryRun || landing ? "would be" : "will be";
    StringBuilder summary = new();
    List<string> problems = [];

    void Output(string key, string value)
    {
        Console.WriteLine($"plan {key}={value}");

        if (!string.IsNullOrEmpty(outputFile))
        {
            File.AppendAllText(outputFile, $"{key}={value}\n");
        }
    }

    void Summary()
    {
        if (!string.IsNullOrEmpty(summaryFile))
        {
            File.AppendAllText(summaryFile, summary.ToString());
        }
    }

    string title = dryRun || landing ? "Release plan (dry run: nothing is created)" : "Release plan";
    summary.Append(CultureInfo.InvariantCulture, $"### {title}\n\n");

    if (since is not null && !dryRun && !landing && repository.Exists(since)
        && repository.Changes(since, "HEAD", repository.Relative(releasesPath)).Count == 0)
    {
        Console.WriteLine($"ok  nothing under {repository.Relative(releasesPath)} changed since {since[..Math.Min(12, since.Length)]}; nothing to cut");
        summary.Append("Nothing under `releases/` changed in this push; nothing to cut.\n");
        Summary();
        Output("count", "0");
        return 0;
    }

    List<Descriptor> descriptors = ReleaseFormat.ReadDirectory(repository.Relative(releasesPath), problems);
    HashSet<string> tags = repository.Tags();

    foreach (Descriptor descriptor in descriptors)
    {
        ReleaseFormat.CheckShape(descriptor, problems);
    }

    List<Descriptor> pending = [.. descriptors.Where(descriptor => descriptor.Version is not null && !tags.Contains(descriptor.Version))];

    foreach (Descriptor cut in descriptors.Where(descriptor => descriptor.Version is not null && tags.Contains(descriptor.Version)))
    {
        Console.WriteLine($"    cut already  {cut.Version}");
    }

    string source = repository.Relative(releasesPath);

    if (pending.Count == 0 && fallback is not null && dryRun)
    {
        List<Descriptor> fallbackDescriptors = ReleaseFormat.ReadDirectory(repository.Relative(fallback), problems);

        foreach (Descriptor descriptor in fallbackDescriptors)
        {
            ReleaseFormat.CheckShape(descriptor, problems);
        }

        pending = fallbackDescriptors;
        source = repository.Relative(fallback);
        summary.Append(CultureInfo.InvariantCulture, $"`releases/` has nothing pending, so the dry run plans `{source}` instead.\n\n");
    }

    if (pending.Count == 0)
    {
        Report(problems, $"{descriptors.Count} descriptor(s), none pending", Console.Out);
        summary.Append(problems.Count == 0 ? "No pending release: every descriptor has its tag.\n" : "Descriptors are malformed; see the log.\n");
        Summary();
        Output("count", "0");
        return problems.Count == 0 ? 0 : 1;
    }

    if (pending.Count > 1)
    {
        problems.Add($"{pending.Count} descriptors are pending ({string.Join(", ", pending.Select(d => d.Version))}); one release at a time, oldest first");
        Report(problems, "more than one pending", Console.Out);
        summary.Append("More than one descriptor is pending; one release at a time. Nothing is cut.\n");
        Summary();
        Output("count", "0");
        return 1;
    }

    Descriptor release = pending[0];
    Console.WriteLine($"    PENDING      {release.Version} ({source})");

    Resolution? resolution = problems.Count == 0 ? Resolve(repository, release, tags, problems, landing) : null;

    // The project's status, at the commit under release: the README that
    // ships in the packages names this release, and the roadmap marks what it
    // closes (eng/status.cs).
    if (resolution is not null)
    {
        List<string> statusProblems = [];
        ProjectStatus.Check(repository.TreeAt(resolution.Sha), statusProblems);
        problems.AddRange(statusProblems.Select(problem => $"status at {resolution.Sha[..12]}: {problem}"));
    }

    // The online checks.
    List<string> notes = [];

    if (resolution is not null)
    {
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        string? name = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        if (offline)
        {
            notes.Add("offline: the issue's existence and the head guard were **not** checked");
        }
        else if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("release: --plan reads the issue and the head's check runs; it needs GITHUB_TOKEN and GITHUB_REPOSITORY (or --offline, for a local look).");
            return 2;
        }
        else
        {
            Api api = new(token, name);
            await CheckIssuesExist(api, resolution.Issues, release, problems);

            if (await HeadGuard(api, repository, resolution.Sha) is string guard)
            {
                problems.Add($"{release.Path}: {guard}");
            }
            else
            {
                notes.Add($"head guard: `{resolution.Sha[..12]}` carries the gates App's successful `agent review`");
            }
        }
    }

    int result = Report(problems, $"{release.Version} from {source}", Console.Out);

    summary.Append("| | |\n|---|---|\n");
    summary.Append(CultureInfo.InvariantCulture, $"| Version | `{release.Version}`{(release.Version is not null && ReleaseFormat.IsPrerelease(release.Version) ? " (prerelease)" : "")} |\n");
    summary.Append(CultureInfo.InvariantCulture, $"| Descriptor | `{repository.Relative(release.Path)}` |\n");

    if (resolution is not null)
    {
        summary.Append(CultureInfo.InvariantCulture, $"| Commit under release | `{resolution.Sha}`{(release.Commit is null ? "" : " (retro-cut)")} |\n");
        summary.Append(CultureInfo.InvariantCulture, $"| Previous release | {(resolution.Previous is null ? "none" : $"`{resolution.Previous}`")} |\n");
        summary.Append(CultureInfo.InvariantCulture, $"| Tag message | {ReleaseFormat.TagMessage(release)} |\n");
        summary.Append(CultureInfo.InvariantCulture, $"| Basis | {string.Join(", ", resolution.Issues.Select(issue => $"#{issue}"))}; {release.Basis.Count(line => line.Kind == "adr")} ADR(s); storage format {release.Basis.FirstOrDefault(line => line.Kind == "storage-format")?.Value} |\n");
        summary.Append(CultureInfo.InvariantCulture, $"| Accepted by | {release.AcceptedBy} |\n");
    }

    summary.Append('\n');

    foreach (string note in notes)
    {
        summary.Append(CultureInfo.InvariantCulture, $"- {note}\n");
    }

    if (problems.Count > 0)
    {
        summary.Append(CultureInfo.InvariantCulture, $"\n**Not cut: {problems.Count} problem(s).** No tag is created.\n\n");

        foreach (string problem in problems)
        {
            summary.Append(CultureInfo.InvariantCulture, $"- {problem}\n");
        }

        Summary();

        // A real cut stops here. A dry run goes on to the gates and the
        // tagging identity, so that one run shows everything that stands in
        // the way; its last step fails on `problems` (release.yml).
        if (!(dryRun || landing) || resolution is null)
        {
            Output("count", "0");
            return result;
        }
    }
    else
    {
        summary.Append(CultureInfo.InvariantCulture, $"\nThe descriptor is valid; `{release.Version}` {verb} tagged on `{resolution!.Sha[..12]}` once every gate passes at that commit.\n");
        Summary();
    }

    Output("problems", problems.Count.ToString(CultureInfo.InvariantCulture));
    Output("count", "1");
    Output("version", release.Version!);
    Output("sha", resolution.Sha);
    Output("message", ReleaseFormat.TagMessage(release));
    Output("prerelease", ReleaseFormat.IsPrerelease(release.Version!) ? "true" : "false");
    Output("previous", resolution.Previous ?? "");
    Output("issues", string.Join(' ', resolution.Issues));
    return 0;
}

// --- --draft -------------------------------------------------------------------------

static int Draft(Repository repository, string requested)
{
    string version = requested.StartsWith('v') ? requested : "v" + requested;

    if (!ReleaseFormat.Version.IsMatch(version))
    {
        Console.Error.WriteLine($"release: '{requested}' is not v<SemVer 2.0.0>.");
        return 2;
    }

    string sha = repository.ResolveCommit("HEAD")!;
    string? previous = repository.PreviousTag(sha);
    Dictionary<string, string> adrs = repository.Adrs(sha);
    Dictionary<string, string> before = previous is null ? [] : repository.Adrs(previous);
    // By key alone: a superseded ruling moves to its new set under the same
    // key, and is not new (docs/decisions/README.md).
    HashSet<string> keysBefore = previous is null ? [] : [.. repository.LedgerKeys(previous).Select(KeyOf)];
    List<string> keys = [.. repository.LedgerKeys(sha).Where(key => !keysBefore.Contains(KeyOf(key))).Order(StringComparer.Ordinal)];

    StringBuilder draft = new();
    draft.Append("# A release descriptor (docs/releases.md). Save as releases/").Append(version).Append(".yaml.\n");
    draft.Append("format: 1\n");
    draft.Append(CultureInfo.InvariantCulture, $"version: {version}\n");
    draft.Append("title: <one line, no version>\n");
    draft.Append(CultureInfo.InvariantCulture, $"date: {DateTime.UtcNow:yyyy-MM-dd}\n");
    draft.Append("basis:\n");
    draft.Append("  - issue: <the milestone issue, closed by a commit in this release>\n");
    draft.Append(CultureInfo.InvariantCulture, $"  - storage-format: {repository.StorageFormat(sha)}\n");

    foreach (string number in adrs.Keys.Where(number => !before.ContainsKey(number)).Order(StringComparer.Ordinal))
    {
        draft.Append(CultureInfo.InvariantCulture, $"  - adr: \"{number}\"\n");
    }

    draft.Append(CultureInfo.InvariantCulture, $"# Ledger keys first shipped since {previous ?? "the first commit"} ({keys.Count}), for information; basis lists ADRs, not keys:\n");

    foreach (string key in keys)
    {
        draft.Append(CultureInfo.InvariantCulture, $"#   {key}\n");
    }

    draft.Append("accepted-by: mailto:<the human who approves the pull request>\n");
    draft.Append("summary: |\n  <the release notes, written: what a consumer of the packages needs to know>\n");
    Console.Write(draft.ToString());
    return 0;
}

static string KeyOf(string qualified) => qualified[(qualified.LastIndexOf('.') + 1)..];

// --- --notes -----------------------------------------------------------------------

static int Notes(Repository repository, string releasesPath, string requested, string? into)
{
    string version = requested.StartsWith('v') ? requested : "v" + requested;
    string path = Path.Combine(releasesPath, version + ".yaml");

    if (into is null)
    {
        Console.Error.WriteLine("release: --notes needs --into <dir>.");
        return 2;
    }

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"release: {version} has no descriptor at {repository.Relative(path)}; a tag is cut from one (ADR 0102).");
        return 1;
    }

    List<string> problems = [];
    Descriptor descriptor = ReleaseFormat.Parse(path, File.ReadAllText(path), problems);
    ReleaseFormat.CheckShape(descriptor, problems);
    string? projectUrl = ReleaseFormat.ReadProjectUrl(repository.Root);

    if (problems.Count > 0 || projectUrl is null)
    {
        problems.ForEach(problem => Console.Error.WriteLine($"  {problem}"));
        Console.Error.WriteLine("release: the descriptor is not valid, or there is no PackageProjectUrl.");
        return 1;
    }

    string? tagged = repository.ResolveCommit($"refs/tags/{version}");
    string? previous = tagged is null ? null : repository.PreviousTag(tagged + "^");

    Directory.CreateDirectory(into);
    File.WriteAllText(Path.Combine(into, "notes.md"), ReleaseFormat.Notes(descriptor, previous, projectUrl));
    File.WriteAllText(Path.Combine(into, "title.txt"), ReleaseFormat.TagMessage(descriptor));
    File.WriteAllText(Path.Combine(into, "prerelease.txt"), ReleaseFormat.IsPrerelease(version) ? "true" : "false");
    Console.WriteLine($"ok  {version}: notes, title and prerelease written to {into}");
    return 0;
}

// --- --fixtures ----------------------------------------------------------------------

// Each directory under the root is a case: releases/, optionally CHANGELOG.md,
// and expect.txt, one line per finding the check must report. Every case must
// fail, and say each of them.
static async Task<int> Fixtures(Repository repository, string root)
{
    List<string> failures = [];
    string[] cases = [.. Directory.GetDirectories(root).Where(directory => File.Exists(Path.Combine(directory, "expect.txt"))).Order(StringComparer.Ordinal)];

    foreach (string directory in cases)
    {
        string name = Path.GetFileName(directory);
        StringWriter output = new();
        int exit = await Check(repository, Path.Combine(directory, "releases"), Path.Combine(directory, "CHANGELOG.md"), baseRef: null, isFixture: true, output);
        string said = output.ToString();
        string[] expected = [.. File.ReadAllLines(Path.Combine(directory, "expect.txt")).Where(line => line.Trim().Length > 0 && !line.StartsWith('#'))];
        string[] missing = [.. expected.Where(line => !said.Contains(line.Trim(), StringComparison.Ordinal))];

        if (exit == 1 && missing.Length == 0)
        {
            Console.WriteLine($"ok   {name}: fails, saying {string.Join("; ", expected.Select(line => $"'{line.Trim()}'"))}");
        }
        else
        {
            failures.Add(name);
            Console.WriteLine($"FAIL {name}: exit {exit}{(missing.Length == 0 ? "" : $", without saying {string.Join("; ", missing.Select(line => $"'{line.Trim()}'"))}")}");
            Console.WriteLine(said);
        }
    }

    if (cases.Length == 0)
    {
        Console.Error.WriteLine($"release: no cases under {root}.");
        return 2;
    }

    Console.WriteLine(failures.Count == 0
        ? $"ok  every one of {cases.Length} failing release fixture(s) fails, for its own reason"
        : $"FAIL: {failures.Count} fixture(s) did not fail as expected: {string.Join(", ", failures)}");
    return failures.Count == 0 ? 0 : 1;
}

// --- helpers ---------------------------------------------------------------------------

// The base of a pull request or push, worked out as eng/issue-refs.cs does.
static string? ResolveBase(Repository repository)
{
    string? prBase = Environment.GetEnvironmentVariable("GITHUB_BASE_REF");

    if (!string.IsNullOrEmpty(prBase))
    {
        return $"origin/{prBase}";
    }

    string? before = Environment.GetEnvironmentVariable("GITHUB_EVENT_BEFORE");
    bool toMain = Environment.GetEnvironmentVariable("GITHUB_REF") == "refs/heads/main";

    if (toMain && !string.IsNullOrEmpty(before))
    {
        return before.Trim('0').Length > 0 && repository.Exists(before) ? before : null;
    }

    return repository.Exists("origin/main") ? "origin/main" : repository.Exists("main") ? "main" : null;
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

sealed record Resolution(string Sha, string? Previous, IReadOnlyList<int> Issues);

sealed record CommitInfo(string Sha, string AuthorEmail, string Message);

sealed class GitException(string message) : Exception(message);

// The clone, read through git. Every path is read at a commit, never from the
// working tree, so a retro-cut is judged by what it pins.
sealed class Repository(string root)
{
    public const string FormatVersionPath = "src/Varve.Store/Log/FormatVersion.cs";

    public string Root { get; } = root;

    public string Relative(string path) =>
        path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path[(Root.Length + 1)..].Replace('\\', '/') : path;

    public HashSet<string> Tags() =>
        [.. Run("tag", "--list", "v*").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public bool Exists(string reference) => Try("rev-parse", "--verify", "--quiet", reference + "^{commit}").Exit == 0;

    public string? ResolveCommit(string reference)
    {
        (int exit, string output) = Try("rev-parse", "--verify", "--quiet", reference + "^{commit}");
        return exit == 0 ? output.Trim() : null;
    }

    public bool IsAncestor(string ancestor, string descendant) => Try("merge-base", "--is-ancestor", ancestor, descendant).Exit == 0;

    // The newest v* tag reachable from the commit, by the history.
    public string? PreviousTag(string commit)
    {
        (int exit, string output) = Try("describe", "--tags", "--abbrev=0", "--match", "v*", commit);
        return exit == 0 ? output.Trim() : null;
    }

    public List<(string Status, string File)> Changes(string from, string to, string directory) =>
        [.. Run("diff", "--name-status", "--no-renames", $"{from}...{to}", "--", directory)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0].Trim(), parts[1].Trim()))];

    public List<CommitInfo> Commits(string range) =>
        [.. Run("log", "--no-merges", "--format=%x01%H%x02%ae%x02%B", range)
            .Split('\u0001', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Split('\u0002'))
            .Where(parts => parts.Length == 3)
            .Select(parts => new CommitInfo(parts[0].Trim(), parts[1].Trim(), parts[2]))];

    // The tree of a commit, for the status check.
    public StatusTree TreeAt(string commit) => new(
        path => Show(commit, path),
        directory => Try("ls-tree", "-r", "--name-only", commit, "--", directory) is (0, string listed)
            ? listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : []);

    public string? Show(string commit, string path)
    {
        (int exit, string output) = Try("show", $"{commit}:{path}");
        return exit == 0 ? output : null;
    }

    // ADR number to file name, at a commit.
    public Dictionary<string, string> Adrs(string commit)
    {
        Dictionary<string, string> adrs = new(StringComparer.Ordinal);

        foreach (string file in Run("ls-tree", "--name-only", $"{commit}:docs/adr").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = Regex.Match(file, @"^(?<number>\d{4})-.+\.md$", RegexOptions.CultureInvariant);

            if (match.Success)
            {
                adrs[match.Groups["number"].Value] = file;
            }
        }

        return adrs;
    }

    // Accepted: the first line of its Status section says so (ADR 0001's form).
    public bool IsAccepted(string commit, string file)
    {
        string? text = Show(commit, $"docs/adr/{file}");

        if (text is null)
        {
            return false;
        }

        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int status = Array.FindIndex(lines, line => line.Trim() == "## Status");

        if (status < 0)
        {
            return false;
        }

        string? first = lines.Skip(status + 1).FirstOrDefault(line => line.Trim().Length > 0);
        return first is not null && Regex.IsMatch(first, @"^\**Accepted\b", RegexOptions.CultureInvariant);
    }

    public int? StorageFormat(string commit)
    {
        string? text = Show(commit, FormatVersionPath);
        Match match = text is null ? Match.Empty : Regex.Match(text, @"\bCurrent\s*=>\s*new\((?<value>\d+)\)", RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture) : null;
    }

    // Every ledger key at a commit, as <set>.<Key>; information for --draft.
    public HashSet<string> LedgerKeys(string commit)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (string file in Run("ls-tree", "--name-only", $"{commit}:docs/decisions").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!file.EndsWith(".md", StringComparison.Ordinal) || file == "README.md")
            {
                continue;
            }

            string text = Show(commit, $"docs/decisions/{file}") ?? "";
            string set = Regex.Match(text, @"(?m)^set:\s*(?<set>\S+)", RegexOptions.CultureInvariant).Groups["set"].Value;

            foreach (Match key in Regex.Matches(text, @"(?m)^\s+-\s+key:\s*(?<key>\w+)", RegexOptions.CultureInvariant))
            {
                keys.Add($"{set}.{key.Groups["key"].Value}");
            }
        }

        return keys;
    }

    // The integration id ruleset 1 pins a required check to, as eng/agent-review.cs reads it.
    public long? PinnedIntegration(string name)
    {
        string path = Path.Combine(Root, ".github", "repo-standard.yaml");
        string[] lines = File.Exists(path) ? File.ReadAllLines(path) : [];

        for (int i = 0; i < lines.Length; i++)
        {
            Match context = Regex.Match(lines[i], @"^\s*-\s*context:\s*(?<name>.+?)\s*$", RegexOptions.CultureInvariant);

            if (!context.Success || context.Groups["name"].Value.Trim('"', '\'') != name)
            {
                continue;
            }

            for (int j = i + 1; j < lines.Length && !Regex.IsMatch(lines[j], @"^\s*-\s", RegexOptions.CultureInvariant); j++)
            {
                Match id = Regex.Match(lines[j], @"^\s*integration_id:\s*(?<id>\d+)\s*$", RegexOptions.CultureInvariant);

                if (id.Success)
                {
                    return long.Parse(id.Groups["id"].Value, CultureInfo.InvariantCulture);
                }
            }
        }

        return null;
    }

    private string Run(params string[] arguments)
    {
        (int exit, string output) = Try(arguments);
        return exit == 0 ? output : throw new GitException($"git {string.Join(' ', arguments)} failed; a release needs the full history and the tags (fetch-depth: 0)");
    }

    private (int Exit, string Output) Try(params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new GitException("could not start git");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = error.Result;
        return (process.ExitCode, output);
    }
}

// The repository's REST API, read as data.
sealed class Api
{
    private readonly HttpClient client = new();
    private readonly string root;

    public Api(string token, string repository)
    {
        string api = Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com";
        root = $"{api.TrimEnd('/')}/repos/{repository}/";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-release", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<JsonDocument?> TryGet(string path)
    {
        using HttpResponseMessage response = await client.GetAsync(root + path);
        return response.IsSuccessStatusCode ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()) : null;
    }
}
