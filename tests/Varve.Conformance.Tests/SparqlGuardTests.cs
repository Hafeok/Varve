// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The guards <c>docs/testing.md</c> §1 asks of every suite, for the SPARQL
/// ones: pinned case counts, unique identities, and the one test that pins
/// the version mechanism itself.
/// </summary>
public class SparqlGuardTests
{
    /// <summary>
    /// The number of syntax cases each suite must enumerate, counted from the
    /// pinned submodule's manifests. The 1.2 manifests that mix syntax and
    /// evaluation entries count their syntax entries only.
    /// </summary>
    public static TheoryData<string, int> ExpectedCounts() => new()
    {
        { "sparql10/syntax-sparql1", 81 },
        { "sparql10/syntax-sparql2", 53 },
        { "sparql10/syntax-sparql3", 51 },
        { "sparql10/syntax-sparql4", 12 },
        { "sparql10/syntax-sparql5", 2 },
        { "sparql11/syntax-query", 94 },
        { "sparql11/syntax-update-1", 54 },
        { "sparql11/syntax-update-2", 1 },
        { "sparql11/syntax-fed", 3 },
        { "sparql12/syntax-triple-terms-positive", 113 },
        { "sparql12/syntax-triple-terms-negative", 65 },
        { "sparql12/syntax", 6 },
        { "sparql12/version", 9 },
        { "sparql12/codepoint-escapes", 9 },
        { "sparql12/lang-basedir", 1 },
    };

    [Fact]
    public void Every_wired_sparql_suite_has_a_manifest()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");

        foreach (SparqlSuite suite in SparqlSuite.All)
        {
            string path = TestData.ResolveFromRoot(suite.ManifestPath);
            Assert.True(File.Exists(path), "Suite '" + suite.Id + "' has no manifest at " + path + ".");
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedCounts))]
    public void Every_wired_sparql_suite_enumerates_the_cases_its_manifest_lists(string suiteId, int expected)
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");

        SparqlSuite? suite = null;

        foreach (SparqlSuite candidate in SparqlSuite.All)
        {
            if (string.Equals(candidate.Id, suiteId, StringComparison.Ordinal))
            {
                suite = candidate;
            }
        }

        Assert.True(suite is not null, "No SPARQL suite is wired with id '" + suiteId + "'.");
        Assert.Equal(expected, SparqlCatalogue.Of(suite).Count);
    }

    [Fact]
    public void Every_wired_sparql_suite_has_a_pinned_count()
    {
        HashSet<string> counted = new(StringComparer.Ordinal);

        foreach (TheoryDataRow<string, int> row in ExpectedCounts())
        {
            counted.Add(row.Data.Item1);
        }

        foreach (SparqlSuite suite in SparqlSuite.All)
        {
            Assert.Contains(suite.Id, counted);
        }
    }

    [Fact]
    public void The_sparql_suites_total_554_cases_and_every_iri_is_unique_across_all_catalogues()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Assert.Equal(554, SparqlCatalogue.Entries.Count);
        Assert.Equal(SparqlCatalogue.Entries.Count, SparqlCatalogue.ByIri.Count);

        foreach (SparqlManifestEntry entry in SparqlCatalogue.Entries)
        {
            Assert.False(Catalogue.ByIri.ContainsKey(entry.TestIri), "A SPARQL case shares its IRI with an RDF case: " + entry.TestIri);
        }
    }

    /// <summary>
    /// The version mechanism, pinned by the suites themselves: at least one
    /// 1.2 positive case is a 1.1 negative one, and it is refused under 1.1
    /// with an error that names the version.
    /// </summary>
    [Fact]
    public void A_case_positive_under_1_2_exists_that_is_negative_under_1_1()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        ISparqlSubject subject = SparqlSubjects.Current!;
        int refused = 0;

        foreach (SparqlManifestEntry entry in SparqlCatalogue.Entries)
        {
            if (entry.Version != SparqlVersion.Sparql12 || !entry.MustParse)
            {
                continue;
            }

            Assert.True(subject.Parse(entry, asUtf16: false).Succeeded, entry.TestIri + " must parse under 1.2.");

            SparqlOutcome under11 = subject.Parse(entry with { Version = SparqlVersion.Sparql11 }, asUtf16: false);

            if (!under11.Succeeded)
            {
                Assert.Equal(SparqlErrorKind.Version, under11.Error.Kind);
                refused++;
            }
        }

        Assert.True(refused > 0, "No 1.2 positive case is refused under 1.1, so nothing exercises the version gate.");
    }
}
