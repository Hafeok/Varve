// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using CsCheck;
using Json.Schema;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>The gate of docs/spec/events.md §8, over generated histories.</summary>
public sealed class PropertyTests
{
    internal const int Histories = 200;
    internal const int Edits = 500;
    internal const int Catalogues = 100;

    private static readonly EventEndpoints Endpoints = new("https://varve.example.org/", "d");

    [Fact]
    public void rendering_every_commit_twice_yields_identical_bytes() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);
            List<RenderedEvent> first = history.RenderAll(Endpoints, history.Head);
            List<RenderedEvent> second = Generators.Play(steps).RenderAll(Endpoints, history.Head);
            return first.Count == second.Count && first.Zip(second).All(p => p.First.Json.Span.SequenceEqual(p.Second.Json.Span));
        }, iter: Histories));

    [Fact]
    public void a_commit_renders_with_the_declaration_in_force_at_its_position() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);

            for (long p = 1; p <= history.Head.Value; p++)
            {
                // The catalogue replayed only to p, and the whole history: the
                // commit at p renders the same either way.
                List<RenderedEvent> early = [.. history.RenderAll(Endpoints, new FeedPosition(p)).Where(e => e.Position.Value == p)];
                List<RenderedEvent> late = [.. history.RenderAll(Endpoints, history.Head).Where(e => e.Position.Value == p)];

                if (early.Count != late.Count || !early.Zip(late).All(x => x.First.Json.Span.SequenceEqual(x.Second.Json.Span)))
                {
                    return false;
                }
            }

            return true;
        }, iter: Histories / 4));

    [Fact]
    public void every_rendered_event_validates_against_its_schema() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);
            EventCatalogueHistory catalogue = history.Catalogue(history.Head);

            foreach (RenderedEvent rendered in history.RenderAll(Endpoints, history.Head).Where(e => !e.IsProblem))
            {
                JsonNode cloudEvent = Json.Node(rendered.Json);
                Assert.True(catalogue.At(rendered.Position).TryGet(rendered.Type, out CataloguedType? type));
                string schema = Encoding.UTF8.GetString(EventSchemas.Generate(type.Declaration, cloudEvent["dataschema"]!.GetValue<string>()));
                EvaluationResults results = Json.Validate(schema, cloudEvent["data"]);
                Assert.True(results.IsValid, Json.Text(rendered.Json) + " against " + schema + ": " + Json.Errors(results));
            }

            return true;
        }, iter: Histories));

    [Fact]
    public void a_select_with_no_row_or_many_is_the_problem_and_no_event() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);

            foreach (RenderedEvent rendered in history.RenderAll(Endpoints, history.Head))
            {
                JsonNode cloudEvent = Json.Node(rendered.Json);

                if (rendered.IsProblem)
                {
                    // In its place: the id, sequence and source of the event it stands for.
                    Assert.Equal(EventRenderer.ProblemType, cloudEvent["type"]!.GetValue<string>());
                    Assert.Null(cloudEvent["dataschema"]);
                    Assert.StartsWith(rendered.Position.Value + " ", cloudEvent["id"]!.GetValue<string>(), StringComparison.Ordinal);
                }
                else
                {
                    Assert.NotEqual(EventRenderer.ProblemType, cloudEvent["type"]!.GetValue<string>());
                }
            }

            return true;
        }, iter: Histories));

    /// <summary>One generated edit of a declaration, and whether it is compatible by an oracle that reads only the field lists.</summary>
    private static readonly Gen<(List<FieldSpec> Old, List<FieldSpec> New)> FieldEdits =
        Gen.Select(Generators.Fields, Generators.Fields, Gen.Int[0, 3], (old, other, mode) => mode switch
        {
            // keep, and add only optional fields
            0 => (old, old.Concat(other.Where(f => old.All(o => o.Name != f.Name)).Select(f => f with { Required = false })).ToList()),
            // flip one field's required-ness
            1 => (old, old.Select((f, i) => i == 0 ? f with { Required = !f.Required } : f).ToList()),
            // anything
            _ => (old, other),
        });

    private static bool Compatible(List<FieldSpec> old, List<FieldSpec> now)
    {
        foreach (FieldSpec before in old)
        {
            FieldSpec? after = now.FirstOrDefault(f => f.Name == before.Name);

            // §7.2: the JSON type or format, and fieldAs and fieldKeepLanguage
            // themselves, whatever the JSON type they lead to.
            if (after is null || after.Required != before.Required || Json(before) != Json(after)
                || after.AsString != before.AsString || after.KeepLanguage != before.KeepLanguage)
            {
                return false;
            }
        }

        return now.Where(f => old.All(o => o.Name != f.Name)).All(f => !f.Required);

        static string Json(FieldSpec f) => f.AsString ? "string"
            : f.Datatype.EndsWith("langString", StringComparison.Ordinal) ? (f.KeepLanguage ? "object" : "string")
            : f.Datatype.EndsWith("#integer", StringComparison.Ordinal) ? "integer"
            : f.Datatype.EndsWith("#decimal", StringComparison.Ordinal) || f.Datatype.EndsWith("#double", StringComparison.Ordinal) ? "number"
            : f.Datatype.EndsWith("#boolean", StringComparison.Ordinal) ? "boolean"
            : f.Datatype.EndsWith("#dateTime", StringComparison.Ordinal) ? "date-time"
            : f.Datatype.EndsWith("#date", StringComparison.Ordinal) ? "date"
            : f.Datatype.EndsWith("#anyURI", StringComparison.Ordinal) ? "iri" : "string";
    }

    [Fact]
    public void every_breaking_change_is_refused_unless_it_declares_a_new_version() => Property.Run(() =>
        FieldEdits.Sample(edit =>
        {
            TypeSpec old = Generators.TypeOf(0, edit.Old, 1, retired: false);
            TypeSpec same = Generators.TypeOf(0, edit.New, 1, retired: false);
            TypeSpec bumped = Generators.TypeOf(0, edit.New, 2, retired: false);
            bool compatible = Compatible(edit.Old, edit.New);

            DeclarationVerdict atSameVersion = CheckEdit(old.Quads(Terms.EventsGraph), same.Quads(Terms.EventsGraph));
            DeclarationVerdict atNewVersion = CheckEdit(old.Quads(Terms.EventsGraph), bumped.Quads(Terms.EventsGraph));

            return atSameVersion.Accepted == compatible
                && (compatible || atSameVersion.ProblemType == DeclarationVerdict.IncompatibleType)
                && atNewVersion.Accepted;
        }, iter: Edits));

    [Fact]
    public void an_instance_valid_under_a_later_revision_is_valid_under_every_earlier_one() => Property.Run(() =>
        Gen.Select(FieldEdits, Gen.Int[0, 1000]).Sample((edit, seed) =>
        {
            if (!Compatible(edit.Old, edit.New))
            {
                return true;
            }

            EventTypeDeclaration earlier = Declaration(Generators.TypeOf(0, edit.Old, 1, retired: false));
            EventTypeDeclaration later = Declaration(Generators.TypeOf(0, edit.New, 1, retired: false));
            string earlierSchema = Encoding.UTF8.GetString(EventSchemas.Generate(earlier, "https://x.example/1"));
            string laterSchema = Encoding.UTF8.GetString(EventSchemas.Generate(later, "https://x.example/2"));

            // Instances valid under the later revision: each field present or
            // not, with a value of its JSON type.
            Random random = new(seed);

            for (int i = 0; i < 8; i++)
            {
                JsonObject instance = [];

                foreach (FieldSpec field in edit.New)
                {
                    if (field.Required || random.Next(2) == 0)
                    {
                        instance[field.Name] = Sample(field);
                    }
                }

                if (Json.Validate(laterSchema, instance).IsValid && !Json.Validate(earlierSchema, instance).IsValid)
                {
                    return false;
                }
            }

            return true;
        }, iter: Edits));

    private static JsonNode Sample(FieldSpec field) => field.AsString ? JsonValue.Create("x")!
        : field.Datatype.EndsWith("langString", StringComparison.Ordinal) && field.KeepLanguage ? new JsonObject { ["language"] = "en", ["value"] = "x" }
        : field.Datatype.EndsWith("#integer", StringComparison.Ordinal) ? JsonValue.Create(7)!
        : field.Datatype.EndsWith("#decimal", StringComparison.Ordinal) || field.Datatype.EndsWith("#double", StringComparison.Ordinal) ? JsonValue.Create(1.5)!
        : field.Datatype.EndsWith("#boolean", StringComparison.Ordinal) ? JsonValue.Create(true)!
        : field.Datatype.EndsWith("#dateTime", StringComparison.Ordinal) ? JsonValue.Create("2026-10-15T09:30:00Z")!
        : field.Datatype.EndsWith("#date", StringComparison.Ordinal) ? JsonValue.Create("2026-10-15")!
        : field.Datatype.EndsWith("#anyURI", StringComparison.Ordinal) ? JsonValue.Create("https://example.org/x")!
        : JsonValue.Create("x")!;

    private static EventTypeDeclaration Declaration(TypeSpec type)
    {
        DeclarationSet set = EventDeclarations.ReadAll(Trig.Load(type.Quads(Terms.EventsGraph)));
        Assert.Empty(set.Problems);
        return Assert.Single(set.Types);
    }

    private static DeclarationVerdict CheckEdit(List<Q> before, List<Q> after)
    {
        HashSet<Q> then = [.. before], now = [.. after];
        List<FeedChange> changes =
        [
            .. then.Except(now).Select(q => new FeedChange(FeedChangeKind.Retract, q.S, q.P, q.O, q.G)),
            .. now.Except(then).Select(q => new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G)),
        ];
        return EventDeclarationCheck.Check(Trig.Load(now), changes);
    }

    private static readonly SparqlEvaluator Evaluator = new();

    private static readonly Query NamesQuery = SparqlParser.ParseQuery("""
        PREFIX varve: <https://w3id.org/varve/ns#>
        SELECT DISTINCT ?name WHERE {
          GRAPH ?g { ?t a varve:EventType ; varve:eventName ?name }
          FILTER(STRSTARTS(STR(?g), "https://w3id.org/varve/ns#events-catalogue/"))
        }
        """);

    [Fact]
    public void the_list_the_asyncapi_document_and_sparql_agree_at_every_position() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);
            EventCatalogueHistory catalogue = history.Catalogue(history.Head);

            for (long p = 0; p <= history.Head.Value; p++)
            {
                FeedPosition position = new(p);
                EventCatalogueSnapshot snapshot = catalogue.At(position);
                SortedSet<string> sparql = new(StringComparer.Ordinal);

                using (QueryResults results = Evaluator.Evaluate(NamesQuery, history.At(position)))
                {
                    SolutionResults solutions = (SolutionResults)results;

                    while (solutions.MoveNext())
                    {
                        Assert.True(solutions.TryGetTerm(new ColumnIndex(0), out RdfTerm? name));
                        sparql.Add(Encoding.UTF8.GetString(name!.Lexical));
                    }
                }

                JsonNode list = Json.Node(EventViews.DatasetList([.. snapshot.Types.Select(t => new ListedType(Endpoints, t))]));
                SortedSet<string> listed = new(list["types"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()), StringComparer.Ordinal);

                JsonNode asyncApi = Json.Node(EventViews.AsyncApi(Endpoints, snapshot.Types, position, null));
                SortedSet<string> channels = new(asyncApi["channels"]!.AsObject().Select(c => c.Key), StringComparer.Ordinal);

                if (!sparql.SetEquals(listed) || !sparql.SetEquals(channels))
                {
                    return false;
                }
            }

            return true;
        }, iter: Catalogues));

    private static readonly Lazy<string> AsyncApiSchema = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "asyncapi", "asyncapi-3.0.0.json")));

    [Fact]
    public void every_asyncapi_document_validates_against_the_asyncapi_3_schema() => Property.Run(() =>
        Generators.Histories.Sample(steps =>
        {
            History history = Generators.Play(steps);
            EventCatalogueSnapshot snapshot = history.Catalogue(history.Head).Current;
            byte[] document = EventViews.AsyncApi(Endpoints, snapshot.Types, history.Head, new EventHubsServer("ns.servicebus.windows.net", "events"));
            EvaluationResults results = Json.Validate(AsyncApiSchema.Value, Json.Node(document));
            Assert.True(results.IsValid, Json.Text(document) + ": " + Json.Errors(results));
            return true;
        }, iter: Catalogues));
}
