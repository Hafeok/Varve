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

    /// <summary>
    /// The oracle's corpus is counted too, for the same reason.
    /// </summary>
    /// <remarks>
    /// A suite whose manifest silently stops being read would make the oracle
    /// pass by having nothing to disagree about. "More than zero" does not
    /// catch a suite read as nine cases instead of three hundred and thirteen.
    /// </remarks>
    public static TheoryData<string, int> ExpectedOracleCounts() => new()
    {
        { "rdf11/turtle", 313 },
        { "rdf11/trig", 357 },
    };

    [Theory]
    [MemberData(nameof(ExpectedOracleCounts))]
    public void Every_suite_the_oracle_reads_enumerates_the_cases_its_manifest_lists(
        string suiteId, int expected)
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        ConformanceSuite? suite = null;

        foreach (ConformanceSuite candidate in ConformanceSuite.OracleCorpus)
        {
            if (string.Equals(candidate.Id, suiteId, StringComparison.Ordinal))
            {
                suite = candidate;
            }
        }

        Assert.True(suite is not null, "No suite is wired with id '" + suiteId + "'.");

        Assert.Equal(expected, OracleCatalogue.Of(suite).Count);
    }

    /// <summary>
    /// Every evaluation entry has a result to compare against.
    /// </summary>
    /// <remarks>
    /// An evaluation test asserts that the parse produces a particular dataset,
    /// and the comparison is <see cref="Isomorphism"/>. This replaces the guard
    /// that kept such entries out of the ratchet while no comparison existed:
    /// what can still go wrong is an entry whose <c>mf:result</c> the reader
    /// fails to pick up, which would leave the runner asserting only that the
    /// action parsed — the weaker claim the earlier guard was there to prevent.
    /// </remarks>
    [Fact]
    public void Every_evaluation_entry_has_a_result_to_compare_against()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        List<string> unchecked_ = [];

        foreach (ManifestEntry entry in OracleCatalogue.Entries)
        {
            if (entry.Expected == ExpectedOutcome.Evaluates && entry.ResultPath is null)
            {
                unchecked_.Add(entry.TestIri);
            }
        }

        Assert.True(
            unchecked_.Count == 0,
            "These evaluation entries have no mf:result, so nothing would compare what they produce "
            + "against what they assert:\n  " + string.Join("\n  ", unchecked_));
    }

    /// <summary>
    /// Evaluation entries exist to be compared, so there had better be some.
    /// </summary>
    /// <remarks>
    /// The companion to the above: a reader that stopped recognising
    /// <c>rdft:TestTurtleEval</c> would satisfy it by having nothing to check.
    /// </remarks>
    [Fact]
    public void The_suites_contribute_evaluation_entries()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        int evaluations = 0;

        foreach (ManifestEntry entry in OracleCatalogue.Entries)
        {
            if (entry.Expected == ExpectedOutcome.Evaluates)
            {
                evaluations++;
            }
        }

        // Turtle 145 and TriG 143, as the manifests list them.
        Assert.Equal(288, evaluations);
    }

    /// <summary>
    /// Every format has a suite the chunk-boundary oracle reads.
    /// </summary>
    /// <remarks>
    /// The standing rule in <c>docs/testing.md</c> §2 is that every syntax
    /// package runs the oracle over its own manifests. A rule that is only
    /// written down is one a future format will skip without anyone noticing,
    /// so this is the enforcing half: adding a value to
    /// <see cref="RdfFormat"/> without wiring a suite for it fails here, and
    /// the message says what to do.
    /// </remarks>
    [Fact]
    public void Every_format_is_covered_by_the_chunk_boundary_oracle()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        List<string> uncovered = [];

        foreach (RdfFormat format in Enum.GetValues<RdfFormat>())
        {
            bool covered = false;

            foreach (ConformanceSuite suite in ConformanceSuite.OracleCorpus)
            {
                if (suite.Format == format)
                {
                    covered = true;
                }
            }

            if (!covered)
            {
                uncovered.Add(format.ToString());
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "These formats have no suite the chunk-boundary oracle can read, so nothing checks that "
            + "their reader gives the same answer however the input arrives (docs/testing.md \u00A72). "
            + "Add the suite to ConformanceSuite.All, or to NotYetRatcheted while its results are not "
            + "yet claimed: " + string.Join(", ", uncovered));
    }

    /// <summary>
    /// Every suite the oracle reads has at least one input for it to read.
    /// </summary>
    /// <remarks>
    /// The companion to the count guards: a suite wired with a manifest path
    /// that resolves to nothing would satisfy the format check above and give
    /// the oracle nothing to do.
    /// </remarks>
    [Fact]
    public void Every_suite_the_oracle_reads_has_entries()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see the first failure.");

        foreach (ConformanceSuite suite in ConformanceSuite.OracleCorpus)
        {
            Assert.True(
                OracleCatalogue.Of(suite).Count > 0,
                "Suite '" + suite.Id + "' contributed no entries to the oracle.");
        }
    }
}
