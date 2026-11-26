// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Web UI's download budget (ADR 0205).
//
//   dotnet run eng/browser-size.cs -- <published wwwroot> [--budget <path>]
//
// Measures what a browser downloads to run the Web UI: every file under
// _framework/, compressed with brotli at quality 11 and window 24 (as
// `brotli -q 11`), its .br, .gz, .map and .pdb siblings skipped, in two rows
// (ADR 0205, amended 2026-11-19), each against its own budget in
// eng/browser-size.json:
//
// - webui: the first screen, every file the boot loads;
// - webui-explorer: the lazy set, the assemblies the boot manifest marks
//   lazyAssembly, fetched when the explorer's route is first visited.
//
// The lazy set must be exactly the assemblies webui-explorer names: one lazy
// that the budget does not name, or named and loaded with the first screen,
// fails, so nothing moves between the rows unseen.
//
// It fails when the sum exceeds the budget. The budget is the measured size
// plus 5%, rounded up to 10 kB; raising it is an amendment to ADR 0205, so a
// growth is a decision, not a drift. A drop of more than 5% below the budget
// prints the line to lower it, as the conformance ratchet prints its new
// passes.
//
// Measure a publish into an empty folder: a publish over an earlier one
// leaves its fingerprinted files beside the new ones, and they count.
//
// It also fails when the publish holds the graph view's spike (ADR 0251
// point 8): its page and script are built in only with
// -p:VarveGraphSpike=true, for a measurement, and this is the publish the
// release zip and the image carry. Every file under the wwwroot, its .br and
// .gz siblings skipped, is searched for the spike's script name, its page's
// type name and its route.
//
// Exit codes: 0 within budget, 1 over it or holding the spike, 2 could not run.

using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;

string? root = null;
string budgetPath = "eng/browser-size.json";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--budget" when i + 1 < args.Length:
            budgetPath = args[++i];
            break;
        default:
            root ??= args[i];
            break;
    }
}

if (root is null || !Directory.Exists(Path.Combine(root, "_framework")))
{
    Console.Error.WriteLine("browser-size: give the published wwwroot, the directory holding _framework/.");
    return 2;
}

JsonNode? budgets = File.Exists(budgetPath) ? JsonNode.Parse(File.ReadAllText(budgetPath)) : null;

if (budgets?["webui"]?["budgetBytes"]?.GetValue<long>() is not long firstBudget
    || budgets?["webui-explorer"]?["budgetBytes"]?.GetValue<long>() is not long lazyBudget
    || budgets?["webui-explorer"]?["assemblies"] is not JsonArray named)
{
    Console.Error.WriteLine("browser-size: " + budgetPath + " needs webui.budgetBytes, and webui-explorer.budgetBytes and .assemblies.");
    return 2;
}

// The boot manifest is in dotnet.js (.NET 10): its lazyAssembly entries name
// each lazy assembly's published file, beside its virtual path.
string framework = Path.Combine(root, "_framework");
Dictionary<string, string> lazy = new(StringComparer.Ordinal);

foreach (string script in Directory.EnumerateFiles(framework, "dotnet*.js"))
{
    string text = File.ReadAllText(script);
    int at = text.IndexOf("\"lazyAssembly\": [", StringComparison.Ordinal);

    if (at < 0)
    {
        continue;
    }

    string list = text[at..text.IndexOf(']', at)];

    foreach (System.Text.RegularExpressions.Match entry in System.Text.RegularExpressions.Regex.Matches(list, "\"virtualPath\":\\s*\"([^\"]+)\",\\s*\"name\":\\s*\"([^\"]+)\""))
    {
        lazy[entry.Groups[2].Value] = entry.Groups[1].Value;
    }
}

HashSet<string> expected = [.. named.Select(n => n!.GetValue<string>())];
HashSet<string> actual = [.. lazy.Values];

if (!expected.SetEquals(actual))
{
    Console.WriteLine("FAIL: the lazy set is not what webui-explorer names. Lazy and unnamed: "
        + string.Join(", ", actual.Except(expected).Order(StringComparer.Ordinal)) + "; named and not lazy: "
        + string.Join(", ", expected.Except(actual).Order(StringComparer.Ordinal)) + ". Moving an assembly between the rows is an amendment to ADR 0205.");
    return 1;
}

long raw = 0;
long first = 0;
long deferred = 0;
List<(string Name, long Bytes, bool Lazy)> files = [];

foreach (string file in Directory.EnumerateFiles(framework).Order(StringComparer.Ordinal))
{
    if (file.EndsWith(".br", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal)
        || file.EndsWith(".map", StringComparison.Ordinal) || file.EndsWith(".pdb", StringComparison.Ordinal))
    {
        continue;
    }

    byte[] bytes = File.ReadAllBytes(file);
    byte[] output = new byte[BrotliEncoder.GetMaxCompressedLength(bytes.Length)];

    if (!BrotliEncoder.TryCompress(bytes, output, out int written, quality: 11, window: 24))
    {
        Console.Error.WriteLine("browser-size: brotli could not compress " + file + ".");
        return 2;
    }

    bool isLazy = lazy.ContainsKey(Path.GetFileName(file));
    raw += bytes.Length;
    first += isLazy ? 0 : written;
    deferred += isLazy ? written : 0;
    files.Add((Path.GetFileName(file), written, isLazy));
}

string Kb(long bytes) => (bytes / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " kB";

Console.WriteLine($"browser-size: {files.Count} files, {Kb(raw)} raw.");
Console.WriteLine($"  first screen   {Kb(first),10} brotli; budget {Kb(firstBudget)}.");
Console.WriteLine($"  explorer, lazy {Kb(deferred),10} brotli; budget {Kb(lazyBudget)}.");

foreach ((string name, long bytes, bool isLazy) in files.OrderByDescending(file => file.Bytes).Take(8))
{
    Console.WriteLine($"  {Kb(bytes),12}  {name}{(isLazy ? "  (lazy)" : "")}");
}

int result = 0;

// The spike is never shipped (ADR 0251 point 8).
byte[][] spikeMarks = [.. new[] { "spike.js", "GraphSpike", "_spike/graphs/spike" }.Select(System.Text.Encoding.UTF8.GetBytes)];
List<string> spiked = [];

foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    if (file.EndsWith(".br", StringComparison.Ordinal) || file.EndsWith(".gz", StringComparison.Ordinal))
    {
        continue;
    }

    byte[] content = File.ReadAllBytes(file);

    if (Path.GetFileName(file) == "spike.js" || spikeMarks.Any(mark => content.AsSpan().IndexOf(mark) >= 0))
    {
        spiked.Add(Path.GetRelativePath(root, file));
    }
}

if (spiked.Count > 0)
{
    Console.WriteLine("FAIL: the publish holds the graph view's spike, which only -p:VarveGraphSpike=true builds in: "
        + string.Join(", ", spiked) + ". Publish without it (ADR 0251 point 8).");
    result = 1;
}
else
{
    Console.WriteLine("ok  no file of the graph view's spike in the publish");
}

foreach ((string row, long measured, long budget) in new[] { ("webui", first, firstBudget), ("webui-explorer", deferred, lazyBudget) })
{
    if (measured > budget)
    {
        Console.WriteLine($"FAIL: {row} is {Kb(measured - budget)} over its budget. Find what grew; raising a budget is an amendment to ADR 0205.");
        result = 1;
    }
    else if (measured < budget * 100 / 105 - 10_000)
    {
        long lower = (measured * 105 / 100 + 9_999) / 10_000 * 10_000;
        Console.WriteLine($"ratchet: {row} is more than 5% under its budget. Lower {row}.budgetBytes in {budgetPath} to {lower.ToString(CultureInfo.InvariantCulture)}.");
    }
}

if (result != 0)
{
    return result;
}

Console.WriteLine("ok  within budget");
return 0;
