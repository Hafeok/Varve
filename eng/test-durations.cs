// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Test durations from a run's TRX.
//
//   dotnet run eng/test-durations.cs -- <trx-file-or-directory> [--top <n>] [--label <text>]
//
// Writes the slowest tests of a run, and the classes that held the run's
// parallel slots, to standard output and to the job summary. #126: the store
// suite's step went from 8 to 27–54 minutes on Windows, and nothing in the log
// said where the time went, because Microsoft.Testing.Platform prints only
// failed and skipped tests.
//
// Two tables:
//   - the <n> slowest tests (default 20), by their own duration;
//   - the slowest classes by their busy time (the sum of their tests'
//     durations), with the minutes into the run at which each class's first
//     test started and its last test ended. A class whose last test ends long
//     after every other class's is the tail the step waits for.
//
// It reads every *.trx under a directory, so a step that writes several (a
// re-run, a filter run twice) is read as one run.
//
// Exit codes: 0 written, 2 could not run. No TRX at all is reported and exits
// 0: the step that should have written it has already failed, and a second red
// step would say nothing new.
//
// See docs/testing.md, "How long the tests take".

using System.Globalization;
using System.Text;
using System.Xml.Linq;

XNamespace trxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run eng/test-durations.cs -- <trx-file-or-directory> [--top <n>] [--label <text>]");
    return 2;
}

string input = args[0];
int top = 20;
string? label = null;
for (int i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--top" when int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0:
            top = n;
            break;
        case "--label":
            label = args[i + 1];
            break;
        default:
            break;
    }
}

string[] files = File.Exists(input)
    ? [input]
    : Directory.Exists(input)
        ? Directory.GetFiles(input, "*.trx", SearchOption.AllDirectories)
        : [];

var output = new StringBuilder();
output.AppendLine(CultureInfo.InvariantCulture, $"### Test durations{(label is null ? "" : $" — {label}")}");
output.AppendLine();

if (files.Length == 0)
{
    output.AppendLine(CultureInfo.InvariantCulture, $"No TRX under `{input}`: the test step did not write one.");
    Emit(output.ToString());
    return 0;
}

var results = new List<Result>();
foreach (string file in files)
{
    XDocument document;
    try
    {
        document = XDocument.Load(file);
    }
    catch (System.Xml.XmlException e)
    {
        Console.Error.WriteLine($"{file}: not a TRX ({e.Message})");
        return 2;
    }

    // The class is on the definition, the duration on the result; testId joins them.
    var classes = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (XElement test in document.Descendants(trxNs + "UnitTest"))
    {
        string? id = (string?)test.Attribute("id");
        string? className = (string?)test.Element(trxNs + "TestMethod")?.Attribute("className");
        if (id is not null && className is not null)
        {
            classes[id] = className;
        }
    }

    foreach (XElement result in document.Descendants(trxNs + "UnitTestResult"))
    {
        string name = (string?)result.Attribute("testName") ?? "?";
        string id = (string?)result.Attribute("testId") ?? "";
        string outcome = (string?)result.Attribute("outcome") ?? "?";
        TimeSpan duration = TimeSpan.TryParse((string?)result.Attribute("duration"), CultureInfo.InvariantCulture, out TimeSpan d) ? d : TimeSpan.Zero;
        DateTimeOffset? start = DateTimeOffset.TryParse((string?)result.Attribute("startTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset s) ? s : null;
        DateTimeOffset? end = DateTimeOffset.TryParse((string?)result.Attribute("endTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset e) ? e : null;
        string className = classes.TryGetValue(id, out string? c) ? c : "?";
        results.Add(new Result(className, name, outcome, duration, start, end));
    }
}

// The namespace every class shares is dropped from the tables, so a suite's
// classes read as SmokeTests and Faults.FaultInjectionTests.
string sharedNamespace = SharedNamespace(results.Select(r => r.Class).Where(c => c != "?").Distinct(StringComparer.Ordinal).ToList());

DateTimeOffset? runStart = results.Where(r => r.Start is not null).Select(r => r.Start).Min();
DateTimeOffset? runEnd = results.Where(r => r.End is not null).Select(r => r.End).Max();
TimeSpan busy = results.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);

output.AppendLine(CultureInfo.InvariantCulture,
    $"{results.Count} results from {files.Length} TRX; first start to last end {Minutes(runEnd - runStart)}; busy time (sum of durations) {Minutes(busy)}.");
output.AppendLine();
output.AppendLine("| # | Duration | Outcome | Class | Test |");
output.AppendLine("|---:|---:|---|---|---|");
int rank = 0;
foreach (Result r in results.OrderByDescending(r => r.Duration).Take(top))
{
    output.AppendLine(CultureInfo.InvariantCulture,
        $"| {++rank} | {Minutes(r.Duration)} | {r.Outcome} | {Cell(Short(r.Class))} | {Cell(r.Name)} |");
}

output.AppendLine();
output.AppendLine("Classes by busy time; *from* and *to* are minutes into the run of the class's first start and last end.");
output.AppendLine();
output.AppendLine("| Class | Tests | Busy | From | To |");
output.AppendLine("|---|---:|---:|---:|---:|");
foreach (IGrouping<string, Result> group in results
    .GroupBy(r => r.Class, StringComparer.Ordinal)
    .OrderByDescending(g => g.Sum(r => r.Duration.Ticks))
    .Take(top))
{
    TimeSpan classBusy = TimeSpan.FromTicks(group.Sum(r => r.Duration.Ticks));
    DateTimeOffset? from = group.Where(r => r.Start is not null).Select(r => r.Start).Min();
    DateTimeOffset? to = group.Where(r => r.End is not null).Select(r => r.End).Max();
    output.AppendLine(CultureInfo.InvariantCulture,
        $"| {Cell(Short(group.Key))} | {group.Count()} | {Minutes(classBusy)} | {Offset(from)} | {Offset(to)} |");
}

Emit(output.ToString());
return 0;

string Offset(DateTimeOffset? at) =>
    at is { } value && runStart is { } origin
        ? (value - origin).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)
        : "?";

static string Minutes(TimeSpan? span) =>
    span is not { } value ? "?"
    : value.TotalMinutes >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes}m {value.Seconds:00}s")
        : string.Create(CultureInfo.InvariantCulture, $"{value.TotalSeconds:0.0}s");

string Short(string className) =>
    sharedNamespace.Length > 0 && className.StartsWith(sharedNamespace, StringComparison.Ordinal)
        ? className[sharedNamespace.Length..]
        : className;

// The longest run of whole namespace segments, with its trailing dot, that
// every class's namespace starts with.
static string SharedNamespace(List<string> classes)
{
    if (classes.Count == 0)
    {
        return "";
    }

    string[] shared = classes[0].Split('.')[..^1];
    foreach (string className in classes.Skip(1))
    {
        string[] parts = className.Split('.')[..^1];
        int same = 0;
        while (same < shared.Length && same < parts.Length && shared[same] == parts[same])
        {
            same++;
        }

        shared = shared[..same];
    }

    return shared.Length == 0 ? "" : string.Join('.', shared) + ".";
}

static string Cell(string text)
{
    string escaped = text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    return escaped.Length <= 160 ? escaped : string.Concat(escaped.AsSpan(0, 157), "...");
}

static void Emit(string text)
{
    Console.Write(text);
    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
    {
        File.AppendAllText(summary, text + Environment.NewLine);
    }
}

sealed record Result(string Class, string Name, string Outcome, TimeSpan Duration, DateTimeOffset? Start, DateTimeOffset? End);
