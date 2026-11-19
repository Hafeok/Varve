// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Text;
using Varve.Rdf;
using Varve.WebUi.Components.Atoms;

namespace Varve.WebUi.Components.Parts;

/// <summary>
/// An RDF term as a <c>TermChip</c> shows it, as a value, for components that
/// draw terms from data (a results table): the same compact IRIs, captions
/// and blank node labels as the <c>Term</c> part.
/// </summary>
public static class TermValues
{
    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    // The vocabularies every dataset shares; a dataset's own are not served.
    private static readonly ImmutableArray<(string Prefix, string Namespace)> Prefixes =
    [
        ("rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#"),
        ("rdfs", "http://www.w3.org/2000/01/rdf-schema#"),
        ("xsd", "http://www.w3.org/2001/XMLSchema#"),
        ("owl", "http://www.w3.org/2002/07/owl#"),
        ("sh", "http://www.w3.org/ns/shacl#"),
        ("skos", "http://www.w3.org/2004/02/skos/core#"),
        ("dct", "http://purl.org/dc/terms/"),
        ("schema", "https://schema.org/"),
        ("foaf", "http://xmlns.com/foaf/0.1/"),
        ("prov", "http://www.w3.org/ns/prov#"),
        ("varve", "https://w3id.org/varve/ns#"),
    ];

    /// <summary>
    /// <paramref name="term"/> as a chip's parameters. A triple term is its
    /// three terms written out, as a chip of its own kind, which TermChip does
    /// not yet show from a value (#377).
    /// </summary>
    public static TermValue Of(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        switch (term.Kind)
        {
            case RdfTermKind.Iri:
                string iri = Text(term.Lexical);
                string compact = Compact(iri);
                return new TermValue(TermKind.Iri, compact, Full: compact == iri ? null : iri);
            case RdfTermKind.Literal:
                return new TermValue(TermKind.Literal, Text(term.Lexical), LiteralType(term));
            case RdfTermKind.BlankNode:
                return new TermValue(TermKind.BlankNode, "_:" + Text(term.Lexical));
            default:
                return new TermValue(TermKind.Triple, "<< " + Of(term.Subject!).Text + " " + Of(term.Predicate!).Text + " " + Of(term.Object!).Text + " >>");
        }
    }

    /// <summary>A literal's caption: its language, or its datatype unless <c>xsd:string</c>.</summary>
    internal static string? LiteralType(RdfTerm literal)
    {
        string language = Text(literal.Language);

        if (language.Length > 0)
        {
            return "@" + language;
        }

        string datatype = Text(literal.DatatypeIri);
        return datatype.Length == 0 || datatype == XsdString ? null : Compact(datatype);
    }

    /// <summary>An IRI under a shared vocabulary as <c>prefix:local</c>; any other whole.</summary>
    internal static string Compact(string iri)
    {
        foreach ((string prefix, string ns) in Prefixes)
        {
            if (iri.StartsWith(ns, StringComparison.Ordinal) && iri.Length > ns.Length)
            {
                return prefix + ":" + iri[ns.Length..];
            }
        }

        return iri;
    }

    internal static string Text(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);
}
