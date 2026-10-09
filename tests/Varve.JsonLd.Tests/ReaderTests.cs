// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using Varve.JsonLd.Model;
using Varve.Rdf;
using Xunit;
using static Varve.JsonLd.Tests.Harness;

namespace Varve.JsonLd.Tests;

/// <summary>The toRdf path, document by document: what json-ld.md §2–§5 say a document becomes.</summary>
public class ReaderTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    [Fact]
    public void a_node_with_a_context_becomes_triples()
    {
        Read read = ToRdf("""
            {
              "@context": {"ex": "http://example.org/", "name": "ex:name", "knows": {"@id": "ex:knows", "@type": "@id"}},
              "@id": "ex:alice",
              "@type": "ex:Person",
              "name": "Alice",
              "knows": "ex:bob"
            }
            """);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(3, read.Result.QuadCount);
        Assert.Equal(
            [
                "<http://example.org/alice> <" + Rdf + "type> <http://example.org/Person>",
                "<http://example.org/alice> <http://example.org/knows> <http://example.org/bob>",
                "<http://example.org/alice> <http://example.org/name> \"Alice\"",
            ],
            read.Lines);
    }

    [Fact]
    public void blank_nodes_are_relabelled_in_order_of_first_appearance()
    {
        Read read = ToRdf("""
            [{"@id": "_:x", "http://example.org/p": [{"@id": "_:y"}, {"http://example.org/q": "v"}, {"@id": "_:x"}]}]
            """);

        Assert.Equal(
            [
                "_:b0 <http://example.org/p> _:b1",
                "_:b2 <http://example.org/q> \"v\"",
                "_:b0 <http://example.org/p> _:b2",
                "_:b0 <http://example.org/p> _:b0",
            ],
            read.Lines);
    }

    [Fact]
    public void a_list_is_a_chain_of_fresh_blank_nodes_and_the_empty_list_is_nil()
    {
        Read read = ToRdf("""
            {"@id": "http://a/s", "http://a/p": {"@list": ["x", "y"]}, "http://a/q": {"@list": []}}
            """);

        Assert.Equal(
            [
                "_:b0 <" + Rdf + "first> \"x\"",
                "_:b0 <" + Rdf + "rest> _:b1",
                "_:b1 <" + Rdf + "first> \"y\"",
                "_:b1 <" + Rdf + "rest> <" + Rdf + "nil>",
                "<http://a/s> <http://a/p> _:b0",
                "<http://a/s> <http://a/q> <" + Rdf + "nil>",
            ],
            read.Lines);
    }

    [Fact]
    public void a_graph_object_names_its_graph()
    {
        Read read = ToRdf("""
            {"@id": "http://a/g", "@graph": [{"@id": "http://a/s", "http://a/p": "x"}], "http://a/q": "y"}
            """);

        Assert.Equal(
            [
                "<http://a/s> <http://a/p> \"x\" <http://a/g>",
                "<http://a/g> <http://a/q> \"y\"",
            ],
            read.Lines);
    }

    [Fact]
    public void numbers_are_xsd_canonical_integers_or_doubles()
    {
        Read read = ToRdf("""
            {"@id": "http://a/s", "http://a/p": [1, 1.0, -0.0, 1.5, 1e21, 1E2, true, {"@value": 7, "@type": "http://www.w3.org/2001/XMLSchema#double"}]}
            """);

        Assert.Equal(
            [
                "<http://a/s> <http://a/p> \"1\"^^<" + Xsd + "integer>",
                "<http://a/s> <http://a/p> \"1\"^^<" + Xsd + "integer>",
                "<http://a/s> <http://a/p> \"0\"^^<" + Xsd + "integer>",
                "<http://a/s> <http://a/p> \"1.5E0\"^^<" + Xsd + "double>",
                "<http://a/s> <http://a/p> \"1.0E21\"^^<" + Xsd + "double>",
                "<http://a/s> <http://a/p> \"100\"^^<" + Xsd + "integer>",
                "<http://a/s> <http://a/p> \"true\"^^<" + Xsd + "boolean>",
                "<http://a/s> <http://a/p> \"7.0E0\"^^<" + Xsd + "double>",
            ],
            read.Lines);
    }

    [Fact]
    public void a_json_literal_is_canonical_per_rfc_8785()
    {
        Read read = ToRdf("""
            {"@context": {"e": {"@id": "http://a/e", "@type": "@json"}}, "@id": "http://a/s", "e": {"b": [1.0, "\u00e9", 1e21, 0.1], "a": {"z": null, "y": true}}}
            """);

        Assert.Equal(
            // The harness renders the lexical form as N-Quads, which spells é
            // as an escape; the literal itself holds the character (RFC 8785 §3.2.2.2).
            ["<http://a/s> <http://a/e> \"{\\\"a\\\":{\\\"y\\\":true,\\\"z\\\":null},\\\"b\\\":[1,\\\"\\u00E9\\\",1e+21,0.1]}\"^^<" + Rdf + "JSON>"],
            read.Lines);
    }

    [Theory]
    [InlineData(RdfDirection.Native, "\"x\"@ar--rtl", "\"y\"")]
    [InlineData(RdfDirection.None, "\"x\"@ar", "\"y\"")]
    [InlineData(RdfDirection.I18nDatatype, "\"x\"^^<https://www.w3.org/ns/i18n#ar_rtl>", "\"y\"^^<https://www.w3.org/ns/i18n#_rtl>")]
    public void a_direction_crosses_into_rdf_by_the_option(RdfDirection direction, string withLanguage, string withoutLanguage)
    {
        Read read = ToRdf("""
            {"@id": "http://a/s", "http://a/p": {"@value": "x", "@language": "AR", "@direction": "rtl"}, "http://a/q": {"@value": "y", "@direction": "rtl"}}
            """, direction: direction);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(["<http://a/s> <http://a/p> " + withLanguage, "<http://a/s> <http://a/q> " + withoutLanguage], read.Lines);
    }

    [Fact]
    public void a_compound_literal_is_a_blank_node_with_value_language_and_direction()
    {
        Read read = ToRdf("""
            {"@id": "http://a/s", "http://a/p": {"@value": "x", "@language": "ar", "@direction": "rtl"}}
            """, direction: RdfDirection.CompoundLiteral);

        Assert.Equal(
            [
                "_:b0 <" + Rdf + "value> \"x\"",
                "_:b0 <" + Rdf + "language> \"ar\"",
                "_:b0 <" + Rdf + "direction> \"rtl\"",
                "<http://a/s> <http://a/p> _:b0",
            ],
            read.Lines);
    }

    [Fact]
    public void what_is_not_well_formed_is_not_emitted()
    {
        // A relative @id, a blank node predicate, an ill-formed language tag:
        // each statement is left out and the rest stand (json-ld.md §4).
        Read read = ToRdf("""
            {"@id": "relative", "http://a/p": "x", "_:p": "y", "http://a/q": {"@value": "z", "@language": "a b"}, "http://a/r": {"@id": "http://a/o", "http://a/p": "w"}}
            """, baseIri: null);

        Assert.True(read.Result.Succeeded);
        Assert.Equal(["<http://a/o> <http://a/p> \"w\""], read.Lines);
    }

    [Theory]
    [InlineData("{\"@id\": 1}", JsonLdErrorCode.InvalidIdValue)]
    [InlineData("{\"@context\": {\"@vocab\": 1}}", JsonLdErrorCode.InvalidVocabMapping)]
    [InlineData("{\"@context\": {\"t\": {\"@id\": \"http://a/t\", \"@container\": \"@foo\"}}}", JsonLdErrorCode.InvalidContainerMapping)]
    [InlineData("{\"@context\": {\"@type\": \"http://a/t\"}}", JsonLdErrorCode.KeywordRedefinition)]
    [InlineData("{\"@context\": \"http://remote.example/context\"}", JsonLdErrorCode.LoadingRemoteContextFailed)]
    [InlineData("{\"@context\": {\"@protected\": true, \"t\": \"http://a/t\"}, \"http://a/p\": {\"@context\": {\"t\": \"http://a/u\"}, \"t\": \"x\"}}", JsonLdErrorCode.ProtectedTermRedefinition)]
    [InlineData("[", JsonLdErrorCode.LoadingDocumentFailed)]
    public void an_error_carries_the_specifications_code(string document, JsonLdErrorCode code)
    {
        Read read = ToRdf(document);

        Assert.False(read.Result.Succeeded);
        Assert.Equal(code, read.Result.Error!.Code);
        Assert.StartsWith(JsonLdErrorCodes.Text(code) + ": ", read.Result.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void a_remote_context_comes_through_the_callers_loader()
    {
        static bool Load(ReadOnlySpan<byte> iri, out ReadOnlyMemory<byte> document)
        {
            document = U("{\"@context\": {\"name\": \"http://a/name\"}}");
            return iri.SequenceEqual("http://remote.example/context"u8);
        }

        Read read = ToRdf("{\"@context\": \"http://remote.example/context\", \"@id\": \"http://a/s\", \"name\": \"x\"}", loader: Load);

        Assert.True(read.Result.Succeeded, read.Result.Error?.ToString());
        Assert.Equal(["<http://a/s> <http://a/name> \"x\""], read.Lines);
    }

    [Fact]
    public void a_split_sequence_gives_the_same_quads_as_the_whole()
    {
        byte[] document = U("{\"@context\": {\"ex\": \"http://example.org/\"}, \"@id\": \"ex:s\", \"ex:p\": [\"\\u00e9\", {\"@value\": 1.5}, {\"@id\": \"ex:o\"}]}");
        List<string> whole = [];
        JsonLdOptions options = default;
        JsonLdParser.Parse(document, (in QuadView quad) => whole.Add(Line(in quad)), in options);

        for (int at = 0; at <= document.Length; at++)
        {
            List<string> split = [];
            Segment first = new(document.AsMemory(0, at), 0);
            Segment second = first.Append(document.AsMemory(at));
            ReadOnlySequence<byte> sequence = new(first, 0, second, second.Memory.Length);
            JsonLdResult result = JsonLdParser.Parse(in sequence, (in QuadView quad) => split.Add(Line(in quad)), in options);
            Assert.True(result.Succeeded, "split at " + at + ": " + result.Error);
            Assert.Equal(whole, split);
        }
    }

    [Fact]
    public void the_quad_count_is_what_was_handed_out()
    {
        Read read = ToRdf("{\"@id\": \"http://a/s\", \"http://a/p\": [1, 2, 3]}");
        Assert.Equal(3, read.Result.QuadCount);
        Assert.Equal(3, read.Lines.Count);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal Segment Append(ReadOnlyMemory<byte> memory)
        {
            Segment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
