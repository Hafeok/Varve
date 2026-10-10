// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.RdfXml.Model;
using Xunit;
using static Varve.RdfXml.Tests.Harness;

namespace Varve.RdfXml.Tests;

/// <summary>
/// Named cases for the RDF/XML reader: what each construct of RDF 1.1 XML
/// Syntax §7 and RDF 1.2 XML §6 means, in one line of N-Triples each. The
/// W3C suites are the gate; these say which sentence a failure is against.
/// </summary>
public class ReaderTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Open = "<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:ex=\"http://example.org/\">";

    /// <summary>A root announcing RDF 1.2, which <c>rdf:parseType="Triple"</c> needs (RDF 1.2 XML §3.1).</summary>
    private const string Open12 = "<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:ex=\"http://example.org/\" rdf:version=\"1.2\">";
    private const string Close = "</rdf:RDF>";

    [Fact]
    public void a_description_with_literal_and_resource_properties()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p>text</ex:p><ex:q rdf:resource=\"http://example.org/o\"/></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"text\"",
                "<http://example.org/s> <http://example.org/q> <http://example.org/o>",
            ],
            read.Lines);
    }

    [Fact]
    public void a_typed_node_element_states_its_type_first()
    {
        Read read = Parse(Open + "<ex:Thing rdf:about=\"http://example.org/s\" ex:name=\"n\" xml:lang=\"en\"/>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <" + Rdf + "type> <http://example.org/Thing>",
                "<http://example.org/s> <http://example.org/name> \"n\"@en",
            ],
            read.Lines);
    }

    [Fact]
    public void a_datatype_beats_a_language_and_a_language_beats_nothing()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\" xml:lang=\"fr\"><ex:p rdf:datatype=\"http://www.w3.org/2001/XMLSchema#integer\">10</ex:p><ex:q>dix</ex:q><ex:r xml:lang=\"\">plain</ex:r></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"10\"^^<http://www.w3.org/2001/XMLSchema#integer>",
                "<http://example.org/s> <http://example.org/q> \"dix\"@fr",
                "<http://example.org/s> <http://example.org/r> \"plain\"",
            ],
            read.Lines);
    }

    [Fact]
    public void a_node_id_passes_through_and_a_fresh_node_is_a_number()
    {
        // rdf:nodeID is an NCName, which cannot begin with a digit, so a
        // document's label and the parser's can never collide (ADR 0122).
        Read read = Parse(Open + "<rdf:Description rdf:nodeID=\"a\"><ex:p><rdf:Description><ex:q>x</ex:q></rdf:Description></ex:p></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "_:1 <http://example.org/q> \"x\"",
                "_:a <http://example.org/p> _:1",
            ],
            read.Lines);
    }

    [Fact]
    public void li_is_numbered_per_node_element()
    {
        Read read = Parse(Open + "<rdf:Seq rdf:about=\"http://example.org/s\"><rdf:li>a</rdf:li><rdf:li>b</rdf:li></rdf:Seq><rdf:Bag rdf:about=\"http://example.org/t\"><rdf:li>c</rdf:li></rdf:Bag>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <" + Rdf + "type> <" + Rdf + "Seq>",
                "<http://example.org/s> <" + Rdf + "_1> \"a\"",
                "<http://example.org/s> <" + Rdf + "_2> \"b\"",
                "<http://example.org/t> <" + Rdf + "type> <" + Rdf + "Bag>",
                "<http://example.org/t> <" + Rdf + "_1> \"c\"",
            ],
            read.Lines);
    }

    [Fact]
    public void parse_type_resource_describes_a_fresh_node()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Resource\"><ex:q>x</ex:q></ex:p></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> _:1",
                "_:1 <http://example.org/q> \"x\"",
            ],
            read.Lines);
    }

    [Fact]
    public void parse_type_collection_is_a_list_and_an_empty_one_is_nil()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Collection\"><rdf:Description rdf:about=\"http://example.org/a\"/><rdf:Description rdf:about=\"http://example.org/b\"/></ex:p><ex:q rdf:parseType=\"Collection\"/></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "_:1 <" + Rdf + "first> <http://example.org/a>",
                "_:1 <" + Rdf + "rest> _:2",
                "_:2 <" + Rdf + "first> <http://example.org/b>",
                "_:2 <" + Rdf + "rest> <" + Rdf + "nil>",
                "<http://example.org/s> <http://example.org/p> _:1",
                "<http://example.org/s> <http://example.org/q> <" + Rdf + "nil>",
            ],
            read.Lines);
    }

    [Fact]
    public void parse_type_literal_is_exclusive_canonical_xml()
    {
        // An empty element becomes a start and an end tag; attributes are
        // sorted; the namespace an inner element uses is declared on it.
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Literal\"><b z=\"1\" a=\"2\">bold<br/></b> &amp; <ex:i>i</ex:i></ex:p></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"<b a=\\\"2\\\" z=\\\"1\\\">bold<br></br></b> &amp; <ex:i xmlns:ex=\\\"http://example.org/\\\">i</ex:i>\"^^<" + Rdf + "XMLLiteral>",
            ],
            read.Lines);
    }

    [Fact]
    public void an_id_on_a_property_element_reifies_it()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:ID=\"r\">x</ex:p></rdf:Description>" + Close, baseIri: "http://example.org/doc");

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"x\"",
                "<http://example.org/doc#r> <" + Rdf + "type> <" + Rdf + "Statement>",
                "<http://example.org/doc#r> <" + Rdf + "subject> <http://example.org/s>",
                "<http://example.org/doc#r> <" + Rdf + "predicate> <http://example.org/p>",
                "<http://example.org/doc#r> <" + Rdf + "object> \"x\"",
            ],
            read.Lines);
    }

    [Fact]
    public void an_annotation_names_a_reifier_of_the_triple_term()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:annotation=\"http://example.org/t\">x</ex:p><ex:q rdf:annotationNodeID=\"n\" rdf:resource=\"http://example.org/o\"/></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"x\"",
                "<http://example.org/t> <" + Rdf + "reifies> <<( <http://example.org/s> <http://example.org/p> \"x\" )>>",
                "<http://example.org/s> <http://example.org/q> <http://example.org/o>",
                "_:n <" + Rdf + "reifies> <<( <http://example.org/s> <http://example.org/q> <http://example.org/o> )>>",
            ],
            read.Lines);
    }

    [Fact]
    public void parse_type_triple_is_a_triple_term_and_asserts_nothing()
    {
        Read read = Parse(Open12 + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"http://example.org/a\"><ex:b rdf:resource=\"http://example.org/c\"/></rdf:Description></ex:p></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            ["<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>>"],
            read.Lines);
    }

    [Fact]
    public void parse_type_triple_needs_exactly_one_triple()
    {
        Read read = Parse(Open12 + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Triple\"><ex:T rdf:about=\"http://example.org/a\"><ex:b rdf:resource=\"http://example.org/c\"/></ex:T></ex:p></rdf:Description>" + Close);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(RdfXmlErrorKind.InvalidTripleTerm, read.Result.Error!.Kind);
    }

    [Fact]
    public void parse_type_triple_without_a_1_2_version_in_scope_is_ignored()
    {
        // RDF 1.2 XML §3.1: the 1.2 forms are read only where rdf:version
        // announces 1.2; the suite's tt-01 expects nothing from this element.
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"http://example.org/a\"><ex:b rdf:resource=\"http://example.org/c\"/></rdf:Description></ex:p><ex:q>after</ex:q></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(["<http://example.org/s> <http://example.org/q> \"after\""], read.Lines);

        // The version may also sit on the element itself.
        read = Parse(Open + "<rdf:Description rdf:version=\"1.2\" rdf:about=\"http://example.org/s\"><ex:p rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"http://example.org/a\"><ex:b rdf:resource=\"http://example.org/c\"/></rdf:Description></ex:p></rdf:Description>" + Close);
        Assert.Equal(["<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>>"], read.Lines);
    }

    [Fact]
    public void an_rdf_namespace_property_attribute_and_an_unprefixed_xml_attribute()
    {
        // rdf:value and rdf:_n are property attributes (rdfms-rdf-names-use);
        // an attribute with no namespace whose name starts "xml" is XML's own.
        Read read = Parse(Open + "<rdf:Seq rdf:about=\"http://example.org/s\" rdf:_3=\"3\" rdf:value=\"v\" xmlnewthing=\"ignored\"/>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <" + Rdf + "type> <" + Rdf + "Seq>",
                "<http://example.org/s> <" + Rdf + "_3> \"3\"",
                "<http://example.org/s> <" + Rdf + "value> \"v\"",
            ],
            read.Lines);
    }

    [Fact]
    public void a_direction_joins_a_language_and_is_nothing_without_one()
    {
        Read read = Parse("<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:ex=\"http://example.org/\" xmlns:its=\"http://www.w3.org/2005/11/its\" its:dir=\"rtl\" rdf:version=\"1.2\"><rdf:Description rdf:about=\"http://example.org/s\"><ex:p xml:lang=\"ar\">x</ex:p><ex:q>y</ex:q><ex:r xml:lang=\"en\" its:dir=\"ltr\">z</ex:r></rdf:Description></rdf:RDF>");

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://example.org/s> <http://example.org/p> \"x\"@ar--rtl",
                "<http://example.org/s> <http://example.org/q> \"y\"",
                "<http://example.org/s> <http://example.org/r> \"z\"@en--ltr",
            ],
            read.Lines);
    }

    [Fact]
    public void a_direction_without_a_1_2_version_in_scope_is_ignored()
    {
        // RDF 1.2 XML §3.1; the suite's dir-02 ("Language with direction and
        // no RDF version") expects the language alone.
        Read read = Parse("<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:ex=\"http://example.org/\" xmlns:its=\"http://www.w3.org/2005/11/its\" its:dir=\"ltr\" xml:lang=\"en\"><rdf:Description rdf:about=\"http://example.org/s\" ex:p=\"x\"/></rdf:RDF>");

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(["<http://example.org/s> <http://example.org/p> \"x\"@en"], read.Lines);
    }

    [Fact]
    public void xml_base_resolves_about_resource_and_id()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"a\" xml:base=\"http://b.example/dir/\"><ex:p rdf:resource=\"../o\"/><ex:q rdf:ID=\"frag\">x</ex:q></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal("<http://b.example/dir/a> <http://example.org/p> <http://b.example/o>", read.Lines[0]);
        Assert.Contains("<http://b.example/dir/#frag> <" + Rdf + "type> <" + Rdf + "Statement>", read.Lines);
    }

    [Fact]
    public void a_relative_reference_with_no_base_is_an_error()
    {
        Read read = Parse(Open + "<rdf:Description rdf:about=\"a\"/>" + Close, baseIri: null);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(RdfXmlErrorKind.RelativeIri, read.Result.Error!.Kind);
    }

    [Theory]
    [InlineData("<rdf:Description rdf:ID=\"a\"/><rdf:Description rdf:ID=\"a\"/>", RdfXmlErrorKind.DuplicateId)]
    [InlineData("<rdf:Description rdf:ID=\"1a\"/>", RdfXmlErrorKind.InvalidName)]
    [InlineData("<rdf:Description rdf:nodeID=\"a:b\"/>", RdfXmlErrorKind.InvalidName)]
    [InlineData("<rdf:Description rdf:about=\"http://example.org/s\" rdf:nodeID=\"a\"/>", RdfXmlErrorKind.ConflictingAttributes)]
    [InlineData("<rdf:Description rdf:li=\"x\"/>", RdfXmlErrorKind.ForbiddenAttribute)]
    [InlineData("<rdf:Description rdf:aboutEach=\"x\"/>", RdfXmlErrorKind.ForbiddenAttribute)]
    [InlineData("<rdf:li><ex:p>x</ex:p></rdf:li>", RdfXmlErrorKind.ForbiddenElementName)]
    [InlineData("<rdf:Description><rdf:Description>x</rdf:Description></rdf:Description>", RdfXmlErrorKind.ForbiddenElementName)]
    [InlineData("<rdf:Description>text</rdf:Description>", RdfXmlErrorKind.UnexpectedText)]
    [InlineData("<rdf:Description><ex:p>text<rdf:Description/></ex:p></rdf:Description>", RdfXmlErrorKind.UnexpectedContent)]
    [InlineData("<rdf:Description><ex:p rdf:resource=\"http://example.org/o\">text</ex:p></rdf:Description>", RdfXmlErrorKind.ForbiddenAttribute)]
    [InlineData("<rdf:Description><ex:p rdf:resource=\"http://example.org/o\" rdf:nodeID=\"n\"/></rdf:Description>", RdfXmlErrorKind.ConflictingAttributes)]
    [InlineData("<rdf:Description><ex:p xml:lang=\"not a tag\">x</ex:p></rdf:Description>", RdfXmlErrorKind.InvalidLanguageTag)]
    [InlineData("<rdf:Description about=\"http://example.org/s\"/>", RdfXmlErrorKind.ForbiddenAttribute)]
    public void a_malformed_document_is_rejected_by_kind(string body, RdfXmlErrorKind expected)
    {
        Read read = Parse(Open + body + Close);

        Assert.False(read.Result.Succeeded, "accepted: " + string.Join(" | ", read.Lines));
        Assert.Equal(expected, read.Result.Error!.Kind);
    }

    [Fact]
    public void ill_formed_xml_and_a_dtd_are_malformed_xml_with_the_readers_position()
    {
        Read read = Parse(Open + "<rdf:Description><ex:p>x</rdf:Description>" + Close);
        Assert.Equal(RdfXmlErrorKind.MalformedXml, read.Result.Error!.Kind);
        Assert.Equal(1, read.Result.Error.Line);
        Assert.True(read.Result.Error.Column > 1);

        Read dtd = Parse("<!DOCTYPE rdf:RDF [<!ENTITY x \"y\">]>" + Open + Close);
        Assert.Equal(RdfXmlErrorKind.MalformedXml, dtd.Result.Error!.Kind);
    }

    [Fact]
    public void a_document_without_the_rdf_root_is_one_node_element()
    {
        Read read = Parse("<ex:Thing xmlns:rdf=\"" + Rdf + "\" xmlns:ex=\"http://example.org/\" rdf:about=\"http://example.org/s\"/>");

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(["<http://example.org/s> <" + Rdf + "type> <http://example.org/Thing>"], read.Lines);
    }

    [Fact]
    public void namespace_declarations_are_reported_as_made()
    {
        Read read = Parse(Open + "<rdf:Description xmlns:local=\"http://l.example/\" rdf:about=\"http://example.org/s\"><local:p>x</local:p></rdf:Description>" + Close);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal([("rdf", Rdf), ("ex", "http://example.org/"), ("local", "http://l.example/")], read.Namespaces);
    }

    [Fact]
    public void the_quads_already_handed_out_stand_when_a_later_element_fails()
    {
        // No recovery unit (rdf-xml.md §5): the parse stops, and what was
        // emitted was emitted.
        Read read = Parse(Open + "<rdf:Description rdf:about=\"http://example.org/s\"><ex:p>x</ex:p></rdf:Description><rdf:Description rdf:ID=\"1bad\"/>" + Close);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(1, read.Result.QuadCount);
        Assert.Single(read.Lines);
    }
}
