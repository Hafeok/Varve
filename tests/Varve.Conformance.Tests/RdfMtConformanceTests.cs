// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>rdf11/rdf-mt</c> on the rewriting (ADR 0193): each case's result graph
/// as an <c>ASK</c>, its blank nodes the query's, rewritten under the case's
/// regime recognising its datatypes and evaluated over its action graph; a
/// positive case is true, a negative one false. One ratchet line per case,
/// <c>&lt;test IRI&gt;@rewriting</c>.
/// </summary>
public class RdfMtConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (RdfMtEntry entry in RdfMtSuite.Entries)
        {
            if (RdfMtSuite.NotRun(entry) is null)
            {
                string id = entry.TestIri + "@rewriting";
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        RdfMtEntry entry = RdfMtSuite.ByIri[testIri[..testIri.LastIndexOf('@')]];
        List<TriplePattern> patterns = [];
        foreach (DataQuad quad in EvaluationData.Quads(entry.Result!))
        {
            patterns.Add(new TriplePattern(Pattern(quad.Subject), Pattern(quad.Predicate), Pattern(quad.Object)));
        }

        AskQuery ask = new(new Prologue(null, AlgebraList.Empty<PrefixDeclaration>(), SparqlVersion.Sparql11), null, new Bgp(AlgebraList.From(patterns)));
        await using LoadedSource loaded = await EvaluationSubjects.Dataset.LoadAsync([(EvaluationData.Quads(entry.Action), null)]);
        EntailmentRegime regime = entry.Regime switch
        {
            "simple" => EntailmentRegime.Simple,
            "RDF" => EntailmentRegime.Rdf,
            _ => EntailmentRegime.Rdfs,
        };

        EntailedQuery entailed = EntailmentRewriter.Rewrite(ask, loaded.Source, [], regime, entry.Recognised, TestContext.Current.CancellationToken);
        using QueryResults results = new SparqlEvaluator(EntailmentFunctions.Options).Evaluate(entailed.Query, entailed.Source, TestContext.Current.CancellationToken);
        bool entailedResult = ((BooleanResult)results).Value;
        Assert.True(entailedResult == entry.Positive, entry.Name + ": the action " + (entailedResult ? "entails" : "does not entail") + " the result under " + entry.Regime);
    }

    private static PatternTerm Pattern(RdfTerm term) =>
        term.Kind == RdfTermKind.BlankNode ? new BlankNodePattern(System.Text.Encoding.UTF8.GetString(term.Lexical)) : new TermPattern(term);
}
