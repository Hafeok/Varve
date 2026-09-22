// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// TriG is Turtle plus graph blocks, and its one hard case is that a statement
/// beginning with an IRI may be a triple or a named graph — which is not known
/// until the term after it (TriG §2.1).
/// </summary>
public class TriGReaderTests
{
    [Fact]
    public void a_bare_triple_is_in_the_default_graph()
    {
        Read read = ParseTriG("<http://a/s> <http://a/p> <http://a/o> .");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
        Assert.Null(read.Rows[0].Graph);
    }

    [Fact]
    public void a_named_graph_block_labels_its_triples()
    {
        Read read = ParseTriG("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o> <http://a/g>"], read.Lines());
    }

    [Fact]
    public void the_graph_keyword_is_accepted()
    {
        Read read = ParseTriG("GRAPH <http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }");

        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o> <http://a/g>"], read.Lines());
    }

    [Fact]
    public void a_wrapped_default_graph_has_no_label()
    {
        Read read = ParseTriG("{ <http://a/s> <http://a/p> <http://a/o> . }");

        Assert.True(read.Result.Succeeded);
        Assert.Null(read.Rows[0].Graph);
    }

    [Fact]
    public void a_graph_block_may_be_labelled_by_a_blank_node()
    {
        Read read = ParseTriG("_:g { <http://a/s> <http://a/p> <http://a/o> . }");

        Assert.True(read.Result.Succeeded);
        Assert.Equal(RdfTermKind.BlankNode, read.Rows[0].Graph!.Kind);
    }

    [Fact]
    public void a_graph_block_may_be_labelled_by_a_prefixed_name()
    {
        Read read = ParseTriG("@prefix p: <http://a/> .\np:g { p:s p:p p:o . }");

        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o> <http://a/g>"], read.Lines());
    }

    [Fact]
    public void a_graph_blocks_final_dot_is_optional()
    {
        Read read = ParseTriG("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> }");

        Assert.True(read.Result.Succeeded);
        Assert.Single(read.Rows);
    }

    [Fact]
    public void a_dot_may_not_follow_a_graph_block()
    {
        // [3g] triplesOrGraph puts the '.' after a predicateObjectList and not
        // after a wrappedGraph, which is what trig-graph-bad-02 tests.
        Read read = ParseTriG("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> } .");

        Assert.False(read.Result.Succeeded);
    }

    [Fact]
    public void an_empty_graph_block_yields_nothing_and_succeeds()
    {
        Read read = ParseTriG("<http://a/g> { }\n<http://a/s> <http://a/p> <http://a/o> .");

        Assert.True(read.Result.Succeeded);
        Assert.Single(read.Rows);
        Assert.Null(read.Rows[0].Graph);
    }

    [Fact]
    public void a_graph_block_holds_several_statements()
    {
        Read read = ParseTriG(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . <http://a/t> <http://a/q> <http://a/r> . }");

        Assert.Equal(2, read.Rows.Count);
        Assert.All(read.Rows, row => Assert.Equal(RdfTerm.Iri(Harness.U("http://a/g")), row.Graph));
    }

    [Fact]
    public void a_block_and_a_bare_triple_alternate()
    {
        Read read = ParseTriG(
            "<http://a/s> <http://a/p> <http://a/o> .\n"
            + "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }\n"
            + "<http://a/s> <http://a/q> <http://a/r> .");

        Assert.Equal(3, read.Rows.Count);
        Assert.Null(read.Rows[0].Graph);
        Assert.NotNull(read.Rows[1].Graph);
        Assert.Null(read.Rows[2].Graph);
    }

    [Fact]
    public void a_directive_between_blocks_applies_to_the_next_one()
    {
        Read read = ParseTriG(
            "@prefix p: <http://a/> .\np:g { p:s p:p p:o . }\n@prefix p: <http://b/> .\np:g { p:s p:p p:o . }");

        Assert.Equal(
            [
                "<http://a/s> <http://a/p> <http://a/o> <http://a/g>",
                "<http://b/s> <http://b/p> <http://b/o> <http://b/g>",
            ],
            read.Lines());
    }

    [Fact]
    public void a_blank_node_property_list_inside_a_block_is_labelled_too()
    {
        Read read = ParseTriG("<http://a/g> { <http://a/s> <http://a/p> [ <http://a/q> <http://a/r> ] . }");

        Assert.Equal(2, read.Rows.Count);
        Assert.All(read.Rows, row => Assert.Equal(RdfTerm.Iri(Harness.U("http://a/g")), row.Graph));
    }

    [Fact]
    public void a_collection_inside_a_block_is_labelled_too()
    {
        Read read = ParseTriG("<http://a/g> { <http://a/s> <http://a/p> ( <http://a/1> ) . }");

        Assert.Equal(3, read.Rows.Count);
        Assert.All(read.Rows, row => Assert.NotNull(row.Graph));
    }

    [Fact]
    public void a_blank_node_label_is_the_same_node_across_graphs()
    {
        Read read = ParseTriG(
            "<http://a/g> { _:x <http://a/p> <http://a/o> . }\n"
            + "<http://a/h> { _:x <http://a/p> <http://a/o> . }");

        Assert.Equal(read.Rows[0].Subject, read.Rows[1].Subject);
    }

    [Fact]
    public void a_graph_block_is_rejected_in_turtle()
    {
        Read read = Parse("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }");

        Assert.False(read.Result.Succeeded);
    }
}
