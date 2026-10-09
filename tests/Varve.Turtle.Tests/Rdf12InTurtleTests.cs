// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Turtle.Model;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// RDF 1.2 syntax is accepted by the Turtle and TriG reader (ADR 0110).
/// </summary>
/// <remarks>
/// <para>
/// The rdf12 suites are the gate; these are the named cases that state what
/// each construct means in the terms of RDF 1.2 Turtle §7.3, so that a suite
/// failure can be read against a sentence rather than against a file. Until
/// this milestone the same file held the closed door: every construct here was
/// asserted <em>rejected</em>, so that RDF 1.2 Turtle would arrive by wiring
/// its suites and not by discovering the feature half-worked. The door is open
/// and the suites are wired.
/// </para>
/// </remarks>
public class Rdf12InTurtleTests
{
    private const string Reifies = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies>";

    [Theory]
    [InlineData("<http://a/s> <http://a/p> \"x\"@en--ltr .", "<http://a/s> <http://a/p> \"x\"@en--ltr")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@ar--rtl .", "<http://a/s> <http://a/p> \"x\"@ar--rtl")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@en-GB--ltr .", "<http://a/s> <http://a/p> \"x\"@en-GB--ltr")]
    public void a_base_direction_is_read(string document, string expected)
    {
        Read read = Parse(document);

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal([expected], read.Lines());
    }

    [Theory]
    // RDF 1.2 Concepts §3.3: the direction is "ltr" or "rtl", lowercase.
    [InlineData("<http://a/s> <http://a/p> \"x\"@en--LTR .")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@en--xyz .")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@en-- .")]
    public void a_direction_that_is_not_ltr_or_rtl_is_rejected(string document)
    {
        Read read = Parse(document);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(ParseErrorKind.InvalidBaseDirection, read.Result.FirstError.Kind);
    }

    [Fact]
    public void a_triple_term_is_an_object()
    {
        Read read = Parse("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <http://a/c> )>> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            ["<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <http://a/c> )>>"],
            read.Lines());
    }

    [Fact]
    public void a_triple_term_nests_in_its_object()
    {
        Read read = Parse("<http://a/s> <http://a/p> <<(<http://a/a> <http://a/b> <<(<http://a/c> <http://a/d> \"e\")>>)>> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            ["<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <<( <http://a/c> <http://a/d> \"e\" )>> )>>"],
            read.Lines());
    }

    [Theory]
    // [15] subject and [9] verb admit no triple term; [33] ttSubject admits no
    // literal and no triple term; [34] ttObject admits no reified triple.
    [InlineData("<<( <http://a/a> <http://a/b> <http://a/c> )>> <http://a/p> <http://a/o> .")]
    [InlineData("<http://a/s> <<( <http://a/a> <http://a/b> <http://a/c> )>> <http://a/o> .")]
    [InlineData("<http://a/s> <http://a/p> <<( \"a\" <http://a/b> <http://a/c> )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> \"b\" <http://a/c> )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <<( <http://a/a> <http://a/b> <http://a/c> )>> <http://a/b> <http://a/c> )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> << <http://a/c> <http://a/d> <http://a/e> >> )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> ( <http://a/c> ) )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> [ <http://a/c> <http://a/d> ] )>> .")]
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <http://a/c> .")]
    public void a_triple_term_out_of_place_is_rejected(string document)
    {
        Assert.False(Parse(document).Result.Succeeded, "Turtle accepted it");
        Assert.False(ParseTriG(document).Result.Succeeded, "TriG accepted it");
    }

    [Fact]
    public void a_reified_triple_as_subject_asserts_only_its_reification()
    {
        // §7.3.2: the node is the reifier, fresh when none is named, and the
        // triple inside the brackets is not asserted.
        Read read = Parse("<< <http://a/s> <http://a/p> <http://a/o> >> <http://a/q> <http://a/z> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "_:g0 <http://a/q> <http://a/z>",
            ],
            read.Lines());
    }

    [Fact]
    public void a_reified_triple_may_name_its_reifier()
    {
        Read read = Parse("<< <http://a/s> <http://a/p> <http://a/o> ~ <http://a/r> >> <http://a/q> <http://a/z> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "<http://a/r> " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "<http://a/r> <http://a/q> <http://a/z>",
            ],
            read.Lines());
    }

    [Fact]
    public void a_reified_triple_stands_alone_as_a_statement()
    {
        // [11] triples ::= reifiedTriple predicateObjectList?
        Read read = Parse("<< <http://a/s> <http://a/p> <http://a/o> ~ _:r >> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(["_:r " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>"], read.Lines());
    }

    [Fact]
    public void a_reified_triple_as_object_is_its_reifier()
    {
        Read read = Parse("<http://a/x> <http://a/y> << <http://a/s> <http://a/p> \"o\" >> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> \"o\" )>>",
                "<http://a/x> <http://a/y> _:g0",
            ],
            read.Lines());
    }

    [Fact]
    public void reified_triples_nest_in_both_positions()
    {
        Read read = Parse("<< << <http://a/a> <http://a/b> <http://a/c> >> <http://a/p> << <http://a/d> <http://a/e> <http://a/f> >> >> <http://a/q> <http://a/z> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "_:g0 " + Reifies + " <<( <http://a/a> <http://a/b> <http://a/c> )>>",
                "_:g1 " + Reifies + " <<( <http://a/d> <http://a/e> <http://a/f> )>>",
                "_:g2 " + Reifies + " <<( _:g0 <http://a/p> _:g1 )>>",
                "_:g2 <http://a/q> <http://a/z>",
            ],
            read.Lines());
    }

    [Theory]
    // [30] rtSubject and [31] rtObject: no literal subject, no collection, no
    // property list, no blank node as verb; and the brackets must close.
    [InlineData("<< \"s\" <http://a/p> <http://a/o> >> <http://a/q> <http://a/z> .")]
    [InlineData("<< <http://a/s> _:p <http://a/o> >> <http://a/q> <http://a/z> .")]
    [InlineData("<< <http://a/s> <http://a/p> ( <http://a/o> ) >> <http://a/q> <http://a/z> .")]
    [InlineData("<< [ <http://a/p> <http://a/o> ] <http://a/p> <http://a/o> >> <http://a/q> <http://a/z> .")]
    [InlineData("<< <http://a/s> <http://a/p> >> <http://a/q> <http://a/z> .")]
    [InlineData("<< <http://a/s> <http://a/p> <http://a/o> <http://a/x> >> <http://a/q> <http://a/z> .")]
    [InlineData("<http://a/s> << <http://a/a> <http://a/b> <http://a/c> >> <http://a/o> .")]
    public void a_malformed_reified_triple_is_rejected(string document)
    {
        Assert.False(Parse(document).Result.Succeeded, "Turtle accepted it");
        Assert.False(ParseTriG(document).Result.Succeeded, "TriG accepted it");
    }

    [Fact]
    public void an_annotation_block_asserts_the_triple_and_describes_a_fresh_reifier()
    {
        // §7.3.3, §7.3.4.
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> {| <http://a/r> <http://a/z> |} .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "<http://a/s> <http://a/p> <http://a/o>",
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "_:g0 <http://a/r> <http://a/z>",
            ],
            read.Lines());
    }

    [Fact]
    public void a_reifier_names_the_block_that_follows_it()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> ~ <http://a/i> {| <http://a/r> <http://a/z> |} .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "<http://a/s> <http://a/p> <http://a/o>",
                "<http://a/i> " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "<http://a/i> <http://a/r> <http://a/z>",
            ],
            read.Lines());
    }

    [Fact]
    public void each_block_takes_the_reifier_before_it_or_mints_one()
    {
        // ":s :p :o ~ :r1 {| :a :b |} {| :c :d |} ~ ~ :r2" — the second block
        // has no reifier before it (the first block cleared it) and mints one;
        // a bare "~" mints one too; and ":r2" reifies without a block.
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o> ~ <http://a/r1> {| <http://a/a> <http://a/b> |} {| <http://a/c> <http://a/d> |} ~ ~ <http://a/r2> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "<http://a/s> <http://a/p> <http://a/o>",
                "<http://a/r1> " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "<http://a/r1> <http://a/a> <http://a/b>",
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "_:g0 <http://a/c> <http://a/d>",
                "_:g1 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
                "<http://a/r2> " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>>",
            ],
            read.Lines());
    }

    [Fact]
    public void an_annotation_applies_to_each_object_of_a_list()
    {
        Read read = Parse("<http://a/s> <http://a/p> <http://a/o1> ~ <http://a/r1> , <http://a/o2> {| <http://a/q> <http://a/z> |} ; <http://a/p2> <http://a/o3> .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "<http://a/s> <http://a/p> <http://a/o1>",
                "<http://a/r1> " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o1> )>>",
                "<http://a/s> <http://a/p> <http://a/o2>",
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o2> )>>",
                "_:g0 <http://a/q> <http://a/z>",
                "<http://a/s> <http://a/p2> <http://a/o3>",
            ],
            read.Lines());
    }

    [Theory]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> {| |} .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> {| <http://a/q> <http://a/z> .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> {| <http://a/q> <http://a/z> } .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> ~ \"r\" .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> ~ ( <http://a/r> ) .")]
    [InlineData("<http://a/s> {| <http://a/q> <http://a/z> |} <http://a/o> .")]
    public void a_malformed_annotation_is_rejected(string document)
    {
        Assert.False(Parse(document).Result.Succeeded, "Turtle accepted it");
        Assert.False(ParseTriG(document).Result.Succeeded, "TriG accepted it");
    }

    [Theory]
    [InlineData("VERSION \"1.2\"\n<http://a/s> <http://a/p> <http://a/o> .", "1.2")]
    [InlineData("version '1.2'\n<http://a/s> <http://a/p> <http://a/o> .", "1.2")]
    [InlineData("@version \"1.2\" .\n<http://a/s> <http://a/p> <http://a/o> .", "1.2")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> .\nVERSION \"1.1\"\n", "1.1")]
    [InlineData("VERSION \"2.7\"\n<http://a/s> <http://a/p> <http://a/o> .", "2.7")]
    public void the_version_directive_is_reported_and_refuses_nothing(string document, string version)
    {
        List<string> versions = [];
        TurtleOptions options = new() { OnVersion = v => versions.Add(Encoding.UTF8.GetString(v)) };
        List<Harness.Row> rows = [];

        ParseResult result = TurtleParser.Parse(Harness.U(document), rows.Collect(), in options);

        Assert.True(result.Succeeded, result.FirstError.ToString());
        Assert.Equal([version], versions);
        Assert.Single(rows);
    }

    [Theory]
    // [10] VersionSpecifier is a short string, never a long one or a bare token.
    [InlineData("VERSION 1.2\n<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("VERSION \"\"\"1.2\"\"\"\n<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("VERSION '''1.2'''\n<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("@version \"1.2\"\n<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("@version 1.2 .\n<http://a/s> <http://a/p> <http://a/o> .")]
    public void a_malformed_version_directive_is_rejected(string document)
    {
        Assert.False(Parse(document).Result.Succeeded, "Turtle accepted it");
        Assert.False(ParseTriG(document).Result.Succeeded, "TriG accepted it");
    }

    [Fact]
    public void a_prefixed_name_starting_with_version_is_not_the_keyword()
    {
        Read read = Parse("@prefix VERSION: <http://a/> .\nVERSION:s VERSION:p VERSION:o .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(["<http://a/s> <http://a/p> <http://a/o>"], read.Lines());
    }

    [Fact]
    public void trig_reads_the_same_constructs_inside_a_graph()
    {
        Read read = ParseTriG("<http://a/g> { << <http://a/s> <http://a/p> <http://a/o> >> <http://a/q> <http://a/z> {| <http://a/r> \"x\"@en--rtl |} }");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(
            [
                "_:g0 " + Reifies + " <<( <http://a/s> <http://a/p> <http://a/o> )>> <http://a/g>",
                "_:g0 <http://a/q> <http://a/z> <http://a/g>",
                "_:g1 " + Reifies + " <<( _:g0 <http://a/q> <http://a/z> )>> <http://a/g>",
                "_:g1 <http://a/r> \"x\"@en--rtl <http://a/g>",
            ],
            read.Lines());
    }

    [Fact]
    public void a_reified_triple_is_not_a_graph_label()
    {
        // TriG [7] labelOrSubject ::= iri | BlankNode.
        Assert.False(ParseTriG("<< <http://a/s> <http://a/p> <http://a/o> >> { <http://a/a> <http://a/b> <http://a/c> }").Result.Succeeded);
    }

    [Fact]
    public void the_writer_writes_a_base_direction_and_declares_the_version_first()
    {
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;

        using (TurtleWriter writer = new(output, in options))
        {
            NQuadsParser.Parse(
                Harness.U("<http://a/s> <http://a/p> \"plain\" .\n<http://a/s> <http://a/p> \"x\"@en--ltr .\n<http://a/s> <http://a/q> <<( <http://a/a> <http://a/b> <http://a/c> )>> .\n"),
                (in QuadView quad) => writer.Write(in quad),
                default);
        }

        Assert.Equal(
            "<http://a/s> <http://a/p> \"plain\" .\n"
            + "VERSION \"1.2\"\n"
            + "<http://a/s> <http://a/p> \"x\"@en--ltr .\n"
            + "<http://a/s> <http://a/q> <<( <http://a/a> <http://a/b> <http://a/c> )>> .\n",
            Encoding.UTF8.GetString(output.Written));
    }

    [Fact]
    public void the_writer_declares_no_version_for_a_document_rdf_11_can_read()
    {
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;

        using (TurtleWriter writer = new(output, in options))
        {
            NQuadsParser.Parse(
                Harness.U("<http://a/s> <http://a/p> \"x\"@en .\n"),
                (in QuadView quad) => writer.Write(in quad),
                default);
        }

        Assert.Equal("<http://a/s> <http://a/p> \"x\"@en .\n", Encoding.UTF8.GetString(output.Written));
    }

    [Fact]
    public void the_version_can_be_forced_to_the_top()
    {
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = new() { AlwaysDeclareVersion = true };

        using (TurtleWriter writer = new(output, in options))
        {
            writer.DeclarePrefix(Harness.U("a"), Harness.U("http://a/"));
            NQuadsParser.Parse(
                Harness.U("<http://a/s> <http://a/p> \"x\" .\n"),
                (in QuadView quad) => writer.Write(in quad),
                default);
        }

        Assert.Equal("VERSION \"1.2\"\n@prefix a: <http://a/> .\na:s a:p \"x\" .\n", Encoding.UTF8.GetString(output.Written));
    }

    [Fact]
    public void trig_closes_an_open_block_before_the_version_directive()
    {
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = new() { Syntax = RdfSyntax.TriG };

        using (TurtleWriter writer = new(output, in options))
        {
            NQuadsParser.Parse(
                Harness.U("<http://a/s> <http://a/p> \"x\" <http://a/g> .\n<http://a/s> <http://a/p> \"y\"@en--ltr <http://a/g> .\n"),
                (in QuadView quad) => writer.Write(in quad),
                new ParseOptions { Syntax = RdfSyntax.NQuads });
        }

        string written = Encoding.UTF8.GetString(output.Written);
        Assert.Equal(
            "<http://a/g> {\n  <http://a/s> <http://a/p> \"x\" .\n}\nVERSION \"1.2\"\n<http://a/g> {\n  <http://a/s> <http://a/p> \"y\"@en--ltr .\n}\n",
            written);

        Read read = ParseTriG(written);
        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(2, read.Rows.Count);
    }

    [Fact]
    public void the_writer_refuses_a_triple_term_as_subject_by_name()
    {
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;
        RdfTerm tt = RdfTerm.TripleTerm(RdfTerm.Iri(Harness.U("http://a/a")), RdfTerm.Iri(Harness.U("http://a/b")), RdfTerm.Iri(Harness.U("http://a/c")));
        InMemoryDatasetBuilder builder = new();
        builder.Add(tt, RdfTerm.Iri(Harness.U("http://a/p")), RdfTerm.Iri(Harness.U("http://a/o")));
        InMemoryDataset dataset = builder.ToDataset();

        using TurtleWriter writer = new(output, in options);
        System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(() =>
        {
            using IQuadCursor cursor = dataset.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                writer.Write(in quad, dataset);
            }
        });

        Assert.Contains("subject or predicate", error.Message, System.StringComparison.Ordinal);
    }
}
