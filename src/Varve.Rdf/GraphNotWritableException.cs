// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;

namespace Varve.Rdf;

/// <summary>
/// A change reaches a graph outside the caller's writable scope (ADR 0106):
/// the whole request fails before the submit, nothing is committed, and the
/// graph is named so that the caller knows which. Defined beside
/// <see cref="GraphScope"/>, at layer 1, because the executor that throws it
/// and the protocol that answers it are both layer 5 and cannot reference
/// each other (ADR 0091).
/// </summary>
public sealed class GraphNotWritableException : Exception
{
    /// <summary>A failure naming no graph.</summary>
    public GraphNotWritableException()
        : base("The request changes a graph outside the writable scope.")
    {
    }

    /// <summary>A failure with a message.</summary>
    public GraphNotWritableException(string message)
        : base(message)
    {
    }

    /// <summary>A failure with a message and its cause.</summary>
    public GraphNotWritableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The request changes <paramref name="graph"/>, or the default graph when <see langword="null"/>.</summary>
    public GraphNotWritableException(RdfTerm? graph)
        : base(graph is null ? "The request changes the default graph, which is outside the writable scope." : "The request changes <" + Encoding.UTF8.GetString(graph.Lexical) + ">, which is outside the writable scope.")
    {
        Graph = graph;
    }

    /// <summary>The graph the request may not change, or <see langword="null"/> for the default graph.</summary>
    public RdfTerm? Graph { get; }
}
