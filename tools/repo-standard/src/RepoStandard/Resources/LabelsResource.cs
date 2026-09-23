// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Engine;
using RepoStandard.GitHub;
using RepoStandard.Json;

namespace RepoStandard.Resources;

/// <summary>
/// <c>labels</c>: name, colour, description. Keyed by name; a name that
/// changes is a delete and a create, and the delete removes the label from
/// every issue that carries it.
/// </summary>
internal sealed class LabelsResource : IResourceKind
{
    public string Key => "labels";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        List<JsonNode> labels = await context.Client
            .GetAllAsync(Endpoints.ListLabels, context.Repo, static body => body as JsonArray, cancellationToken)
            .ConfigureAwait(false);

        JsonArray result = [];
        foreach (JsonNode label in labels.OrderBy(l => JsonTree.Str(l, "name"), StringComparer.Ordinal))
        {
            result.Append(new JsonObject
            {
                ["name"] = JsonTree.Str(label, "name"),
                ["color"] = Colour(JsonTree.Str(label, "color")),
                ["description"] = JsonTree.Str(label, "description") ?? string.Empty,
            });
        }

        return result;
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        (List<JsonObject> create, List<(JsonObject Declared, JsonObject Live)> both, List<JsonObject> delete) =
            Keyed.Match(Normalise(declared), live, "name", StringComparer.OrdinalIgnoreCase);

        foreach (JsonObject label in create)
        {
            string name = JsonTree.Str(label, "name")!;
            JsonObject body = new()
            {
                ["name"] = name,
                ["color"] = JsonTree.Str(label, "color") ?? "ededed",
                ["description"] = JsonTree.Str(label, "description") ?? string.Empty,
            };

            yield return new Change(Key, name, ChangeAction.Create, Differ.Declared(label, null), null,
                ct => context.Client.WriteAsync(Endpoints.CreateLabel, context.Repo, body, ct));
        }

        foreach ((JsonObject want, JsonObject have) in both)
        {
            List<FieldChange> fields = Differ.Declared(want, have);
            if (fields.Count == 0)
            {
                continue;
            }

            string name = JsonTree.Str(have, "name")!;
            JsonObject body = [];
            foreach (FieldChange field in fields)
            {
                // Label names are case-insensitive on GitHub, so "Bug" and "bug"
                // are one label and a difference in case is a rename.
                body[field.Path == "name" ? "new_name" : field.Path] = field.Declared?.DeepClone();
            }

            yield return new Change(Key, name, ChangeAction.Update, fields, null,
                ct => context.Client.WriteAsync(Endpoints.UpdateLabel, context.With(name), body, ct));
        }

        foreach (JsonObject label in delete)
        {
            string name = JsonTree.Str(label, "name")!;
            yield return new Change(Key, name, ChangeAction.Delete, [], "removes it from every issue and pull request that carries it",
                ct => context.Client.WriteAsync(Endpoints.DeleteLabel, context.With(name), null, ct));
        }
    }

    private static JsonArray Normalise(JsonNode declared)
    {
        JsonArray result = [];
        foreach (JsonObject label in ((JsonArray)declared).OfType<JsonObject>())
        {
            JsonObject copy = (JsonObject)label.DeepClone();
            if (JsonTree.Str(copy, "color") is string colour)
            {
                copy["color"] = Colour(colour);
            }

            result.Append(copy);
        }

        return result;
    }

    private static string Colour(string? colour) =>
        (colour ?? string.Empty).TrimStart('#').ToLower(CultureInfo.InvariantCulture);
}
