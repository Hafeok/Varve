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
