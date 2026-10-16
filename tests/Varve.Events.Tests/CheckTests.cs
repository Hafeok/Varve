// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Events.Tests;

public sealed class CheckTests
{
    private static readonly RdfTerm OtherGraph = Terms.Iri("https://example.org/secret-events");

    private static readonly TypeSpec Placed = new("OrderPlaced", Terms.Ex + "OrderPlaced", 1,
    [
        new FieldSpec("order", Terms.Xsd + "anyURI", Required: true),
        new FieldSpec("total", Terms.Xsd + "decimal", Required: false),
    ]);

    /// <summary>Checks the change from <paramref name="before"/> to <paramref name="after"/>, as the validator would see it.</summary>
    private static DeclarationVerdict Check(IEnumerable<Q> before, IEnumerable<Q> after)
    {
        HashSet<Q> then = [.. before];
        HashSet<Q> now = [.. after];
        List<FeedChange> changes = [];

        foreach (Q q in then.Except(now))
        {
            changes.Add(new FeedChange(FeedChangeKind.Retract, q.S, q.P, q.O, q.G));
        }

        foreach (Q q in now.Except(then))
        {
            changes.Add(new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G));
        }

        return EventDeclarationCheck.Check(Trig.Load(now), changes);
    }

    [Fact]
    public void a_new_declaration_is_accepted() =>
        Assert.True(Check([], Placed.Quads(Terms.EventsGraph)).Accepted);

    [Fact]
    public void an_optional_field_added_keeps_the_version()
    {
        TypeSpec next = Placed with { Fields = [.. Placed.Fields, new FieldSpec("note", Terms.Xsd + "string", Required: false)] };
        Assert.True(Check(Placed.Quads(Terms.EventsGraph), next.Quads(Terms.EventsGraph)).Accepted);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("type")]
    [InlineData("required")]
    [InlineData("optional")]
    public void a_breaking_change_needs_a_higher_version(string change)
    {
        TypeSpec next = change switch
        {
            "removed" => Placed with { Fields = [Placed.Fields[0]] },
            "type" => Placed with { Fields = [Placed.Fields[0], Placed.Fields[1] with { Datatype = Terms.Xsd + "string" }] },
            "required" => Placed with { Fields = [Placed.Fields[0], Placed.Fields[1] with { Required = true }] },
            _ => Placed with { Fields = [Placed.Fields[0] with { Required = false }, Placed.Fields[1]] },
        };

        DeclarationVerdict refused = Check(Placed.Quads(Terms.EventsGraph), next.Quads(Terms.EventsGraph));
        Assert.False(refused.Accepted);
        Assert.Equal(DeclarationVerdict.IncompatibleType, refused.ProblemType);
        Assert.Equal(change, Assert.Single(refused.Fields).Change);

        Assert.True(Check(Placed.Quads(Terms.EventsGraph), (next with { Version = 2 }).Quads(Terms.EventsGraph)).Accepted);
    }

    [Fact]
    public void the_subject_variable_is_not_a_field()
    {
        Assert.Equal("term", Check([], (Placed with { Subject = "order" }).Quads(Terms.EventsGraph)).Reason);
        Assert.True(Check([], (Placed with { Subject = "subject", Select = "PREFIX ex: <https://example.org/ns#> SELECT ?order ?total ?subject WHERE { ?event ex:order ?order OPTIONAL { ?event ex:total ?total } BIND(?order AS ?subject) }" }).Quads(Terms.EventsGraph)).Accepted);
    }

    [Fact]
    public void a_version_never_decreases()
    {
        DeclarationVerdict refused = Check((Placed with { Version = 3 }).Quads(Terms.EventsGraph), (Placed with { Version = 2 }).Quads(Terms.EventsGraph));
        Assert.Equal("version", refused.Reason);
    }

    [Fact]
    public void a_declaration_is_retired_never_removed()
    {
        Assert.Equal("removed", Check(Placed.Quads(Terms.EventsGraph), []).Reason);
        Assert.True(Check(Placed.Quads(Terms.EventsGraph), (Placed with { Retired = true }).Quads(Terms.EventsGraph)).Accepted);
    }

    [Fact]
    public void a_short_name_taken_in_another_graph_is_refused_naming_nothing_of_it()
    {
        List<Q> secret = (Placed with { Class = Terms.Ex + "Secret", Description = "classified" }).Quads(OtherGraph)
            .Select(q => q with { S = Rename(q.S), O = Rename(q.O) }).ToList();
        TypeSpec mine = new("OrderPlaced", Terms.Ex + "Mine", 1, [new FieldSpec("x", Terms.Xsd + "string", Required: true)]);

        DeclarationVerdict refused = Check(secret, [.. secret, .. mine.Quads(Terms.EventsGraph)]);

        Assert.False(refused.Accepted);
        Assert.Equal("name-taken", refused.Reason);
        Assert.Equal(mine.Node, Encoding.UTF8.GetString(refused.Declaration!.Lexical));
        Assert.Equal("the short name is taken", refused.Detail);
        string said = refused.Detail + " " + Encoding.UTF8.GetString(refused.Declaration.Lexical);
        Assert.DoesNotContain("secret", said, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", said);
        Assert.DoesNotContain("classified", said);

        static RdfTerm Rename(RdfTerm term) =>
            term.Kind == RdfTermKind.Iri && Encoding.UTF8.GetString(term.Lexical).StartsWith(Terms.Types, System.StringComparison.Ordinal)
                ? Terms.Iri(Encoding.UTF8.GetString(term.Lexical).Replace(Terms.Types, "https://example.org/hidden/", System.StringComparison.Ordinal))
                : term;
    }

    [Theory]
    [InlineData("SELECT ?order ?total WHERE { ?event <https://example.org/ns#order> ?order } ORDER BY ?order", "select")]
    [InlineData("SELECT ?order WHERE { ?event <https://example.org/ns#order> ?order }", "select")]
    [InlineData("SELECT (COUNT(*) AS ?order) ?total WHERE { ?event <https://example.org/ns#order> ?total } GROUP BY ?total", "select")]
    [InlineData("SELECT ?order ?total WHERE { ?x <https://example.org/ns#order> ?order ; <https://example.org/ns#total> ?total }", "select")]
    [InlineData("SELECT ?order ?total WHERE {", "select")]
    public void a_query_outside_the_form_is_refused(string select, string reason) =>
        Assert.Equal(reason, Check([], (Placed with { Select = select }).Quads(Terms.EventsGraph)).Reason);

    [Fact]
    public void a_blank_node_in_a_catalogue_graph_is_refused()
    {
        List<Q> quads = Placed.Quads(Terms.EventsGraph);
        quads.Add(new Q(Terms.Iri(Placed.Node), Terms.Iri("http://www.w3.org/2000/01/rdf-schema#seeAlso"), RdfTerm.BlankNode("b0"u8), EventCatalogue.GraphOf(Terms.EventsGraph)));
        Assert.Equal("blank-node", Check([], quads).Reason);
    }

    [Fact]
    public void a_graph_under_the_namespace_that_is_no_catalogue_is_refused()
    {
        RdfTerm bogus = Terms.Iri(EventCatalogue.Namespace + "not-an-iri");
        List<Q> quads = [.. Placed.Quads(Terms.EventsGraph).Select(q => q with { G = bogus })];
        Assert.Equal("graph", Check([], quads).Reason);
    }

    [Fact]
    public void the_catalogue_graph_name_round_trips()
    {
        RdfTerm graph = EventCatalogue.GraphOf(Terms.EventsGraph);
        Assert.Equal("https://w3id.org/varve/ns#events-catalogue/https%3A%2F%2Fexample.org%2Fevents", Encoding.UTF8.GetString(graph.Lexical));
        Assert.True(EventCatalogue.TryGetSourceGraph(graph, out RdfTerm? source));
        Assert.Equal(Terms.EventsGraph, source);
        Assert.True(EventCatalogue.TryGetSourceGraph(EventCatalogue.GraphOf(null), out RdfTerm? none));
        Assert.Null(none);
        Assert.False(EventCatalogue.TryGetSourceGraph(Terms.Iri(EventCatalogue.Namespace + "https%3a%2F%2Fexample.org%2Fevents"), out _));
    }
}
