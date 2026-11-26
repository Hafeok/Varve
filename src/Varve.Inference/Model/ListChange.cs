// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Inference.Model;

/// <summary>
/// What reading a cross-graph merge's lists again per graph changed
/// (<c>Closure.ReadListsPerGraph</c>):
/// whether the next delta rebuilds the merge, and, when it does, the cells
/// of the lists an axiom names whose reading changed, before and after, each
/// with the graph that reads it.
/// </summary>
/// <param name="CellsBefore">The statements of those lists' cells as each graph read them before.</param>
/// <param name="CellsAfter">The same, as each graph reads them after.</param>
public sealed record ListChange(IReadOnlyList<(Fact Fact, DataGraphIndex Graph)> CellsBefore, IReadOnlyList<(Fact Fact, DataGraphIndex Graph)> CellsAfter)
{
    /// <summary>No list an axiom names reads differently: the delta is applied as any other.</summary>
    public static ListChange None { get; } = new([], []);

    /// <summary>Whether the next delta rebuilds the merge: a list an axiom names reads differently, so it has cells on one side at least.</summary>
    public bool Rebuilds => CellsBefore.Count + CellsAfter.Count > 0;
}

/// <summary>One data graph's <c>rdf:first</c> and <c>rdf:rest</c> statements a delta adds and removes, for a cross-graph merge to read its lists per graph.</summary>
/// <param name="Added">The statements the graph now asserts.</param>
/// <param name="Removed">The statements it no longer does.</param>
public sealed record ListStatements(IReadOnlyCollection<Fact> Added, IReadOnlyCollection<Fact> Removed);
