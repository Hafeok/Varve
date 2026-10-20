// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Management.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Management.Tests;

public sealed class PlanTests
{
    // Sized for the land gate: each case canonicalises a few small graphs.
    private const int Iterations = 200;

    private static readonly Gen<string[]> Graph = Gen.Int[0, 5].Select(Gen.Int[0, 3], Gen.Int[0, 4], (s, p, o) => "n:s" + s + " n:p" + p + " \"o" + o + "\" .").Array[0, 8];

    private static readonly Gen<(string[] A, string[] B, string[] F)> Declarations = Gen.Select(Graph, Graph, Graph);

    /// <summary>
    /// For generated desired and current declarations: applying the plan,
    /// which replaces each owned graph with its desired content, leaves the
    /// next plan empty; the plan's lines are exactly the difference; and a
    /// graph the files do not own is never touched.
    /// </summary>
    [Fact]
    public void applying_a_plan_leaves_the_next_plan_empty()
    {
        Gen.Select(Declarations, Declarations).Sample(
            pair =>
            {
                ((string[] a, string[] b, string[] _), (string[] ca, string[] cb, string[] cf)) = pair;
                InMemoryDataset desired = T.Trig(Wrap("a", a) + Wrap("b", b));
                InMemoryDataset current = T.Trig(Wrap("a", ca) + Wrap("b", cb) + Fleet(cf));
                IReadOnlyList<RdfTerm> owned = [ManagementVocabulary.DatasetGraph("a"), ManagementVocabulary.DatasetGraph("b")];

                ManagementPlan plan = ManagementPlan.Compute(desired, owned, current, new Dictionary<RdfTerm, ManagementPosition>(), new ManagementPosition(3));
                InMemoryDataset applied = Apply(current, plan);
                ManagementPlan again = ManagementPlan.Compute(desired, owned, applied, new Dictionary<RdfTerm, ManagementPosition>(), new ManagementPosition(4));

                Assert.True(again.IsEmpty);
                Assert.Equal(ManagementPlan.CanonicalLines(current, ManagementVocabulary.FleetGraph), ManagementPlan.CanonicalLines(applied, ManagementVocabulary.FleetGraph));

                foreach (GraphPlan graph in plan.Graphs)
                {
                    HashSet<string> before = [.. ManagementPlan.CanonicalLines(current, graph.Graph)];
                    HashSet<string> after = [.. graph.Content];
                    Assert.Equal(after.Except(before).Order(System.StringComparer.Ordinal), graph.Added);
                    Assert.Equal(before.Except(after).Order(System.StringComparer.Ordinal), graph.Removed);
                }

                Assert.Equal(plan.IsEmpty, ca.ToHashSet().SetEquals(a) && cb.ToHashSet().SetEquals(b));
            },
            iter: Iterations);
    }

    [Fact]
    public void a_saved_plan_reads_back_as_it_was_written()
    {
        InMemoryDataset desired = T.Trig(DeclarationTests.Notes);
        InMemoryDataset current = T.Trig("<https://w3id.org/varve/ns#manage/fleet> { n:x n:y n:z . }");
        Dictionary<RdfTerm, ManagementPosition> versions = new() { [ManagementVocabulary.FleetGraph] = new ManagementPosition(5) };
        ManagementPlan plan = ManagementPlan.Compute(desired, ManagementPlan.NamedGraphs(desired), current, versions, new ManagementPosition(9));

        ManagementPlan back = ManagementPlan.FromJson(plan.ToJson());

        Assert.Equal(new ManagementPosition(9), back.Position);
        Assert.Equal(plan.Graphs.Count, back.Graphs.Count);

        for (int i = 0; i < plan.Graphs.Count; i++)
        {
            Assert.Equal(plan.Graphs[i].Graph, back.Graphs[i].Graph);
            Assert.Equal(plan.Graphs[i].Version, back.Graphs[i].Version);
            Assert.Equal(plan.Graphs[i].Content, back.Graphs[i].Content);
            Assert.Equal(plan.Graphs[i].Added, back.Graphs[i].Added);
            Assert.Equal(plan.Graphs[i].Removed, back.Graphs[i].Removed);
        }

        Assert.Equal(new ManagementPosition(5), plan.Graphs.Single(graph => graph.Graph.Equals(ManagementVocabulary.FleetGraph)).Version);
        Assert.Contains("- <https://notes.example/manage/x>", plan.Render(), System.StringComparison.Ordinal);
        Assert.Throws<System.IO.InvalidDataException>(() => ManagementPlan.FromJson("{\"format\":2}"u8));
    }

    [Fact]
    public void declarations_written_as_trig_read_back_to_the_same_canonical_lines()
    {
        InMemoryDataset desired = T.Trig(DeclarationTests.Notes);
        IReadOnlyList<RdfTerm> graphs = ManagementPlan.NamedGraphs(desired);
        InMemoryDataset? back = ManagementTrig.TryParse(ManagementTrig.Write(desired, graphs), null, out string? error);

        Assert.True(back is not null, error);

        foreach (RdfTerm graph in graphs)
        {
            Assert.Equal(ManagementPlan.CanonicalLines(desired, graph), ManagementPlan.CanonicalLines(back!, graph));
        }
    }

    private static string Wrap(string dataset, string[] lines) =>
        "<https://w3id.org/varve/ns#manage/dataset/" + dataset + "> { " + string.Join(" ", lines) + " }\n";

    private static string Fleet(string[] lines) => "<https://w3id.org/varve/ns#manage/fleet> { " + string.Join(" ", lines) + " }\n";

    // What the server's apply does: every owned graph replaced by its content.
    private static InMemoryDataset Apply(InMemoryDataset current, ManagementPlan plan)
    {
        HashSet<RdfTerm> owned = [.. plan.Graphs.Select(graph => graph.Graph)];
        List<string> lines = [];

        foreach (RdfTerm graph in ManagementPlan.NamedGraphs(current))
        {
            if (!owned.Contains(graph))
            {
                lines.AddRange(ManagementPlan.CanonicalLines(current, graph));
            }
        }

        foreach (GraphPlan graph in plan.Graphs)
        {
            lines.AddRange(graph.Content);
        }

        InMemoryDataset? applied = ManagementTrig.TryParseLines(lines, out string? error);
        Assert.True(applied is not null, error);
        return applied!;
    }
}
