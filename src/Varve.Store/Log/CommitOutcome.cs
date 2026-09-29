// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Log;

/// <summary>How a commit request ended (spec T1).</summary>
public enum CommitOutcome
{
    /// <summary>The change is in the log at <see cref="CommitResult.Position"/>.</summary>
    Committed,

    /// <summary>The request changed nothing. No commit, no trace; the position is the head.</summary>
    NoChange,

    /// <summary>The expected position was not the head, which the position carries. Nothing changed.</summary>
    Conflict,

    /// <summary>A validator refused. No commit, no trace; <see cref="CommitResult.Report"/> says why.</summary>
    Rejected,

    /// <summary>
    /// The dataset cannot take a commit now: it is in the failed state, or its
    /// default projection is not at the head. <see cref="CommitResult.Reason"/> says which.
    /// </summary>
    Unavailable,
}
