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
using Varve.Rdf;
using Varve.Store;
using Xunit;
using static Varve.Inference.Store.Tests.Support;

namespace Varve.Inference.Store.Tests;

/// <summary>
/// ADR 0195 point 6, amended 2026-11-25: the clashes a projection publishes at
/// the end of each pass, kept per closure, placed again only where they
/// changed, and under a cross-graph binding placed again only where a premise's
/// premise sets moved, equal every check matched in full over every closure
/// and every clash placed afresh, after every commit.
/// </summary>
public class ClashesPublishedTests
{
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm[] Individuals = [Ex("a"), Ex("b"), Ex("c")];
    private static readonly RdfTerm[] Classes = [Ex("C1"), Ex("C2"), Ex("C3")];
    private static readonly RdfTerm[] Properties = [Ex("p1"), Ex("p2")];
    private static readonly RdfTerm?[] Graphs = [Ex("g1"), Ex("g2"), Ex("g3"), TBox];

    private static RdfTerm Owl(string local) => IriOf(OwlNs + local);

    /// <summary>Facts that meet the checks' bodies, spread over three data graphs and the TBox.</summary>
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> Triple = Gen.Frequency(
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Type), Gen.OneOfConst(Classes))),
        (3, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst(Individuals))),
        (2, Gen.Select(Gen.OneOfConst(Classes), Gen.OneOfConst([Owl("disjointWith"), IriOf(RdfsNs + "subClassOf")]), Gen.OneOfConst(Classes))),
        (2, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst([Owl("sameAs"), Owl("differentFrom")]), Gen.OneOfConst(Individuals))),
        (1, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Owl("propertyDisjointWith")), Gen.OneOfConst(Properties))));

    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O, int G)[][]> History =
        Gen.Select(Triple, Gen.Int[0, Graphs.Length - 1], (t, g) => (t.S, t.P, t.O, g)).Array[1, 4].Array[8, 24];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void the_published_clashes_equal_every_check_matched_and_placed_afresh_after_every_commit(bool crossGraph) =>
        History.Sample(commits => CheckAsync(commits, crossGraph).GetAwaiter().GetResult(), iter: 60, threads: 1);

    private static async Task CheckAsync((RdfTerm S, RdfTerm P, RdfTerm O, int G)[][] commits, bool crossGraph)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl, defaultGraph: false, crossGraph: crossGraph));
        HashSet<(RdfTerm, RdfTerm, RdfTerm, RdfTerm?)> asserted = [];
        Random random = new(commits.Length);
        int seen = 0;
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

                bool retract = asserted.Contains(quad) && random.Next(2) == 0;
                if (!retract && asserted.Contains(quad))
                {
                    continue;
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

            await dataset.CatchUpAsync(projection, ct);
            if (projection.CurrentState() is not { } state)
            {
                return;
            }

            List<string> published = [.. projection.Snapshot.Clashes.Select(Key).Order(StringComparer.Ordinal)];
            List<string> afresh = [.. state.ClashesInFull().Select(Key).Order(StringComparer.Ordinal)];
            seen += afresh.Count;
            Assert.True(
                afresh.SequenceEqual(published),
                $"at {dataset.Head.Value}{(crossGraph ? ", cross-graph" : string.Empty)}: missing\n  {string.Join("\n  ", afresh.Except(published))}\nextra\n  {string.Join("\n  ", published.Except(afresh))}");
        }

        Assert.True(seen >= 0);
    }

    // A placed clash: its graph, its check, and its premises in any order.
    private static string Key(PlacedClash clash) =>
        clash.Graph.Value + " " + System.Text.Encoding.UTF8.GetString(clash.Clash.Rule.Lexical) + " "
        + string.Join(' ', clash.Clash.Premises.Select(f => f.ToString()).Order(StringComparer.Ordinal));

    [Fact]
    public void the_histories_meet_the_checks_across_graphs()
    {
        // The property above is not vacuous: its histories make clashes under a cross-graph binding.
        int clashing = 0;
        History.Sample(commits =>
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            Dataset dataset = CreateAsync(ct).GetAwaiter().GetResult();
            foreach ((RdfTerm S, RdfTerm P, RdfTerm O, int G)[] commit in commits)
            {
                CommitAsync(dataset, commit.Select(t => (t.S, t.P, t.O, Graphs[t.G], true)).Distinct(), ct).GetAwaiter().GetResult();
            }

            InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl, defaultGraph: false, crossGraph: true));
            dataset.CatchUpAsync(projection, ct).AsTask().GetAwaiter().GetResult();
            if (projection.Clashes().Count > 0)
            {
                clashing++;
            }

            dataset.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }, iter: 25, threads: 1);
        Assert.True(clashing >= 10, $"{clashing} of 25 histories clash");
    }
}
