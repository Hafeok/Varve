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
using static Varve.Inference.Tests.Terms;

namespace Varve.Inference.Tests;

/// <summary>
/// ADR 0195 point 6, amended 2026-11-25: a closure keeps its clashes from the
/// facts each pass changes, so a probe never matches the checks over the whole
/// closure. What it keeps equals every check matched in full over the closure,
/// at every commit, for each retraction strategy, with and without a TBox
/// closure beneath.
/// </summary>
public class ClashesKeptPerPassTests
{
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";

    private static readonly RdfTerm[] Individuals = [Named("a"), Named("b"), Named("c")];
    private static readonly RdfTerm[] Classes = [Named("C1"), Named("C2"), Named("C3")];
    private static readonly RdfTerm[] Properties = [Named("p1"), Named("p2"), Named("p3")];
    private static readonly RdfTerm[] Cells = [Named("L1"), Named("L2")];
    private static readonly RdfTerm Nil = IriOf(RdfNs + "nil");
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");

    private static RdfTerm Owl(string local) => IriOf(OwlNs + local);

    /// <summary>Triples that meet the checks' bodies, beside the general ones: disjointness, difference, characteristics, lists of members.</summary>
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> ClashTriple = Gen.Frequency(
        (8, IncrementalEqualsFromScratchTests.OwlTriple),
        (2, Gen.Select(Gen.OneOfConst(Classes), Gen.OneOfConst([Owl("disjointWith"), Owl("complementOf")]), Gen.OneOfConst([.. Classes, Owl("Nothing")]))),
        (2, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst([Owl("sameAs"), Owl("differentFrom")]), Gen.OneOfConst(Individuals))),
        (1, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Type), Gen.OneOfConst([Owl("AsymmetricProperty"), Owl("IrreflexiveProperty")]))),
        (1, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Owl("propertyDisjointWith")), Gen.OneOfConst(Properties))),
        (1, Gen.Select(Gen.Const(Named("x")), Gen.Const(Type), Gen.OneOfConst([Owl("AllDifferent"), Owl("AllDisjointClasses"), Owl("AllDisjointProperties")]))),
        (1, Gen.Select(Gen.Const(Named("x")), Gen.OneOfConst([Owl("members"), Owl("distinctMembers")]), Gen.OneOfConst(Cells))),
        (2, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "first")), Gen.OneOfConst([.. Classes, .. Properties, .. Individuals]))),
        (2, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "rest")), Gen.OneOfConst([.. Cells, Nil]))));

    private static readonly Gen<(int Seed, (RdfTerm S, RdfTerm P, RdfTerm O)[][] Commits)> Histories =
        Gen.Select(Gen.Int, ClashTriple.Array[1, 4].Array[15, 40]);

    [Theory]
    [InlineData(InferenceProfile.Rdfs, "DeleteAndRederive", false)]
    [InlineData(InferenceProfile.Owl2Rl, "DeleteAndRederive", false)]
    [InlineData(InferenceProfile.Owl2Rl, "RecursiveCounting", false)]
    [InlineData(InferenceProfile.Owl2Rl, "DeleteAndRederive", true)]
    public void the_clashes_kept_equal_every_check_matched_in_full_at_every_commit(InferenceProfile profile, string strategyName, bool beneath)
    {
        Retraction strategy = Strategies.Named(strategyName);
        Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple = profile == InferenceProfile.Rdfs ? IncrementalEqualsFromScratchTests.Triple : ClashTriple;
        Gen.Select(triple.Array[0, beneath ? 6 : 0], Histories).Sample(sample =>
        {
            Terms terms = new();
            Program program = Program.Of(profile, terms);
            Closure? tbox = null;
            if (beneath)
            {
                tbox = new Closure(program, Retraction.DeleteAndRederive);
                tbox.Apply([.. sample.Item1.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()], []);
            }

            Closure closure = new(program, strategy, budget: null, tbox: tbox);
            HashSet<Fact> data = [];
            Random random = new(sample.Item2.Seed);
            for (int c = 0; c < sample.Item2.Commits.Length; c++)
            {
                List<Fact> asserted = [], retracted = [];
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in sample.Item2.Commits[c])
                {
                    Fact fact = terms.Fact(s, p, o);
                    if (data.Contains(fact) && random.Next(2) == 0 && !retracted.Contains(fact))
                    {
                        retracted.Add(fact);
                    }
                    else if (!data.Contains(fact) && !asserted.Contains(fact))
                    {
                        asserted.Add(fact);
                    }
                }

                closure.Apply([.. asserted], [.. retracted]);
                data.ExceptWith(retracted);
                data.UnionWith(asserted);

                // Asked now and then, as a probe asks: between two calls a pass
                // or several may have run, and the first is after the build.
                if (random.Next(3) != 0)
                {
                    continue;
                }

                List<string> kept = [.. closure.Clashes().Select(clash => Key(clash.Rule, clash.Premises)).Order(StringComparer.Ordinal)];
                List<string> full = [.. InFull(closure).Order(StringComparer.Ordinal)];
                Assert.True(
                    full.SequenceEqual(kept),
                    $"after commit {c + 1}, {profile}, {strategyName}: missing\n  {string.Join("\n  ", full.Except(kept))}\nextra\n  {string.Join("\n  ", kept.Except(full))}");
            }
        }, iter: 60, threads: 1);
    }

    [Fact]
    public void the_histories_meet_the_checks()
    {
        // The property above is not vacuous: its histories make clashes, and lose them.
        int clashing = 0;
        Histories.Sample(history =>
        {
            Terms terms = new();
            Closure closure = new(Program.Of(InferenceProfile.Owl2Rl, terms), Retraction.DeleteAndRederive);
            closure.Apply([.. history.Commits.SelectMany(commit => commit).Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()], []);
            if (closure.Clashes().Count > 0)
            {
                clashing++;
            }
        }, iter: 60, threads: 1);
        Assert.True(clashing >= 10, $"{clashing} of 60 histories clash");
    }

    // Every check matched over the whole closure, each set of premises once: what Clashes() answered before it kept them.
    private static IEnumerable<string> InFull(Closure closure)
    {
        foreach (Check check in closure.Program.Checks)
        {
            List<Fact[]> matches = [];
            check.Matches(closure.Facts, matches);
            HashSet<string> each = [];
            foreach (Fact[] premises in matches)
            {
                string key = Key(check.Iri, premises);
                if (each.Add(key))
                {
                    yield return key;
                }
            }
        }
    }

    private static string Key(RdfTerm rule, IReadOnlyList<Fact> premises) =>
        rule + " " + string.Join(' ', premises.Select(premise => premise.ToString()).Order(StringComparer.Ordinal));
}
