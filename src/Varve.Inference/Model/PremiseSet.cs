// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Inference.Model;

/// <summary>A data graph of a cross-graph binding, by the index its owner assigns (ADR 0190 point 6).</summary>
/// <param name="Value">The index, not negative.</param>
public readonly record struct DataGraphIndex(int Value);

/// <summary>
/// A premise set (ADR 0190 point 6): the data graphs one derivation of a fact
/// draws on, in increasing index. Empty for a fact drawn from the TBox and
/// the axioms alone; one graph for an intra-graph fact.
/// </summary>
public sealed class PremiseSet
{
    private readonly int[] _graphs;

    internal PremiseSet(int[] graphs) => _graphs = graphs;

    /// <summary>The graphs, in increasing index.</summary>
    public IReadOnlyList<DataGraphIndex> Graphs
    {
        get
        {
            DataGraphIndex[] graphs = new DataGraphIndex[_graphs.Length];
            for (int i = 0; i < graphs.Length; i++)
            {
                graphs[i] = new DataGraphIndex(_graphs[i]);
            }

            return graphs;
        }
    }

    /// <summary>Whether the set has two graphs or more: a conclusion with it as a minimal set lands in a reserved graph.</summary>
    public bool IsCrossGraph => _graphs.Length >= 2;

    /// <summary>Whether the set is empty: drawn from the TBox and the axioms alone.</summary>
    public bool IsEmpty => _graphs.Length == 0;

    /// <inheritdoc/>
    public override string ToString() => "{" + string.Join(", ", _graphs) + "}";
}
