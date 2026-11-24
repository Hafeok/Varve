// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;
using static Varve.Inference.Store.Tests.Support;

namespace Varve.Inference.Store.Tests;

/// <summary>
/// ADR 0195 point 9 over the store: after generated histories of commits to
/// the TBox graph and to several data graphs, the default graph among them,
/// with retractions, every read of the projection at every position is the
/// oracle's closure of that position: held where the history holds it, from
/// scratch below it, under every history ceiling.
/// </summary>
public class ProjectionPropertyTests
{
    private static readonly bool Long = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is not null;
    private static readonly int Histories = Long ? 200 : 20;
    private static readonly int Commits = Long ? 60 : 20;

    private static readonly RdfTerm[] Individuals = [Ex("a"), Ex("b"), Ex("c")];
    private static readonly RdfTerm[] Classes = [Ex("C1"), Ex("C2"), Ex("C3")];
    private static readonly RdfTerm[] Properties = [Ex("p1"), Ex("p2")];
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm[] Schema = [IriOf(RdfsNs + "subClassOf"), IriOf(RdfsNs + "subPropertyOf"), IriOf(RdfsNs + "domain"), IriOf(RdfsNs + "range")];
    private static readonly RdfTerm?[] Graphs = [TBox, TBox, Ex("g1"), Ex("g2"), null, Ex("other")];

    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> Triple = Gen.Frequency(
        (5, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst([.. Individuals, RdfTerm.Literal("x"u8)]))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Type), Gen.OneOfConst(Classes))),
        (4, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(Schema), Gen.OneOfConst([.. Classes, .. Properties]))),
        (1, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Type), Gen.OneOfConst([IriOf(OwlNs + "SymmetricProperty"), IriOf(OwlNs + "TransitiveProperty")]))));

    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O, int G)[][]> History =
        Gen.Select(Triple, Gen.Int[0, Graphs.Length - 1], (t, g) => (t.S, t.P, t.O, g)).Array[1, 4].Array[Commits / 2, Commits];

    [Theory]
    [InlineData(InferenceProfile.Rdfs, 32L << 20)]
    [InlineData(InferenceProfile.Rdfs, 0L)]
    [InlineData(InferenceProfile.Rdfs, 640L)]
    [InlineData(InferenceProfile.Owl2Rl, 640L)]
    public void every_read_at_every_position_is_the_closure_of_that_position(InferenceProfile profile, long historyBytes) =>
        History.Sample(commits => Check(profile, historyBytes, commits).GetAwaiter().GetResult(), iter: Histories, threads: 1);

    /// <summary>A cross-graph binding's rows (ADR 0190 point 6), each placed by its minimal premise sets.</summary>
    [Theory]
    [InlineData(InferenceProfile.Rdfs, 32L << 20)]
    [InlineData(InferenceProfile.Rdfs, 640L)]
    [InlineData(InferenceProfile.Owl2Rl, 32L << 20)]
    public void a_cross_graph_bindings_reads_place_each_row_by_its_minimal_premise_sets(InferenceProfile profile, long historyBytes) =>
        History.Sample(commits => Check(profile, historyBytes, commits, crossGraph: true).GetAwaiter().GetResult(), iter: Histories, threads: 1);

    private static async Task Check(InferenceProfile profile, long historyBytes, (RdfTerm S, RdfTerm P, RdfTerm O, int G)[][] commits, bool crossGraph = false)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(profile, crossGraph: crossGraph);
        InferenceProjection projection = new(dataset, binding, new InferenceOptions { HistoryBytes = new ByteCount(historyBytes) });
        HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm?)> asserted = [];
        Random random = new(commits.Length);
        foreach ((RdfTerm S, RdfTerm P, RdfTerm O, int G)[] commit in commits)
        {
            List<(RdfTerm, RdfTerm, RdfTerm, RdfTerm?, bool)> toggles = [];
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o, int g) in commit)
            {
                (RdfTerm, RdfTerm, RdfTerm, RdfTerm?) quad = (s, p, o, Graphs[g]);
                if (toggles.Any(t => (t.Item1, t.Item2, t.Item3, t.Item4).Equals(quad)))
                {
                    continue;
                }

                bool retract = asserted.Contains(quad) || (asserted.Count > 0 && random.Next(3) == 0);
                if (retract && !asserted.Contains(quad))
                {
                    quad = asserted.ElementAt(random.Next(asserted.Count));
                    if (toggles.Any(t => (t.Item1, t.Item2, t.Item3, t.Item4).Equals(quad)))
                    {
                        continue;
                    }
                }

                toggles.Add((quad.Item1, quad.Item2, quad.Item3, quad.Item4, !retract));
            }

            if (await CommitAsync(dataset, toggles, ct))
            {
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o, RdfTerm? g, bool assert) in toggles)
                {
                    _ = assert ? asserted.Add((s, p, o, g)) : asserted.Remove((s, p, o, g));
                }
            }

            // Half the time the projection keeps up commit by commit; otherwise it catches up at the end.
            if (random.Next(2) == 0)
            {
                await dataset.CatchUpAsync(projection, ct);
            }
        }

        await dataset.CatchUpAsync(projection, ct);
        Assert.Equal(dataset.Head, projection.Position);
        Assert.True(projection.HistoryHeld.Value <= historyBytes);

        for (long p = 0; p <= dataset.Head.Value; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
            HashSet<string> expected = crossGraph ? ExpectedCrossGraph(view, binding) : Expected(view, binding);
            InferenceReadOutcome outcome = projection.TryRead(new Position(p), out InferredRows? rows);
            if (outcome == InferenceReadOutcome.FromScratch)
            {
                Assert.True(p < projection.HeldFrom.Value, $"position {p} is read from scratch at or above HeldFrom {projection.HeldFrom.Value}");
                rows = await projection.ReadFromScratchAsync(new Position(p), ct);
            }
            else
            {
                Assert.Equal(InferenceReadOutcome.Held, outcome);
            }

            HashSet<string> actual = Text(rows!, view);
            if (!expected.SetEquals(actual))
            {
                Assert.Fail($"at position {p} of {dataset.Head.Value} ({outcome}):\nmissing:\n  {string.Join("\n  ", expected.Except(actual).Order(StringComparer.Ordinal))}\nextra:\n  {string.Join("\n  ", actual.Except(expected).Order(StringComparer.Ordinal))}");
            }
        }

        Assert.Equal(InferenceReadOutcome.Behind, projection.TryRead(new Position(dataset.Head.Value + 1), out _));
    }
}
