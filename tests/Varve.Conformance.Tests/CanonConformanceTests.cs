// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Varve.Rdf;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The W3C RDFC-1.0 suite (<c>rdf-canon.md</c> §7): one ratchet line per
/// case, named by its test IRI; and its guards.
/// </summary>
public class CanonConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCanonCheckedOut)
        {
            yield break;
        }

        foreach (CanonEntry entry in CanonCatalogue.Entries)
        {
            yield return new TheoryDataRow<string>(entry.TestIri) { TestDisplayName = entry.TestIri };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string testIri)
    {
        CanonEntry entry = CanonCatalogue.ByIri[testIri];
        string? failure = CanonRunner.Run(entry);
        Assert.True(failure is null, entry.Name + ": " + failure);
    }

    [Fact]
    public void The_rdf_canon_submodule_is_checked_out() =>
        Assert.True(
            TestData.IsCanonCheckedOut,
            "tests/w3c/rdf-canon is empty. Run: git submodule update --init --recursive");

    /// <summary>The cases the manifest lists, by kind, pinned; SHA-384 selected where the manifest says.</summary>
    [Fact]
    public void The_suite_enumerates_the_cases_its_manifest_lists()
    {
        Assert.True(TestData.IsCanonCheckedOut, "The rdf-canon test data is missing; see the previous test.");
        IReadOnlyList<CanonEntry> entries = CanonCatalogue.Entries;
        Assert.Equal(64, entries.Count(e => e.Kind == CanonKind.Eval));
        Assert.Equal(21, entries.Count(e => e.Kind == CanonKind.Map));
        Assert.Equal(1, entries.Count(e => e.Kind == CanonKind.Negative));
        Assert.Equal(2, entries.Count(e => e.Hash == System.Security.Cryptography.HashAlgorithmName.SHA384));
        Assert.Equal(entries.Count, entries.Select(e => e.TestIri).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The default work limit is measured, not guessed (ADR 0059): the least
    /// limit each positive case needs, found by search, and the default at
    /// least three times the worst of them, so that no case the suite calls
    /// computable is near it. The poison case meets the default (the suite
    /// asserts that), in tens of milliseconds.
    /// </summary>
    [Fact]
    public void The_default_work_limit_leaves_a_margin_over_every_positive_case()
    {
        Assert.True(TestData.IsCanonCheckedOut, "The rdf-canon test data is missing; see the previous test.");
        int worst = 0;
        string worstCase = "";

        foreach (CanonEntry entry in CanonCatalogue.Entries.Where(e => e.Kind != CanonKind.Negative))
        {
            int least = 1;

            while (CanonRunner.Run(entry, new CanonicalisationOptions { HashAlgorithm = entry.Hash, WorkLimit = least }) is { } failure)
            {
                Assert.Contains("met the work limit", failure, StringComparison.Ordinal);
                least++;
            }

            if (least > worst)
            {
                (worst, worstCase) = (least, entry.TestIri);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("worst: " + worstCase + " needs " + worst);
        Assert.True(worst * 3 <= CanonicalisationOptions.Default.WorkLimit, worstCase + " needs " + worst + ", within a third of the default " + CanonicalisationOptions.Default.WorkLimit);
    }
}
