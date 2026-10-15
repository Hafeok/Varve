// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Json.Canonical;
using Varve.Events.Model;

namespace Varve.Events;

/// <summary>
/// The JSON Schema of an event type's <c>data</c> (docs/spec/events.md §6.1),
/// generated from its declaration when read and never stored. Its bytes for a
/// declaration never change: golden files hold them (ADR 0145,
/// <c>GeneratorGoldenFiles</c>).
/// </summary>
[Contract(typeof(TheEventSchemaRegistry.SchemaComputedOnRead), Role = "the schema generator, under the read-forever rule")]
public static class EventSchemas
{
    /// <summary>The JSON Schema dialect every generated schema states.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>
    /// The schema of <paramref name="declaration"/> in canonical JSON, with
    /// <paramref name="id"/> as its <c>$id</c>, or none when it is
    /// <see langword="null"/> (the form two revisions are compared in).
    /// </summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public static byte[] Generate(EventTypeDeclaration declaration, string? id)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArrayBufferWriter<byte> output = new();
        CanonicalJsonWriter writer = new(output);
        Write(writer, declaration, id);
        return output.WrittenSpan.ToArray();
    }

    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    internal static void Write(CanonicalJsonWriter writer, EventTypeDeclaration declaration, string? id)
    {
        writer.StartObject();

        if (id is not null)
        {
            writer.WriteName("$id"u8);
            writer.WriteString(id);
        }

        writer.WriteName("$schema"u8);
        writer.WriteString(Dialect);
        writer.WriteName("additionalProperties"u8);
        writer.WriteBoolean(true);

        if (declaration.Description is not null)
        {
            writer.WriteName("description"u8);
            writer.WriteString(declaration.Description);
        }

        writer.WriteName("properties"u8);
        writer.StartObject();

        foreach (EventFieldDeclaration field in Sorted(declaration.Fields))
        {
            writer.WriteName(field.Name);
            WriteField(writer, field);
        }

        writer.EndObject();
        writer.WriteName("required"u8);
        writer.StartArray();

        foreach (EventFieldDeclaration field in Sorted(declaration.Fields))
        {
            if (field.Required)
            {
                writer.WriteString(field.Name);
            }
        }

        writer.EndArray();
        writer.WriteName("title"u8);
        writer.WriteString(declaration.Name);
        writer.WriteName("type"u8);
        writer.WriteString("object"u8);
        writer.WriteName("x-varve-version"u8);
        writer.WriteNumber(declaration.Version);
        writer.EndObject();
    }

    private static void WriteField(CanonicalJsonWriter writer, EventFieldDeclaration field)
    {
        JsonKind kind = ValueMapping.KindOf(field);

        if (kind == JsonKind.LanguageObject)
        {
            writer.StartObject();
            writer.WriteName("additionalProperties"u8);
            writer.WriteBoolean(false);

            if (field.Description is not null)
            {
                writer.WriteName("description"u8);
                writer.WriteString(field.Description);
            }

            writer.WriteName("properties"u8);
            writer.StartObject();
            writer.WriteName("direction"u8);
            writer.StartObject();
            writer.WriteName("enum"u8);
            writer.StartArray();
            writer.WriteString("ltr"u8);
            writer.WriteString("rtl"u8);
            writer.EndArray();
            writer.EndObject();
            writer.WriteName("language"u8);
            WriteType(writer, "string"u8);
            writer.WriteName("value"u8);
            WriteType(writer, "string"u8);
            writer.EndObject();
            writer.WriteName("required"u8);
            writer.StartArray();
            writer.WriteString("language"u8);
            writer.WriteString("value"u8);
            writer.EndArray();
            writer.WriteName("type"u8);
            writer.WriteString("object"u8);
            writer.EndObject();
            return;
        }

        writer.StartObject();

        if (field.Description is not null)
        {
            writer.WriteName("description"u8);
            writer.WriteString(field.Description);
        }

        string? format = kind switch
        {
            JsonKind.DateTime => "date-time",
            JsonKind.Date => "date",
            JsonKind.Time => "time",
            JsonKind.Iri => "iri",
            _ => null,
        };

        if (format is not null)
        {
            writer.WriteName("format"u8);
            writer.WriteString(format);
        }

        writer.WriteName("type"u8);
        writer.WriteString(kind switch
        {
            JsonKind.Integer => "integer",
            JsonKind.Number => "number",
            JsonKind.Boolean => "boolean",
            _ => "string",
        });
        writer.EndObject();
    }

    private static void WriteType(CanonicalJsonWriter writer, ReadOnlySpan<byte> type)
    {
        writer.StartObject();
        writer.WriteName("type"u8);
        writer.WriteString(type);
        writer.EndObject();
    }

    /// <summary>Fields in canonical member order (UTF-16 code units), which for the ASCII names a field may have is ordinal order.</summary>
    internal static List<EventFieldDeclaration> Sorted(IReadOnlyList<EventFieldDeclaration> fields)
    {
        List<EventFieldDeclaration> sorted = [.. fields];
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return sorted;
    }
}
