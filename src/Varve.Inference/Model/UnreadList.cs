// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Inference.Model;

/// <summary>Why a cell the asserted statements name is not read as a list (ADR 0197, amended 2026-11-24).</summary>
public enum UnreadListReason
{
    /// <summary>More than one asserted <c>rdf:first</c>.</summary>
    SeveralFirsts,

    /// <summary>More than one asserted <c>rdf:rest</c>.</summary>
    SeveralRests,

    /// <summary>An asserted <c>rdf:rest</c> and no asserted <c>rdf:first</c>.</summary>
    NoFirst,

    /// <summary>An asserted <c>rdf:first</c> and no asserted <c>rdf:rest</c>.</summary>
    NoRest,

    /// <summary>Its <c>rdf:rest</c> chain comes back to a cell before it reaches <c>rdf:nil</c>.</summary>
    Cycle,

    /// <summary>Its <c>rdf:rest</c> chain reaches a node that is not a list cell before it reaches <c>rdf:nil</c>.</summary>
    Broken,

    /// <summary>
    /// A cell the TBox graphs make, of which the data graph asserts a
    /// different <c>rdf:first</c> or <c>rdf:rest</c>: the TBox's list is read,
    /// and the data graph's statement is not.
    /// </summary>
    DiffersFromTBox,

    /// <summary>
    /// A cell whose list goes on in another data graph: its graph asserts
    /// only one of its <c>rdf:first</c> and <c>rdf:rest</c> and another graph
    /// the other, or its <c>rdf:rest</c> chain reaches a cell only another
    /// graph makes. Under a cross-graph binding each graph's lists are read
    /// from its own statements and the TBox's alone (ADR 0197, amended
    /// 2026-11-26).
    /// </summary>
    SpansGraphs,
}

/// <summary>
/// A cell of an asserted list that the list rules and checks do not read
/// (ADR 0197, amended 2026-11-24): the subject of an asserted <c>rdf:first</c> or
/// <c>rdf:rest</c>, and why. Reported, never skipped in silence.
/// </summary>
/// <param name="Cell">The cell.</param>
/// <param name="Reason">Why it is not read.</param>
public readonly record struct UnreadList(TermHandle Cell, UnreadListReason Reason);

/// <summary>An unread list cell, under the graph whose closure reports it: the binding's TBox graph for one the TBox graphs make.</summary>
/// <param name="Graph">The graph.</param>
/// <param name="List">The cell and why.</param>
public readonly record struct PlacedUnreadList(TermHandle Graph, UnreadList List);

/// <summary>How many times a closure has read its lists differently, each time rebuilding it (ADR 0197, amended 2026-11-24).</summary>
/// <param name="Value">The count, from 0.</param>
public readonly record struct ListReadings(int Value);
