// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The format gate (ADR 0216).
//
//   dotnet run eng/formats.cs
//   dotnet run eng/formats.cs -- --fixtures tests/fixtures/formats
//
// docs/formats/README.md holds the inventory: a table, between the markers
// <!-- formats --> and <!-- /formats -->, with one row per format version. This
// checks it against the repository:
//   - every version constant the gate watches has a row naming it, and its
//     value in the code is that row's version;
//   - every row's fixture under testdata/formats/ exists, and every line of
//     its SHA256SUMS verifies; or the row names a gap, "gap: <reason>";
//   - every fixture under testdata/formats/ is named by a row.
// The tests that read each fixture are the rest of the gate: a format change
// without a new version fails there, since current code writes each
// fixture's workload and the bytes must equal it.
//
// The watched constants are listed below; a case under --fixtures lists its
// own in formats-constants.txt, one "<file> <name>" a line. --fixtures judges
// every case directory there, each a repository of its own: a case named
// pass-* must pass and fail-* must fail (tests/fixtures/formats/README.md).
//
// Exit codes: 0 consistent, 1 not, 2 could not run.

using System.Security.Cryptography;
using System.Text.RegularExpressions;

(string File, string Name)[] watched =
[
    ("src/Varve.Store/LogFormat.cs", "Version"),
    ("src/Varve.Store/LogFormat.cs", "InlineSetVersion"),
    ("src/Varve.Store/DerivedFormat.cs", "Version"),
    ("src/Varve.Store/DerivedFormat.cs", "ExtentVersion"),
    ("src/Varve.Store/DerivedFormat.cs", "CheckedVersion"),
    ("src/Varve.Store.ObjectStorage/ChunkHeader.cs", "Version"),
    ("src/Varve.Store.ObjectStorage/WriterRecordFormat.cs", "Version"),
    ("src/Varve.Store.ObjectStorage/ReaderRecordFormat.cs", "Version"),
    ("src/Varve.Delta/DeltaFormat.cs", "Current"),
    ("src/Varve.Management/ManagementPlan.cs", "Format"),
];

if (args is ["--fixtures", string directory])
{
    int wrong = 0;
    string[] cases = [.. Directory.GetDirectories(directory).Order(StringComparer.Ordinal)];

    foreach (string root in cases)
    {
        string name = Path.GetFileName(root);
        (string, string)[] own = [.. File.ReadAllLines(Path.Combine(root, "formats-constants.txt")).Where(l => l.Length > 0)
            .Select(l => (l.Split(' ')[0], l.Split(' ')[1]))];
        List<string> problems = Check(root, own);
        bool expected = name.StartsWith("pass-", StringComparison.Ordinal);
        bool right = (problems.Count == 0) == expected;
        wrong += right ? 0 : 1;
        Console.WriteLine((right ? "ok   " : "FAIL ") + name + ": " + (problems.Count == 0 ? "consistent" : string.Join("; ", problems)));
    }

    Console.WriteLine(cases.Length + " cases, " + wrong + " judged wrong");
    return cases.Length > 0 && wrong == 0 ? 0 : 1;
}

if (args.Length > 0)
{
    Console.Error.WriteLine("formats: unknown arguments " + string.Join(' ', args));
    return 2;
}

List<string> found = Check(Directory.GetCurrentDirectory(), watched);

foreach (string problem in found)
{
    Console.WriteLine("FAIL " + problem);
}

Console.WriteLine(found.Count == 0 ? "ok  the format inventory matches the code and every fixture verifies (ADR 0216)" : found.Count + " problem(s) in the format inventory (ADR 0216)");
return found.Count == 0 ? 0 : 1;

static List<string> Check(string root, IReadOnlyList<(string File, string Name)> watched)
{
    List<string> problems = [];
    string inventory = Path.Combine(root, "docs", "formats", "README.md");

    if (!File.Exists(inventory))
    {
        return ["docs/formats/README.md is missing"];
    }

    string text = File.ReadAllText(inventory);
    int start = text.IndexOf("<!-- formats -->", StringComparison.Ordinal);
    int end = text.IndexOf("<!-- /formats -->", StringComparison.Ordinal);

    if (start < 0 || end < start)
    {
        return ["docs/formats/README.md has no table between <!-- formats --> and <!-- /formats -->"];
    }

    List<Row> rows = [];

    foreach (string line in text[start..end].Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|') && !l.StartsWith("|---", StringComparison.Ordinal) && !l.StartsWith("| Format ", StringComparison.Ordinal)))
    {
        string[] cells = [.. line.Trim('|').Split('|').Select(c => c.Trim())];

        if (cells.Length != 6)
        {
            problems.Add("a row does not have six cells: " + line);
            continue;
        }

        Match constant = Regex.Match(cells[2], @"^`([^`]+)` `([A-Za-z]+)`$");
        rows.Add(new Row(cells[0], cells[1], constant.Success ? (constant.Groups[1].Value, constant.Groups[2].Value) : null, cells[5]));
    }

    // Every watched constant: one row names it, and the code's value is its version.
    foreach ((string file, string name) in watched)
    {
        Row[] naming = [.. rows.Where(r => r.Constant == (file, name))];

        if (naming.Length != 1)
        {
            problems.Add(file + " " + name + " is named by " + naming.Length + " rows, not one");
            continue;
        }

        string path = Path.Combine(root, file);

        if (!File.Exists(path))
        {
            problems.Add(file + " does not exist");
            continue;
        }

        Match value = Regex.Match(File.ReadAllText(path), @"\b" + name + @"\s*(?:=|=>\s*new)\s*\(?\s*(\d+)\s*\)?\s*;");

        if (!value.Success)
        {
            problems.Add(file + " declares no " + name + " the gate can read");
        }
        else if (value.Groups[1].Value != naming[0].Version)
        {
            problems.Add(file + " " + name + " is " + value.Groups[1].Value + " but its row, " + naming[0].Format + ", says " + naming[0].Version
                + ": a new version needs its row and its fixture");
        }
    }

    // A row's constant is one the gate watches.
    foreach (Row row in rows.Where(r => r.Constant is not null && !watched.Contains(r.Constant.Value)))
    {
        problems.Add(row.Format + " names " + row.Constant!.Value.File + " " + row.Constant.Value.Name + ", which the gate does not watch");
    }

    // Every row has a fixture that verifies, or names a gap.
    HashSet<string> named = new(StringComparer.Ordinal);

    foreach (Row row in rows)
    {
        if (row.Fixture.StartsWith("gap: ", StringComparison.Ordinal) && row.Fixture.Length > "gap: ".Length)
        {
            continue;
        }

        Match fixture = Regex.Match(row.Fixture, "^`([a-z0-9.-]+)`$");

        if (!fixture.Success)
        {
            problems.Add(row.Format + " " + row.Version + " has no fixture and names no gap");
            continue;
        }

        string directory = Path.Combine(root, "testdata", "formats", fixture.Groups[1].Value);
        named.Add(fixture.Groups[1].Value);
        string sums = Path.Combine(directory, "SHA256SUMS");

        if (!File.Exists(sums))
        {
            problems.Add(row.Format + " " + row.Version + ": testdata/formats/" + fixture.Groups[1].Value + " has no SHA256SUMS");
            continue;
        }

        foreach (string line in File.ReadAllLines(sums).Where(l => l.Length > 0))
        {
            string file = Path.Combine(directory, line[66..]);

            if (!File.Exists(file) || !string.Equals(line[..64], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))), StringComparison.Ordinal))
            {
                problems.Add("testdata/formats/" + fixture.Groups[1].Value + "/" + line[66..] + " does not verify");
            }
        }
    }

    // Every fixture is named.
    string fixtures = Path.Combine(root, "testdata", "formats");

    foreach (string directory in Directory.Exists(fixtures) ? Directory.GetDirectories(fixtures) : [])
    {
        if (!named.Contains(Path.GetFileName(directory)))
        {
            problems.Add("testdata/formats/" + Path.GetFileName(directory) + " is named by no row");
        }
    }

    return problems;
}

sealed record Row(string Format, string Version, (string File, string Name)? Constant, string Fixture);
