// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
    public void an_axiom_asserted_then_retracted_stays(InferenceProfile profile, string strategy)
    {
        Terms terms = new();
        Closure closure = new(Program.Of(profile, terms), Enum.Parse<Retraction>(strategy));
        Fact axiom = terms.Fact(IriOf(RdfNs + "type"), IriOf(RdfsNs + "domain"), IriOf(RdfsNs + "Resource"));
        Assert.True(closure.Contains(axiom));

        closure.Apply([axiom], []);
        closure.Apply([], [axiom]);

        Assert.True(closure.Contains(axiom));
        Assert.False(closure.IsExplicit(axiom));
    }

    /// <summary>A subclass chain, then its middle link retracted: what it alone supported goes, the rest stays.</summary>
    [Theory]
    [InlineData("DeleteAndRederive")]
    [InlineData("BackwardForward")]
    public void retracting_a_link_takes_what_it_alone_supported(string strategy)
    {
        Terms terms = new();
        Closure closure = new(Program.Of(InferenceProfile.Rdfs, terms), Enum.Parse<Retraction>(strategy));
        RdfTerm type = IriOf(RdfNs + "type"), subClassOf = IriOf(RdfsNs + "subClassOf");
        Fact ab = terms.Fact(Named("A"), subClassOf, Named("B"));
        Fact bc = terms.Fact(Named("B"), subClassOf, Named("C"));
        Fact x = terms.Fact(Named("x"), type, Named("A"));
        closure.Apply([ab, bc, x], []);
        Assert.True(closure.Contains(terms.Fact(Named("x"), type, Named("C"))));

        closure.Apply([], [bc]);

        Assert.False(closure.Contains(terms.Fact(Named("x"), type, Named("C"))));
        Assert.False(closure.Contains(terms.Fact(Named("A"), subClassOf, Named("C"))));
        Assert.True(closure.Contains(terms.Fact(Named("x"), type, Named("B"))));
    }
}
