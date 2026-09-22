using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The harness's checks on itself.
/// </summary>
/// <remarks>
/// A clone without <c>--recurse-submodules</c> has an empty
/// <c>tests/w3c/rdf-tests</c>. Without these tests that enumerates zero cases
/// and the suite passes — the one failure mode that would make the conformance
/// gate worthless, because it is silent and it looks like success.
/// </remarks>
public class SubmoduleGuardTests
{
    [Fact]
    public void The_w3c_submodule_is_checked_out()
    {
        Assert.True(
            TestData.IsCheckedOut,
            "The W3C test data is missing at " + TestData.RdfTestsRoot
            + ". Run: git submodule update --init --recursive");
    }

    [Fact]
    public void Every_wired_suite_has_its_manifest()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the previous failure.");

        foreach (ConformanceSuite suite in ConformanceSuite.All)
        {
            string path = TestData.ResolveFromRoot(suite.ManifestPath);
            Assert.True(File.Exists(path), "Suite '" + suite.Id + "' has no manifest at " + path + ".");
        }
    }

    /// <summary>
    /// A manifest that parses to nothing is indistinguishable from a suite that
    /// passes, so an empty suite is a failure in its own right.
    /// </summary>
    [Fact]
    public void Every_wired_suite_enumerates_cases()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        foreach (ConformanceSuite suite in ConformanceSuite.All)
        {
            IReadOnlyList<ManifestEntry> entries = Catalogue.Of(suite);
            Assert.True(
                entries.Count > 0,
                "Suite '" + suite.Id + "' enumerated no cases. Either its manifest changed shape or the reader "
                + "stopped understanding it; both are harness defects, not parser results.");
        }
    }

    /// <summary>
    /// The number of cases each suite must enumerate, counted from the pinned
    /// submodule's manifests.
    /// </summary>
    /// <remarks>
    /// "More than zero" catches a manifest that stopped being read at all. It
    /// does not catch a manifest that is now read as nine cases instead of
    /// twenty-nine, which is what a subtly wrong reader produces — and a suite
    /// that quietly shrinks looks exactly like a suite that passes. These
    /// numbers are counted from the submodule at the revision it is pinned to;
    /// a change to either is a change to make deliberately, in the same commit
    /// that moves the pin.
    /// </remarks>
    public static TheoryData<string, int> ExpectedCounts() => new()
    {
        { "rdf11/n-triples", 70 },
        { "rdf11/n-quads", 87 },
        { "rdf12/n-triples", 29 },
        { "rdf12/n-quads", 27 },
    };

    [Theory]
    [MemberData(nameof(ExpectedCounts))]
    public void Every_wired_suite_enumerates_the_cases_its_manifest_lists(string suiteId, int expected)
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        ConformanceSuite? suite = null;

        foreach (ConformanceSuite candidate in ConformanceSuite.All)
        {
            if (string.Equals(candidate.Id, suiteId, StringComparison.Ordinal))
            {
                suite = candidate;
            }
        }

        Assert.True(suite is not null, "No suite is wired with id '" + suiteId + "'.");

        Assert.Equal(expected, Catalogue.Of(suite).Count);
    }

    /// <summary>
    /// Every wired suite is counted above. A suite added without a count would
    /// be guarded only by "more than zero", which is the guard this pair exists
    /// to strengthen.
    /// </summary>
    [Fact]
    public void Every_wired_suite_has_a_pinned_count()
    {
        HashSet<string> counted = new(StringComparer.Ordinal);

        foreach (TheoryDataRow<string, int> row in ExpectedCounts())
        {
            counted.Add(row.Data.Item1);
        }

        foreach (ConformanceSuite suite in ConformanceSuite.All)
        {
            Assert.Contains(suite.Id, counted);
        }
    }

    /// <summary>
    /// Entry IRIs are the baseline's identity. A duplicate would make one
    /// baseline line mean two tests.
    /// </summary>
    [Fact]
    public void Test_iris_are_unique_across_suites()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");
        Assert.Equal(Catalogue.Entries.Count, Catalogue.ByIri.Count);
    }
}
