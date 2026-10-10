// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Rdf;
using Xunit;
using static Varve.RdfXml.Tests.Harness;

namespace Varve.RdfXml.Tests;

/// <summary>The RDF/XML writer: its shape, its one invention, and what it refuses by name (rdf-xml.md §6).</summary>
public class WriterTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    [Fact]
    public void consecutive_triples_of_one_subject_share_a_description()
    {
        string xml = WriteXml(
            "<http://a/s> <http://a/p> \"x\" .\n<http://a/s> <http://a/q> <http://a/o> .\n<http://a/t> <http://a/p> _:b .\n",
            writer => writer.DeclarePrefix(U("a"), U("http://a/")));

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:a=\"http://a/\">\n"
            + "  <rdf:Description rdf:about=\"http://a/s\">\n"
            + "    <a:p>x</a:p>\n"
            + "    <a:q rdf:resource=\"http://a/o\" />\n"
            + "  </rdf:Description>\n"
            + "  <rdf:Description rdf:about=\"http://a/t\">\n"
            + "    <a:p rdf:nodeID=\"b\" />\n"
            + "  </rdf:Description>\n"
            + "</rdf:RDF>",
            xml);
    }

    [Fact]
    public void an_undeclared_namespace_gets_an_invented_prefix_and_a_numeric_label_a_b()
    {
        string xml = WriteXml("_:0 <http://a/p> \"x\"@en .\n_:0 <http://b/q> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n", indent: false);

        // XmlWriter declares an invented prefix on the element that first
        // uses it, after that element's other attributes.
        Assert.Contains("<rdf:Description rdf:nodeID=\"b0\">", xml, StringComparison.Ordinal);
        Assert.Contains("<ns0:p xml:lang=\"en\" xmlns:ns0=\"http://a/\">x</ns0:p>", xml, StringComparison.Ordinal);
        Assert.Contains("<ns1:q rdf:datatype=\"http://www.w3.org/2001/XMLSchema#integer\" xmlns:ns1=\"http://b/\">1</ns1:q>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void rdf_12_terms_carry_the_version_on_their_description()
    {
        string xml = WriteXml(
            "<http://a/s> <http://a/p> \"x\" .\n<http://a/s> <http://a/q> \"y\"@ar--rtl .\n<http://a/s> <http://a/r> <<( <http://a/a> <http://a/b> \"c\" )>> .\n",
            writer => writer.DeclarePrefix(U("a"), U("http://a/")),
            indent: false);

        // The first triple's description has no version; the directional
        // literal reopens one that has, and the triple term shares it.
        Assert.Contains("<rdf:Description rdf:about=\"http://a/s\"><a:p>x</a:p></rdf:Description>", xml, StringComparison.Ordinal);
        Assert.Contains("<rdf:Description rdf:version=\"1.2\" rdf:about=\"http://a/s\"><a:q xml:lang=\"ar\" its:dir=\"rtl\" xmlns:its=\"http://www.w3.org/2005/11/its\">y</a:q><a:r rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"http://a/a\"><a:b>c</a:b></rdf:Description></a:r></rdf:Description>", xml, StringComparison.Ordinal);

        Read back = Parse(xml);
        Assert.True(back.Result.Succeeded, back.Result.Error?.ToString());
        Assert.Equal(
            [
                "<http://a/s> <http://a/p> \"x\"",
                "<http://a/s> <http://a/q> \"y\"@ar--rtl",
                "<http://a/s> <http://a/r> <<( <http://a/a> <http://a/b> \"c\" )>>",
            ],
            back.Lines);
    }

    [Fact]
    public void an_xml_literal_is_written_as_a_typed_literal_and_reads_back_the_same()
    {
        string xml = WriteXml("<http://a/s> <http://a/p> \"<b>x</b>\"^^<" + Rdf + "XMLLiteral> .\n", indent: false);

        Assert.Contains("rdf:datatype=\"" + Rdf + "XMLLiteral\"", xml, StringComparison.Ordinal);
        Assert.Contains(">&lt;b&gt;x&lt;/b&gt;<", xml, StringComparison.Ordinal);
        Read back = Parse(xml);
        Assert.Equal(["<http://a/s> <http://a/p> \"<b>x</b>\"^^<" + Rdf + "XMLLiteral>"], back.Lines);
    }

    [Fact]
    public void a_document_with_no_triples_is_an_empty_root()
    {
        Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<rdf:RDF xmlns:rdf=\"" + Rdf + "\" />", WriteXml(""));
    }

    [Theory]
    [InlineData("<http://a/s> <http://a/p> \"x\" <http://a/g> .\n", "no graphs")]
    [InlineData("<http://a/s> <http://a/123> \"x\" .\n", "NCName")]
    [InlineData("<http://a/s> <http://a/p/> \"x\" .\n", "NCName")]
    [InlineData("<http://a/s> <http://a/p> \"x\\u0001\" .\n", "U+0001")]
    public void what_the_syntax_cannot_spell_is_refused_by_name(string nquads, string named)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => WriteXml(nquads));
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_triple_term_as_subject_is_refused_by_name()
    {
        ArrayBufferWriter output = new();
        RdfXmlWriteOptions options = default;
        RdfTerm tt = RdfTerm.TripleTerm(RdfTerm.Iri(U("http://a/a")), RdfTerm.Iri(U("http://a/b")), RdfTerm.Iri(U("http://a/c")));
        InMemoryDatasetBuilder builder = new();
        builder.Add(tt, RdfTerm.Iri(U("http://a/p")), RdfTerm.Iri(U("http://a/o")));
        InMemoryDataset dataset = builder.ToDataset();

        using RdfXmlWriter writer = new(output, in options);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
        {
            using IQuadCursor cursor = dataset.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                writer.Write(in quad, dataset);
            }
        });

        Assert.Contains("subject or predicate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_reserved_or_malformed_prefix_is_refused()
    {
        ArrayBufferWriter output = new();
        RdfXmlWriteOptions options = default;
        using RdfXmlWriter writer = new(output, in options);

        Assert.Throws<ArgumentException>(() => writer.DeclarePrefix(U("xmlfoo"), U("http://a/")));
        Assert.Throws<ArgumentException>(() => writer.DeclarePrefix(U("1a"), U("http://a/")));
    }
}
