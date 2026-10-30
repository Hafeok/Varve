// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Entailment.Model;
using Xunit;
using static Varve.Sparql.Entailment.Tests.Support;

namespace Varve.Sparql.Entailment.Tests;

/// <summary>ADR 0193's rewriting, case by case: the rules a reader checks by eye.</summary>
public class ExampleTests
{
    private static RdfTerm Type => IriOf(RdfNs + "type");

    private static RdfTerm SubClassOf => IriOf(RdfsNs + "subClassOf");

    private static RdfTerm SubPropertyOf => IriOf(RdfsNs + "subPropertyOf");

    private static RdfTerm Domain => IriOf(RdfsNs + "domain");

    private static RdfTerm Range => IriOf(RdfsNs + "range");

    // The ontology in the TBox graph, the instances in the default graph.
    private static InMemoryDataset Building()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Named("Room"), SubClassOf, Named("Space"), Tbox);
        builder.Add(Named("Space"), SubClassOf, Named("Thing"), Tbox);
        builder.Add(Named("hasRoom"), Domain, Named("Building"), Tbox);
        builder.Add(Named("hasRoom"), Range, Named("Room"), Tbox);
        builder.Add(Named("hasMainRoom"), SubPropertyOf, Named("hasRoom"), Tbox);
        builder.Add(Named("r1"), Type, Named("Room"));
        builder.Add(Named("b1"), Named("hasMainRoom"), Named("r2"));
        return builder.ToDataset();
    }

    [Fact]
    public void A_type_is_inherited_up_the_class_hierarchy()
    {
        List<string> rows = Run("SELECT ?x { ?x a :Thing }", Building(), EntailmentRegime.Rdfs, Tbox);
        Assert.Equal(["?x=<http://example.org/r1>", "?x=<http://example.org/r2>"], rows);
    }

    [Fact]
    public void A_domain_and_a_range_type_through_a_sub_property()
    {
        Assert.Equal(["?x=<http://example.org/b1>"], Run("SELECT ?x { ?x a :Building }", Building(), EntailmentRegime.Rdfs, Tbox));
        Assert.Equal(["?x=<http://example.org/b1> ?y=<http://example.org/r2>"], Run("SELECT ?x ?y { ?x :hasRoom ?y }", Building(), EntailmentRegime.Rdfs, Tbox));
    }

    [Fact]
    public void Simple_entailment_is_the_query_as_written()
    {
        Assert.Empty(Run("SELECT ?x { ?x a :Thing }", Building(), EntailmentRegime.Simple, Tbox));
    }

    [Fact]
    public void Without_the_TBox_only_the_graph_itself_entails()
    {
        Assert.Empty(Run("SELECT ?x { ?x a :Thing }", Building(), EntailmentRegime.Rdfs));
    }

    [Fact]
    public void A_class_is_its_own_sub_class_and_the_TBox_answers_meta_queries()
    {
        List<string> rows = Run("SELECT ?c { :Room rdfs:subClassOf ?c }", Building(), EntailmentRegime.Rdfs, Tbox);
        Assert.Equal(
        [
            "?c=<http://example.org/Room>", "?c=<http://example.org/Space>", "?c=<http://example.org/Thing>",
            "?c=<http://www.w3.org/2000/01/rdf-schema#Resource>",
        ], rows);
    }

    [Fact]
    public void Every_predicate_is_a_property_and_rdf_type_ranges_over_classes()
    {
        List<string> properties = Run("SELECT ?p { ?p a rdf:Property }", Building(), EntailmentRegime.Rdf, Tbox);
        Assert.Contains("?p=<http://example.org/hasMainRoom>", properties);
        Assert.Contains("?p=<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>", properties);
        Assert.Equal(["true"], Run("ASK { rdf:_7 a rdf:Property }", Building(), EntailmentRegime.Rdf, Tbox));
    }

    [Fact]
    public void A_solution_appears_once_per_blank_node_mapping()
    {
        // r1 is a Thing by one derivation and a Space by another; it is one solution.
        List<string> rows = Run("SELECT ?x { ?x a :Space . ?x a :Thing }", Building(), EntailmentRegime.Rdfs, Tbox);
        Assert.Equal(["?x=<http://example.org/r1>", "?x=<http://example.org/r2>"], rows);
    }
}
