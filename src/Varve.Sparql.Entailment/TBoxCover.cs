// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Entailment;

/// <summary>
/// One TBox binding as the rewriting reads it (ADR 0194 point 7): its TBox
/// graphs, and the data graphs whose reads are merged with them. A graph is
/// covered when <see cref="DataGraphs"/> allows it, it is none of the TBox
/// graphs, and <see cref="Excluded"/> does not allow it.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.TBoxMergedIntoEveryGraph), Role = "a binding's TBox graphs and the graphs they are merged into")]
public sealed class TBoxCover
{
    /// <summary>A binding of <paramref name="tboxGraphs"/> covering <paramref name="dataGraphs"/>.</summary>
    public TBoxCover(IReadOnlyList<RdfTerm> tboxGraphs, GraphScope dataGraphs)
    {
        ArgumentNullException.ThrowIfNull(tboxGraphs);
        ArgumentNullException.ThrowIfNull(dataGraphs);
        TBoxGraphs = tboxGraphs;
        DataGraphs = dataGraphs;
    }

    /// <summary>The TBox graphs.</summary>
    public IReadOnlyList<RdfTerm> TBoxGraphs { get; }

    /// <summary>The data graphs, the default graph among them when the scope includes it.</summary>
    public GraphScope DataGraphs { get; }

    /// <summary>The graphs never covered, whatever <see cref="DataGraphs"/> says: a host's own graphs. None by default.</summary>
    public GraphScope Excluded { get; init; } = GraphScope.None;

    /// <summary>Whether a read of <paramref name="graph"/> is merged with the TBox graphs; <see langword="null"/> is the default graph.</summary>
    public bool Covers(RdfTerm? graph)
    {
        if (graph is null)
        {
            return DataGraphs.Allows(null) && !Excluded.Allows(null);
        }

        if (graph.Kind != RdfTermKind.Iri || !DataGraphs.Allows(graph) || Excluded.Allows(graph))
        {
            return false;
        }

        foreach (RdfTerm tbox in TBoxGraphs)
        {
            if (tbox.Equals(graph))
            {
                return false;
            }
        }

        return true;
    }
}
