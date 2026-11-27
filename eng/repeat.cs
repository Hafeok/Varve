// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Runs a named test filter N times, under load if asked (#254).
//
//   dotnet run eng/repeat.cs -- --project <tests/Name> --filter "<xUnit filter>"
//                               [--count N] [--load] [--at <ref>] [--compare <ref>] [--trx]
//                               [--out <directory>]
//
// A test that fails now and then is found by running it many times in the
// conditions it failed in, and keeping the whole output of each failure. This
// builds the test project once, in Release, then runs its test assembly N
// times through xUnit's in-process runner with the filter given, which is the
// runner's own syntax: "-method Full.Name", "-class Full.Name", either
// repeated. A run that fails keeps its whole output, as run-<i>.log under
// --out (artifacts/repeat by default); a run that passes keeps nothing. It
// ends with the count, the failures, and each distinct first failing line.
//
// --load runs the filter beside what made #254's test fail: a competing
// Release build of the solution, from a second checkout so that it never
// touches the binaries under test, rebuilt in a loop until the runs end; and
// one busy thread per processor. The load is the same on every OS.
//
// --compare <ref> answers "how much slower is it now": it also builds the
// project at <ref>, fetched when it is not here, from a checkout of its own,
// and runs both N times, alternating, the side that goes first changing every
// round, so the runner's drift falls on both alike. The summary gives each
// side's failures and the wall time of its runs: mean, median, least, most.
// --at <ref> runs the project as it is at <ref>, not as checked out, for
// when the head itself does not build: a head red on an unaccepted decision's
// CS0618 (ADR 0066) is measured at the commit before its citations.
// --trx keeps each run's results as <side>-<i>.trx, so that a difference can
// be put on the tests that make it.
//
// The nightly workflow's repeat job runs it on a dispatch, on any branch and
// either OS (docs/testing.md §11). It is never a required check.
//
// Exit codes: 0 every run passed, 1 a run failed, 2 could not run.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

string? project = null;
List<string> filter = [];
int count = 100;
bool load = false;
bool trx = false;
string? compare = null;
string? at = null;
string? output = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project" when i + 1 < args.Length:
            project = args[++i];
            break;
        case "--filter" when i + 1 < args.Length:
            filter.AddRange(args[++i].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            break;
        case "--count" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n > 0:
            count = n;
            i++;
            break;
        case "--load":
            load = true;
            break;
        case "--at" when i + 1 < args.Length:
            at = args[++i];
            break;
        case "--compare" when i + 1 < args.Length:
            compare = args[++i];
            break;
        case "--trx":
            trx = true;
            break;
        case "--out" when i + 1 < args.Length:
            output = args[++i];
            break;
        default:
            Console.Error.WriteLine("repeat: unknown or incomplete argument '" + args[i] + "'.");
            return 2;
    }
}

if (project is null || filter.Count == 0)
{
    Console.Error.WriteLine("repeat: --project and --filter are required, as in --project tests/Varve.Server.Tests --filter \"-class Varve.Server.Tests.EventMoveTests\".");
    return 2;
}

string root = FindRepositoryRoot();
string name = Path.GetFileName(project.TrimEnd('/', '\\'));
string projectFile = Path.Combine(root, project, name + ".csproj");
output = Path.GetFullPath(output ?? Path.Combine(root, "artifacts", "repeat"));

if (!File.Exists(projectFile))
{
    Console.Error.WriteLine("repeat: no project at " + projectFile + ".");
    return 2;
}

Directory.CreateDirectory(output);

// The test assembly of the project in the checkout at `checkout`, built in Release; null when the build fails.
string? Build(string checkout)
{
    if (Run("dotnet", ["build", Path.Combine(checkout, project, name + ".csproj"), "--configuration", "Release"], checkout, echo: true).Exit != 0)
    {
        return null;
    }

    return Directory.GetFiles(Path.Combine(checkout, project, "bin", "Release"), name + ".dll", SearchOption.AllDirectories)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();
}

List<string> checkouts = [];

// A checkout of `reference`, fetched when it is not here; null when it cannot be had.
string? Checkout(string reference)
{
    if (Run("git", ["cat-file", "-e", reference + "^{commit}"], root, echo: false).Exit != 0
        && Run("git", ["fetch", "--depth", "1", "origin", reference], root, echo: true).Exit != 0)
    {
        return null;
    }

    string target = Run("git", ["cat-file", "-e", reference + "^{commit}"], root, echo: false).Exit == 0 ? reference : "FETCH_HEAD";
    string path = Path.Combine(Path.GetTempPath(), "varve-repeat-at-" + Guid.NewGuid().ToString("N")[..8]);

    if (Run("git", ["worktree", "add", "--detach", path, target], root, echo: true).Exit != 0)
    {
        return null;
    }

    checkouts.Add(path);
    return path;
}

static string Short(string reference) => reference.Length > 12 ? reference[..12] : reference;

List<(string Label, string Assembly)> sides = [];

try
{
    string? measured = at is null ? root : Checkout(at);

    if (measured is null || Build(measured) is not { } here)
    {
        Console.Error.WriteLine("repeat: could not check out or build " + (at ?? "the checkout") + ".");
        return 2;
    }

    sides.Add((at is null ? "head" : Short(at), here));

    if (compare is not null)
    {
        if (Checkout(compare) is not { } before || Build(before) is not { } there)
        {
            Console.Error.WriteLine("repeat: could not check out or build " + compare + ".");
            return 2;
        }

        sides.Insert(0, (Short(compare), there));
    }
}
catch
{
    foreach (string path in checkouts)
    {
        Run("git", ["worktree", "remove", "--force", path], root, echo: false);
    }

    throw;
}

using CancellationTokenSource stopping = new();
List<Task> loads = [];
string? second = null;

if (load)
{
    // One busy thread per processor.
    for (int i = 0; i < Environment.ProcessorCount; i++)
    {
        loads.Add(Task.Factory.StartNew(() =>
        {
            long spin = 0;

            while (!stopping.IsCancellationRequested)
            {
                spin++;
            }

            return spin;
        }, TaskCreationOptions.LongRunning));
    }

    // A competing build, from a second checkout of the same commit.
    second = Path.Combine(Path.GetTempPath(), "varve-repeat-" + Guid.NewGuid().ToString("N")[..8]);

    if (Run("git", ["worktree", "add", "--detach", second, "HEAD"], root, echo: true).Exit != 0)
    {
        Console.Error.WriteLine("repeat: could not check out a second copy for the competing build.");
        return 2;
    }

    loads.Add(Task.Run(() =>
    {
        while (!stopping.IsCancellationRequested)
        {
            Run("dotnet", ["build", Path.Combine(second, "Varve.slnx"), "--configuration", "Release", "--no-incremental"], second, echo: false, stopping.Token);
        }
    }));
}

Dictionary<string, int> failed = sides.ToDictionary(side => side.Label, _ => 0, StringComparer.Ordinal);
Dictionary<string, List<double>> seconds = sides.ToDictionary(side => side.Label, _ => new List<double>(), StringComparer.Ordinal);
Dictionary<string, int> distinct = new(StringComparer.Ordinal);
Stopwatch elapsed = Stopwatch.StartNew();

try
{
    for (int i = 1; i <= count; i++)
    {
        // The side that goes first changes every round.
        foreach ((string label, string assembly) in i % 2 == 1 ? sides : Enumerable.Reverse(sides).ToList())
        {
            string prefix = sides.Count == 1 ? "run-" + i : label + "-" + i;
            List<string> arguments = [assembly, .. filter, "-noColor"];

            if (trx)
            {
                arguments.AddRange(["-result-trx", Path.Combine(output, prefix + ".trx")]);
            }

            Stopwatch run = Stopwatch.StartNew();
            (int exit, string text) = Run("dotnet", arguments, Path.GetDirectoryName(assembly)!, echo: false);
            seconds[label].Add(run.Elapsed.TotalSeconds);

            // A filter that matches nothing is a mistake, never a pass.
            Match total = Regex.Match(text, @"Total: (?<n>\d+)", RegexOptions.CultureInvariant);

            if (!total.Success || total.Groups["n"].Value == "0")
            {
                File.WriteAllText(Path.Combine(output, prefix + ".log"), text);
                Console.Error.WriteLine("repeat: " + prefix + " ran no test; its output is in " + prefix + ".log.");
                return 2;
            }

            if (exit != 0)
            {
                failed[label]++;
                File.WriteAllText(Path.Combine(output, prefix + ".log"), text);
                string first = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.EndsWith("[FAIL]", StringComparison.Ordinal)) ?? "(no [FAIL] line)";
                distinct[label + ": " + first] = distinct.GetValueOrDefault(label + ": " + first) + 1;
                Console.WriteLine(prefix + " of " + count + ": FAILED, " + first);
            }
        }

        if (i % 50 == 0 || sides.Count > 1)
        {
            Console.WriteLine("round " + i + " of " + count + ": " + failed.Values.Sum() + " failed so far, " + elapsed.Elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
finally
{
    await stopping.CancelAsync();
    await Task.WhenAll(loads).ConfigureAwait(false);

    if (second is not null)
    {
        Run("git", ["worktree", "remove", "--force", second], root, echo: false);
    }

    foreach (string path in checkouts)
    {
        Run("git", ["worktree", "remove", "--force", path], root, echo: false);
    }
}

int failures = failed.Values.Sum();
StringBuilder summary = new();
summary.AppendLine("### repeat: " + name + " " + string.Join(' ', filter));
summary.AppendLine();
summary.AppendLine("| | |");
summary.AppendLine("|---|---|");
summary.AppendLine("| OS | " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " |");
summary.AppendLine("| Load | " + (load ? "a competing Release build and " + Environment.ProcessorCount + " busy threads" : "none") + " |");
summary.AppendLine("| Runs | " + count + (sides.Count > 1 ? " a side, alternating" : "") + " |");
summary.AppendLine("| Time | " + elapsed.Elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) + " |");
summary.AppendLine();
summary.AppendLine("| Side | Failed | Mean | Median | Least | Most |");
summary.AppendLine("|---|---:|---:|---:|---:|---:|");

foreach ((string label, _) in sides)
{
    List<double> times = [.. seconds[label].Order()];
    string Format(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
    double median = times.Count % 2 == 1 ? times[times.Count / 2] : (times[(times.Count / 2) - 1] + times[times.Count / 2]) / 2;
    summary.AppendLine("| " + label + " | " + failed[label] + " | " + Format(times.Average()) + " | " + Format(median) + " | " + Format(times[0]) + " | " + Format(times[^1]) + " |");
}

foreach ((string first, int times) in distinct.OrderByDescending(pair => pair.Value))
{
    summary.AppendLine();
    summary.AppendLine(times + " × `" + first + "`");
}

Console.WriteLine();
Console.Write(summary);

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } step)
{
    File.AppendAllText(step, summary.ToString());
}

return failures == 0 ? 0 : 1;

static (int Exit, string Text) Run(string file, IEnumerable<string> arguments, string directory, bool echo, CancellationToken cancellationToken = default)
{
    ProcessStartInfo start = new(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };

    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start)!;
    StringBuilder text = new();
    object gate = new();
    process.OutputDataReceived += (_, line) => { if (line.Data is { } data) { lock (gate) { text.AppendLine(data); } if (echo) { Console.WriteLine(data); } } };
    process.ErrorDataReceived += (_, line) => { if (line.Data is { } data) { lock (gate) { text.AppendLine(data); } if (echo) { Console.Error.WriteLine(data); } } };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    try
    {
        process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
    }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
    }

    process.WaitForExit();
    return (process.ExitCode, text.ToString());
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
