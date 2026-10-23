// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Sparql.Tests;

/// <summary>
/// ADR 0171 §4 as a property: SPARQL-based constraints and an ASK-validated
/// component, analysable and not, over generated histories of commits — the
/// whole report before a commit, without the comparison's results before and
/// with its results after, has the same results as a full validation after
/// it — and ADR 0169's refused functions.
/// </summary>
public class IncrementalSparqlTests
{
    /// <summary>Generated histories per run on the land gate, as reported in the pull request.</summary>
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 300 : 3_000;

    private static readonly string[] Queries =
    [
        "SELECT $this ?value WHERE { $this ex:p0 ?value . FILTER NOT EXISTS { ?value a ex:C0 } }",
        "SELECT $this WHERE { $this ex:p1+ ?x . ?x a ex:C1 }",
        "SELECT $this ?value WHERE { $this ex:p0 ?value . OPTIONAL { ?value ex:p1 ?y } FILTER(!BOUND(?y)) }",
        "SELECT $this WHERE { { SELECT $this (COUNT(?v) AS ?n) WHERE { $this ex:p2 ?v } GROUP BY $this } FILTER(?n > 1) }",
        "SELECT $this WHERE { ?a ex:p2 ex:n0 . FILTER EXISTS { $this ex:p0 ?b } }",
        "SELECT $this ?value WHERE { $this ^ex:p1/ex:p0 ?value . ?value ex:p2 ?q }",
        "SELECT $this WHERE { $this ex:p0 ?x . { SELECT ?x WHERE { ?x ex:p1 ?z } } }",
        "SELECT $this ?value WHERE { $this ?p ?value . FILTER(?p = ex:p2) }",
        "SELECT $this WHERE { $this a/rdfs:subClassOf* ex:C2 . FILTER NOT EXISTS { $this ex:p1 ?any } }",
    ];

    private static readonly string[] Nodes = ["ex:n0", "ex:n1", "ex:n2", "ex:n3"];
    private static readonly string[] Classes = ["ex:C0", "ex:C1", "ex:C2"];
    private static readonly string[] Predicates = ["ex:p0", "ex:p1", "ex:p2"];

    private static readonly Gen<string> Target = Gen.OneOf(
        Gen.OneOfConst(Nodes).Select(n => "sh:targetNode " + n),
        Gen.OneOfConst(Classes).Select(c => "sh:targetClass " + c),
        Gen.OneOfConst(Predicates).Select(p => "sh:targetSubjectsOf " + p));

    private static readonly Gen<string> Shapes =
        from targets in Target.Array[3]
        from queries in Gen.Int[0, Queries.Length - 1].Array[3]
        from path in Gen.OneOfConst(Predicates)
        select
            "ex:Limited a sh:ConstraintComponent ; sh:parameter [ sh:path ex:limit ] ;\n"
            + "  sh:validator [ a sh:SPARQLAskValidator ; sh:ask \"PREFIX ex: <http://example.com/ns#> ASK { FILTER NOT EXISTS { $value ex:p2 $limit } }\" ] .\n"
            + string.Concat(Enumerable.Range(0, 3).Select(i =>
                "ex:S" + i + " a sh:NodeShape ; " + targets[i] + " ; sh:sparql [ sh:select \"\"\"PREFIX ex: <http://example.com/ns#> PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#> "
                + Queries[queries[i]] + "\"\"\" ] .\n"))
            + "ex:P a sh:NodeShape ; sh:targetSubjectsOf ex:p0 ; sh:property [ sh:path " + path + " ; ex:limit ex:n1 ;\n"
            + "  sh:sparql [ sh:select \"SELECT $this ?value WHERE { $this $PATH ?value . FILTER(isLiteral(?value)) }\" ] ] .\n";

    private static readonly Gen<(string S, string P, string O)> Triples = Gen.Frequency(
        (6, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Predicates), Gen.OneOfConst([.. Nodes, "1", "\"a\""])).Select((s, p, o) => (s, p, o))),
        (3, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst(Classes)).Select((s, c) => (s, "rdf:type", c))),
        (1, Gen.Select(Gen.OneOfConst(Classes), Gen.OneOfConst(Classes)).Select((a, b) => (a, "rdfs:subClassOf", b))));

    [Fact]
    public async Task The_incremental_report_has_a_full_validations_results_at_every_position()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
        await Gen.Select(Shapes, Triples.Array[1, 4].Array[1, 8]).SampleAsync(async (shapesText, history) =>
        {
            ShapesGraph shapes = ShapesGraph.Load(Support.Dataset(shapesText), GraphPattern.DefaultGraph, options, ct).Shapes
                ?? throw new InvalidOperationException("refused: " + string.Join("; ", ShapesGraph.Load(Support.Dataset(shapesText), GraphPattern.DefaultGraph, options, ct).Problems) + "\n" + shapesText);
            Recorder recorder = new(shapes, options);
            await using Dataset dataset = await Dataset.CreateAsync(
                new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [recorder] }, ct);
            HashSet<(string, string, string)> state = [];
            List<string> before = Results(Full(shapes, dataset, options));
            foreach ((string S, string P, string O)[] toggles in history)
            {
                CommitRequest request = new();
                foreach ((string s, string p, string o) in toggles.Distinct())
                {
                    RequestTerm[] terms = [Term(s), Term(p), Term(o)];
                    if (state.Remove((s, p, o)))
                    {
                        request.Retract(terms[0], terms[1], terms[2]);
                    }
                    else
                    {
                        state.Add((s, p, o));
                        request.Assert(terms[0], terms[1], terms[2]);
                    }
                }

                Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, ct)).Outcome);
                IncrementalComparison comparison = recorder.Last!;
                List<string> after = Results(Full(shapes, dataset, options));
                List<string> combined = [.. before];
                foreach (string removed in Results(comparison.Before))
                {
                    Assert.True(combined.Remove(removed), "A result before is not in the whole report before: " + removed);
                }

                combined.AddRange(Results(comparison.After));
                combined.Sort(StringComparer.Ordinal);
                Assert.True(after.SequenceEqual(combined), "The incremental results differ from a full validation.\n" + shapesText + "\n" + string.Join("\n", toggles)
                    + "\n# only in full\n" + string.Join("\n", after.Except(combined)) + "\n# only in incremental\n" + string.Join("\n", combined.Except(after)) + "\n# state\n" + string.Join("\n", state));
                before = after;
            }
        }, iter: Iterations);
    }

    [Theory]
    [InlineData("SELECT $this WHERE { $this ex:p0 ?d . FILTER(?d < NOW()) }", "NOW()")]
    [InlineData("SELECT $this WHERE { $this ex:p0 ?d . FILTER(RAND() < 0.5) }", "RAND()")]
    [InlineData("SELECT $this (STRUUID() AS ?message) WHERE { $this ex:p0 ?d }", "STRUUID()")]
    [InlineData("SELECT $this ?value WHERE { $this ex:p0 ?d . BIND(BNODE() AS ?value) }", "BNODE()")]
    public void A_binding_refuses_a_function_that_depends_on_the_clock_or_on_chance(string query, string function)
    {
        string shapes = "ex:S sh:targetNode ex:n0 ; sh:sparql [ sh:select \"\"\"PREFIX ex: <http://example.com/ns#> " + query + "\"\"\" ] .";
        ShaclOptions refusing = new() { Extensions = [SparqlConstraints.Extension()], RefuseNonDeterministicFunctions = true };
        ShapesLoad load = ShapesGraph.Load(Support.Dataset(shapes), GraphPattern.DefaultGraph, refusing, TestContext.Current.CancellationToken);
        ShaclProblem problem = Assert.Single(load.Problems);
        Assert.Contains(function, problem.Message, StringComparison.Ordinal);

        ShaclOptions standalone = new() { Extensions = [SparqlConstraints.Extension()] };
        Assert.True(ShapesGraph.Load(Support.Dataset(shapes), GraphPattern.DefaultGraph, standalone, TestContext.Current.CancellationToken).Succeeded);
    }

    [Fact]
    public void BNODE_in_a_filter_does_not_reach_a_result_and_is_not_refused()
    {
        string shapes = "ex:S sh:targetNode ex:n0 ; sh:sparql [ sh:select \"\"\"PREFIX ex: <http://example.com/ns#> SELECT $this WHERE { $this ex:p0 ?d . FILTER(BNODE() != ?d) }\"\"\" ] .";
        ShaclOptions refusing = new() { Extensions = [SparqlConstraints.Extension()], RefuseNonDeterministicFunctions = true };
        Assert.True(ShapesGraph.Load(Support.Dataset(shapes), GraphPattern.DefaultGraph, refusing, TestContext.Current.CancellationToken).Succeeded);
    }

    [Theory]
    [InlineData("SELECT $this WHERE { $this ex:p0/ex:p1* ?x . ?x a ex:C }", Maintainability.Scoped)]
    [InlineData("SELECT $this WHERE { ?a ex:p2 ex:n0 . FILTER EXISTS { $this ex:p0 ?b } }", Maintainability.Full)]
    [InlineData("SELECT $this WHERE { { SELECT (COUNT(*) AS ?n) WHERE { ?s a ex:C } } FILTER(?n > 3) }", Maintainability.Full)]
    [InlineData("SELECT $this WHERE { GRAPH ex:g { $this ex:p0 ?x } }", Maintainability.Full)]
    [InlineData("SELECT $this WHERE { $this !ex:p0 ?x }", Maintainability.Full)]
    public void A_SPARQL_constraint_is_scoped_when_every_pattern_is_joined_to_this(string query, Maintainability expected)
    {
        string shapes = "ex:S sh:targetNode ex:n0 ; sh:sparql [ sh:select \"\"\"PREFIX ex: <http://example.com/ns#> " + query + "\"\"\" ] .";
        ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
        ShapesGraph loaded = ShapesGraph.Load(Support.Dataset(shapes), GraphPattern.DefaultGraph, options, TestContext.Current.CancellationToken).Shapes!;
        Assert.Equal(expected, loaded.Classify().Shapes.Single(s => s.Shape.Equals(RdfTerm.Iri("http://example.com/ns#S"u8))).Maintainability);
    }

    private static RequestTerm Term(string text)
    {
        InMemoryDataset parsed = Support.Dataset("@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . ex:s ex:p " + text + " .");
        using IQuadCursor cursor = parsed.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        cursor.MoveNext();
        parsed.TryExternalise(cursor.Current.Object, out RdfTerm? term);
        return RequestTerm.FromTerm(term!);
    }

    private static ValidationReport Full(ShapesGraph shapes, Dataset dataset, ShaclOptions options)
    {
        using DatasetView view = dataset.Pin();
        ValidationOutcome outcome = ShaclValidator.Validate(shapes, view, GraphPattern.DefaultGraph, options);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Problems));
        return outcome.Report!;
    }

    /// <summary>Each result as one comparable line of its public fields, sorted.</summary>
    private static List<string> Results(ValidationReport report)
    {
        static string Show(RdfTerm? term) => term is null ? "-" : term.Kind + ":" + Encoding.UTF8.GetString(term.Lexical) + "^" + Encoding.UTF8.GetString(term.DatatypeIri) + "@" + Encoding.UTF8.GetString(term.Language);
        List<string> lines = [.. report.Results.Select(r => string.Join(" ",
            Show(r.FocusNode), Show(r.Path), r.HasPath, Show(r.Value), Show(r.SourceShape), Show(r.SourceConstraint), Show(r.SourceConstraintComponent), Show(r.Severity),
            string.Join(",", r.Messages.Select(Show))))];
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private sealed class Recorder(ShapesGraph shapes, ShaclOptions options) : ICommitValidator
    {
        internal IncrementalComparison? Last { get; private set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta)
        {
            IncrementalOutcome outcome = IncrementalValidation.Compare(shapes, proposed, GraphPattern.DefaultGraph, delta, options);
            Last = outcome.Comparison ?? throw new InvalidOperationException(string.Join("; ", outcome.Problems));
            return ValidationVerdict.Accept();
        }
    }
}
