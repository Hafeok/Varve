// Conformance ratchet.
//
//   dotnet run eng/ratchet.cs -- <trx-file-or-directory> [--baseline <path>]
//
// Reads the TRX from a conformance run and compares it against
// tests/Varve.Conformance.Tests/baseline/passing.txt, which holds the test IRIs
// that pass, one per line, sorted.
//
// It fails when:
//   - a test in the baseline is no longer passing;
//   - a test in the baseline is not in the run at all, which is how a renamed
//     or silently dropped case becomes a build failure rather than a quiet loss
//     of coverage;
//   - the harness itself failed — a guard test, not a conformance case. A
//     harness defect gates; a parser result does not.
//
// It does not fail when a test newly passes. It prints those, with the lines to
// add, so the baseline moves in the same pull request as the change that earned
// it. A ratchet that failed on improvement is a ratchet people route around.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0007-w3c-conformance-harness.md.

using System.Globalization;
using System.Text;
using System.Xml.Linq;

const string Marker = "(testIri: \"";

XNamespace trxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

string[] arguments = args;
if (arguments.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run eng/ratchet.cs -- <trx-file-or-directory> [--baseline <path>]");
    return 2;
}

string trxArgument = arguments[0];
string? baselineOverride = null;

for (int i = 1; i < arguments.Length - 1; i++)
{
    if (arguments[i] is "--baseline")
    {
        baselineOverride = arguments[i + 1];
    }
}

string repositoryRoot = FindRepositoryRoot();
string baselinePath = baselineOverride
    ?? Path.Combine(repositoryRoot, "tests", "Varve.Conformance.Tests", "baseline", "passing.txt");

string? trxPath = ResolveTrx(trxArgument);
if (trxPath is null)
{
    Console.Error.WriteLine($"ratchet: no .trx file at '{trxArgument}'.");
    return 2;
}

if (!File.Exists(baselinePath))
{
    Console.Error.WriteLine($"ratchet: no baseline at '{baselinePath}'.");
    return 2;
}

// --- read the run ----------------------------------------------------------

Dictionary<string, bool> caseOutcomes = new(StringComparer.Ordinal);
List<string> harnessFailures = [];

foreach (XElement result in XDocument.Load(trxPath).Descendants(trxNs + "UnitTestResult"))
{
    string name = result.Attribute("testName")?.Value ?? "";
    bool passed = string.Equals(result.Attribute("outcome")?.Value, "Passed", StringComparison.Ordinal);

    int marker = name.IndexOf(Marker, StringComparison.Ordinal);
    if (marker < 0)
    {
        // Not a conformance case: a guard test, or anything else the
        // conformance project runs. These gate.
        if (!passed)
        {
            harnessFailures.Add(name);
        }

        continue;
    }

    // xUnit appends the argument list to the display name and truncates long
    // values, so the identity is the prefix — the display name we set, which is
    // the test IRI — and never the echoed argument.
    string testIri = name[..marker];

    // A case seen twice passes only if it passed every time.
    caseOutcomes[testIri] = passed && caseOutcomes.GetValueOrDefault(testIri, true);
}

HashSet<string> baseline = new(
    File.ReadAllLines(baselinePath)
        .Select(static line => line.Trim())
        .Where(static line => line.Length > 0 && !line.StartsWith('#')),
    StringComparer.Ordinal);

// --- compare ---------------------------------------------------------------

List<string> regressed = [];
List<string> missing = [];

foreach (string expected in baseline.OrderBy(static iri => iri, StringComparer.Ordinal))
{
    if (!caseOutcomes.TryGetValue(expected, out bool passed))
    {
        missing.Add(expected);
    }
    else if (!passed)
    {
        regressed.Add(expected);
    }
}

List<string> newlyPassing = caseOutcomes
    .Where(entry => entry.Value && !baseline.Contains(entry.Key))
    .Select(static entry => entry.Key)
    .OrderBy(static iri => iri, StringComparer.Ordinal)
    .ToList();

// --- report ----------------------------------------------------------------

string summary = BuildSummary(caseOutcomes, baseline.Count, newlyPassing.Count, regressed.Count, missing.Count);
Console.WriteLine(summary);

string? stepSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (!string.IsNullOrEmpty(stepSummary))
{
    File.AppendAllText(stepSummary, summary + Environment.NewLine);
}

if (newlyPassing.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{newlyPassing.Count} test(s) now pass that are not in the baseline.");
    Console.WriteLine($"Add these to {Relative(repositoryRoot, baselinePath)} in the same pull request:");
    Console.WriteLine();

    foreach (string iri in newlyPassing)
    {
        Console.WriteLine(iri);
    }
}

bool failed = false;

if (harnessFailures.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"FAIL: the conformance harness itself failed ({harnessFailures.Count} test(s)). "
        + "This is a harness defect, not a parser result.");

    foreach (string name in harnessFailures.OrderBy(static n => n, StringComparer.Ordinal))
    {
        Console.Error.WriteLine($"  {name}");
    }
}

if (regressed.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine($"FAIL: {regressed.Count} baseline test(s) no longer pass:");

    foreach (string iri in regressed)
    {
        Console.Error.WriteLine($"  {iri}");
    }
}

if (missing.Count > 0)
{
    failed = true;
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        $"FAIL: {missing.Count} baseline test(s) were not in the run at all. A case was renamed, "
        + "dropped, or its suite stopped being enumerated:");

    foreach (string iri in missing)
    {
        Console.Error.WriteLine($"  {iri}");
    }
}

return failed ? 1 : 0;

// --- helpers ---------------------------------------------------------------

static string BuildSummary(
    Dictionary<string, bool> outcomes, int baselineCount, int newlyPassing, int regressed, int missing)
{
    // The suite key is the manifest IRI — everything before the fragment — so
    // this stays in step with the suite table without duplicating it.
    SortedDictionary<string, (int Passed, int Failed)> bySuite = new(StringComparer.Ordinal);

    foreach ((string iri, bool passed) in outcomes)
    {
        int hash = iri.IndexOf('#', StringComparison.Ordinal);
        string suite = hash > 0 ? iri[..hash] : iri;

        (int p, int f) = bySuite.GetValueOrDefault(suite);
        bySuite[suite] = passed ? (p + 1, f) : (p, f + 1);
    }

    StringBuilder builder = new();
    builder.AppendLine("## W3C conformance");
    builder.AppendLine();
    builder.AppendLine("| Suite | Passed | Failed | Total |");
    builder.AppendLine("|---|---:|---:|---:|");

    int totalPassed = 0;
    int totalFailed = 0;

    foreach ((string suite, (int passed, int failed)) in bySuite)
    {
        totalPassed += passed;
        totalFailed += failed;
        builder.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| `{ShortSuiteName(suite)}` | {passed} | {failed} | {passed + failed} |"));
    }

    builder.AppendLine(string.Create(
        CultureInfo.InvariantCulture,
        $"| **total** | **{totalPassed}** | **{totalFailed}** | **{totalPassed + totalFailed}** |"));
    builder.AppendLine();
    builder.AppendLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Baseline: {baselineCount} test(s). Newly passing: {newlyPassing}. "
        + $"Regressed: {regressed}. Missing from the run: {missing}."));

    return builder.ToString().TrimEnd();
}

static string ShortSuiteName(string manifestIri)
{
    string[] segments = manifestIri.Split('/');
    return segments.Length >= 2 ? segments[^2] : manifestIri;
}

static string? ResolveTrx(string argument)
{
    if (File.Exists(argument))
    {
        return argument;
    }

    if (!Directory.Exists(argument))
    {
        return null;
    }

    return Directory.EnumerateFiles(argument, "*.trx", SearchOption.AllDirectories)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();
}

static string Relative(string root, string path) =>
    path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;

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
