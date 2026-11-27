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

using Varve.Benchmarks.Candidates;

namespace Varve.Inference.Tests;

/// <summary>
/// ADR 0195 point 9: over generated histories of assertions and retractions,
/// schema changes and meta-modelling among them, the closure after every
/// commit equals the closure of the explicit facts from scratch, for each
/// retraction strategy.
/// </summary>
public class IncrementalEqualsFromScratchTests
{
    private static readonly bool Long = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is not null;
    private static readonly int Histories = Long ? 400 : 40;
    private static readonly int Commits = Long ? 200 : 60;

    // The vocabulary as data rebuilds a near-complete closure from scratch at every commit, so its histories are shorter.
    private static readonly int VocabularyCommits = Long ? 40 : 16;

    private static readonly RdfTerm[] Individuals = [Named("a"), Named("b"), Named("c")];
    private static readonly RdfTerm[] Classes = [Named("C1"), Named("C2"), Named("C3")];
    private static readonly RdfTerm[] Properties = [Named("p1"), Named("p2"), Named("p3")];
    private static readonly RdfTerm[] Schema =
    [
        IriOf(RdfNs + "type"), IriOf(RdfsNs + "subClassOf"), IriOf(RdfsNs + "subPropertyOf"), IriOf(RdfsNs + "domain"), IriOf(RdfsNs + "range"),
    ];
    private static readonly RdfTerm[] Vocab =
    [
        IriOf(RdfsNs + "Resource"), IriOf(RdfsNs + "Class"), IriOf(RdfNs + "Property"), IriOf(RdfsNs + "member"),
        IriOf(RdfsNs + "ContainerMembershipProperty"), IriOf(RdfsNs + "Datatype"), IriOf(RdfsNs + "Literal"), IriOf(RdfNs + "_1"),
        IriOf(XsdNs + "string"),
    ];
    private static readonly RdfTerm[] Literals =
    [
        RdfTerm.Literal("x"u8), RdfTerm.Literal("y"u8, "en"u8), RdfTerm.Literal("1"u8, IriOf(XsdNs + "integer")),
    ];

    private static readonly RdfTerm[] Nodes = [.. Individuals, .. Classes, .. Properties, .. Schema, .. Vocab];

    /// <summary>
    /// A triple of data and schema over the graph's own terms: no axiom has
    /// an RDF, RDFS or OWL term as its subject or object, so the vocabulary
    /// is never data and no closure grows dense through it (#399). The
    /// vocabulary as data is <see cref="VocabularyTriple"/>'s, over few terms.
    /// </summary>
    internal static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> Triple = Gen.Frequency(
        (6, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst([.. Individuals, .. Literals]))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Schema[0]), Gen.OneOfConst(Classes))),
        (4, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(Schema[1..]), Gen.OneOfConst([.. Classes, .. Properties]))));

    // The few terms the vocabulary as data is drawn over: one individual, one class and one property beside the vocabulary.
    private static readonly RdfTerm[] OneIndividual = [Named("a")];
    private static readonly RdfTerm[] OneClass = [Named("C1")];
    private static readonly RdfTerm[] OneProperty = [Named("p1")];
    private static readonly RdfTerm[] FewVocabulary = [IriOf(RdfsNs + "Resource"), IriOf(RdfsNs + "Class"), IriOf(RdfNs + "Property"), IriOf(RdfsNs + "member"), IriOf(RdfNs + "_1")];
    private static readonly RdfTerm[] FewNodes = [.. OneIndividual, .. OneClass, .. OneProperty, .. Schema, .. FewVocabulary];

    /// <summary>
    /// A triple with the vocabulary as data: RDF and RDFS terms as subjects
    /// and objects of schema axioms (meta-modelling), over a universe of
    /// terms small enough that a complete closure finishes in seconds.
    /// </summary>
    internal static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> VocabularyTriple = Gen.Frequency(
        (2, Gen.Select(Gen.OneOfConst(OneIndividual), Gen.OneOfConst(OneProperty), Gen.OneOfConst([.. OneIndividual, .. Literals]))),
        (2, Gen.Select(Gen.OneOfConst(OneIndividual), Gen.Const(Schema[0]), Gen.OneOfConst(OneClass))),
        (2, Gen.Select(Gen.OneOfConst([.. OneClass, .. OneProperty]), Gen.OneOfConst(Schema[1..]), Gen.OneOfConst([.. OneClass, .. OneProperty]))),
        (4, Gen.Select(Gen.OneOfConst(FewNodes), Gen.OneOfConst([.. OneProperty, .. Schema, FewVocabulary[4]]), Gen.OneOfConst([.. FewNodes, Literals[0]]))));

    private const string OwlNs = "http://www.w3.org/2002/07/owl#";

    private static readonly RdfTerm[] Restrictions = [Named("R1"), Named("R2")];
    private static readonly RdfTerm[] Cells = [Named("L1"), Named("L2"), Named("L3")];
    private static readonly RdfTerm Nil = IriOf(RdfNs + "nil");
    private static readonly RdfTerm[] OwlTypes =
    [
        IriOf(OwlNs + "TransitiveProperty"), IriOf(OwlNs + "SymmetricProperty"), IriOf(OwlNs + "FunctionalProperty"),
        IriOf(OwlNs + "InverseFunctionalProperty"), IriOf(OwlNs + "ObjectProperty"), IriOf(OwlNs + "Class"),
    ];
    private static readonly RdfTerm[] OwlPairs =
    [
        IriOf(OwlNs + "equivalentClass"), IriOf(OwlNs + "equivalentProperty"), IriOf(OwlNs + "inverseOf"),
    ];
    private static readonly RdfTerm[] ListAxioms =
    [
        IriOf(OwlNs + "intersectionOf"), IriOf(OwlNs + "unionOf"), IriOf(OwlNs + "oneOf"),
        IriOf(OwlNs + "propertyChainAxiom"), IriOf(OwlNs + "hasKey"),
    ];

    /// <summary>OWL's axioms beside RDFS's: property characteristics, restrictions, and lists, well formed or not.</summary>
    internal static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> OwlTriple = Gen.Frequency(
        (10, Triple),
        (2, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Schema[0]), Gen.OneOfConst(OwlTypes))),
        (2, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(OwlPairs), Gen.OneOfConst([.. Classes, .. Properties]))),
        (2, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "onProperty")), Gen.OneOfConst(Properties))),
        (2, Gen.Select(Gen.OneOfConst(Restrictions), Gen.OneOfConst([IriOf(OwlNs + "someValuesFrom"), IriOf(OwlNs + "allValuesFrom")]), Gen.OneOfConst([.. Classes, IriOf(OwlNs + "Thing")]))),
        (1, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "hasValue")), Gen.OneOfConst(Individuals))),
        (1, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "maxCardinality")), Gen.Const(RdfTerm.Literal("1"u8, IriOf(XsdNs + "nonNegativeInteger"))))),
        (2, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Schema[0]), Gen.OneOfConst(Restrictions))),
        (3, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "first")), Gen.OneOfConst([.. Classes, .. Properties, .. Individuals]))),
        (3, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "rest")), Gen.OneOfConst([.. Cells, Nil, Nil]))),
        (2, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(ListAxioms), Gen.OneOfConst(Cells))));

    private static readonly RdfTerm[] OneRestriction = [Named("R1")];
    private static readonly RdfTerm[] OneCell = [Named("L1")];
    private static readonly RdfTerm[] OwlVocabulary = [OwlTypes[0], OwlTypes[1], OwlPairs[0], OwlPairs[2], IriOf(OwlNs + "Thing")];

    /// <summary>
    /// OWL's axioms with OWL's vocabulary as data, over few terms: OWL terms
    /// as the subjects and objects of axioms, over one individual, class,
    /// property, restriction and list cell. RDF and RDFS terms are not drawn
    /// as data here: under OWL 2 RL they join the profile's whole axiomatic
    /// vocabulary into one complete closure of 48 terms, 110,592 facts, which
    /// takes tens of seconds from scratch (#408).
    /// </summary>
    internal static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> VocabularyOwlTriple = Gen.Frequency(
        (2, Gen.Select(Gen.OneOfConst(OneIndividual), Gen.OneOfConst(OneProperty), Gen.OneOfConst([.. OneIndividual, .. Literals]))),
        (2, Gen.Select(Gen.OneOfConst(OneIndividual), Gen.Const(Schema[0]), Gen.OneOfConst(OneClass))),
        (2, Gen.Select(Gen.OneOfConst([.. OneClass, .. OneProperty]), Gen.OneOfConst(Schema[1..]), Gen.OneOfConst([.. OneClass, .. OneProperty]))),
        (2, Gen.Select(Gen.OneOfConst(OneProperty), Gen.Const(Schema[0]), Gen.OneOfConst(OwlTypes))),
        (2, Gen.Select(Gen.OneOfConst([.. OneClass, .. OneProperty]), Gen.OneOfConst(OwlPairs), Gen.OneOfConst([.. OneClass, .. OneProperty]))),
        (1, Gen.Select(Gen.OneOfConst(OneRestriction), Gen.Const(IriOf(OwlNs + "onProperty")), Gen.OneOfConst(OneProperty))),
        (1, Gen.Select(Gen.OneOfConst(OneRestriction), Gen.OneOfConst([IriOf(OwlNs + "someValuesFrom"), IriOf(OwlNs + "allValuesFrom")]), Gen.OneOfConst([.. OneClass, IriOf(OwlNs + "Thing")]))),
        (1, Gen.Select(Gen.OneOfConst(OneIndividual), Gen.Const(Schema[0]), Gen.OneOfConst(OneRestriction))),
        (2, Gen.Select(Gen.OneOfConst(OneCell), Gen.Const(IriOf(RdfNs + "first")), Gen.OneOfConst([.. OneClass, .. OneProperty, .. OneIndividual]))),
        (2, Gen.Select(Gen.OneOfConst(OneCell), Gen.Const(IriOf(RdfNs + "rest")), Gen.OneOfConst([.. OneCell, Nil, Nil]))),
        (1, Gen.Select(Gen.OneOfConst([.. OneClass, .. OneProperty]), Gen.OneOfConst(ListAxioms), Gen.OneOfConst(OneCell))),
        (4, Gen.Select(Gen.OneOfConst([.. OneClass, .. OneProperty, .. OwlVocabulary]), Gen.OneOfConst([.. Schema[1..], .. OwlPairs]), Gen.OneOfConst([.. OneClass, .. OneProperty, .. OwlVocabulary]))));

    /// <summary>The generators of <c>main</c> at 693c328a, which drew #399's seeds: a fixed seed replays only the generator it was drawn by.</summary>
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> TripleOf399 = Gen.Frequency(
        (6, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst([.. Individuals, .. Literals]))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Schema[0]), Gen.OneOfConst(Classes))),
        (4, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(Schema[1..]), Gen.OneOfConst([.. Classes, .. Properties]))),
        (1, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst([.. Properties, .. Schema, Vocab[7]]), Gen.OneOfConst([.. Nodes, .. Literals]))));

    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> OwlTripleOf399 = Gen.Frequency(
        (10, TripleOf399),
        (2, Gen.Select(Gen.OneOfConst(Properties), Gen.Const(Schema[0]), Gen.OneOfConst(OwlTypes))),
        (2, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(OwlPairs), Gen.OneOfConst([.. Classes, .. Properties]))),
        (2, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "onProperty")), Gen.OneOfConst(Properties))),
        (2, Gen.Select(Gen.OneOfConst(Restrictions), Gen.OneOfConst([IriOf(OwlNs + "someValuesFrom"), IriOf(OwlNs + "allValuesFrom")]), Gen.OneOfConst([.. Classes, IriOf(OwlNs + "Thing")]))),
        (1, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "hasValue")), Gen.OneOfConst(Individuals))),
        (1, Gen.Select(Gen.OneOfConst(Restrictions), Gen.Const(IriOf(OwlNs + "maxCardinality")), Gen.Const(RdfTerm.Literal("1"u8, IriOf(XsdNs + "nonNegativeInteger"))))),
        (2, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Schema[0]), Gen.OneOfConst(Restrictions))),
        (3, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "first")), Gen.OneOfConst([.. Classes, .. Properties, .. Individuals]))),
        (3, Gen.Select(Gen.OneOfConst(Cells), Gen.Const(IriOf(RdfNs + "rest")), Gen.OneOfConst([.. Cells, Nil, Nil]))),
        (2, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(ListAxioms), Gen.OneOfConst(Cells))));

    /// <summary>A history: each commit asserts some triples and retracts some explicit ones, chosen by index.</summary>
    private static Gen<(int Seed, (RdfTerm S, RdfTerm P, RdfTerm O)[][] Commits)> History(Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple, int commits) =>
        Gen.Select(Gen.Int, triple.Array[1, 4].Array[commits / 2, commits]);

    private static Gen<(int Seed, (RdfTerm S, RdfTerm P, RdfTerm O)[][] Commits)> History(Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple) => History(triple, Commits);

    [Fact]
    public void delete_and_rederive_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Rdfs, Retraction.DeleteAndRederive);

    [Fact]
    public void backward_forward_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Rdfs, BackwardForward.Instance);

    [Fact]
    public void the_baseline_equals_the_oracle_at_every_commit() => Holds(InferenceProfile.Rdfs, Retraction.FromScratch);

    [Fact]
    public void under_owl_2_rl_delete_and_rederive_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Owl2Rl, Retraction.DeleteAndRederive);

    [Fact]
    public void under_owl_2_rl_backward_forward_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Owl2Rl, BackwardForward.Instance);

    [Fact]
    public void recursive_counting_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Rdfs, RecursiveCounting.Instance);

    [Fact]
    public void under_owl_2_rl_recursive_counting_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Owl2Rl, RecursiveCounting.Instance);

    /// <summary>
    /// ADR 0195 point 3: a data graph's closure over the TBox closure beneath
    /// it, maintained through a history of data commits, equals the closure
    /// of the TBox's and the data's triples from scratch after every commit,
    /// for each retraction strategy; the TBox closure is never changed.
    /// </summary>
    [Theory]
    [InlineData(InferenceProfile.Rdfs, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Rdfs, "BackwardForward")]
    [InlineData(InferenceProfile.Rdfs, "RecursiveCounting")]
    [InlineData(InferenceProfile.Owl2Rl, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Owl2Rl, "RecursiveCounting")]
    public void a_closure_over_a_tbox_beneath_equals_from_scratch_at_every_commit(InferenceProfile profile, string strategyName) =>
        Beneath(profile, strategyName, profile == InferenceProfile.Rdfs ? Triple : OwlTriple, seed: null);

    /// <summary>
    /// #399: the seed of the property above, drawn by <c>main</c>'s generator
    /// at 693c328a, whose history made the closure's nodes a dense graph of
    /// inferred <c>rdf:first</c> and <c>rdf:rest</c>, on which the list
    /// rules, reading every path, ran out of memory there. Read from the
    /// asserted statements (ADR 0197, amended 2026-11-24), the history holds
    /// at every commit. #399's other seed, <c>eGI5Cz8BVtI1</c>, reads no list
    /// after the fix, and its closure's work is #408's.
    /// </summary>
    [Theory]
    [InlineData("akRsD4X_QWj2", "DeleteAndRederive")]
    [InlineData("akRsD4X_QWj2", "RecursiveCounting")]
    public void The_seed_of_399_holds_over_a_tbox_beneath(string seed, string strategyName) =>
        Beneath(InferenceProfile.Owl2Rl, strategyName, OwlTripleOf399, seed);

    private static void Beneath(InferenceProfile profile, string strategyName, Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple, string? seed)
    {
        Retraction strategy = Strategies.Named(strategyName);
        Gen.Select(triple.Array[0, 8], History(triple)).Sample(sample =>
        {
            Terms terms = new();
            Program program = Program.Of(profile, terms);
            Fact[] tbox = [.. sample.Item1.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()];
            Closure beneath = new(program, Retraction.DeleteAndRederive);
            beneath.Apply(tbox, []);
            HashSet<Fact> frozen = [.. beneath.All];
            Closure closure = new(program, strategy, budget: null, tbox: beneath);
            HashSet<Fact> data = [];
            Random random = new(sample.Item2.Seed);
            foreach ((RdfTerm S, RdfTerm P, RdfTerm O)[] commit in sample.Item2.Commits)
            {
                List<Fact> asserted = [], retracted = [];
                foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in commit)
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
                // From scratch as the projection builds it: the data's closure over the TBox's, each from scratch.
                HashSet<Fact> expected = FromScratchOver(program, tbox, data);
                HashSet<Fact> actual = [.. closure.All];

                // A data graph that asserts nothing differing about a TBox list's cell (ADR 0197, amended 2026-11-24) is also the closure of the TBox's and its triples together.
                if (!closure.UnreadLists().Any(u => u.Reason == UnreadListReason.DiffersFromTBox))
                {
                    Assert.True(FromScratch(program, [.. tbox, .. data]).SetEquals(expected), $"{profile}: the layered closure differs from the closure of the union");
                }

                Assert.True(expected.SetEquals(actual), $"{profile}, {strategy}: missing {expected.Except(actual).Count()}, extra {actual.Except(expected).Count()}");
                Assert.True(frozen.SetEquals(beneath.All), "the TBox closure beneath changed");
            }
        }, seed: seed, iter: seed is not null ? 1 : Long ? 100 : 15, threads: 1);
    }

    /// <summary>The naive oracle recomputes a whole closure per round, so its OWL 2 RL case runs fewer histories.</summary>
    private static readonly int NaiveHistories = Long ? 40 : 8;

    /// <summary>
    /// ADR 0195 point 9 with the vocabulary as data (#399): RDF, RDFS and OWL
    /// terms as the subjects and objects of axioms, which can make every
    /// term relate to every other, over one individual, one class, one
    /// property, one restriction and one list cell beside the vocabulary, so
    /// a closure that grows complete still finishes in seconds.
    /// </summary>
    [Theory]
    [InlineData(InferenceProfile.Rdfs, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Owl2Rl, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Owl2Rl, "RecursiveCounting")]
    public void with_the_vocabulary_as_data_incremental_equals_from_scratch_at_every_commit(InferenceProfile profile, string strategyName) =>
        Holds(profile, Strategies.Named(strategyName), profile == InferenceProfile.Rdfs ? VocabularyTriple : VocabularyOwlTriple, VocabularyCommits);

    private static void Holds(InferenceProfile profile, Retraction strategy) =>
        Holds(profile, strategy, profile == InferenceProfile.Rdfs ? Triple : OwlTriple, Commits);

    private static void Holds(InferenceProfile profile, Retraction strategy, Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple, int commits) =>
        History(triple, commits)
            .Sample(
                history => Check(profile, strategy, history.Seed, history.Commits),
                iter: profile == InferenceProfile.Owl2Rl && strategy == Retraction.FromScratch ? NaiveHistories : Histories,
                threads: 1);

    private static HashSet<Fact> FromScratchOver(Program program, Fact[] tbox, HashSet<Fact> data)
    {
        Closure beneath = new(program, Retraction.FromScratch);
        beneath.Apply(tbox, []);
        Closure fresh = new(program, Retraction.FromScratch, budget: null, tbox: beneath);
        fresh.Apply([.. data], []);
        return [.. fresh.All];
    }

    private static HashSet<Fact> FromScratch(Program program, HashSet<Fact> explicitFacts)
    {
        Closure fresh = new(program, Retraction.FromScratch);
        fresh.Apply([.. explicitFacts], []);
        return [.. fresh.All];
    }

    [Fact]
    public void under_owl_2_rl_the_baseline_equals_the_oracle_at_every_commit() => Holds(InferenceProfile.Owl2Rl, Retraction.FromScratch);

    private static void Check(InferenceProfile profile, Retraction strategy, int seed, (RdfTerm S, RdfTerm P, RdfTerm O)[][] commits)
    {
        Terms terms = new();
        Program program = Program.Of(profile, terms);
        Closure closure = new(program, strategy);
        HashSet<Fact> explicitFacts = [];
        Random random = new(seed);
        for (int c = 0; c < commits.Length; c++)
        {
            // Half the facts the commit names are retracted when explicit, so
            // that a history retracts as much as it asserts.
            List<Fact> asserted = [], retracted = [];
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in commits[c])
            {
                Fact fact = terms.Fact(s, p, o);
                if (explicitFacts.Contains(fact) || (random.Next(2) == 0 && explicitFacts.Count > 0))
                {
                    Fact gone = explicitFacts.Contains(fact) ? fact : explicitFacts.ElementAt(random.Next(explicitFacts.Count));
                    if (!retracted.Contains(gone) && !asserted.Contains(gone))
                    {
                        retracted.Add(gone);
                    }
                }
                else if (!asserted.Contains(fact) && !retracted.Contains(fact))
                {
                    asserted.Add(fact);
                }
            }

            HashSet<Fact> previous = [.. closure.All];
            IReadOnlyCollection<Fact> changed = closure.Apply([.. asserted], [.. retracted]);
            explicitFacts.ExceptWith(retracted);
            explicitFacts.UnionWith(asserted);

            // The deletion passes are held to a closure built from scratch by
            // insertion; the baseline, which is that build, to the naive oracle.
            HashSet<Fact> expected = strategy == Retraction.FromScratch
                ? Oracle.Closure(program, explicitFacts)
                : FromScratch(program, explicitFacts);
            HashSet<Fact> actual = [.. closure.All];
            HashSet<Fact> difference = [.. previous];
            difference.SymmetricExceptWith(actual);
            Assert.True(difference.SetEquals(changed), $"after commit {c + 1}, Apply answered {changed.Count} changed facts where {difference.Count} changed");
            if (!expected.SetEquals(actual))
            {
                string missing = string.Join("\n  ", expected.Except(actual).Select(terms.Text).Order(StringComparer.Ordinal));
                string extra = string.Join("\n  ", actual.Except(expected).Select(terms.Text).Order(StringComparer.Ordinal));
                Assert.Fail($"after commit {c + 1} of {commits.Length}, {profile}, {strategy}:\nmissing:\n  {missing}\nextra:\n  {extra}");
            }

            foreach (Fact fact in explicitFacts)
            {
                Assert.True(closure.IsExplicit(fact));
            }

            if (strategy == RecursiveCounting.Instance)
            {
                // Each fact's round is the earliest any instance gives it, and
                // its count the instances that give it that round.
                foreach (Fact fact in actual)
                {
                    (int round, int count) = RecursiveCounting.Instance.Earliest(closure, fact);
                    (int held, int counted) = RecursiveCounting.Instance.Standing(closure, fact);
                    bool given = closure.IsExplicit(fact) || program.Axioms.Contains(fact);
                    Assert.True(
                        given ? held == 0 : held == round && counted == count,
                        $"after commit {c + 1}, {terms.Text(fact)}: round {held} count {counted}, earliest {round} by {count}");
                }
            }
        }
    }
}
