// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>ADR 0150's semantics, one example per statement it makes.</summary>
public class RecursionTests
{
    private const string Knows = """
        ex:a a ex:Person ; ex:knows ex:b .
        ex:b a ex:Person ; ex:knows ex:a .
        ex:c a ex:Person ; ex:knows ex:d .
        ex:d ex:name "not a person" .
        """;

    [Fact]
    public void A_self_supporting_existential_cycle_conforms_under_the_greatest_fixpoint()
    {
        // ADR 0150's cost, shown: "a person knows some person who conforms".
        ValidationReport report = Support.Validate(
            """
            ex:PersonShape a sh:NodeShape ;
                sh:targetClass ex:Person ;
                sh:property [ sh:path ex:knows ; sh:someValue ex:PersonShape ] .
            ex:PersonShape sh:class ex:Person .
            """,
            Knows);

        // a and b support each other and conform; c knows d, who is no person.
        Assert.Equal(["c"], Support.Foci(report));
    }

    [Fact]
    public void A_universal_cycle_conforms_where_the_least_fixpoint_would_fail_it()
    {
        ValidationReport report = Support.Validate(
            """
            ex:PersonShape a sh:NodeShape ;
                sh:targetClass ex:Person ;
                sh:class ex:Person ;
                sh:property [ sh:path ex:knows ; sh:node ex:PersonShape ] .
            """,
            Knows);

        Assert.Equal(["c"], Support.Foci(report));
    }

    [Fact]
    public void Negation_through_recursion_is_refused_with_the_cycle_named()
    {
        ValidationOutcome outcome = Support.Outcome(
            """
            ex:A a sh:NodeShape ; sh:targetNode ex:x ; sh:not ex:B .
            ex:B a sh:NodeShape ; sh:node ex:A .
            """,
            "ex:x ex:p ex:y .");

        Assert.False(outcome.Succeeded);
        ShaclProblem problem = Assert.Single(outcome.Problems);
        Assert.Equal(ShaclProblemKind.UnstratifiableRecursion, problem.Kind);

        // A —sh:not→ B —sh:node→ A: shape, parameter, shape, … back to the start.
        IReadOnlyList<RdfTerm> cycle = problem.Cycle;
        Assert.Equal(5, cycle.Count);
        Assert.Equal(cycle[0], cycle[^1]);
        Assert.Contains(RdfTerm.Iri("http://www.w3.org/ns/shacl#not"u8), cycle);
    }

    [Fact]
    public void A_target_where_cycle_is_refused()
    {
        ValidationOutcome outcome = Support.Outcome(
            """
            ex:A a sh:NodeShape ; sh:targetWhere ex:A ; sh:class ex:C .
            """,
            "ex:x a ex:C .");

        Assert.False(outcome.Succeeded);
        Assert.Equal(ShaclProblemKind.UnstratifiableRecursion, Assert.Single(outcome.Problems).Kind);
    }

    [Fact]
    public void Xone_inside_a_cycle_is_refused_because_it_is_negative_too()
    {
        ValidationOutcome outcome = Support.Outcome(
            """
            ex:A a sh:NodeShape ; sh:targetNode ex:x ; sh:xone ( ex:A ex:B ) .
            ex:B a sh:NodeShape ; sh:class ex:C .
            """,
            "ex:x a ex:C .");

        Assert.Equal(ShaclProblemKind.UnstratifiableRecursion, Assert.Single(outcome.Problems).Kind);
    }

    [Fact]
    public void Negation_of_a_lower_stratum_is_allowed()
    {
        ValidationReport report = Support.Validate(
            """
            ex:Leaf a sh:NodeShape ; sh:property [ sh:path ex:child ; sh:maxCount 0 ] .
            ex:Tree a sh:NodeShape ;
                sh:targetNode ex:root ;
                sh:or ( ex:Leaf [ sh:not ex:Leaf ; sh:property [ sh:path ex:child ; sh:node ex:Tree ] ] ) .
            """,
            """
            ex:root ex:child ex:n1 .
            ex:n1 ex:child ex:n2 .
            """);

        Assert.True(report.Conforms);
    }
}
