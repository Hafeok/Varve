// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Engine;
using RepoStandard.GitHub;
using RepoStandard.Json;

namespace RepoStandard.Resources;

/// <summary>
/// <c>repository</c>: description, homepage, topics, default branch, features,
/// merge methods, sign-off, and the security options.
/// </summary>
internal sealed class RepositoryResource : IResourceKind
{
    /// <summary>Declaration path to the field of <c>GET /repos/{owner}/{repo}</c> that holds it.</summary>
    private static readonly (string Path, string ApiField)[] PatchFields =
    [
        ("description", "description"),
        ("homepage", "homepage"),
        ("default_branch", "default_branch"),
        ("features.issues", "has_issues"),
        ("features.projects", "has_projects"),
        ("features.wiki", "has_wiki"),
        ("features.discussions", "has_discussions"),
        ("merge.allow_merge_commit", "allow_merge_commit"),
        ("merge.allow_squash_merge", "allow_squash_merge"),
        ("merge.allow_rebase_merge", "allow_rebase_merge"),
        ("merge.allow_auto_merge", "allow_auto_merge"),
        ("merge.allow_update_branch", "allow_update_branch"),
        ("merge.delete_branch_on_merge", "delete_branch_on_merge"),
        ("merge.merge_commit_title", "merge_commit_title"),
        ("merge.merge_commit_message", "merge_commit_message"),
        ("merge.squash_merge_commit_title", "squash_merge_commit_title"),
        ("merge.squash_merge_commit_message", "squash_merge_commit_message"),
        ("web_commit_signoff_required", "web_commit_signoff_required"),
    ];

    /// <summary>Security options that live in <c>security_and_analysis</c>.</summary>
    private static readonly (string Name, string ApiField)[] AnalysisFields =
    [
        ("secret_scanning", "secret_scanning"),
        ("secret_scanning_push_protection", "secret_scanning_push_protection"),
    ];

    /// <summary>Security options that have an endpoint each: GET, PUT to enable, DELETE to disable.</summary>
    private static readonly (string Name, Endpoint Get, Endpoint Enable, Endpoint Disable)[] ToggleFields =
    [
        ("private_vulnerability_reporting", Endpoints.GetPrivateVulnerabilityReporting,
            Endpoints.EnablePrivateVulnerabilityReporting, Endpoints.DisablePrivateVulnerabilityReporting),
        ("dependabot_alerts", Endpoints.GetVulnerabilityAlerts,
            Endpoints.EnableVulnerabilityAlerts, Endpoints.DisableVulnerabilityAlerts),
        ("dependabot_security_updates", Endpoints.GetAutomatedSecurityFixes,
            Endpoints.EnableAutomatedSecurityFixes, Endpoints.DisableAutomatedSecurityFixes),
    ];

    public string Key => "repository";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        JsonNode? repo = await context.Client.GetAsync(Endpoints.GetRepository, context.Repo, cancellationToken).ConfigureAwait(false);
        JsonObject result = [];

        foreach ((string path, string apiField) in PatchFields)
        {
            JsonNode? value = repo?[apiField]?.DeepClone();

            // GitHub reports an unset description or homepage as null, and
            // takes "" to unset one. The declaration says "".
            if (value is null && apiField is "description" or "homepage")
            {
                value = JsonValue.Create(string.Empty);
            }

            if (value is not null || repo is JsonObject obj && obj.ContainsKey(apiField))
            {
                Set(result, path, value);
            }
        }

        JsonNode? topics = await context.Client.GetAsync(Endpoints.GetTopics, context.Repo, cancellationToken).ConfigureAwait(false);
        result["topics"] = topics?["names"]?.DeepClone() ?? new JsonArray();

        JsonObject security = [];
        foreach ((string name, Endpoint get, _, _) in ToggleFields)
        {
            ApiResponse response = await context.Client.GetStatusAsync(get, context.Repo, cancellationToken).ConfigureAwait(false);
            security[name] = name switch
            {
                // 204 enabled, 404 disabled. GitHub also answers 404 to a token
                // that may not ask, which reads as disabled; see the README.
                "dependabot_alerts" => response.Status == 204,
                _ => response.Status != 404 && JsonTree.Bool(response.Body, "enabled") == true,
            };
        }

        if (repo?["security_and_analysis"] is JsonObject analysis)
        {
            foreach ((string name, string apiField) in AnalysisFields)
            {
                if (JsonTree.Str(analysis[apiField], "status") is string status)
                {
                    security[name] = status == "enabled";
                }
            }
        }

        result["security"] = security;
        return Order(result);
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        JsonObject want = (JsonObject)declared;
        List<Change> changes = [];

        // Everything PATCH /repos takes, in one request.
        JsonObject patchDeclared = [];
        foreach ((string path, _) in PatchFields)
        {
            if (Get(want, path) is (true, var value))
            {
                Set(patchDeclared, path, value?.DeepClone());
            }
        }

        foreach ((string name, _) in AnalysisFields)
        {
            if (want["security"] is JsonObject s && s.ContainsKey(name))
            {
                Set(patchDeclared, "security." + name, s[name]?.DeepClone());
            }
        }

        List<FieldChange> patchFields = Differ.Declared(patchDeclared, live);
        if (patchFields.Count > 0)
        {
            JsonObject body = [];
            foreach (FieldChange field in patchFields)
            {
                string? apiField = PatchFields.FirstOrDefault(f => f.Path == field.Path).ApiField;
                if (apiField is not null)
                {
                    body[apiField] = field.Declared?.DeepClone();
                    continue;
                }

                string name = field.Path["security.".Length..];
                JsonObject analysis = body["security_and_analysis"] as JsonObject ?? [];
                body["security_and_analysis"] = analysis;
                analysis[AnalysisFields.First(f => f.Name == name).ApiField] = new JsonObject
                {
                    ["status"] = field.Declared is JsonValue v && v.GetValue<bool>() ? "enabled" : "disabled",
                };
            }

            changes.Add(new Change(Key, "settings", ChangeAction.Update, patchFields, null,
                ct => context.Client.WriteAsync(Endpoints.UpdateRepository, context.Repo, body, ct)));
        }

        if (want["topics"] is JsonArray topics && !JsonTree.Equal(JsonTree.SortArrays(topics), JsonTree.SortArrays(live?["topics"])))
        {
            JsonObject body = new() { ["names"] = topics.DeepClone() };
            changes.Add(new Change(Key, "topics", ChangeAction.Update,
                [new FieldChange("topics", live?["topics"]?.DeepClone(), topics.DeepClone())], null,
                ct => context.Client.WriteAsync(Endpoints.ReplaceTopics, context.Repo, body, ct)));
        }

        foreach ((string name, _, Endpoint enable, Endpoint disable) in ToggleFields)
        {
            if (want["security"] is not JsonObject security || !security.ContainsKey(name))
            {
                continue;
            }

            bool on = JsonTree.Bool(security, name) == true;
            JsonNode? current = live?["security"]?[name];
            if (JsonTree.Equal(security[name], current))
            {
                continue;
            }

            changes.Add(new Change(Key, "security." + name, ChangeAction.Update,
                [new FieldChange("security." + name, current?.DeepClone(), security[name]?.DeepClone())], null,
                ct => context.Client.WriteAsync(on ? enable : disable, context.Repo, null, ct)));
        }

        return changes;
    }

    private static JsonObject Order(JsonObject result)
    {
        string[] order = ["description", "homepage", "topics", "default_branch", "features", "merge", "web_commit_signoff_required", "security"];
        JsonObject ordered = [];
        foreach (string key in order)
        {
            if (result.ContainsKey(key))
            {
                ordered[key] = result[key]?.DeepClone();
            }
        }

        return ordered;
    }

    private static (bool Found, JsonNode? Value) Get(JsonObject root, string path)
    {
        JsonNode? node = root;
        foreach (string part in path.Split('.'))
        {
            if (node is not JsonObject obj || !obj.ContainsKey(part))
            {
                return (false, null);
            }

            node = obj[part];
        }

        return (true, node);
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        string[] parts = path.Split('.');
        JsonObject node = root;
        foreach (string part in parts[..^1])
        {
            if (node[part] is not JsonObject child)
            {
                child = [];
                node[part] = child;
            }

            node = child;
        }

        node[parts[^1]] = value;
    }
}
