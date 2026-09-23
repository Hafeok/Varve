// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using RepoStandard.Json;

namespace RepoStandard.Engine;

/// <summary>Plans and drift, as text for a terminal and as Markdown for a step summary.</summary>
internal static class Report
{
    /// <summary>The symbol for an action: + create, ~ update, - delete, ! cannot change.</summary>
    public static string Symbol(ChangeAction action) => action switch
    {
        ChangeAction.Create => "+",
        ChangeAction.Update => "~",
        ChangeAction.Delete => "-",
        _ => "!",
    };

    /// <summary>One line naming a change.</summary>
    public static string Line(Change change) => $"{Symbol(change.Action)} {change.Kind} {change.Target}";

    /// <summary>The plan as text: every change with its fields, then the counts.</summary>
    public static string Text(IReadOnlyList<Change> changes)
    {
        StringBuilder builder = new();
        foreach (Change change in changes)
        {
            builder.Append(Line(change)).Append('\n');
            foreach (FieldChange field in change.Fields)
            {
                builder.Append("    ").Append(field.Path).Append(": ")
                    .Append(JsonTree.Display(field.Live)).Append(" -> ").Append(JsonTree.Display(field.Declared)).Append('\n');
            }

            if (change.Note is not null)
            {
                builder.Append("    note: ").Append(change.Note).Append('\n');
            }
        }

        builder.Append(Summary(changes)).Append('\n');
        return builder.ToString();
    }

    /// <summary>One sentence of counts.</summary>
    public static string Summary(IReadOnlyList<Change> changes)
    {
        if (changes.Count == 0)
        {
            return "No differences: the repository matches the declaration.";
        }

        int Count(ChangeAction action) => changes.Count(c => c.Action == action);
        return $"{changes.Count} difference{(changes.Count == 1 ? string.Empty : "s")}: "
            + $"{Count(ChangeAction.Create)} to create, {Count(ChangeAction.Update)} to update, "
            + $"{Count(ChangeAction.Delete)} to delete, {Count(ChangeAction.Unfixable)} repo-standard cannot change.";
    }

    /// <summary>The drift report as Markdown.</summary>
    public static string Markdown(string repository, IReadOnlyList<Change> changes)
    {
        StringBuilder builder = new();
        builder.Append("## repo-standard check: `").Append(repository).Append("`\n\n");
        builder.Append(changes.Count == 0 ? "No drift. " : "**Drift.** ").Append(Summary(changes)).Append('\n');

        if (changes.Count == 0)
        {
            return builder.ToString();
        }

        builder.Append("\n| | Resource | Detail |\n|---|---|---|\n");
        foreach (Change change in changes)
        {
            List<string> details = [.. change.Fields.Select(f =>
                $"`{f.Path}`: {Cell(JsonTree.Display(f.Live))} → {Cell(JsonTree.Display(f.Declared))}")];
            if (change.Note is not null)
            {
                details.Add(Cell(change.Note));
            }

            builder.Append("| `").Append(Symbol(change.Action)).Append("` | ")
                .Append(Cell($"{change.Kind} {change.Target}")).Append(" | ")
                .Append(string.Join("<br>", details)).Append(" |\n");
        }

        builder.Append("\n`+` the declaration names it and the repository lacks it · `~` both have it and they differ · ")
            .Append("`-` the repository has it and the declaration does not name it · `!` a difference repo-standard reports and cannot write\n");
        return builder.ToString();
    }

    private static string Cell(string text) =>
        text.Replace("|", "\\|", System.StringComparison.Ordinal).Replace("\n", " ", System.StringComparison.Ordinal);
}
