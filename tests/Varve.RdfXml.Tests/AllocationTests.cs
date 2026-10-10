// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Xunit;

namespace Varve.RdfXml.Tests;

/// <summary>
/// What RDF/XML costs per triple, measured as a difference between two
/// documents (<c>n-triples.md</c> §6), so that the harness's own cost cancels.
/// </summary>
/// <remarks>
/// <para>
/// This package's own code allocates nothing per triple: names are the
/// reader's atomised strings, values are read in chunks into one arena. What
/// <c>XmlReader</c> and <c>XmlWriter</c> allocate per element is theirs, and
/// the honest figure is what these tests assert and <c>rdf-xml.md</c> §7
/// states — a bound, not a zero (ADR 0122). The bound is set with headroom
/// over the measurement, so that an unrelated runtime change does not teach
/// everyone to raise it, and tightened when the measurement moves down.
/// </para>
/// </remarks>
public class AllocationTests
{
    private const int Small = 64;
    private const int Large = 640;

    /// <summary>Bytes per triple the reader may cost, XmlReader's included. The measured figure is in rdf-xml.md §7.</summary>
    private const int ReaderBytesPerTripleLimit = 24;

    /// <summary>Bytes per triple the writer may cost, XmlWriter's included.</summary>
    private const int WriterBytesPerTripleLimit = 48;

    private static readonly byte[] SmallDocument = Document(Small);
    private static readonly byte[] LargeDocument = Document(Large);
    private static readonly Harness.ArrayBufferWriter Output = new();

    private static int triplesSeen;

    private static void Count(in QuadView quad) => triplesSeen++;

    /// <summary>
    /// Fixed-width indices, so that every description in both documents is
    /// the same length and the difference is the triples and nothing else.
    /// Each description is six triples: a typed node, a plain literal, a
    /// language-tagged literal, a typed literal, a resource and a nested
    /// blank node.
    /// </summary>
    private static byte[] Document(int descriptions)
    {
        StringBuilder builder = new();
        builder.Append("<?xml version=\"1.0\"?>\n<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:p=\"http://example.org/\">\n");

        for (int i = 0; i < descriptions; i++)
        {
            string n = i.ToString("D6", CultureInfo.InvariantCulture);
            builder.Append("  <p:Thing rdf:about=\"http://example.org/s").Append(n).Append("\">\n")
                .Append("    <p:name>value ").Append(n).Append(" with an entity &amp; more</p:name>\n")
                .Append("    <p:label xml:lang=\"en\">label ").Append(n).Append("</p:label>\n")
                .Append("    <p:size rdf:datatype=\"http://www.w3.org/2001/XMLSchema#integer\">").Append(n).Append("</p:size>\n")
                .Append("    <p:knows rdf:resource=\"http://example.org/o").Append(n).Append("\"/>\n")
                .Append("    <p:part><rdf:Description><p:q>x</p:q></rdf:Description></p:part>\n")
                .Append("  </p:Thing>\n");
        }

        builder.Append("</rdf:RDF>\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static (long Fixed, long PerTriple) Profile(Action<bool> parse)
    {
        (long small, long large) = AllocationMeter.MeasurePair(
            () =>
            {
                parse(false);
                return null;
            },
            () =>
            {
                parse(true);
                return null;
            });

        return (small, (large - small) / ((Large - Small) * 7L));
    }

    [Fact]
    public void the_reader_costs_a_bounded_number_of_bytes_per_triple()
    {
        triplesSeen = 0;
        (long fixedCost, long perTriple) = Profile(static large =>
        {
            RdfXmlOptions options = default;
            RdfXmlParser.Parse(large ? LargeDocument : SmallDocument, Count, in options);
        });

        Assert.True(triplesSeen > Large * 7, "the parse did not run");
        Assert.True(
            perTriple <= ReaderBytesPerTripleLimit,
            string.Create(CultureInfo.InvariantCulture, $"{perTriple} bytes per triple exceeds {ReaderBytesPerTripleLimit}; fixed cost {fixedCost}"));
    }

    [Fact]
    public void the_writer_costs_a_bounded_number_of_bytes_per_triple()
    {
        (long fixedCost, long perTriple) = Profile(static large =>
        {
            Output.Reset();
            RdfXmlWriteOptions write = default;
            RdfXmlOptions read = default;

            using RdfXmlWriter writer = new(Output, in write);
            writer.DeclarePrefix("p"u8, "http://example.org/"u8);
            RdfXmlParser.Parse(large ? LargeDocument : SmallDocument, (in QuadView quad) => writer.Write(in quad), in read);
        });

        Assert.True(
            perTriple <= WriterBytesPerTripleLimit,
            string.Create(CultureInfo.InvariantCulture, $"{perTriple} bytes per triple exceeds {WriterBytesPerTripleLimit}; fixed cost {fixedCost}"));
    }

    [Fact]
    public void the_reader_reports_how_much_it_costs()
    {
        // Not an assertion: the measured figures, printed so that a run shows
        // what rdf-xml.md §7 states, and whether the limits above are honest.
        (long readerFixed, long readerPerTriple) = Profile(static large =>
        {
            RdfXmlOptions options = default;
            RdfXmlParser.Parse(large ? LargeDocument : SmallDocument, Count, in options);
        });

        (long writerFixed, long writerPerTriple) = Profile(static large =>
        {
            Output.Reset();
            RdfXmlWriteOptions write = default;
            RdfXmlOptions read = default;
            using RdfXmlWriter writer = new(Output, in write);
            RdfXmlParser.Parse(large ? LargeDocument : SmallDocument, (in QuadView quad) => writer.Write(in quad), in read);
        });

        Assert.True(readerPerTriple >= 0 && writerPerTriple >= 0, string.Create(
            CultureInfo.InvariantCulture,
            $"reader: {readerPerTriple} bytes/triple, {readerFixed} fixed; writer: {writerPerTriple} bytes/triple, {writerFixed} fixed"));
    }
}
