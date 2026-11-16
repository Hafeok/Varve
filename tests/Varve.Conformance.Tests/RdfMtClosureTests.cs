// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Inference;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>rdf11/rdf-mt</c>'s inconsistency cases on the closure (ADR 0197 point
/// 2): each case whose result is <c>false</c> asks whether its action is
/// inconsistent under its regime with the datatypes it recognises. The
/// action is closed under the RDFS profile, recognising the case's
/// datatypes; a positive case finds a clash, a negative one none. One
/// ratchet line per case, <c>&lt;test IRI&gt;@closure</c>.
/// </summary>
public class RdfMtClosureTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (RdfMtEntry entry in RdfMtSuite.Entries)
        {
            if (entry.Result is null)
            {
                string id = entry.TestIri + "@closure";
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case(string testIri)
    {
        RdfMtEntry entry = RdfMtSuite.ByIri[testIri[..testIri.LastIndexOf('@')]];
        ClosureTerms terms = new(entry.Recognised);
        Closure closure = new(InferenceProfile.Rdfs, terms);
        List<Fact> facts = [];
        foreach (DataQuad quad in EvaluationData.Quads(entry.Action))
        {
            facts.Add(new Fact(terms.Handle(quad.Subject), terms.Handle(quad.Predicate), terms.Handle(quad.Object)));
        }

        closure.Apply([.. facts.Distinct()], []);
        IReadOnlyList<Clash> clashes = closure.Clashes();
        Assert.True(
            (clashes.Count > 0) == entry.Positive,
            entry.Name + ": the action is " + (clashes.Count > 0 ? "inconsistent (" + string.Join(", ", clashes.Select(c => Encoding.UTF8.GetString(c.Rule.Lexical))) + ")" : "consistent") + " under " + entry.Regime);
    }
}
