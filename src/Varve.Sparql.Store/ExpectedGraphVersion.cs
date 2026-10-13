// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Sparql.Store;

/// <summary>A graph and the version a client read it at (ADR 0126): one entry of <c>Varve-If-Graphs-Match</c>.</summary>
public sealed class ExpectedGraphVersion
{
    /// <summary>A named graph at a version.</summary>
    public ExpectedGraphVersion(RdfTerm graph, Position version)
    {
        Graph = graph;
        Version = version;
    }

    /// <summary>The default graph at a version.</summary>
    public ExpectedGraphVersion(Position version) => Version = version;

    /// <summary>The graph's IRI; <see langword="null"/> for the default graph.</summary>
    public RdfTerm? Graph { get; }

    /// <summary>The version the client read: the position of the last commit that touched the graph.</summary>
    public Position Version { get; }

    /// <summary>The graph as the store names it in a scope: <see cref="RequestTerm.None"/> for the default graph.</summary>
    internal RequestTerm Term => Graph is { } graph ? RequestTerm.FromTerm(graph) : RequestTerm.None;
}
