// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using Varve.Inference.Model;
using Varve.Rdf;
using Xunit;
using static Varve.Inference.Tests.Terms;

namespace Varve.Inference.Tests;

/// <summary>Cases the generated histories reach rarely, pinned.</summary>
public class ExampleTests
{
    /// <summary>
    /// An axiom asserted and then retracted is still an axiom: the retraction
    /// takes away the assertion, never the axiom. Found by the property at
    /// nightly depth under OWL 2 RL, seed 1CsqyjfNbJH1: delete-and-rederive
    /// over-deleted it and found no rule to rederive it.
    /// </summary>
    [Theory]
    [InlineData(InferenceProfile.Rdfs, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Rdfs, "BackwardForward")]
    [InlineData(InferenceProfile.Owl2Rl, "DeleteAndRederive")]
    [InlineData(InferenceProfile.Owl2Rl, "BackwardForward")]
    [InlineData(InferenceProfile.Rdfs, "RecursiveCounting")]
    [InlineData(InferenceProfile.Owl2Rl, "RecursiveCounting")]
    public void an_axiom_asserted_then_retracted_stays(InferenceProfile profile, string strategy)
    {
        Terms terms = new();
        Closure closure = new(Program.Of(profile, terms), Strategies.Named(strategy));
        Fact axiom = terms.Fact(IriOf(RdfNs + "type"), IriOf(RdfsNs + "domain"), IriOf(RdfsNs + "Resource"));
        Assert.True(closure.Contains(axiom));

        closure.Apply([axiom], [], TestContext.Current.CancellationToken);
        closure.Apply([], [axiom], TestContext.Current.CancellationToken);

        Assert.True(closure.Contains(axiom));
        Assert.False(closure.IsExplicit(axiom));
    }

    /// <summary>A subclass chain, then its middle link retracted: what it alone supported goes, the rest stays.</summary>
    [Theory]
    [InlineData("DeleteAndRederive")]
    [InlineData("BackwardForward")]
    [InlineData("RecursiveCounting")]
    public void retracting_a_link_takes_what_it_alone_supported(string strategy)
    {
        Terms terms = new();
        Closure closure = new(Program.Of(InferenceProfile.Rdfs, terms), Strategies.Named(strategy));
        RdfTerm type = IriOf(RdfNs + "type"), subClassOf = IriOf(RdfsNs + "subClassOf");
        Fact ab = terms.Fact(Named("A"), subClassOf, Named("B"));
        Fact bc = terms.Fact(Named("B"), subClassOf, Named("C"));
        Fact x = terms.Fact(Named("x"), type, Named("A"));
        closure.Apply([ab, bc, x], [], TestContext.Current.CancellationToken);
        Assert.True(closure.Contains(terms.Fact(Named("x"), type, Named("C"))));

        closure.Apply([], [bc], TestContext.Current.CancellationToken);

        Assert.False(closure.Contains(terms.Fact(Named("x"), type, Named("C"))));
        Assert.False(closure.Contains(terms.Fact(Named("A"), subClassOf, Named("C"))));
        Assert.True(closure.Contains(terms.Fact(Named("x"), type, Named("B"))));
    }

    /// <summary>
    /// A budget is shared by the closures of a binding (ADR 0195 point 6):
    /// a closure that would pass it throws before it adds the fact, and a
    /// closure released gives its facts back.
    /// </summary>
    [Fact]
    public void a_closure_past_its_budget_throws_and_a_released_one_gives_its_facts_back()
    {
        Terms terms = new();
        ClosureCosts costs = ClosureCosts.For(InferenceProfile.Rdfs);
        FactBudget probe = new(new MemoryAmount(long.MaxValue), costs);
        Closure empty = new(InferenceProfile.Rdfs, terms, probe);
        long one = probe.Used.Value, building = probe.Peak.Value;
        empty.Release();
        Assert.Equal(0, probe.Used.Value);

        // Room for an empty closure, the build of another (its working sets given back once it settles), and half of
        // one more: two hundred facts will not fit.
        FactBudget budget = new(new MemoryAmount(one + building + (one / 2)), costs);
        Closure first = new(InferenceProfile.Rdfs, terms, budget);
        Closure second = new(InferenceProfile.Rdfs, terms, budget);
        Assert.Equal(2 * one, budget.Used.Value);

        RdfTerm subClassOf = IriOf(RdfsNs + "subClassOf"), type = IriOf(RdfNs + "type");
        Fact[] many = [terms.Fact(Named("A"), subClassOf, Named("B")), .. Enumerable.Range(0, 200).Select(i => terms.Fact(Named("x" + i), type, Named("A")))];
        ClosureLimitExceededException refused = Assert.Throws<ClosureLimitExceededException>(() => second.Apply(many, [], TestContext.Current.CancellationToken));
        Assert.True(budget.Used.Value <= budget.Limit.Value, "a refused request takes nothing");
        Assert.True(refused.Wanted.Value > budget.Limit.Value);
        Assert.True(budget.Peak.Value <= budget.Limit.Value);

        // Released, a closure gives back exactly what it took, working sets included.
        second.Release();
        Assert.Equal(one, budget.Used.Value);
        first.Apply([terms.Fact(Named("x"), type, Named("A"))], [], TestContext.Current.CancellationToken);
        Assert.True(budget.Used.Value <= budget.Limit.Value);
        first.Release();
        Assert.Equal(0, budget.Used.Value);
    }
}
