// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Varve.Protocol.Client.Model;

// Members of a server's JSON, read leniently: a member that is absent or of
// another kind is null, so that a body a newer server writes never fails the
// read of what the client does know.
internal static class Json
{
    // A non-empty string member, or null.
    internal static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.String && member.GetString() is { Length: > 0 } text
            ? text
            : null;

    // A non-negative integer member, or null: a position, an epoch or a lag.
    internal static long? Count(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement member) && member.ValueKind == JsonValueKind.Number && member.TryGetInt64(out long number) && number >= 0
            ? number
            : null;

    // Every member but the named ones, cloned: what a newer server adds is
    // kept, not dropped (ADR 0203).
    internal static IReadOnlyDictionary<string, JsonElement> Rest(JsonElement value, params ReadOnlySpan<string> known)
    {
        Dictionary<string, JsonElement> rest = new(StringComparer.Ordinal);

        foreach (JsonProperty member in value.EnumerateObject())
        {
            if (!known.Contains(member.Name))
            {
                rest[member.Name] = member.Value.Clone();
            }
        }

        return rest;
    }
}
