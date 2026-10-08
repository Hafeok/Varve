// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    private readonly Dataset _dataset;
    private HashSet<ulong>? _carried;

    internal Commit(
        long position,
        CommitKind kind,
        DateTimeOffset timestamp,
        TermHandle agent,
        TermHandle cause,
        TermHandle graphScope,
        ReadOnlyMemory<TermHandle> attachments,
        QuadDelta delta,
        ReadOnlyMemory<TermAllocation> allocations,
        Dataset dataset)
    {
        _dataset = dataset;
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

    /// <summary>
    /// The term of a handle this commit carries — in its delta, its metadata,
    /// its attachments or its allocations — whichever commit allocated it.
    /// </summary>
    /// <remarks>
    /// The dataset's dictionary is append-only (spec §3: <c>D_P = D_{P−1} ∪
    /// alloc_P</c>), so a handle a closed commit carries names the same term
    /// at every later head, and is read from the current dictionary without a
    /// pin (ADR 0042, amended 2026-10-07). A handle the commit does not carry
    /// is answered <see langword="false"/> without a lookup: this names what
    /// the commit holds and nothing else.
    /// </remarks>
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        foreach (TermAllocation allocation in Allocations.Span)
        {
            if (allocation.Handle == handle)
            {
                term = allocation.Term;
                return true;
            }
        }

        if (handle.IsNone || !Carried().Contains(handle.Value))
        {
            term = null;
            return false;
        }

        term = _dataset.TermAtHead(handle.Value);
        return true;
    }

    private HashSet<ulong> Carried()
    {
        if (_carried is not null)
        {
            return _carried;
        }

        HashSet<ulong> carried = [Agent.Value, Cause.Value, GraphScope.Value];

        foreach (TermHandle attachment in Attachments.Span)
        {
            carried.Add(attachment.Value);
        }

        Add(carried, Delta.Asserted);
        Add(carried, Delta.Retracted);
        _carried = carried;
        return carried;
    }

    private static void Add(HashSet<ulong> carried, ReadOnlySpan<Quad> quads)
    {
        foreach (Quad quad in quads)
        {
            carried.Add(quad.Subject.Value);
            carried.Add(quad.Predicate.Value);
            carried.Add(quad.Object.Value);
            carried.Add(quad.Graph.Value);
        }
    }
}
