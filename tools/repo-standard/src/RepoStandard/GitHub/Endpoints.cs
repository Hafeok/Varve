// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace RepoStandard.GitHub;

/// <summary>One REST endpoint: a method and a path template.</summary>
/// <remarks>
/// Every request the tool makes is built from an entry in
/// <see cref="Endpoints"/>, which is what lets the contract tests prove that
/// each endpoint used has a recorded exchange: the list is the code's, not a
/// document's.
/// </remarks>
internal sealed record Endpoint(string Method, string Template)
{
    /// <summary>The HTTP method.</summary>
    public HttpMethod HttpMethod => new(Method);

    /// <summary>
    /// The path with its <c>{placeholders}</c> filled in order, each value
    /// escaped as one path segment.
    /// </summary>
    public string Path(params string[] values)
    {
        StringBuilder builder = new();
        int next = 0;
        int index = 0;

        while (index < Template.Length)
        {
            int open = Template.IndexOf('{', index);
            if (open < 0)
            {
                builder.Append(Template, index, Template.Length - index);
                break;
            }

            int close = Template.IndexOf('}', open);
            builder.Append(Template, index, open - index);
            builder.Append(Uri.EscapeDataString(values[next++]));
            index = close + 1;
        }

        if (next != values.Length)
        {
            throw new ArgumentException($"{Template} takes {next} values, not {values.Length}.", nameof(values));
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => $"{Method} {Template}";
}

/// <summary>Every REST endpoint repo-standard calls.</summary>
internal static class Endpoints
{
    public static readonly Endpoint GetRepository = new("GET", "/repos/{owner}/{repo}");
    public static readonly Endpoint UpdateRepository = new("PATCH", "/repos/{owner}/{repo}");
    public static readonly Endpoint GetTopics = new("GET", "/repos/{owner}/{repo}/topics");
    public static readonly Endpoint ReplaceTopics = new("PUT", "/repos/{owner}/{repo}/topics");

    public static readonly Endpoint GetPrivateVulnerabilityReporting = new("GET", "/repos/{owner}/{repo}/private-vulnerability-reporting");
    public static readonly Endpoint EnablePrivateVulnerabilityReporting = new("PUT", "/repos/{owner}/{repo}/private-vulnerability-reporting");
    public static readonly Endpoint DisablePrivateVulnerabilityReporting = new("DELETE", "/repos/{owner}/{repo}/private-vulnerability-reporting");
    public static readonly Endpoint GetVulnerabilityAlerts = new("GET", "/repos/{owner}/{repo}/vulnerability-alerts");
    public static readonly Endpoint EnableVulnerabilityAlerts = new("PUT", "/repos/{owner}/{repo}/vulnerability-alerts");
    public static readonly Endpoint DisableVulnerabilityAlerts = new("DELETE", "/repos/{owner}/{repo}/vulnerability-alerts");
    public static readonly Endpoint GetAutomatedSecurityFixes = new("GET", "/repos/{owner}/{repo}/automated-security-fixes");
    public static readonly Endpoint EnableAutomatedSecurityFixes = new("PUT", "/repos/{owner}/{repo}/automated-security-fixes");
    public static readonly Endpoint DisableAutomatedSecurityFixes = new("DELETE", "/repos/{owner}/{repo}/automated-security-fixes");

    public static readonly Endpoint ListRulesets = new("GET", "/repos/{owner}/{repo}/rulesets?includes_parents=false&per_page=100");
    public static readonly Endpoint GetRuleset = new("GET", "/repos/{owner}/{repo}/rulesets/{ruleset_id}");
    public static readonly Endpoint CreateRuleset = new("POST", "/repos/{owner}/{repo}/rulesets");
    public static readonly Endpoint UpdateRuleset = new("PUT", "/repos/{owner}/{repo}/rulesets/{ruleset_id}");
    public static readonly Endpoint DeleteRuleset = new("DELETE", "/repos/{owner}/{repo}/rulesets/{ruleset_id}");

    public static readonly Endpoint ListEnvironments = new("GET", "/repos/{owner}/{repo}/environments?per_page=100");
    public static readonly Endpoint PutEnvironment = new("PUT", "/repos/{owner}/{repo}/environments/{environment_name}");
    public static readonly Endpoint DeleteEnvironment = new("DELETE", "/repos/{owner}/{repo}/environments/{environment_name}");
    public static readonly Endpoint ListBranchPolicies = new("GET", "/repos/{owner}/{repo}/environments/{environment_name}/deployment-branch-policies?per_page=100");
    public static readonly Endpoint CreateBranchPolicy = new("POST", "/repos/{owner}/{repo}/environments/{environment_name}/deployment-branch-policies");
    public static readonly Endpoint DeleteBranchPolicy = new("DELETE", "/repos/{owner}/{repo}/environments/{environment_name}/deployment-branch-policies/{branch_policy_id}");
    public static readonly Endpoint ListEnvironmentSecrets = new("GET", "/repos/{owner}/{repo}/environments/{environment_name}/secrets?per_page=100");
    public static readonly Endpoint GetUser = new("GET", "/users/{username}");
    public static readonly Endpoint GetTeam = new("GET", "/orgs/{org}/teams/{team_slug}");

    public static readonly Endpoint ListRepositorySecrets = new("GET", "/repos/{owner}/{repo}/actions/secrets?per_page=100");

    public static readonly Endpoint ListLabels = new("GET", "/repos/{owner}/{repo}/labels?per_page=100");
    public static readonly Endpoint CreateLabel = new("POST", "/repos/{owner}/{repo}/labels");
    public static readonly Endpoint UpdateLabel = new("PATCH", "/repos/{owner}/{repo}/labels/{name}");
    public static readonly Endpoint DeleteLabel = new("DELETE", "/repos/{owner}/{repo}/labels/{name}");

    public static readonly Endpoint GetActionsPermissions = new("GET", "/repos/{owner}/{repo}/actions/permissions");
    public static readonly Endpoint SetActionsPermissions = new("PUT", "/repos/{owner}/{repo}/actions/permissions");
    public static readonly Endpoint GetSelectedActions = new("GET", "/repos/{owner}/{repo}/actions/permissions/selected-actions");
    public static readonly Endpoint SetSelectedActions = new("PUT", "/repos/{owner}/{repo}/actions/permissions/selected-actions");
    public static readonly Endpoint GetWorkflowPermissions = new("GET", "/repos/{owner}/{repo}/actions/permissions/workflow");
    public static readonly Endpoint SetWorkflowPermissions = new("PUT", "/repos/{owner}/{repo}/actions/permissions/workflow");

    public static readonly Endpoint GraphQl = new("POST", "/graphql");

    /// <summary>All of them.</summary>
    public static IReadOnlyList<Endpoint> All { get; } =
    [
        GetRepository, UpdateRepository, GetTopics, ReplaceTopics,
        GetPrivateVulnerabilityReporting, EnablePrivateVulnerabilityReporting, DisablePrivateVulnerabilityReporting,
        GetVulnerabilityAlerts, EnableVulnerabilityAlerts, DisableVulnerabilityAlerts,
        GetAutomatedSecurityFixes, EnableAutomatedSecurityFixes, DisableAutomatedSecurityFixes,
        ListRulesets, GetRuleset, CreateRuleset, UpdateRuleset, DeleteRuleset,
        ListEnvironments, PutEnvironment, DeleteEnvironment,
        ListBranchPolicies, CreateBranchPolicy, DeleteBranchPolicy, ListEnvironmentSecrets,
        GetUser, GetTeam,
        ListRepositorySecrets,
        ListLabels, CreateLabel, UpdateLabel, DeleteLabel,
        GetActionsPermissions, SetActionsPermissions, GetSelectedActions, SetSelectedActions,
        GetWorkflowPermissions, SetWorkflowPermissions,
        GraphQl,
    ];
}
