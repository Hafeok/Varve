// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.WebUi.Components.Parts;

/// <summary>
/// How a term is written on a chip, shared by the console's <c>Term</c> part
/// and the explorer's term values: the vocabularies every dataset shares as
/// prefixes, a literal's caption, and UTF-8 as text.
/// </summary>
[Contract(typeof(TheWebUisPrivateAssemblies.EagerOnlyWhenAConsolePageUses), Role = "what the console's Term part uses of a term's text, kept eager; the explorer's term values build on it")]
public static class TermText
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

    /// <summary>A literal's caption: its language, or its datatype unless <c>xsd:string</c>.</summary>
    public static string? LiteralType(RdfTerm literal)
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
    public static string Compact(string iri)
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

    /// <summary>UTF-8 bytes as a string.</summary>
    public static string Text(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8);
}
