// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Varve.Inference.Model;
using Varve.Rdf;
using Xunit;
using static Varve.Inference.Tests.Terms;

namespace Varve.Inference.Tests;

/// <summary>
/// ADR 0197 points 2, 3 and 5: each rule whose head is <c>false</c> fires on
/// the smallest graph that meets its body, through derived facts as well as
/// asserted ones, and not on the graph one fact short of it; a clash changes
/// nothing the closure holds.
/// </summary>
public class ClashTests
{
    private const string OwlNs = "http://www.w3.org/2002/07/owl#";

    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm First = IriOf(RdfNs + "first");
    private static readonly RdfTerm Rest = IriOf(RdfNs + "rest");
    private static readonly RdfTerm Nil = IriOf(RdfNs + "nil");

    private static RdfTerm Owl(string local) => IriOf(OwlNs + local);

    private static RdfTerm NonNegative(string value) => RdfTerm.Literal(Encoding.UTF8.GetBytes(value), IriOf(XsdNs + "nonNegativeInteger"));

    /// <summary>Each check, and a graph whose last triple completes its body.</summary>
    public static TheoryData<string, string> Cases() => new()
    {
        { "eq-diff1", "a sameAs b|a differentFrom b" },
        { "prp-irp", "p type IrreflexiveProperty|a p a" },
        { "prp-asyp", "p type AsymmetricProperty|a p b|b p a" },
        { "prp-pdw", "p propertyDisjointWith q|a p b|a q b" },
        { "prp-npa1", "n sourceIndividual a|n assertionProperty p|n targetIndividual b|a p b" },
        { "cls-nothing2", "C subClassOf Nothing|a type C" },
        { "cls-com", "C complementOf D|a type C|a type D" },
        { "cls-maxc1", "R maxCardinality 0|R onProperty p|a type R|a p b" },
        { "cls-maxqc1", "R maxQualifiedCardinality 0|R onProperty p|R onClass C|a type R|a p b|b type C" },
        { "cls-maxqc2", "R maxQualifiedCardinality 0|R onProperty p|R onClass Thing|a type R|a p b" },
        { "cax-dw", "C disjointWith D|E subClassOf D|a type C|a type E" },
        { "eq-diff2", "x type AllDifferent|x members L1|L1 first a|L1 rest L2|L2 first b|L2 rest nil|a sameAs b" },
        { "eq-diff3", "x type AllDifferent|x distinctMembers L1|L1 first a|L1 rest L2|L2 first b|L2 rest nil|b sameAs a" },
        { "prp-adp", "x type AllDisjointProperties|x members L1|L1 first p|L1 rest L2|L2 first q|L2 rest nil|a p b|a q b" },
        { "cax-adc", "x type AllDisjointClasses|x members L1|L1 first C|L1 rest L2|L2 first D|L2 rest nil|a type C|a type D" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void each_check_fires_on_its_body_and_not_one_triple_short(string check, string graph)
    {
        foreach (string strategy in new[] { "DeleteAndRederive", "BackwardForward", "RecursiveCounting" })
        {
            Terms terms = new();
            Closure closure = new(Program.Of(InferenceProfile.Owl2Rl, terms), Strategies.Named(strategy));
            Fact[] facts = [.. graph.Split('|').Select(t => Parse(terms, t))];

            closure.Apply(facts.AsSpan(0, facts.Length - 1), [], TestContext.Current.CancellationToken);
            Assert.DoesNotContain(closure.Clashes(), c => Name(c) == check);

            closure.Apply([facts[^1]], [], TestContext.Current.CancellationToken);
            Clash clash = Assert.Single(closure.Clashes(), c => Name(c) == check);
            Assert.All(clash.Premises, premise => Assert.True(closure.Contains(premise)));

            closure.Apply([], [facts[^1]], TestContext.Current.CancellationToken);
            Assert.DoesNotContain(closure.Clashes(), c => Name(c) == check);
        }
    }

    [Fact]
    public void a_language_tagged_literal_in_the_range_of_xsd_string_clashes_and_a_string_does_not()
    {
        foreach (InferenceProfile profile in new[] { InferenceProfile.Rdfs, InferenceProfile.Owl2Rl })
        {
            Terms terms = new();
            Closure closure = new(profile, terms);
            RdfTerm range = IriOf(RdfsNs + "range"), p = Named("p");
            closure.Apply([terms.Fact(p, range, IriOf(XsdNs + "string")), terms.Fact(Named("a"), p, RdfTerm.Literal("x"u8))], [], TestContext.Current.CancellationToken);
            Assert.Empty(closure.Clashes());

            Fact tagged = terms.Fact(Named("a"), p, RdfTerm.Literal("x"u8, "en"u8));
            closure.Apply([tagged], [], TestContext.Current.CancellationToken);
            Clash clash = Assert.Single(closure.Clashes());
            Assert.Equal(Check.RuleNamespace + "rdfs/dt-not-type", Encoding.UTF8.GetString(clash.Rule.Lexical));

            closure.Apply([], [tagged], TestContext.Current.CancellationToken);
            Assert.Empty(closure.Clashes());
        }
    }

    [Fact]
    public void a_literal_of_an_unrecognised_datatype_never_clashes()
    {
        Terms terms = new();
        Closure closure = new(InferenceProfile.Rdfs, terms);
        RdfTerm p = Named("p");
        closure.Apply(
        [
            terms.Fact(p, IriOf(RdfsNs + "range"), IriOf(XsdNs + "string")),
            terms.Fact(Named("a"), p, RdfTerm.Literal("1"u8, IriOf(XsdNs + "integer"))),
        ], [], TestContext.Current.CancellationToken);
        Assert.Empty(closure.Clashes());
    }

    [Fact]
    public void a_clash_changes_nothing_the_closure_holds()
    {
        Terms terms = new();
        Closure clashing = new(InferenceProfile.Owl2Rl, terms), plain = new(InferenceProfile.Owl2Rl, terms);
        Fact[] graph = [.. "C disjointWith D|a type C|a type D".Split('|').Select(t => Parse(terms, t))];
        clashing.Apply(graph, [], TestContext.Current.CancellationToken);
        plain.Apply(graph, [], TestContext.Current.CancellationToken);
        Assert.NotEmpty(clashing.Clashes());
        Assert.Equal(plain.All.ToHashSet(), clashing.All.ToHashSet());
        Assert.DoesNotContain(terms.Fact(Named("a"), Type, Named("Z")), clashing.All);
    }

    private static string Name(Clash clash)
    {
        string iri = Encoding.UTF8.GetString(clash.Rule.Lexical);
        return iri[(iri.LastIndexOf('/') + 1)..];
    }

    /// <summary>A triple in the cases' shorthand: OWL's and RDF's terms by their local names, <c>0</c> and <c>nil</c>, and the rest example names.</summary>
    private static Fact Parse(Terms terms, string text)
    {
        string[] parts = text.Split(' ');
        return terms.Fact(Term(parts[0]), Term(parts[1]), Term(parts[2]));
    }

    private static readonly HashSet<string> OwlLocals =
    [
        "sameAs", "differentFrom", "IrreflexiveProperty", "AsymmetricProperty", "propertyDisjointWith", "sourceIndividual",
        "assertionProperty", "targetIndividual", "Nothing", "Thing", "complementOf", "maxCardinality", "maxQualifiedCardinality",
        "onProperty", "onClass", "disjointWith", "AllDifferent", "members", "distinctMembers", "AllDisjointProperties",
        "AllDisjointClasses",
    ];

    private static RdfTerm Term(string local) => local switch
    {
        "type" => Type,
        "first" => First,
        "rest" => Rest,
        "nil" => Nil,
        "subClassOf" => IriOf(RdfsNs + "subClassOf"),
        "0" => NonNegative("0"),
        _ when OwlLocals.Contains(local) => Owl(local),
        _ => Named(local),
    };
}
