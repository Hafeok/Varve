// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Varve.Events.Model;
using Xunit;

namespace Varve.Events.Tests;

public sealed class RenderingTests
{
    private static readonly EventEndpoints Endpoints = new("https://varve.example.org/", "orders");

    private static readonly TypeSpec OrderPlaced = new(
        "OrderPlaced", Terms.Ex + "OrderPlaced", 1,
        [
            new FieldSpec("order", Terms.Xsd + "anyURI", Required: true),
            new FieldSpec("total", Terms.Xsd + "decimal", Required: true),
            new FieldSpec("placedAt", Terms.Xsd + "dateTime", Required: true),
            new FieldSpec("note", Terms.Xsd + "string", Required: false),
        ],
        Subject: "subject",
        Description: "An order was placed.");

    /// <summary>Declares the type, then commits one event; answers the rendered events of that commit.</summary>
    private static List<RenderedEvent> RenderOne(TypeSpec type, string eventTrig, string node = "https://example.org/orders/1/placed")
    {
        History history = new();
        history.Commit([], type.Quads(Terms.EventsGraph));
        history.Commit([], Trig.Quads("<https://example.org/events> { " + eventTrig + " }"));
        List<RenderedEvent> all = history.RenderAll(Endpoints, history.Head);
        return all.Where(e => e.Position == history.Head).ToList();
    }

    [Fact]
    public void an_event_renders_as_a_canonical_cloudevent()
    {
        IReadOnlyList<RenderedEvent> events = RenderOne(OrderPlaced, """
            <https://example.org/orders/1/placed> a ex:OrderPlaced ;
              ex:order <https://example.org/orders/1> ;
              ex:total 19.99 ;
              ex:placedAt "2026-10-15T09:30:00Z"^^xsd:dateTime .
            """);

        RenderedEvent single = Assert.Single(events);
        Assert.False(single.IsProblem);
        Assert.Equal(
            "{\"data\":{\"order\":\"https://example.org/orders/1\",\"placedAt\":\"2026-10-15T09:30:00Z\",\"total\":19.99},"
            + "\"datacontenttype\":\"application/json\","
            + "\"dataschema\":\"https://varve.example.org/datasets/orders/events/types/OrderPlaced/versions/1/revisions/1\","
            + "\"id\":\"2 https://example.org/orders/1/placed\","
            + "\"sequence\":\"00000000000000000002.000000\","
            + "\"source\":\"https://varve.example.org/datasets/orders\","
            + "\"specversion\":\"1.0\","
            + "\"subject\":\"https://example.org/orders/1/placed\","
            + "\"time\":\"2026-10-15T12:00:02.0000000Z\","
            + "\"type\":\"OrderPlaced\"}",
            Json.Text(single.Json));
    }

    [Fact]
    public void the_rendering_reads_the_closure_with_its_blank_nodes()
    {
        TypeSpec withLine = new("Shipped", Terms.Ex + "Shipped", 1, [new FieldSpec("carrier", Terms.Xsd + "string", Required: true)],
            Select: "PREFIX ex: <https://example.org/ns#> SELECT ?carrier WHERE { ?event ex:via [ ex:carrier ?carrier ] }");
        RenderedEvent single = Assert.Single(RenderOne(withLine, """
            <https://example.org/s/1> a ex:Shipped ; ex:via [ ex:carrier "Post" ] .
            """, "https://example.org/s/1"));
        Assert.Equal("Post", Json.Node(single.Json)["data"]!["carrier"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ex:total 19.99 ; ex:placedAt \"2026-10-15T09:30:00\"^^xsd:dateTime", "datatype")]
    [InlineData("ex:total 19.99 ; ex:placedAt \"2026-10-15T24:00:00Z\"^^xsd:dateTime", "datatype")]
    [InlineData("ex:total 19.99", "no-row")]
    [InlineData("ex:total 19.99, 20.5 ; ex:placedAt \"2026-10-15T09:30:00Z\"^^xsd:dateTime", "rows")]
    [InlineData("ex:total 12345678901234567890.5 ; ex:placedAt \"2026-10-15T09:30:00Z\"^^xsd:dateTime", "number")]
    [InlineData("ex:total \"high\" ; ex:placedAt \"2026-10-15T09:30:00Z\"^^xsd:dateTime", "datatype")]
    public void an_event_that_cannot_be_rendered_is_a_problem_in_its_place(string properties, string reason)
    {
        RenderedEvent single = Assert.Single(RenderOne(OrderPlaced,
            "<https://example.org/orders/1/placed> a ex:OrderPlaced ; ex:order <https://example.org/orders/1> ; " + properties + " ."));

        Assert.True(single.IsProblem);
        JsonNode cloudEvent = Json.Node(single.Json);
        Assert.Equal(EventRenderer.ProblemType, cloudEvent["type"]!.GetValue<string>());
        Assert.Equal("application/problem+json", cloudEvent["datacontenttype"]!.GetValue<string>());
        Assert.Equal(reason, cloudEvent["data"]!["reason"]!.GetValue<string>());
        Assert.Equal("OrderPlaced", cloudEvent["data"]!["eventType"]!.GetValue<string>());
        Assert.Equal("2 https://example.org/orders/1/placed", cloudEvent["id"]!.GetValue<string>());
        Assert.Null(cloudEvent["dataschema"]);
    }

    [Fact]
    public void an_unbound_required_field_is_a_problem()
    {
        TypeSpec optionalPattern = OrderPlaced with
        {
            Select = "PREFIX ex: <https://example.org/ns#> SELECT ?order ?total ?placedAt ?note ?subject WHERE { ?event ex:order ?order . OPTIONAL { ?event ex:total ?total } OPTIONAL { ?event ex:placedAt ?placedAt } OPTIONAL { ?event ex:note ?note } BIND(?event AS ?subject) }",
        };
        RenderedEvent single = Assert.Single(RenderOne(optionalPattern,
            "<https://example.org/orders/1/placed> a ex:OrderPlaced ; ex:order <https://example.org/orders/1> ."));
        Assert.Equal("unbound-required", Json.Node(single.Json)["data"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void a_blank_event_node_is_a_problem()
    {
        RenderedEvent single = Assert.Single(RenderOne(OrderPlaced, "[] a ex:OrderPlaced ."));
        JsonNode cloudEvent = Json.Node(single.Json);
        Assert.Equal("blank-event", cloudEvent["data"]!["reason"]!.GetValue<string>());
        Assert.Equal("2 _:", cloudEvent["id"]!.GetValue<string>());
    }

    [Fact]
    public void matching_is_exact_with_no_subclass()
    {
        Assert.Empty(RenderOne(OrderPlaced, """
            ex:Special rdfs:subClassOf ex:OrderPlaced .
            <https://example.org/orders/1/placed> a ex:Special .
            """));
    }

    [Fact]
    public void a_retired_type_renders_nothing()
    {
        Assert.Empty(RenderOne(OrderPlaced with { Retired = true }, "<https://example.org/orders/1/placed> a ex:OrderPlaced ."));
    }

    [Fact]
    public void events_of_a_commit_are_ordered_by_node_and_sequenced()
    {
        TypeSpec ping = new("Ping", Terms.Ex + "Ping", 1, [new FieldSpec("n", Terms.Xsd + "integer", Required: true)]);
        History history = new();
        history.Commit([], ping.Quads(Terms.EventsGraph));
        history.Commit([], Trig.Quads("""
            <https://example.org/events> {
              <https://example.org/p/b> a ex:Ping ; ex:n 2 .
              <https://example.org/p/a> a ex:Ping ; ex:n 1 .
            }
            """));

        List<RenderedEvent> events = history.RenderAll(Endpoints, history.Head);
        Assert.Equal(["2 https://example.org/p/a", "2 https://example.org/p/b"], events.Select(e => e.Id));
        Assert.Equal(["00000000000000000002.000000", "00000000000000000002.000001"],
            events.Select(e => Json.Node(e.Json)["sequence"]!.GetValue<string>()));
    }

    [Fact]
    public void a_type_assertion_that_already_held_is_no_event()
    {
        TypeSpec ping = new("Ping", Terms.Ex + "Ping", 1, [new FieldSpec("n", Terms.Xsd + "integer", Required: true)]);
        History history = new();
        history.Commit([], ping.Quads(Terms.EventsGraph));
        List<Q> once = Trig.Quads("<https://example.org/events> { <https://example.org/p/a> a ex:Ping ; ex:n 1 . }");
        history.Commit([], once);
        history.Commit([], [.. once, new Q(Terms.Iri("https://example.org/p/a"), Terms.Iri(Terms.Ex + "seen"), Terms.Plain("x"), Terms.EventsGraph)]);
        Assert.Single(history.RenderAll(Endpoints, history.Head));
    }

    [Fact]
    public void a_language_is_kept_only_when_declared()
    {
        TypeSpec greeting = new("Greeted", Terms.Ex + "Greeted", 1,
        [
            new FieldSpec("plain", "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", Required: true),
            new FieldSpec("kept", "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString", Required: true, KeepLanguage: true),
        ]);
        RenderedEvent single = Assert.Single(RenderOne(greeting,
            "<https://example.org/g/1> a ex:Greeted ; ex:plain \"hej\"@da ; ex:kept \"hello\"@en .", "https://example.org/g/1"));
        Assert.Equal("{\"kept\":{\"language\":\"en\",\"value\":\"hello\"},\"plain\":\"hej\"}", Json.Node(single.Json)["data"]!.ToJsonString());
    }

    [Fact]
    public void a_field_declared_as_a_string_carries_any_number()
    {
        TypeSpec big = new("Big", Terms.Ex + "Big", 1, [new FieldSpec("value", Terms.Xsd + "integer", Required: true, AsString: true)]);
        RenderedEvent single = Assert.Single(RenderOne(big,
            "<https://example.org/b/1> a ex:Big ; ex:value 9007199254740993 .", "https://example.org/b/1"));
        Assert.Equal("{\"value\":\"9007199254740993\"}", Json.Node(single.Json)["data"]!.ToJsonString());
    }
}
