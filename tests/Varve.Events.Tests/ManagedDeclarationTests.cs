// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Protocol;
using Varve.Rdf;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>A dataset graph's event nodes in the catalogue graphs' form, and what cannot be put in it (ADR 0167).</summary>
public sealed class ManagedDeclarationTests
{
    private const string Varve = "https://w3id.org/varve/ns#";

    private static List<(RdfTerm, RdfTerm, RdfTerm)> Graph(string body) =>
        [.. Trig.Quads("<" + Varve + "manage/dataset/d> { " + body + " }").Select(q => (q.S, q.P, q.O))];

    [Fact]
    public void a_type_goes_to_its_source_graph_s_catalogue_and_its_fields_with_it()
    {
        List<DeclarationProblem> problems = [];
        List<FeedChange> catalogue = ManagedEventDeclarations.ToCatalogue(Graph(
            "<urn:d> a varve:Dataset ; varve:eventDeclarations varve:Moved . "
            + "<urn:t> a varve:EventType ; varve:sourceGraph <https://example.org/g> ; varve:eventField <urn:f> . "
            + "<urn:u> a varve:EventType ; varve:sourceGraph varve:defaultGraph . "
            + "<urn:f> a varve:EventField ; varve:fieldName \"f\" ."), problems);

        Assert.Empty(problems);
        Assert.All(catalogue, c => Assert.False(c.Predicate.Equals(Terms.Iri(Varve + "sourceGraph"))));
        Assert.DoesNotContain(catalogue, c => c.Subject.Equals(Terms.Iri("urn:d")));
        Assert.Equal(EventCatalogue.GraphOf(Terms.Iri("https://example.org/g")), catalogue.Single(c => c.Subject.Equals(Terms.Iri("urn:f")) && c.Predicate.Equals(Terms.Iri(Varve + "fieldName"))).Graph);
        Assert.Equal(EventCatalogue.GraphOf(null), catalogue.Single(c => c.Subject.Equals(Terms.Iri("urn:u"))).Graph);
    }

    [Theory]
    [InlineData("<urn:t> a varve:EventType .", "exactly one varve:sourceGraph")]
    [InlineData("<urn:t> a varve:EventType ; varve:sourceGraph varve:defaultGraph, <https://example.org/g> .", "exactly one varve:sourceGraph")]
    [InlineData("<urn:t> a varve:EventType ; varve:sourceGraph \"g\" .", "exactly one varve:sourceGraph")]
    [InlineData("<urn:f> a varve:EventField .", "named by no event type")]
    [InlineData("<urn:t> a varve:EventType ; varve:sourceGraph varve:defaultGraph ; varve:eventField <urn:f> . <urn:u> a varve:EventType ; varve:sourceGraph <https://example.org/g> ; varve:eventField <urn:f> . <urn:f> a varve:EventField .", "two source graphs")]
    public void what_cannot_be_put_in_the_catalogue_form_is_a_problem(string body, string expected)
    {
        List<DeclarationProblem> problems = [];
        ManagedEventDeclarations.ToCatalogue(Graph(body), problems);
        Assert.Contains(problems, p => p.Detail.Contains(expected, System.StringComparison.Ordinal));
    }

    [Fact]
    public void the_copy_refuses_a_node_in_two_catalogue_graphs_and_a_graph_that_catalogues_none()
    {
        RdfTerm node = Terms.Iri("urn:f");
        RdfTerm p = Terms.Iri(Varve + "fieldName");
        Assert.False(ManagedEventDeclarations.TryFromCatalogue(
            [new FeedChange(FeedChangeKind.Assert, node, p, Terms.Plain("f"), EventCatalogue.GraphOf(null)),
             new FeedChange(FeedChangeKind.Assert, node, p, Terms.Plain("f"), EventCatalogue.GraphOf(Terms.Iri("https://example.org/g")))],
            out _, out DeclarationProblem? twice));
        Assert.Contains("two catalogue graphs", twice!.Value.Detail, System.StringComparison.Ordinal);
        Assert.False(ManagedEventDeclarations.TryFromCatalogue(
            [new FeedChange(FeedChangeKind.Assert, node, p, Terms.Plain("f"), Terms.Iri(EventCatalogue.Namespace + "not%3"))], out _, out DeclarationProblem? none));
        Assert.Contains("catalogues no graph", none!.Value.Detail, System.StringComparison.Ordinal);
    }
}
