// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The release gate's evidence from main (ADR 0215 point 6, amended
// 2026-11-02 and 2026-11-03).
//
//   dotnet run eng/release-freshness.cs -- --mode --version v0.1.0-preview.17
//   dotnet run eng/release-freshness.cs -- --repository o/r --version v0.1.0-preview.17
//   dotnet run eng/release-freshness.cs -- --repository o/r --evidence
//   dotnet run eng/release-freshness.cs -- --fixtures tests/fixtures/release-freshness
//
// --mode says how the release gate treats a version. A preview,
// 0.x.y-preview.N, does not run the long gates inline: it checks main's latest
// runs of them. Any other version, 1.0.0 and every later release among them,
// runs the faults and the Linux soak inline on the release commit, as before.
// An empty version is a caller that is not a release (the nightly's soaks):
// it runs what its inputs say. Writes inline=true or inline=false to
// GITHUB_OUTPUT when that is set.
//
// Without --mode it judges the checks the version needs, on main's nightly.
// A preview is gated on the faults alone: the store's full-depth job on each
// runner, the newest completed one judged by the job's own conclusion and
// never its run's, green and at most three days old. The soaks inform a
// preview and never refuse it (the amendment of 2026-11-03). Any other
// release is refused when the Windows soak, which never runs inline, is red
// or older than eight days, or while an open issue is labelled soak, which
// the nightly opens on a red or invalid soak. A skipped job is not a run.
//
// A soak's verdict is read from its job: green when the job succeeded, red
// when its step "The soak misses the gate" failed (the judge exited 1; in a
// job from before that step, when "Judge the soak" failed),
// invalid for any other completed job that did not succeed (not judged,
// crashed, cancelled, timed out), none when main has no completed soak. A
// store job's is its conclusion: green, red for a failure, cancelled for any
// other end, none.
//
// --evidence prints, as Markdown for the GitHub Release's notes, main's
// latest soak on each runner and store job at full depth on each, with their
// verdicts and links, and the open soak issues; publish.yml appends it to the
// notes when it creates the release. The judging mode prints it too, and
// writes it to GITHUB_STEP_SUMMARY when that is set.
//
// Both read nightly.yml's runs on main through the Actions API with the job's
// own token (GITHUB_TOKEN; actions: read, and issues: read for the soak
// issues), newest first, until every check has a job or the runs run out.
// The repository is --repository's, or GITHUB_REPOSITORY's.
//
// --fixtures judges every case under the directory, each a JSON file with
// "version", "now", "inline" (what --mode must say), the "jobs" the API would
// give, newest first, each with optional "steps", optional "soak_issues", and
// optional "evidence", lines the printed evidence must contain: a case named
// pass-*.json must pass and fail-*.json must fail
// (tests/fixtures/release-freshness/README.md).
//
// Exit codes: 0 passes (or printed), 1 refused, 2 could not run.
//
// See docs/adr/0215-the-soak-gate-run-and-judged.md.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string version = "";
bool mode = false;
bool evidence = false;
string? fixtures = null;
string? repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--version" when i + 1 < args.Length:
            version = args[++i];
            break;
        case "--mode":
            mode = true;
            break;
        case "--evidence":
            evidence = true;
            break;
        case "--fixtures" when i + 1 < args.Length:
            fixtures = args[++i];
            break;
        case "--repository" when i + 1 < args.Length:
            repository = args[++i];
            break;
        default:
            Console.Error.WriteLine("release-freshness: unknown argument " + args[i]);
            return 2;
    }
}

if (fixtures is not null)
{
    int wrong = 0;
    string[] cases = [.. Directory.GetFiles(fixtures, "*.json").Order(StringComparer.Ordinal)];

    foreach (string file in cases)
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file));
        JsonElement root = fixture.RootElement;
        string caseVersion = root.GetProperty("version").GetString()!;
        DateTimeOffset now = DateTimeOffset.Parse(root.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        List<Job> jobs = [.. root.GetProperty("jobs").EnumerateArray().Select(Job.Of)];
        List<Issue> issues = root.TryGetProperty("soak_issues", out JsonElement open) ? [.. open.EnumerateArray().Select(Issue.Of)] : [];
        bool inlineExpected = root.GetProperty("inline").GetBoolean();
        (bool passes, List<string> said) = Gate.Judge(caseVersion, jobs, issues, now);
        string printed = Gate.Evidence(jobs, issues);
        string[] missing = root.TryGetProperty("evidence", out JsonElement lines)
            ? [.. lines.EnumerateArray().Select(line => line.GetString()!).Where(line => !printed.Contains(line, StringComparison.Ordinal))]
            : [];
        bool expected = Path.GetFileName(file).StartsWith("pass-", StringComparison.Ordinal);
        bool right = passes == expected && Gate.Inline(caseVersion) == inlineExpected && missing.Length == 0;
        wrong += right ? 0 : 1;
        Console.WriteLine((right ? "ok   " : "FAIL ") + Path.GetFileName(file) + ": " + caseVersion + " " + (Gate.Inline(caseVersion) ? "inline" : "a preview") + "; " + string.Join("; ", said));

        foreach (string line in missing)
        {
            Console.WriteLine("     the evidence does not say: " + line);
        }

        if (missing.Length > 0)
        {
            Console.WriteLine(printed);
        }
    }

    Console.WriteLine(cases.Length + " cases, " + wrong + " judged wrong");
    return cases.Length > 0 && wrong == 0 ? 0 : 1;
}

if (mode)
{
    bool inline = Gate.Inline(version);
    Console.WriteLine((version.Length == 0 ? "no release" : version) + ": " + (inline
        ? "the long gates run inline"
        : "a preview: main's latest faults must be green and fresh, and its soaks inform"));
    string? output = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");

    if (!string.IsNullOrEmpty(output))
    {
        File.AppendAllText(output, "inline=" + (inline ? "true" : "false") + "\n");
    }

    return 0;
}

string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repository))
{
    Console.Error.WriteLine("release-freshness: GITHUB_TOKEN must be set, and --repository or GITHUB_REPOSITORY; the job passes github.token.");
    return 2;
}

string api = (Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com").TrimEnd('/') + "/repos/" + repository + "/";
using HttpClient client = new();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-release-freshness", "1"));
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

List<Job> found = [];
List<Issue> soakIssues = [];

try
{
    using JsonDocument runs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/workflows/nightly.yml/runs?branch=main&per_page=50"));

    foreach (JsonElement run in runs.RootElement.GetProperty("workflow_runs").EnumerateArray())
    {
        long id = run.GetProperty("id").GetInt64();
        using JsonDocument jobs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/runs/" + id.ToString(CultureInfo.InvariantCulture) + "/jobs?per_page=100"));
        found.AddRange(jobs.RootElement.GetProperty("jobs").EnumerateArray().Select(Job.Of).Where(j => Gate.All.Any(c => c.Matches(j))));

        if (Gate.All.All(c => found.Any(j => c.Matches(j) && j.Completed is not null)))
        {
            break;
        }
    }

    // An issue, not a pull request: the issues API returns both.
    using JsonDocument issues = JsonDocument.Parse(await client.GetStringAsync(api + "issues?labels=soak&state=open&per_page=100"));
    soakIssues.AddRange(issues.RootElement.EnumerateArray().Where(issue => !issue.TryGetProperty("pull_request", out _)).Select(Issue.Of));
}
catch (HttpRequestException error)
{
    Console.Error.WriteLine("release-freshness: the GitHub API could not be read: " + error.Message);
    return 2;
}

string markdown = Gate.Evidence(found, soakIssues);

if (evidence)
{
    Console.Write(markdown);
    return 0;
}

(bool ok, List<string> verdicts) = Gate.Judge(version, found, soakIssues, DateTimeOffset.UtcNow);

foreach (string verdict in verdicts)
{
    Console.WriteLine(verdict);
}

string? summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

if (!string.IsNullOrEmpty(summary))
{
    File.AppendAllText(summary, markdown + "\n" + string.Join("\n", verdicts.Select(verdict => "- " + verdict)) + "\n");
}

return ok ? 0 : 1;

static partial class Gate
{
    // ADR 0215 point 6, amended 2026-11-02: eight days for the Windows soak,
    // which runs weekly; three for the faults, which run every night, so that
    // two nights that did not run still leave a release possible and a third
    // does not. The Linux soak's eight days are for the evidence's reader: it
    // informs and refuses nothing (the amendment of 2026-11-03).
    public static readonly Check LinuxSoak = new("linux-soak", "the Linux soak", "soak (ubuntu-latest)", 8, Soak: true);
    public static readonly Check WindowsSoak = new("windows-soak", "the Windows soak", "soak (windows-latest)", 8, Soak: true);
    public static readonly Check LinuxFaults = new("store-linux", "the store at full depth on Linux, the faults at every point", "store, full depth (ubuntu-latest)", 3, Soak: false);
    public static readonly Check WindowsFaults = new("store-windows", "the store at full depth on Windows, the faults at every point", "store, full depth (windows-latest)", 3, Soak: false);

    public static readonly IReadOnlyList<Check> All = [LinuxSoak, WindowsSoak, LinuxFaults, WindowsFaults];

    // The step of release-gate.yml's soak job that fails when the judge
    // misses: its failure, and only its failure, is a red soak.
    public const string MissesTheGate = "The soak misses the gate";

    // Before the amendment of 2026-11-03 the judge failed its own step, so a
    // soak job with no MissesTheGate step is red when that step failed.
    public const string JudgeTheSoak = "Judge the soak";

    // A preview: 0.x.y-preview.N, with or without the tag's v.
    [GeneratedRegex(@"^v?0\.\d+\.\d+-preview\.\d+$")]
    private static partial Regex Preview();

    // The long gates run inline unless the version is a preview.
    public static bool Inline(string version) => !Preview().IsMatch(version);

    // What the version is refused on, and what only informs it.
    public static (bool Passes, List<string> Said) Judge(string version, List<Job> jobs, List<Issue> issues, DateTimeOffset now)
    {
        List<string> said = [];
        bool passes = true;
        bool inline = Inline(version);

        foreach (Check check in inline ? [WindowsSoak] : (Check[])[LinuxFaults, WindowsFaults])
        {
            (bool fresh, string verdict) = Fresh(check, jobs, now);
            passes &= fresh;
            said.Add((fresh ? "fresh: " : "refused: ") + verdict);
        }

        if (!inline)
        {
            foreach (Check check in (Check[])[LinuxSoak, WindowsSoak])
            {
                said.Add("inform: " + Fresh(check, jobs, now).Said);
            }
        }

        foreach (Issue issue in issues)
        {
            said.Add((inline ? "refused: " : "inform: ") + "the soak issue #" + issue.Number.ToString(CultureInfo.InvariantCulture) + " is open: " + issue.Url);
        }

        passes &= !inline || issues.Count == 0;

        if (inline && issues.Count == 0)
        {
            said.Add("fresh: no open issue is labelled soak");
        }

        return (passes, said);
    }

    // The newest completed job of a check: green and no older than its window.
    private static (bool Fresh, string Said) Fresh(Check check, List<Job> jobs, DateTimeOffset now)
    {
        Job? latest = check.Latest(jobs);

        if (latest is null)
        {
            return (false, "no completed run of " + check.Title + " on main's nightly");
        }

        double age = (now - latest.Completed!.Value).TotalDays;
        string which = latest.Url + ", " + check.Verdict(latest) + " (" + latest.Conclusion + "), finished "
            + latest.Completed.Value.ToString("u", CultureInfo.InvariantCulture) + " (" + age.ToString("F1", CultureInfo.InvariantCulture) + " days ago)";

        return latest.Conclusion != "success"
            ? (false, "the latest run of " + check.Title + " is not green: " + which)
            : age > check.Days
                ? (false, "the latest run of " + check.Title + " is older than " + check.Days.ToString(CultureInfo.InvariantCulture) + " days: " + which)
                : (true, check.Title + ": " + which);
    }

    // Main's latest runs as Markdown, for the GitHub Release's notes and the
    // step summary: one line each, whatever its verdict or age.
    public static string Evidence(List<Job> jobs, List<Issue> issues)
    {
        StringBuilder markdown = new();
        markdown.Append("### Evidence on main at the release\n\n");
        markdown.Append("The latest completed runs on `main`'s nightly when this release was published. ");
        markdown.Append("A preview is gated on the faults and informed by the soaks; a later release runs both inline and is refused while a soak issue is open (ADR 0215, amended 2026-11-03).\n\n");

        foreach (Check check in All)
        {
            Job? latest = check.Latest(jobs);
            markdown.Append(latest is null
                ? "- " + check.Heading + ": **none**, no completed run on `main`\n"
                : "- " + check.Heading + ": **" + check.Verdict(latest) + "**, [the job](" + latest.Url + "), finished "
                    + latest.Completed!.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n");
        }

        markdown.Append(issues.Count == 0
            ? "- Open soak issues: **none**\n"
            : "- Open soak issues: " + string.Join(", ", issues.Select(issue => "[#" + issue.Number.ToString(CultureInfo.InvariantCulture) + "](" + issue.Url + ")")) + "\n");
        return markdown.ToString();
    }
}

sealed record Check(string Kind, string Title, string JobName, int Days, bool Soak)
{
    // A job of the nightly, named directly or through the reusable workflow
    // ("linux soak / soak (ubuntu-latest)"); a skipped job is not a run.
    public string Heading => char.ToUpperInvariant(Title[0]) + Title[1..];

    public bool Matches(Job job) => job.Conclusion != "skipped" && job.Name.EndsWith(JobName, StringComparison.Ordinal);

    public Job? Latest(List<Job> jobs) => jobs.Where(j => Matches(j) && j.Completed is not null).OrderByDescending(j => j.Completed).FirstOrDefault();

    // The verdict of a completed job of this check, by the job's own
    // conclusion, and for a soak by its steps.
    private static bool Failed(Job job, string step) => job.Steps.Any(s => s.Name == step && s.Conclusion == "failure");

    public string Verdict(Job job) =>
        job.Conclusion == "success" ? "green"
        : Soak ? (Failed(job, Gate.MissesTheGate) || (job.Steps.All(step => step.Name != Gate.MissesTheGate) && Failed(job, Gate.JudgeTheSoak)) ? "red" : "invalid")
        : job.Conclusion == "failure" ? "red"
        : "cancelled";
}

sealed record Step(string Name, string? Conclusion);

sealed record Job(string Name, string? Conclusion, DateTimeOffset? Completed, string Url, IReadOnlyList<Step> Steps)
{
    public static Job Of(JsonElement job) => new(
        job.GetProperty("name").GetString() ?? "",
        Text(job, "conclusion"),
        Text(job, "completed_at") is string at ? DateTimeOffset.Parse(at, CultureInfo.InvariantCulture) : null,
        Text(job, "html_url") ?? "(no url)",
        job.TryGetProperty("steps", out JsonElement steps) && steps.ValueKind == JsonValueKind.Array
            ? [.. steps.EnumerateArray().Select(step => new Step(Text(step, "name") ?? "", Text(step, "conclusion")))]
            : []);

    public static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

sealed record Issue(long Number, string Url)
{
    public static Issue Of(JsonElement issue) => new(issue.GetProperty("number").GetInt64(), Job.Text(issue, "html_url") ?? "(no url)");
}
