// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>One implicit quad: the graph it is placed in (ADR 0190), and the fact.</summary>
/// <param name="Graph">The graph: a data graph's handle, <see cref="TermHandle.None"/> for the default graph, or the handle of the binding's <c>varve:inferred/…/tbox</c> graph.</param>
/// <param name="Fact">The fact, over the dataset's handles and the projection's own.</param>
public readonly record struct InferredRow(TermHandle Graph, Fact Fact);

/// <summary>
/// A binding's implicit quads at one position (ADR 0195 point 2): its rows
/// valid there, every one well formed. A handle the source does not hold is
/// the projection's own, named by <see cref="TryExternalise"/>; a read
/// stages it in its view.
/// </summary>
[Contract(typeof(TheInferenceProjection.RowsAreImplicitQuadsOverHandles), Role = "a binding's implicit quads at a position")]
public sealed class InferredRows
{
    private readonly Func<TermHandle, RdfTerm?> _local;

    /// <summary>
    /// <paramref name="rows"/>, the handles of which <paramref name="local"/>
    /// names that the source does not hold, and none else.
    /// </summary>
    public InferredRows(IReadOnlyList<InferredRow> rows, Func<TermHandle, RdfTerm?> local)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(local);
        Rows = rows;
        _local = local;
    }

    /// <summary>The rows.</summary>
    public IReadOnlyList<InferredRow> Rows { get; }

    /// <summary>
    /// The term of a handle that is the projection's own: a vocabulary
    /// constant, or the binding's TBox conclusions' graph, that the dataset
    /// did not hold. False for a dataset's handle.
    /// </summary>
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        term = _local(handle);
        return term is not null;
    }
}

/// <summary>A clash, and the graph whose closure it is in: a data graph, or the binding's TBox conclusions' graph.</summary>
/// <param name="Graph">The graph, as an <see cref="InferredRow"/> names it.</param>
/// <param name="Clash">The clash.</param>
public readonly record struct PlacedClash(TermHandle Graph, Clash Clash);
