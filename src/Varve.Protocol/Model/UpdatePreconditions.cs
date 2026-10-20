// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>A graph and the version the client read it at: one entry of <c>Varve-If-Graphs-Match</c> (ADR 0126).</summary>
public sealed class GraphPrecondition
{
    /// <summary>Creates the entry; a <see langword="null"/> graph is the default graph.</summary>
    public GraphPrecondition(RdfTerm? graph, Position version)
    {
        Graph = graph;
        Version = version;
    }

    /// <summary>The graph's IRI; <see langword="null"/> for the default graph.</summary>
    public RdfTerm? Graph { get; }

    /// <summary>The version the client read: the position of the last commit that touched the graph.</summary>
    public Position Version { get; }
}

/// <summary>
/// What the client stated about the state its update was written against
/// (ADRs 0094, 0126): the dataset's position by <c>If-Match</c>, or the
/// graphs it read with their versions by <c>Varve-If-Graphs-Match</c>, never
/// both; or nothing, when the only precondition is the one the executor
/// infers from what the request reads and writes (ADR 0057, amended).
/// </summary>
public sealed class UpdatePreconditions
{
    /// <summary>No precondition from the client.</summary>
    public static UpdatePreconditions None { get; } = new();

    /// <summary>The position <c>If-Match</c> named.</summary>
    public Position? ExpectedPosition { get; init; }

    /// <summary>The graphs <c>Varve-If-Graphs-Match</c> named, with their versions.</summary>
    public IReadOnlyList<GraphPrecondition>? ExpectedGraphs { get; init; }

    /// <summary>
    /// The host's validators the commit carries into the sequencer, such as
    /// the order of management positions (ADR 0165); the executor passes
    /// them to the store with the request.
    /// </summary>
    public IReadOnlyList<Varve.Store.ICommitValidator> Validators { get; init; } = [];

    /// <summary>
    /// True when the client stated nothing: a conflict then comes from the
    /// executor's own inferred precondition, and the executor may re-pin and
    /// re-evaluate before answering it (ADR 0057, amended 2026-10-13).
    /// </summary>
    public bool IsImplicit => ExpectedPosition is null && ExpectedGraphs is null;
}
