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
/// <c>projects</c>: the Projects v2 boards linked to the repository — title,
/// short description, readme, visibility, open or closed, and the options of
/// the Status field, which are a board's columns.
/// </summary>
/// <remarks>
/// <para>
/// A board belongs to a user or an organisation, not to the repository, so a
/// board the repository links and the declaration does not name is unlinked,
/// never deleted. A declared board that is not linked is looked up by title
/// under its owner and linked if found, created if not.
/// </para>
/// <para>
/// Not expressible, because the Projects v2 API cannot write it: views (board,
/// table or roadmap layout, grouping, sorting, filters, visible fields), column
/// limits such as a WIP limit (a board-view setting), the built-in workflows,
/// insights charts, and iteration fields. They are left out rather than half
/// supported.
/// </para>
/// <para>
/// Changing the Status options replaces the whole list — the API takes no
/// option ids — so every item on a changed option loses its Status. A board
/// with items is changed only when <c>--allow-status-reset</c> says so.
/// </para>
/// </remarks>
internal sealed class ProjectsResource : IResourceKind
{
    private readonly Dictionary<string, LiveProject> _live = new(StringComparer.Ordinal);
    private string _repositoryId = string.Empty;

    public string Key => "projects";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        JsonNode data = await context.Client.GraphQlAsync(GraphQlOperations.LinkedProjects,
            new JsonObject { ["owner"] = context.Owner, ["name"] = context.Name }, cancellationToken).ConfigureAwait(false);

        JsonNode? repository = data["repository"];
        _repositoryId = JsonTree.Str(repository, "id") ?? string.Empty;

        JsonArray result = [];
        foreach (JsonNode? node in JsonTree.Items(repository?["projectsV2"], "nodes")
            .OrderBy(n => JsonTree.Str(n, "title"), StringComparer.Ordinal))
        {
            string title = JsonTree.Str(node, "title") ?? string.Empty;
            string owner = JsonTree.Str(node?["owner"], "login") ?? context.Owner;
            JsonNode? field = node?["field"];

            _live[LiveKey(owner, title)] = new LiveProject(
                JsonTree.Str(node, "id") ?? string.Empty,
                JsonTree.Str(field, "id"),
                JsonTree.Int(node?["items"], "totalCount") ?? 0);

            JsonObject project = new() { ["title"] = title };
            if (!string.Equals(owner, context.Owner, StringComparison.OrdinalIgnoreCase))
            {
                project["owner"] = owner;
            }

            project["short_description"] = JsonTree.Str(node, "shortDescription") ?? string.Empty;
            project["readme"] = JsonTree.Str(node, "readme") ?? string.Empty;
            project["public"] = JsonTree.Bool(node, "public") ?? false;
            project["closed"] = JsonTree.Bool(node, "closed") ?? false;
            project["status"] = Options(field?["options"]);
            result.Append(project);
        }

        return result;
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context)
    {
        JsonArray want = Normalise(declared, context);
        JsonArray have = Normalise(live ?? new JsonArray(), context);
        (List<JsonObject> create, List<(JsonObject Declared, JsonObject Live)> both, List<JsonObject> delete) =
            Keyed.Match(Keys(want), Keys(have), "key", StringComparer.OrdinalIgnoreCase);

        foreach (JsonObject project in create)
        {
            JsonObject body = Clean(project);
            yield return new Change(Key, Label(body, context), ChangeAction.Create, Differ.Declared(body, null),
                "linked if a board with this title exists under the owner (its settings are then compared on the next run), created if not",
                ct => CreateAsync(context, body, ct));
        }

        foreach ((JsonObject declaredProject, JsonObject liveProject) in both)
        {
            JsonObject body = Clean(declaredProject);
            JsonObject current = Clean(liveProject);
            List<FieldChange> fields = Differ.Declared(body, current);
            if (fields.Count == 0)
            {
                continue;
            }

            LiveProject known = _live[LiveKey(Owner(current, context), JsonTree.Str(current, "title")!)];
            bool statusChanges = fields.Any(f => f.Path == "status");
            string? note = statusChanges && known.Items > 0
                ? $"rewrites the Status options of a board with {known.Items.ToString(CultureInfo.InvariantCulture)} or more items, "
                    + "clearing Status on every item whose option changes; needs --allow-status-reset"
                : null;

            yield return new Change(Key, Label(body, context), ChangeAction.Update, fields, note,
                ct => UpdateAsync(context, known, body, fields, ct));
        }

        foreach (JsonObject project in delete)
        {
            JsonObject body = Clean(project);
            LiveProject known = _live[LiveKey(Owner(body, context), JsonTree.Str(body, "title")!)];
            yield return new Change(Key, Label(body, context), ChangeAction.Delete, [],
                "unlinks the board from the repository; the board itself is not deleted",
                ct => context.Client.GraphQlAsync(GraphQlOperations.UnlinkProject, new JsonObject
                {
                    ["input"] = new JsonObject { ["projectId"] = known.Id, ["repositoryId"] = _repositoryId },
                }, ct));
        }
    }

    private async Task CreateAsync(RepoContext context, JsonObject want, CancellationToken cancellationToken)
    {
        string owner = Owner(want, context);
        string title = JsonTree.Str(want, "title")!;

        JsonNode found = await context.Client.GraphQlAsync(GraphQlOperations.OwnerProjects,
            new JsonObject { ["login"] = owner, ["title"] = title }, cancellationToken).ConfigureAwait(false);

        string ownerId = JsonTree.Str(found["repositoryOwner"], "id")
            ?? throw new GitHubException($"GraphQL OwnerProjects: no user or organisation '{owner}'");

        string? existing = JsonTree.Items(found["repositoryOwner"]?["projectsV2"], "nodes")
            .Where(n => JsonTree.Str(n, "title") == title)
            .Select(n => JsonTree.Str(n, "id"))
            .FirstOrDefault();

        if (existing is not null)
        {
            // Linked, not created. What the board already holds is unknown
            // until it is read, so its settings are compared on the next run
            // rather than written blind.
            await context.Client.GraphQlAsync(GraphQlOperations.LinkProject, new JsonObject
            {
                ["input"] = new JsonObject { ["projectId"] = existing, ["repositoryId"] = _repositoryId },
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonNode created = await context.Client.GraphQlAsync(GraphQlOperations.CreateProject, new JsonObject
        {
            ["input"] = new JsonObject { ["ownerId"] = ownerId, ["title"] = title, ["repositoryId"] = _repositoryId },
        }, cancellationToken).ConfigureAwait(false);

        JsonNode? node = created["createProjectV2"]?["projectV2"];
        LiveProject project = new(JsonTree.Str(node, "id") ?? string.Empty, JsonTree.Str(node?["field"], "id"), 0);

        // A new board comes with GitHub's default Status options, which are
        // compared like any others.
        JsonObject fresh = new() { ["title"] = title, ["status"] = Options(node?["field"]?["options"]) };
        await UpdateAsync(context, project, want, Differ.Declared(want, fresh), cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpdateAsync(
        RepoContext context, LiveProject project, JsonObject want, List<FieldChange> fields, CancellationToken cancellationToken)
    {
        JsonObject input = new() { ["projectId"] = project.Id };
        foreach (FieldChange field in fields)
        {
            string? name = field.Path switch
            {
                "short_description" => "shortDescription",
                "readme" => "readme",
                "public" => "public",
                "closed" => "closed",
                _ => null,
            };

            if (name is not null)
            {
                input[name] = field.Declared?.DeepClone();
            }
        }

        if (input.Count > 1)
        {
            await context.Client.GraphQlAsync(GraphQlOperations.UpdateProject, new JsonObject { ["input"] = input }, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!fields.Any(f => f.Path == "status") || want["status"] is not JsonArray status)
        {
            return;
        }

        if (project.Items > 0 && !context.AllowStatusReset)
        {
            throw new RepoStandardException(
                "not changed: rewriting the Status options of a board that has items clears their Status. "
                + "Run again with --allow-status-reset to accept that.");
        }

        JsonArray options = [];
        foreach (JsonObject option in status.OfType<JsonObject>())
        {
            options.Append(new JsonObject
            {
                ["name"] = JsonTree.Str(option, "name"),
                ["color"] = JsonTree.Str(option, "color") ?? "GRAY",
                ["description"] = JsonTree.Str(option, "description") ?? string.Empty,
            });
        }

        if (project.StatusFieldId is not null)
        {
            await context.Client.GraphQlAsync(GraphQlOperations.UpdateStatusField, new JsonObject
            {
                ["input"] = new JsonObject { ["fieldId"] = project.StatusFieldId, ["singleSelectOptions"] = options },
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await context.Client.GraphQlAsync(GraphQlOperations.CreateStatusField, new JsonObject
            {
                ["input"] = new JsonObject
                {
                    ["projectId"] = project.Id,
                    ["dataType"] = "SINGLE_SELECT",
                    ["name"] = "Status",
                    ["singleSelectOptions"] = options,
                },
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static JsonArray Options(JsonNode? options)
    {
        JsonArray result = [];
        foreach (JsonNode? option in options as JsonArray ?? [])
        {
            result.Append(new JsonObject
            {
                ["name"] = JsonTree.Str(option, "name"),
                ["color"] = JsonTree.Str(option, "color"),
                ["description"] = JsonTree.Str(option, "description") ?? string.Empty,
            });
        }

        return result;
    }

    private static JsonArray Normalise(JsonNode projects, RepoContext context)
    {
        JsonArray result = [];
        foreach (JsonObject project in ((JsonArray)projects).OfType<JsonObject>())
        {
            JsonObject copy = (JsonObject)project.DeepClone();
            if (string.Equals(JsonTree.Str(copy, "owner"), context.Owner, StringComparison.OrdinalIgnoreCase))
            {
                copy.Remove("owner");
            }

            if (copy["status"] is JsonArray status)
            {
                foreach (JsonObject option in status.OfType<JsonObject>())
                {
                    option["color"] = (JsonTree.Str(option, "color") ?? "GRAY").ToUpperInvariant();
                    option["description"] = JsonTree.Str(option, "description") ?? string.Empty;
                }
            }

            result.Append(copy);
        }

        return result;
    }

    /// <summary>Adds a matching key of owner and title, which <see cref="Clean"/> removes.</summary>
    private static JsonArray Keys(JsonArray projects)
    {
        foreach (JsonObject project in projects.OfType<JsonObject>())
        {
            project["key"] = (JsonTree.Str(project, "owner") ?? string.Empty) + "/" + JsonTree.Str(project, "title");
        }

        return projects;
    }

    private static JsonObject Clean(JsonObject project)
    {
        JsonObject copy = (JsonObject)project.DeepClone();
        copy.Remove("key");
        return copy;
    }

    private static string Owner(JsonObject project, RepoContext context) => JsonTree.Str(project, "owner") ?? context.Owner;

    private static string Label(JsonObject project, RepoContext context) =>
        $"{Owner(project, context)}/{JsonTree.Str(project, "title")}";

    private static string LiveKey(string owner, string title) =>
        owner.ToLowerInvariant() + "/" + title;

    private sealed record LiveProject(string Id, string? StatusFieldId, long Items);
}
