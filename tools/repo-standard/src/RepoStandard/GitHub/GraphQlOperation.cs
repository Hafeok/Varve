// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RepoStandard.GitHub;

/// <summary>A named GraphQL query or mutation.</summary>
internal sealed record GraphQlOperation(string Name, bool IsMutation, string Text);

/// <summary>A GraphQL request body.</summary>
internal sealed record GraphQlRequest(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("variables")] JsonObject Variables);

/// <summary>The source-generated serializer for the fixed-shape bodies.</summary>
[JsonSerializable(typeof(GraphQlRequest))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;

/// <summary>Every GraphQL operation repo-standard runs.</summary>
/// <remarks>
/// Each carries its name in its text, which is how the in-memory GitHub used by
/// the tests dispatches it and how the contract tests prove each one has a
/// recorded exchange.
/// </remarks>
internal static class GraphQlOperations
{
    public static readonly GraphQlOperation DiscussionCategories = new("DiscussionCategories", false, """
        query DiscussionCategories($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            hasDiscussionsEnabled
            discussionCategories(first: 100) { nodes { name emoji description isAnswerable } }
          }
        }
        """);

    public static readonly GraphQlOperation LinkedProjects = new("LinkedProjects", false, """
        query LinkedProjects($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            id
            projectsV2(first: 100) {
              nodes {
                id title shortDescription readme public closed
                owner { ... on Organization { login } ... on User { login } }
                items(first: 1) { totalCount }
                field(name: "Status") { ... on ProjectV2SingleSelectField { id options { name color description } } }
              }
            }
          }
        }
        """);

    public static readonly GraphQlOperation OwnerProjects = new("OwnerProjects", false, """
        query OwnerProjects($login: String!, $title: String!) {
          repositoryOwner(login: $login) {
            id
            ... on ProjectV2Owner { projectsV2(first: 20, query: $title) { nodes { id title } } }
          }
        }
        """);

    public static readonly GraphQlOperation CreateProject = new("CreateProject", true, """
        mutation CreateProject($input: CreateProjectV2Input!) {
          createProjectV2(input: $input) {
            projectV2 { id field(name: "Status") { ... on ProjectV2SingleSelectField { id options { name color description } } } }
          }
        }
        """);

    public static readonly GraphQlOperation UpdateProject = new("UpdateProject", true, """
        mutation UpdateProject($input: UpdateProjectV2Input!) {
          updateProjectV2(input: $input) { projectV2 { id } }
        }
        """);

    public static readonly GraphQlOperation LinkProject = new("LinkProject", true, """
        mutation LinkProject($input: LinkProjectV2ToRepositoryInput!) {
          linkProjectV2ToRepository(input: $input) { repository { id } }
        }
        """);

    public static readonly GraphQlOperation UnlinkProject = new("UnlinkProject", true, """
        mutation UnlinkProject($input: UnlinkProjectV2FromRepositoryInput!) {
          unlinkProjectV2FromRepository(input: $input) { repository { id } }
        }
        """);

    public static readonly GraphQlOperation UpdateStatusField = new("UpdateStatusField", true, """
        mutation UpdateStatusField($input: UpdateProjectV2FieldInput!) {
          updateProjectV2Field(input: $input) { projectV2Field { ... on ProjectV2SingleSelectField { id } } }
        }
        """);

    public static readonly GraphQlOperation CreateStatusField = new("CreateStatusField", true, """
        mutation CreateStatusField($input: CreateProjectV2FieldInput!) {
          createProjectV2Field(input: $input) { projectV2Field { ... on ProjectV2SingleSelectField { id } } }
        }
        """);

    /// <summary>All of them.</summary>
    public static ImmutableArray<GraphQlOperation> All { get; } =
    [
        DiscussionCategories, LinkedProjects, OwnerProjects, CreateProject, UpdateProject,
        LinkProject, UnlinkProject, UpdateStatusField, CreateStatusField,
    ];
}
