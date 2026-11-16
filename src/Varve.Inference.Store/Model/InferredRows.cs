// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Inference.Store.Model;

/// <summary>One implicit quad: the graph it is placed in (ADR 0190), and the fact.</summary>
/// <param name="Graph">The graph: a data graph's handle, <see cref="TermHandle.None"/> for the default graph, or the handle of the binding's <c>varve:inferred/…/tbox</c> graph.</param>
/// <param name="Fact">The fact, over the dataset's handles and the projection's own.</param>
public readonly record struct InferredRow(TermHandle Graph, Fact Fact);

/// <summary>
/// A binding's implicit quads at one position (ADR 0195 point 2): its rows
/// valid there, every one well formed. A handle the dataset does not hold is
/// the projection's own, named by <see cref="TryExternalise"/>; a read
/// stages it in its view.
/// </summary>
[Contract(typeof(TheInferenceProjection.RowsAreImplicitQuadsOverHandles), Role = "a binding's implicit quads at a position")]
public sealed class InferredRows
{
    private readonly Func<TermHandle, RdfTerm?> _local;

    internal InferredRows(Position position, IReadOnlyList<InferredRow> rows, Func<TermHandle, RdfTerm?> local)
    {
        Position = position;
        Rows = rows;
        _local = local;
    }

    /// <summary>The position the rows are valid at.</summary>
    public Position Position { get; }

    /// <summary>The rows.</summary>
    public IReadOnlyList<InferredRow> Rows { get; }

    /// <summary>
    /// The term of a handle that is the projection's own: a vocabulary
    /// constant, or the binding's TBox conclusions' graph, that the dataset
    /// did not hold. False for a dataset's handle.
    /// </summary>
    public bool TryExternalise(TermHandle handle, [MaybeNullWhen(false)] out RdfTerm term)
    {
        term = ProjectionTerms.IsLocal(handle) ? _local(handle) : null;
        return term is not null;
    }
}

/// <summary>What a read of a binding's closure at a position found (ADR 0195 points 5, 5a and 6).</summary>
public enum InferenceReadOutcome
{
    /// <summary>The rows are held: the position is at or above the floor and the held history, and reached.</summary>
    Held,

    /// <summary>The projection has not reached the position: wait, then <c>inference-behind</c>.</summary>
    Behind,

    /// <summary>Below the floor or the held history: the closure is computed from scratch, as of the position.</summary>
    FromScratch,

    /// <summary>The binding is stopped by its memory limit at or below the position: <c>inference-unavailable</c>.</summary>
    Unavailable,
}

/// <summary>A clash, and the graph whose closure it is in: a data graph, or the binding's TBox conclusions' graph.</summary>
/// <param name="Graph">The graph, as an <see cref="InferredRow"/> names it.</param>
/// <param name="Clash">The clash.</param>
public readonly record struct PlacedClash(TermHandle Graph, Clash Clash);
