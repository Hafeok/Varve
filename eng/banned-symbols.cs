// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Banned-symbol citation gate.
//
//   dotnet run eng/banned-symbols.cs -- [--dir <eng>] [--adr-dir <docs/adr>]
//
// ADR 0063: every entry of every eng/BannedSymbols*.txt cites the decision that
// bans it, in its comment and at the end of its message. The banned list is a
// decision surface like the dependency register, and an entry nobody can trace
// is a ban nobody can argue with. That was a convention from ADR 0004 on; 0063
// made it a decision and said it becomes a gate when session 2 of #43 touches
// the file. A rule only in a document is not a rule.
//
// For each entry, a line that is neither blank nor a // comment:
//
//   - it has the form <symbol id>; <message>, the id starting T:, M:, P:, F:,
//     E: or N:, which is what BannedApiAnalyzers reads;
//   - its message ends with the citation, "ADR NNNN." or "ADR NNNN, ADR MMMM.",
//     and every ADR cited names an existing docs/adr/NNNN-*.md;
//   - the comment block it belongs to (the nearest // lines above it, back to
//     the previous blank line or entry group) cites at least one of the same
//     ADRs, so the reasoning next to the ban is the decision it names;
//   - no symbol id is banned twice across the files, since BannedApiAnalyzers
//     would report the one it reads first and the other message would be dead.
//
// Exit codes: 0 conformant, 1 findings, 2 could not run.
//
// See docs/adr/0063-build-time-analyzer-packages.md.

using System.Text.RegularExpressions;

string repositoryRoot = FindRepositoryRoot();
string listDirectory = Path.Combine(repositoryRoot, "eng");
string adrDirectory = Path.Combine(repositoryRoot, "docs", "adr");

for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--dir" && i + 1 < args.Length)
    {
        listDirectory = args[++i];
    }
    else if (args[i] is "--adr-dir" && i + 1 < args.Length)
    {
        adrDirectory = args[++i];
    }
}

if (!Directory.Exists(listDirectory) || !Directory.Exists(adrDirectory))
{
    Console.Error.WriteLine($"banned-symbols: no list directory at '{listDirectory}' or no ADR directory at '{adrDirectory}'.");
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

Regex entrySyntax = new(@"^(?<id>[TMPFEN]:[^;\s]+);\s*(?<message>.+)$", RegexOptions.CultureInvariant);
Regex citationAtEnd = new(@"ADR \d{4}(, ADR \d{4})*\.$", RegexOptions.CultureInvariant);
Regex anyCitation = new(@"ADR (?<number>\d{4})", RegexOptions.CultureInvariant);

List<string> findings = [];
Dictionary<string, string> idOwner = new(StringComparer.Ordinal);
int entries = 0;
int files = 0;

foreach (string path in Directory.EnumerateFiles(listDirectory, "BannedSymbols*.txt").Order(StringComparer.Ordinal))
{
    files++;
    string name = Path.GetFileName(path);
    string[] lines = File.ReadAllText(path).Split('\n');

    // The ADRs the comment block governing the next entries cites. A blank
    // line after an entry ends a group; a comment after a blank line starts a
    // new block, and the entries below it answer to that block.
    HashSet<string> blockCitations = new(StringComparer.Ordinal);
    bool previousWasEntry = false;

    for (int i = 0; i < lines.Length; i++)
    {
        string line = lines[i].TrimEnd('\r').Trim();
        string where = $"{name}:{i + 1}";

        if (line.Length == 0)
        {
            continue;
        }

        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            if (previousWasEntry)
            {
                blockCitations.Clear();
                previousWasEntry = false;
            }

            foreach (Match match in anyCitation.Matches(line))
            {
                blockCitations.Add(match.Groups["number"].Value);
            }

            continue;
        }

        previousWasEntry = true;
        entries++;

        Match entry = entrySyntax.Match(line);
        if (!entry.Success)
        {
            findings.Add($"{where}: not '<T:|M:|P:|F:|E:|N:id>; <message>'.");
            continue;
        }

        string id = entry.Groups["id"].Value;
        string message = entry.Groups["message"].Value.Trim();

        if (idOwner.TryGetValue(id, out string? other))
        {
            findings.Add($"{where}: {id} is also banned at {other}.");
        }
        else
        {
            idOwner[id] = where;
        }

        if (!citationAtEnd.IsMatch(message))
        {
            findings.Add($"{where}: the message of {id} does not end with its citation, 'ADR NNNN.' (ADR 0063).");
            continue;
        }

        HashSet<string> cited = new(StringComparer.Ordinal);
        foreach (Match match in anyCitation.Matches(message))
        {
            string number = match.Groups["number"].Value;
            cited.Add(number);

            if (!knownAdrs.Contains(number))
            {
                findings.Add($"{where}: {id} cites ADR {number}, which has no docs/adr/{number}-*.md.");
            }
        }

        if (!cited.Overlaps(blockCitations))
        {
            findings.Add($"{where}: the comment above {id} cites none of the ADRs its message does ({string.Join(", ", cited.Order())}).");
        }
    }
}

Console.WriteLine($"banned-symbols: {files} list(s), {entries} entr{(entries == 1 ? "y" : "ies")}.");

if (findings.Count == 0)
{
    return 0;
}

Console.Error.WriteLine();
Console.Error.WriteLine($"banned-symbols: {findings.Count} finding(s):");
foreach (string finding in findings)
{
    Console.Error.WriteLine($"  {finding}");
}

return 1;

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
