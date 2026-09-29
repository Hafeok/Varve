// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>One entry of a commit's <c>alloc</c>: a fresh handle and the term it names.</summary>
public readonly struct TermAllocation : IEquatable<TermAllocation>
{
    internal TermAllocation(TermHandle handle, RdfTerm term)
    {
        Handle = handle;
        Term = term;
    }

    /// <summary>The fresh handle.</summary>
    public TermHandle Handle { get; }

    /// <summary>
    /// The term. A blank node's label is derived from its id and is not an
    /// identity to send back (ADR 0044).
    /// </summary>
    public RdfTerm Term { get; }

    /// <inheritdoc />
    public bool Equals(TermAllocation other) => Handle == other.Handle && Equals(Term, other.Term);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TermAllocation other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Handle, Term);

    /// <summary>Compares the handle and the term.</summary>
    public static bool operator ==(TermAllocation left, TermAllocation right) => left.Equals(right);

    /// <summary>Compares the handle and the term.</summary>
    public static bool operator !=(TermAllocation left, TermAllocation right) => !left.Equals(right);
}

/// <summary>
/// A projection (spec §7, ADR 0016): a state machine fed closed commits in
/// order, at least once.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is persisted atomically with the state — not beside
/// it, not after it — or idempotence and rebuild equivalence both fail across a
/// crash. Feed one with <see cref="Dataset.CatchUpAsync"/> and rebuild it with
/// <see cref="Dataset.RebuildAsync"/>.
/// </remarks>
[Contract(typeof(ProjectionContractAndSubscriptions.ProjectionGetsClosedCommitsInOrder), Role = "a projection of the log, fed closed commits in position order")]
public interface IProjection
{
    /// <summary>The position of the last commit applied: <c>pos(π)</c>.</summary>
    Position Position { get; }

    /// <summary>
    /// Applies a commit. A commit at or below <see cref="Position"/> is a no-op,
    /// which is what makes at-least-once delivery an exactly-once effect.
    /// </summary>
    ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken);

    /// <summary>
    /// Drops all state. With no checkpoint, the projection is empty at position
    /// 0; with one, it takes that view's quads as its state at that view's
    /// position.
    /// </summary>
    ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken);
}

/// <summary>Which quads an access request covers by default (spec §9). A setting.</summary>
public enum AccessScope : byte
{
    /// <summary>Every quad ever asserted that mentions the subject. The default.</summary>
    AllHistory = 0,

    /// <summary>Only quads in the current graph.</summary>
    Current = 1,
}

/// <summary>
/// The log does not verify: its chain breaks, a content hash or header hash
/// does not match, or its records are out of order. The store refuses to open
/// it rather than guess (ADR 0014).
/// </summary>
public sealed class LogVerificationException : Exception
{
    /// <summary>A refusal at a position.</summary>
    public LogVerificationException(Position position, string message)
        : base(message) => Position = position;

    /// <summary>A refusal at a position the reader holds as a count of commits.</summary>
    internal LogVerificationException(long position, string message)
        : this(new Position(position), message)
    {
    }

    /// <summary>A refusal with no position.</summary>
    public LogVerificationException()
    {
    }

    /// <summary>A refusal with no position.</summary>
    public LogVerificationException(string message)
        : base(message)
    {
    }

    /// <summary>A refusal with no position, and its cause.</summary>
    public LogVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The position at which verification failed.</summary>
    public Position Position { get; }
}

/// <summary>
/// The dataset is in the failed state — its default projection could not reach
/// the head — and <see cref="Dataset.Pin"/> refuses until it is rebuilt
/// (spec §7). Nothing waits indefinitely.
/// </summary>
public sealed class DatasetUnavailableException : Exception
{
    /// <summary>The dataset cannot be read now.</summary>
    public DatasetUnavailableException()
    {
    }

    /// <summary>The dataset cannot be read now, and why.</summary>
    public DatasetUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>The dataset cannot be read now, why, and the cause.</summary>
    public DatasetUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
