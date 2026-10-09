// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Text;
using Varve.JsonLd;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Turtle;

namespace Varve.WasmSmoke;

/// <summary>
/// The milestone 6b syntaxes in the browser: RDF 1.2 Turtle (ADR 0110),
/// RDF/XML over System.Xml (ADR 0111) and JSON-LD over System.Text.Json (ADR
/// 0112), each read, written and read back. The bundle's System.Private.Xml
/// was already there for the SPARQL XML results format; what RDF/XML and
/// JSON-LD add is their own two assemblies, which ADR 0111 states.
/// </summary>
internal static partial class Smoke
{
    private static string Syntaxes() => Turtle12() + "; " + RdfXml() + "; " + JsonLd();

    private static string Turtle12()
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
            throw new InvalidOperationException(
                "rdf 1.2 turtle: " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture) + " quads, "
                + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + tripleTerms.ToString(CultureInfo.InvariantCulture)
                + " triple terms; first error " + parsed.FirstError.ToString());
        }

        Writer output = new();
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
            throw new InvalidOperationException("rdf 1.2 turtle: the round trip disagreed: " + written);
        }

        return string.Create(CultureInfo.InvariantCulture, $"rdf 1.2 turtle: {parsed.QuadCount} quads, VERSION declared, reparsed {reparsed.QuadCount}");
    }

    private static string RdfXml()
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
            throw new InvalidOperationException(
                "rdf/xml: " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture) + " triples, "
                + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + tripleTerms.ToString(CultureInfo.InvariantCulture)
                + " triple terms; error " + (parsed.Error?.ToString() ?? "none"));
        }

        Writer output = new();
        RdfXmlWriteOptions write = default;

        using (RdfXmlWriter writer = new(output, in write))
        {
            writer.DeclarePrefix("ex"u8, "http://example.org/"u8);
            RdfXmlParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in options);
        }

        RdfXmlParseResult reparsed = RdfXmlParser.Parse(output.Written.ToArray(), static (in QuadView _) => { }, in options);

        if (!reparsed.Succeeded || reparsed.QuadCount != parsed.QuadCount)
        {
            throw new InvalidOperationException("rdf/xml: the round trip disagreed: " + (reparsed.Error?.ToString() ?? reparsed.QuadCount.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Create(CultureInfo.InvariantCulture, $"rdf/xml: {parsed.QuadCount} triples, rewritten {output.Written.Length} bytes, reparsed {reparsed.QuadCount}");
    }

    private static string JsonLd()
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
            throw new InvalidOperationException(
                "json-ld: " + parsed.QuadCount.ToString(CultureInfo.InvariantCulture) + " quads, "
                + directional.ToString(CultureInfo.InvariantCulture) + " directional, " + jsonLiterals.ToString(CultureInfo.InvariantCulture)
                + " JSON literals; error " + (parsed.Error?.ToString() ?? "none"));
        }

        Writer expanded = new();
        JsonLdResult expansion = JsonLdExpander.Expand(document, expanded, in options);

        if (!expansion.Succeeded || expanded.Written.Length == 0)
        {
            throw new InvalidOperationException("json-ld: expansion failed: " + (expansion.Error?.ToString() ?? "empty"));
        }

        Writer output = new();
        JsonLdWriteOptions write = new() { Indent = false };

        using (JsonLdWriter writer = new(output, in write))
        {
            JsonLdParser.Parse(document, (in QuadView quad) => writer.Write(in quad), in options);
        }

        JsonLdResult reparsed = JsonLdParser.Parse(output.Written.ToArray(), static (in QuadView _) => { }, in options);

        if (!reparsed.Succeeded || reparsed.QuadCount != parsed.QuadCount)
        {
            throw new InvalidOperationException("json-ld: the round trip disagreed: " + (reparsed.Error?.ToString() ?? reparsed.QuadCount.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Create(CultureInfo.InvariantCulture, $"json-ld: {parsed.QuadCount} quads, expanded {expanded.Written.Length} bytes, rewritten {output.Written.Length} bytes, reparsed {reparsed.QuadCount}");
    }
}
