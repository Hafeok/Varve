// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The Web UI's download budget (ADR 0205).
//
//   dotnet run eng/browser-size.cs -- <published wwwroot> [--budget <path>]
//
// Measures what a browser downloads to run the Web UI: every file under
// _framework/, compressed with brotli at quality 11 and window 24 (as
// `brotli -q 11`), its .br, .gz, .map and .pdb siblings skipped. Compares the
// sum with eng/browser-size.json.
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
// Exit codes: 0 within budget, 1 over it, 2 could not run.

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

if (!File.Exists(budgetPath) || JsonNode.Parse(File.ReadAllText(budgetPath))?["webui"]?["budgetBytes"]?.GetValue<long>() is not long budget)
{
    Console.Error.WriteLine("browser-size: no webui.budgetBytes in " + budgetPath + ".");
    return 2;
}

long raw = 0;
long compressed = 0;
List<(string Name, long Bytes)> files = [];

foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "_framework")).Order(StringComparer.Ordinal))
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

    raw += bytes.Length;
    compressed += written;
    files.Add((Path.GetFileName(file), written));
}

string Kb(long bytes) => (bytes / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " kB";

Console.WriteLine($"browser-size: {files.Count} files, {Kb(raw)} raw, {Kb(compressed)} brotli; budget {Kb(budget)}.");

foreach ((string name, long bytes) in files.OrderByDescending(file => file.Bytes).Take(8))
{
    Console.WriteLine($"  {Kb(bytes),12}  {name}");
}

if (compressed > budget)
{
    Console.WriteLine($"FAIL: the Web UI is {Kb(compressed - budget)} over its budget. Find what grew; raising the budget is an amendment to ADR 0205.");
    return 1;
}

if (compressed < budget * 100 / 105 - 10_000)
{
    long lower = (compressed * 105 / 100 + 9_999) / 10_000 * 10_000;
    Console.WriteLine($"ratchet: the Web UI is more than 5% under its budget. Lower webui.budgetBytes in {budgetPath} to {lower.ToString(CultureInfo.InvariantCulture)}.");
}

Console.WriteLine("ok  within budget");
return 0;
