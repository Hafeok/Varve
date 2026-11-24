// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Inference;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Xsd;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>sparql11/entailment</c> under OWL 2 RDF-Based semantics in the RL
/// profile, by the closure (ADR 0197 point 3): each case that lists that
/// regime and profile, its data closed under <c>varve:OWL2RL</c> with every
/// datatype of Varve.Xsd's map recognised, the closure's servable facts
/// added to its default graph, and its query evaluated over that with no
/// rewriting, as <c>Varve-Entailment: owl2-rl</c> answers. One ratchet line
/// per case, <c>&lt;test IRI&gt;@rl</c>. A case whose data names
/// <c>owl:sameAs</c> waits for part 3's <c>sameAs</c> classes (ADR 0197
/// point 4).
/// </summary>
public class EntailmentClosureTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EntailmentEntry entry in EntailmentSuite.Entries)
        {
            if (EntailmentSuite.UnderRl(entry) && EntailmentSuite.WaitsForSameAs(entry) is null)
            {
                string id = entry.Evaluation.TestIri + "@rl";
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        EntailmentEntry entry = EntailmentSuite.ByIri[testIri[..testIri.LastIndexOf('@')]];
        string? failure = await RunAsync(entry.Evaluation);
        Assert.True(failure is null, entry.Evaluation.Name + " (OWL 2 RL): " + failure);
    }

    /// <summary>Every datatype of Varve.Xsd's map and <c>rdf:langString</c>, as the projection's terms recognise them under OWL 2 RL.</summary>
    internal static IReadOnlyList<RdfTerm> RlDatatypes()
    {
        List<RdfTerm> datatypes = [RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8)];
        foreach (XsdDatatype datatype in Enum.GetValues<XsdDatatype>())
        {
            if (datatype != XsdDatatype.None)
            {
                datatypes.Add(RdfTerm.Iri(XsdDatatypes.Iri(datatype)));
            }
        }

        return datatypes;
    }

    private static async Task<string?> RunAsync(EvaluationEntry entry)
    {
        Query query = EvaluationRunner.ParseQuery(entry);
        List<DataQuad> data = [];
        foreach (string iri in entry.Data)
        {
            data.AddRange(EvaluationData.Quads(iri));
        }

        ClosureTerms terms = new(RlDatatypes());
        Closure closure = new(InferenceProfile.Owl2Rl, terms);
        closure.Apply([.. data.Select(q => new Fact(terms.Handle(q.Subject), terms.Handle(q.Predicate), terms.Handle(q.Object))).Distinct()], []);
        if (closure.Clashes() is { Count: > 0 } clashes)
        {
            return "the data is inconsistent: " + string.Join(", ", clashes.Select(c => Encoding.UTF8.GetString(c.Rule.Lexical)));
        }

        List<DataQuad> closed = [.. data];
        foreach (Fact fact in closure.Implicit)
        {
            RdfTerm subject = terms.Term(fact.Subject), predicate = terms.Term(fact.Predicate);
            if (subject.Kind != RdfTermKind.Literal && predicate.Kind == RdfTermKind.Iri)
            {
                closed.Add(new DataQuad(subject, predicate, terms.Term(fact.Object), null));
            }
        }

        List<(IReadOnlyList<DataQuad>, RdfTerm?)> graphs = [(closed, null)];
        foreach (string iri in entry.GraphData)
        {
            graphs.Add((EvaluationData.Quads(iri), RdfTerm.Iri(Encoding.UTF8.GetBytes(iri))));
        }

        await using LoadedSource loaded = await EvaluationSubjects.Dataset.LoadAsync(graphs);
        using QueryResults results = new SparqlEvaluator(EntailmentFunctions.Options).Evaluate(query, loaded.Source, TestContext.Current.CancellationToken);
        return EvaluationRunner.Compare(entry, query, results, null);
    }
}
