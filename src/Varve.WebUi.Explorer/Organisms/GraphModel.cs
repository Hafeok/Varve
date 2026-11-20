// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.WebUi.Explorer.Organisms;

/// <summary>
/// A laid-out neighbourhood, as <c>GraphView</c> draws it: every coordinate
/// is decided here, by <see cref="RadialLayout"/>, so the view only renders
/// and the layout is tested on its own (ADR 0251).
/// </summary>
/// <param name="MinX">The left edge of the drawing, in layout units (pixels).</param>
/// <param name="MinY">The top edge of the drawing.</param>
/// <param name="Width">The drawing's width: the SVG's, one to one, never scaled.</param>
/// <param name="Height">The drawing's height.</param>
/// <param name="Nodes">The nodes, the centre first, then in ring order.</param>
/// <param name="Edges">The edges, in the nodes' order.</param>
/// <param name="Labels">The edge labels placed; a crowded sector keeps one per predicate.</param>
public sealed record GraphLayout(double MinX, double MinY, int Width, int Height, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, IReadOnlyList<GraphLabel> Labels);

/// <summary>One node: a term's chip at its centre point.</summary>
/// <param name="X">The chip's centre.</param>
/// <param name="Y">The chip's centre.</param>
/// <param name="Height">The chip's height: taller with a type caption.</param>
/// <param name="Label">The term, whole, as the chip's title.</param>
/// <param name="Shown">The term as drawn, shortened to fit.</param>
/// <param name="Kind">The chip's form: <c>iri</c>, <c>literal</c> or <c>bnode</c>.</param>
/// <param name="Centre">The resource the neighbourhood is of.</param>
/// <param name="Type">Its <c>rdf:type</c>, as a caption.</param>
/// <param name="Diff"><c>added</c> or <c>removed</c> in a diff.</param>
/// <param name="Expand"><c>Expand</c> or <c>Collapse</c>, when it can be.</param>
/// <param name="Name">Its accessible name: the relation, its direction and its kind.</param>
/// <param name="Href">Its own resource view.</param>
public sealed record GraphNode(double X, double Y, int Height, string Label, string Shown, string Kind, bool Centre, string? Type, string? Diff, string? Expand, string Name, string Href);

/// <summary>One edge, a straight line between two chips' centres.</summary>
/// <param name="X1">From.</param>
/// <param name="Y1">From.</param>
/// <param name="X2">To.</param>
/// <param name="Y2">To.</param>
/// <param name="Kind"><c>inferred</c>, <c>added</c> or <c>removed</c>, or none for an asserted edge.</param>
public sealed record GraphEdge(double X1, double Y1, double X2, double Y2, string? Kind);

/// <summary>One edge's label, centred on its point.</summary>
/// <param name="X">The label's centre.</param>
/// <param name="Y">The label's centre.</param>
/// <param name="Text">The predicate, and the edge's kind when it has one.</param>
/// <param name="Kind"><c>added</c> or <c>removed</c>, coloured so; none otherwise.</param>
public sealed record GraphLabel(double X, double Y, string Text, string? Kind);

/// <summary>A node to lay out: what it shows and says.</summary>
/// <param name="Label">The term, whole.</param>
/// <param name="Kind"><c>iri</c>, <c>literal</c> or <c>bnode</c>.</param>
/// <param name="Name">Its accessible name.</param>
/// <param name="Href">Its own resource view.</param>
/// <param name="Type">Its <c>rdf:type</c>, as a caption.</param>
/// <param name="Diff"><c>added</c> or <c>removed</c> in a diff.</param>
public sealed record NodeSpec(string Label, string Kind, string Name, string Href, string? Type = null, string? Diff = null);

/// <summary>A neighbour on a ring, with the edge that reaches it and, when expanded, its own neighbours.</summary>
/// <param name="Node">The neighbour.</param>
/// <param name="Predicate">The edge's predicate: the unit of the one-label-per-predicate fallback.</param>
/// <param name="Label">The edge's label: the predicate, and its kind's word when it has one.</param>
/// <param name="EdgeKind"><c>inferred</c>, <c>added</c> or <c>removed</c>, or none.</param>
/// <param name="Children">Its own neighbours, when it is expanded; null when it is not.</param>
public sealed record RingNeighbour(NodeSpec Node, string Predicate, string Label, string? EdgeKind = null, IReadOnlyList<RingNeighbour>? Children = null);

/// <summary>The graph view's node budget, for its budget line.</summary>
/// <param name="Shown">The nodes drawn.</param>
/// <param name="Total">The nodes the neighbourhood has.</param>
/// <param name="Budget">The node budget.</param>
/// <param name="TableHref">The same statements as a table.</param>
[Contract(typeof(TheGraphView.NodeBudgetFromTheSpike), Role = "the graph view's node budget and its budget line")]
public sealed record GraphBudget(int Shown, int Total, int Budget, string TableHref)
{
    /// <summary>
    /// The node budget: the largest size on the spike's grid whose first graph
    /// draws, cold median, in under 100 ms (ADR 0251: 50 measured 73.0 ms, 100
    /// measured 110.5 ms). Raised only from a re-measurement on the perf
    /// environment with the explorer lazy (#376).
    /// </summary>
    public const int Nodes = 50;
}
