// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Derivation.Model;

/// <summary>
/// A cycle that refuses a stratification: <see cref="Edge"/>, negative, both
/// or target, then the edges back from its target to its source, so the
/// cycle starts and ends at the edge's source.
/// </summary>
/// <typeparam name="TLabel">The caller's label of an edge.</typeparam>
public sealed class RefusedCycle<TLabel>
{
    internal RefusedCycle(DependencyEdge<TLabel> edge, IReadOnlyList<DependencyEdge<TLabel>> edges)
    {
        Edge = edge;
        Edges = edges;
    }

    /// <summary>The negative, both or target edge the cycle runs through.</summary>
    public DependencyEdge<TLabel> Edge { get; }

    /// <summary>The cycle's edges in order, starting with <see cref="Edge"/>.</summary>
    public IReadOnlyList<DependencyEdge<TLabel>> Edges { get; }
}
