// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// Graph versions (ADR 0125): the version of a graph at the head and at any
/// closed position, the commits that caused a conflict, and the index's
/// persistence beside the default projection and the checkpoints.
/// </summary>
public sealed partial class Dataset
{
    /// <summary>
    /// The version of a graph at the head: the position of the last commit
    /// that touched it, or touched every graph; 0 when none has (ADR 0125).
    /// <see cref="RequestTerm.None"/> is the default graph.
    /// </summary>
    /// <remarks>Answered from the head's index, without a pin.</remarks>
    public Position GraphVersion(RequestTerm graph)
    {
        State state = _state;
        return new Position(GraphId(state, graph) is ulong id ? state.Graphs.VersionOf(id) : state.Graphs.All);
    }

    /// <summary>
    /// The version of a graph as of a closed position (ADR 0125): the last
    /// commit at or before it that touched the graph. From the head's index
    /// when the graph has not changed since; otherwise from the nearest
    /// checkpoint's index and the commits after it, newest first, so its cost
    /// is at most the log distance an as-of read at that position pays (R2).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is past the head.</exception>
    public async ValueTask<Position> GraphVersionAsync(RequestTerm graph, Position at, CancellationToken cancellationToken = default)
    {
        State state = _state;
        long position = at.Value;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head, nameof(at));
        ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(at));
        ulong id = GraphId(state, graph) ?? ulong.MaxValue;
        long atHead = id == ulong.MaxValue ? state.Graphs.All : state.Graphs.VersionOf(id);

        if (atHead <= position)
        {
            return new Position(atHead);
        }

        // Some commit after the position touched it: the newest commit at or
        // before the position that did, or the nearest checkpoint's answer.
        long floor = 0;
        long below = 0;

        for (int i = state.Checkpoints.Length - 1; i >= 0; i--)
        {
            long checkpoint = state.Checkpoints[i].Position;

            if (checkpoint > position)
            {
                continue;
            }

            if (await GraphVersions.TryLookupAsync(_storage.Derived, GraphVersions.NameAt(checkpoint), Id, state.Commits.Entry(checkpoint).HeaderHash(), id, cancellationToken).ConfigureAwait(false) is long found)
            {
                (floor, below) = (checkpoint, found);
                break;
            }
        }

        for (long p = position; p > floor; p--)
        {
            LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);

            if (commit.Header.Kind != CommitKind.Data || Holds(commit.Asserted, id) || Holds(commit.Retracted, id))
            {
                return new Position(p);
            }
        }

        return new Position(below);
    }

    /// <summary>
    /// The commits after <paramref name="since"/> that touched the scope, newest
    /// first, each with the graphs of the scope it touched (ADR 0125): at most
    /// <paramref name="limit"/> of them, reading at most
    /// <paramref name="maxDistance"/> commits back from the head. With no
    /// scope, every commit after <paramref name="since"/>, with every graph it
    /// touched. A <c>Settings</c> or <c>Erasure</c> commit touched every graph
    /// of the scope. Read outside the sequencer, as a subscription reads.
    /// </summary>
    public async ValueTask<CommitConflicts> ConflictsAsync(
        Position since, IReadOnlyList<RequestTerm>? scope, int limit, long maxDistance, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        State state = _state;
        long from = Math.Max(since.Value, state.Head - maxDistance);
        bool truncated = from > since.Value;
        HashSet<ulong>? wanted = null;
        List<RequestTerm> scopeTerms = [];

        if (scope is not null)
        {
            wanted = [];

            foreach (RequestTerm graph in scope)
            {
                if (GraphId(state, graph) is ulong id)
                {
                    wanted.Add(id);
                }

                scopeTerms.Add(graph);
            }
        }

        List<ConflictingCommit> commits = [];

        for (long p = state.Head; p > from; p--)
        {
            if (commits.Count == limit)
            {
                truncated = true;
                break;
            }

            LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
            List<RequestTerm> graphs = [];

            if (commit.Header.Kind != CommitKind.Data)
            {
                if (scope is not null)
                {
                    graphs.AddRange(scopeTerms);
                }
            }
            else
            {
                ulong[] distinct = new ulong[commit.Asserted.Length + commit.Retracted.Length];
                int count = GraphVersions.Distinct(commit.Asserted, commit.Retracted, distinct);

                for (int i = 0; i < count; i++)
                {
                    if (wanted is null || wanted.Contains(distinct[i]))
                    {
                        graphs.Add(distinct[i] == 0 ? RequestTerm.None : RequestTerm.FromTerm(TermAtHead(distinct[i])));
                    }
                }
            }

            if (graphs.Count > 0 || (scope is null && commit.Header.Kind != CommitKind.Data))
            {
                commits.Add(new ConflictingCommit(new Position(p), graphs));
            }
        }

        return new CommitConflicts(commits, truncated);
    }

    // A graph's id in the head's dictionary: 0 for the default graph; null
    // when the dictionary has never held it, so no commit touched it alone.
    private ulong? GraphId(State state, RequestTerm graph)
    {
        if (graph.IsNone)
        {
            return 0;
        }

        if (graph.IsExisting)
        {
            return graph.Handle.Value;
        }

        while (true)
        {
            IndexVersion index = state.Index;

            if (index.TryAcquire())
            {
                try
                {
                    return _dictionary.TryFind(index.Runs, graph.Term!, state.CanonicalAt(state.Head), out ulong id) ? id : null;
                }
                finally
                {
                    index.Release();
                }
            }

            state = _state;
        }
    }

    private static bool Holds(ReadOnlySpan<Quad> quads, ulong graph)
    {
        foreach (Quad quad in quads)
        {
            if (quad.Graph.Value == graph)
            {
                return true;
            }
        }

        return false;
    }

    // Opening: the head's persisted index when it names this log, else the
    // newest checkpoint's that does, else nothing; then the log after it.
    private async ValueTask<GraphVersions> LoadGraphsAsync(State state, Dictionary<long, LoggedCommit> bodies, CancellationToken cancellationToken)
    {
        GraphVersions? start = await TryReadGraphsAsync(state, GraphVersions.HeadName, cancellationToken).ConfigureAwait(false);

        for (int i = state.Checkpoints.Length - 1; i >= 0; i--)
        {
            long checkpoint = state.Checkpoints[i].Position;

            if (start is not null && start.Position >= checkpoint)
            {
                break;
            }

            if (await TryReadGraphsAsync(state, GraphVersions.NameAt(checkpoint), cancellationToken).ConfigureAwait(false) is { } read)
            {
                start = read;
                break;
            }
        }

        return await ReplayGraphsAsync(state, start ?? GraphVersions.Empty, state.Head, bodies, cancellationToken).ConfigureAwait(false);
    }

    // The index at a position: the head's, or a checkpoint's at or below it
    // and the log after it. For a checkpoint written below the head.
    private async ValueTask<GraphVersions> GraphsAtAsync(State state, long position, CancellationToken cancellationToken)
    {
        if (position == state.Head)
        {
            return state.Graphs;
        }

        GraphVersions start = GraphVersions.Empty;

        for (int i = state.Checkpoints.Length - 1; i >= 0; i--)
        {
            long checkpoint = state.Checkpoints[i].Position;

            if (checkpoint <= position && await TryReadGraphsAsync(state, GraphVersions.NameAt(checkpoint), cancellationToken).ConfigureAwait(false) is { } read)
            {
                start = read;
                break;
            }
        }

        return await ReplayGraphsAsync(state, start, position, null, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<GraphVersions?> TryReadGraphsAsync(State state, BlobName name, CancellationToken cancellationToken)
    {
        if (await GraphVersions.TryReadAsync(_storage.Derived, name, Id, cancellationToken).ConfigureAwait(false) is not { } read)
        {
            return null;
        }

        long position = read.Versions.Position;

        // Derived data names the commit it is at; one beside another log is a
        // cache miss, never a wrong answer (ADR 0041).
        return position >= 1 && position <= state.Head && state.Commits.Entry(position).HashEquals(read.Hash) ? read.Versions : null;
    }

    private async ValueTask<GraphVersions> ReplayGraphsAsync(State state, GraphVersions start, long to, Dictionary<long, LoggedCommit>? bodies, CancellationToken cancellationToken)
    {
        GraphVersions graphs = start;

        for (long p = start.Position + 1; p <= to; p++)
        {
            LoggedCommit commit = bodies is not null && bodies.TryGetValue(p, out LoggedCommit? read)
                ? read
                : await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
            graphs = graphs.Apply(commit.Header.Kind, commit.Asserted, commit.Retracted, p);
        }

        return graphs.Position == to ? graphs : graphs.At(to);
    }
}
