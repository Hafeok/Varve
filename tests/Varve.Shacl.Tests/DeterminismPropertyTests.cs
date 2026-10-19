// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>
/// ADR 0154 as a property: the same data, inserted in any order, gives the
/// same report, byte for byte, under the same blank node labels.
/// </summary>
public class DeterminismPropertyTests
{
    /// <summary>The number of generated cases each run checks, as reported in the pull request.</summary>
    internal const int Iterations = 1_000;

    private const string Shapes = """
        ex:S sh:targetClass ex:C ;
            sh:property [ sh:path ex:age ; sh:maxCount 1 ; sh:datatype xsd:integer ; sh:lessThan ex:limit ] ;
            sh:property [ sh:path ex:knows ; sh:class ex:C ; sh:minCount 1 ] ;
            sh:property [ sh:path ( ex:knows ex:age ) ; sh:maxInclusive 3 ] ;
            sh:property [ sh:path [ sh:inversePath ex:knows ] ; sh:nodeKind sh:IRI ] .
        ex:Named sh:targetSubjectsOf ex:name ;
            sh:property [ sh:path ex:name ; sh:datatype xsd:string ; sh:minLength 2 ] ;
            sh:node [ sh:property [ sh:path ex:knows ; sh:maxCount 1 ] ] .
        """;

    private static readonly RdfTerm[] Nodes =
    [
        Support.Ex("a"), Support.Ex("b"), Support.Ex("c"), Support.Ex("d"),
        RdfTerm.BlankNode("x"u8), RdfTerm.BlankNode("y"u8), RdfTerm.BlankNode("z"u8),
    ];

    private static readonly RdfTerm[] Predicates = [Support.Ex("age"), Support.Ex("limit"), Support.Ex("knows"), Support.Ex("name"), Support.Ex("type")];

    private static readonly RdfTerm Type = RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);

    private static readonly RdfTerm Integer = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8);

    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> Triples =
        Gen.Select(Gen.Int[0, Nodes.Length - 1], Gen.Int[0, Predicates.Length - 1], Gen.Int[0, 5], Gen.Int[0, Nodes.Length - 1])
            .Select((s, p, n, o) => Predicates[p].Equals(Support.Ex("type"))
                ? (Nodes[s], Type, Support.Ex("C"))
                : Predicates[p].Equals(Support.Ex("knows"))
                    ? (Nodes[s], Predicates[p], Nodes[o])
                    : Predicates[p].Equals(Support.Ex("name"))
                        ? (Nodes[s], Predicates[p], n % 2 == 0 ? RdfTerm.Literal(Encoding.UTF8.GetBytes(new string('n', n))) : RdfTerm.Literal(Encoding.UTF8.GetBytes(n.ToString(System.Globalization.CultureInfo.InvariantCulture)), Integer))
                        : (Nodes[s], Predicates[p], RdfTerm.Literal(Encoding.UTF8.GetBytes(n.ToString(System.Globalization.CultureInfo.InvariantCulture)), Integer)));

    [Fact]
    public void The_report_does_not_depend_on_the_order_the_data_was_inserted_in()
    {
        ShapesGraph shapes = ShapesGraph.Load(Support.Dataset(Shapes), GraphPattern.DefaultGraph, null, TestContext.Current.CancellationToken).Shapes!;
        Gen.Select(Triples.Array[0, 24], Gen.Int.Array[24]).Sample((triples, keys) =>
        {
            (RdfTerm, RdfTerm, RdfTerm)[] shuffled = (((RdfTerm, RdfTerm, RdfTerm)[])triples.Clone());
            int[] order = keys[..triples.Length];
            Array.Sort(order, shuffled);

            ValidationReport first = Validate(shapes, triples);
            ValidationReport second = Validate(shapes, shuffled);
            Assert.Equal(Encoding.UTF8.GetString(first.ToCanonicalNQuads().Span), Encoding.UTF8.GetString(second.ToCanonicalNQuads().Span));
            Assert.Equal(first.Results.Count, second.Results.Count);
            for (int i = 0; i < first.Results.Count; i++)
            {
                Assert.Equal(first.Results[i].FocusNode, second.Results[i].FocusNode);
                Assert.Equal(first.Results[i].SourceConstraintComponent, second.Results[i].SourceConstraintComponent);
                Assert.Equal(first.Results[i].Value, second.Results[i].Value);
            }
        }, iter: Iterations);
    }

    [Fact]
    public void Many_equal_results_canonicalise_within_the_work_limit()
    {
        // 400 sh:node references to shapes x fails: 400 results equal in every term but their own node.
        StringBuilder text = new("ex:S sh:targetNode ex:x");
        for (int i = 0; i < 400; i++)
        {
            text.Append(" ; sh:node ex:F").Append(i);
        }

        text.Append(" .\n");
        for (int i = 0; i < 400; i++)
        {
            text.Append("ex:F").Append(i).Append(" sh:class ex:Missing .\n");
        }

        Stopwatch time = Stopwatch.StartNew();
        ValidationReport report = Support.Validate(text.ToString(), "ex:x ex:p 1 .");
        string nquads = Encoding.UTF8.GetString(report.ToCanonicalNQuads().Span);
        time.Stop();

        Assert.Equal(400, report.Results.Count);
        Assert.Equal(400, nquads.Split("<http://www.w3.org/ns/shacl#result>").Length - 1);
        Assert.True(time.Elapsed < TimeSpan.FromSeconds(30), $"canonicalising 400 equal results took {time.Elapsed}");
    }

    private static ValidationReport Validate(ShapesGraph shapes, (RdfTerm S, RdfTerm P, RdfTerm O)[] triples)
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            builder.Add(s, p, o);
        }

        ValidationOutcome outcome = ShaclValidator.Validate(shapes, builder.ToDataset(), GraphPattern.DefaultGraph);
        Assert.True(outcome.Succeeded);
        return outcome.Report!;
    }
}
