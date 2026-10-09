// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Turtle;

namespace Varve.AotSmoke;

/// <summary>
/// The milestone 6b syntaxes under Native AOT: RDF 1.2 Turtle (ADR 0110),
/// RDF/XML over System.Xml (ADR 0111) and JSON-LD over System.Text.Json (ADR
/// 0112), each read, written and read back. XmlReader and Utf8JsonReader
/// lean on things a trimmer can remove without a build warning, which is why
/// a real ILC run and not an analyzer is what proves them.
/// </summary>
internal static class Syntaxes
{
    internal static int Run()
    {
        int turtle = Turtle12();

        if (turtle != 0)
        {
            return turtle;
        }

        int xml = RdfXml();
        return xml != 0 ? xml : JsonLd();
    }

    private static int Turtle12()
    {
        byte[] document = Encoding.UTF8.GetBytes(
            "PREFIX : <http://example.org/>\n"
            + ":s :p :o {| :source :doc |} .\n"
            + "<< :s :q :o ~ :r >> :said \"x\"@ar--rtl .\n"
            + ":t :holds <<( :s :p :o )>> .\n");

        TurtleOptions read = new() { Syntax = RdfSyntax.Turtle };
        int directional = 0;
        int tripleTerms = 0;

        ParseResult parsed = TurtleParser.Parse(
            document,
            (in QuadView quad) =>
            {
                directional += quad.Object.Kind == RdfTermKind.Literal && quad.Object.Direction != TextDirection.None ? 1 : 0;
                tripleTerms += quad.Object.Kind == RdfTermKind.TripleTerm ? 1 : 0;
            },
            in read);

        if (!parsed.Succeeded || parsed.QuadCount != 6 || directional != 1 || tripleTerms != 3)
        {
            Console.Error.WriteLine(
                "aot-smoke: rdf 1.2 turtle gave " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture)
                + " quads, " + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + tripleTerms.ToString(CultureInfo.InvariantCulture)
                + " triple terms; first error " + parsed.FirstError.ToString());
            return 1;
        }

        Sink output = new();
        TurtleWriteOptions write = default;

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix(""u8, "http://example.org/"u8);
            TurtleParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in read);
        }

        string written = Encoding.UTF8.GetString(output.Written);
        ParseResult reparsed = TurtleParser.Parse(output.Written, static (in QuadView _) => { }, in read);

        if (!written.Contains("VERSION \"1.2\"", StringComparison.Ordinal) || reparsed.QuadCount != parsed.QuadCount)
        {
            Console.Error.WriteLine("aot-smoke: the rdf 1.2 turtle round trip disagreed: " + written);
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rdf 1.2 turtle: {parsed.QuadCount} quads, VERSION declared, reparsed {reparsed.QuadCount}"));
        return 0;
    }

    private static int RdfXml()
    {
        byte[] document = Encoding.UTF8.GetBytes(
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:ex=\"http://example.org/\" xmlns:its=\"http://www.w3.org/2005/11/its\" rdf:version=\"1.2\" xml:base=\"http://example.org/base/\">\n"
            + "  <ex:Thing rdf:about=\"s\" ex:n=\"1\">\n"
            + "    <ex:label xml:lang=\"ar\" its:dir=\"rtl\">x &amp; y</ex:label>\n"
            + "    <ex:items rdf:parseType=\"Collection\"><rdf:Description rdf:about=\"a\"/><rdf:Description rdf:about=\"b\"/></ex:items>\n"
            + "    <ex:says rdf:parseType=\"Triple\"><rdf:Description rdf:about=\"a\"><ex:p>q</ex:p></rdf:Description></ex:says>\n"
            + "    <ex:doc rdf:parseType=\"Literal\"><b>bold</b></ex:doc>\n"
            + "  </ex:Thing>\n"
            + "</rdf:RDF>\n");

        RdfXmlOptions options = new() { BaseIri = "http://example.org/doc"u8.ToArray() };
        int directional = 0;
        int tripleTerms = 0;

        RdfXmlParseResult parsed = RdfXmlParser.Parse(
            document,
            (in QuadView quad) =>
            {
                directional += quad.Object.Kind == RdfTermKind.Literal && quad.Object.Direction != TextDirection.None ? 1 : 0;
                tripleTerms += quad.Object.Kind == RdfTermKind.TripleTerm ? 1 : 0;
            },
            in options);

        if (!parsed.Succeeded || parsed.QuadCount != 10 || directional != 1 || tripleTerms != 1)
        {
            Console.Error.WriteLine(
                "aot-smoke: rdf/xml gave " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture) + " triples, "
                + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + tripleTerms.ToString(CultureInfo.InvariantCulture)
                + " triple terms; error " + (parsed.Error?.ToString() ?? "none"));
            return 1;
        }

        Sink output = new();
        RdfXmlWriteOptions write = default;

        using (RdfXmlWriter writer = new(output, in write))
        {
            writer.DeclarePrefix("ex"u8, "http://example.org/"u8);
            RdfXmlParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in options);
        }

        RdfXmlParseResult reparsed = RdfXmlParser.Parse(output.Written.ToArray(), static (in QuadView _) => { }, in options);

        if (!reparsed.Succeeded || reparsed.QuadCount != parsed.QuadCount)
        {
            Console.Error.WriteLine("aot-smoke: the rdf/xml round trip disagreed: " + (reparsed.Error?.ToString() ?? reparsed.QuadCount.ToString(CultureInfo.InvariantCulture)));
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rdf/xml: {parsed.QuadCount} triples, rewritten {output.Written.Length} bytes, reparsed {reparsed.QuadCount}"));
        return 0;
    }

    private static int JsonLd()
    {
        byte[] document = Encoding.UTF8.GetBytes(
            "{\"@context\": {\"@version\": 1.1, \"ex\": \"http://example.org/\", \"name\": {\"@id\": \"ex:name\", \"@language\": \"ar\", \"@direction\": \"rtl\"},"
            + " \"list\": {\"@id\": \"ex:list\", \"@container\": \"@list\"}, \"json\": {\"@id\": \"ex:json\", \"@type\": \"@json\"}},"
            + " \"@id\": \"ex:s\", \"@type\": \"ex:Thing\", \"name\": \"x\", \"list\": [1, 1.5, true], \"json\": {\"b\": [1], \"a\": \"\\u00e9\"}, \"ex:knows\": {\"@id\": \"ex:o\", \"ex:n\": 2}}");

        JsonLdOptions options = new() { BaseIri = "http://example.org/doc"u8.ToArray() };
        int directional = 0;
        int jsonLiterals = 0;

        JsonLdResult parsed = JsonLdParser.Parse(
            document,
            (in QuadView quad) =>
            {
                directional += quad.Object.Kind == RdfTermKind.Literal && quad.Object.Direction != TextDirection.None ? 1 : 0;
                jsonLiterals += quad.Object.Kind == RdfTermKind.Literal && quad.Object.HasDatatype && quad.Object.Datatype.EndsWith("#JSON"u8) ? 1 : 0;
            },
            in options);

        if (!parsed.Succeeded || parsed.QuadCount != 12 || directional != 1 || jsonLiterals != 1)
        {
            Console.Error.WriteLine(
                "aot-smoke: json-ld gave " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture) + " quads, "
                + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + jsonLiterals.ToString(CultureInfo.InvariantCulture)
                + " JSON literals; error " + (parsed.Error?.ToString() ?? "none"));
            return 1;
        }

        Sink expanded = new();
        JsonLdResult expansion = JsonLdExpander.Expand(document, expanded, in options);

        if (!expansion.Succeeded || expanded.Written.Length == 0)
        {
            Console.Error.WriteLine("aot-smoke: json-ld expansion failed: " + (expansion.Error?.ToString() ?? "empty"));
            return 1;
        }

        Sink output = new();
        JsonLdWriteOptions write = new() { Indent = false };

        using (JsonLdWriter writer = new(output, in write))
        {
            JsonLdParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in options);
        }

        JsonLdResult reparsed = JsonLdParser.Parse(output.Written.ToArray(), static (in QuadView _) => { }, in options);

        if (!reparsed.Succeeded || reparsed.QuadCount != parsed.QuadCount)
        {
            Console.Error.WriteLine("aot-smoke: the json-ld round trip disagreed: " + (reparsed.Error?.ToString() ?? reparsed.QuadCount.ToString(CultureInfo.InvariantCulture)) + "\n" + Encoding.UTF8.GetString(output.Written));
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"json-ld: {parsed.QuadCount} quads, expanded {expanded.Written.Length} bytes, rewritten {output.Written.Length} bytes, reparsed {reparsed.QuadCount}"));
        return 0;
    }

    private sealed class Sink : IBufferWriter<byte>
    {
        private byte[] _bytes = new byte[4096];
        private int _written;

        internal ReadOnlySpan<byte> Written => _bytes.AsSpan(0, _written);

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _bytes.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _bytes.AsSpan(_written);
        }

        private void Ensure(int sizeHint)
        {
            int needed = Math.Max(sizeHint, 1);

            if (_bytes.Length - _written < needed)
            {
                Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, _written + needed));
            }
        }
    }
}
