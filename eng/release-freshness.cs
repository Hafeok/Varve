// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The release gate's evidence from main, fresh (ADR 0215 point 6, amended
// 2026-11-02).
//
//   dotnet run eng/release-freshness.cs -- --mode --version v0.1.0-preview.17
//   dotnet run eng/release-freshness.cs -- --version v0.1.0-preview.17
//   dotnet run eng/release-freshness.cs -- --fixtures tests/fixtures/release-freshness
//
// --mode says how the release gate treats a version. A preview,
// 0.x.y-preview.N, does not run the long gates inline: it checks that main's
// latest runs of them are green and fresh. Any other version, 1.0.0 and every
// later release among them, runs the faults and the Linux soak inline on the
// release commit, as before. An empty version is a caller that is not a
// release (the nightly's Windows soak): it runs what its inputs say. Writes
// inline=true or inline=false to GITHUB_OUTPUT when that is set.
//
// Without --mode it judges the checks the version needs. Every release needs
// the Windows soak, which never runs inline. A preview also needs the Linux
// soak and the store's full-depth suites, which hold the file backend's
// faults at every point, on both runners. Each check is the latest completed
// job on main's nightly whose name ends with the check's job, green and no
// older than its window. This reads nightly.yml's runs on main through the
// Actions API with the job's own token (GITHUB_TOKEN, actions: read), newest
// first, until every check has a job or the runs run out, and names each job
// it judged. A skipped job is not a run and is passed over.
//
// --fixtures judges every case under the directory, each a JSON file with
// "version", "now", "inline" (what --mode must say) and the "jobs" the API
// would give, newest first: a case named pass-*.json must pass and
// fail-*.json must fail (tests/fixtures/release-freshness/README.md).
//
// Exit codes: 0 fresh and green, 1 refused, 2 could not run.
//
// See docs/adr/0215-the-soak-gate-run-and-judged.md.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

string version = "";
bool mode = false;
string? fixtures = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--version":
            version = args[++i];
            break;
        case "--mode":
            mode = true;
            break;
        case "--fixtures":
            fixtures = args[++i];
            break;
        case "--repository":
            // The repository is GITHUB_REPOSITORY's; accepted for the caller's clarity.
            i++;
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
        string caseVersion = fixture.RootElement.GetProperty("version").GetString()!;
        DateTimeOffset now = DateTimeOffset.Parse(fixture.RootElement.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        List<Job> jobs = [.. fixture.RootElement.GetProperty("jobs").EnumerateArray().Select(Job.Of)];
        bool inlineExpected = fixture.RootElement.GetProperty("inline").GetBoolean();
        (bool fresh, List<string> said) = JudgeAll(Gate.ChecksFor(caseVersion), jobs, now);
        bool expected = Path.GetFileName(file).StartsWith("pass-", StringComparison.Ordinal);
        bool right = fresh == expected && Gate.Inline(caseVersion) == inlineExpected;
        wrong += right ? 0 : 1;
        Console.WriteLine((right ? "ok   " : "FAIL ") + Path.GetFileName(file) + ": " + caseVersion + " " + (Gate.Inline(caseVersion) ? "inline" : "fresh from main") + "; " + string.Join("; ", said));
    }

    Console.WriteLine(cases.Length + " cases, " + wrong + " judged wrong");
    return cases.Length > 0 && wrong == 0 ? 0 : 1;
}

if (mode)
{
    bool inline = Gate.Inline(version);
    Console.WriteLine((version.Length == 0 ? "no release" : version) + ": " + (inline ? "the long gates run inline" : "a preview: the long gates' latest runs on main must be green and fresh"));
    string? output = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");

    if (!string.IsNullOrEmpty(output))
    {
        File.AppendAllText(output, "inline=" + (inline ? "true" : "false") + "\n");
    }

    return 0;
}

string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
string? repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repository))
{
    Console.Error.WriteLine("release-freshness: GITHUB_TOKEN and GITHUB_REPOSITORY must be set; the job passes github.token.");
    return 2;
}

IReadOnlyList<Check> checks = Gate.ChecksFor(version);
string api = (Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com").TrimEnd('/') + "/repos/" + repository + "/";
using HttpClient client = new();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-release-freshness", "1"));
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

List<Job> found = [];

try
{
    using JsonDocument runs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/workflows/nightly.yml/runs?branch=main&per_page=50"));

    foreach (JsonElement run in runs.RootElement.GetProperty("workflow_runs").EnumerateArray())
    {
        long id = run.GetProperty("id").GetInt64();
        using JsonDocument jobs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/runs/" + id.ToString(CultureInfo.InvariantCulture) + "/jobs?per_page=100"));
        found.AddRange(jobs.RootElement.GetProperty("jobs").EnumerateArray().Select(Job.Of).Where(j => checks.Any(c => c.Matches(j))));

        if (checks.All(c => found.Any(j => c.Matches(j) && j.Completed is not null)))
        {
            break;
        }
    }
}
catch (HttpRequestException error)
{
    Console.Error.WriteLine("release-freshness: the Actions API could not be read: " + error.Message);
    return 2;
}

(bool ok, List<string> verdicts) = JudgeAll(checks, found, DateTimeOffset.UtcNow);

foreach (string verdict in verdicts)
{
    Console.WriteLine(verdict);
}

return ok ? 0 : 1;

// Every check the version needs, each on the newest completed job of its own.
static (bool Fresh, List<string> Said) JudgeAll(IReadOnlyList<Check> checks, List<Job> jobs, DateTimeOffset now)
{
    List<string> said = [];
    bool fresh = true;

    foreach (Check check in checks)
    {
        (bool one, string verdict) = Judge(check, jobs, now);
        fresh &= one;
        said.Add(verdict);
    }

    return (fresh, said);
}

// The newest completed job of a check: green and no older than its window.
static (bool Fresh, string Said) Judge(Check check, List<Job> jobs, DateTimeOffset now)
{
    Job? latest = jobs.Where(j => check.Matches(j) && j.Completed is not null).OrderByDescending(j => j.Completed).FirstOrDefault();

    if (latest is null)
    {
        return (false, "refused: no completed " + check.Title + " on main's nightly");
    }

    double age = (now - latest.Completed!.Value).TotalDays;
    string which = latest.Url + ", " + latest.Conclusion + ", finished " + latest.Completed.Value.ToString("u", CultureInfo.InvariantCulture)
        + " (" + age.ToString("F1", CultureInfo.InvariantCulture) + " days ago)";

    return latest.Conclusion != "success"
        ? (false, "refused: the latest " + check.Title + " is not green: " + which)
        : age > check.Days
            ? (false, "refused: the latest " + check.Title + " is older than " + check.Days + " days: " + which)
            : (true, "fresh: " + check.Title + ": " + which);
}

static partial class Gate
{
    // ADR 0215 point 6, amended 2026-11-02: eight days for a soak, which
    // runs weekly at the least; three for the faults, which run every night,
    // so that two nights that did not run still leave a release possible
    // and a third does not.
    private static readonly Check WindowsSoak = new("Windows soak", "soak (windows-latest)", 8);
    private static readonly Check LinuxSoak = new("Linux soak", "soak (ubuntu-latest)", 8);
    private static readonly Check LinuxFaults = new("store at full depth on Linux, the faults at every point", "store, full depth (ubuntu-latest)", 3);
    private static readonly Check WindowsFaults = new("store at full depth on Windows, the faults at every point", "store, full depth (windows-latest)", 3);

    // A preview: 0.x.y-preview.N, with or without the tag's v.
    [GeneratedRegex(@"^v?0\.\d+\.\d+-preview\.\d+$")]
    private static partial Regex Preview();

    // The long gates run inline unless the version is a preview.
    public static bool Inline(string version) => !Preview().IsMatch(version);

    public static IReadOnlyList<Check> ChecksFor(string version) =>
        Inline(version) ? [WindowsSoak] : [WindowsSoak, LinuxSoak, LinuxFaults, WindowsFaults];
}

sealed record Check(string Title, string JobName, int Days)
{
    // A job of the nightly, named directly or through the reusable workflow
    // ("linux soak / soak (ubuntu-latest)"); a skipped job is not a run.
    public bool Matches(Job job) => job.Conclusion != "skipped" && job.Name.EndsWith(JobName, StringComparison.Ordinal);
}

sealed record Job(string Name, string? Conclusion, DateTimeOffset? Completed, string Url)
{
    public static Job Of(JsonElement job) => new(
        job.GetProperty("name").GetString() ?? "",
        job.TryGetProperty("conclusion", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
        job.TryGetProperty("completed_at", out JsonElement at) && at.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(at.GetString()!, CultureInfo.InvariantCulture) : null,
        job.TryGetProperty("html_url", out JsonElement url) && url.ValueKind == JsonValueKind.String ? url.GetString()! : "(no url)");
}
