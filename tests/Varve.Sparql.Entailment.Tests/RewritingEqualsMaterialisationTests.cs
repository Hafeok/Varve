// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
    private static readonly RdfTerm[] Literals =
    [
        RdfTerm.Literal("x"u8), RdfTerm.Literal("1"u8, IriOf("http://www.w3.org/2001/XMLSchema#integer")), RdfTerm.Literal("y"u8, "en"u8),
        RdfTerm.Literal("01"u8, IriOf("http://www.w3.org/2001/XMLSchema#integer")), RdfTerm.Literal("1"u8, IriOf("http://www.w3.org/2001/XMLSchema#int")),
        RdfTerm.Literal("1.0"u8, IriOf("http://www.w3.org/2001/XMLSchema#decimal")),
    ];
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

    // A schema triple about the user's terms only, the vocabulary in object
    // position alone: the schema the probes read without the whole-graph fallback.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> PlainSchemaTriple =
        Gen.Select(Of(Classes, Properties), Gen.OneOfConst(SchemaPredicates), Of(Classes, Properties, Vocab));

    // Two graphs in three keep the vocabulary unredefined, so that most cases
    // are answered by the probes and the tables (ADR 0193 point 2); the third
    // may redefine it, and is read whole.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)[]> Graph =
        Gen.Frequency(
            (1, Gen.Frequency((2, SchemaTriple), (3, DataTriple)).Array[0, 7]),
            (2, Gen.Frequency((2, PlainSchemaTriple), (3, DataTriple)).Array[0, 7]));

    // What pruning decides on: rdf:_n and literals as constants, in data and
    // in the query, from pools small enough that they meet.
    private static readonly RdfTerm[] Guarded = [Vocab[12], Vocab[13], Vocab[8], Vocab[9], Vocab[0], Classes[0], Constants[0], Literals[0], Literals[4]];

    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)[]> GuardedGraph =
        Gen.Frequency(
            (3, Gen.Select(Of([Constants[0], Classes[0], Vocab[12]]), Of([Properties[0], Vocab[0], Vocab[12], Vocab[13], Vocab[8]]), Of([Constants[0], Classes[0], Vocab[12], Literals[0], Literals[4]]))),
            (2, Gen.Select(Of([Properties[0], Vocab[12], Classes[0]]), Gen.OneOfConst(SchemaPredicates), Of([Classes[0], Vocab[8], Vocab[9], Vocab[11], IriOf(RdfsNs + "Resource")]))))
        .Array[0, 6];

    private static readonly Gen<Slot> GuardedPosition =
        Gen.Frequency(
            (3, Gen.OneOfConst("x", "y").Select(v => new Slot(v, null, null))),
            (1, Gen.Const(new Slot(null, "q", null))),
            (4, Gen.OneOfConst(Guarded).Select(t => new Slot(null, null, t))));

    private static readonly Gen<Slot[][]> GuardedBgp =
        Gen.Select(GuardedPosition, GuardedPosition, GuardedPosition).Select(t => new[] { t.Item1, t.Item2, t.Item3 }).Array[1, 2]
            .Where(b => b.All(p => p[0].Term is not { Kind: RdfTermKind.Literal } && p[1].Term is not { Kind: RdfTermKind.Literal or RdfTermKind.BlankNode } && p[1].Blank is null));

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

    /// <summary>
    /// ADR 0193 point 3's pruning: a branch decided false on its constants,
    /// or the same as one before it once a one-row table is folded in, is
    /// dropped, and no answer changes. The rewriting with every branch kept
    /// is the oracle, in the default graph, a named graph and under
    /// <c>GRAPH ?g</c>, under the three regimes; the share of cases whose
    /// rewriting pruning changed is held above a quarter, so the property
    /// tests the pruning.
    /// </summary>
    [Fact]
    public void Pruning_changes_no_answer()
    {
        int cases = 0;
        int pruned = 0;
        Gen.Select(Gen.Frequency((1, Graph), (2, GuardedGraph)), Gen.Frequency((1, Graph), (2, GuardedGraph)), Gen.Frequency((1, Bgp), (2, GuardedBgp)), Gen.OneOfConst(EntailmentRegime.Rdf, EntailmentRegime.Rdfs, EntailmentRegime.D), Gen.OneOfConst(Scope.Default, Scope.NamedConstant, Scope.NamedVariable))
            .Sample(
                (tbox, data, bgp, regime, scope) =>
                {
                    InMemoryDatasetBuilder builder = new();
                    foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in tbox)
                    {
                        builder.Add(s, p, o, Tbox);
                    }

                    foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in data)
                    {
                        if (scope == Scope.Default)
                        {
                            builder.Add(s, p, o);
                        }
                        else
                        {
                            builder.Add(s, p, o, G1);
                        }
                    }

                    InMemoryDataset dataset = builder.ToDataset();
                    string patterns = string.Join(" . ", bgp.Select(t => t[0].Text + " " + t[1].Text + " " + t[2].Text));
                    string text = scope switch
                    {
                        Scope.Default => "SELECT * { " + patterns + " }",
                        Scope.NamedConstant => "SELECT * { GRAPH <" + Ex + "g1> { " + patterns + " } }",
                        _ => "SELECT * { GRAPH ?g { " + patterns + " } }",
                    };

                    TBoxCover[] covers = [new TBoxCover([Tbox], GraphScope.All)];
                    CancellationToken ct = TestContext.Current.CancellationToken;
                    EntailedQuery on = EntailmentRewriter.Rewrite(Parse(text), dataset, covers, regime, Pruning.On, cancellationToken: ct);
                    EntailedQuery off = EntailmentRewriter.Rewrite(Parse(text), dataset, covers, regime, Pruning.Off, cancellationToken: ct);
                    List<string> expected = Run(off);
                    List<string> actual = Run(on);
                    cases++;
                    pruned += on.Query.Equals(off.Query) ? 0 : 1;
                    Assert.True(
                        expected.SequenceEqual(actual),
                        "Under " + regime + ", " + text + "\nTBox: " + Show(tbox) + "\ndata: " + Show(data)
                        + "\nmissing:\n  " + string.Join("\n  ", Minus(expected, actual)) + "\nextra:\n  " + string.Join("\n  ", Minus(actual, expected)));
                },
                iter: Iterations);
        Assert.True(pruned * 4 > cases, "pruning changed " + pruned + " of " + cases + " rewritings");
    }

    /// <summary>
    /// Two bindings and a graph neither covers (ADR 0193 point 1, ADR 0194
    /// point 2): each graph is read merged with its own binding's TBox, the
    /// default graph among them, a TBox graph and the uncovered graph over
    /// their own triples.
    /// </summary>
    [Fact]
    public void Rewriting_merges_each_graph_with_the_TBox_of_the_binding_that_covers_it() =>
        Gen.Select(Gen.Select(Graph, Graph), Gen.Select(Graph, Graph, Graph, Graph), Bgp, Gen.Bool)
            .Sample((tboxes, graphs, bgp, byGraph) => SameUnderTwo(tboxes, graphs, bgp, byGraph), iter: Iterations);

    /// <summary>
    /// ADR 0190 point 8, tier 1's half: for a caller whose readable set is
    /// generated (none, the default graph alone, some named graphs), the
    /// rewritten query read through the caller's scope, the TBox
    /// dataset-public, answers exactly the union over each readable graph of
    /// its closure with the TBox, and no term that only an unreadable graph
    /// holds reaches the answer.
    /// </summary>
    [Fact]
    public void A_caller_sees_exactly_the_entailments_of_the_graphs_it_reads() =>
        Gen.Select(Gen.Select(Graph, Graph), Gen.Select(Graph, Graph, Graph), Gen.Int[0, 15], Bgp)
            .Sample((graphs, named, readable, bgp) => SameForCaller(graphs, named, readable, bgp), iter: Iterations);

    private static void SameForCaller(
        ((RdfTerm, RdfTerm, RdfTerm)[] Tbox, (RdfTerm, RdfTerm, RdfTerm)[] Data) graphs,
        ((RdfTerm, RdfTerm, RdfTerm)[] G1, (RdfTerm, RdfTerm, RdfTerm)[] G2, (RdfTerm, RdfTerm, RdfTerm)[] G3) named,
        int readable,
        Slot[][] bgp)
    {
        // Bit 0 the default graph, bits 1 to 3 the named graphs g1 to g3.
        (RdfTerm Name, (RdfTerm, RdfTerm, RdfTerm)[] Triples)[] data = [(G1, named.G1), (G2, named.G2), (G3, named.G3)];
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in graphs.Tbox)
        {
            builder.Add(s, p, o, Tbox);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in graphs.Data)
        {
            builder.Add(s, p, o);
        }

        foreach ((RdfTerm name, (RdfTerm, RdfTerm, RdfTerm)[] triples) in data)
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
            {
                builder.Add(s, p, o, name);
            }
        }

        List<RdfTerm> listed = [];
        for (int i = 0; i < data.Length; i++)
        {
            if ((readable & (2 << i)) != 0)
            {
                listed.Add(data[i].Name);
            }
        }

        bool readsDefault = (readable & 1) != 0;
        GraphScope scope = GraphScope.Of([.. listed], [], readsDefault ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded).WithDatasetPublic([Tbox]);
        InMemoryDataset dataset = builder.ToDataset();
        GraphScopedQuadSource source = new(dataset, scope);
        TBoxCover[] covers = [new([Tbox], GraphScope.All)];
        string patterns = string.Join(" . ", bgp.Select(t => t[0].Text + " " + t[1].Text + " " + t[2].Text));

        List<string> expected = [];
        if (!scope.IsEmpty)
        {
            if (graphs.Tbox.Length > 0)
            {
                expected.AddRange(Oracle.Solutions(EntailmentRegime.Rdfs, graphs.Tbox, bgp, "g", Tbox));
            }

            foreach ((RdfTerm name, (RdfTerm, RdfTerm, RdfTerm)[] triples) in data)
            {
                if (listed.Contains(name) && triples.Length > 0)
                {
                    expected.AddRange(Oracle.Solutions(EntailmentRegime.Rdfs, [.. graphs.Tbox, .. triples], bgp, "g", name));
                }
            }
        }

        expected.Sort(StringComparer.Ordinal);
        string query = "SELECT * { GRAPH ?g { " + patterns + " } }";
        List<string> actual = Run(query, source, EntailmentRegime.Rdfs, covers);

        // A named graph the caller does not read is no graph of its dataset, under any regime.
        foreach ((RdfTerm name, _) in data.Where(d => !listed.Contains(d.Name)))
        {
            Assert.Empty(Run("SELECT * { GRAPH <" + Encoding.UTF8.GetString(name.Lexical) + "> { " + patterns + " } }", source, EntailmentRegime.Rdfs, covers));
        }
        string shown = query + "\nreadable " + readable + "\nTBox: " + Show(graphs.Tbox) + "\ng1: " + Show(named.G1) + "\ng2: " + Show(named.G2) + "\ng3: " + Show(named.G3)
            + "\nmissing:\n  " + string.Join("\n  ", Minus(expected, actual)) + "\nextra:\n  " + string.Join("\n  ", Minus(actual, expected));
        Assert.True(expected.SequenceEqual(actual), shown);

        // The default graph is always a graph of the dataset: read empty when the caller does not read it, merged with the TBox the caller reads.
        List<string> byDefault = Oracle.Solutions(EntailmentRegime.Rdfs, [.. scope.IsEmpty ? [] : graphs.Tbox, .. readsDefault ? graphs.Data : []], bgp);
        List<string> answeredByDefault = Run("SELECT * { " + patterns + " }", source, EntailmentRegime.Rdfs, covers);
        Assert.True(byDefault.SequenceEqual(answeredByDefault), "the default graph, " + shown);

        // No more: a term only an unreadable graph holds is in no answer.
        HashSet<string> readableTerms = [];
        foreach ((RdfTerm, RdfTerm, RdfTerm)[] triples in (IEnumerable<(RdfTerm, RdfTerm, RdfTerm)[]>)[graphs.Tbox, .. data.Where(d => listed.Contains(d.Name)).Select(d => d.Triples), readsDefault ? graphs.Data : []])
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
            {
                readableTerms.UnionWith([Text(s), Text(p), Text(o)]);
            }
        }

        foreach ((RdfTerm, RdfTerm, RdfTerm)[] hidden in (IEnumerable<(RdfTerm, RdfTerm, RdfTerm)[]>)[.. data.Where(d => !listed.Contains(d.Name)).Select(d => d.Triples), readsDefault ? [] : graphs.Data])
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in hidden)
            {
                foreach (RdfTerm term in (RdfTerm[])[s, p, o])
                {
                    string text = Text(term);
                    if (!readableTerms.Contains(text) && !patterns.Contains(text, StringComparison.Ordinal) && term.Kind == RdfTermKind.Iri && !text.Contains("www.w3.org", StringComparison.Ordinal))
                    {
                        Assert.True(!actual.Exists(row => row.Contains("=" + text, StringComparison.Ordinal)) && !answeredByDefault.Exists(row => row.Contains("=" + text, StringComparison.Ordinal)), text + " reached the answer: " + shown);
                    }
                }
            }
        }
    }

    private static readonly RdfTerm Tbox2 = Named("tbox2");
    private static readonly RdfTerm G2 = Named("g2");
    private static readonly RdfTerm G3 = Named("g3");

    private static void SameUnderTwo(
        ((RdfTerm, RdfTerm, RdfTerm)[] One, (RdfTerm, RdfTerm, RdfTerm)[] Two) tboxes,
        ((RdfTerm, RdfTerm, RdfTerm)[] Data, (RdfTerm, RdfTerm, RdfTerm)[] G1, (RdfTerm, RdfTerm, RdfTerm)[] G2, (RdfTerm, RdfTerm, RdfTerm)[] G3) graphs,
        Slot[][] bgp,
        bool byGraph)
    {
        (RdfTerm Name, (RdfTerm, RdfTerm, RdfTerm)[] Triples, (RdfTerm, RdfTerm, RdfTerm)[] Merged)[] named =
        [
            (Tbox, tboxes.One, []), (Tbox2, tboxes.Two, []), (G1, graphs.G1, tboxes.One), (G2, graphs.G2, tboxes.Two), (G3, graphs.G3, []),
        ];
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in graphs.Data)
        {
            builder.Add(s, p, o);
        }

        foreach ((RdfTerm name, (RdfTerm, RdfTerm, RdfTerm)[] triples, _) in named)
        {
            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
            {
                builder.Add(s, p, o, name);
            }
        }

        // The first binding covers g1; the second g2 and the default graph.
        TBoxCover[] covers =
        [
            new([Tbox], GraphScope.Of([G1], [], DefaultGraphAccess.Excluded)),
            new([Tbox2], GraphScope.Of([G2], [], DefaultGraphAccess.Included)),
        ];
        string patterns = string.Join(" . ", bgp.Select(t => t[0].Text + " " + t[1].Text + " " + t[2].Text));
        string query = byGraph ? "SELECT * { GRAPH ?g { " + patterns + " } }" : "SELECT * { " + patterns + " }";
        List<string> expected = [];
        if (byGraph)
        {
            foreach ((RdfTerm name, (RdfTerm, RdfTerm, RdfTerm)[] triples, (RdfTerm, RdfTerm, RdfTerm)[] merged) in named)
            {
                if (triples.Length > 0)
                {
                    expected.AddRange(Oracle.Solutions(EntailmentRegime.Rdfs, [.. merged, .. triples], bgp, "g", name));
                }
            }

            expected.Sort(StringComparer.Ordinal);
        }
        else
        {
            expected = Oracle.Solutions(EntailmentRegime.Rdfs, [.. tboxes.Two, .. graphs.Data], bgp);
        }

        List<string> actual = Run(query, builder.ToDataset(), EntailmentRegime.Rdfs, covers);
        Assert.True(
            expected.SequenceEqual(actual),
            query + "\nTBox 1: " + Show(tboxes.One) + "\nTBox 2: " + Show(tboxes.Two) + "\ndata: " + Show(graphs.Data)
            + "\ng1: " + Show(graphs.G1) + "\ng2: " + Show(graphs.G2) + "\ng3: " + Show(graphs.G3)
            + "\nmissing:\n  " + string.Join("\n  ", Minus(expected, actual)) + "\nextra:\n  " + string.Join("\n  ", Minus(actual, expected)));
    }

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

        // Pruning changes no answer here either: every branch kept, the same rows.
        List<string> unpruned = Run(EntailmentRewriter.Rewrite(Parse(query), dataset, [new TBoxCover([Tbox], GraphScope.All)], regime, Pruning.Off, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(unpruned.SequenceEqual(actual), "Pruning changed the answers under " + regime + ", " + query + "\nTBox: " + Show(tbox) + "\ndata: " + Show(data) + "\ng1: " + Show(named));
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
