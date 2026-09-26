// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoStandard.Json;

/// <summary>Comparison, canonical ordering and small accessors over JSON trees.</summary>
internal static class JsonTree
{
    // For people to read, not for a parser: "Q&A" rather than "Q\u0026A".
    // Made per call: JsonSerializerOptions is mutable until first use, and a
    // shared static one is shared mutable state (DD0004). Display is for a
    // drift report, not a loop.
    private static JsonSerializerOptions DisplayOptions() => new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Structural equality. Object members compare by name whatever their
    /// order; arrays compare in order; numbers compare by value, so 30 and
    /// 30.0 are equal.
    /// </summary>
    public static bool Equal(JsonNode? left, JsonNode? right)
    {
        switch (left, right)
        {
            case (null, null):
                return true;
            case (null, _) or (_, null):
                return false;
            case (JsonObject a, JsonObject b):
                return a.Count == b.Count
                    && a.All(property => b.TryGetPropertyValue(property.Key, out JsonNode? other) && Equal(property.Value, other));
            case (JsonArray a, JsonArray b):
                return a.Count == b.Count && a.Zip(b).All(pair => Equal(pair.First, pair.Second));
            case (JsonValue a, JsonValue b):
                return ValueEqual(a, b);
            default:
                return false;
        }
    }

    /// <summary>
    /// A copy in which every array is sorted by the canonical text of its
    /// elements, for trees whose arrays are sets rather than sequences. Object
    /// members keep their order, which is only a matter of how it reads.
    /// </summary>
    public static JsonNode? SortArrays(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                JsonObject copy = [];
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    copy[property.Key] = SortArrays(property.Value);
                }

                return copy;
            }

            case JsonArray array:
            {
                List<JsonNode?> items = [.. array.Select(SortArrays)];
                items.Sort((a, b) => string.CompareOrdinal(Canonical(a), Canonical(b)));
                JsonArray copy = [];
                foreach (JsonNode? item in items)
                {
                    copy.Add(item);
                }

                return copy;
            }

            default:
                return node?.DeepClone();
        }
    }

    /// <summary>The tree as compact JSON with object members in ordinal order.</summary>
    public static string Canonical(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return "null";
            case JsonObject obj:
                return "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => JsonValue.Create(p.Key).ToJsonString() + ":" + Canonical(p.Value))) + "}";
            case JsonArray array:
                return "[" + string.Join(",", array.Select(Canonical)) + "]";
            case JsonValue value when value.GetValueKind() is JsonValueKind.Number:
                return decimal.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
            default:
                return node.ToJsonString();
        }
    }

    /// <summary>The tree as display text: strings quoted, the rest as JSON.</summary>
    public static string Display(JsonNode? node) => node is null ? "(absent)" : node.ToJsonString(DisplayOptions());

    /// <summary>A string member, or null when absent or not a string.</summary>
    public static string? Str(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.GetValueKind() is JsonValueKind.String
            ? value.GetValue<string>()
            : null;

    /// <summary>A boolean member, or null when absent or not a boolean.</summary>
    public static bool? Bool(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    /// <summary>An integer member, or null when absent or not a number.</summary>
    public static long? Int(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.GetValueKind() is JsonValueKind.Number
            ? (long)decimal.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;

    /// <summary>The elements of an array member; empty when absent.</summary>
    public static IEnumerable<JsonNode?> Items(JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonArray array ? array : [];

    /// <summary>An array of strings from a sequence.</summary>
    public static JsonArray StringArray(IEnumerable<string> values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Append(JsonValue.Create(value));
        }

        return array;
    }

    /// <summary>
    /// Adds a node. <see cref="JsonArray.Add{T}(T)"/> is what C# picks for a
    /// <see cref="JsonObject"/> argument, and it is not AOT-safe; this one
    /// takes the node as a node.
    /// </summary>
    public static void Append(this JsonArray array, JsonNode? node) => array.Add(node);

    /// <summary>A JSON value for a nullable string.</summary>
    public static JsonNode? Value(string? value) => value is null ? null : JsonValue.Create(value);

    private static bool ValueEqual(JsonValue a, JsonValue b)
    {
        JsonValueKind kindA = a.GetValueKind();
        JsonValueKind kindB = b.GetValueKind();

        if (kindA != kindB)
        {
            return false;
        }

        return kindA switch
        {
            JsonValueKind.String => string.Equals(a.GetValue<string>(), b.GetValue<string>(), StringComparison.Ordinal),
            JsonValueKind.Number => string.Equals(Canonical(a), Canonical(b), StringComparison.Ordinal),
            _ => true,
        };
    }
}
