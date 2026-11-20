// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.WebUi.Explorer.Parts;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// What a regime added (ADR 0254 point 3): the regime's answer less
/// <c>simple</c>'s at one position. A store's blank node is the same node in
/// both reads by its label (ADR 0098); a blank node the query made is not,
/// and its statements are not compared. Nothing is marked unless both reads
/// ran to their end at one position.
/// </summary>
public class RegimeComparisonTests
{
    private static readonly FeedPosition At = new(18417);
    private static readonly RdfTerm Type = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    private static readonly RdfTerm SubClassOf = Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf");
    private static readonly RdfTerm Note = Iri("https://notes.example/Note");
    private static readonly RdfTerm Document = Iri("https://notes.example/Document");
    private static readonly RdfTerm Title = Iri("http://purl.org/dc/terms/title");

    [Fact]
    public void a_store_blank_node_is_one_node_in_both_reads_and_is_compared_by_its_label()
    {
        RdfTerm b7 = Blank("b7");
        Statement stored = new(b7, Type, Note);
        Statement inferred = new(b7, Type, Document);
        Statement ontology = new(Note, SubClassOf, Document);

        RegimeMarks marks = RegimeComparison.Compare(
            [stored, inferred, ontology], QueryEnd.Complete, At,
            [stored], QueryEnd.Complete, At,
            new HashSet<Statement> { ontology });

        Assert.Null(marks.Unmarked);
        Assert.Equal([StatementMark.Asserted, StatementMark.Inferred, StatementMark.FromTheOntology], marks.Marks);
        Assert.Equal(0, marks.NotCompared);
        Assert.Null(marks.NotComparedNote);
    }

    [Fact]
    public void a_blank_node_the_query_made_is_never_compared_even_under_the_same_label()
    {
        // q1 in read A and q1 in read B are two executions' nodes (sparql-evaluation.md §7.9).
        Statement minted = new(Blank("q1"), Title, Literal("Weekly sync"));
        Statement nested = new(Iri("https://notes.example/42"), Title, RdfTerm.TripleTerm(Blank("q2"), Type, Note));
        Statement plain = new(Iri("https://notes.example/42"), Type, Note);

        RegimeMarks marks = RegimeComparison.Compare(
            [minted, nested, plain], QueryEnd.Complete, At,
            [minted, nested], QueryEnd.Complete, At,
            new HashSet<Statement>());

        Assert.Equal([StatementMark.NotCompared, StatementMark.NotCompared, StatementMark.Inferred], marks.Marks);
        Assert.Equal(2, marks.NotCompared);
        Assert.Equal("2 statements hold a blank node this query made, which two reads cannot match, so they are not marked.", marks.NotComparedNote);
    }

    [Theory]
    [InlineData("b1", true)]
    [InlineData("b18417", true)]
    [InlineData("b", false)]
    [InlineData("q1", false)]
    [InlineData("b1x", false)]
    [InlineData("B1", false)]
    [InlineData("genid1", false)]
    public void only_a_store_label_names_one_node_across_reads(string label, bool store) =>
        Assert.Equal(store, RegimeComparison.IsStoreLabel(Encoding.UTF8.GetBytes(label)));

    [Theory]
    [InlineData(QueryEnd.Closed, QueryEnd.Complete, "the read under the regime stopped before its end, at the ceiling or by a cancel")]
    [InlineData(QueryEnd.Cut, QueryEnd.Complete, "the read under the regime ended before the server finished it")]
    [InlineData(QueryEnd.Reading, QueryEnd.Complete, "the read under the regime has not ended")]
    [InlineData(QueryEnd.Complete, QueryEnd.Closed, "the read under simple stopped before its end, at the ceiling or by a cancel")]
    [InlineData(QueryEnd.Complete, QueryEnd.Cut, "the read under simple ended before the server finished it")]
    public void nothing_is_marked_unless_both_reads_ran_to_their_end(QueryEnd regimeEnd, QueryEnd simpleEnd, string why)
    {
        Statement statement = new(Iri("https://notes.example/42"), Type, Document);

        RegimeMarks marks = RegimeComparison.Compare(
            [statement], regimeEnd, At,
            [], simpleEnd, At,
            new HashSet<Statement>());

        Assert.Null(marks.Marks);
        Assert.Equal("Nothing is marked as added: " + why + ", so what the regime added cannot be told.", marks.Unmarked);
        Assert.Null(marks.NotComparedNote);
    }

    [Fact]
    public void nothing_is_marked_when_the_reads_were_at_two_positions()
    {
        Statement statement = new(Iri("https://notes.example/42"), Type, Document);

        RegimeMarks marks = RegimeComparison.Compare(
            [statement], QueryEnd.Complete, At,
            [], QueryEnd.Complete, new FeedPosition(18418),
            new HashSet<Statement>());

        Assert.Null(marks.Marks);
        Assert.Equal("Nothing is marked as added: the two reads were not at one position, so what the regime added cannot be told.", marks.Unmarked);
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    private static RdfTerm Blank(string label) => RdfTerm.BlankNode(Encoding.UTF8.GetBytes(label));

    private static RdfTerm Literal(string text) => RdfTerm.Literal(Encoding.UTF8.GetBytes(text));
}
