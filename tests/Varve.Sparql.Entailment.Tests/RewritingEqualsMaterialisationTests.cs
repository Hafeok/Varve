// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Rdf;
using Varve.Sparql.Entailment.Model;
using Xunit;
using static Varve.Sparql.Entailment.Tests.Support;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>
/// ADR 0193 point 9: over generated graphs, schemas inside the TBox and
/// inside data graphs, meta-modelling, container membership and literals,
/// and generated basic graph patterns with variables, constants and blank
/// nodes in every position, the rewritten query equals the oracle's closure
/// under the regime's answer conditions, as multisets.
/// </summary>
public class RewritingEqualsMaterialisationTests
{
    internal static readonly int Iterations = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is null ? 300 : 3_000;

    private static readonly RdfTerm[] Individuals = [Named("a"), Named("b"), Named("c"), RdfTerm.BlankNode("n1"u8)];
    private static readonly RdfTerm[] Constants = [Named("a"), Named("b"), Named("c")];
    private static readonly RdfTerm[] Literals = [RdfTerm.Literal("x"u8), RdfTerm.Literal("1"u8, IriOf("http://www.w3.org/2001/XMLSchema#integer")), RdfTerm.Literal("y"u8, "en"u8)];
    private static readonly RdfTerm[] Classes = [Named("C1"), Named("C2"), Named("C3")];
    private static readonly RdfTerm[] Properties = [Named("p1"), Named("p2"), Named("p3")];
    private static readonly RdfTerm[] Vocab =
    [
        IriOf(RdfNs + "type"), IriOf(RdfsNs + "subClassOf"), IriOf(RdfsNs + "subPropertyOf"), IriOf(RdfsNs + "domain"),
        IriOf(RdfsNs + "range"), IriOf(RdfsNs + "Resource"), IriOf(RdfsNs + "Class"), IriOf(RdfNs + "Property"),
        IriOf(RdfsNs + "member"), IriOf(RdfsNs + "ContainerMembershipProperty"), IriOf(RdfsNs + "Datatype"),
        IriOf(RdfsNs + "Literal"), IriOf(RdfNs + "_1"), IriOf(RdfNs + "_2"), IriOf("http://www.w3.org/2001/XMLSchema#string"),
    ];

    private static readonly RdfTerm G1 = Named("g1");

    private static readonly RdfTerm[] SchemaPredicates = [Vocab[1], Vocab[2], Vocab[3], Vocab[4], Vocab[0]];

    private static Gen<RdfTerm> Of(params RdfTerm[][] pools) => Gen.OneOfConst(pools.SelectMany(p => p).ToArray());

    // A schema triple: classes, properties and vocabulary related by the schema predicates.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> SchemaTriple =
        Gen.Select(Of(Classes, Properties, Vocab), Gen.OneOfConst(SchemaPredicates), Of(Classes, Properties, Vocab));

    // A data triple: individuals and classes as subjects, the properties, rdf:type and rdf:_n as predicates.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> DataTriple =
        Gen.Select(Of(Individuals, Classes), Of(Properties, [Vocab[0], Vocab[12], Vocab[8]]), Of(Individuals, Classes, Literals));

    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)[]> Graph =
        Gen.Frequency((2, SchemaTriple), (3, DataTriple)).Array[0, 7];

    private static readonly Gen<Slot> Position =
        Gen.Frequency(
            (5, Gen.OneOfConst("x", "y", "z").Select(v => new Slot(v, null, null))),
            (1, Gen.Const(new Slot(null, "q", null))),
            (4, Of(Constants, Classes, Properties, Vocab, Literals).Select(t => new Slot(null, null, t))));

    private static readonly Gen<Slot[][]> Bgp =
        Gen.Select(Position, Position, Position).Select(t => new[] { t.Item1, t.Item2, t.Item3 }).Array[1, 2]
            .Where(b => b.All(p => p[0].Term is not { Kind: RdfTermKind.Literal } && p[1].Term is not { Kind: RdfTermKind.Literal or RdfTermKind.BlankNode } && p[1].Blank is null));

    [Fact]
    public void Rewriting_equals_the_closure_under_each_regime_in_the_default_graph() =>
        Gen.Select(Graph, Graph, Bgp, Gen.OneOfConst(EntailmentRegime.Rdf, EntailmentRegime.Rdfs, EntailmentRegime.D))
            .Sample((tbox, data, bgp, regime) => Same(tbox, data, [], bgp, regime, Scope.Default), iter: Iterations);

    [Fact]
    public void Rewriting_equals_the_closure_in_a_named_graph_with_its_own_schema() =>
        Gen.Select(Graph, Graph, Bgp, Gen.OneOfConst(EntailmentRegime.Rdf, EntailmentRegime.Rdfs))
            .Sample((tbox, named, bgp, regime) => Same(tbox, [], named, bgp, regime, Scope.NamedConstant), iter: Iterations);

    [Fact]
    public void Rewriting_equals_the_closure_of_each_named_graph_under_a_graph_variable() =>
        Gen.Select(Graph, Graph, Graph, Bgp)
            .Sample((tbox, data, named, bgp) => Same(tbox, data, named, bgp, EntailmentRegime.Rdfs, Scope.NamedVariable), iter: Iterations);

    private enum Scope
    {
        Default,
        NamedConstant,
        NamedVariable,
    }

    private static void Same((RdfTerm, RdfTerm, RdfTerm)[] tbox, (RdfTerm, RdfTerm, RdfTerm)[] data, (RdfTerm, RdfTerm, RdfTerm)[] named, Slot[][] bgp, EntailmentRegime regime, Scope scope)
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in tbox)
        {
            builder.Add(s, p, o, Tbox);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in data)
        {
            builder.Add(s, p, o);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in named)
        {
            builder.Add(s, p, o, G1);
        }

        InMemoryDataset dataset = builder.ToDataset();
        string patterns = string.Join(" . ", bgp.Select(t => t[0].Text + " " + t[1].Text + " " + t[2].Text));
        string query = scope switch
        {
            Scope.Default => "SELECT * { " + patterns + " }",
            Scope.NamedConstant => "SELECT * { GRAPH <" + Ex + "g1> { " + patterns + " } }",
            _ => "SELECT * { GRAPH ?g { " + patterns + " } }",
        };

        List<string> expected = scope switch
        {
            Scope.Default => Oracle.Solutions(regime, [.. tbox, .. data], bgp),
            Scope.NamedConstant => named.Length == 0 ? [] : Oracle.Solutions(regime, [.. tbox, .. named], bgp),
            _ => ByGraph(regime, tbox, named, bgp),
        };

        List<string> actual = Run(query, dataset, regime, Tbox);
        Assert.True(
            expected.SequenceEqual(actual),
            "Under " + regime + ", " + query + "\nTBox: " + Show(tbox) + "\ndata: " + Show(data) + "\ng1: " + Show(named)
            + "\nmissing:\n  " + string.Join("\n  ", Minus(expected, actual)) + "\nextra:\n  " + string.Join("\n  ", Minus(actual, expected)));
    }

    // Each named graph the dataset holds, the TBox graph among them, merged with the TBox.
    private static List<string> ByGraph(EntailmentRegime regime, (RdfTerm, RdfTerm, RdfTerm)[] tbox, (RdfTerm, RdfTerm, RdfTerm)[] named, Slot[][] bgp)
    {
        List<string> rows = [];
        if (tbox.Length > 0)
        {
            rows.AddRange(Oracle.Solutions(regime, tbox, bgp, "g", Tbox));
        }

        if (named.Length > 0)
        {
            rows.AddRange(Oracle.Solutions(regime, [.. tbox, .. named], bgp, "g", G1));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static List<string> Minus(List<string> left, List<string> right)
    {
        List<string> rest = [.. left];
        foreach (string row in right)
        {
            rest.Remove(row);
        }

        return rest;
    }

    private static string Show((RdfTerm, RdfTerm, RdfTerm)[] triples) =>
        string.Join(" . ", triples.Select(t => Text(t.Item1) + " " + Text(t.Item2) + " " + Text(t.Item3)));
}
