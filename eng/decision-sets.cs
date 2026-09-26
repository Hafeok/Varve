// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Decision set gate.
//
//   dotnet run eng/decision-sets.cs -- [--dir <docs/decisions>] [--adr-dir <docs/adr>]
//
// ADR 0062 enumerates every Varve ADR into docs/decisions/ as the interim set
// files DecisionDriven.Analyzers reads: one markdown file per ADR, whose YAML
// front matter lists that ADR's rulings as keys with one-line statements and,
// where the ADR is accepted, the acceptance transcribed from it:
//
//   ---
//   set: enforcement-by-analyzers
//   namespace: varve
//   adr: 0004
//   decisions:
//     - key: OffTheShelfFirst
//       statement: "..."
//       accepted-by: mailto:...
//       accepted-at: 2026-09-20T00:00:00Z
//   ---
//
// Until session 2 of #43 wires these files into the generator, nothing reads
// them, and DDGEN0001 (a key claimed twice) cannot fire. A ledger that nothing
// reads rots the same way an unenforced rule does, so this checks them against
// the package's documented rules (docs/rules/ledger-input.md and the README's
// "The decision set format" in hafeok/decision-driven-analyzers):
//
//   - a key matches ^[A-Z][A-Za-z0-9]{0,63}$, and is unique across the
//     namespace — every file, not just its own (DDGEN0001, DDGEN0002);
//   - set, namespace, and each decision's key and statement are present;
//   - accepted-at comes with accepted-by and accepted-by with accepted-at;
//     accepted-by is a mailto: identity; every date is an xsd:dateTime;
//   - a set id is lowercase alphanumerics, dashes and dots;
//   - a key is not its own set's generated class name, which C# rejects as CS0542
//     (found by the first real build against the generator, not by the documentation).
//
// And three rules of Varve's own, which the package does not need but this
// ledger does:
//
//   - the namespace is `varve`, and the file is named <set>.md;
//   - adr names an existing docs/adr/NNNN-*.md, and no two files claim one;
//   - a statement is double-quoted with no quote inside it. The package's
//     reader cuts an unquoted value at " #", so an unquoted statement can lose
//     its end silently; and a field the reader does not know is reported
//     rather than ignored, because the reader ignores it, and a misspelt
//     accepted-by would read as an unaccepted decision.
//
// It reads front matter exactly where the package does: the file's first
// non-blank line must be ---. A file without front matter is skipped by the
// package and reported here, except a README.md.
//
// revoked-at is reported in the summary every run: ADR 0062 reserves it for a
// ruling withdrawn with no successor, and each use should be visible.
//
// When session 2 wires the generator, this is kept for what DDGEN does not
// check (the three Varve rules) or retired, and that session says which.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0062-adopting-decisiondriven-analyzers.md.

using System.Globalization;
using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();

string decisionDirectory = Path.Combine(repositoryRoot, "docs", "decisions");
string adrDirectory = Path.Combine(repositoryRoot, "docs", "adr");

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--dir" && i + 1 < args.Length)
    {
        decisionDirectory = args[++i];
    }
    else if (args[i] is "--adr-dir" && i + 1 < args.Length)
    {
        adrDirectory = args[++i];
    }
}

if (!Directory.Exists(decisionDirectory))
{
    Console.Error.WriteLine($"decision-sets: no decision directory at '{decisionDirectory}'.");
    return 2;
}

if (!Directory.Exists(adrDirectory))
{
    Console.Error.WriteLine($"decision-sets: no ADR directory at '{adrDirectory}'.");
    return 2;
}

HashSet<string> knownAdrs = new(StringComparer.Ordinal);
foreach (string file in Directory.EnumerateFiles(adrDirectory, "*.md"))
{
    Match match = Regex.Match(Path.GetFileName(file), @"^(?<number>\d{4})-");
    if (match.Success)
    {
        knownAdrs.Add(match.Groups["number"].Value);
    }
}

const string Namespace = "varve";

Regex keySyntax = new(@"^[A-Z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant);
Regex setSyntax = new(@"^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant);
Regex adrSyntax = new(@"^\d{4}$", RegexOptions.CultureInvariant);
Regex identitySyntax = new(@"^mailto:[^\s@]+@[^\s@]+$", RegexOptions.CultureInvariant);

// xsd:dateTime: a date, T, a time with optional fraction, and an optional zone.
// The zone is required here although xsd allows it absent, because a local time
// with no zone is not a moment and an acceptance is one.
Regex dateTimeSyntax = new(
    @"^-?\d{4,}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$",
    RegexOptions.CultureInvariant);

// Top-level fields, then the fields of one decision. Anything else is a typo
// the package's reader would silently ignore.
HashSet<string> setFields = new(StringComparer.Ordinal) { "set", "namespace", "adr", "decisions" };
HashSet<string> decisionFields = new(StringComparer.Ordinal) { "key", "statement", "accepted-by", "accepted-at", "revoked-at" };

List<string> findings = [];
Dictionary<string, string> keyOwner = new(StringComparer.Ordinal);
Dictionary<string, string> adrOwner = new(StringComparer.Ordinal);
Dictionary<string, string> setOwner = new(StringComparer.Ordinal);
List<string> revoked = [];

int files = 0;
int decisions = 0;
int accepted = 0;

foreach (string path in Directory.EnumerateFiles(decisionDirectory, "*.md").Order(StringComparer.Ordinal))
{
    string name = Path.GetFileName(path);
    string[] lines = File.ReadAllText(path).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    int start = -1;
    int end = -1;
    for (int i = 0; i < lines.Length; i++)
    {
        if (lines[i] != "---")
        {
            if (start < 0 && lines[i].Trim().Length > 0)
            {
                break;
            }

            continue;
        }

        if (start < 0)
        {
            start = i;
        }
        else
        {
            end = i;
            break;
        }
    }

    if (start < 0 || end < 0)
    {
        if (!name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add($"{name}: no front matter. The package skips it, so nothing in it can be cited.");
        }

        continue;
    }

    files++;

    string? set = null;
    string? ns = null;
    string? adr = null;
    bool sawDecisions = false;
    List<Dictionary<string, (string Value, int Line)>> entries = [];
    Dictionary<string, (string Value, int Line)>? current = null;

    for (int i = start + 1; i < end; i++)
    {
        string raw = lines[i];
        int lineNumber = i + 1;
        string where = $"{name}:{lineNumber}";

        if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
        {
            continue;
        }

        if (raw != raw.TrimEnd())
        {
            findings.Add($"{where}: trailing whitespace.");
        }

        bool topLevel = !char.IsWhiteSpace(raw[0]);
        string line = raw.Trim();

        if (!topLevel && line.StartsWith("- ", StringComparison.Ordinal))
        {
            if (!sawDecisions)
            {
                findings.Add($"{where}: a decision before 'decisions:'.");
            }

            current = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
            entries.Add(current);
            line = line[2..].Trim();
        }

        int colon = line.IndexOf(':');
        if (colon <= 0)
        {
            findings.Add($"{where}: not a 'field: value' line.");
            continue;
        }

        string field = line[..colon];
        string value = line[(colon + 1)..].Trim();

        if (topLevel)
        {
            if (!setFields.Contains(field))
            {
                findings.Add($"{where}: unknown field '{field}'. The package's reader ignores it.");
                continue;
            }

            switch (field)
            {
                case "set": set = value; break;
                case "namespace": ns = value; break;
                case "adr": adr = value; break;
                case "decisions":
                    sawDecisions = true;
                    if (value.Length > 0)
                    {
                        findings.Add($"{where}: 'decisions:' takes a list on the lines below it, not a value.");
                    }

                    break;
            }

            continue;
        }

        if (current is null)
        {
            findings.Add($"{where}: an indented field outside any decision.");
            continue;
        }

        if (!decisionFields.Contains(field))
        {
            findings.Add($"{where}: unknown field '{field}'. The package's reader ignores it.");
            continue;
        }

        if (current.ContainsKey(field))
        {
            findings.Add($"{where}: '{field}' twice in one decision.");
            continue;
        }

        current[field] = (value, lineNumber);
    }

    // --- the set ------------------------------------------------------------

    if (set is null)
    {
        findings.Add($"{name}: no 'set'.");
    }
    else
    {
        if (!setSyntax.IsMatch(set))
        {
            findings.Add($"{name}: set id '{set}' is not lowercase alphanumerics, dashes and dots.");
        }

        if (!name.Equals(set + ".md", StringComparison.Ordinal))
        {
            findings.Add($"{name}: set id '{set}' does not match the file name; it should be {set}.md.");
        }

        if (setOwner.TryGetValue(set, out string? other))
        {
            findings.Add($"{name}: set id '{set}' is also claimed by {other}.");
        }
        else
        {
            setOwner[set] = name;
        }
    }

    if (ns is null)
    {
        findings.Add($"{name}: no 'namespace'.");
    }
    else if (ns != Namespace)
    {
        findings.Add($"{name}: namespace '{ns}'; every Varve decision is in '{Namespace}'.");
    }

    if (adr is null)
    {
        findings.Add($"{name}: no 'adr'.");
    }
    else if (!adrSyntax.IsMatch(adr))
    {
        findings.Add($"{name}: adr '{adr}' is not four digits.");
    }
    else if (!knownAdrs.Contains(adr))
    {
        findings.Add($"{name}: adr {adr} has no docs/adr/{adr}-*.md.");
    }
    else if (adrOwner.TryGetValue(adr, out string? other))
    {
        findings.Add($"{name}: ADR {adr} is also enumerated by {other}; one set per ADR.");
    }
    else
    {
        adrOwner[adr] = name;
    }

    if (!sawDecisions || entries.Count == 0)
    {
        findings.Add($"{name}: no decisions. An ADR with no ruling in force has no set file.");
    }

    // --- each decision ------------------------------------------------------

    foreach (Dictionary<string, (string Value, int Line)> entry in entries)
    {
        decisions++;

        if (!entry.TryGetValue("key", out (string Value, int Line) key))
        {
            findings.Add($"{name}: a decision with no 'key'.");
        }
        else
        {
            string where = $"{name}:{key.Line}";

            if (!keySyntax.IsMatch(key.Value))
            {
                findings.Add($"{where}: key '{key.Value}' does not match ^[A-Z][A-Za-z0-9]{{0,63}}$ (DDGEN0002).");
            }

            // The generator emits the set as a class named by PascalCasing its id and each key
            // as a class nested in it. C# forbids a member named like its enclosing type, so a
            // key equal to its set's class name is CS0542 in every consuming compilation.
            if (set is not null && key.Value == ToPascalCase(set))
            {
                findings.Add($"{where}: key '{key.Value}' is its set's own generated class name; a nested type cannot share it (CS0542).");
            }

            if (keyOwner.TryGetValue(key.Value, out string? other))
            {
                findings.Add($"{where}: key '{key.Value}' is also claimed at {other} (DDGEN0001).");
            }
            else
            {
                keyOwner[key.Value] = where;
            }
        }

        string label = key.Value ?? "(no key)";

        if (!entry.TryGetValue("statement", out (string Value, int Line) statement))
        {
            findings.Add($"{name}: decision '{label}' has no 'statement'.");
        }
        else
        {
            string where = $"{name}:{statement.Line}";
            string text = statement.Value;

            if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
            {
                findings.Add($"{where}: the statement of '{label}' is not double-quoted.");
            }
            else if (text[1..^1].Contains('"'))
            {
                findings.Add($"{where}: the statement of '{label}' has a quote inside it.");
            }
            else if (text[1..^1].Trim().Length == 0)
            {
                findings.Add($"{where}: the statement of '{label}' is empty.");
            }
        }

        bool hasBy = entry.TryGetValue("accepted-by", out (string Value, int Line) by);
        bool hasAt = entry.TryGetValue("accepted-at", out (string Value, int Line) at);

        if (hasBy && !identitySyntax.IsMatch(by.Value))
        {
            findings.Add($"{name}:{by.Line}: accepted-by '{by.Value}' of '{label}' is not a mailto: identity.");
        }

        if (hasAt && !IsDateTime(at.Value))
        {
            findings.Add($"{name}:{at.Line}: accepted-at '{at.Value}' of '{label}' is not an xsd:dateTime with a zone.");
        }

        if (hasBy != hasAt)
        {
            findings.Add($"{name}: '{label}' has {(hasBy ? "accepted-by without accepted-at" : "accepted-at without accepted-by")}; the two go together.");
        }

        if (hasBy && hasAt)
        {
            accepted++;
        }

        if (entry.TryGetValue("revoked-at", out (string Value, int Line) revokedAt))
        {
            if (!IsDateTime(revokedAt.Value))
            {
                findings.Add($"{name}:{revokedAt.Line}: revoked-at '{revokedAt.Value}' of '{label}' is not an xsd:dateTime with a zone.");
            }

            revoked.Add($"{label} ({name}, {revokedAt.Value})");
        }
    }
}

Console.WriteLine($"decision-sets: {files} set file(s), {decisions} decision(s), {accepted} accepted, in namespace '{Namespace}'.");

if (revoked.Count > 0)
{
    Console.WriteLine($"decision-sets: {revoked.Count} revoked with no successor (ADR 0062: each one is listed):");
    foreach (string entry in revoked)
    {
        Console.WriteLine($"  {entry}");
    }
}

if (findings.Count == 0)
{
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"decision-sets: {findings.Count} finding(s) in {Relative(repositoryRoot, decisionDirectory)}:");
foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

Console.Error.WriteLine();
Console.Error.WriteLine("The format is the interim form in hafeok/decision-driven-analyzers,");
Console.Error.WriteLine("docs/rules/ledger-input.md, with Varve's own rules in ADR 0062.");
return 1;

bool IsDateTime(string value) =>
    dateTimeSyntax.IsMatch(value)
    && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);

// The generator's rule for a set id's class name (Identifiers.ToPascalCase in
// hafeok/decision-driven-analyzers): each run between -, ., _ and space starts upper-case,
// and a leading digit is prefixed with _.
static string ToPascalCase(string id)
{
    System.Text.StringBuilder builder = new(id.Length);
    bool startOfWord = true;

    foreach (char c in id)
    {
        if (c is '-' or '.' or '_' or ' ')
        {
            startOfWord = true;
            continue;
        }

        builder.Append(startOfWord ? char.ToUpperInvariant(c) : c);
        startOfWord = false;
    }

    string name = builder.ToString();
    return name.Length > 0 && char.IsAsciiDigit(name[0]) ? "_" + name : name;
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

static string Relative(string root, string path) =>
    path.StartsWith(root, StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;
