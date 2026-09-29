// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>
/// A request to change the dataset: an ordered list of assertions and
/// retractions over terms, metadata, an optional expected position, and the
/// validators to run (spec T1).
/// </summary>
/// <remarks>
/// The operations are applied in order to an overlay on the head, and the
/// log records the net result: asserting a present quad, retracting an absent
/// one, and asserting then retracting an absent one contribute nothing
/// (ADR 0010).
/// </remarks>
public sealed class CommitRequest
{
    private readonly List<Operation> _operations = [];

    /// <summary>
    /// The position the caller read at. When given and not the readable head,
    /// the request is refused with <see cref="CommitOutcome.Conflict"/> and
    /// changes nothing (ADR 0011).
    /// </summary>
    public Position? ExpectedPosition { get; init; }

    /// <summary>What the commit records about itself.</summary>
    public CommitMetadata Metadata { get; init; } = new();

    /// <summary>Validators run against the proposed state, in order. The first reject wins.</summary>
    /// <remarks>
    /// Given when the request is made and read-only afterwards (DD0019): a
    /// request is handed to the sequencer, and the list it validates with is
    /// fixed by then.
    /// </remarks>
    public IReadOnlyList<ICommitValidator> Validators { get; init; } = [];

    /// <summary>How many quads the request asserts or retracts: one per operation.</summary>
    public QuadCount Count => new(_operations.Count);

    /// <summary>Asserts a quad in the default graph.</summary>
    public CommitRequest Assert(RequestTerm subject, RequestTerm predicate, RequestTerm @object) =>
        Add(true, subject, predicate, @object, RequestTerm.None);

    /// <summary>Asserts a quad in a named graph.</summary>
    public CommitRequest Assert(RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph) =>
        Add(true, subject, predicate, @object, graph);

    /// <summary>Retracts a quad from the default graph.</summary>
    public CommitRequest Retract(RequestTerm subject, RequestTerm predicate, RequestTerm @object) =>
        Add(false, subject, predicate, @object, RequestTerm.None);

    /// <summary>Retracts a quad from a named graph.</summary>
    public CommitRequest Retract(RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph) =>
        Add(false, subject, predicate, @object, graph);

    internal ReadOnlySpan<Operation> Operations => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_operations);

    private CommitRequest Add(bool assert, RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph)
    {
        if (subject.IsNone || predicate.IsNone || @object.IsNone)
        {
            throw new ArgumentException("A quad has a subject, a predicate and an object; only the graph may be absent.");
        }

        _operations.Add(new Operation(assert, subject, predicate, @object, graph));
        return this;
    }

    internal readonly struct Operation
    {
        internal Operation(bool assert, RequestTerm subject, RequestTerm predicate, RequestTerm @object, RequestTerm graph)
        {
            IsAssert = assert;
            Subject = subject;
            Predicate = predicate;
            Object = @object;
            Graph = graph;
        }

        internal bool IsAssert { get; }

        internal RequestTerm Subject { get; }

        internal RequestTerm Predicate { get; }

        internal RequestTerm Object { get; }

        internal RequestTerm Graph { get; }
    }
}
