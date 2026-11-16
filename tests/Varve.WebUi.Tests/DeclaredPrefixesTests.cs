// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.WebUi.Parts;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// One form per thing (review F13): a declaration's IRIs are compact only
/// with the prefixes the declaration itself defines.
/// </summary>
public class DeclaredPrefixesTests
{
    private const string Declaration = """
        @prefix varve: <https://w3id.org/varve/ns#> .
        PREFIX n: <https://notes.example/manage/>

        <https://w3id.org/varve/ns#manage/dataset/notes> {
          n:notes a varve:Dataset .
        }
        """;

    [Theory]
    [InlineData("https://w3id.org/varve/ns#manage/dataset/notes", "varve:manage/dataset/notes")]
    [InlineData("https://notes.example/manage/users", "n:users")]
    [InlineData("http://purl.org/dc/terms/title", null)]
    [InlineData("https://w3id.org/varve/ns#", null)]
    public void An_iri_is_compact_only_with_a_prefix_the_declaration_defines(string iri, string? compact) =>
        Assert.Equal(compact, DeclaredPrefixes.Of(Declaration).Compact(iri));

    [Fact]
    public void Without_a_declaration_every_iri_is_shown_in_full() =>
        Assert.Null(DeclaredPrefixes.None.Compact("https://w3id.org/varve/ns#manage/fleet"));
}
