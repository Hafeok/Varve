// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The SDK pin gate (ADR 0224 point 5, amended 2026-11-11; #276).
//
//   dotnet run eng/sdk-pin.cs -- [--root <dir>]
//
// CI installs .NET in one place, .github/actions/setup-dotnet: exactly the
// SDK global.json pins, with no roll-forward, and the workloads from the
// workload set global.json pins. Any other way takes a newer patch the day
// one reaches the runners, and a locked restore refuses it (NU1004), which is
// how #276 began. This fails on:
//
//   - a `uses: actions/setup-dotnet@…` in a workflow or another action,
//     unless a comment in its step, above the `uses:` line, says
//     `sdk-pin: exempt` with the reason (agent-review.yml's judge, which
//     checks out main and restores nothing);
//   - a `dotnet workload install|update|restore|repair` in a workflow or
//     another action;
//   - a global.json without sdk.workloadVersion, or with one that is not its
//     sdk.version: the workload set that ships with an SDK carries that SDK's
//     version, so an SDK bump moves both together.
//
// It reads every .yml and .yaml file under .github/, except the composite
// action itself, and global.json, at the repository root or at --root, which
// is how its failure path is proven: tests/fixtures/sdk-pin/failing/ breaks
// each rule once, and tests/fixtures/sdk-pin/passing/ keeps them all.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.

using System.Text.Json;
using System.Text.RegularExpressions;

string? rootArgument = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--root" && i + 1 < args.Length)
    {
        rootArgument = args[++i];
    }
    else
    {
        Console.Error.WriteLine($"sdk-pin: unknown argument '{args[i]}'. Usage: dotnet run eng/sdk-pin.cs -- [--root <dir>]");
        return 2;
    }
}

string root = rootArgument is null ? FindRepositoryRoot() : Path.GetFullPath(rootArgument);
string github = Path.Combine(root, ".github");
string composite = Path.Combine(github, "actions", "setup-dotnet") + Path.DirectorySeparatorChar;
string globalJson = Path.Combine(root, "global.json");

if (!Directory.Exists(github) || !File.Exists(globalJson))
{
    Console.Error.WriteLine($"sdk-pin: {root} has no .github directory or no global.json.");
    return 2;
}

Regex setupDotnet = new(@"^\s*-?\s*uses:\s*actions/setup-dotnet@", RegexOptions.CultureInvariant);
Regex workload = new(@"\bdotnet(\.exe)?""?\s+workload\s+(install|update|restore|repair)\b", RegexOptions.CultureInvariant);
List<string> findings = [];

foreach (string file in Directory.EnumerateFiles(github, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".yml", StringComparison.Ordinal) || f.EndsWith(".yaml", StringComparison.Ordinal))
    .Where(f => !f.StartsWith(composite, StringComparison.Ordinal))
    .Order(StringComparer.Ordinal))
{
    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
    string[] lines = File.ReadAllLines(file);

    for (int n = 0; n < lines.Length; n++)
    {
        string line = lines[n];
        string code = line.TrimStart().StartsWith('#') ? string.Empty : line;

        if (setupDotnet.IsMatch(code) && !ExemptInItsStep(lines, n))
        {
            findings.Add($"{relative}:{n + 1}: actions/setup-dotnet directly; use ./.github/actions/setup-dotnet, which pins global.json's SDK with no roll-forward (or mark the step 'sdk-pin: exempt' with the reason)");
        }

        if (workload.IsMatch(code))
        {
            findings.Add($"{relative}:{n + 1}: a workload installed outside ./.github/actions/setup-dotnet; pass it as that action's `workloads:` input, which installs from the workload set global.json pins");
        }
    }
}

try
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJson));
    JsonElement sdk = document.RootElement.TryGetProperty("sdk", out JsonElement s) ? s : default;
    string? version = sdk.ValueKind == JsonValueKind.Object && sdk.TryGetProperty("version", out JsonElement v) ? v.GetString() : null;
    string? workloadVersion = sdk.ValueKind == JsonValueKind.Object && sdk.TryGetProperty("workloadVersion", out JsonElement w) ? w.GetString() : null;

    if (workloadVersion is null)
    {
        findings.Add("global.json: no sdk.workloadVersion; a workload install then takes the newest manifests on the feed, not the SDK's workload set");
    }
    else if (!string.Equals(workloadVersion, version, StringComparison.Ordinal))
    {
        findings.Add($"global.json: sdk.workloadVersion is {workloadVersion} and sdk.version is {version}; the workload set that ships with an SDK carries its version, so an SDK bump moves both, and the lockfiles, together");
    }
}
catch (JsonException e)
{
    Console.Error.WriteLine($"sdk-pin: global.json is not JSON: {e.Message}");
    return 2;
}

if (findings.Count == 0)
{
    Console.WriteLine("ok  .NET is set up, and workloads installed, only through ./.github/actions/setup-dotnet, from global.json's SDK and its workload set");
    return 0;
}

foreach (string finding in findings)
{
    Console.WriteLine("FAIL " + finding);
}

Console.WriteLine($"sdk-pin: {findings.Count} finding(s) (ADR 0224 point 5, amended 2026-11-11; #276).");
return 1;

// The step a `uses:` line belongs to starts at the nearest line above it that
// opens a list item; an exemption is a comment between there and the line.
static bool ExemptInItsStep(string[] lines, int index)
{
    if (lines[index].TrimStart().StartsWith("- ", StringComparison.Ordinal))
    {
        return false;
    }

    for (int n = index - 1; n >= 0; n--)
    {
        string trimmed = lines[n].TrimStart();
        if (trimmed.StartsWith('#') && trimmed.Contains("sdk-pin: exempt", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            return false;
        }
    }

    return false;
}

static string FindRepositoryRoot()
{
    string? directory = Directory.GetCurrentDirectory();
    while (directory is not null && !File.Exists(Path.Combine(directory, "Varve.slnx")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? throw new InvalidOperationException("Run from inside the repository.");
}
