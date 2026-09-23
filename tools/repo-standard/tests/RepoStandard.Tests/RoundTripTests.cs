// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CsCheck;
using RepoStandard.Json;
using RepoStandard.Tests.Support;
using RepoStandard.Yaml;
using Xunit;

namespace RepoStandard.Tests;

/// <summary>
/// The round-trip property: <c>apply</c>, then <c>export</c>, then <c>plan</c>
/// shows no difference — for the declaration applied, and for the export.
/// </summary>
/// <remarks>
/// Run against the in-memory GitHub, starting from a state some other
/// generated declaration left behind, so that every apply is a mix of creates,
/// updates and deletes rather than a write onto an empty repository.
/// Discussions categories and secret names cannot be written, so the fake is
/// seeded with the declared ones and the property checks that they compare
/// clean rather than that they converge.
/// </remarks>
public sealed class RoundTripTests
{
    [Fact]
    public void Apply_then_export_then_plan_shows_no_difference()
    {
        Gen.Select(Generators.Declaration, Generators.Declaration).Sample(pair =>
        {
            RunAsync(pair.Item1, pair.Item2).GetAwaiter().GetResult();
        }, iter: 150, threads: 1);
    }

    private static async Task RunAsync(JsonObject before, JsonObject declaration)
    {
        FakeGitHub fake = new();
        Seed(fake, declaration);

        string directory = Directory.CreateTempSubdirectory("repo-standard-").FullName;
        try
        {
            await ApplyAsync(fake, directory, "before.yaml", before, expectConverged: false);

            string file = await ApplyAsync(fake, directory, "declaration.yaml", declaration, expectConverged: true);

            (int planExit, string planOutput) = await RunAsync(fake, "check", "--repo", "octo-org/hello-world", "--file", file);
            Assert.True(planExit == 0, $"plan after apply is not empty:\n{planOutput}\ndeclaration:\n{YamlJson.Write(declaration)}");

            string exported = Path.Combine(directory, "exported.yaml");
            (int exportExit, string exportOutput) = await RunAsync(fake, "export", "--repo", "octo-org/hello-world", "--file", exported);
            Assert.True(exportExit == 0, exportOutput);

            (int exportPlanExit, string exportPlanOutput) = await RunAsync(fake, "check", "--repo", "octo-org/hello-world", "--file", exported);
            Assert.True(exportPlanExit == 0, $"plan of the export is not empty:\n{exportPlanOutput}\nexport:\n{File.ReadAllText(exported)}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> ApplyAsync(FakeGitHub fake, string directory, string name, JsonObject declaration, bool expectConverged)
    {
        string file = Path.Combine(directory, name);
        await File.WriteAllTextAsync(file, YamlJson.Write(declaration));
        (int exit, string output) = await RunAsync(fake, "apply", "--repo", "octo-org/hello-world", "--file", file, "--allow-status-reset");

        if (expectConverged)
        {
            Assert.True(exit == 0, $"apply did not converge:\n{output}\ndeclaration:\n{YamlJson.Write(declaration)}");
        }

        return file;
    }

    private static async Task<(int Exit, string Output)> RunAsync(FakeGitHub fake, params string[] args)
    {
        TestHost host = new();
        int exit = await host.RunAsync(fake, args);
        return (exit, host.Output.ToString() + host.Error.ToString());
    }

    /// <summary>What repo-standard cannot write is made true before the run.</summary>
    private static void Seed(FakeGitHub fake, JsonObject declaration)
    {
        foreach (JsonNode? secret in declaration["secrets"] as JsonArray ?? [])
        {
            fake.Secrets.Add(secret!.GetValue<string>());
        }

        foreach (JsonObject category in JsonTree.Items(declaration["discussions"], "categories").OfType<JsonObject>())
        {
            fake.Categories.Add(new JsonObject
            {
                ["name"] = category["name"]?.DeepClone(),
                ["emoji"] = category["emoji"]?.DeepClone(),
                ["description"] = category["description"]?.DeepClone() ?? string.Empty,
                ["isAnswerable"] = category["answerable"]?.DeepClone() ?? false,
            });
        }
    }
}

/// <summary>Generated declarations, within what the schema accepts and GitHub keeps.</summary>
internal static class Generators
{
    private static Gen<JsonNode?> Maybe(Gen<JsonNode?> gen) =>
        Gen.Bool.SelectMany(present => present ? gen : Gen.Const(static () => (JsonNode?)null));

    private static Gen<JsonNode?> OneOf(params string[] values) => Gen.OneOfConst(values).Select(v => (JsonNode?)JsonValue.Create(v));

    private static Gen<JsonNode?> Bool => Gen.Bool.Select(b => (JsonNode?)JsonValue.Create(b));

    private static Gen<string[]> SubsetOf(params string[] values) =>
        Gen.Bool.Array[values.Length].Select(mask => values.Where((_, i) => mask[i]).ToArray());

    private static JsonObject Object(params (string Key, JsonNode? Value)[] fields)
    {
        JsonObject obj = [];
        foreach ((string key, JsonNode? value) in fields.Where(f => f.Value is not null))
        {
            obj[key] = value;
        }

        return obj;
    }

    private static readonly Gen<JsonNode?> Repository =
        Gen.Select(
            Maybe(OneOf("", "A graph database.", "Tools: the second kind")),
            Maybe(OneOf("https://example.org")),
            Maybe(SubsetOf("rdf", "dotnet", "graph", "sparql").Select(t => (JsonNode?)JsonTree.StringArray(t))),
            Maybe(Gen.Select(Bool, Bool, Bool, Bool).Select(f => (JsonNode?)Object(("issues", f.Item1), ("projects", f.Item2), ("wiki", f.Item3), ("discussions", f.Item4)))),
            Maybe(Gen.Select(Bool, Bool, Bool, Bool, Maybe(OneOf("PR_TITLE", "COMMIT_OR_PR_TITLE"))).Select(m => (JsonNode?)Object(
                ("allow_merge_commit", m.Item1), ("allow_squash_merge", m.Item2), ("allow_rebase_merge", m.Item3),
                ("delete_branch_on_merge", m.Item4), ("squash_merge_commit_title", m.Item5)))),
            Maybe(Bool),
            Maybe(Gen.Select(Maybe(Bool), Maybe(Bool), Maybe(Bool), Maybe(Bool), Maybe(Bool)).Select(s => (JsonNode?)Object(
                ("private_vulnerability_reporting", s.Item1), ("dependabot_alerts", s.Item2), ("dependabot_security_updates", s.Item3),
                ("secret_scanning", s.Item4), ("secret_scanning_push_protection", s.Item5)))))
        .Select(r => (JsonNode?)Object(
            ("description", r.Item1), ("homepage", r.Item2), ("topics", r.Item3), ("features", r.Item4),
            ("merge", r.Item5), ("web_commit_signoff_required", r.Item6), ("security", r.Item7)));

    private static readonly Gen<JsonNode?> Labels =
        Gen.Select(SubsetOf("bug", "documentation", "good first issue", "Security"), Gen.OneOfConst("d73a4a", "0e8a16", "EDEDED", "1d76db").Array[4], Gen.OneOfConst("", "Something is wrong", "Needs: a look").Array[4])
            .Select(l => (JsonNode?)new JsonArray([.. l.Item1.Select((name, i) => (JsonNode)new JsonObject
            {
                ["name"] = name, ["color"] = l.Item2[i], ["description"] = l.Item3[i],
            })]));

    private static readonly JsonObject[] RulesetTemplates =
    [
        (JsonObject)JsonNode.Parse("""
            {"name":"main","target":"branch","enforcement":"active",
             "conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},
             "rules":[{"type":"deletion"},{"type":"non_fast_forward"},
                      {"type":"required_status_checks","parameters":{"strict_required_status_checks_policy":false,"do_not_enforce_on_create":false,
                        "required_status_checks":[{"context":"build (ubuntu-latest)"},{"context":"pipeline (devcontainer)"}]}}],
             "bypass_actors":[]}
            """)!,
        (JsonObject)JsonNode.Parse("""
            {"name":"signed","target":"branch","enforcement":"evaluate",
             "conditions":{"ref_name":{"include":["~ALL"],"exclude":["refs/heads/dependabot/**"]}},
             "rules":[{"type":"required_signatures"}],
             "bypass_actors":[{"actor_id":1236702,"actor_type":"Integration","bypass_mode":"always"}]}
            """)!,
        (JsonObject)JsonNode.Parse("""
            {"name":"tags","target":"tag","enforcement":"active",
             "conditions":{"ref_name":{"include":["refs/tags/v*"],"exclude":[]}},
             "rules":[{"type":"creation"},{"type":"update"},{"type":"deletion"}],
             "bypass_actors":[{"actor_id":5,"actor_type":"RepositoryRole","bypass_mode":"always"}]}
            """)!,
    ];

    private static readonly Gen<JsonNode?> Rulesets =
        Gen.Select(SubsetOf("0", "1", "2"), Gen.OneOfConst("active", "evaluate", "disabled"))
            .Select(r => (JsonNode?)new JsonArray([.. r.Item1.Select((index, i) =>
            {
                JsonObject ruleset = (JsonObject)RulesetTemplates[int.Parse(index, System.Globalization.CultureInfo.InvariantCulture)].DeepClone();
                if (i == 0)
                {
                    ruleset["enforcement"] = r.Item2;
                }

                return (JsonNode)ruleset;
            })]));

    private static readonly Gen<JsonNode?> Environment =
        Gen.Select(Gen.OneOfConst(0L, 5L, 30L), SubsetOf("User:alice", "Team:core"), Gen.Bool, Gen.Int[0, 2], SubsetOf("tag:v*", "branch:main"))
            .Select(e =>
            {
                JsonObject environment = new() { ["wait_timer"] = e.Item1 };
                environment["reviewers"] = new JsonArray([.. e.Item2.Select(r => (JsonNode)(r.StartsWith("User", StringComparison.Ordinal)
                    ? new JsonObject { ["type"] = "User", ["login"] = r[5..] }
                    : new JsonObject { ["type"] = "Team", ["slug"] = r[5..] }))]);
                if (e.Item2.Length > 0)
                {
                    environment["prevent_self_review"] = e.Item3;
                }

                switch (e.Item4)
                {
                    case 0:
                        environment["deployment_branch_policy"] = null;
                        break;
                    case 1:
                        environment["deployment_branch_policy"] = new JsonObject { ["protected_branches"] = true, ["custom_branch_policies"] = false };
                        break;
                    default:
                        environment["deployment_branch_policy"] = new JsonObject { ["protected_branches"] = false, ["custom_branch_policies"] = true };
                        environment["branch_policies"] = new JsonArray([.. e.Item5.Select(p => (JsonNode)new JsonObject
                        {
                            ["name"] = p[(p.IndexOf(':', StringComparison.Ordinal) + 1)..], ["type"] = p[..p.IndexOf(':', StringComparison.Ordinal)],
                        })]);
                        break;
                }

                return (JsonNode?)environment;
            });

    private static readonly Gen<JsonNode?> Environments =
        Gen.Select(SubsetOf("release", "staging"), Environment, Environment)
            .Select(e => (JsonNode?)new JsonArray([.. e.Item1.Select((name, i) =>
            {
                JsonObject environment = (JsonObject)(i == 0 ? e.Item2 : e.Item3)!.DeepClone();
                environment["name"] = name;
                return (JsonNode)environment;
            })]));

    private static readonly Gen<JsonNode?> Secrets =
        SubsetOf("NUGET_API_KEY", "REPO_STANDARD_APP_ID").Select(s => (JsonNode?)JsonTree.StringArray(s));

    private static readonly Gen<JsonNode?> Discussions =
        SubsetOf("Announcements", "Ideas", "Q&A").Select(names => (JsonNode?)new JsonObject
        {
            ["categories"] = new JsonArray([.. names.Select(n => (JsonNode)new JsonObject
            {
                ["name"] = n, ["emoji"] = ":speech_balloon:", ["description"] = "About " + n, ["answerable"] = n == "Q&A",
            })]),
        });

    private static readonly Gen<JsonNode?> Status =
        SubsetOf("Backlog", "Ready", "In progress", "In review", "Done")
            .Where(s => s.Length > 0)
            .SelectMany(names => Gen.OneOfConst("GRAY", "BLUE", "GREEN", "YELLOW", "PURPLE").Array[names.Length]
                .Select(colours => (JsonNode?)new JsonArray([.. names.Select((n, i) => (JsonNode)new JsonObject
                {
                    ["name"] = n, ["color"] = colours[i], ["description"] = i == 0 ? string.Empty : "Stage " + n,
                })])));

    private static readonly Gen<JsonNode?> Projects =
        Gen.Select(SubsetOf("Roadmap", "Work"), Maybe(OneOf("", "Where it is going")), Maybe(Bool), Maybe(Bool), Maybe(Status))
            .Select(p => (JsonNode?)new JsonArray([.. p.Item1.Select(title => (JsonNode)Object(
                ("title", JsonValue.Create(title)), ("short_description", p.Item2?.DeepClone()), ("public", p.Item3?.DeepClone()),
                ("closed", p.Item4?.DeepClone()), ("status", p.Item5?.DeepClone())))]));

    private static readonly Gen<JsonNode?> Actions =
        Gen.Select(Maybe(Bool), Gen.OneOfConst("all", "local_only", "selected"), Maybe(Bool), Gen.Bool, SubsetOf("actions/*", "devcontainers/ci@*"), Maybe(OneOf("read", "write")), Maybe(Bool))
            .Select(a =>
            {
                JsonObject actions = Object(("enabled", a.Item1), ("allowed_actions", JsonValue.Create(a.Item2)), ("sha_pinning_required", a.Item3));
                if (a.Item2 == "selected")
                {
                    actions["selected_actions"] = new JsonObject
                    {
                        ["github_owned_allowed"] = a.Item4, ["verified_allowed"] = !a.Item4, ["patterns_allowed"] = JsonTree.StringArray(a.Item5),
                    };
                }

                JsonObject workflow = Object(("default_workflow_permissions", a.Item6), ("can_approve_pull_request_reviews", a.Item7));
                if (workflow.Count > 0)
                {
                    actions["workflow"] = workflow;
                }

                return (JsonNode?)actions;
            });

    public static readonly Gen<JsonObject> Declaration =
        Gen.Select(Maybe(Repository), Maybe(Labels), Maybe(Rulesets), Maybe(Environments), Maybe(Secrets), Maybe(Discussions), Maybe(Projects), Maybe(Actions))
            .Select(d => Object(
                ("repository", d.Item1), ("labels", d.Item2), ("rulesets", d.Item3), ("environments", d.Item4),
                ("secrets", d.Item5), ("discussions", d.Item6), ("projects", d.Item7), ("actions", d.Item8)));
}
