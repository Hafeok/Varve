// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>The answer to a commit request. A <see cref="CommitOutcome.Conflict"/> is a normal answer, not an error.</summary>
public readonly struct CommitResult : IEquatable<CommitResult>
{
    private CommitResult(CommitOutcome outcome, long position, IReadOnlyList<RdfTerm>? report, string? reason, bool storageFull = false, long unknownAt = 0)
    {
        Outcome = outcome;
        Position = new Position(position);
        Report = report ?? [];
        Reason = reason;
        StorageFull = storageFull;
        OutcomeUnknownAt = unknownAt > 0 ? new Position(unknownAt) : null;
    }

    /// <summary>How it ended.</summary>
    public CommitOutcome Outcome { get; }

    /// <summary>
    /// The new position when committed; otherwise the readable head at the
    /// time of the answer.
    /// </summary>
    public Position Position { get; }

    /// <summary>The validator's report, when rejected; empty otherwise.</summary>
    public IReadOnlyList<RdfTerm> Report { get; }

    /// <summary>Why, when unavailable.</summary>
    [DesignDecision(typeof(StoreLogSurfaces.UnavailableReasonIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Reason { get; }

    /// <summary>
    /// When unavailable, whether because the storage had no space for the
    /// commit (ADR 0214): nothing was written, and the next write tries again
    /// once there is space. Over HTTP, <c>507 storage-full</c>.
    /// </summary>
    [DesignDecision(typeof(FileBackendFailureInjection.FullDiskFailsTheCommitAndTheNextWriteRecovers), Scope = ExceptionScope.Boundary)]
    public bool StorageFull { get; }

    /// <summary>
    /// When unavailable, the position at which the commit was written and
    /// closed when the flush that would make it durable did not return (ADR
    /// 0214): the commit may or may not be in the log. It is settled once the
    /// head reaches the position, by whether the commit there carries this
    /// request's cause. Over HTTP, <c>503 commit-outcome-unknown</c>. Null
    /// otherwise.
    /// </summary>
    [DesignDecision(typeof(FileBackendFailureInjection.ClosedAndUnflushedIsNotKnown), Scope = ExceptionScope.Boundary)]
    public Position? OutcomeUnknownAt { get; }

    internal static CommitResult Committed(long position) => new(CommitOutcome.Committed, position, null, null);

    internal static CommitResult NoChange(long head) => new(CommitOutcome.NoChange, head, null, null);

    internal static CommitResult Conflict(long head) => new(CommitOutcome.Conflict, head, null, null);

    internal static CommitResult Rejected(long head, IReadOnlyList<RdfTerm> report) => new(CommitOutcome.Rejected, head, report, null);

    internal static CommitResult Unavailable(long head, string reason) => new(CommitOutcome.Unavailable, head, null, reason);

    internal static CommitResult NoSpace(long head, string reason) => new(CommitOutcome.Unavailable, head, null, reason, storageFull: true);

    internal static CommitResult OutcomeUnknown(long head, long position, string reason) => new(CommitOutcome.Unavailable, head, null, reason, unknownAt: position);

    /// <inheritdoc />
    public bool Equals(CommitResult other) =>
        Outcome == other.Outcome && Position == other.Position && ReferenceEquals(Report, other.Report) && Reason == other.Reason && StorageFull == other.StorageFull && OutcomeUnknownAt == other.OutcomeUnknownAt;

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
