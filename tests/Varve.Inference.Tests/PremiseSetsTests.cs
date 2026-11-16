// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Inference.Model;
using Varve.Rdf;
using Xunit;

namespace Varve.Inference.Tests;

/// <summary>
/// The minimal premise sets (ADR 0190 point 6) against their definition:
/// with no negation the rules are monotone, so a fact's minimal premise sets
/// are the minimal sets of data graphs whose merge, over the TBox, entails
/// it. The oracle closes the TBox with every subset of three data graphs.
/// </summary>
public class PremiseSetsTests
{
    private const int Graphs = 3;

    [Theory]
    [InlineData(InferenceProfile.Rdfs)]
    [InlineData(InferenceProfile.Owl2Rl)]
    public void each_facts_minimal_premise_sets_are_the_minimal_sets_of_graphs_that_entail_it(InferenceProfile profile)
    {
        Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple = profile == InferenceProfile.Rdfs ? IncrementalEqualsFromScratchTests.Triple : IncrementalEqualsFromScratchTests.OwlTriple;
        Gen.Select(triple.Array[0, 6], triple.Array[0, 6].Array[Graphs]).Sample(sample =>
        {
            Terms terms = new();
            Program program = Program.Of(profile, terms);
            Fact[] tbox = [.. sample.Item1.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()];
            Fact[][] data = [.. sample.Item2.Select(g => g.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct().ToArray())];

            Closure beneath = new(program, Retraction.DeleteAndRederive);
            beneath.Apply(tbox, []);
            Closure merge = new(program, Retraction.RecursiveCounting, budget: null, tbox: beneath);
            merge.Apply([.. data.SelectMany(g => g).Distinct()], []);
            List<(Fact, DataGraphIndex)> held = [];
            for (int g = 0; g < Graphs; g++)
            {
                held.AddRange(data[g].Select(f => (f, new DataGraphIndex(g))));
            }

            PremiseSets sets = new(merge, held);

            // The oracle: the closure of the TBox with each subset of the graphs.
            Dictionary<int, HashSet<Fact>> closures = [];
            for (int mask = 0; mask < 1 << Graphs; mask++)
            {
                Closure fresh = new(program, Retraction.FromScratch);
                fresh.Apply([.. tbox.Concat(Enumerable.Range(0, Graphs).Where(g => (mask & (1 << g)) != 0).SelectMany(g => data[g])).Distinct()], []);
                closures[mask] = [.. fresh.All];
            }

            foreach (Fact fact in merge.All)
            {
                int[] entailing = [.. closures.Keys.Where(mask => closures[mask].Contains(fact))];
                HashSet<int> minimal = [.. entailing.Where(m => !entailing.Any(o => o != m && (o & m) == o))];
                HashSet<int> actual = [.. sets.Of(fact).Select(s => s.Graphs.Aggregate(0, (mask, g) => mask | (1 << g.Value)))];
                Assert.True(minimal.SetEquals(actual), $"{profile}: {fact} has [{string.Join(", ", actual)}], the oracle [{string.Join(", ", minimal)}]");
            }
        }, iter: 60, threads: 1);
    }
}
