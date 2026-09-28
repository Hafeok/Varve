// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>The answer to a commit request. A <see cref="CommitOutcome.Conflict"/> is a normal answer, not an error.</summary>
public readonly struct CommitResult : IEquatable<CommitResult>
{
    private CommitResult(CommitOutcome outcome, long position, IReadOnlyList<RdfTerm>? report, string? reason)
    {
        Outcome = outcome;
        Position = position;
        Report = report ?? [];
        Reason = reason;
    }

    /// <summary>How it ended.</summary>
    public CommitOutcome Outcome { get; }

    /// <summary>
    /// The new position when committed; otherwise the readable head at the
    /// time of the answer.
    /// </summary>
    public long Position { get; }

    /// <summary>The validator's report, when rejected; empty otherwise.</summary>
    public IReadOnlyList<RdfTerm> Report { get; }

    /// <summary>Why, when unavailable.</summary>
    public string? Reason { get; }

    internal static CommitResult Committed(long position) => new(CommitOutcome.Committed, position, null, null);

    internal static CommitResult NoChange(long head) => new(CommitOutcome.NoChange, head, null, null);

    internal static CommitResult Conflict(long head) => new(CommitOutcome.Conflict, head, null, null);

    internal static CommitResult Rejected(long head, IReadOnlyList<RdfTerm> report) => new(CommitOutcome.Rejected, head, report, null);

    internal static CommitResult Unavailable(long head, string reason) => new(CommitOutcome.Unavailable, head, null, reason);

    /// <inheritdoc />
    public bool Equals(CommitResult other) =>
        Outcome == other.Outcome && Position == other.Position && ReferenceEquals(Report, other.Report) && Reason == other.Reason;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CommitResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Outcome, Position);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator ==(CommitResult left, CommitResult right) => left.Equals(right);

    /// <summary>Compares every field; reports by reference.</summary>
    public static bool operator !=(CommitResult left, CommitResult right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Outcome + "(" + Position + ")";
}
