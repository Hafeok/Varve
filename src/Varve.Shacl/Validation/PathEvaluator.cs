// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Shacl.Shapes;

namespace Varve.Shacl.Validation;

/// <summary>
/// SHACL paths over the data graph (ADR 0152, amended for ADR 0172): each
/// path's automaton searched over the data graph, giving the set of nodes it
/// reaches from a node — sequences and alternatives deduplicated, closures as
/// least fixpoints, a zero-length path reaching the start whatever the graph
/// holds — and, backwards, the nodes that reach one.
/// </summary>
internal sealed class PathEvaluator(DataGraph data)
{
    /// <summary>The nodes <paramref name="path"/> reaches from <paramref name="start"/>, distinct.</summary>
    [DesignDecision(typeof(ShaclHotPathScope.ValueNodesAreGatheredPerFocusNode), Scope = ExceptionScope.HotPath)]
    internal List<Node> Values(ShapePath path, Node start)
    {
        List<Node> result = [];
        path.Automaton.Evaluate(data, start, result, new HashSet<Node>(data.Nodes));
        return result;
    }

    /// <summary>The nodes from which <paramref name="path"/> reaches <paramref name="end"/>, added to <paramref name="into"/> unless <paramref name="seen"/> holds them.</summary>
    [DesignDecision(typeof(ShaclHotPathScope.ValueNodesAreGatheredPerFocusNode), Scope = ExceptionScope.HotPath)]
    internal void Sources(ShapePath path, Node end, List<Node> into, HashSet<Node> seen) => path.Automaton.Reverse(data, end, into, seen);
}
