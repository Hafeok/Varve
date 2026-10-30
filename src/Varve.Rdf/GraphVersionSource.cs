// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>
/// A quad source that names the version of each graph it reads: a value that
/// changes whenever the graph's quads, as this source reads them, do, so that
/// what is derived from a graph can be kept while its version stands
/// (ADRs 0125, 0193). Optional: a source that implements it may still refuse a
/// graph, and what is derived from it is then not kept.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.SchemaCachedPerGraphVersion), Role = "a quad source that names the version of each graph")]
public interface IGraphVersionSource
{
    /// <summary>
    /// The version of <paramref name="graph"/>, a single graph
    /// (<see cref="GraphMatch.DefaultGraph"/> or <see cref="GraphMatch.Named"/>),
    /// as this source reads it; false when this source cannot name it. Two
    /// reads of a graph that report one version read the same quads;
    /// <see cref="GraphVersion.Empty"/> is a graph with none.
    /// </summary>
    bool TryGetGraphVersion(GraphPattern graph, out GraphVersion version);
}

/// <summary>
/// The version of one graph as a source reads it (ADR 0125): in the store,
/// the position of the last commit that touched the graph; 0 when none has,
/// and the graph holds no quad.
/// </summary>
/// <param name="Value">The version; 0 for a graph with no quad.</param>
public readonly record struct GraphVersion(long Value)
{
    /// <summary>The version of a graph with no quad, or one the source does not show.</summary>
    public static GraphVersion Empty => default;
}
