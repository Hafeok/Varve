// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Engine;
using RepoStandard.GitHub;
using RepoStandard.Json;

namespace RepoStandard.Resources;

/// <summary>
/// <c>discussions</c>: the repository's Discussions categories.
/// </summary>
/// <remarks>
/// Read, compared and reported; never written. GitHub has no API — REST or
/// GraphQL — that creates, edits or deletes a Discussions category, so every
/// difference here is one a person makes in the repository's settings. Whether
/// Discussions is on at all is <c>repository.features.discussions</c>, which
/// can be written. A category's format (open, question and answer,
/// announcement, poll) is exposed only as <c>answerable</c>.
/// </remarks>
internal sealed class DiscussionsResource : IResourceKind
{
    private const string Manual = "GitHub has no API that writes Discussions categories; change it in the repository's settings";

    public string Key => "discussions";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        JsonNode data = await context.Client.GraphQlAsync(GraphQlOperations.DiscussionCategories,
            new JsonObject { ["owner"] = context.Owner, ["name"] = context.Name }, cancellationToken).ConfigureAwait(false);

        JsonArray categories = [];
        foreach (JsonNode? node in JsonTree.Items(data["repository"]?["discussionCategories"], "nodes"))
        {
            categories.Append(new JsonObject
            {
                ["name"] = JsonTree.Str(node, "name"),
                ["emoji"] = JsonTree.Str(node, "emoji"),
                ["description"] = JsonTree.Str(node, "description") ?? string.Empty,
                ["answerable"] = JsonTree.Bool(node, "isAnswerable") ?? false,
            });
        }

        return new JsonObject { ["categories"] = categories };
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        if (declared["categories"] is not JsonArray)
        {
            yield break;
        }

        (List<JsonObject> create, List<(JsonObject Declared, JsonObject Live)> both, List<JsonObject> delete) =
            Keyed.Match(declared["categories"], live?["categories"], "name");

        foreach (JsonObject category in create)
        {
            yield return new Change(Key, $"category {JsonTree.Str(category, "name")}", ChangeAction.Unfixable,
                Differ.Declared(category, null), "declared and not present. " + Manual, null);
        }

        foreach ((JsonObject want, JsonObject have) in both)
        {
            List<FieldChange> fields = Differ.Declared(want, have);
            if (fields.Count > 0)
            {
                yield return new Change(Key, $"category {JsonTree.Str(have, "name")}", ChangeAction.Unfixable, fields, Manual, null);
            }
        }

        foreach (JsonObject category in delete)
        {
            yield return new Change(Key, $"category {JsonTree.Str(category, "name")}", ChangeAction.Unfixable, [],
                "present and not declared. " + Manual, null);
        }
    }
}
