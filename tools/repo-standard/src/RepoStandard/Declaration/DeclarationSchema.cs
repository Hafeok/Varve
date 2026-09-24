// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepoStandard.Declaration;

/// <summary>
/// What a declaration may contain. Validation is strict about names: a key the
/// schema does not know is an error, because a misspelt key would otherwise be
/// a setting nobody manages while its author believes it managed.
/// </summary>
/// <remarks>
/// Inside a ruleset the schema checks only the top level. What a ruleset
/// holds below that is GitHub's shape, and GitHub validates it on write.
/// </remarks>
internal static partial class DeclarationSchema
{
    private static readonly Shape Str = new Leaf("a string", static n => Kind(n) is JsonValueKind.String);
    private static readonly Shape Bool = new Leaf("true or false", static n => Kind(n) is JsonValueKind.True or JsonValueKind.False);
    private static readonly Shape Int = new Leaf("a whole number", static n => Kind(n) is JsonValueKind.Number && n!.ToJsonString().All(char.IsAsciiDigit));
    private static readonly Shape Any = new Leaf("anything", static _ => true);
    private static readonly Shape Colour = new Leaf("a six-digit hex colour, quoted", static n => Kind(n) is JsonValueKind.String && HexColour().IsMatch(n!.GetValue<string>()));
    private static readonly Shape Strings = new ListOf(Str);

    private static Leaf OneOf(params string[] values) =>
        new($"one of {string.Join(", ", values)}", n => Kind(n) is JsonValueKind.String && values.Contains(n!.GetValue<string>(), StringComparer.Ordinal));

    private static readonly Shape Repository = new Obj(new()
    {
        ["description"] = Str,
        ["homepage"] = Str,
        ["topics"] = Strings,
        ["default_branch"] = Str,
        ["features"] = new Obj(new()
        {
            ["issues"] = Bool, ["projects"] = Bool, ["wiki"] = Bool, ["discussions"] = Bool,
        }),
        ["merge"] = new Obj(new()
        {
            ["allow_merge_commit"] = Bool,
            ["allow_squash_merge"] = Bool,
            ["allow_rebase_merge"] = Bool,
            ["allow_auto_merge"] = Bool,
            ["allow_update_branch"] = Bool,
            ["delete_branch_on_merge"] = Bool,
            ["merge_commit_title"] = OneOf("PR_TITLE", "MERGE_MESSAGE"),
            ["merge_commit_message"] = OneOf("PR_BODY", "PR_TITLE", "BLANK"),
            ["squash_merge_commit_title"] = OneOf("PR_TITLE", "COMMIT_OR_PR_TITLE"),
            ["squash_merge_commit_message"] = OneOf("PR_BODY", "COMMIT_MESSAGES", "BLANK"),
        }),
        ["web_commit_signoff_required"] = Bool,
        ["security"] = new Obj(new()
        {
            ["private_vulnerability_reporting"] = Bool,
            ["dependabot_alerts"] = Bool,
            ["dependabot_security_updates"] = Bool,
            ["secret_scanning"] = Bool,
            ["secret_scanning_push_protection"] = Bool,
        }),
    });

    private static readonly Shape Ruleset = new Obj(new()
    {
        ["name"] = Str,
        ["target"] = OneOf("branch", "tag", "push"),
        ["enforcement"] = OneOf("active", "evaluate", "disabled"),
        ["bypass_actors"] = new ListOf(Any),
        ["conditions"] = Any,
        ["rules"] = new ListOf(Any),
        ["absent"] = Bool,
    }, Required: ["name", "target", "enforcement"]);

    private static readonly Shape Environment = new Obj(new()
    {
        ["name"] = Str,
        ["wait_timer"] = Int,
        ["prevent_self_review"] = Bool,
        ["reviewers"] = new ListOf(new Obj(new()
        {
            ["type"] = OneOf("User", "Team"), ["login"] = Str, ["slug"] = Str,
        }, Required: ["type"])),
        ["deployment_branch_policy"] = new Nullable(new Obj(new()
        {
            ["protected_branches"] = Bool, ["custom_branch_policies"] = Bool,
        }, Required: ["protected_branches", "custom_branch_policies"])),
        ["branch_policies"] = new ListOf(new Obj(new()
        {
            ["name"] = Str, ["type"] = OneOf("branch", "tag"),
        }, Required: ["name"])),
        ["secrets"] = Strings,
        ["absent"] = Bool,
    }, Required: ["name"]);

    private static readonly Shape Label = new Obj(new()
    {
        ["name"] = Str, ["color"] = Colour, ["description"] = Str, ["absent"] = Bool,
    }, Required: ["name"]);

    private static readonly Shape Category = new Obj(new()
    {
        ["name"] = Str, ["emoji"] = Str, ["description"] = Str, ["answerable"] = Bool, ["absent"] = Bool,
    }, Required: ["name"]);

    private static readonly Shape Project = new Obj(new()
    {
        ["title"] = Str,
        ["owner"] = Str,
        ["short_description"] = Str,
        ["readme"] = Str,
        ["public"] = Bool,
        ["closed"] = Bool,
        ["status"] = new ListOf(new Obj(new()
        {
            ["name"] = Str,
            ["color"] = OneOf("GRAY", "BLUE", "GREEN", "YELLOW", "ORANGE", "RED", "PINK", "PURPLE"),
            ["description"] = Str,
        }, Required: ["name"])),
        ["absent"] = Bool,
    }, Required: ["title"]);

    private static readonly Shape Actions = new Obj(new()
    {
        ["enabled"] = Bool,
        ["allowed_actions"] = OneOf("all", "local_only", "selected"),
        ["sha_pinning_required"] = Bool,
        ["selected_actions"] = new Obj(new()
        {
            ["github_owned_allowed"] = Bool, ["verified_allowed"] = Bool, ["patterns_allowed"] = Strings,
        }),
        ["workflow"] = new Obj(new()
        {
            ["default_workflow_permissions"] = OneOf("read", "write"),
            ["can_approve_pull_request_reviews"] = Bool,
        }),
    });

    private static readonly Shape Root = new Obj(new()
    {
        ["repository"] = Repository,
        ["rulesets"] = new ListOf(Ruleset, UniqueBy: "name"),
        ["environments"] = new ListOf(Environment, UniqueBy: "name"),
        ["secrets"] = Strings,
        ["labels"] = new ListOf(Label, UniqueBy: "name"),
        ["discussions"] = new Obj(new() { ["categories"] = new ListOf(Category, UniqueBy: "name") }),
        ["projects"] = new ListOf(Project, UniqueBy: "title"),
        ["actions"] = Actions,
    });

    /// <summary>Throws a <see cref="DeclarationException"/> listing every problem.</summary>
    public static void Validate(JsonObject declaration, string source)
    {
        List<string> problems = [];
        Root.Check(declaration, "", problems);

        if (problems.Count == 0)
        {
            CrossCheck(declaration, problems);
        }

        if (problems.Count > 0)
        {
            throw new DeclarationException($"{source} is not a valid declaration:\n  " + string.Join("\n  ", problems));
        }
    }

    /// <summary>The rules GitHub enforces across fields, checked before anything is written.</summary>
    private static void CrossCheck(JsonObject declaration, List<string> problems)
    {
        int index = 0;
        foreach (JsonNode? environment in declaration["environments"] as JsonArray ?? [])
        {
            string path = $"environments[{index++}]";
            JsonNode? policy = environment?["deployment_branch_policy"];
            bool custom = policy?["custom_branch_policies"]?.GetValueKind() is JsonValueKind.True;
            bool protectedOnly = policy?["protected_branches"]?.GetValueKind() is JsonValueKind.True;

            if (custom && protectedOnly)
            {
                problems.Add($"{path}.deployment_branch_policy: protected_branches and custom_branch_policies cannot both be true");
            }

            // Only when the policy is declared: left out, it is the live one,
            // and apply checks it there.
            if (environment?["branch_policies"] is JsonArray { Count: > 0 } && environment is JsonObject e
                && e.ContainsKey("deployment_branch_policy") && !custom)
            {
                problems.Add($"{path}.branch_policies: needs deployment_branch_policy.custom_branch_policies: true");
            }

            if (environment?["prevent_self_review"]?.GetValueKind() is JsonValueKind.True
                && environment["reviewers"] is not JsonArray { Count: > 0 })
            {
                problems.Add($"{path}.prevent_self_review: GitHub keeps it only while there are reviewers; declare reviewers or leave it out");
            }
        }

        JsonNode? actions = declaration["actions"];
        if (actions?["selected_actions"] is not null && actions["allowed_actions"] is JsonNode allowed
            && allowed.GetValueKind() is JsonValueKind.String && allowed.GetValue<string>() != "selected")
        {
            problems.Add("actions.selected_actions: applies only when allowed_actions is selected");
        }
    }

    private static JsonValueKind Kind(JsonNode? node) => node?.GetValueKind() ?? JsonValueKind.Null;

    [GeneratedRegex("^[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColour();

    private abstract record Shape
    {
        public abstract void Check(JsonNode? node, string path, List<string> problems);
    }

    private sealed record Leaf(string Description, Func<JsonNode?, bool> Accepts) : Shape
    {
        public override void Check(JsonNode? node, string path, List<string> problems)
        {
            if (!Accepts(node))
            {
                string got = node is null ? "null" : node.ToJsonString();
                problems.Add($"{path}: expected {Description}, found {got}");
            }
        }
    }

    private sealed record Nullable(Shape Inner) : Shape
    {
        public override void Check(JsonNode? node, string path, List<string> problems)
        {
            if (node is not null)
            {
                Inner.Check(node, path, problems);
            }
        }
    }

    private sealed record ListOf(Shape Item, string? UniqueBy = null) : Shape
    {
        public override void Check(JsonNode? node, string path, List<string> problems)
        {
            if (node is not JsonArray array)
            {
                problems.Add($"{path}: expected a list");
                return;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = $"{path}[{i}]";
                Item.Check(array[i], itemPath, problems);

                if (UniqueBy is not null && array[i]?[UniqueBy] is JsonValue key && Kind(key) is JsonValueKind.String
                    && !seen.Add(key.GetValue<string>()))
                {
                    problems.Add($"{itemPath}: {UniqueBy} '{key.GetValue<string>()}' appears more than once");
                }
            }
        }
    }

    private sealed record Obj(Dictionary<string, Shape> Fields, string[]? Required = null) : Shape
    {
        public override void Check(JsonNode? node, string path, List<string> problems)
        {
            if (node is not JsonObject obj)
            {
                problems.Add($"{(path.Length == 0 ? "(top level)" : path)}: expected a mapping");
                return;
            }

            foreach (string required in Required ?? [])
            {
                if (!obj.ContainsKey(required))
                {
                    problems.Add($"{Join(path, required)}: required");
                }
            }

            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                string childPath = Join(path, property.Key);
                if (!Fields.TryGetValue(property.Key, out Shape? shape))
                {
                    problems.Add($"{childPath}: not a known setting; known here: {string.Join(", ", Fields.Keys)}");
                    continue;
                }

                shape.Check(property.Value, childPath, problems);
            }
        }

        private static string Join(string path, string key) => path.Length == 0 ? key : path + "." + key;
    }
}
