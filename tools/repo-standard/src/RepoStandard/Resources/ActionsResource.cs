// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Engine;
using RepoStandard.GitHub;
using RepoStandard.Json;

namespace RepoStandard.Resources;

/// <summary>
/// <c>actions</c>: whether Actions run, which actions may, whether they must
/// be pinned to a full SHA, and what the workflow token may do.
/// </summary>
internal sealed class ActionsResource : IResourceKind
{
    private static readonly ImmutableArray<string> PermissionFields = ["enabled", "allowed_actions", "sha_pinning_required"];

    public string Key => "actions";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        JsonNode? permissions = await context.Client
            .GetAsync(Endpoints.GetActionsPermissions, context.Repo, cancellationToken).ConfigureAwait(false);

        JsonObject result = [];
        foreach (string field in PermissionFields)
        {
            if (permissions?[field] is JsonNode value)
            {
                result[field] = value.DeepClone();
            }
        }

        // Only meaningful, and only readable, when the policy is "selected".
        if (JsonTree.Str(permissions, "allowed_actions") == "selected")
        {
            JsonNode? selected = await context.Client
                .GetAsync(Endpoints.GetSelectedActions, context.Repo, cancellationToken).ConfigureAwait(false);
            result["selected_actions"] = new JsonObject
            {
                ["github_owned_allowed"] = JsonTree.Bool(selected, "github_owned_allowed") ?? false,
                ["verified_allowed"] = JsonTree.Bool(selected, "verified_allowed") ?? false,
                ["patterns_allowed"] = JsonTree.SortArrays(selected?["patterns_allowed"] ?? new JsonArray()),
            };
        }

        JsonNode? workflow = await context.Client
            .GetAsync(Endpoints.GetWorkflowPermissions, context.Repo, cancellationToken).ConfigureAwait(false);
        result["workflow"] = new JsonObject
        {
            ["default_workflow_permissions"] = JsonTree.Str(workflow, "default_workflow_permissions"),
            ["can_approve_pull_request_reviews"] = JsonTree.Bool(workflow, "can_approve_pull_request_reviews"),
        };

        return result;
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        JsonObject want = (JsonObject)declared.DeepClone();
        if (want["selected_actions"]?["patterns_allowed"] is JsonArray patterns)
        {
            want["selected_actions"]!["patterns_allowed"] = JsonTree.SortArrays(patterns);
        }

        JsonObject permissions = [];
        foreach (string field in PermissionFields.Where(want.ContainsKey))
        {
            permissions[field] = want[field]?.DeepClone();
        }

        List<FieldChange> permissionFields = Differ.Declared(permissions, live);
        if (permissionFields.Count > 0)
        {
            // PUT requires "enabled"; the rest keep their live values.
            JsonObject body = [];
            foreach (string field in PermissionFields)
            {
                JsonNode? value = permissions.ContainsKey(field) ? permissions[field] : live?[field];
                if (value is not null)
                {
                    body[field] = value.DeepClone();
                }
            }

            yield return new Change(Key, "permissions", ChangeAction.Update, permissionFields, null,
                ct => context.Client.WriteAsync(Endpoints.SetActionsPermissions, context.Repo, body, ct));
        }

        if (want["selected_actions"] is JsonObject selected)
        {
            List<FieldChange> fields = Differ.Declared(selected, live?["selected_actions"], "selected_actions");
            if (fields.Count > 0)
            {
                JsonObject body = (JsonObject)selected.DeepClone();
                yield return new Change(Key, "selected_actions", ChangeAction.Update, fields, null,
                    ct => context.Client.WriteAsync(Endpoints.SetSelectedActions, context.Repo, body, ct));
            }
        }

        if (want["workflow"] is JsonObject workflow)
        {
            List<FieldChange> fields = Differ.Declared(workflow, live?["workflow"], "workflow");
            if (fields.Count > 0)
            {
                JsonObject body = (JsonObject)(live?["workflow"]?.DeepClone() ?? new JsonObject());
                foreach (KeyValuePair<string, JsonNode?> property in workflow)
                {
                    body[property.Key] = property.Value?.DeepClone();
                }

                yield return new Change(Key, "workflow", ChangeAction.Update, fields, null,
                    ct => context.Client.WriteAsync(Endpoints.SetWorkflowPermissions, context.Repo, body, ct));
            }
        }
    }
}
