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
/// it. The oracle closes the TBox with every subset of three data graphs, reading the lists as the merge reads them.
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
            Closure merge = new(program, Retraction.DeleteAndRederive, budget: null, tbox: beneath);
            merge.Apply([.. data.SelectMany(g => g).Distinct()], []);
            List<(Fact, DataGraphIndex)> held = [];
            for (int g = 0; g < Graphs; g++)
            {
                held.AddRange(data[g].Select(f => (f, new DataGraphIndex(g))));
            }

            PremiseSets sets = new(merge, held);

            // The oracle: the closure of the TBox with each subset of the graphs, the lists read as the
            // merge reads them (ADR 0197, amended 2026-11-24): a set is drawn from the merge's instances.
            Dictionary<int, HashSet<Fact>> closures = [];
            for (int mask = 0; mask < 1 << Graphs; mask++)
            {
                closures[mask] = Oracle.Closure(program, tbox.Concat(Enumerable.Range(0, Graphs).Where(g => (mask & (1 << g)) != 0).SelectMany(g => data[g])).Distinct(), merge.Facts.Lists);
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

    /// <summary>
    /// Maintained from each commit's delta, the sets equal those computed
    /// from scratch after it, for every fact, and every fact whose sets
    /// changed is answered with its sets before.
    /// </summary>
    [Theory]
    [InlineData(InferenceProfile.Rdfs)]
    [InlineData(InferenceProfile.Owl2Rl)]
    public void sets_maintained_commit_by_commit_equal_those_from_scratch(InferenceProfile profile)
    {
        Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple = profile == InferenceProfile.Rdfs ? IncrementalEqualsFromScratchTests.Triple : IncrementalEqualsFromScratchTests.OwlTriple;
        Gen.Select(triple.Array[0, 6], Gen.Select(triple, Gen.Int[0, Graphs - 1]).Array[1, 4].Array[4, 16], Gen.Int).Sample(sample =>
        {
            Terms terms = new();
            Program program = Program.Of(profile, terms);
            Closure beneath = new(program, Retraction.DeleteAndRederive);
            beneath.Apply([.. sample.Item1.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()], []);
            Closure merge = new(program, Retraction.DeleteAndRederive, budget: null, tbox: beneath);
            HashSet<Fact>[] graphs = [.. Enumerable.Range(0, Graphs).Select(_ => new HashSet<Fact>())];
            PremiseSets sets = new(merge, []);
            Random random = new(sample.Item3);
            foreach (((RdfTerm S, RdfTerm P, RdfTerm O) Triple, int Graph)[] commit in sample.Item2)
            {
                List<(Fact, int)> added = [], removed = [];
                foreach (((RdfTerm s, RdfTerm p, RdfTerm o), int g) in commit)
                {
                    Fact fact = terms.Fact(s, p, o);
                    if (added.Contains((fact, g)) || removed.Contains((fact, g)))
                    {
                        continue;
                    }

                    if (graphs[g].Contains(fact))
                    {
                        removed.Add((fact, g));
                    }
                    else
                    {
                        added.Add((fact, g));
                    }

                    // Now and then a retraction of a fact held elsewhere too.
                    if (random.Next(4) == 0 && graphs[random.Next(Graphs)] is { Count: > 0 } other)
                    {
                        Fact pick = other.ElementAt(random.Next(other.Count));
                        int from = Array.IndexOf(graphs, other);
                        if (!removed.Contains((pick, from)) && !added.Contains((pick, from)))
                        {
                            removed.Add((pick, from));
                        }
                    }
                }

                Dictionary<Fact, HashSet<int>> beforeSets = [];
                foreach (Fact fact in merge.Own)
                {
                    beforeSets[fact] = Masks(sets.Of(fact));
                }

                HashSet<Fact> mergedBefore = [.. graphs.SelectMany(g => g)];
                foreach ((Fact fact, int g) in removed)
                {
                    graphs[g].Remove(fact);
                }

                foreach ((Fact fact, int g) in added)
                {
                    graphs[g].Add(fact);
                }

                HashSet<Fact> mergedAfter = [.. graphs.SelectMany(g => g)];
                IReadOnlySet<Fact> affected = sets.Reach(removed.Select(r => (r.Item1, new DataGraphIndex(r.Item2))), (f, g) => graphs[g.Value].Contains(f));
                ListReadings readings = merge.ListReadings;
                merge.Apply([.. mergedAfter.Except(mergedBefore)], [.. mergedBefore.Except(mergedAfter)]);
                List<(Fact, DataGraphIndex)> held = [];
                for (int g = 0; g < Graphs; g++)
                {
                    held.AddRange(graphs[g].Where(f => affected.Contains(f) || added.Contains((f, g))).Select(f => (f, new DataGraphIndex(g))));
                }

                PremiseSets fresh = new(merge, Enumerable.Range(0, Graphs).SelectMany(g => graphs[g].Select(f => (f, new DataGraphIndex(g)))));
                IReadOnlyDictionary<Fact, IReadOnlyList<PremiseSet>> changed;
                if (merge.ListReadings != readings)
                {
                    // The merge read its lists again and was rebuilt (ADR 0197, amended 2026-11-24): the sets are labelled again from scratch, as the projection does, and every fact's sets before are the answer.
                    PremiseSets was = sets;
                    changed = merge.Own.Concat(beforeSets.Keys).Distinct().ToDictionary(f => f, f => was.Of(f));
                    sets = new PremiseSets(merge, Enumerable.Range(0, Graphs).SelectMany(g => graphs[g].Select(f => (f, new DataGraphIndex(g)))));
                }
                else
                {
                    changed = sets.Update(affected, held);
                }

                foreach (Fact fact in merge.All.Concat(beforeSets.Keys).Distinct())
                {
                    HashSet<int> now = Masks(sets.Of(fact)), expected = Masks(fresh.Of(fact));
                    Assert.True(expected.SetEquals(now), $"{profile}: {fact} has [{string.Join(", ", now)}], from scratch [{string.Join(", ", expected)}]");
                    HashSet<int> was = beforeSets.TryGetValue(fact, out HashSet<int>? b) ? b : beneath.Contains(fact) ? [0] : [];
                    if (!was.SetEquals(now))
                    {
                        Assert.True(changed.TryGetValue(fact, out IReadOnlyList<PremiseSet>? old) && Masks(old).SetEquals(was), $"{profile}: {fact} changed from [{string.Join(", ", was)}] and was not answered so");
                    }
                }
            }
        }, iter: 60, threads: 1);
    }

    private static HashSet<int> Masks(IReadOnlyList<PremiseSet> sets) => [.. sets.Select(s => s.Graphs.Aggregate(0, (mask, g) => mask | (1 << g.Value)))];
}
