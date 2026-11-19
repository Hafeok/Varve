// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Varve.Site;

/// <summary>The tone of a badge: the design system's state tones (docs/ui, vw-state--…).</summary>
internal enum Tone
{
    Ok,
    Warn,
    Neutral,
}

/// <summary>A badge as the page draws it: its tone and the word it shows.</summary>
internal sealed record Badge(Tone Tone, string Word)
{
    public string Html => $"<span class=\"vw-state vw-state--{Tone.ToString().ToLowerInvariant()}\">{Layout.Encode(Word)}</span>";
}

/// <summary>An unaccepted amendment of an ADR by another ADR: shown on the amended ADR as a note, never as its badge.</summary>
internal sealed record AmendmentNote(string By, string? Date, int Unaccepted);

/// <summary>Rulings of an ADR's own set still waiting for acceptance: how many, and the date of the amendment they belong to, when their statements give one.</summary>
internal sealed record Pending(string? Amendment, int Count);

/// <summary>
/// What an ADR's head shows: its badge, its decision set, its own pending
/// rulings grouped by amendment, and the notes of unaccepted amendments made
/// by other ADRs.
/// </summary>
internal sealed record AdrState(Badge Badge, string? Set, IReadOnlyList<AmendmentNote> Notes, IReadOnlyList<Pending> Pending);

/// <summary>
/// One ruling of a decision set: accepted when its key carries accepted-by,
/// in force unless revoked, and the date of the amendment that added or
/// changed it when its statement ends "amended YYYY-MM-DD" (ADR 0068 point 4).
/// </summary>
internal sealed record Ruling(string Key, bool Accepted, bool Revoked, string? Amended = null);

/// <summary>A decision set: the rulings one ADR records (docs/decisions/, ADR 0062).</summary>
internal sealed record DecisionSet(string Name, string? Adr, IReadOnlyList<Ruling> Rulings);

/// <summary>
/// An ADR's status badge, read from the ledger and never from its status line
/// (the maintainer's ruling 4 on #368, corrected 2026-11-19). Every status line
/// on main reads "Accepted — filed unaccepted by …", before and after the
/// maintainer accepts, so the line cannot tell the two apart; acceptance is in
/// the decision set, where a ruling is accepted when its key carries
/// accepted-by. The whole mapping is here, in one place:
/// <list type="bullet">
///   <item>the ADR index row says it is superseded (a clause of its Status
///   cell that starts "Superseded by"): Superseded, tone neutral. A wholly
///   superseded ADR has no set, so the index is where this is recorded;</item>
///   <item>it has a set, and every ruling in force in it is accepted:
///   Accepted, tone ok;</item>
///   <item>some of its rulings in force are accepted and some are not:
///   "Accepted · N pending", tone warn, N the unaccepted ones; the head says
///   which amendment they belong to when their statements date one;</item>
///   <item>none of its rulings in force is accepted: Not yet accepted, tone
///   warn;</item>
///   <item>it has no set and the index does not say it is superseded: the
///   first word of its status line, tone neutral, with a "no decision set"
///   note. None is like this on main.</item>
/// </list>
/// The maintainer set this wording on #368 (2026-11-19). The badge reflects
/// the ADR's own set only. An unaccepted ruling of another
/// ADR that amends this one is a note, with the amendment's date.
/// </summary>
internal sealed partial class Ledger
{
    private readonly Dictionary<string, DecisionSet> _setByAdr = new(StringComparer.Ordinal);
    private readonly HashSet<string> _supersededInIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _statusByAdr = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string By, string? Date)>> _amendedBy = new(StringComparer.Ordinal);

    public static Ledger Read(Repository repository)
    {
        Ledger ledger = new();

        if (repository.IsDirectory("docs/decisions"))
        {
            foreach (string file in Directory.EnumerateFiles(repository.Full("docs/decisions"), "*.md").Order(StringComparer.Ordinal))
            {
                if (ParseSet(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)) is { Adr: { } adr } set)
                {
                    ledger._setByAdr[adr] = set;
                }
            }
        }

        if (repository.IsFile("docs/adr/README.md"))
        {
            foreach (Match row in IndexRow().Matches(repository.Read("docs/adr/README.md")))
            {
                if (IsWhollySuperseded(row.Groups[2].Value))
                {
                    ledger._supersededInIndex.Add(row.Groups[1].Value);
                }
            }
        }

        if (repository.IsDirectory("docs/adr"))
        {
            foreach (string file in Directory.EnumerateFiles(repository.Full("docs/adr"), "*.md").Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(file);

                if (NumberOf(name) is not { } number)
                {
                    continue;
                }

                string status = StatusSection(File.ReadAllText(file));
                ledger._statusByAdr[number] = status;

                foreach ((string amended, string? date) in Amends(status))
                {
                    if (amended == number)
                    {
                        continue;
                    }

                    if (!ledger._amendedBy.TryGetValue(amended, out List<(string, string?)>? list))
                    {
                        ledger._amendedBy[amended] = list = [];
                    }

                    list.Add((number, date));
                }
            }
        }

        return ledger;
    }

    /// <summary>The badge, set and notes of the ADR with this four-digit number.</summary>
    public AdrState StateOf(string number)
    {
        _setByAdr.TryGetValue(number, out DecisionSet? set);
        List<AmendmentNote> notes = [];

        foreach ((string by, string? date) in _amendedBy.GetValueOrDefault(number) ?? [])
        {
            if (_setByAdr.GetValueOrDefault(by) is { } other && Unaccepted(other) is > 0 and int count)
            {
                notes.Add(new AmendmentNote(by, date, count));
            }
        }

        Badge badge;
        List<Pending> pending = [];

        if (_supersededInIndex.Contains(number))
        {
            badge = new Badge(Tone.Neutral, "Superseded");
        }
        else if (set is not null)
        {
            List<Ruling> inForce = [.. set.Rulings.Where(static r => !r.Revoked)];
            int unaccepted = inForce.Count(static r => !r.Accepted);
            int accepted = inForce.Count - unaccepted;
            badge = unaccepted == 0 ? new Badge(Tone.Ok, "Accepted")
                : accepted == 0 ? new Badge(Tone.Warn, "Not yet accepted")
                : new Badge(Tone.Warn, $"Accepted · {unaccepted} pending");

            // Grouped by amendment, the dated ones in date order, then those no statement dates.
            pending = [.. inForce.Where(static r => !r.Accepted)
                .GroupBy(static r => r.Amended)
                .OrderBy(static g => g.Key is null)
                .ThenBy(static g => g.Key, StringComparer.Ordinal)
                .Select(static g => new Pending(g.Key, g.Count()))];
        }
        else
        {
            string first = FirstWord().Match(Plain(_statusByAdr.GetValueOrDefault(number) ?? "")) is { Success: true } m ? m.Value : "Unknown";
            badge = new Badge(Tone.Neutral, first);
        }

        return new AdrState(badge, set?.Name, notes, pending);
    }

    private static int Unaccepted(DecisionSet set) => set.Rulings.Count(static r => !r.Revoked && !r.Accepted);

    /// <summary>A decision set's front matter, read as the ledger's interim form writes it (docs/decisions/README.md).</summary>
    public static DecisionSet? ParseSet(string name, string text)
    {
        text = text.ReplaceLineEndings("\n");

        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        {
            return null;
        }

        int end = text.IndexOf("\n---", 4, StringComparison.Ordinal);

        if (end < 0)
        {
            return null;
        }

        string front = text[4..end];
        string? adr = AdrField().Match(front) is { Success: true } a ? a.Groups[1].Value : null;
        List<Ruling> rulings = [];

        foreach (string entry in front.Split("\n  - ", StringSplitOptions.None).Skip(1))
        {
            if (KeyField().Match(entry) is { Success: true } key)
            {
                string? amended = StatementAmended().Match(entry) is { Success: true } a2 ? a2.Groups[1].Value : null;
                rulings.Add(new Ruling(key.Groups[1].Value, AcceptedBy().IsMatch(entry), RevokedAt().IsMatch(entry), amended));
            }
        }

        return new DecisionSet(name, adr, rulings);
    }

    /// <summary>
    /// Whether an index Status cell says the ADR is superseded as a whole: one
    /// of its clauses, split at ';', starts "Superseded by". "Layer table
    /// superseded by 0060" names a part, and is not.
    /// </summary>
    public static bool IsWhollySuperseded(string cell) =>
        cell.Split(';').Any(static clause => Plain(clause).TrimStart().StartsWith("superseded by", StringComparison.OrdinalIgnoreCase));

    /// <summary>The ADR numbers an ADR's Status section says it amends, each with the amendment's date when the paragraph gives one.</summary>
    public static IEnumerable<(string Amended, string? Date)> Amends(string status)
    {
        foreach (string paragraph in status.ReplaceLineEndings("\n").Split("\n\n"))
        {
            if (!paragraph.Contains("**Amends**", StringComparison.Ordinal))
            {
                continue;
            }

            string? date = DateIn().Match(paragraph) is { Success: true } d ? d.Value : null;

            foreach (string amended in AdrLink().Matches(paragraph).Select(static m => m.Groups[1].Value).Distinct(StringComparer.Ordinal))
            {
                yield return (amended, date);
            }
        }
    }

    /// <summary>An ADR's Status section: the text between "## Status" and the next heading.</summary>
    public static string StatusSection(string adr)
    {
        adr = adr.ReplaceLineEndings("\n");
        int start = adr.IndexOf("\n## Status\n", StringComparison.Ordinal);

        if (start < 0)
        {
            return "";
        }

        start += "\n## Status\n".Length;
        int end = adr.IndexOf("\n## ", start, StringComparison.Ordinal);
        return (end < 0 ? adr[start..] : adr[start..end]).Trim();
    }

    public static string? NumberOf(string fileName) =>
        fileName.Length > 5 && char.IsAsciiDigit(fileName[0]) && fileName[4] == '-' && fileName[..4].All(char.IsAsciiDigit) ? fileName[..4] : null;

    private static string Plain(string markdown) => Emphasis().Replace(markdown, "");

    [GeneratedRegex(@"^\| \[(\d{4})\]\([^)]*\) \| .*? \| (.*) \|$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex IndexRow();

    [GeneratedRegex(@"^adr:\s*(\d{4})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AdrField();

    [GeneratedRegex(@"^key:\s*(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyField();

    [GeneratedRegex(@"^\s+accepted-by:\s*\S", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedBy();

    [GeneratedRegex(@"^\s+statement:\s*"".*\bamended (\d{4}-\d{2}-\d{2})""\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex StatementAmended();

    [GeneratedRegex(@"^\s+revoked-at:\s*\S", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex RevokedAt();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex DateIn();

    [GeneratedRegex(@"\]\((\d{4})-[^)]*\.md[^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex AdrLink();

    [GeneratedRegex(@"[*_`]", RegexOptions.CultureInvariant)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex FirstWord();
}
