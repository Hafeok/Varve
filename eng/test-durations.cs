// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Test durations from a run's TRX.
//
//   dotnet run eng/test-durations.cs -- <trx-file-or-directory> [--top <n>] [--label <text>]
//                                       [--started <unix-seconds> --timeout-minutes <n>]
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
// The budget (#219): with --started, the Unix time at which the leg's time
// limit began counting, and --timeout-minutes, that limit, it also writes the
// leg's elapsed time against the limit, and fails when the leg has used more
// than 80% of it. A leg that creeps towards its limit is red, with the reason,
// on the night it passes 80%, not cancelled without a word on the night it
// passes 100%. The budget is judged whether or not a TRX was written.
//
// Exit codes: 0 written, 1 over budget, 2 could not run. No TRX at all is
// reported and exits 0 (or 1 over budget): the step that should have written
// it has already failed, and a second red step would say nothing new.
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
long? started = null;
int? timeoutMinutes = null;
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
        case "--started" when long.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out long at) && at > 0:
            started = at;
            break;
        case "--timeout-minutes" when int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int limit) && limit > 0:
            timeoutMinutes = limit;
            break;
        case "--started" or "--timeout-minutes":
            Console.Error.WriteLine($"{args[i]} takes a positive whole number, not '{args[i + 1]}'.");
            return 2;
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

if ((started is null) != (timeoutMinutes is null))
{
    Console.Error.WriteLine("--started and --timeout-minutes go together: the budget needs both.");
    return 2;
}

if (files.Length == 0)
{
    output.AppendLine(CultureInfo.InvariantCulture, $"No TRX under `{input}`: the test step did not write one.");
    output.AppendLine();
    bool overNoTrx = Budget(output);
    Emit(output.ToString());
    return overNoTrx ? 1 : 0;
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
        $"| {++rank} | {Minutes(r.Duration)} | {r.Outcome} | {Cell(Short(r.Class))} | {Cell(Method(r))} |");
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

output.AppendLine();
bool over = Budget(output);
Emit(output.ToString());
return over ? 1 : 0;

// The leg's elapsed time against 80% of its limit: whether it is over.
bool Budget(StringBuilder text)
{
    if (started is not { } from || timeoutMinutes is not { } limit)
    {
        return false;
    }

    TimeSpan elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(from);
    TimeSpan budget = TimeSpan.FromMinutes(limit * 0.8);
    double share = elapsed.TotalMinutes / limit;
    string leg = label ?? input;
    text.AppendLine(CultureInfo.InvariantCulture,
        $"Budget: {Minutes(elapsed)} of the {limit}-minute limit ({share * 100:0}%); 80% is {Minutes(budget)}.");
    if (elapsed <= budget)
    {
        return false;
    }

    string message = string.Create(CultureInfo.InvariantCulture,
        $"{leg} took {Minutes(elapsed)}, {share * 100:0}% of its {limit}-minute limit, past the 80% budget of {Minutes(budget)}. Find where the time went in the tables above, then make the slow tests cheaper or shard the leg (docs/testing.md section 8, #219); never lower an iteration count to fit the clock.");
    text.AppendLine();
    text.AppendLine("**Over budget.** " + message);
    Console.WriteLine("::error title=Over the time budget::" + message);
    return true;
}

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
// The platform names a test by its class and method; the class has a column.
static string Method(Result r) =>
    r.Name.StartsWith(r.Class + ".", StringComparison.Ordinal) ? r.Name[(r.Class.Length + 1)..] : r.Name;

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
