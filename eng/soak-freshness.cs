// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Windows soak, fresh (ADR 0215 point 6).
//
//   dotnet run eng/soak-freshness.cs -- --days 8
//   dotnet run eng/soak-freshness.cs -- --fixtures tests/fixtures/soak-freshness
//
// A release refuses when the latest Windows soak on main's nightly is red, or
// older than --days. This reads nightly.yml's runs on main through the
// Actions API with the job's own token (GITHUB_TOKEN, actions: read), newest
// first, and judges the first completed job named "soak (windows-latest)" it
// finds. It names the run it judged.
//
// --fixtures judges every case under the directory, each a JSON file with
// "now" and the "jobs" the API would give, newest first: a case named
// pass-*.json must pass and fail-*.json must fail
// (tests/fixtures/soak-freshness/README.md).
//
// Exit codes: 0 fresh and green, 1 refused, 2 could not run.
//
// See docs/adr/0215-the-soak-gate-run-and-judged.md.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

const string JobName = "soak (windows-latest)";
int days = 8;
string? fixtures = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--days":
            days = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--fixtures":
            fixtures = args[++i];
            break;
        case "--repository":
            // The repository is GITHUB_REPOSITORY's; accepted for the caller's clarity.
            i++;
            break;
        default:
            Console.Error.WriteLine("soak-freshness: unknown argument " + args[i]);
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
        DateTimeOffset now = DateTimeOffset.Parse(fixture.RootElement.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        List<Job> jobs = [.. fixture.RootElement.GetProperty("jobs").EnumerateArray().Select(Job.Of)];
        (bool fresh, string said) = Judge(jobs, now, days);
        bool expected = Path.GetFileName(file).StartsWith("pass-", StringComparison.Ordinal);
        wrong += fresh == expected ? 0 : 1;
        Console.WriteLine((fresh == expected ? "ok   " : "FAIL ") + Path.GetFileName(file) + ": " + said);
    }

    Console.WriteLine(cases.Length + " cases, " + wrong + " judged wrong");
    return cases.Length > 0 && wrong == 0 ? 0 : 1;
}

string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
string? repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repository))
{
    Console.Error.WriteLine("soak-freshness: GITHUB_TOKEN and GITHUB_REPOSITORY must be set; the job passes github.token.");
    return 2;
}

string api = (Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com").TrimEnd('/') + "/repos/" + repository + "/";
using HttpClient client = new();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("varve-soak-freshness", "1"));
client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

List<Job> found = [];

try
{
    using JsonDocument runs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/workflows/nightly.yml/runs?branch=main&per_page=30"));

    foreach (JsonElement run in runs.RootElement.GetProperty("workflow_runs").EnumerateArray())
    {
        long id = run.GetProperty("id").GetInt64();
        using JsonDocument jobs = JsonDocument.Parse(await client.GetStringAsync(api + "actions/runs/" + id.ToString(CultureInfo.InvariantCulture) + "/jobs?per_page=100"));
        found.AddRange(jobs.RootElement.GetProperty("jobs").EnumerateArray().Select(Job.Of).Where(j => j.Name.EndsWith(JobName, StringComparison.Ordinal)));

        if (found.Any(j => j.Completed is not null))
        {
            break;
        }
    }
}
catch (HttpRequestException error)
{
    Console.Error.WriteLine("soak-freshness: the Actions API could not be read: " + error.Message);
    return 2;
}

(bool ok, string verdict) = Judge(found, DateTimeOffset.UtcNow, days);
Console.WriteLine(verdict);
return ok ? 0 : 1;

// The newest completed Windows soak: green and no older than `days`.
static (bool Fresh, string Said) Judge(List<Job> jobs, DateTimeOffset now, int days)
{
    Job? latest = jobs.Where(j => j.Completed is not null).OrderByDescending(j => j.Completed).FirstOrDefault();

    if (latest is null)
    {
        return (false, "refused: no completed Windows soak on main's nightly");
    }

    double age = (now - latest.Completed!.Value).TotalDays;
    string which = latest.Url + ", " + latest.Conclusion + ", finished " + latest.Completed.Value.ToString("u", CultureInfo.InvariantCulture)
        + " (" + age.ToString("F1", CultureInfo.InvariantCulture) + " days ago)";

    return latest.Conclusion != "success"
        ? (false, "refused: the latest Windows soak is not green: " + which)
        : age > days
            ? (false, "refused: the latest Windows soak is older than " + days + " days: " + which)
            : (true, "fresh: " + which);
}

sealed record Job(string Name, string? Conclusion, DateTimeOffset? Completed, string Url)
{
    public static Job Of(JsonElement job) => new(
        job.GetProperty("name").GetString() ?? "",
        job.TryGetProperty("conclusion", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
        job.TryGetProperty("completed_at", out JsonElement at) && at.ValueKind == JsonValueKind.String ? DateTimeOffset.Parse(at.GetString()!, CultureInfo.InvariantCulture) : null,
        job.TryGetProperty("html_url", out JsonElement url) && url.ValueKind == JsonValueKind.String ? url.GetString()! : "(no url)");
}
