// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.JsonLd.Tests;

/// <summary>
/// What JSON-LD costs per quad, measured as a difference between two documents
/// (<c>n-triples.md</c> §6), so that the harness's own cost cancels.
/// </summary>
/// <remarks>
/// <para>
/// A JSON-LD document is a tree before it is a dataset: the whole input is
/// read into one index-linked tree, expanded into the same tree, and only
/// then walked for quads. So the figure per quad is not the emitter's, which
/// allocates nothing, but the tree's growth per node — pooled arrays and one
/// UTF-8 arena — and it is a bound rather than zero (ADR 0123). The honest
/// row is the first one in <c>json-ld.md</c> §7. The bound is set with
/// headroom over the measurement and tightened when the measurement moves.
/// </para>
/// </remarks>
public class AllocationTests
{
    private const int Small = 64;
    private const int Large = 640;
    private const int QuadsPerNode = 6;

    /// <summary>Bytes per quad the toRdf path may cost, the tree's growth included.</summary>
    private const int ReaderBytesPerQuadLimit = 64;

    /// <summary>Bytes per quad the fromRdf writer may cost, the buffered dataset included.</summary>
    private const int WriterBytesPerQuadLimit = 512;

    /// <summary>Bytes per quad expansion may cost, the written JSON included.</summary>
    private const int ExpanderBytesPerQuadLimit = 64;

    private static readonly byte[] SmallDocument = Document(Small);
    private static readonly byte[] LargeDocument = Document(Large);
    private static readonly byte[] SmallQuads = Quads(Small);
    private static readonly byte[] LargeQuads = Quads(Large);
    private static readonly Harness.ArrayBufferWriter Output = new();

    private static int quadsSeen;

    private static void Count(in QuadView quad) => quadsSeen++;

    /// <summary>
    /// Fixed-width indices, so that every node in both documents is the same
    /// length and the difference is the quads and nothing else. Each node is
    /// six quads: a type, a plain literal, a language-tagged literal, a typed
    /// literal, an IRI and a nested blank node.
    /// </summary>
    private static byte[] Document(int nodes)
    {
        StringBuilder builder = new();
        builder.Append("{\"@context\": {\"ex\": \"http://example.org/\", \"name\": \"ex:name\", \"label\": {\"@id\": \"ex:label\", \"@language\": \"en\"}, \"size\": {\"@id\": \"ex:size\", \"@type\": \"http://www.w3.org/2001/XMLSchema#integer\"}, \"knows\": {\"@id\": \"ex:knows\", \"@type\": \"@id\"}}, \"@graph\": [\n");

        for (int i = 0; i < nodes; i++)
        {
            string n = i.ToString("D6", CultureInfo.InvariantCulture);
            builder.Append(i == 0 ? "" : ",\n")
                .Append("{\"@id\": \"ex:s").Append(n).Append("\", \"@type\": \"ex:Thing\", \"name\": \"value ").Append(n)
                .Append("\", \"label\": \"label ").Append(n).Append("\", \"size\": \"").Append(n)
                .Append("\", \"knows\": \"ex:o").Append(n).Append("\", \"ex:part\": {\"ex:q\": \"x\"}}");
        }

        builder.Append("\n]}\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] Quads(int nodes)
    {
        StringBuilder builder = new();

        for (int i = 0; i < nodes; i++)
        {
            string n = i.ToString("D6", CultureInfo.InvariantCulture);
            string s = "<http://example.org/s" + n + ">";
            builder.Append(s).Append(" <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <http://example.org/Thing> .\n")
                .Append(s).Append(" <http://example.org/name> \"value ").Append(n).Append("\" .\n")
                .Append(s).Append(" <http://example.org/label> \"label ").Append(n).Append("\"@en .\n")
                .Append(s).Append(" <http://example.org/size> \"").Append(n).Append("\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n")
                .Append(s).Append(" <http://example.org/knows> <http://example.org/o").Append(n).Append("> .\n")
                .Append(s).Append(" <http://example.org/part> _:b").Append(n).Append(" .\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static (long Fixed, long PerQuad) Profile(Action<bool> run)
    {
        (long small, long large) = AllocationMeter.MeasurePair(
            () =>
            {
                run(false);
                return null;
            },
            () =>
            {
                run(true);
                return null;
            });

        return (small, (large - small) / ((Large - Small) * (long)QuadsPerNode));
    }

    [Fact]
    public void the_reader_costs_a_bounded_number_of_bytes_per_quad()
    {
        quadsSeen = 0;
        (long fixedCost, long perQuad) = Profile(static large =>
        {
            JsonLdOptions options = default;
            JsonLdParser.Parse(large ? LargeDocument : SmallDocument, Count, in options);
        });

        Assert.True(quadsSeen > Large * QuadsPerNode, "the parse did not run");
        Assert.True(
            perQuad <= ReaderBytesPerQuadLimit,
            string.Create(CultureInfo.InvariantCulture, $"{perQuad} bytes per quad exceeds {ReaderBytesPerQuadLimit}; fixed cost {fixedCost}"));
    }

    [Fact]
    public void the_expander_costs_a_bounded_number_of_bytes_per_quad()
    {
        (long fixedCost, long perQuad) = Profile(static large =>
        {
            Output.Reset();
            JsonLdOptions options = default;
            JsonLdExpander.Expand(large ? LargeDocument : SmallDocument, Output, in options);
        });

        Assert.True(
            perQuad <= ExpanderBytesPerQuadLimit,
            string.Create(CultureInfo.InvariantCulture, $"{perQuad} bytes per quad exceeds {ExpanderBytesPerQuadLimit}; fixed cost {fixedCost}"));
    }

    [Fact]
    public void the_writer_costs_a_bounded_number_of_bytes_per_quad()
    {
        (long fixedCost, long perQuad) = Profile(static large =>
        {
            Output.Reset();
            JsonLdWriteOptions write = new() { Indent = false };

            using JsonLdWriter writer = new(Output, in write);
            NQuadsParser.Parse(large ? LargeQuads : SmallQuads, (in QuadView quad) => writer.Write(in quad), new ParseOptions { Syntax = RdfSyntax.NQuads });
        });

        Assert.True(
            perQuad <= WriterBytesPerQuadLimit,
            string.Create(CultureInfo.InvariantCulture, $"{perQuad} bytes per quad exceeds {WriterBytesPerQuadLimit}; fixed cost {fixedCost}"));
    }

    [Fact]
    public void the_package_reports_how_much_it_costs()
    {
        // Not an assertion: the measured figures, printed so that a run shows
        // what json-ld.md §7 states, and whether the limits above are honest.
        (long readerFixed, long readerPerQuad) = Profile(static large =>
        {
            JsonLdOptions options = default;
            JsonLdParser.Parse(large ? LargeDocument : SmallDocument, Count, in options);
        });

        (long expanderFixed, long expanderPerQuad) = Profile(static large =>
        {
            Output.Reset();
            JsonLdOptions options = default;
            JsonLdExpander.Expand(large ? LargeDocument : SmallDocument, Output, in options);
        });

        (long writerFixed, long writerPerQuad) = Profile(static large =>
        {
            Output.Reset();
            JsonLdWriteOptions write = new() { Indent = false };
            using JsonLdWriter writer = new(Output, in write);
            NQuadsParser.Parse(large ? LargeQuads : SmallQuads, (in QuadView quad) => writer.Write(in quad), new ParseOptions { Syntax = RdfSyntax.NQuads });
        });

        Assert.True(readerPerQuad >= 0 && writerPerQuad >= 0 && expanderPerQuad >= 0, string.Create(
            CultureInfo.InvariantCulture,
            $"reader: {readerPerQuad} bytes/quad, {readerFixed} fixed; expander: {expanderPerQuad} bytes/quad, {expanderFixed} fixed; writer: {writerPerQuad} bytes/quad, {writerFixed} fixed"));
    }
}
