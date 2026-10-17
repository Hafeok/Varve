// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>ADR 0151: inputs, failures, imports, entailment, and that a data source is never added to.</summary>
public class InputTests
{
    [Fact]
    public void An_ill_formed_shape_is_refused_naming_the_shape_and_the_rule()
    {
        ValidationOutcome outcome = Support.Outcome(
            "ex:S a sh:NodeShape ; sh:targetNode ex:x ; sh:minCount 1 .",
            "ex:x ex:p 1 .");

        ShaclProblem problem = Assert.Single(outcome.Problems);
        Assert.Equal(ShaclProblemKind.IllFormedShape, problem.Kind);
        Assert.Equal(Support.Ex("S"), problem.Shape);
        Assert.Contains("§7.2.1", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entailment_regime_other_than_simple_is_a_failure()
    {
        ValidationOutcome outcome = Support.Outcome(
            "ex:graph sh:entailment <http://www.w3.org/ns/entailment/RDFS> . ex:S sh:targetNode ex:x ; sh:class ex:C .",
            "ex:x a ex:C .");

        Assert.Equal(ShaclProblemKind.UnsupportedEntailment, Assert.Single(outcome.Problems).Kind);
    }

    [Fact]
    public void Imports_are_not_followed_and_the_report_says_so()
    {
        ValidationReport report = Support.Validate(
            "ex:graph owl:imports <http://example.com/other> . ex:S sh:targetNode ex:x ; sh:class ex:C .",
            "ex:x a ex:C .");

        Assert.True(report.Conforms);
        Assert.Equal([RdfTerm.Iri("http://example.com/other"u8)], report.ImportsNotFollowed);
        Assert.Contains("<https://w3id.org/varve/ns#importNotFollowed> <http://example.com/other>", System.Text.Encoding.UTF8.GetString(report.ToCanonicalNQuads().Span), StringComparison.Ordinal);
    }

    [Fact]
    public void A_shapes_graph_using_sparql_without_the_extension_is_refused()
    {
        ValidationOutcome outcome = Support.Outcome(
            "ex:S sh:targetNode ex:x ; sh:sparql [ sh:select \"SELECT $this WHERE { }\" ] .",
            "ex:x ex:p 1 .");

        Assert.Contains(outcome.Problems, p => p.Kind == ShaclProblemKind.MissingExtension);
    }

    [Fact]
    public void A_pattern_past_the_timeout_fails_the_validation()
    {
        ValidationOutcome outcome = Support.Outcome(
            "ex:S sh:targetNode ex:x ; sh:property [ sh:path ex:p ; sh:pattern \"^(a+)+$\" ] .",
            "ex:x ex:p \"" + new string('a', 30) + "!\" .",
            new ShaclOptions { RegexTimeout = TimeSpan.FromMilliseconds(20) });

        Assert.False(outcome.Succeeded);
        Assert.Equal(ShaclProblemKind.PatternTimeout, Assert.Single(outcome.Problems).Kind);
    }

    [Fact]
    public void A_constant_the_data_does_not_hold_matches_nothing_and_is_not_added()
    {
        ValidationReport report = Support.Validate(
            "ex:S sh:targetNode ex:x ; sh:property [ sh:path ex:p ; sh:hasValue ex:absent ] .",
            "ex:x ex:p ex:present .");

        ShaclResult result = Assert.Single(report.Results);
        Assert.Equal(RdfTerm.Iri("http://www.w3.org/ns/shacl#HasValueConstraintComponent"u8), result.SourceConstraintComponent);
    }

    [Fact]
    public async Task Validating_a_store_leaves_it_as_it_was()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, ct);
        CommitRequest request = new();
        request.Assert(RequestTerm.FromTerm(Support.Ex("x")), RequestTerm.FromTerm(Support.Ex("p")), RequestTerm.FromTerm(Support.Ex("present")));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, ct)).Outcome);

        InMemoryDataset shapes = Support.Dataset("""
            ex:S sh:targetNode ex:x, ex:alsoAbsent ;
                sh:property [ sh:path ex:p ; sh:hasValue ex:absent ; sh:in ( ex:other "a literal" ) ] .
            """);
        ShapesGraph loaded = ShapesGraph.Load(shapes, GraphPattern.DefaultGraph, null, ct).Shapes!;

        using (DatasetView view = dataset.Pin())
        {
            long head = view.Position.Value;
            ValidationOutcome outcome = ShaclValidator.Validate(loaded, view, GraphPattern.DefaultGraph, null, ct);
            Assert.True(outcome.Succeeded);
            Assert.False(outcome.Report!.Conforms);

            StagingView staging = view.Stage();
            Assert.True(ShaclValidator.Validate(loaded, staging, GraphPattern.DefaultGraph, null, ct).Succeeded);
            Assert.False(staging.TryInternalise(Support.Ex("absent"), out _));
            Assert.Equal(head, view.Position.Value);
        }

        // Nothing the shapes named reached the dictionary: a fresh view finds none of it.
        using DatasetView after = dataset.Pin();
        Assert.False(after.TryInternalise(Support.Ex("absent"), out _));
        Assert.False(after.TryInternalise(Support.Ex("alsoAbsent"), out _));
        Assert.False(after.TryInternalise(Support.Ex("other"), out _));
        Assert.False(after.TryInternalise(RdfTerm.Literal("a literal"u8), out _));
        Assert.True(after.TryInternalise(Support.Ex("present"), out _));
    }

    [Fact]
    public void Cancellation_throws()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        InMemoryDataset shapes = Support.Dataset("ex:S sh:targetNode ex:x ; sh:class ex:C .");
        Assert.Throws<OperationCanceledException>(() => ShapesGraph.Load(shapes, GraphPattern.DefaultGraph, null, cancelled.Token));
    }
}
