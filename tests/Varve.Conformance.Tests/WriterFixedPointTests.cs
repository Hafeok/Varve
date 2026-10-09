// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.JsonLd;
using Varve.RdfXml;
using Varve.Turtle;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// Writing, reading back and writing again produces the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline of stages reads and writes the same graph repeatedly, so a
/// transformation that renames a blank node on every pass renames every node
/// in the graph every time — the diff between two stages is then noise rather
/// than the change. The property that stops that is a fixed point: whatever
/// the first write produces, a second one reproduces exactly.
/// </para>
/// <para>
/// It is the writer's half of the naming contract in <c>turtle.md</c> §4, and
/// it holds because the writer emits every blank node as an explicit label and
/// a document's own label passes through the reader unchanged. It does not
/// claim the <em>first</em> write preserves the input's labels: a document that
/// uses generated-form labels beside anonymous nodes may have some renamed
/// once, which §4 states and this measures by starting from the first write
/// rather than from the file.
/// </para>
/// <para>
/// Unlike the chunk-boundary oracle this goes at <c>Varve.Turtle</c> directly
/// rather than through <see cref="IParserSubject"/>: it is a property of this
/// writer, not a question one could ask of any implementation.
/// </para>
/// </remarks>
public class WriterFixedPointTests
{
    /// <summary>
    /// Declared for both writes, so that byte-identity is a real comparison and
    /// prefix compaction is exercised rather than avoided.
    /// </summary>
    private static readonly (string Prefix, string Iri)[] Prefixes =
    [
        ("e", "http://example/"),
        ("a", "http://a.example/"),
        ("x", "http://www.w3.org/2001/XMLSchema#"),
    ];

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (ManifestEntry entry in OracleCatalogue.Entries)
        {
            if (entry.Expected != ExpectedOutcome.IsRejected)
            {
                yield return new TheoryDataRow<string>(entry.TestIri)
                {
                    TestDisplayName = entry.TestIri,
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void writing_what_was_written_reproduces_it(string testIri)
    {
        ManifestEntry entry = OracleCatalogue.ByIri[testIri];
        byte[] source = File.ReadAllBytes(entry.ActionPath);

        if (entry.Format == RdfFormat.JsonLd)
        {
            // The round trip of the fromRdf writer (json-ld.md §6): the quads
            // of a toRdf input, written as expanded JSON-LD, read back as the
            // same dataset; and written again from that reading, the same
            // bytes. A toRdf input this reader rejects is the suite's business.
            if (!TryReadJsonLd(source, entry, out List<string> quads))
            {
                return;
            }

            byte[] firstJson = WriteJsonLd(quads);
            Assert.True(TryReadJsonLdBytes(firstJson, entry.ActionIri, out List<string> again), Text(firstJson));
            IsomorphismResult comparison = Isomorphism.Compare(Parsed(quads), Parsed(again));
            Assert.True(comparison.IsSame, entry.TestIri + ": the dataset written as JSON-LD read back differently — " + comparison.Reason + "\n" + Text(firstJson));
            // Up to blank node labels, which toRdf renumbers (ADR 0112).
            byte[] secondJson = WriteJsonLd(again);
            Assert.Equal(NumberBlankLabels(Text(firstJson)), NumberBlankLabels(Text(secondJson)));
            return;
        }

        if (VarveParserSubject.IsRdfXml(entry.Format))
        {
            // The same property of the RDF/XML writer (rdf-xml.md §6): read,
            // write, read, write, same bytes. A document the writer refuses —
            // a predicate with no NCName suffix — is not this property's
            // business either, and the suites hold none.
            if (!TryWriteXml(source, entry.ActionIri, out byte[] firstXml))
            {
                return;
            }

            Assert.True(TryWriteXml(firstXml, entry.ActionIri, out byte[] secondXml), Text(firstXml));
            Assert.Equal(Text(firstXml), Text(secondXml));
            return;
        }

        RdfSyntax syntax = Syntax(entry.Format);

        if (!TryWrite(source, syntax, out byte[] first))
        {
            // A positive-syntax entry this reader rejects is a conformance
            // failure the suites report; it is not this property's business.
            return;
        }

        Assert.True(TryWrite(first, syntax, out byte[] second), Text(first));
        Assert.Equal(Text(first), Text(second));
    }

    /// <summary>
    /// The syntax to read and write each format as. N-Triples is a subset of
    /// Turtle and N-Quads of TriG, so their inputs are corpus for this property
    /// too rather than a separate path.
    /// </summary>
    private static RdfSyntax Syntax(RdfFormat format) => format switch
    {
        RdfFormat.TriG or RdfFormat.NQuads or RdfFormat.TriG12 or RdfFormat.NQuads12 => RdfSyntax.TriG,
        _ => RdfSyntax.Turtle,
    };

    /// <summary>
    /// Parses <paramref name="source"/> and writes it out, or reports that it
    /// was rejected.
    /// </summary>
    private static bool TryWrite(byte[] source, RdfSyntax syntax, out byte[] written)
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions writeOptions = new() { Syntax = syntax };
        ParseResult result;

        using (TurtleWriter writer = new(output, in writeOptions))
        {
            foreach ((string prefix, string iri) in Prefixes)
            {
                writer.DeclarePrefix(Encoding.UTF8.GetBytes(prefix), Encoding.UTF8.GetBytes(iri));
            }

            TurtleOptions readOptions = new()
            {
                Syntax = syntax == RdfSyntax.TriG ? RdfSyntax.TriG : RdfSyntax.Turtle,
                BaseIri = Encoding.UTF8.GetBytes("http://example/base"),

                // The widest reading: an rdf11 input may spell a character as a
                // surrogate pair, which the writer then writes as the
                // character, so the second read needs no edition at all.
                Version = RdfVersion.Rdf11,
            };

            result = TurtleParser.Parse(
                source, (in QuadView quad) => writer.Write(in quad), in readOptions);
        }

        written = output.Written.ToArray();
        return result.Succeeded;
    }

    private static string NumberBlankLabels(string json)
    {
        Dictionary<string, int> seen = new(System.StringComparer.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(json, "_:[A-Za-z0-9]+", m =>
        {
            if (!seen.TryGetValue(m.Value, out int index))
            {
                index = seen.Count;
                seen[m.Value] = index;
            }

            return "_:n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
    }

    private static bool TryReadJsonLd(byte[] source, ManifestEntry entry, out List<string> quads)
    {
        List<string> lines = [];
        JsonLdResult result = JsonLdParser.Parse(source, (in QuadView quad) => lines.Add(NQuadsLine(in quad)), VarveParserSubject.JsonLdOptionsFor(entry.ActionIri, entry.JsonLd));
        quads = lines;
        return result.Succeeded;
    }

    private static bool TryReadJsonLdBytes(byte[] source, string baseIri, out List<string> quads)
    {
        List<string> lines = [];
        JsonLdResult result = JsonLdParser.Parse(source, (in QuadView quad) => lines.Add(NQuadsLine(in quad)), new JsonLdOptions { BaseIri = Encoding.UTF8.GetBytes(baseIri) });
        quads = lines;
        return result.Succeeded;
    }

    /// <summary>Writes N-Quads lines as expanded JSON-LD through <see cref="JsonLdWriter"/>.</summary>
    private static byte[] WriteJsonLd(List<string> quads)
    {
        ArrayBufferWriter output = new();
        JsonLdWriteOptions options = default;

        using (JsonLdWriter writer = new(output, in options))
        {
            byte[] document = Encoding.UTF8.GetBytes(string.Join('\n', quads) + "\n");
            ParseResult parsed = NQuadsParser.Parse(document, (in QuadView quad) => writer.Write(in quad), new ParseOptions { Syntax = RdfSyntax.NQuads, Version = RdfVersion.Rdf11 });
            Assert.True(parsed.Succeeded, parsed.FirstError.ToString());
        }

        return output.Written.ToArray();
    }

    private static List<ParsedQuad> Parsed(List<string> quads)
    {
        List<ParsedQuad> parsed = [];
        byte[] document = Encoding.UTF8.GetBytes(string.Join('\n', quads) + "\n");
        WriteOptions canonical = new() { Syntax = RdfSyntax.NQuads };
        NQuadsParser.Parse(
            document,
            (in QuadView quad) => parsed.Add(new ParsedQuad(Term(quad.Subject, canonical), Term(quad.Predicate, canonical), Term(quad.Object, canonical), quad.HasGraph ? Term(quad.Graph, canonical) : null)),
            new ParseOptions { Syntax = RdfSyntax.NQuads, Version = RdfVersion.Rdf11 });
        return parsed;
    }

    private static string NQuadsLine(in QuadView quad)
    {
        WriteOptions options = new() { Syntax = RdfSyntax.NQuads };
        string line = Term(quad.Subject, options) + " " + Term(quad.Predicate, options) + " " + Term(quad.Object, options);
        return (quad.HasGraph ? line + " " + Term(quad.Graph, options) : line) + " .";
    }

    private static string Term(in RdfTermView view, in WriteOptions options)
    {
        RdfTerm term = view.Materialise();
        byte[] buffer = new byte[256];

        while (!NQuadsWriter.TryWriteTerm(term, buffer, out _, in options))
        {
            buffer = new byte[buffer.Length * 2];
        }

        NQuadsWriter.TryWriteTerm(term, buffer, out int written, in options);
        return Encoding.UTF8.GetString(buffer, 0, written);
    }

    private static bool TryWriteXml(byte[] source, string baseIri, out byte[] written)
    {
        ArrayBufferWriter output = new();
        RdfXmlWriteOptions writeOptions = default;
        RdfXmlParseResult result;

        using (RdfXmlWriter writer = new(output, in writeOptions))
        {
            foreach ((string prefix, string iri) in Prefixes)
            {
                writer.DeclarePrefix(Encoding.UTF8.GetBytes(prefix), Encoding.UTF8.GetBytes(iri));
            }

            result = RdfXmlParser.Parse(
                source, (in QuadView quad) => writer.Write(in quad), new RdfXmlOptions { BaseIri = Encoding.UTF8.GetBytes(baseIri) });
        }

        written = output.Written.ToArray();
        return result.Succeeded;
    }

    private static string Text(byte[] utf8) => Encoding.UTF8.GetString(utf8);
}
