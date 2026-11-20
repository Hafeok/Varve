// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// A dataset the previous release wrote, opened by this build
// (docs/operator/upgrade.md, #129).
//
//   dotnet run eng/upgrade.cs -- --resolve
//   dotnet run eng/upgrade.cs -- --previous <varve> --current <varve, or Varve.Server.dll>
//
// --resolve prints the previous release's package version: the newest
// descriptor in releases/ whose version is also a tag (`git tag --list v*`),
// by SemVer precedence, without its "v". A descriptor not yet tagged is a
// release still proposed (ADR 0102) and is skipped; a tag with no descriptor
// is not a release this repository describes. Never a version from memory.
//
// The check: the previous release's command line creates a file dataset,
// loads, updates, checkpoints and updates again, then exports the dataset at
// every position and reads its id. This build then opens the same directory —
// as an in-place upgrade does, derived/ and all — and a copy of its log/
// alone, as a restore from a log copy does. Each must have the same id and
// head, export the same quads at every position, answer the same query, and
// take a write after the head. Writing with this build and reading back with
// the previous release is a downgrade, which is not tested (upgrade.md).
//
// Exit codes: 0 the same answers, 1 a difference, 2 could not run.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args is ["--resolve"])
{
    string? version = Resolve(out string? why);

    if (version is null)
    {
        Console.Error.WriteLine("upgrade: " + why);
        return 2;
    }

    Console.WriteLine(version);
    return 0;
}

if (args is not ["--previous", string previous, "--current", string current] || !File.Exists(previous) || !File.Exists(current))
{
    Console.Error.WriteLine("usage: dotnet run eng/upgrade.cs -- --resolve | --previous <varve> --current <varve, or Varve.Server.dll>; both files existing.");
    return 2;
}

previous = Path.GetFullPath(previous);
current = Path.GetFullPath(current);
string root = Directory.CreateTempSubdirectory("varve-upgrade-").FullName;
string dataset = Path.Combine(root, "people");
string logOnly = Path.Combine(root, "people-log");
string quads = Path.Combine(root, "people.nq");
List<string> findings = [];

const string Everything = "SELECT ?g ?s ?p ?o WHERE { { ?s ?p ?o } UNION { GRAPH ?g { ?s ?p ?o } } } ORDER BY ?g ?s ?p ?o";

try
{
    await File.WriteAllTextAsync(quads,
        "<http://ex/a> <http://ex/name> \"Ada\"@en .\n" +
        "<http://ex/a> <http://ex/age> \"36\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n" +
        "<http://ex/b> <http://ex/knows> <http://ex/a> <http://ex/people> .\n");

    // The previous release writes the dataset.
    await Require(previous, "create", dataset);
    await Require(previous, "load", dataset, quads);
    await Require(previous, "update", dataset, "-u", "INSERT DATA { <http://ex/c> <http://ex/name> \"Cé\"@fr }");
    await Require(previous, "checkpoint", dataset);
    await Require(previous, "update", dataset, "-u", "DELETE DATA { <http://ex/a> <http://ex/age> 36 } ; INSERT DATA { <http://ex/a> <http://ex/age> 37 }");
    await Require(previous, "update", dataset, "-u", "INSERT DATA { GRAPH <http://ex/people> { <http://ex/c> <http://ex/knows> <http://ex/b> } }");

    (string id, long head) = Info(await Require(previous, "info", dataset));
    Dictionary<long, string> expected = [];

    for (long position = 1; position <= head; position++)
    {
        expected[position] = Sorted(await Require(previous, "export", dataset, "--as-of", "position:" + position.ToString(CultureInfo.InvariantCulture)));
    }

    string answer = await Require(previous, "query", dataset, "-q", Everything, "-f", "csv");

    if (head != 4 || expected.Values.Distinct(StringComparer.Ordinal).Count() != 4)
    {
        Console.Error.WriteLine("upgrade: the previous release wrote head " + head + " and " + expected.Values.Distinct(StringComparer.Ordinal).Count() + " distinct states, not 4 and 4.");
        return 2;
    }

    Copy(Path.Combine(dataset, "log"), Path.Combine(logOnly, "log"));

    // This build reads it: in place, and from the log alone.
    foreach ((string label, string directory) in ((string, string)[])[("in place", dataset), ("log/ alone", logOnly)])
    {
        (string readId, long readHead) = Info(await Require(current, "info", directory));

        if (readId != id || readHead != head)
        {
            findings.Add(label + ": id " + readId + " head " + readHead + ", the previous release wrote id " + id + " head " + head);
        }

        for (long position = 1; position <= head; position++)
        {
            string read = Sorted(await Require(current, "export", directory, "--as-of", "position:" + position.ToString(CultureInfo.InvariantCulture)));

            if (read != expected[position])
            {
                findings.Add(label + ": position " + position + " exports\n" + read + "\nand the previous release exported\n" + expected[position]);
            }
        }

        string query = await Require(current, "query", directory, "-q", Everything, "-f", "csv");

        if (query != answer)
        {
            findings.Add(label + ": the query answers\n" + query + "\nand the previous release answered\n" + answer);
        }

        string written = await Require(current, "update", directory, "-u", "INSERT DATA { <http://ex/d> <http://ex/name> \"after\" }");

        if (!written.Contains("committed, position " + (head + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            findings.Add(label + ": a write after the upgrade answered " + written.Trim());
        }
    }
}
catch (CouldNotRun failure)
{
    Console.Error.WriteLine("upgrade: " + failure.Message);
    return findings.Count > 0 ? 1 : 2;
}
finally
{
    Directory.Delete(root, recursive: true);
}

foreach (string finding in findings)
{
    Console.Error.WriteLine("upgrade: " + finding);
}

string verdict = findings.Count == 0
    ? "This build reads the previous release's dataset, in place and from its log alone: the same id, head and answers at every position, and a write after."
    : findings.Count + " difference(s) between the previous release's dataset and this build's reading of it.";
Console.WriteLine(verdict);

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
{
    await File.AppendAllTextAsync(summary, "### Upgrade from the previous release\n\n" + verdict + "\n");
}

return findings.Count == 0 ? 0 : 1;

// The newest tagged release with a descriptor, by SemVer precedence.
static string? Resolve(out string? why)
{
    string releases = Path.Combine(Directory.GetCurrentDirectory(), "releases");

    if (!Directory.Exists(releases))
    {
        why = "no releases/ here; run it from the repository's root.";
        return null;
    }

    ProcessStartInfo git = new("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    git.ArgumentList.Add("tag");
    git.ArgumentList.Add("--list");
    git.ArgumentList.Add("v*");
    using Process process = Process.Start(git)!;
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        why = "git tag --list failed: " + process.StandardError.ReadToEnd().Trim();
        return null;
    }

    HashSet<string> tags = [.. output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    Regex versionLine = new(@"^version:\s*(?<v>v\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    List<string> released = [];

    foreach (string file in Directory.EnumerateFiles(releases, "v*.yaml"))
    {
        Match match = versionLine.Match(File.ReadAllText(file));

        if (match.Success && tags.Contains(match.Groups["v"].Value) && Path.GetFileNameWithoutExtension(file) == match.Groups["v"].Value)
        {
            released.Add(match.Groups["v"].Value[1..]);
        }
    }

    if (released.Count == 0)
    {
        why = "no descriptor in releases/ names a tag; fetch the tags (fetch-depth: 0) first.";
        return null;
    }

    released.Sort(Precedence);
    why = null;
    return released[^1];
}

// SemVer 2.0.0 §11: the core numerically, a pre-release below its release,
// pre-release identifiers numerically when both are numbers, else ordinally.
static int Precedence(string x, string y)
{
    static (string Core, string? Pre) Split(string v) => v.IndexOf('-', StringComparison.Ordinal) is int dash and >= 0 ? (v[..dash], v[(dash + 1)..]) : (v, null);

    (string coreX, string? preX) = Split(x);
    (string coreY, string? preY) = Split(y);
    int[] a = [.. coreX.Split('.').Select(int.Parse)];
    int[] b = [.. coreY.Split('.').Select(int.Parse)];

    for (int i = 0; i < 3; i++)
    {
        if (a[i] != b[i])
        {
            return a[i].CompareTo(b[i]);
        }
    }

    if (preX is null || preY is null)
    {
        return preX is null ? (preY is null ? 0 : 1) : -1;
    }

    string[] p = preX.Split('.');
    string[] q = preY.Split('.');

    for (int i = 0; i < Math.Min(p.Length, q.Length); i++)
    {
        bool pn = long.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out long pv);
        bool qn = long.TryParse(q[i], NumberStyles.None, CultureInfo.InvariantCulture, out long qv);
        int order = pn && qn ? pv.CompareTo(qv) : pn ? -1 : qn ? 1 : string.CompareOrdinal(p[i], q[i]);

        if (order != 0)
        {
            return order;
        }
    }

    return p.Length.CompareTo(q.Length);
}

// A command's output; a non-zero exit is not a difference but a run that failed.
static async Task<string> Require(string varve, params string[] arguments)
{
    bool dll = varve.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    ProcessStartInfo command = new(dll ? "dotnet" : varve) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

    if (dll)
    {
        command.ArgumentList.Add(varve);
    }

    foreach (string argument in arguments)
    {
        command.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(command)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync();
    Task<string> error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    string text = (await output).Replace("\r\n", "\n", StringComparison.Ordinal);

    if (process.ExitCode != 0)
    {
        throw new CouldNotRun(Path.GetFileName(Path.GetDirectoryName(varve)) + "/" + Path.GetFileName(varve) + " " + string.Join(' ', arguments) + " exited " + process.ExitCode + ": " + (await error).Trim());
    }

    return text;
}

static (string Id, long Head) Info(string json)
{
    using JsonDocument document = JsonDocument.Parse(json);
    return (document.RootElement.GetProperty("id").GetString()!, document.RootElement.GetProperty("head").GetInt64());
}

static string Sorted(string lines) =>
    string.Join('\n', lines.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

static void Copy(string from, string to)
{
    Directory.CreateDirectory(to);

    foreach (string file in Directory.EnumerateFiles(from))
    {
        File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }

    foreach (string child in Directory.EnumerateDirectories(from))
    {
        Copy(child, Path.Combine(to, Path.GetFileName(child)));
    }
}

sealed class CouldNotRun(string message) : Exception(message);
