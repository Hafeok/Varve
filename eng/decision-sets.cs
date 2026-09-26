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
// DecisionDriven.Analyzers' generator reads these files in every build
// (Directory.Build.props points DdLedgerDirectory here), and reports what it
// checks itself: a key claimed twice (DDGEN0001), a key that is not an
// identifier (DDGEN0002), and a key that collides with its set's generated
// class (DDGEN0005). This gate no longer checks those three. It is KEPT,
// because it checks what the generator does not, and what the generator
// silently tolerates:
//
//   - set, namespace, and each decision's key and statement are present;
//   - accepted-at comes with accepted-by and accepted-by with accepted-at;
//     accepted-by is a mailto: identity; every date is an xsd:dateTime with a
//     zone;
//   - a set id is lowercase alphanumerics, dashes and dots;
//   - a field the reader does not know is reported rather than ignored,
//     because the reader ignores it, and a misspelt accepted-by would read as
//     an unaccepted decision;
//   - a statement is double-quoted with no quote inside it. The package's
//     reader cuts an unquoted value at " #", so an unquoted statement can lose
//     its end silently;
//   - a file without front matter is skipped by the package and reported
//     here, except a README.md;
//
// and Varve's own rules, which the package does not need but this ledger does:
//
//   - the namespace is `varve`, and the file is named <set>.md;
//   - a set names where its decisions come from: `adr: NNNN` for a set that
//     enumerates an ADR, naming an existing docs/adr/NNNN-*.md that no other
//     file claims; or `origin: "..."` for a set filed without one (a ruling of
//     the brief, or a question a finding raised), which is unaccepted until
//     the maintainer accepts it (ADR 0066). Exactly one of the two.
//
// revoked-at is reported in the summary every run: ADR 0062 reserves it for a
// ruling withdrawn with no successor, and each use should be visible.
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
HashSet<string> setFields = new(StringComparer.Ordinal) { "set", "namespace", "adr", "origin", "decisions" };
HashSet<string> decisionFields = new(StringComparer.Ordinal) { "key", "statement", "accepted-by", "accepted-at", "revoked-at" };

List<string> findings = [];
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
    string? origin = null;
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
                case "origin": origin = value; break;
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

    if (adr is not null && origin is not null)
    {
        findings.Add($"{name}: both 'adr' and 'origin'; a set enumerates an ADR or is filed without one, not both.");
    }
    else if (adr is null && origin is null)
    {
        findings.Add($"{name}: no 'adr' and no 'origin'.");
    }
    else if (origin is not null)
    {
        if (origin.Length < 3 || origin[0] != '"' || origin[^1] != '"' || origin[1..^1].Contains('"'))
        {
            findings.Add($"{name}: origin is not one double-quoted line with no quote inside it.");
        }
    }
    else if (adr is null)
    {
        // Unreachable: one of the two is present.
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

        // Key syntax (DDGEN0002), a key claimed twice (DDGEN0001) and a key
        // equal to its set's class name (DDGEN0005) are the generator's to
        // report, in every build.

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
