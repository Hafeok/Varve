// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Derivation;

/// <summary>
/// The graph a regular path walks (ADR 0172): the caller's nodes and edge
/// labels, read one step at a time. A store gives handles, SHACL gives its
/// nodes, a test gives integers.
/// </summary>
/// <typeparam name="TNode">The caller's node.</typeparam>
/// <typeparam name="TLabel">The caller's edge label.</typeparam>
[Contract(typeof(VarveDerivation.PathsAreRegularPaths), Role = "the graph a path walks, one step at a time")]
public interface IPathGraph<TNode, TLabel>
{
    /// <summary>The equality of nodes, which every search's visited set uses.</summary>
    IEqualityComparer<TNode> Nodes { get; }

    /// <summary>Adds to <paramref name="into"/> each node o with an edge <paramref name="subject"/> → o labelled <paramref name="label"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    void Objects(TNode subject, TLabel label, List<TNode> into);

    /// <summary>Adds to <paramref name="into"/> each node s with an edge s → <paramref name="obj"/> labelled <paramref name="label"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    void Subjects(TLabel label, TNode obj, List<TNode> into);
}
