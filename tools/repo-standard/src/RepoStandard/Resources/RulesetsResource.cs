// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// <c>rulesets</c>: the repository's own branch and tag rulesets, in the JSON
/// shape GitHub's ruleset export uses — required checks, bypass actors and all.
/// </summary>
/// <remarks>
/// A ruleset is written whole (PUT replaces it), so it is compared whole: a
/// rule added by hand is drift even though the declaration never mentions it.
/// What GitHub adds and a declaration cannot own — the id, the source, links,
/// timestamps — is removed from both sides first, and the arrays inside a
/// ruleset are compared as sets, because GitHub does not promise their order.
/// Rulesets inherited from an organisation are not read.
/// </remarks>
internal sealed class RulesetsResource : IResourceKind
{
    private static readonly ImmutableArray<string> ServerFields =
    [
        "id", "node_id", "source", "source_type", "_links", "created_at", "updated_at", "current_user_can_bypass",
    ];

    private readonly Dictionary<string, long> _ids = new(StringComparer.Ordinal);

    public string Key => "rulesets";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        List<JsonNode> summaries = await context.Client
            .GetAllAsync(Endpoints.ListRulesets, context.Repo, static body => body as JsonArray, cancellationToken)
            .ConfigureAwait(false);

        JsonArray result = [];
        foreach (JsonNode summary in summaries.OrderBy(s => JsonTree.Str(s, "name"), StringComparer.Ordinal))
        {
            long id = JsonTree.Int(summary, "id") ?? 0;
            JsonNode? full = await context.Client
                .GetAsync(Endpoints.GetRuleset, context.With(id.ToString(CultureInfo.InvariantCulture)), cancellationToken)
                .ConfigureAwait(false);

            if (full is not JsonObject ruleset)
            {
                continue;
            }

            _ids[JsonTree.Str(ruleset, "name") ?? string.Empty] = id;
            result.Append(Strip(ruleset));
        }

        return result;
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        (List<JsonObject> create, List<(JsonObject Declared, JsonObject Live)> both, List<JsonObject> delete) =
            Keyed.Match(declared, live, "name");

        foreach (JsonObject ruleset in create)
        {
            string name = JsonTree.Str(ruleset, "name")!;
            JsonObject body = Strip(ruleset);
            yield return new Change(Key, name, ChangeAction.Create, [], null,
                ct => context.Client.WriteAsync(Endpoints.CreateRuleset, context.Repo, body, ct));
        }

        foreach ((JsonObject want, JsonObject have) in both)
        {
            List<FieldChange> fields = Differ.Whole(Canonical(want), Canonical(have));
            if (fields.Count == 0)
            {
                continue;
            }

            string name = JsonTree.Str(have, "name")!;
            string id = _ids[name].ToString(CultureInfo.InvariantCulture);
            JsonObject body = Strip(want);
            yield return new Change(Key, name, ChangeAction.Update, fields, null,
                ct => context.Client.WriteAsync(Endpoints.UpdateRuleset, context.With(id), body, ct));
        }

        foreach (JsonObject ruleset in delete)
        {
            string name = JsonTree.Str(ruleset, "name")!;
            string id = _ids[name].ToString(CultureInfo.InvariantCulture);
            yield return new Change(Key, name, ChangeAction.Delete, [], null,
                ct => context.Client.WriteAsync(Endpoints.DeleteRuleset, context.With(id), null, ct));
        }
    }

    /// <summary>A ruleset without the fields GitHub owns.</summary>
    public static JsonObject Strip(JsonObject ruleset)
    {
        JsonObject copy = (JsonObject)ruleset.DeepClone();
        foreach (string field in ServerFields)
        {
            copy.Remove(field);
        }

        return copy;
    }

    private static JsonNode? Canonical(JsonObject ruleset) => JsonTree.SortArrays(Strip(ruleset));
}
