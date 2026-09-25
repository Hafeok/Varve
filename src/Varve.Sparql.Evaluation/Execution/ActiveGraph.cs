// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>Which graph a pattern is matched against (SPARQL 1.1 §18.6, the active graph).</summary>
internal enum GraphMode : byte
{
    /// <summary>The dataset's default graph: the source's, or the merge of the FROM graphs.</summary>
    Default,

    /// <summary>One named graph.</summary>
    Named,

    /// <summary>The named graphs, the graph name bound to a slot as scans find it (<c>GRAPH ?g</c>).</summary>
    Slot,
}

/// <summary>The active graph.</summary>
internal readonly struct ActiveGraph
{
    private ActiveGraph(GraphMode mode, TermHandle graph, int slot)
    {
        Mode = mode;
        Graph = graph;
        Slot = slot;
    }

    internal static ActiveGraph Default => default;

    internal GraphMode Mode { get; }

    internal TermHandle Graph { get; }

    internal int Slot { get; }

    internal static ActiveGraph Named(TermHandle graph) => new(GraphMode.Named, graph, -1);

    internal static ActiveGraph ForSlot(int slot) => new(GraphMode.Slot, default, slot);
}
