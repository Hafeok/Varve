// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Inference.Model;
using Xunit;

namespace Varve.Inference.Tests;

/// <summary>
/// ADR 0199 point 2: every fact of a closure has a well-founded proof. Over
/// generated graphs, under both profiles, each implicit fact's proof is
/// finite, uses no fact on its own path, has explicit facts and axioms for
/// leaves, and each of its nodes is an instance of its rule over its
/// premises; an explicit fact is a leaf, and a fact not held has none.
/// </summary>
public class ExplainTests
{
    [Theory]
    [InlineData(InferenceProfile.Rdfs)]
    [InlineData(InferenceProfile.Owl2Rl)]
    public void every_implicit_fact_has_a_well_founded_proof(InferenceProfile profile) =>
        (profile == InferenceProfile.Rdfs ? IncrementalEqualsFromScratchTests.Triple : IncrementalEqualsFromScratchTests.OwlTriple).Array[1, 25]
            .Sample(triples =>
            {
                Terms terms = new();
                Closure closure = new(Program.Of(profile, terms), Retraction.DeleteAndRederive);
                Fact[] facts = [.. triples.Select(t => terms.Fact(t.S, t.P, t.O)).Distinct()];
                closure.Apply(facts, []);
                // About forty facts a closure, spread over all of them: each is its own search.
                Fact[] all = [.. closure.All];
                for (int i = 0; i < all.Length; i += 1 + (all.Length / 40))
                {
                    Fact fact = all[i];
                    Proof proof = Assert.IsType<Proof>(closure.Explain(fact));
                    Assert.Equal(fact, proof.Fact);
                    Check(closure, proof, []);
                }

                Assert.Null(closure.Explain(terms.Fact(Terms.Named("nowhere"), Terms.Named("nowhere"), Terms.Named("nowhere"))));
            }, iter: 60, threads: 1);

    private static void Check(Closure closure, Proof proof, HashSet<Fact> path)
    {
        Assert.True(path.Add(proof.Fact), "a proof uses its own fact on its path");
        Assert.True(closure.Contains(proof.Fact));
        switch (proof.Status)
        {
            case ProofStatus.Explicit:
                Assert.True(closure.IsExplicit(proof.Fact));
                Assert.Empty(proof.Premises);
                break;
            case ProofStatus.Axiom:
                Assert.Contains(proof.Fact, closure.Program.Axioms);
                Assert.Empty(proof.Premises);
                break;
            default:
                Assert.False(closure.IsExplicit(proof.Fact));
                Rule rule = Assert.Single(closure.Program.Rules, r => r.Iri.Equals(proof.Rule));
                HashSet<Fact> premises = [.. proof.Premises.Select(p => p.Fact)];
                Assert.True(rule.Proves(proof.Fact, closure.Facts, premises.Contains, premises: null), "the rule does not conclude the fact from its proof's premises");
                foreach (Proof premise in proof.Premises)
                {
                    Check(closure, premise, path);
                }

                break;
        }

        path.Remove(proof.Fact);
    }
}
