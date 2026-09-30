// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;

namespace Varve.Store.Log;

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
        Position = new Position(position);
        Kind = kind;
        Timestamp = new CommitTimestamp(timestamp);
        Agent = agent;
        Cause = cause;
        GraphScope = graphScope;
        Attachments = attachments;
        Delta = delta;
        Allocations = allocations;
    }

    /// <summary>Its position in the log.</summary>
    public Position Position { get; }

    /// <summary>What kind of commit it is.</summary>
    public CommitKind Kind { get; }

    /// <summary>When the sequencer closed it: <c>max(clock, ts(previous))</c>, so never earlier than the one before.</summary>
    public CommitTimestamp Timestamp { get; }

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
