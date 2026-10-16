// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Json.Canonical;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>
/// The JSON views of the catalogue (ADR 0144, docs/spec/events.md §6.3, §8):
/// the dataset's types list, the server-wide list, and the AsyncAPI 3.0
/// document, each in canonical JSON, each from the catalogue as of one
/// position, each over the types a caller may see.
/// </summary>
[Contract(typeof(TheEventCatalogue.ThreeViewsAgree), Role = "the list and AsyncAPI views of the catalogue")]
public static class EventViews
{
    /// <summary>The dataset's list: <c>{"types":[…]}</c>, in short-name order.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static byte[] DatasetList(IReadOnlyList<ListedType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        List<ListedType> sorted = [.. types];
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Type.Declaration.Name, b.Type.Declaration.Name));
        return List(sorted, named: false);
    }

    /// <summary>The server-wide list: each entry with its dataset, in dataset then short-name order.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static byte[] ServerList(IReadOnlyList<ListedType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        List<ListedType> sorted = [.. types];
        sorted.Sort(static (a, b) =>
        {
            int byDataset = string.CompareOrdinal(a.Endpoints.Dataset, b.Endpoints.Dataset);
            return byDataset != 0 ? byDataset : string.CompareOrdinal(a.Type.Declaration.Name, b.Type.Declaration.Name);
        });
        return List(sorted, named: true);
    }

    private static byte[] List(List<ListedType> types, bool named)
    {
        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        writer.StartObject();
        writer.WriteName("types"u8);
        writer.StartArray();

        foreach (ListedType listed in types)
        {
            EventTypeDeclaration declaration = listed.Type.Declaration;
            EventEndpoints endpoints = listed.Endpoints;
            writer.StartObject();
            writer.WriteName("class"u8);
            writer.WriteString(declaration.Class.Lexical);

            if (named)
            {
                writer.WriteName("dataset"u8);
                writer.WriteString(endpoints.Dataset);
            }

            if (declaration.Description is not null)
            {
                writer.WriteName("description"u8);
                writer.WriteString(declaration.Description);
            }

            writer.WriteName("name"u8);
            writer.WriteString(declaration.Name);
            writer.WriteName("retired"u8);
            writer.WriteBoolean(declaration.Retired);
            writer.WriteName("schema"u8);
            writer.WriteString(endpoints.Schema(declaration.Name, listed.Type.Current.Version, listed.Type.Current.Revision));
            writer.WriteName("sourceGraph"u8);

            if (declaration.SourceGraph is null)
            {
                writer.WriteNull();
            }
            else
            {
                writer.WriteString(declaration.SourceGraph.Lexical);
            }

            writer.WriteName("version"u8);
            writer.WriteNumber(declaration.Version);
            writer.WriteName("versions"u8);
            writer.StartArray();

            SortedDictionary<int, List<SchemaRevision>> versions = [];

            foreach (SchemaRevision revision in listed.Type.Revisions)
            {
                if (!versions.TryGetValue(revision.Version, out List<SchemaRevision>? list))
                {
                    versions[revision.Version] = list = [];
                }

                list.Add(revision);
            }

            foreach ((int version, List<SchemaRevision> revisions) in versions)
            {
                writer.StartObject();
                writer.WriteName("revisions"u8);
                writer.StartArray();

                foreach (SchemaRevision revision in revisions)
                {
                    writer.WriteString(endpoints.Schema(declaration.Name, revision.Version, revision.Revision));
                }

                writer.EndArray();
                writer.WriteName("schema"u8);
                writer.WriteString(endpoints.Schema(declaration.Name, version));
                writer.WriteName("version"u8);
                writer.WriteNumber(version);
                writer.EndObject();
            }

            writer.EndArray();
            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// The AsyncAPI 3.0.0 document of a dataset's events at <paramref name="position"/>:
    /// a channel per type with its event and the problem event, the structured
    /// CloudEvent as each message's payload, the <c>sse</c> server, and the
    /// <c>eventhubs</c> server when one is configured. AsyncAPI 3.0 has no
    /// CloudEvents binding and no Event Hubs binding; the document describes the
    /// envelope as a payload instead (ADR 0144, <c>NoCloudEventsBindingExists</c>).
    /// </summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static byte[] AsyncApi(EventEndpoints endpoints, IReadOnlyList<CataloguedType> types, FeedPosition position, EventHubsServer? eventHubs)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(types);
        List<CataloguedType> sorted = [.. types];
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Declaration.Name, b.Declaration.Name));

        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        writer.StartObject();
        writer.WriteName("asyncapi"u8);
        writer.WriteString("3.0.0"u8);

        writer.WriteName("channels"u8);
        writer.StartObject();

        foreach (CataloguedType type in sorted)
        {
            string name = type.Declaration.Name;
            writer.WriteName(name);
            writer.StartObject();
            writer.WriteName("address"u8);
            writer.WriteString("datasets/" + endpoints.Dataset + "/events?type=" + name);

            if (type.Declaration.Description is not null)
            {
                writer.WriteName("description"u8);
                writer.WriteString(type.Declaration.Description);
            }

            writer.WriteName("messages"u8);
            writer.StartObject();
            writer.WriteName("event"u8);
            Reference(writer, "#/components/messages/" + name);
            writer.WriteName("problem"u8);
            Reference(writer, "#/components/messages/" + ProblemName);
            writer.EndObject();
            writer.EndObject();
        }

        writer.EndObject();

        writer.WriteName("components"u8);
        writer.StartObject();
        writer.WriteName("messageTraits"u8);
        writer.StartObject();
        writer.WriteName("cloudEvent"u8);
        writer.StartObject();
        writer.WriteName("contentType"u8);
        writer.WriteString("application/cloudevents+json"u8);
        writer.WriteName("description"u8);
        writer.WriteString(
            "A CloudEvents 1.0 event in the structured JSON mode, written in RFC 8785 canonical form: the attributes "
            + "specversion, id (the position, a space and the event node's IRI), source, type, time, subject, datacontenttype, "
            + "dataschema and sequence (the position padded to 20 digits, a dot and the ordinal padded to 6) are members of the "
            + "payload beside data. AsyncAPI 3.0 has no CloudEvents binding and no Event Hubs binding, so the envelope is "
            + "described as the message's payload.");
        writer.WriteName("title"u8);
        writer.WriteString("CloudEvents 1.0, structured JSON"u8);
        writer.EndObject();
        writer.EndObject();

        writer.WriteName("messages"u8);
        writer.StartObject();
        foreach ((string name, CataloguedType? type) in WithProblem(sorted))
        {
            writer.WriteName(name);
            Message(writer, name, type is null ? "An event that could not be rendered, in its place in the stream" : type.Declaration.Description);
        }

        writer.EndObject();

        writer.WriteName("schemas"u8);
        writer.StartObject();
        foreach ((string name, CataloguedType? type) in WithProblem(sorted))
        {
            writer.WriteName(name);
            MultiFormat(writer, w =>
            {
                if (type is null)
                {
                    ProblemEnvelope(w);
                }
                else
                {
                    EventEnvelope(w, endpoints, type);
                }
            });
        }

        writer.EndObject();
        writer.EndObject();

        writer.WriteName("defaultContentType"u8);
        writer.WriteString("application/cloudevents+json"u8);

        writer.WriteName("info"u8);
        writer.StartObject();
        writer.WriteName("description"u8);
        writer.WriteString(
            "The domain events of dataset " + endpoints.Dataset + " as of position " + position.Value.ToString(CultureInfo.InvariantCulture)
            + ", rendered from its events graphs through the declarations of its event catalogue (" + endpoints.Types + ").");
        writer.WriteName("title"u8);
        writer.WriteString("Events of " + endpoints.Dataset);
        writer.WriteName("version"u8);
        writer.WriteString(position.Value.ToString(CultureInfo.InvariantCulture));
        writer.EndObject();

        writer.WriteName("operations"u8);
        writer.StartObject();

        foreach (CataloguedType type in sorted)
        {
            string name = type.Declaration.Name;
            writer.WriteName("receive-" + name);
            writer.StartObject();
            writer.WriteName("action"u8);
            writer.WriteString("receive"u8);
            writer.WriteName("channel"u8);
            Reference(writer, "#/channels/" + name);
            writer.WriteName("messages"u8);
            writer.StartArray();
            Reference(writer, "#/channels/" + name + "/messages/event");
            Reference(writer, "#/channels/" + name + "/messages/problem");
            writer.EndArray();
            writer.EndObject();
        }

        writer.EndObject();

        writer.WriteName("servers"u8);
        writer.StartObject();

        if (eventHubs is not null)
        {
            writer.WriteName("eventhubs"u8);
            writer.StartObject();
            writer.WriteName("description"u8);
            writer.WriteString("Azure Event Hubs over AMQP 1.0, partitioned by dataset: each partition holds the commit order."u8);
            writer.WriteName("host"u8);
            writer.WriteString(eventHubs.Host);
            writer.WriteName("pathname"u8);
            writer.WriteString("/" + eventHubs.Hub);
            writer.WriteName("protocol"u8);
            writer.WriteString("amqp"u8);
            writer.EndObject();
        }

        writer.WriteName("sse"u8);
        writer.StartObject();
        writer.WriteName("description"u8);
        writer.WriteString("Server-sent events on an open range of the events resource, one event per commit, resumed by Last-Event-ID."u8);
        writer.WriteName("host"u8);
        writer.WriteString(endpoints.Host);
        writer.WriteName("pathname"u8);
        writer.WriteString(endpoints.BasePath.Length > 1 ? endpoints.BasePath.TrimEnd('/') : "/");
        writer.WriteName("protocol"u8);
        writer.WriteString(endpoints.Scheme);
        writer.EndObject();
        writer.EndObject();

        writer.EndObject();
        return output.WrittenSpan.ToArray();
    }

    private const string ProblemName = "event-not-rendered";

    /// <summary>The types' names and the problem's, in canonical member order.</summary>
    private static List<(string Name, CataloguedType? Type)> WithProblem(List<CataloguedType> sorted)
    {
        List<(string Name, CataloguedType? Type)> all = [(ProblemName, null)];

        foreach (CataloguedType type in sorted)
        {
            all.Add((type.Declaration.Name, type));
        }

        all.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return all;
    }

    private static void Reference(CanonicalJsonWriter writer, string target)
    {
        writer.StartObject();
        writer.WriteName("$ref"u8);
        writer.WriteString(target);
        writer.EndObject();
    }

    private static void Message(CanonicalJsonWriter writer, string name, string? description)
    {
        writer.StartObject();
        writer.WriteName("contentType"u8);
        writer.WriteString("application/cloudevents+json"u8);

        if (description is not null)
        {
            writer.WriteName("description"u8);
            writer.WriteString(description);
        }

        writer.WriteName("name"u8);
        writer.WriteString(name);
        writer.WriteName("payload"u8);
        Reference(writer, "#/components/schemas/" + name);
        writer.WriteName("title"u8);
        writer.WriteString(name);
        writer.WriteName("traits"u8);
        writer.StartArray();
        Reference(writer, "#/components/messageTraits/cloudEvent");
        writer.EndArray();
        writer.EndObject();
    }

    private static void MultiFormat(CanonicalJsonWriter writer, Action<CanonicalJsonWriter> schema)
    {
        writer.StartObject();
        writer.WriteName("schema"u8);
        schema(writer);
        writer.WriteName("schemaFormat"u8);
        writer.WriteString("application/schema+json;version=draft-2020-12"u8);
        writer.EndObject();
    }

    /// <summary>The structured CloudEvent of a type, its <c>data</c> the type's schema in force.</summary>
    internal static void EventEnvelope(CanonicalJsonWriter writer, EventEndpoints endpoints, CataloguedType type)
    {
        EventTypeDeclaration declaration = type.Declaration;
        writer.StartObject();
        writer.WriteName("$schema"u8);
        writer.WriteString(EventSchemas.Dialect);
        writer.WriteName("properties"u8);
        writer.StartObject();
        writer.WriteName("data"u8);
        EventSchemas.Write(writer, declaration, endpoints.Schema(declaration.Name, type.Current.Version, type.Current.Revision));
        Const(writer, "datacontenttype"u8, "application/json");
        writer.WriteName("dataschema"u8);
        StringOf(writer, "uri");
        writer.WriteName("id"u8);
        StringOf(writer, null);
        writer.WriteName("sequence"u8);
        StringOf(writer, null);
        writer.WriteName("source"u8);
        StringOf(writer, "uri");
        Const(writer, "specversion"u8, "1.0");
        writer.WriteName("subject"u8);
        StringOf(writer, null);
        writer.WriteName("time"u8);
        StringOf(writer, "date-time");
        Const(writer, "type"u8, declaration.Name);
        writer.EndObject();
        Required(writer, "data", "datacontenttype", "dataschema", "id", "sequence", "source", "specversion", "time", "type");
        writer.WriteName("type"u8);
        writer.WriteString("object"u8);
        writer.EndObject();
    }

    /// <summary>The structured CloudEvent of an event that could not be rendered (docs/spec/events.md §4.3).</summary>
    internal static void ProblemEnvelope(CanonicalJsonWriter writer)
    {
        writer.StartObject();
        writer.WriteName("$schema"u8);
        writer.WriteString(EventSchemas.Dialect);
        writer.WriteName("properties"u8);
        writer.StartObject();
        writer.WriteName("data"u8);
        writer.StartObject();
        writer.WriteName("properties"u8);
        writer.StartObject();
        writer.WriteName("event"u8);
        StringOf(writer, null);
        writer.WriteName("eventType"u8);
        StringOf(writer, null);
        writer.WriteName("reason"u8);
        writer.StartObject();
        writer.WriteName("enum"u8);
        writer.StartArray();

        foreach (string reason in (string[])[Problems.BlankEvent, Problems.Datatype, Problems.NoRow, Problems.Number, Problems.Rows, Problems.Types, Problems.UnboundRequired])
        {
            writer.WriteString(reason);
        }

        writer.EndArray();
        writer.EndObject();
        writer.WriteName("status"u8);
        writer.StartObject();
        writer.WriteName("const"u8);
        writer.WriteNumber(422);
        writer.EndObject();
        writer.WriteName("title"u8);
        StringOf(writer, null);
        Const(writer, "type"u8, EventRenderer.ProblemType);
        writer.EndObject();
        Required(writer, "event", "eventType", "reason", "status", "title", "type");
        writer.WriteName("type"u8);
        writer.WriteString("object"u8);
        writer.EndObject();
        Const(writer, "datacontenttype"u8, "application/problem+json");
        writer.WriteName("id"u8);
        StringOf(writer, null);
        writer.WriteName("sequence"u8);
        StringOf(writer, null);
        writer.WriteName("source"u8);
        StringOf(writer, "uri");
        Const(writer, "specversion"u8, "1.0");
        writer.WriteName("time"u8);
        StringOf(writer, "date-time");
        Const(writer, "type"u8, EventRenderer.ProblemType);
        writer.EndObject();
        Required(writer, "data", "datacontenttype", "id", "sequence", "source", "specversion", "time", "type");
        writer.WriteName("type"u8);
        writer.WriteString("object"u8);
        writer.EndObject();
    }

    private static void Const(CanonicalJsonWriter writer, ReadOnlySpan<byte> name, string value)
    {
        writer.WriteName(name);
        writer.StartObject();
        writer.WriteName("const"u8);
        writer.WriteString(value);
        writer.WriteName("type"u8);
        writer.WriteString("string"u8);
        writer.EndObject();
    }

    private static void StringOf(CanonicalJsonWriter writer, string? format)
    {
        writer.StartObject();

        if (format is not null)
        {
            writer.WriteName("format"u8);
            writer.WriteString(format);
        }

        writer.WriteName("type"u8);
        writer.WriteString("string"u8);
        writer.EndObject();
    }

    private static void Required(CanonicalJsonWriter writer, params string[] names)
    {
        writer.WriteName("required"u8);
        writer.StartArray();

        foreach (string name in names)
        {
            writer.WriteString(name);
        }

        writer.EndArray();
    }
}
