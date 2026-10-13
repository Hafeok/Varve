// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Store.Log;

/// <summary>A commit that touched a conflicting request's scope, and the graphs of the scope it touched (ADR 0125).</summary>
public sealed class ConflictingCommit
{
    /// <summary>Creates the entry.</summary>
    internal ConflictingCommit(Position position, IReadOnlyList<RequestTerm> graphs)
    {
        Position = position;
        Graphs = graphs;
    }

    /// <summary>The commit's position.</summary>
    public Position Position { get; }

    /// <summary>The graphs it touched, as terms; <see cref="RequestTerm.None"/> is the default graph.</summary>
    public IReadOnlyList<RequestTerm> Graphs { get; }
}

/// <summary>The commits that caused a conflict, newest first, and whether the list stopped short (ADR 0125).</summary>
public sealed class CommitConflicts
{
    /// <summary>Creates the report.</summary>
    internal CommitConflicts(IReadOnlyList<ConflictingCommit> commits, bool truncated)
    {
        Commits = commits;
        Truncated = truncated;
    }

    /// <summary>The commits, newest first.</summary>
    public IReadOnlyList<ConflictingCommit> Commits { get; }

    /// <summary>True when the limit or the distance stopped the list before the expected position.</summary>
    public bool Truncated { get; }
}
