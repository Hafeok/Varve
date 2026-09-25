// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Sparql.Evaluation;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The guards on the evaluation suites: each wired suite enumerates the cases
/// its manifest lists — a count that changes is looked at, not absorbed — and
/// the cases blocked on RDF 1.2 Turtle are pinned, by count and by suite, with
/// the slice that unblocks them named.
/// </summary>
public class EvaluationGuardTests
{
    /// <summary>Per suite: the query evaluation cases its manifest lists, and how many of them are blocked.</summary>
    public static TheoryData<string, int, int> ExpectedCounts() => new()
    {
        { "sparql10/basic", 27, 0 },
        { "sparql10/triple-match", 4, 0 },
        { "sparql10/open-world", 18, 0 },
        { "sparql10/algebra", 14, 0 },
        { "sparql10/bnode-coreference", 1, 0 },
        { "sparql10/optional", 7, 0 },
        { "sparql10/optional-filter", 5, 0 },
        { "sparql10/graph", 17, 0 },
        { "sparql10/dataset", 12, 0 },
        { "sparql10/type-promotion", 30, 0 },
        { "sparql10/cast", 7, 0 },
        { "sparql10/boolean-effective-value", 7, 0 },
        { "sparql10/bound", 1, 0 },
        { "sparql10/expr-builtin", 25, 0 },
        { "sparql10/expr-ops", 18, 0 },
        { "sparql10/expr-equals", 15, 0 },
        { "sparql10/regex", 21, 0 },
        { "sparql10/i18n", 5, 0 },
        { "sparql10/construct", 5, 0 },
        { "sparql10/ask", 4, 0 },
        { "sparql10/distinct", 11, 0 },
        { "sparql10/sort", 14, 0 },
        { "sparql10/solution-seq", 13, 0 },
        { "sparql10/reduced", 2, 0 },
        { "sparql11/aggregates", 42, 0 },
        { "sparql11/bind", 10, 0 },
        { "sparql11/bindings", 11, 0 },
        { "sparql11/cast", 6, 0 },
        { "sparql11/construct", 5, 0 },
        { "sparql11/csv-tsv-res", 3, 0 },
        { "sparql11/exists", 6, 0 },
        { "sparql11/functions", 75, 0 },
        { "sparql11/grouping", 4, 0 },
        { "sparql11/json-res", 4, 0 },
        { "sparql11/negation", 12, 0 },
        { "sparql11/project-expression", 7, 0 },
        { "sparql11/property-path", 33, 0 },
        { "sparql11/service", 7, 0 },
        { "sparql11/subquery", 14, 0 },
        { "sparql12/codepoint-escapes", 5, 0 },
        { "sparql12/eval-triple-terms", 38, 37 },
        { "sparql12/expression", 5, 0 },
        { "sparql12/grouping", 2, 0 },
        { "sparql12/lang-basedir", 10, 4 },
        { "sparql12/rdf11", 3, 0 },
    };

    [Fact]
    public void Every_wired_evaluation_suite_has_a_manifest()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        foreach (EvaluationSuite suite in EvaluationSuite.All)
        {
            Assert.True(File.Exists(TestData.ResolveFromRoot(suite.ManifestPath)), "Suite '" + suite.Id + "' has no manifest.");
        }
    }

    [Fact]
    public void Every_wired_evaluation_suite_enumerates_the_cases_its_manifest_lists()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        Dictionary<string, (int Cases, int Blocked)> expected = [];
        foreach (TheoryDataRow<string, int, int> row in ExpectedCounts())
        {
            expected[row.Data.Item1] = (row.Data.Item2, row.Data.Item3);
        }

        List<string> wrong = [];
        foreach (EvaluationSuite suite in EvaluationSuite.All)
        {
            IReadOnlyList<EvaluationEntry> entries = EvaluationCatalogue.Of(suite);
            int blocked = entries.Count(EvaluationCatalogue.IsBlocked);
            if (!expected.TryGetValue(suite.Id, out (int Cases, int Blocked) pinned) || pinned != (entries.Count, blocked))
            {
                wrong.Add($"        {{ \"{suite.Id}\", {entries.Count}, {blocked} }},");
            }
        }

        Assert.True(wrong.Count == 0, "Suites whose counts are not the pinned ones (the lines as measured):\n" + string.Join("\n", wrong));
    }

    /// <summary>
    /// The blocked cases, all of whose data is RDF 1.2 Turtle or TriG that
    /// turtle.md §9 refuses. The slice that unblocks them is the roadmap's 6b —
    /// RDF 1.2 Turtle and TriG, with RDF/XML and JSON-LD — before milestone 7.
    /// </summary>
    [Fact]
    public void The_cases_blocked_on_rdf_12_turtle_are_the_pinned_ones()
    {
        IReadOnlyList<EvaluationEntry> blocked = EvaluationCatalogue.Blocked;
        Assert.All(blocked, entry => Assert.StartsWith("sparql12/", entry.Suite, StringComparison.Ordinal));
        Assert.True(blocked.Count == BlockedCount, $"{blocked.Count} cases are blocked on RDF 1.2 Turtle (turtle.md §9; unblocked by roadmap slice 6b), pinned {BlockedCount}:\n"
            + string.Join("\n", blocked.Select(e => e.TestIri + " — " + string.Join("; ", EvaluationData.RefusedFiles(e)))));
    }

    private const int BlockedCount = 41;

    /// <summary>
    /// Every RDF/XML translation in <c>tests/fixtures/w3c-rdfxml/</c> was made
    /// from the original now in the submodule, and none is left over from a
    /// file that is gone (ADR 0027's dated note).
    /// </summary>
    [Fact]
    public void Every_rdfxml_translation_matches_its_original()
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        string[] translations = Directory.GetFiles(EvaluationData.FixtureRoot, "*.rdf.nt", SearchOption.AllDirectories);
        Assert.NotEmpty(translations);
        foreach (string translation in translations)
        {
            string relative = Path.GetRelativePath(EvaluationData.FixtureRoot, translation)[..^".nt".Length];
            string original = Path.Combine(TestData.RdfTestsRoot, relative);
            Assert.True(File.Exists(original), "A translation with no original: " + relative);
            Assert.Null(EvaluationData.Translation(original).Problem);
        }
    }

    /// <summary>
    /// ADR 0050's other two arms answer every case the default arm does, over
    /// the store, whose handles are the ones the arms differ on. The ratchet
    /// carries the default arm case by case; this gates the others whole. The
    /// first run of ADR 0050's suite time found the materialised arm failing
    /// 25 cases — every join through a blank node, whose label the store does
    /// not internalise back to a handle — which no test had run.
    /// </summary>
    [Theory]
    [InlineData(ValueAccess.Externalise)]
    [InlineData(ValueAccess.Materialise)]
    public async Task Every_arm_passes_every_case_over_the_store(ValueAccess arm)
    {
        Assert.True(TestData.IsCheckedOut, "The W3C test data is missing; see SubmoduleGuardTests.");
        List<string> failures = [];
        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries)
        {
            if (!EvaluationCatalogue.IsBlocked(entry)
                && await EvaluationRunner.RunAsync(entry, EvaluationSubjects.Store, arm, null, TestContext.Current.CancellationToken) is { } failure)
            {
                failures.Add(entry.TestIri + ": " + failure.Split('\n')[0]);
            }
        }

        Assert.True(failures.Count == 0, arm + " fails " + failures.Count + " cases:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Every_evaluation_case_iri_is_unique()
    {
        IReadOnlyList<EvaluationEntry> entries = EvaluationCatalogue.Entries;
        Assert.Equal(entries.Count, entries.Select(e => e.TestIri).Distinct(StringComparer.Ordinal).Count());
    }
}
