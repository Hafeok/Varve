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
/// The guards on the entailment suites (ADR 0193, <c>sparql-evaluation.md</c>
/// §12.1): each manifest's count, the cases that run, and by name the ones
/// that do not — blocked as out of scope, in <c>baseline/blocked.txt</c>, or
/// pending a later part. A count that changes is looked at, not absorbed.
/// </summary>
public class EntailmentGuardTests
{
    private const string Entailment = "http://www.w3.org/2009/sparql/docs/tests/data-sparql11/entailment/manifest#";

    [Fact]
    public void Sparql11_entailment_lists_seventy_cases_and_ninety_five_run_under_rdf_rdfs_and_d()
    {
        if (!TestData.IsCheckedOut)
        {
            return;
        }

        Assert.Equal(70, EntailmentSuite.Entries.Length);
        Assert.Equal(95, EntailmentConformanceTests.Cases().Count());
    }

    [Fact]
    public void The_cases_waiting_for_a_later_part_are_named()
    {
        if (!TestData.IsCheckedOut)
        {
            return;
        }

        Assert.Equal(["lang", "plainLit", "sparqldl-13"], Named("pending: OWL 2 RL"));
        Assert.Equal(["paper-sparqldl-Q1", "paper-sparqldl-Q4"], Named("pending: OWL 2 QL"));
    }

    [Fact]
    public void The_blocked_cases_are_the_baselines_blocked_list()
    {
        if (!TestData.IsCheckedOut)
        {
            return;
        }

        List<string> computed = [];
        foreach (EntailmentEntry entry in EntailmentSuite.Entries)
        {
            if (EntailmentSuite.NotRun(entry) is { } reason && reason.StartsWith("blocked: ", StringComparison.Ordinal))
            {
                computed.Add(entry.Evaluation.TestIri + " " + reason["blocked: ".Length..]);
            }
        }

        computed.Sort(StringComparer.Ordinal);
        List<string> pinned = [.. File.ReadAllLines(TestData.ResolveFromRepository("tests/Varve.Conformance.Tests/baseline/blocked.txt")).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        Assert.Equal(computed, pinned);
        Assert.Equal(25, pinned.Count);
    }

    [Fact]
    public void Rdf_mt_lists_forty_eight_active_cases_and_its_ten_inconsistency_cases_wait_for_the_closure()
    {
        if (!TestData.IsCheckedOut)
        {
            return;
        }

        Assert.Equal(48, RdfMtSuite.Entries.Length);
        Assert.Equal(38, RdfMtConformanceTests.Cases().Count());
        List<string> pending = [.. RdfMtSuite.Entries.Where(e => RdfMtSuite.NotRun(e) is not null).Select(e => e.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(10, pending.Count);
    }

    private static List<string> Named(string prefix) =>
        [.. EntailmentSuite.Entries
            .Where(e => EntailmentSuite.NotRun(e) is { } reason && reason.StartsWith(prefix, StringComparison.Ordinal))
            .Select(e => e.Evaluation.TestIri[Entailment.Length..])
            .Order(StringComparer.Ordinal)];
}
