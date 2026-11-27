// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Varve.Store.Log;

namespace Varve.Learn;

/// <summary>A refused commit's findings as a table: the samples' own formatting of the store's report, not the store's.</summary>
internal static class Findings
{
    private static readonly ImmutableArray<string> Columns = ["focusNode", "path", "value", "component"];

    /// <summary>Prints the findings of <paramref name="result"/>'s shapes report, one row each.</summary>
    internal static void Show(CommitResult result)
    {
        using JsonDocument report = JsonDocument.Parse(result.Report[1].Lexical.ToArray());
        List<string[]> rows = [];
        foreach (JsonElement finding in report.RootElement.GetProperty("findings").EnumerateArray())
        {
            rows.Add([.. Columns.Select(m => Learn.Show.Written(Member(finding, m)))]);
        }

        Learn.Show.Table(Columns, rows);
    }

    private static string Member(JsonElement finding, string name) =>
        finding.TryGetProperty(name, out JsonElement value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText() : "";
}
