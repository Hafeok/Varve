// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

public class TermTests
{
    private static byte[] U(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void iris_with_the_same_text_are_the_same_term()
    {
        Assert.Equal(RdfTerm.Iri(U("http://a/b")), RdfTerm.Iri(U("http://a/b")));
        Assert.Equal(
            RdfTerm.Iri(U("http://a/b")).GetHashCode(),
            RdfTerm.Iri(U("http://a/b")).GetHashCode());
    }

    [Fact]
    public void an_iri_and_a_blank_node_with_the_same_text_are_different_terms()
    {
        Assert.NotEqual(RdfTerm.Iri(U("x")), RdfTerm.BlankNode(U("x")));
    }

    [Fact]
    public void iri_case_is_significant_because_rdf_compares_byte_for_byte()
    {
        Assert.NotEqual(RdfTerm.Iri(U("http://A/b")), RdfTerm.Iri(U("http://a/b")));
    }

    [Fact]
    public void language_tags_compare_ignoring_case()
    {
        RdfTerm lower = RdfTerm.Literal(U("chat"), U("en-gb"));
        RdfTerm upper = RdfTerm.Literal(U("chat"), U("EN-GB"));

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void a_base_direction_makes_a_different_term()
    {
        Assert.NotEqual(
            RdfTerm.Literal(U("chat"), U("en")),
            RdfTerm.Literal(U("chat"), U("en"), TextDirection.LeftToRight));
    }

    [Fact]
    public void lexically_different_literals_of_one_value_are_different_terms()
    {
        RdfTerm integer = RdfTerm.Iri(U("http://www.w3.org/2001/XMLSchema#integer"));

        Assert.NotEqual(
            RdfTerm.Literal(U("1"), integer),
            RdfTerm.Literal(U("01"), integer));
    }

    [Fact]
    public void an_explicit_xsd_string_folds_to_the_shorthand()
    {
        RdfTerm spelt = RdfTerm.Literal(U("a"), RdfTerm.Iri(RdfVocabulary.XsdString));

        Assert.Null(spelt.Datatype);
        Assert.Equal(RdfTerm.Literal(U("a")), spelt);
    }

    [Fact]
    public void an_implied_datatype_is_still_reported()
    {
        Assert.True(RdfTerm.Literal(U("a")).DatatypeIri.SequenceEqual(RdfVocabulary.XsdString));
        Assert.True(RdfTerm.Literal(U("a"), U("en")).DatatypeIri.SequenceEqual(RdfVocabulary.RdfLangString));
        Assert.True(
            RdfTerm.Literal(U("a"), U("en"), TextDirection.RightToLeft)
                .DatatypeIri.SequenceEqual(RdfVocabulary.RdfDirLangString));
    }

    [Fact]
    public void a_non_literal_has_no_datatype_iri()
    {
        Assert.True(RdfTerm.Iri(U("http://a/b")).DatatypeIri.IsEmpty);
    }

    [Fact]
    public void triple_terms_compare_by_their_components()
    {
        static RdfTerm Make() => RdfTerm.TripleTerm(
            RdfTerm.Iri(U("http://a/s")),
            RdfTerm.Iri(U("http://a/p")),
            RdfTerm.Literal(U("o")));

        Assert.Equal(Make(), Make());
        Assert.Equal(Make().GetHashCode(), Make().GetHashCode());

        RdfTerm other = RdfTerm.TripleTerm(
            RdfTerm.Iri(U("http://a/s")),
            RdfTerm.Iri(U("http://a/p")),
            RdfTerm.Literal(U("other")));

        Assert.NotEqual(Make(), other);
    }

    [Fact]
    public void nested_triple_terms_compare_all_the_way_down()
    {
        static RdfTerm Inner(string o) => RdfTerm.TripleTerm(
            RdfTerm.Iri(U("http://a/s")),
            RdfTerm.Iri(U("http://a/p")),
            RdfTerm.Literal(U(o)));

        static RdfTerm Outer(string o) => RdfTerm.TripleTerm(
            Inner(o),
            RdfTerm.Iri(U("http://a/q")),
            RdfTerm.Literal(U("x")));

        Assert.Equal(Outer("a"), Outer("a"));
        Assert.NotEqual(Outer("a"), Outer("b"));
    }

    [Fact]
    public void an_empty_lexical_form_is_a_term()
    {
        Assert.Equal(RdfTerm.Literal(U("")), RdfTerm.Literal(U("")));
        Assert.NotEqual(RdfTerm.Literal(U("")), RdfTerm.Literal(U(" ")));
    }
}
