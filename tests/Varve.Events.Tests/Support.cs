// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Varve.Delta.Model;
using Varve.Events.Model;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Events.Tests;

/// <summary>A quad of terms, the default graph as <see langword="null"/>.</summary>
internal readonly record struct Q(RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G);

internal static class Terms
{
    internal const string Ex = "https://example.org/ns#";
    internal const string Types = "https://example.org/types/";
    internal const string Xsd = "http://www.w3.org/2001/XMLSchema#";
    internal const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    internal static readonly RdfTerm EventsGraph = Iri("https://example.org/events");

    internal static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static RdfTerm Literal(string lexical, string datatype) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), Iri(datatype));

    internal static RdfTerm Plain(string lexical) => RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical));

    internal static RdfTerm Lang(string lexical, string language) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(lexical), Encoding.UTF8.GetBytes(language));
}

/// <summary>TriG in, quads out.</summary>
internal static class Trig
{
    internal const string Prefixes = """
        PREFIX varve: <https://w3id.org/varve/ns#>
        PREFIX xsd:   <http://www.w3.org/2001/XMLSchema#>
        PREFIX rdf:   <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
        PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>
        PREFIX ex:    <https://example.org/ns#>

        """;

    internal static List<Q> Quads(string trig)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Prefixes + trig);
        TurtleOptions options = new() { Syntax = RdfSyntax.TriG };
        TurtleReader reader = new(bytes, in options);
        List<Q> quads = [];

        while (reader.Read())
        {
            quads.Add(new Q(
                reader.Current.Subject.Materialise(),
                reader.Current.Predicate.Materialise(),
                reader.Current.Object.Materialise(),
                reader.Current.HasGraph ? reader.Current.Graph.Materialise() : null));
        }

        Assert.True(reader.Result.Succeeded, reader.Result.FirstError.ToString());
        return quads;
    }

    internal static InMemoryDataset Load(IEnumerable<Q> quads)
    {
        InMemoryDatasetBuilder builder = new();

        foreach (Q q in quads)
        {
            builder.Add(q.S, q.P, q.O, q.G);
        }

        return builder.ToDataset();
    }

    internal static InMemoryDataset Load(string trig) => Load(Quads(trig));
}

/// <summary>Writes declarations as TriG, the way a catalogue graph holds them.</summary>
internal sealed record FieldSpec(string Name, string Datatype, bool Required, bool AsString = false, bool KeepLanguage = false, string? Description = null);

internal sealed record TypeSpec(string Name, string Class, int Version, IReadOnlyList<FieldSpec> Fields, string? Subject = null, string? Select = null, bool Retired = false, string? Description = null)
{
    /// <summary>The default query: required fields as patterns, optional ones under OPTIONAL, values read from ex:{field}.</summary>
    internal string Query
    {
        get
        {
            if (Select is not null)
            {
                return Select;
            }

            IEnumerable<string> names = Fields.Select(f => f.Name).Concat(Subject is null ? [] : [Subject]);
            StringBuilder where = new();

            foreach (FieldSpec spec in Fields.Where(f => f.Required))
            {
                where.Append(" ?event <").Append(Terms.Ex).Append(spec.Name).Append("> ?").Append(spec.Name).Append(" .");
            }

            foreach (FieldSpec spec in Fields.Where(f => !f.Required))
            {
                where.Append(" OPTIONAL { ?event <").Append(Terms.Ex).Append(spec.Name).Append("> ?").Append(spec.Name).Append(" }");
            }

            if (Subject is not null)
            {
                where.Append(" BIND(?event AS ?").Append(Subject).Append(')');
            }

            if (where.Length == 0)
            {
                where.Append(" ?event a ?class");
            }

            return "SELECT " + string.Join(' ', names.Select(n => "?" + n)) + " WHERE {" + where + " }";
        }
    }

    internal string Node => Terms.Types + Name;

    internal string Trig(RdfTerm? sourceGraph)
    {
        StringBuilder text = new();
        text.Append('<').Append(Encoding.UTF8.GetString(EventCatalogue.GraphOf(sourceGraph).Lexical)).Append("> {\n");
        text.Append("  <").Append(Node).Append("> a varve:EventType ;\n");
        text.Append("    varve:eventName \"").Append(Name).Append("\" ;\n");
        text.Append("    varve:eventClass <").Append(Class).Append("> ;\n");
        text.Append("    varve:eventVersion ").Append(Version).Append(" ;\n");

        if (Subject is not null)
        {
            text.Append("    varve:eventSubject \"").Append(Subject).Append("\" ;\n");
        }

        if (Retired)
        {
            text.Append("    varve:eventRetired true ;\n");
        }

        if (Description is not null)
        {
            text.Append("    rdfs:comment \"").Append(Description).Append("\" ;\n");
        }

        foreach (FieldSpec field in Fields)
        {
            text.Append("    varve:eventField <").Append(Node).Append("/field/").Append(field.Name).Append("> ;\n");
        }

        text.Append("    varve:eventSelect \"\"\"").Append(Query).Append("\"\"\" .\n");

        foreach (FieldSpec field in Fields)
        {
            text.Append("  <").Append(Node).Append("/field/").Append(field.Name).Append("> a varve:EventField ;\n");
            text.Append("    varve:fieldName \"").Append(field.Name).Append("\" ;\n");
            text.Append("    varve:fieldDatatype <").Append(field.Datatype).Append("> ;\n");

            if (field.AsString)
            {
                text.Append("    varve:fieldAs \"string\" ;\n");
            }

            if (field.KeepLanguage)
            {
                text.Append("    varve:fieldKeepLanguage true ;\n");
            }

            if (field.Description is not null)
            {
                text.Append("    rdfs:comment \"").Append(field.Description).Append("\" ;\n");
            }

            text.Append("    varve:fieldRequired ").Append(field.Required ? "true" : "false").Append(" .\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    internal List<Q> Quads(RdfTerm? sourceGraph) => global::Varve.Events.Tests.Trig.Quads(Trig(sourceGraph));
}

/// <summary>
/// A dataset's history in memory, as a store keeps it: each commit's
/// effective delta (I2: an assertion of what holds, or a retraction of what
/// does not, is no change), and the state at every position.
/// </summary>
internal sealed class History
{
    private readonly List<HashSet<Q>> _states = [[]];
    private readonly List<(FeedPosition Position, FeedTimestamp Timestamp, List<FeedChange> Changes)> _commits = [];

    internal IReadOnlyList<(FeedPosition Position, FeedTimestamp Timestamp, List<FeedChange> Changes)> Commits => _commits;

    internal FeedPosition Head => new(_commits.Count);

    /// <summary>Commits one change set; answers its position, or none when its effective delta is empty.</summary>
    internal FeedPosition? Commit(IEnumerable<Q> retract, IEnumerable<Q> assert)
    {
        HashSet<Q> state = new(_states[^1]);
        List<FeedChange> changes = [];

        foreach (Q q in retract)
        {
            if (state.Remove(q))
            {
                changes.Add(new FeedChange(FeedChangeKind.Retract, q.S, q.P, q.O, q.G));
            }
        }

        foreach (Q q in assert)
        {
            if (state.Add(q))
            {
                changes.Add(new FeedChange(FeedChangeKind.Assert, q.S, q.P, q.O, q.G));
            }
        }

        if (changes.Count == 0)
        {
            return null;
        }

        _states.Add(state);
        FeedPosition position = new(_commits.Count + 1);
        _commits.Add((position, new FeedTimestamp(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).AddSeconds(position.Value)), changes));
        return position;
    }

    internal InMemoryDataset At(FeedPosition position) => Trig.Load(_states[(int)position.Value]);

    /// <summary>The catalogue history replayed from position 1 to <paramref name="upTo"/>.</summary>
    internal EventCatalogueHistory Catalogue(FeedPosition upTo)
    {
        EventCatalogueHistory history = new();

        foreach ((FeedPosition position, FeedTimestamp _, List<FeedChange> changes) in _commits)
        {
            if (position > upTo)
            {
                break;
            }

            history.Apply(position, changes);
        }

        return history;
    }

    internal static EventCommit ToCommit(FeedPosition position, FeedTimestamp timestamp, IEnumerable<FeedChange> changes)
    {
        List<TypeAssertion> assertions = [];

        foreach (FeedChange change in changes)
        {
            if (change.Kind == FeedChangeKind.Assert && change.Predicate.Equals(Terms.Iri(Terms.RdfType)))
            {
                assertions.Add(new TypeAssertion(change.Subject, change.Object, change.Graph));
            }
        }

        return new EventCommit(position, timestamp, assertions);
    }

    /// <summary>Renders every commit with the catalogue replayed to <paramref name="catalogueUpTo"/> and read at each commit's position.</summary>
    internal List<RenderedEvent> RenderAll(EventEndpoints endpoints, FeedPosition catalogueUpTo)
    {
        EventCatalogueHistory catalogue = Catalogue(catalogueUpTo);
        EventRenderer renderer = new();
        List<RenderedEvent> events = [];

        foreach ((FeedPosition position, FeedTimestamp timestamp, List<FeedChange> changes) in _commits)
        {
            if (position > catalogueUpTo)
            {
                break;
            }

            EventCommit commit = ToCommit(position, timestamp, changes);
            EventCatalogueSnapshot snapshot = catalogue.At(position);

            if (EventRenderer.HasEvents(commit, snapshot))
            {
                events.AddRange(renderer.Render(commit, snapshot, At(position), endpoints));
            }
        }

        return events;
    }
}

internal static class Property
{
    /// <summary>Runs a CsCheck sample, carrying the failing case's own message out with the seed.</summary>
    internal static void Run(Action sample)
    {
        try
        {
            sample();
        }
        catch (CsCheck.CsCheckException e) when (e.InnerException is not null)
        {
            throw new InvalidOperationException(e.Message + Environment.NewLine + e.InnerException.Message, e);
        }
    }
}

internal static class Json
{
    internal static string Text(ReadOnlyMemory<byte> bytes) => Encoding.UTF8.GetString(bytes.Span);

    internal static JsonNode Node(ReadOnlyMemory<byte> bytes) => JsonNode.Parse(Text(bytes))!;

    /// <summary>Validates an instance against a schema, with format assertion on and a registry of its own.</summary>
    internal static EvaluationResults Validate(string schema, JsonNode? instance)
    {
        // A registry of its own per schema: revisions of one type share a
        // base of $id, and the global registry refuses a second content.
        JsonSchema parsed = JsonSchema.FromText(schema, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        EvaluationOptions options = new() { OutputFormat = OutputFormat.List, RequireFormatValidation = true };
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        return parsed.Evaluate(document.RootElement.Clone(), options);
    }

    internal static string Errors(EvaluationResults results) =>
        string.Join("; ", (results.Details ?? []).Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => d.InstanceLocation + " " + e.Key + ": " + e.Value)));
}
