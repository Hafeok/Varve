// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Runs a named test filter N times, under load if asked (#254).
//
//   dotnet run eng/repeat.cs -- --project <tests/Name> --filter "<xUnit filter>"
//                               [--count N] [--load] [--out <directory>]
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

if (Run("dotnet", ["build", projectFile, "--configuration", "Release"], root, echo: true).Exit != 0)
{
    Console.Error.WriteLine("repeat: the build failed.");
    return 2;
}

string assembly = Directory.GetFiles(Path.Combine(root, project, "bin", "Release"), name + ".dll", SearchOption.AllDirectories)
    .OrderByDescending(File.GetLastWriteTimeUtc)
    .FirstOrDefault() ?? "";

if (assembly.Length == 0)
{
    Console.Error.WriteLine("repeat: no " + name + ".dll under " + Path.Combine(project, "bin", "Release") + ".");
    return 2;
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

int failures = 0;
Dictionary<string, int> distinct = new(StringComparer.Ordinal);
Stopwatch elapsed = Stopwatch.StartNew();

try
{
    for (int i = 1; i <= count; i++)
    {
        (int exit, string text) = Run("dotnet", [assembly, .. filter, "-noColor"], Path.GetDirectoryName(assembly)!, echo: false);

        // A filter that matches nothing is a mistake, never a pass.
        Match total = Regex.Match(text, @"Total: (?<n>\d+)", RegexOptions.CultureInvariant);

        if (!total.Success || total.Groups["n"].Value == "0")
        {
            File.WriteAllText(Path.Combine(output, "run-" + i + ".log"), text);
            Console.Error.WriteLine("repeat: run " + i + " ran no test; its output is in run-" + i + ".log.");
            return 2;
        }

        if (exit != 0)
        {
            failures++;
            File.WriteAllText(Path.Combine(output, "run-" + i + ".log"), text);
            string first = text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.EndsWith("[FAIL]", StringComparison.Ordinal)) ?? "(no [FAIL] line)";
            distinct[first] = distinct.GetValueOrDefault(first) + 1;
            Console.WriteLine("run " + i + " of " + count + ": FAILED, " + first);
        }
        else if (i % 50 == 0)
        {
            Console.WriteLine("run " + i + " of " + count + ": " + failures + " failed so far, " + elapsed.Elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture));
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
}

StringBuilder summary = new();
summary.AppendLine("### repeat: " + name + " " + string.Join(' ', filter));
summary.AppendLine();
summary.AppendLine("| | |");
summary.AppendLine("|---|---|");
summary.AppendLine("| OS | " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " |");
summary.AppendLine("| Load | " + (load ? "a competing Release build and " + Environment.ProcessorCount + " busy threads" : "none") + " |");
summary.AppendLine("| Runs | " + count + " |");
summary.AppendLine("| Failed | " + failures + " |");
summary.AppendLine("| Time | " + elapsed.Elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) + " |");

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
