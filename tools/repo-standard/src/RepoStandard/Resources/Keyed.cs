// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RepoStandard.Json;

namespace RepoStandard.Resources;

/// <summary>
/// Pairs a declared list with a live one by a natural key. A declared list is
/// exhaustive: what live has and the declaration does not name is a delete.
/// </summary>
internal static class Keyed
{
    public static (List<JsonObject> Create, List<(JsonObject Declared, JsonObject Live)> Both, List<JsonObject> Delete)
        Match(JsonNode? declared, JsonNode? live, string key, StringComparer? comparer = null)
    {
        comparer ??= StringComparer.Ordinal;
        List<JsonObject> want = Objects(declared);
        List<JsonObject> have = Objects(live);

        Dictionary<string, JsonObject> haveByKey = new(comparer);
        foreach (JsonObject item in have)
        {
            haveByKey.TryAdd(JsonTree.Str(item, key) ?? string.Empty, item);
        }

        HashSet<string> wanted = new(want.Select(item => JsonTree.Str(item, key) ?? string.Empty), comparer);

        List<JsonObject> create = [];
        List<(JsonObject, JsonObject)> both = [];
        foreach (JsonObject item in want)
        {
            if (haveByKey.TryGetValue(JsonTree.Str(item, key) ?? string.Empty, out JsonObject? match))
            {
                both.Add((item, match));
            }
            else
            {
                create.Add(item);
            }
        }

        List<JsonObject> delete = [.. have.Where(item => !wanted.Contains(JsonTree.Str(item, key) ?? string.Empty))];
        return (create, both, delete);
    }

    private static List<JsonObject> Objects(JsonNode? node) =>
        node is JsonArray array ? [.. array.OfType<JsonObject>()] : [];
}
