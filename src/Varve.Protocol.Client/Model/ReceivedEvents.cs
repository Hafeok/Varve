// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;

namespace Varve.Protocol.Client.Model;

/// <summary>The events one commit holds, as the events resource delivered them, in order (ADR 0143).</summary>
public sealed class CommitEvents
{
    /// <summary>The events of the commit at <paramref name="position"/>.</summary>
    public CommitEvents(FeedPosition position, IReadOnlyList<ReceivedEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        Position = position;
        Events = events;
    }

    /// <summary>The commit's position.</summary>
    public FeedPosition Position { get; }

    /// <summary>The commit's events, problems in their places, in the stream's order.</summary>
    public IReadOnlyList<ReceivedEvent> Events { get; }
}

/// <summary>
/// One CloudEvent as received: its bytes are the server's canonical JSON,
/// untouched, so that what a consumer forwards is what was rendered.
/// </summary>
[DesignDecision(typeof(DomainEventsAsCloudevents.FollowEventsIsFollowAsyncsSibling), Scope = ExceptionScope.Boundary)]
public sealed class ReceivedEvent
{
    /// <summary>An event with these attributes and bytes.</summary>
    public ReceivedEvent(string id, string type, string sequence, bool isProblem, ReadOnlyMemory<byte> json)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(sequence);
        Id = id;
        Type = type;
        Sequence = sequence;
        IsProblem = isProblem;
        Json = json;
    }

    /// <summary>The CloudEvents <c>id</c>: the position, a space, the event node.</summary>
    public string Id { get; }

    /// <summary>The CloudEvents <c>type</c>: the short name, or the problem type's IRI.</summary>
    public string Type { get; }

    /// <summary>The <c>sequence</c> extension: ordered as strings, in commit and event order.</summary>
    public string Sequence { get; }

    /// <summary>Whether this stands for an event that could not be rendered.</summary>
    public bool IsProblem { get; }

    /// <summary>The structured CloudEvent, canonical JSON, as received.</summary>
    public ReadOnlyMemory<byte> Json { get; }
}
