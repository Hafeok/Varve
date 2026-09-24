// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RepoStandard.Json;

namespace RepoStandard.Engine;

/// <summary>
/// The two comparisons a declaration needs.
/// </summary>
/// <remarks>
/// <see cref="Declared(JsonObject, JsonNode?, string)"/> compares what the declaration names and nothing else:
/// a field left out of an object is not managed. An array, once declared, is
/// compared whole, because a list the declaration writes down is exhaustive.
/// <see cref="Whole(JsonNode?, JsonNode?, string)"/> compares in both directions, for resources that are
/// replaced whole on write — a ruleset is one.
/// </remarks>
internal static class Differ
{
    /// <summary>The fields the declaration names that differ from live.</summary>
    public static List<FieldChange> Declared(JsonObject declared, JsonNode? live, string prefix = "")
    {
        List<FieldChange> changes = [];
        Declared(declared, live as JsonObject, prefix, changes);
        return changes;
    }

    /// <summary>Every path at which the two trees differ, in either direction.</summary>
    public static List<FieldChange> Whole(JsonNode? declared, JsonNode? live, string prefix = "")
    {
        List<FieldChange> changes = [];
        Whole(declared, live, prefix, changes);
        return changes;
    }

    private static void Declared(JsonObject declared, JsonObject? live, string prefix, List<FieldChange> changes)
    {
        foreach (KeyValuePair<string, JsonNode?> property in declared)
        {
            string path = Join(prefix, property.Key);
            JsonNode? liveValue = live?[property.Key];

            if (property.Value is JsonObject declaredObject && liveValue is JsonObject)
            {
                Declared(declaredObject, (JsonObject)liveValue, path, changes);
            }
            else if (!JsonTree.Equal(property.Value, liveValue))
            {
                changes.Add(new FieldChange(path, liveValue?.DeepClone(), property.Value?.DeepClone()));
            }
        }
    }

    private static void Whole(JsonNode? declared, JsonNode? live, string prefix, List<FieldChange> changes)
    {
        if (declared is JsonObject a && live is JsonObject b)
        {
            foreach (string key in a.Select(p => p.Key).Union(b.Select(p => p.Key)))
            {
                Whole(a[key], b[key], Join(prefix, key), changes);
            }
        }
        else if (!JsonTree.Equal(declared, live))
        {
            changes.Add(new FieldChange(prefix.Length == 0 ? "(whole)" : prefix, live?.DeepClone(), declared?.DeepClone()));
        }
    }

    private static string Join(string prefix, string key) => prefix.Length == 0 ? key : prefix + "." + key;
}
