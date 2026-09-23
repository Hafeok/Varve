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
/// <c>environments</c>: wait timer, required reviewers, self-review, which
/// branches and tags may deploy, and the names of the environment's secrets.
/// </summary>
/// <remarks>
/// Reviewers are declared by login (<c>type: User</c>) or team slug
/// (<c>type: Team</c>, a team of the repository's owner) and resolved to ids
/// when written. Deleting an environment deletes its secrets and deployment
/// history with it; the plan says so.
/// </remarks>
internal sealed class EnvironmentsResource : IResourceKind
{
    private readonly Dictionary<string, Dictionary<string, long>> _policyIds = new(StringComparer.Ordinal);

    public string Key => "environments";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        List<JsonNode> environments = await context.Client
            .GetAllAsync(Endpoints.ListEnvironments, context.Repo, static body => body?["environments"] as JsonArray, cancellationToken)
            .ConfigureAwait(false);

        JsonArray result = [];
        foreach (JsonNode environment in environments.OrderBy(e => JsonTree.Str(e, "name"), StringComparer.Ordinal))
        {
            string name = JsonTree.Str(environment, "name") ?? string.Empty;
            JsonObject item = new() { ["name"] = name };

            long waitTimer = 0;
            bool preventSelfReview = false;
            JsonArray reviewers = [];

            foreach (JsonNode? rule in JsonTree.Items(environment, "protection_rules"))
            {
                switch (JsonTree.Str(rule, "type"))
                {
                    case "wait_timer":
                        waitTimer = JsonTree.Int(rule, "wait_timer") ?? 0;
                        break;
                    case "required_reviewers":
                        preventSelfReview = JsonTree.Bool(rule, "prevent_self_review") ?? false;
                        foreach (JsonNode? reviewer in JsonTree.Items(rule, "reviewers"))
                        {
                            string type = JsonTree.Str(reviewer, "type") ?? "User";
                            reviewers.Append(type == "Team"
                                ? new JsonObject { ["type"] = "Team", ["slug"] = JsonTree.Str(reviewer?["reviewer"], "slug") }
                                : new JsonObject { ["type"] = "User", ["login"] = JsonTree.Str(reviewer?["reviewer"], "login") });
                        }

                        break;
                    default:
                        break;
                }
            }

            item["wait_timer"] = waitTimer;

            // GitHub keeps prevent_self_review inside the required-reviewers
            // rule, which exists only while there are reviewers.
            if (reviewers.Count > 0)
            {
                item["prevent_self_review"] = preventSelfReview;
            }

            item["reviewers"] = JsonTree.SortArrays(reviewers);

            JsonNode? policy = environment["deployment_branch_policy"];
            item["deployment_branch_policy"] = policy is JsonObject
                ? new JsonObject
                {
                    ["protected_branches"] = JsonTree.Bool(policy, "protected_branches") ?? false,
                    ["custom_branch_policies"] = JsonTree.Bool(policy, "custom_branch_policies") ?? false,
                }
                : null;

            JsonArray branchPolicies = [];
            Dictionary<string, long> ids = new(StringComparer.Ordinal);
            if (JsonTree.Bool(policy, "custom_branch_policies") == true)
            {
                List<JsonNode> policies = await context.Client
                    .GetAllAsync(Endpoints.ListBranchPolicies, context.With(name), static body => body?["branch_policies"] as JsonArray, cancellationToken)
                    .ConfigureAwait(false);

                foreach (JsonNode entry in policies)
                {
                    string type = JsonTree.Str(entry, "type") ?? "branch";
                    string pattern = JsonTree.Str(entry, "name") ?? string.Empty;
                    ids[PolicyKey(pattern, type)] = JsonTree.Int(entry, "id") ?? 0;
                    branchPolicies.Append(new JsonObject { ["name"] = pattern, ["type"] = type });
                }
            }

            _policyIds[name] = ids;
            item["branch_policies"] = JsonTree.SortArrays(branchPolicies);

            List<JsonNode> secrets = await context.Client
                .GetAllAsync(Endpoints.ListEnvironmentSecrets, context.With(name), static body => body?["secrets"] as JsonArray, cancellationToken)
                .ConfigureAwait(false);
            item["secrets"] = JsonTree.StringArray(secrets.Select(s => JsonTree.Str(s, "name") ?? string.Empty).Order(StringComparer.Ordinal));

            result.Append(item);
        }

        return result;
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        (List<JsonObject> create, List<(JsonObject Declared, JsonObject Live)> both, List<JsonObject> delete) =
            Keyed.Match(Normalise(declared), live, "name");

        foreach (JsonObject environment in create)
        {
            string name = JsonTree.Str(environment, "name")!;
            JsonObject want = WithoutSecrets(environment);
            yield return new Change(Key, name, ChangeAction.Create, Differ.Declared(want, null), null,
                ct => WriteAsync(context, name, want, null, ct));

            foreach (Change secret in SecretsResource.Compare(Key, $"{name}: secret", environment["secrets"], null))
            {
                yield return secret;
            }
        }

        foreach ((JsonObject declaredEnvironment, JsonObject have) in both)
        {
            string name = JsonTree.Str(have, "name")!;
            JsonObject want = WithoutSecrets(declaredEnvironment);
            List<FieldChange> fields = Differ.Declared(want, WithoutSecrets(have));
            if (fields.Count > 0)
            {
                // Unmanaged fields keep their live values: PUT replaces the
                // environment's protection rules as a whole.
                JsonObject merged = WithoutSecrets(have);
                foreach (KeyValuePair<string, JsonNode?> property in want)
                {
                    merged[property.Key] = property.Value?.DeepClone();
                }

                JsonObject liveCopy = (JsonObject)have.DeepClone();
                yield return new Change(Key, name, ChangeAction.Update, fields, null,
                    ct => WriteAsync(context, name, merged, liveCopy, ct));
            }

            if (declaredEnvironment.ContainsKey("secrets"))
            {
                foreach (Change secret in SecretsResource.Compare(Key, $"{name}: secret", declaredEnvironment["secrets"], have["secrets"]))
                {
                    yield return secret;
                }
            }
        }

        foreach (JsonObject environment in delete)
        {
            string name = JsonTree.Str(environment, "name")!;
            yield return new Change(Key, name, ChangeAction.Delete, [],
                "deletes the environment's secrets and deployment history with it",
                ct => context.Client.WriteAsync(Endpoints.DeleteEnvironment, context.With(name), null, ct));
        }
    }

    private async Task WriteAsync(RepoContext context, string name, JsonObject want, JsonObject? live, CancellationToken cancellationToken)
    {
        JsonArray reviewers = [];
        foreach (JsonObject reviewer in JsonTree.Items(want, "reviewers").OfType<JsonObject>())
        {
            reviewers.Append(await ResolveAsync(context, reviewer, cancellationToken).ConfigureAwait(false));
        }

        JsonObject body = new()
        {
            ["wait_timer"] = JsonTree.Int(want, "wait_timer") ?? 0,
            ["prevent_self_review"] = JsonTree.Bool(want, "prevent_self_review") ?? false,
            ["reviewers"] = reviewers,
            ["deployment_branch_policy"] = want["deployment_branch_policy"]?.DeepClone(),
        };

        await context.Client.WriteAsync(Endpoints.PutEnvironment, context.With(name), body, cancellationToken).ConfigureAwait(false);

        if (!want.ContainsKey("branch_policies") || JsonTree.Bool(want["deployment_branch_policy"], "custom_branch_policies") != true)
        {
            return;
        }

        Dictionary<string, long> existing = _policyIds.TryGetValue(name, out Dictionary<string, long>? ids) && live is not null
            ? ids
            : new Dictionary<string, long>(StringComparer.Ordinal);

        HashSet<string> wanted = new(StringComparer.Ordinal);
        foreach (JsonObject policy in JsonTree.Items(want, "branch_policies").OfType<JsonObject>())
        {
            string pattern = JsonTree.Str(policy, "name") ?? string.Empty;
            string type = JsonTree.Str(policy, "type") ?? "branch";
            wanted.Add(PolicyKey(pattern, type));

            if (!existing.ContainsKey(PolicyKey(pattern, type)))
            {
                await context.Client.WriteAsync(Endpoints.CreateBranchPolicy, context.With(name),
                    new JsonObject { ["name"] = pattern, ["type"] = type }, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach ((string key, long id) in existing.Where(p => !wanted.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            await context.Client.WriteAsync(Endpoints.DeleteBranchPolicy,
                context.With(name, id.ToString(CultureInfo.InvariantCulture)), null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<JsonObject> ResolveAsync(RepoContext context, JsonObject reviewer, CancellationToken cancellationToken)
    {
        if (JsonTree.Str(reviewer, "type") == "Team")
        {
            string slug = JsonTree.Str(reviewer, "slug") ?? string.Empty;
            JsonNode? team = await context.Client.GetAsync(Endpoints.GetTeam, [context.Owner, slug], cancellationToken).ConfigureAwait(false);
            return new JsonObject { ["type"] = "Team", ["id"] = JsonTree.Int(team, "id") };
        }

        string login = JsonTree.Str(reviewer, "login") ?? string.Empty;
        JsonNode? user = await context.Client.GetAsync(Endpoints.GetUser, [login], cancellationToken).ConfigureAwait(false);
        return new JsonObject { ["type"] = "User", ["id"] = JsonTree.Int(user, "id") };
    }

    private static JsonArray Normalise(JsonNode declared)
    {
        JsonArray result = [];
        foreach (JsonObject environment in ((JsonArray)declared).OfType<JsonObject>())
        {
            JsonObject copy = (JsonObject)environment.DeepClone();

            if (copy["reviewers"] is JsonArray { Count: 0 })
            {
                copy.Remove("prevent_self_review");
            }

            // Lists inside an environment are sets.
            foreach (string list in (string[])["reviewers", "branch_policies", "secrets"])
            {
                if (copy[list] is JsonArray array)
                {
                    if (list == "branch_policies")
                    {
                        foreach (JsonObject policy in array.OfType<JsonObject>().Where(p => !p.ContainsKey("type")))
                        {
                            policy["type"] = "branch";
                        }
                    }

                    copy[list] = JsonTree.SortArrays(array);
                }
            }

            result.Append(copy);
        }

        return result;
    }

    private static JsonObject WithoutSecrets(JsonObject environment)
    {
        JsonObject copy = (JsonObject)environment.DeepClone();
        copy.Remove("secrets");
        return copy;
    }

    private static string PolicyKey(string pattern, string type) => type + ":" + pattern;
}
