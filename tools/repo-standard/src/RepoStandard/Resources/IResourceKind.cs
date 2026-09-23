// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Engine;
using RepoStandard.GitHub;

namespace RepoStandard.Resources;

/// <summary>The repository being converged, and the switches that change how.</summary>
/// <param name="Owner">The owner's login.</param>
/// <param name="Name">The repository's name.</param>
/// <param name="Client">The API.</param>
/// <param name="AllowStatusReset">
/// Whether a Projects v2 Status field that has items may have its options
/// rewritten, which clears the Status of every item on a changed option.
/// </param>
internal sealed record RepoContext(string Owner, string Name, GitHubClient Client, bool AllowStatusReset)
{
    /// <summary>The owner and name, for endpoints that take both.</summary>
    public string[] Repo => [Owner, Name];

    /// <summary>The owner and name followed by further path values.</summary>
    public string[] With(params string[] more) => [Owner, Name, .. more];
}

/// <summary>
/// One kind of resource a declaration can manage: how to read it from GitHub in
/// declaration shape, and what to change so that it matches.
/// </summary>
/// <remarks>
/// A kind is created per run and may remember what its read saw — ids GitHub
/// assigns and the declaration never names — so that the writes its diff
/// produces can address them.
/// </remarks>
internal interface IResourceKind
{
    /// <summary>The declaration key: <c>repository</c>, <c>labels</c>, ...</summary>
    string Key { get; }

    /// <summary>Reads the live state, in the shape a declaration writes it.</summary>
    Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken);

    /// <summary>
    /// The changes that make live match the declaration. Only called after
    /// <see cref="ReadAsync"/> on the same instance.
    /// </summary>
    IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context);
}
