// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Json;

namespace RepoStandard.Tests.Support;

/// <summary>
/// An in-memory GitHub for one repository: the endpoints repo-standard calls,
/// answering in the shapes the recorded exchanges show.
/// </summary>
/// <remarks>
/// It is a double at a process boundary, which is the one place the test
/// strategy allows one. It is kept honest by <c>FakeConformanceTests</c>, which
/// replays every recorded GET against it and requires the same status and at
/// least the fields repo-standard reads.
/// </remarks>
internal sealed partial class FakeGitHub : HttpMessageHandler
{
    private long _nextId = 1000;

    public FakeGitHub(string owner = "octo-org", string name = "hello-world")
    {
        Owner = owner;
        Name = name;
        Repo = new JsonObject
        {
            ["id"] = 1,
            ["name"] = name,
            ["full_name"] = owner + "/" + name,
            ["description"] = null,
            ["homepage"] = null,
            ["default_branch"] = "main",
            ["has_issues"] = true,
            ["has_projects"] = true,
            ["has_wiki"] = true,
            ["has_discussions"] = false,
            ["allow_merge_commit"] = true,
            ["allow_squash_merge"] = true,
            ["allow_rebase_merge"] = true,
            ["allow_auto_merge"] = false,
            ["allow_update_branch"] = false,
            ["delete_branch_on_merge"] = false,
            ["merge_commit_title"] = "MERGE_MESSAGE",
            ["merge_commit_message"] = "PR_TITLE",
            ["squash_merge_commit_title"] = "COMMIT_OR_PR_TITLE",
            ["squash_merge_commit_message"] = "COMMIT_MESSAGES",
            ["web_commit_signoff_required"] = false,
            ["security_and_analysis"] = new JsonObject
            {
                ["secret_scanning"] = new JsonObject { ["status"] = "disabled" },
                ["secret_scanning_push_protection"] = new JsonObject { ["status"] = "disabled" },
            },
        };
    }

    public string Owner { get; }

    public string Name { get; }

    public JsonObject Repo { get; }

    public List<string> Topics { get; } = [];

    public bool PrivateVulnerabilityReporting { get; set; }

    public bool VulnerabilityAlerts { get; set; }

    public bool AutomatedSecurityFixes { get; set; }

    public SortedDictionary<long, JsonObject> Rulesets { get; } = [];

    public SortedDictionary<string, FakeEnvironment> Environments { get; } = new(StringComparer.Ordinal);

    public List<string> Secrets { get; } = [];

    public List<JsonObject> Labels { get; } = [];

    public List<JsonObject> Categories { get; } = [];

    public List<FakeProject> Projects { get; } = [];

    public JsonObject ActionsPermissions { get; } = new() { ["enabled"] = true, ["allowed_actions"] = "all", ["sha_pinning_required"] = false };

    public JsonObject SelectedActions { get; } = new() { ["github_owned_allowed"] = true, ["verified_allowed"] = false, ["patterns_allowed"] = new JsonArray() };

    public JsonObject WorkflowPermissions { get; } = new() { ["default_workflow_permissions"] = "read", ["can_approve_pull_request_reviews"] = false };

    /// <summary>Every request, as METHOD path, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Answers requests only; writes fail with 403, as a read-only token's would.</summary>
    public bool ReadOnly { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string query = request.RequestUri.Query;
        string method = request.Method.Method;
        string? text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? body = string.IsNullOrEmpty(text) ? null : JsonNode.Parse(text);
        Requests.Add($"{method} {path}{query}");

        if (path == "/graphql")
        {
            return GraphQl(body!);
        }

        if (ReadOnly && method != "GET")
        {
            return Status(403, new JsonObject { ["message"] = "Resource not accessible by integration" });
        }

        string prefix = $"/repos/{Owner}/{Name}";
        if (path.StartsWith("/users/", StringComparison.Ordinal))
        {
            string login = Uri.UnescapeDataString(path["/users/".Length..]);
            return Ok(new JsonObject { ["login"] = login, ["id"] = IdOf("user:" + login) });
        }

        if (path.StartsWith($"/orgs/{Owner}/teams/", StringComparison.Ordinal))
        {
            string slug = Uri.UnescapeDataString(path[$"/orgs/{Owner}/teams/".Length..]);
            return Ok(new JsonObject { ["slug"] = slug, ["id"] = IdOf("team:" + slug) });
        }

        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Status(404, new JsonObject { ["message"] = "Not Found" });
        }

        string rest = path[prefix.Length..];
        return (method, rest) switch
        {
            ("GET", "") => Ok(Repo.DeepClone()),
            ("PATCH", "") => PatchRepo((JsonObject)body!),
            ("GET", "/topics") => Ok(new JsonObject { ["names"] = JsonTree.StringArray(Topics) }),
            ("PUT", "/topics") => ReplaceTopics(body!),
            ("GET", "/private-vulnerability-reporting") => Ok(new JsonObject { ["enabled"] = PrivateVulnerabilityReporting }),
            ("PUT", "/private-vulnerability-reporting") => Toggle(v => PrivateVulnerabilityReporting = v, true),
            ("DELETE", "/private-vulnerability-reporting") => Toggle(v => PrivateVulnerabilityReporting = v, false),
            ("GET", "/vulnerability-alerts") => VulnerabilityAlerts ? Status(204, null) : Status(404, new JsonObject { ["message"] = "Not Found" }),
            ("PUT", "/vulnerability-alerts") => Toggle(v => VulnerabilityAlerts = v, true),
            ("DELETE", "/vulnerability-alerts") => Toggle(v => VulnerabilityAlerts = v, false),
            ("GET", "/automated-security-fixes") => Ok(new JsonObject { ["enabled"] = AutomatedSecurityFixes, ["paused"] = false }),
            ("PUT", "/automated-security-fixes") => Toggle(v => AutomatedSecurityFixes = v, true),
            ("DELETE", "/automated-security-fixes") => Toggle(v => AutomatedSecurityFixes = v, false),
            ("GET", "/rulesets") => Ok(new JsonArray([.. Rulesets.Values.Select(Summary)])),
            ("POST", "/rulesets") => CreateRuleset((JsonObject)body!),
            ("GET", "/environments") => Ok(new JsonObject
            {
                ["total_count"] = Environments.Count,
                ["environments"] = new JsonArray([.. Environments.Select(e => (JsonNode)EnvironmentBody(e.Key, e.Value))]),
            }),
            ("GET", "/actions/secrets") => Ok(new JsonObject
            {
                ["total_count"] = Secrets.Count,
                ["secrets"] = new JsonArray([.. Secrets.Select(s => (JsonNode)new JsonObject { ["name"] = s })]),
            }),
            ("GET", "/labels") => Ok(new JsonArray([.. Labels.Select(l => l.DeepClone())])),
            ("POST", "/labels") => CreateLabel((JsonObject)body!),
            ("GET", "/actions/permissions") => Ok(ActionsPermissions.DeepClone()),
            ("PUT", "/actions/permissions") => Merge(ActionsPermissions, body!),
            ("GET", "/actions/permissions/selected-actions") => (string?)ActionsPermissions["allowed_actions"] == "selected"
                ? Ok(SelectedActions.DeepClone())
                : Status(409, new JsonObject { ["message"] = "Conflict" }),
            ("PUT", "/actions/permissions/selected-actions") => (string?)ActionsPermissions["allowed_actions"] == "selected"
                ? Merge(SelectedActions, body!)
                : Status(409, new JsonObject { ["message"] = "Conflict" }),
            ("GET", "/actions/permissions/workflow") => Ok(WorkflowPermissions.DeepClone()),
            ("PUT", "/actions/permissions/workflow") => Merge(WorkflowPermissions, body!),
            _ => Nested(method, rest, body),
        };
    }

    private HttpResponseMessage Nested(string method, string rest, JsonNode? body)
    {
        Match ruleset = RulesetPath().Match(rest);
        if (ruleset.Success)
        {
            long id = long.Parse(ruleset.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!Rulesets.TryGetValue(id, out JsonObject? stored))
            {
                return Status(404, new JsonObject { ["message"] = "Not Found" });
            }

            switch (method)
            {
                case "GET":
                    return Ok(Full(id, stored));
                case "PUT":
                    Rulesets[id] = Stored((JsonObject)body!);
                    return Ok(Full(id, Rulesets[id]));
                case "DELETE":
                    Rulesets.Remove(id);
                    return Status(204, null);
                default:
                    break;
            }
        }

        Match label = LabelPath().Match(rest);
        if (label.Success)
        {
            string name = Uri.UnescapeDataString(label.Groups[1].Value);
            JsonObject? existing = Labels.FirstOrDefault(l => string.Equals((string?)l["name"], name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Status(404, new JsonObject { ["message"] = "Not Found" });
            }

            if (method == "DELETE")
            {
                Labels.Remove(existing);
                return Status(204, null);
            }

            if (method == "PATCH")
            {
                if (body?["new_name"] is JsonNode newName)
                {
                    existing["name"] = newName.DeepClone();
                }

                foreach (string field in (string[])["color", "description"])
                {
                    if (body?[field] is JsonNode value)
                    {
                        existing[field] = value.DeepClone();
                    }
                }

                return Ok(existing.DeepClone());
            }
        }

        Match environment = EnvironmentPath().Match(rest);
        if (environment.Success)
        {
            string name = Uri.UnescapeDataString(environment.Groups[1].Value);
            string tail = environment.Groups[2].Value;

            if (tail.Length == 0)
            {
                switch (method)
                {
                    case "PUT":
                        FakeEnvironment env = Environments.TryGetValue(name, out FakeEnvironment? found) ? found : new FakeEnvironment();
                        env.WaitTimer = JsonTree.Int(body, "wait_timer") ?? 0;
                        env.PreventSelfReview = JsonTree.Bool(body, "prevent_self_review") ?? false;
                        env.Reviewers = [.. JsonTree.Items(body, "reviewers").Select(r => ((string)r!["type"]!, JsonTree.Int(r, "id") ?? 0))];
                        env.BranchPolicy = body?["deployment_branch_policy"]?.DeepClone() as JsonObject;
                        if (JsonTree.Bool(env.BranchPolicy, "custom_branch_policies") != true)
                        {
                            env.Policies.Clear();
                        }

                        Environments[name] = env;
                        return Ok(EnvironmentBody(name, env));
                    case "DELETE":
                        Environments.Remove(name);
                        return Status(204, null);
                    default:
                        break;
                }
            }

            if (!Environments.TryGetValue(name, out FakeEnvironment? current))
            {
                return Status(404, new JsonObject { ["message"] = "Not Found" });
            }

            if (tail == "/secrets" && method == "GET")
            {
                return Ok(new JsonObject
                {
                    ["total_count"] = current.Secrets.Count,
                    ["secrets"] = new JsonArray([.. current.Secrets.Select(s => (JsonNode)new JsonObject { ["name"] = s })]),
                });
            }

            if (tail == "/deployment-branch-policies")
            {
                if (JsonTree.Bool(current.BranchPolicy, "custom_branch_policies") != true)
                {
                    return Status(404, new JsonObject { ["message"] = "Not Found" });
                }

                if (method == "GET")
                {
                    return Ok(new JsonObject
                    {
                        ["total_count"] = current.Policies.Count,
                        ["branch_policies"] = new JsonArray([.. current.Policies.Select(p =>
                            (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["type"] = p.Type })]),
                    });
                }

                if (method == "POST")
                {
                    (long, string, string) policy = (_nextId++, JsonTree.Str(body, "name")!, JsonTree.Str(body, "type") ?? "branch");
                    current.Policies.Add(policy);
                    return Ok(new JsonObject { ["id"] = policy.Item1, ["name"] = policy.Item2, ["type"] = policy.Item3 });
                }
            }

            Match policyPath = PolicyPath().Match(tail);
            if (policyPath.Success && method == "DELETE")
            {
                long id = long.Parse(policyPath.Groups[1].Value, CultureInfo.InvariantCulture);
                current.Policies.RemoveAll(p => p.Id == id);
                return Status(204, null);
            }
        }

        return Status(404, new JsonObject { ["message"] = $"fake: no route for {method} {rest}" });
    }

    private HttpResponseMessage GraphQl(JsonNode body)
    {
        string query = JsonTree.Str(body, "query") ?? string.Empty;
        string operation = OperationName().Match(query).Groups[1].Value;
        JsonNode? variables = body["variables"];
        JsonNode? input = variables?["input"];

        if (ReadOnly && query.TrimStart().StartsWith("mutation", StringComparison.Ordinal))
        {
            return Ok(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["message"] = "Resource not accessible by integration" }) });
        }

        JsonNode? data = operation switch
        {
            "DiscussionCategories" => new JsonObject
            {
                ["repository"] = new JsonObject
                {
                    ["hasDiscussionsEnabled"] = JsonTree.Bool(Repo, "has_discussions") ?? false,
                    ["discussionCategories"] = new JsonObject { ["nodes"] = new JsonArray([.. Categories.Select(c => c.DeepClone())]) },
                },
            },
            "LinkedProjects" => new JsonObject
            {
                ["repository"] = new JsonObject
                {
                    ["id"] = RepositoryId,
                    ["projectsV2"] = new JsonObject { ["nodes"] = new JsonArray([.. Projects.Where(p => p.Linked).Select(p => (JsonNode)p.Node())]) },
                },
            },
            "OwnerProjects" => new JsonObject
            {
                ["repositoryOwner"] = new JsonObject
                {
                    ["id"] = "OWNER_" + JsonTree.Str(variables, "login"),
                    ["projectsV2"] = new JsonObject
                    {
                        ["nodes"] = new JsonArray([.. Projects
                            .Where(p => string.Equals(p.Owner, JsonTree.Str(variables, "login"), StringComparison.OrdinalIgnoreCase)
                                && p.Title.Contains(JsonTree.Str(variables, "title") ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                            .Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["title"] = p.Title })]),
                    },
                },
            },
            "CreateProject" => CreateProject(input!),
            "UpdateProject" => UpdateProject(input!),
            "LinkProject" => SetLinked(input!, true),
            "UnlinkProject" => SetLinked(input!, false),
            "UpdateStatusField" => UpdateStatusField(input!),
            "CreateStatusField" => CreateStatusField(input!),
            _ => null,
        };

        return data is null
            ? Ok(new JsonObject { ["errors"] = new JsonArray(new JsonObject { ["message"] = $"fake: unknown operation {operation}" }) })
            : Ok(new JsonObject { ["data"] = data });
    }

    private string RepositoryId => "R_" + Owner + "_" + Name;

    private JsonObject CreateProject(JsonNode input)
    {
        FakeProject project = new()
        {
            Id = "PVT_" + _nextId++,
            Owner = JsonTree.Str(input, "ownerId")!["OWNER_".Length..],
            Title = JsonTree.Str(input, "title")!,
            Linked = JsonTree.Str(input, "repositoryId") == RepositoryId,
            StatusFieldId = "PVTSSF_" + _nextId++,
            Options =
            [
                new JsonObject { ["name"] = "Todo", ["color"] = "GREEN", ["description"] = "This item hasn't been started" },
                new JsonObject { ["name"] = "In Progress", ["color"] = "YELLOW", ["description"] = "This is actively being worked on" },
                new JsonObject { ["name"] = "Done", ["color"] = "PURPLE", ["description"] = "This has been completed" },
            ],
        };

        Projects.Add(project);
        return new JsonObject { ["createProjectV2"] = new JsonObject { ["projectV2"] = project.Node() } };
    }

    private JsonObject UpdateProject(JsonNode input)
    {
        FakeProject project = Projects.First(p => p.Id == JsonTree.Str(input, "projectId"));
        project.ShortDescription = JsonTree.Str(input, "shortDescription") ?? project.ShortDescription;
        project.Readme = JsonTree.Str(input, "readme") ?? project.Readme;
        project.Public = JsonTree.Bool(input, "public") ?? project.Public;
        project.Closed = JsonTree.Bool(input, "closed") ?? project.Closed;
        project.Title = JsonTree.Str(input, "title") ?? project.Title;
        return new JsonObject { ["updateProjectV2"] = new JsonObject { ["projectV2"] = new JsonObject { ["id"] = project.Id } } };
    }

    private JsonObject SetLinked(JsonNode input, bool linked)
    {
        Projects.First(p => p.Id == JsonTree.Str(input, "projectId")).Linked = linked;
        string name = linked ? "linkProjectV2ToRepository" : "unlinkProjectV2FromRepository";
        return new JsonObject { [name] = new JsonObject { ["repository"] = new JsonObject { ["id"] = RepositoryId } } };
    }

    private JsonObject UpdateStatusField(JsonNode input)
    {
        FakeProject project = Projects.First(p => p.StatusFieldId == JsonTree.Str(input, "fieldId"));
        project.Options = [.. JsonTree.Items(input, "singleSelectOptions").Select(o => (JsonObject)o!.DeepClone())];

        // What the real API does: the options are new, so every item loses its Status.
        project.ItemsWithStatus = 0;
        return new JsonObject { ["updateProjectV2Field"] = new JsonObject { ["projectV2Field"] = new JsonObject { ["id"] = project.StatusFieldId } } };
    }

    private JsonObject CreateStatusField(JsonNode input)
    {
        FakeProject project = Projects.First(p => p.Id == JsonTree.Str(input, "projectId"));
        project.StatusFieldId = "PVTSSF_" + _nextId++;
        project.Options = [.. JsonTree.Items(input, "singleSelectOptions").Select(o => (JsonObject)o!.DeepClone())];
        return new JsonObject { ["createProjectV2Field"] = new JsonObject { ["projectV2Field"] = new JsonObject { ["id"] = project.StatusFieldId } } };
    }

    private HttpResponseMessage PatchRepo(JsonObject body)
    {
        foreach ((string key, JsonNode? value) in body)
        {
            if (key == "security_and_analysis")
            {
                foreach ((string feature, JsonNode? setting) in (JsonObject)value!)
                {
                    Repo["security_and_analysis"]![feature] = setting!.DeepClone();
                }

                continue;
            }

            Repo[key] = value?.DeepClone();
        }

        return Ok(Repo.DeepClone());
    }

    private HttpResponseMessage ReplaceTopics(JsonNode body)
    {
        Topics.Clear();
        Topics.AddRange(JsonTree.Items(body, "names").Select(n => n!.GetValue<string>()));
        return Ok(new JsonObject { ["names"] = JsonTree.StringArray(Topics) });
    }

    private HttpResponseMessage CreateRuleset(JsonObject body)
    {
        long id = _nextId++;
        Rulesets[id] = Stored(body);
        return Status(201, Full(id, Rulesets[id]));
    }

    private HttpResponseMessage CreateLabel(JsonObject body)
    {
        string name = JsonTree.Str(body, "name")!;
        if (Labels.Any(l => string.Equals((string?)l["name"], name, StringComparison.OrdinalIgnoreCase)))
        {
            return Status(422, new JsonObject { ["message"] = "Validation Failed" });
        }

        JsonObject label = new()
        {
            ["id"] = _nextId++,
            ["name"] = name,
            ["color"] = JsonTree.Str(body, "color"),
            ["description"] = JsonTree.Str(body, "description"),
            ["default"] = false,
        };

        Labels.Add(label);
        return Status(201, label.DeepClone());
    }

    private static HttpResponseMessage Toggle(Action<bool> set, bool value)
    {
        set(value);
        return Status(204, null);
    }

    private static HttpResponseMessage Merge(JsonObject target, JsonNode body)
    {
        foreach ((string key, JsonNode? value) in (JsonObject)body)
        {
            target[key] = value?.DeepClone();
        }

        return Status(204, null);
    }

    private static JsonObject Stored(JsonObject body)
    {
        JsonObject stored = (JsonObject)body.DeepClone();
        stored.Remove("id");
        return stored;
    }

    private JsonObject Full(long id, JsonObject stored)
    {
        JsonObject full = new() { ["id"] = id };
        foreach ((string key, JsonNode? value) in stored)
        {
            full[key] = value?.DeepClone();
        }

        full["source_type"] = "Repository";
        full["source"] = Owner + "/" + Name;
        full["node_id"] = "RRS_" + id.ToString(CultureInfo.InvariantCulture);
        full["created_at"] = "2026-09-23T12:00:00Z";
        full["updated_at"] = "2026-09-23T12:00:00Z";
        return full;
    }

    private JsonNode Summary(JsonObject stored)
    {
        long id = Rulesets.First(r => ReferenceEquals(r.Value, stored)).Key;
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = stored["name"]?.DeepClone(),
            ["source_type"] = "Repository",
            ["source"] = Owner + "/" + Name,
            ["enforcement"] = stored["enforcement"]?.DeepClone(),
        };
    }

    private JsonObject EnvironmentBody(string name, FakeEnvironment env)
    {
        JsonArray rules = [];
        if (env.WaitTimer > 0)
        {
            rules.Append(new JsonObject { ["id"] = 1, ["type"] = "wait_timer", ["wait_timer"] = env.WaitTimer });
        }

        if (env.Reviewers.Count > 0)
        {
            rules.Append(new JsonObject
            {
                ["id"] = 2,
                ["type"] = "required_reviewers",
                ["prevent_self_review"] = env.PreventSelfReview,
                ["reviewers"] = new JsonArray([.. env.Reviewers.Select(r => (JsonNode)new JsonObject
                {
                    ["type"] = r.Type,
                    ["reviewer"] = r.Type == "Team"
                        ? new JsonObject { ["id"] = r.Id, ["slug"] = NameOf(r.Id, "team:") }
                        : new JsonObject { ["id"] = r.Id, ["login"] = NameOf(r.Id, "user:") },
                })]),
            });
        }

        if (env.BranchPolicy is not null)
        {
            rules.Append(new JsonObject { ["id"] = 3, ["type"] = "branch_policy" });
        }

        return new JsonObject
        {
            ["id"] = IdOf("env:" + name),
            ["name"] = name,
            ["protection_rules"] = rules,
            ["deployment_branch_policy"] = env.BranchPolicy?.DeepClone(),
        };
    }

    private readonly Dictionary<string, long> _ids = new(StringComparer.Ordinal);

    private long IdOf(string key)
    {
        if (!_ids.TryGetValue(key, out long id))
        {
            id = _nextId++;
            _ids[key] = id;
        }

        return id;
    }

    private string NameOf(long id, string prefix) =>
        _ids.First(p => p.Value == id && p.Key.StartsWith(prefix, StringComparison.Ordinal)).Key[prefix.Length..];

    private static HttpResponseMessage Ok(JsonNode body) => Status(200, body);

    private static HttpResponseMessage Status(int status, JsonNode? body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body?.ToJsonString() ?? string.Empty, Encoding.UTF8, "application/json"),
    };

    [GeneratedRegex(@"^/rulesets/(\d+)$")]
    private static partial Regex RulesetPath();

    [GeneratedRegex(@"^/labels/([^/]+)$")]
    private static partial Regex LabelPath();

    [GeneratedRegex(@"^/environments/([^/]+)(.*)$")]
    private static partial Regex EnvironmentPath();

    [GeneratedRegex(@"^/deployment-branch-policies/(\d+)$")]
    private static partial Regex PolicyPath();

    [GeneratedRegex(@"^\s*(?:query|mutation)\s+(\w+)")]
    private static partial Regex OperationName();
}

internal sealed class FakeEnvironment
{
    public long WaitTimer { get; set; }

    public bool PreventSelfReview { get; set; }

    public List<(string Type, long Id)> Reviewers { get; set; } = [];

    public JsonObject? BranchPolicy { get; set; }

    public List<(long Id, string Name, string Type)> Policies { get; } = [];

    public List<string> Secrets { get; } = [];
}

internal sealed class FakeProject
{
    public string Id { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ShortDescription { get; set; } = string.Empty;

    public string Readme { get; set; } = string.Empty;

    public bool Public { get; set; }

    public bool Closed { get; set; }

    public bool Linked { get; set; }

    public string? StatusFieldId { get; set; }

    public List<JsonObject> Options { get; set; } = [];

    public long Items { get; set; }

    public long ItemsWithStatus { get; set; }

    public JsonObject Node() => new()
    {
        ["id"] = Id,
        ["title"] = Title,
        ["shortDescription"] = ShortDescription,
        ["readme"] = Readme,
        ["public"] = Public,
        ["closed"] = Closed,
        ["owner"] = new JsonObject { ["login"] = Owner },
        ["items"] = new JsonObject { ["totalCount"] = Items },
        ["field"] = StatusFieldId is null
            ? null
            : new JsonObject
            {
                ["id"] = StatusFieldId,
                ["options"] = new JsonArray([.. Options.Select(o => o.DeepClone())]),
            },
    };
}
