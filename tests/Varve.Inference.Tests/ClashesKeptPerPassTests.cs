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

                // At every commit, the first after the build.
                List<string> kept = [.. closure.Clashes().Select(clash => Key(clash.Rule, clash.Premises)).Order(StringComparer.Ordinal)];
                List<string> full = [.. InFull(closure).Order(StringComparer.Ordinal)];
                Assert.True(
                    full.SequenceEqual(kept),
                    $"after commit {c + 1}, {profile}, {strategyName}: missing\n  {string.Join("\n  ", full.Except(kept))}\nextra\n  {string.Join("\n  ", kept.Except(full))}");
            }
        }, iter: 60, threads: 1);
    }

    /// <summary>
    /// The list checks kept from the facts a pass changes (ADR 0195 point 6,
    /// amended 2026-11-25). Three well-formed lists, of individuals, classes
    /// and properties, each named by an axiom, are asserted first, in the
    /// data or in a TBox beneath; the commits then toggle the facts their
    /// pairs match and, now and then, a typing, a members axiom or a cell's
    /// statement, so that a list check's matches come and go each way, and a
    /// list is read differently.
    /// </summary>
    [Theory]
    [InlineData("DeleteAndRederive", false)]
    [InlineData("RecursiveCounting", false)]
    [InlineData("DeleteAndRederive", true)]
    public void the_list_checks_kept_equal_a_full_match_at_every_commit(string strategyName, bool beneath)
    {
        Retraction strategy = Strategies.Named(strategyName);
        Gen.Select(ListTriple.Array[1, 4].Array[15, 40], Gen.Int).Sample(sample =>
        {
            Terms terms = new();
            Program program = Program.Of(InferenceProfile.Owl2Rl, terms);
            Fact[] lists = [.. Lists.Select(t => terms.Fact(t.S, t.P, t.O))];
            Closure? tbox = null;
            if (beneath)
            {
                tbox = new Closure(program, Retraction.DeleteAndRederive);
                tbox.Apply(lists, [], TestContext.Current.CancellationToken);
            }

            Closure closure = new(program, strategy, budget: null, tbox: tbox);
            HashSet<Fact> data = [];
            if (!beneath)
            {
                closure.Apply(lists, [], TestContext.Current.CancellationToken);
                data.UnionWith(lists);
            }

            Random random = new(sample.Item2);
            for (int c = 0; c < sample.Item1.Length; c++)
            {
                List<Fact> asserted = [], retracted = [];
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in sample.Item1[c])
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

                closure.Apply([.. asserted], [.. retracted], TestContext.Current.CancellationToken);
                data.ExceptWith(retracted);
                data.UnionWith(asserted);
                List<string> kept = [.. closure.Clashes().Select(clash => Key(clash.Rule, clash.Premises)).Order(StringComparer.Ordinal)];
                List<string> full = [.. InFull(closure).Order(StringComparer.Ordinal)];
                Assert.True(
                    full.SequenceEqual(kept),
                    $"after commit {c + 1}, {strategyName}{(beneath ? " over a TBox" : string.Empty)}: missing\n  {string.Join("\n  ", full.Except(kept))}\nextra\n  {string.Join("\n  ", kept.Except(full))}");
            }
        }, iter: 80, threads: 1);
    }

    [Fact]
    public void the_list_histories_meet_the_list_checks()
    {
        // The property above is not vacuous: each list check fires in some history.
        HashSet<string> fired = [];
        ListTriple.Array[10, 40].Sample(triples =>
        {
            Terms terms = new();
            Closure closure = new(Program.Of(InferenceProfile.Owl2Rl, terms), Retraction.DeleteAndRederive);
            closure.Apply([.. Lists.Concat(triples).Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()], [], TestContext.Current.CancellationToken);
            foreach (Clash clash in closure.Clashes())
            {
                fired.Add(System.Text.Encoding.UTF8.GetString(clash.Rule.Lexical));
            }
        }, iter: 100, threads: 1);
        foreach (string check in new[] { "eq-diff2", "eq-diff3", "prp-adp", "cax-adc" })
        {
            Assert.Contains(fired, rule => rule.EndsWith("/owl2rl/" + check, StringComparison.Ordinal));
        }
    }

    private static readonly RdfTerm First = IriOf(RdfNs + "first");
    private static readonly RdfTerm Rest = IriOf(RdfNs + "rest");

    /// <summary>A list's cells, <c>{name}1</c> to <c>{name}3</c>, holding <paramref name="members"/>.</summary>
    private static IEnumerable<(RdfTerm S, RdfTerm P, RdfTerm O)> List(string name, RdfTerm[] members)
    {
        for (int i = 0; i < members.Length; i++)
        {
            yield return (Named(name + (i + 1)), First, members[i]);
            yield return (Named(name + (i + 1)), Rest, i + 1 < members.Length ? Named(name + (i + 2)) : Nil);
        }
    }

    /// <summary>Three lists and the axioms naming them: individuals all different, classes and properties all disjoint.</summary>
    private static readonly (RdfTerm S, RdfTerm P, RdfTerm O)[] Lists =
    [
        .. List("I", Individuals), .. List("C", Classes), .. List("P", Properties),
        (Named("x"), Type, Owl("AllDifferent")), (Named("x"), Owl("members"), Named("I1")), (Named("x"), Owl("distinctMembers"), Named("I1")),
        (Named("y"), Type, Owl("AllDisjointClasses")), (Named("y"), Owl("members"), Named("C1")),
        (Named("z"), Type, Owl("AllDisjointProperties")), (Named("z"), Owl("members"), Named("P1")),
    ];

    /// <summary>The facts the lists' pairs match, and now and then one of the lists' own statements or another cell.</summary>
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> ListTriple = Gen.Frequency(
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Owl("sameAs")), Gen.OneOfConst(Individuals))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Type), Gen.OneOfConst(Classes))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst(Individuals))),
        (1, Gen.Select(Gen.OneOfConst(Classes), Gen.Const(IriOf(RdfsNs + "subClassOf")), Gen.OneOfConst(Classes))),
        (1, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(IriOf(RdfsNs + "subPropertyOf")), Gen.OneOfConst(Properties))),
        (2, Gen.OneOfConst(Lists)),
        (1, Gen.Select(Gen.OneOfConst([Named("I2"), Named("C2"), Named("P3")]), Gen.OneOfConst([First, Rest]), Gen.OneOfConst([.. Individuals, Nil]))));

    /// <summary>
    /// ADR 0195 point 6: what a closure keeps for its clashes is charged to its
    /// budget before it is held, a clash and an entry for each premise, given
    /// back when the clash goes, and all of it when the closure is released.
    /// </summary>
    [Fact]
    public void the_clashes_kept_are_charged_to_the_budget_and_given_back()
    {
        Terms terms = new();
        ClosureCosts costs = ClosureCosts.For(InferenceProfile.Owl2Rl);
        FactBudget budget = new(new MemoryAmount(1L << 30), costs);
        Closure closure = new(InferenceProfile.Owl2Rl, terms, budget);
        Fact[] typed = [.. Enumerable.Range(0, 10).SelectMany(i => new[] { terms.Fact(Named("x" + i), Type, Named("C")), terms.Fact(Named("x" + i), Type, Named("D")) })];
        closure.Apply([.. typed, terms.Fact(Named("C"), Owl("disjointWith"), Named("D"))], [], TestContext.Current.CancellationToken);
        long before = budget.Used.Value;
        Assert.Equal(10, closure.Clashes().Count);
        long clash = costs.PerClash.Value + (3 * costs.PerClashPremise.Value);
        Assert.Equal(10 * clash, budget.Used.Value - before);

        // One individual typed D no longer: its clash goes, and so does its charge, beside the fact's.
        long kept = budget.Used.Value;
        closure.Apply([], [typed[1]], TestContext.Current.CancellationToken);
        Assert.Equal(9, closure.Clashes().Count);
        Assert.True(budget.Used.Value <= kept - clash, $"{kept - budget.Used.Value} bytes given back where at least {clash} were the clash's");

        closure.Release();
        Assert.Equal(0, budget.Used.Value);
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
