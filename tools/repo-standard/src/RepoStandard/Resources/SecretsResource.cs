// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
/// <c>secrets</c>: the names of the repository's Actions secrets, asserted to
/// exist. repo-standard never reads a secret's value, never writes one, and
/// never deletes a secret; a difference in either direction is reported and
/// left for a person.
/// </summary>
internal sealed class SecretsResource : IResourceKind
{
    public string Key => "secrets";

    public async Task<JsonNode?> ReadAsync(RepoContext context, CancellationToken cancellationToken)
    {
        List<JsonNode> secrets = await context.Client
            .GetAllAsync(Endpoints.ListRepositorySecrets, context.Repo, static body => body?["secrets"] as JsonArray, cancellationToken)
            .ConfigureAwait(false);

        return JsonTree.StringArray(secrets.Select(s => JsonTree.Str(s, "name") ?? string.Empty).Order(StringComparer.Ordinal));
    }

    public IEnumerable<Change> Diff(JsonNode declared, JsonNode? live, RepoContext context) =>
        Compare(Key, "secret", declared, live);

    /// <summary>The unfixable changes between two lists of secret names.</summary>
    public static IEnumerable<Change> Compare(string kind, string prefix, JsonNode? declared, JsonNode? live)
    {
        HashSet<string> want = Names(declared);
        HashSet<string> have = Names(live);

        foreach (string name in want.Except(have).Order(StringComparer.Ordinal))
        {
            yield return new Change(kind, $"{prefix} {name}", ChangeAction.Unfixable, [],
                "declared and not present; repo-standard never writes a secret, so add it by hand", null);
        }

        foreach (string name in have.Except(want).Order(StringComparer.Ordinal))
        {
            yield return new Change(kind, $"{prefix} {name}", ChangeAction.Unfixable, [],
                "present and not declared; repo-standard never deletes a secret, so declare it or remove it by hand", null);
        }
    }

    private static HashSet<string> Names(JsonNode? node) =>
        node is JsonArray array
            ? new HashSet<string>(array.Select(item => item?.GetValue<string>() ?? string.Empty), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}
