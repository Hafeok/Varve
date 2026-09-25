// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The guards on the update evaluation suites: each wired suite enumerates the
/// cases its manifest lists, pinned, so that a manifest read short is a
/// failure rather than a smaller suite that passes.
/// </summary>
public class UpdateGuardTests
{
    /// <summary>Per suite, the update evaluation cases its manifest lists.</summary>
    public static TheoryData<string, int> ExpectedCounts() => new()
    {
        { "sparql11/basic-update", 13 },
        { "sparql11/clear", 4 },
        { "sparql11/delete", 19 },
        { "sparql11/delete-data", 6 },
        { "sparql11/delete-insert", 9 },
        { "sparql11/delete-where", 6 },
        { "sparql11/drop", 4 },
        { "sparql11/add", 8 },
        { "sparql11/copy", 6 },
        { "sparql11/move", 6 },
        { "sparql11/update-silent", 13 },
    };

    [Fact]
    public void Every_wired_update_suite_has_a_manifest()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        foreach (UpdateSuite suite in UpdateSuite.All)
        {
            Assert.True(File.Exists(TestData.ResolveFromRoot(suite.ManifestPath)), "Suite '" + suite.Id + "' has no manifest.");
        }
    }

    [Fact]
    public void Every_wired_update_suite_enumerates_the_cases_its_manifest_lists()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Dictionary<string, int> expected = [];
        foreach (TheoryDataRow<string, int> row in ExpectedCounts())
        {
            expected[row.Data.Item1] = row.Data.Item2;
        }

        List<string> wrong = [];
        foreach (UpdateSuite suite in UpdateSuite.All)
        {
            int count = UpdateCatalogue.Of(suite).Count;
            if (!expected.TryGetValue(suite.Id, out int pinned) || pinned != count)
            {
                wrong.Add($"        {{ \"{suite.Id}\", {count} }},");
            }
        }

        Assert.True(wrong.Count == 0, "Suites whose counts are not the pinned ones (the lines as measured):\n" + string.Join("\n", wrong));
        Assert.Equal(94, UpdateCatalogue.Entries.Count);
    }

    [Fact]
    public void Every_update_case_iri_is_unique()
    {
        IReadOnlyList<UpdateEntry> entries = UpdateCatalogue.Entries;
        Assert.Equal(entries.Count, entries.Select(e => e.TestIri).Distinct(StringComparer.Ordinal).Count());
    }
}
