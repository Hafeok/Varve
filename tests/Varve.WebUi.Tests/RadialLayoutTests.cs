// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bunit;
using CsCheck;
using Varve.WebUi.Explorer.Organisms;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The graph view's layout (ADR 0251): it is the layout the reviewed cases
/// were drawn with, it is deterministic, and no label ever meets another
/// label, a node or an expand target.
/// </summary>
public class RadialLayoutTests
{
    private const string Centre = "notes:42";

    [Theory]
    [InlineData("ring")]
    [InlineData("inferred-edges")]
    [InlineData("expanded")]
    [InlineData("diff")]
    public void it_lays_out_the_cases_as_they_were_drawn(string name)
    {
        GraphLayout expected = CaseLayout(name);
        GraphLayout actual = Neighbourhood(name);

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.MinX, actual.MinX, 9);
        Assert.Equal(expected.MinY, actual.MinY, 9);
        Assert.Equal(expected.Nodes.Count, actual.Nodes.Count);
        Assert.Equal(expected.Labels.Count, actual.Labels.Count);

        for (int i = 0; i < expected.Nodes.Count; i++)
        {
            Assert.Equal(expected.Nodes[i] with { X = 0, Y = 0 }, actual.Nodes[i] with { X = 0, Y = 0 });
            Assert.Equal(expected.Nodes[i].X, actual.Nodes[i].X, 9);
            Assert.Equal(expected.Nodes[i].Y, actual.Nodes[i].Y, 9);
        }

        for (int i = 0; i < expected.Labels.Count; i++)
        {
            Assert.Equal(expected.Labels[i].Text, actual.Labels[i].Text);
            Assert.Equal(expected.Labels[i].X, actual.Labels[i].X, 9);
            Assert.Equal(expected.Labels[i].Y, actual.Labels[i].Y, 9);
        }
    }

    [Fact]
    public void no_label_meets_another_label_a_node_or_a_target() =>
        Ring.Sample(ring =>
        {
            GraphLayout layout = RadialLayout.Lay(Spec(Centre, "iri"), ring);
            var labels = layout.Labels.Select(RadialLayout.LabelBox).ToList();
            var obstacles = layout.Nodes.Select(RadialLayout.NodeBox)
                .Concat(layout.Nodes.Select(RadialLayout.TargetBox).Where(t => t is not null).Select(t => t!.Value))
                .ToList();

            for (int i = 0; i < labels.Count; i++)
            {
                Assert.DoesNotContain(obstacles, o => RadialLayout.Meet(labels[i], o, pad: 0));

                for (int j = i + 1; j < labels.Count; j++)
                {
                    Assert.False(RadialLayout.Meet(labels[i], labels[j], pad: 0), $"labels {layout.Labels[i].Text} and {layout.Labels[j].Text} meet");
                }
            }
        }, iter: 300);

    [Fact]
    public void every_predicate_of_a_sector_keeps_a_label() =>
        Ring.Sample(ring =>
        {
            GraphLayout layout = RadialLayout.Lay(Spec(Centre, "iri"), ring);
            // The fallback drops a label only where another edge of its sector
            // has its predicate, so every predicate keeps one.
            foreach (RingNeighbour neighbour in ring)
            {
                Assert.Contains(layout.Labels, l => l.Text.StartsWith(neighbour.Predicate, StringComparison.Ordinal));
            }
        }, iter: 300);

    [Fact]
    public void the_same_neighbourhood_gives_the_same_picture() =>
        Ring.Sample(ring => Assert.Equal(
            JsonSerializer.Serialize(RadialLayout.Lay(Spec(Centre, "iri"), ring)),
            JsonSerializer.Serialize(RadialLayout.Lay(Spec(Centre, "iri"), [.. ring]))), iter: 100);

    [Fact]
    public void nodes_never_meet() =>
        Ring.Sample(ring =>
        {
            var boxes = RadialLayout.Lay(Spec(Centre, "iri"), ring).Nodes.Select(RadialLayout.NodeBox).ToList();

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Assert.False(RadialLayout.Meet(boxes[i], boxes[j], pad: 0));
                }
            }
        }, iter: 300);

    [Fact]
    public void a_label_that_fits_nowhere_after_the_last_widening_is_left_out_and_said()
    {
        // Thirty neighbours, each with a predicate of its own and a label of 40
        // characters. A ring's radius leaves room for its longest label, so
        // this fits at the first size; laid out from a fifth of that size with
        // no widening left, the labels across the steep spokes meet, and one
        // label per predicate is all a sector already has.
        RingNeighbour[] ring = [.. Enumerable.Range(0, 30).Select(i =>
            new RingNeighbour(Spec("ex:n" + i, "iri"), "ex:p" + i, "ex:p" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 35)))];

        GraphLayout widened = RadialLayout.Lay(Spec(Centre, "iri"), ring);
        GraphLayout last = RadialLayout.Lay(Spec(Centre, "iri"), ring, widenings: 0, scale: 0.2);

        Assert.True(widened.Unlabelled is null or { Count: 0 });
        Assert.Equal(ring.Length, widened.Labels.Count);

        Assert.NotNull(last.Unlabelled);
        Assert.NotEmpty(last.Unlabelled);
        Assert.Equal(ring.Length, last.Edges.Count);
        Assert.Equal(ring.Length, last.Labels.Count + last.Unlabelled.Count);
        Assert.Empty(last.Labels.Select(l => l.Text).Intersect(last.Unlabelled));

        var labels = last.Labels.Select(RadialLayout.LabelBox).ToList();
        var obstacles = last.Nodes.Select(RadialLayout.NodeBox).ToList();

        for (int i = 0; i < labels.Count; i++)
        {
            Assert.DoesNotContain(obstacles, o => RadialLayout.Meet(labels[i], o, pad: 0));

            for (int j = i + 1; j < labels.Count; j++)
            {
                Assert.False(RadialLayout.Meet(labels[i], labels[j], pad: 0));
            }
        }

        using BunitContext context = new();
        IRenderedComponent<GraphView> view = context.Render<GraphView>(parameters => parameters
            .Add(p => p.Id, "g")
            .Add(p => p.Title, "Neighbourhood of notes:42")
            .Add(p => p.Layout, last));
        string note = view.Find("p.vw-budget").TextContent;

        Assert.All(last.Unlabelled, label => Assert.Contains(label, note, StringComparison.Ordinal));
        Assert.Contains("drawn without", note, StringComparison.Ordinal);
        Assert.Empty(context.Render<GraphView>(parameters => parameters
            .Add(p => p.Id, "g")
            .Add(p => p.Title, "Neighbourhood of notes:42")
            .Add(p => p.Layout, widened)).FindAll("p.vw-budget"));
    }

    private static readonly string[] Predicates = ["dct:creator", "dct:title", "dct:subject", "schema:about", "dct:references", "rdfs:seeAlso"];

    // A ring of 1 to 40 neighbours, some predicates shared, some edges marked,
    // at most one expanded with 1 to 8 of its own.
    private static readonly Gen<RingNeighbour[]> Ring =
        from count in Gen.Int[1, 40]
        from predicates in Gen.Int[0, 5].Array[count]
        from kinds in Gen.Int[0, 3].Array[count]
        from expanded in Gen.Int[-1, count - 1]
        from kids in Gen.Int[1, 8]
        select Enumerable.Range(0, count).Select(i => new RingNeighbour(
            Spec("ex:n" + i, kinds[i] == 1 ? "literal" : "iri"),
            Predicates[predicates[i]],
            Predicates[predicates[i]] + (kinds[i] == 2 ? " · inferred" : kinds[i] == 3 ? " · added" : ""),
            kinds[i] == 2 ? "inferred" : kinds[i] == 3 ? "added" : null,
            i == expanded && kinds[i] != 1
                ? [.. Enumerable.Range(0, kids).Select(k => new RingNeighbour(Spec("ex:k" + k, "iri"), "foaf:knows", "foaf:knows"))]
                : null)).ToArray();

    private static NodeSpec Spec(string label, string kind, string? type = null, string? diff = null) =>
        new(label, kind, label, "#", type, diff);

    // The cases' neighbourhood, built as the preview's generator built it.
    private static GraphLayout Neighbourhood(string name)
    {
        (string D, string P, string T, string K, string? Type)[] ring =
        [
            ("out", "dct:creator", "users:alice", "iri", "schema:Person"),
            ("out", "dct:created", "\"2026-10-27\"", "literal", null),
            ("out", "dct:subject", "topics:planning", "iri", null),
            ("out", "dct:subject", "topics:sync", "iri", null),
            ("out", "dct:title", "\"Weekly sync\"@en", "literal", null),
            ("out", "schema:about", "_:b7", "bnode", null),
            ("in", "dct:references", "notes:7", "iri", null),
            ("in", "dct:references", "notes:9", "iri", null),
        ];
        (string D, string P, string T)[] kids = [("out", "foaf:knows", "users:bob"), ("out", "foaf:knows", "users:carol"), ("in", "dct:creator", "notes:51")];

        List<RingNeighbour> neighbours = [];

        for (int i = 0; i < ring.Length; i++)
        {
            var (d, p, t, k, type) = ring[i];
            bool inferred = name == "inferred-edges" && i is 2 or 5;
            string? diff = name == "diff" ? i switch { 3 => "added", 6 => "removed", _ => null } : null;
            string? kind = inferred ? "inferred" : diff;
            string note = kind is null ? "" : " · " + kind;
            string arrow = d == "out" ? "→" : "←";
            string nodeName = $"{p} {arrow} {t}, {(d == "out" ? "outgoing" : "incoming")}{(inferred ? ", inferred" : "")}{(diff is null ? "" : ", " + diff)}";
            List<RingNeighbour>? children = name == "expanded" && i == 0
                ? [.. kids.Select(kid => new RingNeighbour(
                    new NodeSpec(kid.T, "iri", $"{kid.P} {(kid.D == "out" ? "→" : "←")} {kid.T}, {(kid.D == "out" ? "outgoing" : "incoming")} from users:alice", "#"),
                    kid.P, kid.P))]
                : null;
            neighbours.Add(new RingNeighbour(new NodeSpec(t, k, nodeName, "#", type, diff), p, p + note, kind, children));
        }

        return RadialLayout.Lay(new NodeSpec(Centre, "iri", "notes:42, the resource at the centre", "#", "schema:Note"), neighbours);
    }

    private static GraphLayout CaseLayout(string name)
    {
        string path = Path.Combine(Root(), "docs", "ui", "components", "GraphView", "cases", name + ".json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("parameters").GetProperty("Layout").Deserialize<GraphLayout>()!;
    }

    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Varve.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No Varve.slnx above " + AppContext.BaseDirectory + ".");
    }
}
