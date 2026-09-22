// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// BCP 47 §2.1 well-formedness, which RDF 1.2 requires of a language tag in
/// prose rather than in its grammar.
/// </summary>
public class LanguageTagTests
{
    private static bool Ok(string tag) => LanguageTag.IsWellFormed(Encoding.UTF8.GetBytes(tag));

    [Theory]
    // language = 2*3ALPHA / 4ALPHA / 5*8ALPHA
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("fr")]
    [InlineData("ast")]
    [InlineData("qaaa")]
    [InlineData("abcdefgh")]
    // language-region
    [InlineData("en-GB")]
    [InlineData("en-uk")]
    [InlineData("es-419")]
    // language-script-region
    [InlineData("zh-Hans-CN")]
    // extlang
    [InlineData("zh-cmn-Hans-CN")]
    // variant
    [InlineData("de-DE-1901")]
    [InlineData("sl-rozaj-biske")]
    // extension
    [InlineData("en-US-u-islamcal")]
    [InlineData("de-DE-u-co-phonebk")]
    // private use
    [InlineData("x-whatever")]
    [InlineData("en-x-custom")]
    // grandfathered
    [InlineData("en-GB-oed")]
    [InlineData("i-klingon")]
    [InlineData("sgn-BE-FR")]
    [InlineData("zh-min-nan")]
    public void a_well_formed_tag_is_accepted(string tag) => Assert.True(Ok(tag), tag);

    [Theory]
    [InlineData("")]
    // The primary subtag is two to eight letters. This is the one the RDF 1.2
    // suite tests directly, and the one the grammar alone would accept.
    [InlineData("cantbethislong")]
    [InlineData("a")]
    [InlineData("abcdefghi")]
    [InlineData("1")]
    [InlineData("12")]
    // Empty subtags.
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--GB")]
    // A three-character region must be digits, and a three-letter subtag after
    // a region is neither an extlang nor a variant.
    [InlineData("en-GB-abc")]
    // A singleton with nothing after it.
    [InlineData("en-u")]
    [InlineData("en-x")]
    // Subtags longer than eight characters.
    [InlineData("en-GB-abcdefghi")]
    public void an_ill_formed_tag_is_rejected(string tag) => Assert.False(Ok(tag), tag);

    [Fact]
    public void only_two_base_directions_exist()
    {
        Assert.True(LanguageTag.IsBaseDirection("ltr"u8));
        Assert.True(LanguageTag.IsBaseDirection("rtl"u8));
        Assert.False(LanguageTag.IsBaseDirection("LTR"u8));
        Assert.False(LanguageTag.IsBaseDirection("unk"u8));
        Assert.False(LanguageTag.IsBaseDirection(default));
    }

    [Fact]
    public void a_term_cannot_be_built_with_an_ill_formed_tag()
    {
        Assert.Throws<ArgumentException>(
            () => RdfTerm.Literal("chat"u8, "cantbethislong"u8));
    }

    /// <summary>
    /// A model that can hold a literal no syntax can express is a model whose
    /// writer produces documents its own reader must reject. RDF 1.1 Concepts
    /// §3.3: these two datatypes belong to a language-tagged literal.
    /// </summary>
    [Fact]
    public void the_two_language_datatypes_cannot_be_given_explicitly()
    {
        Assert.Throws<ArgumentException>(
            () => RdfTerm.Literal("chat"u8, RdfTerm.Iri(RdfVocabulary.RdfLangString)));

        Assert.Throws<ArgumentException>(
            () => RdfTerm.Literal("chat"u8, RdfTerm.Iri(RdfVocabulary.RdfDirLangString)));
    }

    [Fact]
    public void the_language_overload_still_reports_those_datatypes()
    {
        Assert.True(RdfTerm.Literal("chat"u8, "en"u8).DatatypeIri.SequenceEqual(RdfVocabulary.RdfLangString));
        Assert.True(
            RdfTerm.Literal("chat"u8, "en"u8, TextDirection.RightToLeft)
                .DatatypeIri.SequenceEqual(RdfVocabulary.RdfDirLangString));
    }
}
