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

    /// <summary>A triple: mostly data and schema, now and then one about the vocabulary itself.</summary>
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> Triple = Gen.Frequency(
        (6, Gen.Select(Gen.OneOfConst(Individuals), Gen.OneOfConst(Properties), Gen.OneOfConst([.. Individuals, .. Literals]))),
        (4, Gen.Select(Gen.OneOfConst(Individuals), Gen.Const(Schema[0]), Gen.OneOfConst(Classes))),
        (4, Gen.Select(Gen.OneOfConst([.. Classes, .. Properties]), Gen.OneOfConst(Schema[1..]), Gen.OneOfConst([.. Classes, .. Properties]))),
        (1, Gen.Select(Gen.OneOfConst(Nodes), Gen.OneOfConst([.. Properties, .. Schema, Vocab[7]]), Gen.OneOfConst([.. Nodes, .. Literals]))));

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
    private static readonly Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> OwlTriple = Gen.Frequency(
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

    /// <summary>A history: each commit asserts some triples and retracts some explicit ones, chosen by index.</summary>
    private static Gen<(int Seed, (RdfTerm S, RdfTerm P, RdfTerm O)[][] Commits)> History(Gen<(RdfTerm S, RdfTerm P, RdfTerm O)> triple) =>
        Gen.Select(Gen.Int, triple.Array[1, 4].Array[Commits / 2, Commits]);

    [Fact]
    public void delete_and_rederive_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Rdfs, Retraction.DeleteAndRederive);

    [Fact]
    public void backward_forward_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Rdfs, Retraction.BackwardForward);

    [Fact]
    public void the_baseline_equals_the_oracle_at_every_commit() => Holds(InferenceProfile.Rdfs, Retraction.FromScratch);

    [Fact]
    public void under_owl_2_rl_delete_and_rederive_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Owl2Rl, Retraction.DeleteAndRederive);

    [Fact]
    public void under_owl_2_rl_backward_forward_equals_from_scratch_at_every_commit() => Holds(InferenceProfile.Owl2Rl, Retraction.BackwardForward);

    /// <summary>The naive oracle recomputes a whole closure per round, so its OWL 2 RL case runs fewer histories.</summary>
    private static readonly int NaiveHistories = Long ? 40 : 8;

    private static void Holds(InferenceProfile profile, Retraction strategy) =>
        History(profile == InferenceProfile.Rdfs ? Triple : OwlTriple)
            .Sample(
                history => Check(profile, strategy, history.Seed, history.Commits),
                iter: profile == InferenceProfile.Owl2Rl && strategy == Retraction.FromScratch ? NaiveHistories : Histories,
                threads: 1);

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

            closure.Apply([.. asserted], [.. retracted]);
            explicitFacts.ExceptWith(retracted);
            explicitFacts.UnionWith(asserted);

            // The deletion passes are held to a closure built from scratch by
            // insertion; the baseline, which is that build, to the naive oracle.
            HashSet<Fact> expected = strategy == Retraction.FromScratch
                ? Oracle.Closure(program, explicitFacts)
                : FromScratch(program, explicitFacts);
            HashSet<Fact> actual = [.. closure.All];
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
        }
    }
}
