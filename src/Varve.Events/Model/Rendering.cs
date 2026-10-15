// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Delta.Model;
using Varve.Rdf;

namespace Varve.Events.Model;

/// <summary>An asserted <c>rdf:type</c> in a commit's effective delta: the candidate event (docs/spec/events.md §4.1).</summary>
/// <param name="Node">The typed node.</param>
/// <param name="Class">The asserted class.</param>
/// <param name="Graph">The graph it was asserted in; <see langword="null"/> for the default graph.</param>
public readonly record struct TypeAssertion(RdfTerm Node, RdfTerm Class, RdfTerm? Graph);

/// <summary>A commit, as the renderer needs it: its position, its timestamp, and the types its effective delta asserts, in the delta's order.</summary>
public sealed record EventCommit(FeedPosition Position, FeedTimestamp Timestamp, IReadOnlyList<TypeAssertion> Assertions);

/// <summary>One rendered event: a CloudEvent in canonical JSON, or the problem in its place (docs/spec/events.md §4.2–§4.3).</summary>
public sealed class RenderedEvent
{
    internal RenderedEvent(FeedPosition position, int ordinal, string id, string type, bool isProblem, byte[] json)
    {
        Position = position;
        Ordinal = ordinal;
        Id = id;
        Type = type;
        IsProblem = isProblem;
        Json = json;
    }

    /// <summary>The commit's position.</summary>
    public FeedPosition Position { get; }

    /// <summary>The event's place within its commit, from 0.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public int Ordinal { get; }

    /// <summary>The CloudEvents <c>id</c>: the position, a space, the event node.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Id { get; }

    /// <summary>The CloudEvents <c>type</c>: the short name, or the problem type's IRI.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public string Type { get; }

    /// <summary>Whether this is the problem of an event that could not be rendered.</summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.EventSurfaceCarriesDeclaredValues), Scope = ExceptionScope.Boundary)]
    public bool IsProblem { get; }

    /// <summary>The CloudEvent, structured JSON, RFC 8785 canonical form.</summary>
    public ReadOnlyMemory<byte> Json { get; }
}
