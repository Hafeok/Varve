// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Varve.Rdf;
using Varve.Sparql.Entailment.Closure;
using Varve.Sparql.Entailment.Model;
using Xunit;
using static Varve.Sparql.Entailment.Tests.Support;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>
/// ADR 0193 point 2's equivalence: the schema built by probes equals the one
/// built by reading the scoping graph whole (<see cref="ScanBuiltSchema"/>),
/// on generated TBoxes and data graphs, both with schema triples, with
/// sub-properties of the schema predicates and chains through
/// <c>rdfs:subPropertyOf</c>, and with <c>rdf:_n</c> in every position.
/// </summary>
/// <remarks>
/// The schema compared is Σ's triples under the four schema predicates, less
/// what every term has — <c>t ⊑ t</c> and <c>t ⊑ rdfs:Resource</c> — and less
/// the axioms of an <c>rdf:_n</c> the probe-built Σ does not name, which are
/// the same for every <c>rdf:_n</c> and answered by the container-membership
/// families. The whole read also holds the data's own typing, which the
/// rewriting answers and the comparison leaves out.
/// </remarks>
public class ProbeBuiltSchemaTests
{
    private static readonly RdfTerm[] Classes = [Named("C1"), Named("C2")];
    private static readonly RdfTerm[] Properties = [Named("p1"), Named("p2"), Named("p3")];
    private static readonly RdfTerm[] Individuals = [Named("a"), Named("b"), RdfTerm.BlankNode("n1"u8)];
    private static readonly RdfTerm[] Literals = [RdfTerm.Literal("x"u8), RdfTerm.Literal("1"u8, IriOf("http://www.w3.org/2001/XMLSchema#integer"))];
    private static readonly RdfTerm[] SchemaPredicates = [IriOf(RdfsNs + "subClassOf"), IriOf(RdfsNs + "subPropertyOf"), IriOf(RdfsNs + "domain"), IriOf(RdfsNs + "range")];
    private static readonly RdfTerm[] Membership = [IriOf(RdfNs + "_1"), IriOf(RdfNs + "_2"), IriOf(RdfsNs + "member")];
    private static readonly RdfTerm[] MetaClasses = [IriOf(RdfsNs + "Datatype"), IriOf(RdfsNs + "ContainerMembershipProperty"), IriOf(RdfsNs + "Class"), IriOf(RdfNs + "Property"), IriOf(RdfsNs + "Literal")];
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");

    private static Gen<RdfTerm> Of(params RdfTerm[][] pools) => Gen.OneOfConst(pools.SelectMany(p => p).ToArray());

    // A schema triple, under a schema predicate or one of the user's properties
    // (which a chain may make a sub-property of one), about the user's terms
    // and rdf:_n, the vocabulary as object.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> SchemaTriple =
        Gen.Select(
            Of(Classes, Properties, Membership),
            Gen.Frequency((3, Of(SchemaPredicates)), (1, Of(Properties))),
            Gen.Frequency((3, Of(Classes, Properties)), (3, Of(MetaClasses)), (1, Of(Membership, SchemaPredicates, [Type]))));

    // A data triple: individuals, classes and properties under the user's
    // properties, rdf:type and rdf:_n, objects of every kind.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> DataTriple =
        Gen.Select(
            Of(Individuals, Classes, Properties),
            Gen.Frequency((2, Gen.Const(Type)), (2, Of(Properties)), (1, Of(Membership))),
            Gen.Frequency((3, Of(Classes)), (1, Of(Individuals)), (1, Of(Literals)), (1, Of(MetaClasses)), (1, Of(Membership)), (1, Of(Properties))));

    // Now and then, a triple that redefines the vocabulary, which the probe-built schema reads whole.
    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)> VocabularyTriple =
        Gen.Select(Of(SchemaPredicates, [Type], Membership), Of(SchemaPredicates), Of(Classes, Properties, MetaClasses, SchemaPredicates));

    private static readonly Gen<(RdfTerm, RdfTerm, RdfTerm)[]> Graph =
        Gen.Frequency((4, SchemaTriple), (5, DataTriple), (1, VocabularyTriple)).Array[0, 8];

    /// <remarks>
    /// Most cases are answered by the probes; the rest redefine the vocabulary
    /// and are read whole. The share read whole is held below a half, so the
    /// probes are what the property tests.
    /// </remarks>
    [Fact]
    public void The_probe_built_schema_equals_the_scan_built_one()
    {
        int whole = 0;
        int cases = 0;
        Gen.Select(Graph, Graph, Gen.Bool).Sample(
            (tbox, data, inNamedGraph) =>
            {
                cases++;
                whole += Same(tbox, data, inNamedGraph) ? 1 : 0;
            },
            iter: RewritingEqualsMaterialisationTests.Iterations);
        Assert.True(whole * 2 < cases, whole + " of " + cases + " cases were read whole");
    }

    /// <summary>A typing into a class the TBox makes a datatype: the probes read it, as rdfs13 needs.</summary>
    [Fact]
    public void A_typing_into_a_users_meta_class_is_read_into_the_schema() =>
        Assert.False(Same([(Named("C1"), SchemaPredicates[0], MetaClasses[0])], [(Named("a"), Type, Named("C1"))], false));

    // Whether the probe-built schema read the scoping graph whole.
    private static bool Same((RdfTerm, RdfTerm, RdfTerm)[] tbox, (RdfTerm, RdfTerm, RdfTerm)[] data, bool inNamedGraph)
    {
        InMemoryDatasetBuilder builder = new();
        RdfTerm data1 = Named("g1");
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in tbox)
        {
            builder.Add(s, p, o, Tbox);
        }

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in data)
        {
            if (inNamedGraph)
            {
                builder.Add(s, p, o, data1);
            }
            else
            {
                builder.Add(s, p, o);
            }
        }

        TBoxMerge merge = new(builder.ToDataset(), [new TBoxCover([Tbox], GraphScope.All)]);
        IReadOnlyList<GraphPattern> scope = inNamedGraph && merge.TryInternalise(data1, out TermHandle handle) ? [GraphPattern.Named(handle)] : [GraphPattern.DefaultGraph];
        RuleSet rules = EntailmentRewriter.RulesOf(EntailmentRegime.Rdfs);

        Schema probed = Schema.Saturate(merge, scope, rules, [], TestContext.Current.CancellationToken);
        TripleClosure scanned = ScanBuiltSchema.Saturate(merge, scope, rules);

        HashSet<RdfTerm> named = new(RdfTerm.Comparer);
        foreach (Triple t in probed.Closure.All)
        {
            named.UnionWith([t.S, t.P, t.O]);
        }

        SortedSet<string> expected = Projection(scanned.All, named);
        SortedSet<string> actual = Projection(probed.Closure.All, named);
        Assert.True(
            expected.SetEquals(actual),
            "TBox: " + Show(tbox) + "\ndata: " + Show(data) + "\nonly scanned:\n  " + string.Join("\n  ", expected.Except(actual)) + "\nonly probed:\n  " + string.Join("\n  ", actual.Except(expected)));
        return probed.ReadWhole;
    }

    private static SortedSet<string> Projection(IEnumerable<Triple> closure, HashSet<RdfTerm> named)
    {
        SortedSet<string> kept = new(StringComparer.Ordinal);
        foreach (Triple t in closure)
        {
            bool schema = Array.Exists(SchemaPredicates, p => p.Equals(t.P));
            bool trivial = (t.S.Equals(t.O) && (t.P.Equals(SchemaPredicates[0]) || t.P.Equals(SchemaPredicates[1])))
                || (t.P.Equals(SchemaPredicates[0]) && t.O.Equals(IriOf(RdfsNs + "Resource")));
            bool membership = Vocabulary.IsContainerMembership(t.S) && !named.Contains(t.S);
            if (schema && !trivial && !membership)
            {
                kept.Add(Text(t.S) + " " + Text(t.P) + " " + Text(t.O));
            }
        }

        return kept;
    }

    private static string Show((RdfTerm, RdfTerm, RdfTerm)[] triples) =>
        string.Join(" . ", triples.Select(t => Text(t.Item1) + " " + Text(t.Item2) + " " + Text(t.Item3)));
}
