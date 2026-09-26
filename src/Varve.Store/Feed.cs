// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>What a commit is (spec §1).</summary>
public enum CommitKind : byte
{
    /// <summary>A change to the graph. Its delta is never empty (I4).</summary>
    Data = 0,

    /// <summary>A key destroyed. Empty delta; nothing produces one before erasure mode exists.</summary>
    Erasure = 1,

    /// <summary>A change to the dataset's settings (ADR 0021). Empty delta.</summary>
    Settings = 2,
}

/// <summary>
/// A closed commit as a subscriber or a projection receives it: never a
/// record, never an unclosed tail (ADR 0013).
/// </summary>
/// <remarks>
/// Its handles are the dataset's ids, so they mean the same thing in every view
/// of the dataset. <see cref="Allocations"/> lists the fresh ids the delivered
/// delta and metadata refer to, so a consumer can keep a dictionary of its own
/// without reading the dataset's (ADR 0042).
/// </remarks>
public sealed class Commit
{
    internal Commit(
        long position,
        CommitKind kind,
        DateTimeOffset timestamp,
        TermHandle agent,
        TermHandle cause,
        TermHandle graphScope,
        ReadOnlyMemory<TermHandle> attachments,
        QuadDelta delta,
        ReadOnlyMemory<TermAllocation> allocations)
    {
        Position = position;
        Kind = kind;
        Timestamp = timestamp;
        Agent = agent;
        Cause = cause;
        GraphScope = graphScope;
        Attachments = attachments;
        Delta = delta;
        Allocations = allocations;
    }

    /// <summary>Its position in the log.</summary>
    public long Position { get; }

    /// <summary>What kind of commit it is.</summary>
    public CommitKind Kind { get; }

    /// <summary>When the sequencer closed it: <c>max(clock, ts(previous))</c>, so never earlier than the one before.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Who made it, or <see cref="TermHandle.None"/>.</summary>
    public TermHandle Agent { get; }

    /// <summary>Why, or <see cref="TermHandle.None"/>.</summary>
    public TermHandle Cause { get; }

    /// <summary>The graph it declared it was about, or <see cref="TermHandle.None"/>. Recorded, not enforced.</summary>
    public TermHandle GraphScope { get; }

    /// <summary>What validators attached when they accepted it.</summary>
    public ReadOnlyMemory<TermHandle> Attachments { get; }

    /// <summary>Its effective delta — filtered, when delivered to a filtered subscription.</summary>
    public QuadDelta Delta { get; }

    /// <summary>The fresh ids its delivered delta and metadata refer to, and their terms.</summary>
    public ReadOnlyMemory<TermAllocation> Allocations { get; }
}

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
/// What a subscription wants: every quad, a graph pattern, or a quad pattern.
/// It restricts each delivered delta; <see cref="CommitKind.Settings"/> and
/// <see cref="CommitKind.Erasure"/> commits pass whatever it says
/// (specification 1.2, §8; ADR 0046).
/// </summary>
public readonly struct SubscriptionFilter : IEquatable<SubscriptionFilter>
{
    private readonly bool _restricted;

    private SubscriptionFilter(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        _restricted = true;
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        Graph = graph;
    }

    /// <summary>Everything.</summary>
    public static SubscriptionFilter All => default;

    /// <summary>The subject to match, or <see cref="TermHandle.None"/> for any.</summary>
    public TermHandle Subject { get; }

    /// <summary>The predicate to match, or <see cref="TermHandle.None"/> for any.</summary>
    public TermHandle Predicate { get; }

    /// <summary>The object to match, or <see cref="TermHandle.None"/> for any.</summary>
    public TermHandle Object { get; }

    /// <summary>The graphs to match. Ignored for <see cref="All"/>.</summary>
    public GraphPattern Graph { get; }

    /// <summary>Quads in the graphs a pattern names.</summary>
    public static SubscriptionFilter ForGraph(GraphPattern graph) =>
        new(TermHandle.None, TermHandle.None, TermHandle.None, graph);

    /// <summary>Quads matching a pattern; <see cref="TermHandle.None"/> is a wildcard.</summary>
    public static SubscriptionFilter ForPattern(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new(subject, predicate, @object, graph);

    /// <summary>Whether a quad passes.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Matches(in Quad quad) =>
        !_restricted
        || ((Subject.IsNone || Subject == quad.Subject)
            && (Predicate.IsNone || Predicate == quad.Predicate)
            && (Object.IsNone || Object == quad.Object)
            && Graph.Matches(quad.Graph));

    /// <inheritdoc />
    public bool Equals(SubscriptionFilter other) =>
        _restricted == other._restricted && Subject == other.Subject && Predicate == other.Predicate
        && Object == other.Object && Graph == other.Graph;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SubscriptionFilter other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_restricted, Subject, Predicate, Object, Graph);

    /// <summary>Compares every field.</summary>
    public static bool operator ==(SubscriptionFilter left, SubscriptionFilter right) => left.Equals(right);

    /// <summary>Compares every field.</summary>
    public static bool operator !=(SubscriptionFilter left, SubscriptionFilter right) => !left.Equals(right);
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
public interface IProjection
{
    /// <summary>The position of the last commit applied: <c>pos(π)</c>.</summary>
    long Position { get; }

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
/// The dataset's settings: a fold over its <see cref="CommitKind.Settings"/>
/// commits, so every copy of the dataset agrees on them (ADR 0021).
/// </summary>
/// <remarks>
/// Erasure mode is part of the fold and is always off before milestone 9; it
/// is deliberately not public until something can turn it on.
/// </remarks>
public sealed class DatasetSettings
{
    internal DatasetSettings(AccessScope defaultAccessScope) => DefaultAccessScope = defaultAccessScope;

    internal static DatasetSettings Default { get; } = new(AccessScope.AllHistory);

    /// <summary>What an access request covers when it does not say.</summary>
    public AccessScope DefaultAccessScope { get; }

    internal static bool ErasureMode => false;
}

/// <summary>A change to the settings: every field left null is unchanged.</summary>
public sealed class SettingsChange
{
    /// <summary>A new default access scope, or null to leave it.</summary>
    public AccessScope? DefaultAccessScope { get; init; }
}

/// <summary>
/// The log does not verify: its chain breaks, a content hash or header hash
/// does not match, or its records are out of order. The store refuses to open
/// it rather than guess (ADR 0014).
/// </summary>
public sealed class LogVerificationException : Exception
{
    /// <summary>A refusal at a position.</summary>
    public LogVerificationException(long position, string message)
        : base(message) => Position = position;

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
    public long Position { get; }
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
