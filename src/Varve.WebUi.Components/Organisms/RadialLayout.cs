// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.WebUi.Components.Organisms;

/// <summary>
/// The graph view's layout (ADR 0251): deterministic and radial, no physics.
/// The resource sits at the centre, its neighbours on a ring in the order
/// given (the page orders them by direction, predicate and term), and an
/// expanded neighbour's own neighbours on an arc of a second ring within its
/// sector. The same neighbourhood always gives the same picture, so a diff
/// laid out over the union of two positions keeps unchanged nodes in place.
/// </summary>
/// <remarks>
/// <para>
/// A ring's radius is the smallest that keeps its chips <see cref="Gap"/>
/// apart and leaves room on its shortest edge for its longest label. Each
/// label sits toward the neighbour end of its edge, at the first spot clear
/// of every node, every expand target and every label placed before it; a
/// sector where one does not fit keeps one label per predicate. Text is never
/// scaled: the bounds are the drawing's size in pixels.
/// </para>
/// </remarks>
[Contract(typeof(TheGraphView.DeterministicRadialLayout), Role = "the graph view's layout, a pure function of the rows")]
public static class RadialLayout
{
    /// <summary>A chip's width.</summary>
    public const int ChipWidth = 132;

    /// <summary>A chip's height; <see cref="TypeCaption"/> more with a type caption.</summary>
    public const int ChipHeight = 28;

    /// <summary>The type caption's height.</summary>
    public const int TypeCaption = 12;

    /// <summary>A label character's advance: 11px monospace.</summary>
    public const double LabelChar = 6.6;

    /// <summary>A label box's height.</summary>
    public const double LabelHeight = 14;

    /// <summary>The space between chips on a ring.</summary>
    public const double Gap = 16;

    /// <summary>An expand target's side (WCAG 2.2 SC 2.5.8).</summary>
    public const double Target = 24;

    /// <summary>The padding around the drawing.</summary>
    public const double Margin = 16;

    /// <summary>How many times the rings widen, by a quarter each, before a label that fits nowhere is an error.</summary>
    public const int Widenings = 12;

    /// <summary>
    /// Lays out <paramref name="centre"/> and its ring. Where a label fits
    /// nowhere, even at one per predicate, the rings widen by a quarter and
    /// the layout is made again: the first fit wins, so the same
    /// neighbourhood always gives the same picture.
    /// </summary>
    /// <exception cref="InvalidOperationException">A label still fits nowhere after <see cref="Widenings"/> widenings.</exception>
    public static GraphLayout Lay(NodeSpec centre, IReadOnlyList<RingNeighbour> ring)
    {
        ArgumentNullException.ThrowIfNull(centre);
        ArgumentNullException.ThrowIfNull(ring);
        string? unplaced = null;

        for (int widening = 0; widening <= Widenings; widening++)
        {
            if (TryLay(centre, ring, Math.Pow(1.25, widening), out GraphLayout? layout, out unplaced))
            {
                return layout;
            }
        }

        throw new InvalidOperationException($"The label {unplaced} fits nowhere, even at one label per predicate and rings widened {Widenings} times.");
    }

    private static bool TryLay(NodeSpec centre, IReadOnlyList<RingNeighbour> ring, double widen, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GraphLayout? layout, out string? unplaced)
    {
        layout = null;

        List<Placed> nodes = [new Placed(centre, 0, 0, Centre: true, Expand: null)];
        List<Edge> edges = [];

        if (ring.Count > 0)
        {
            int[] weights = ring.Select(n => n.Children is { Count: > 0 } children ? children.Count : 1).ToArray();
            int total = weights.Sum();
            double longest = ring.Max(n => n.Label.Length) * LabelChar + 4;
            double r1 = widen * Math.Max(total * (ChipWidth + Gap) / (2 * Math.PI), ChipWidth + longest + 24);
            int acc = 0;

            for (int i = 0; i < ring.Count; i++)
            {
                RingNeighbour neighbour = ring[i];
                double a = -90 + 360.0 * (acc + weights[i] / 2.0) / total;
                acc += weights[i];
                Placed node = new(neighbour.Node, r1 * Math.Cos(Radians(a)), r1 * Math.Sin(Radians(a)), Centre: false, Expandable(neighbour));
                nodes.Add(node);
                edges.Add(new Edge(nodes[0], node, neighbour.EdgeKind, neighbour.Label, i, neighbour.Predicate));

                if (neighbour.Children is not { Count: > 0 } kids)
                {
                    continue;
                }

                double span = 360.0 * weights[i] / total;
                double start = a - span / 2;
                double kidLongest = kids.Max(k => k.Label.Length) * LabelChar + 4;
                double r2 = Math.Max(r1 + widen * (ChipWidth + kidLongest + 24), kids.Count * (ChipWidth + Gap) / Radians(span));

                for (int j = 0; j < kids.Count; j++)
                {
                    double a2 = start + span * (j + 0.5) / kids.Count;
                    Placed kid = new(kids[j].Node, r2 * Math.Cos(Radians(a2)), r2 * Math.Sin(Radians(a2)), Centre: false, Expandable(kids[j]));
                    nodes.Add(kid);
                    edges.Add(new Edge(node, kid, kids[j].EdgeKind, kids[j].Label, i, kids[j].Predicate));
                }
            }
        }

        if (PlaceLabels(nodes, edges, out unplaced) is not { } labels)
        {
            return false;
        }

        List<Box> boxes = [.. nodes.Select(n => n.Box)];
        boxes.AddRange(nodes.Where(n => n.Hit is not null).Select(n => n.Hit!.Value));
        boxes.AddRange(labels.Values);
        double x0 = boxes.Min(b => b.X) - Margin;
        double y0 = boxes.Min(b => b.Y) - Margin;
        double x1 = boxes.Max(b => b.X + b.W) + Margin;
        double y1 = boxes.Max(b => b.Y + b.H) + Margin;

        layout = new GraphLayout(
            x0,
            y0,
            (int)Math.Ceiling(x1 - x0),
            (int)Math.Ceiling(y1 - y0),
            [.. nodes.Select(n => new GraphNode(n.X, n.Y, n.H, n.Spec.Label, Shorten(n.Spec.Label), n.Spec.Kind, n.Centre, n.Spec.Type, n.Spec.Diff, n.Expand, n.Spec.Name, n.Spec.Href))],
            [.. edges.Select(e => new GraphEdge(e.From.X, e.From.Y, e.To.X, e.To.Y, e.Kind))],
            [.. labels.OrderBy(l => l.Key).Select(l => new GraphLabel(l.Value.X + l.Value.W / 2, l.Value.Y + LabelHeight / 2, edges[l.Key].Label, LabelKind(edges[l.Key].Kind)))]);
        return true;
    }

    /// <summary>A label's box, centred on its point: what no other box may meet.</summary>
    public static (double X, double Y, double W, double H) LabelBox(GraphLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        double w = label.Text.Length * LabelChar + 4;
        return (label.X - w / 2, label.Y - LabelHeight / 2, w, LabelHeight);
    }

    /// <summary>A node's chip.</summary>
    public static (double X, double Y, double W, double H) NodeBox(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return (node.X - ChipWidth / 2.0, node.Y - node.Height / 2.0, ChipWidth, node.Height);
    }

    /// <summary>A node's expand target, centred on the chip's top right corner, or null.</summary>
    public static (double X, double Y, double W, double H)? TargetBox(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Expand is null ? null : (node.X + ChipWidth / 2.0 - Target / 2, node.Y - node.Height / 2.0 - Target / 2, Target, Target);
    }

    /// <summary>Whether two boxes meet, with <paramref name="pad"/> between them counted as meeting.</summary>
    public static bool Meet((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b, double pad = 2) =>
        a.X < b.X + b.W + pad && b.X < a.X + a.W + pad && a.Y < b.Y + b.H + pad && b.Y < a.Y + a.H + pad;

    private static Dictionary<int, Box>? PlaceLabels(List<Placed> nodes, List<Edge> edges, out string? unplaced)
    {
        unplaced = null;
        List<Box> blocked = [.. nodes.Select(n => n.Box)];
        blocked.AddRange(nodes.Where(n => n.Hit is not null).Select(n => n.Hit!.Value));
        HashSet<int> chosen = [.. Enumerable.Range(0, edges.Count)];

        while (true)
        {
            (Dictionary<int, Box>? placed, int failed) = Attempt(edges, blocked, chosen);

            if (placed is not null)
            {
                return placed;
            }

            int sector = edges[failed].Sector;
            HashSet<string> seen = [];
            HashSet<int> keep = [];

            foreach (int k in chosen.Order())
            {
                if (edges[k].Sector != sector)
                {
                    keep.Add(k);
                }
                else if (seen.Add(edges[k].Predicate))
                {
                    keep.Add(k);
                }
            }

            if (keep.SetEquals(chosen))
            {
                unplaced = edges[failed].Label;
                return null;
            }

            chosen = keep;
        }
    }

    private static (Dictionary<int, Box>? Placed, int Failed) Attempt(List<Edge> edges, List<Box> blocked, HashSet<int> chosen)
    {
        Dictionary<int, Box> placed = [];

        for (int k = 0; k < edges.Count; k++)
        {
            if (!chosen.Contains(k))
            {
                continue;
            }

            Edge edge = edges[k];
            double dx = edge.To.X - edge.From.X;
            double dy = edge.To.Y - edge.From.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length;
            double uy = dy / length;
            double w = edge.Label.Length * LabelChar + 4;
            double half = Math.Abs(ux) * w / 2 + Math.Abs(uy) * LabelHeight / 2;
            double d = Exit(edge.To, ux, uy) + half + 4;
            double stop = length - Exit(edge.From, ux, uy) - half - 4;
            Box? spot = null;

            for (; d <= stop; d += 4)
            {
                double cx = edge.To.X - ux * d;
                double cy = edge.To.Y - uy * d;
                Box box = new(cx - w / 2, cy - LabelHeight / 2, w, LabelHeight);

                if (!blocked.Any(o => Meet(box, o)) && !placed.Values.Any(o => Meet(box, o)))
                {
                    spot = box;
                    break;
                }
            }

            if (spot is null)
            {
                return (null, k);
            }

            placed[k] = spot.Value;
        }

        return (placed, -1);
    }

    private static double Exit(Placed node, double ux, double uy) =>
        Math.Min(ux != 0 ? ChipWidth / 2.0 / Math.Abs(ux) : 1e9, uy != 0 ? node.H / 2.0 / Math.Abs(uy) : 1e9);

    private static double Radians(double degrees) => degrees * (Math.PI / 180.0);

    private static string? Expandable(RingNeighbour neighbour) =>
        neighbour.Node.Kind == "literal" ? null : neighbour.Children is { Count: > 0 } ? "Collapse" : "Expand";

    private static string? LabelKind(string? edgeKind) => edgeKind is "added" or "removed" ? edgeKind : null;

    private static string Shorten(string text) => text.Length <= 16 ? text : text[..15] + "…";

    private readonly record struct Box(double X, double Y, double W, double H)
    {
        public static implicit operator (double X, double Y, double W, double H)(Box box) => (box.X, box.Y, box.W, box.H);
    }

    private sealed record Placed(NodeSpec Spec, double X, double Y, bool Centre, string? Expand)
    {
        public int H => ChipHeight + (Spec.Type is null ? 0 : TypeCaption);

        public Box Box => new(X - ChipWidth / 2.0, Y - H / 2.0, ChipWidth, H);

        public Box? Hit => Expand is null ? null : new Box(X + ChipWidth / 2.0 - RadialLayout.Target / 2, Y - H / 2.0 - RadialLayout.Target / 2, RadialLayout.Target, RadialLayout.Target);
    }

    private sealed record Edge(Placed From, Placed To, string? Kind, string Label, int Sector, string Predicate);
}
