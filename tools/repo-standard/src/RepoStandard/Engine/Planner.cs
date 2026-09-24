// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.GitHub;
using RepoStandard.Resources;

namespace RepoStandard.Engine;

/// <summary>What a read of the repository found, per kind, and what could not be read.</summary>
internal sealed record LiveState(JsonObject State, IReadOnlyList<string> Unreadable);

/// <summary>Reads, compares, and writes, kind by kind.</summary>
internal static class Planner
{
    /// <summary>A fresh set of kinds, in the order they are read and written.</summary>
    /// <remarks>
    /// Repository settings first, because features gate the rest (Discussions
    /// categories need Discussions on). Actions last.
    /// </remarks>
    public static List<IResourceKind> Kinds() =>
    [
        new RepositoryResource(),
        new LabelsResource(),
        new RulesetsResource(),
        new EnvironmentsResource(),
        new SecretsResource(),
        new DiscussionsResource(),
        new ProjectsResource(),
        new ActionsResource(),
    ];

    /// <summary>
    /// Reads the kinds named. A kind that cannot be read stops the run unless
    /// <paramref name="tolerant"/>, which export sets: an export leaves out what
    /// it could not read and says so.
    /// </summary>
    public static async Task<LiveState> ReadAsync(
        IEnumerable<IResourceKind> kinds, RepoContext context, bool tolerant, TextWriter errors, CancellationToken cancellationToken)
    {
        JsonObject state = [];
        List<string> unreadable = [];

        foreach (IResourceKind kind in kinds)
        {
            try
            {
                state[kind.Key] = await kind.ReadAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (GitHubException exception) when (tolerant)
            {
                unreadable.Add($"{kind.Key}: {exception.Message}");
                await errors.WriteLineAsync($"warning: could not read {kind.Key}, left out: {exception.Message}").ConfigureAwait(false);
            }
        }

        return new LiveState(state, unreadable);
    }

    /// <summary>Reads what the declaration manages and diffs it.</summary>
    public static async Task<List<Change>> PlanAsync(JsonObject declaration, RepoContext context, TextWriter errors, CancellationToken cancellationToken)
    {
        List<IResourceKind> kinds = [.. Kinds().Where(kind => declaration.ContainsKey(kind.Key))];
        LiveState live = await ReadAsync(kinds, context, tolerant: false, errors, cancellationToken).ConfigureAwait(false);
        return Diff(kinds, declaration, live.State, context);
    }

    /// <summary>The changes, ordered: kind by kind, and within a kind creates and updates before deletes.</summary>
    public static List<Change> Diff(IEnumerable<IResourceKind> kinds, JsonObject declaration, JsonObject live, RepoContext context)
    {
        List<Change> changes = [];
        foreach (IResourceKind kind in kinds)
        {
            if (declaration[kind.Key] is JsonNode declared)
            {
                changes.AddRange(kind.Diff(declared, live[kind.Key], context)
                    .OrderBy(change => change.Action is ChangeAction.Delete ? 1 : 0));
            }
        }

        return changes;
    }

    /// <summary>
    /// Writes each change in order, logging it. Stops at the first write that
    /// fails and returns the failure and the changes not made.
    /// </summary>
    public static async Task<(Change? Failed, Exception? Error, List<Change> NotApplied)> ApplyAsync(
        IReadOnlyList<Change> changes, TextWriter log, CancellationToken cancellationToken)
    {
        for (int i = 0; i < changes.Count; i++)
        {
            Change change = changes[i];
            if (change.Apply is null)
            {
                continue;
            }

            try
            {
                await change.Apply(cancellationToken).ConfigureAwait(false);
                await log.WriteLineAsync($"applied  {Report.Line(change)}").ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is GitHubException or RepoStandardException)
            {
                List<Change> rest = [.. changes.Skip(i).Where(c => c.Apply is not null)];
                return (change, exception, rest);
            }
        }

        return (null, null, []);
    }
}
