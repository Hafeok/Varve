// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json;
using Varve.Rdf;
using Xunit;
using static Varve.JsonLd.Tests.Harness;

namespace Varve.JsonLd.Tests;

/// <summary>The fromRdf writer: its shape, its options, and what it refuses by name (json-ld.md §6).</summary>
public class WriterTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    [Fact]
    public void subjects_and_members_are_sorted_and_a_named_graph_hangs_off_its_node()
    {
        string json = FromRdf(
            "<http://a/s> <http://a/p> \"x\" .\n<http://a/s> <" + Rdf + "type> <http://a/T> .\n<http://a/r> <http://a/p> <http://a/s> .\n<http://a/t> <http://a/q> \"y\" <http://a/g> .\n",
            new JsonLdWriteOptions { Indent = false });

        Assert.Equal(
            """[{"@graph":[{"@id":"http://a/t","http://a/q":[{"@value":"y"}]}],"@id":"http://a/g"},{"@id":"http://a/r","http://a/p":[{"@id":"http://a/s"}]},{"@id":"http://a/s","@type":["http://a/T"],"http://a/p":[{"@value":"x"}]}]""",
            json);
    }

    [Fact]
    public void a_well_formed_list_becomes_a_list_object()
    {
        string json = FromRdf(
            "<http://a/s> <http://a/p> _:l0 .\n_:l0 <" + Rdf + "first> \"x\" .\n_:l0 <" + Rdf + "rest> _:l1 .\n_:l1 <" + Rdf + "first> \"y\" .\n_:l1 <" + Rdf + "rest> <" + Rdf + "nil> .\n",
            new JsonLdWriteOptions { Indent = false });

        Assert.Equal("""[{"@id":"http://a/s","http://a/p":[{"@list":[{"@value":"x"},{"@value":"y"}]}]}]""", json);
    }

    [Fact]
    public void native_types_and_rdf_type_are_options()
    {
        string nquads = "<http://a/s> <http://a/p> \"1\"^^<" + Xsd + "integer> .\n<http://a/s> <http://a/p> \"1.5E0\"^^<" + Xsd + "double> .\n<http://a/s> <http://a/p> \"true\"^^<" + Xsd + "boolean> .\n<http://a/s> <" + Rdf + "type> <http://a/T> .\n";

        string plain = FromRdf(nquads, new JsonLdWriteOptions { Indent = false });
        Assert.Contains("{\"@value\":\"1\",\"@type\":\"" + Xsd + "integer\"}", plain, StringComparison.Ordinal);
        Assert.Contains("\"@type\":[\"http://a/T\"]", plain, StringComparison.Ordinal);

        string native = FromRdf(nquads, new JsonLdWriteOptions { Indent = false, UseNativeTypes = true, UseRdfType = true });
        Assert.Contains("{\"@value\":1}", native, StringComparison.Ordinal);
        Assert.Contains("{\"@value\":1.5}", native, StringComparison.Ordinal);
        Assert.Contains("{\"@value\":true}", native, StringComparison.Ordinal);
        Assert.Contains("\"" + Rdf + "type\":[{\"@id\":\"http://a/T\"}]", native, StringComparison.Ordinal);
    }

    [Fact]
    public void a_json_literal_is_parsed_back_into_json()
    {
        string json = FromRdf("<http://a/s> <http://a/p> \"{\\\"a\\\":[1,true]}\"^^<" + Rdf + "JSON> .\n", new JsonLdWriteOptions { Indent = false });
        Assert.Equal("""[{"@id":"http://a/s","http://a/p":[{"@value":{"a":[1,true]},"@type":"@json"}]}]""", json);
    }

    [Theory]
    [InlineData(RdfDirection.Native, "\"x\"@ar--rtl", "{\"@value\":\"x\",\"@language\":\"ar\",\"@direction\":\"rtl\"}")]
    [InlineData(RdfDirection.None, "\"x\"@ar--rtl", "{\"@value\":\"x\",\"@language\":\"ar\"}")]
    [InlineData(RdfDirection.I18nDatatype, "\"x\"^^<https://www.w3.org/ns/i18n#ar-EG_rtl>", "{\"@value\":\"x\",\"@language\":\"ar-eg\",\"@direction\":\"rtl\"}")]
    [InlineData(RdfDirection.None, "\"x\"^^<https://www.w3.org/ns/i18n#ar_rtl>", "{\"@value\":\"x\",\"@type\":\"https://www.w3.org/ns/i18n#ar_rtl\"}")]
    public void a_direction_is_read_out_of_rdf_by_the_option(RdfDirection direction, string literal, string expected)
    {
        string json = FromRdf("<http://a/s> <http://a/p> " + literal + " .\n", new JsonLdWriteOptions { Indent = false, RdfDirection = direction });
        Assert.Equal("[{\"@id\":\"http://a/s\",\"http://a/p\":[" + expected + "]}]", json);
    }

    [Fact]
    public void a_compound_literal_is_folded_only_under_its_option()
    {
        string nquads = "<http://a/s> <http://a/p> _:c .\n_:c <" + Rdf + "value> \"x\" .\n_:c <" + Rdf + "language> \"AR\" .\n_:c <" + Rdf + "direction> \"rtl\" .\n";

        string folded = FromRdf(nquads, new JsonLdWriteOptions { Indent = false, RdfDirection = RdfDirection.CompoundLiteral });
        Assert.Equal("""[{"@id":"http://a/s","http://a/p":[{"@value":"x","@language":"ar","@direction":"rtl"}]}]""", folded);

        string kept = FromRdf(nquads, new JsonLdWriteOptions { Indent = false });
        Assert.Contains("\"@id\":\"_:c\"", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void an_rdf_json_literal_that_is_not_json_is_refused_by_name()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FromRdf("<http://a/s> <http://a/p> \"{not json\"^^<" + Rdf + "JSON> .\n", default));
        Assert.StartsWith("invalid JSON literal:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void a_triple_term_is_refused_by_name()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => FromRdf("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <http://a/c> )>> .\n", default));
        Assert.Contains("triple term", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void the_document_is_written_once_and_the_default_is_indented()
    {
        ArrayBufferWriter output = new();
        JsonLdWriteOptions options = default;
        JsonLdWriter writer = new(output, in options);
        writer.Flush();
        writer.Flush();
        Assert.Equal("[]", S(output.Written));
        Assert.Throws<InvalidOperationException>(() => writer.Write(default(QuadView)));
        writer.Dispose();

        string indented = FromRdf("<http://a/s> <http://a/p> \"x\" .\n", default);
        Assert.Contains("\n", indented, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(indented);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    }
}
