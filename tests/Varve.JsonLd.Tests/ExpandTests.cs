// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text.Json;
using Xunit;
using static Varve.JsonLd.Tests.Harness;

namespace Varve.JsonLd.Tests;

/// <summary>The expand path: the expanded document as JSON (json-ld.md §2).</summary>
public class ExpandTests
{
    [Fact]
    public void expansion_resolves_terms_and_wraps_values()
    {
        string expanded = Expand("""
            {"@context": {"ex": "http://example.org/", "name": {"@id": "ex:name", "@language": "en"}}, "@id": "ex:s", "name": "x", "ex:n": 1}
            """);

        Assert.Equal(
            """[{"@id":"http://example.org/s","http://example.org/n":[{"@value":1}],"http://example.org/name":[{"@value":"x","@language":"en"}]}]""",
            expanded);
    }

    [Fact]
    public void nested_properties_are_lifted_and_scoped_contexts_apply()
    {
        string expanded = Expand("""
            {
              "@context": {"@vocab": "http://v/", "details": {"@id": "@nest", "@context": {"colour": "http://c/colour"}}},
              "@id": "http://a/s",
              "details": {"colour": "red", "size": 2}
            }
            """);

        using JsonDocument document = JsonDocument.Parse(expanded);
        JsonElement node = document.RootElement[0];
        Assert.Equal("red", node.GetProperty("http://c/colour")[0].GetProperty("@value").GetString());
        Assert.Equal(2, node.GetProperty("http://v/size")[0].GetProperty("@value").GetInt32());
        Assert.False(node.TryGetProperty("details", out _));
    }

    [Fact]
    public void a_free_floating_value_and_a_lone_id_are_dropped()
    {
        Assert.Equal("[]", Expand("""[{"@value": "x"}, {"@id": "http://a/s"}, "y", 1]"""));
    }

    [Fact]
    public void the_expand_context_option_applies_before_the_document()
    {
        ArrayBufferWriter output = new();
        JsonLdOptions options = new()
        {
            BaseIri = U("http://a/doc"),
            ExpandContext = U("{\"@context\": {\"p\": \"http://a/p\"}}"),
        };

        JsonLdResult result = JsonLdExpander.Expand(U("{\"@id\": \"s\", \"p\": \"x\"}"), output, in options);

        Assert.True(result.Succeeded, result.Error?.ToString());
        Assert.Equal("""[{"@id":"http://a/s","http://a/p":[{"@value":"x"}]}]""", S(output.Written));
    }

    [Fact]
    public void indentation_is_an_option_and_non_ascii_text_is_kept()
    {
        string expanded = Expand("{\"@id\": \"http://a/s\", \"http://a/p\": \"é中\"}", indent: true);

        Assert.Contains("\n", expanded, StringComparison.Ordinal);
        Assert.Contains("\"é中\"", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public void an_error_is_the_result_not_an_exception()
    {
        ArrayBufferWriter output = new();
        JsonLdOptions options = default;
        JsonLdResult result = JsonLdExpander.Expand(U("{\"@id\": [\"x\"]}"), output, in options);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid @id value: @id must be a string.", result.Error!.ToString());
    }
}
