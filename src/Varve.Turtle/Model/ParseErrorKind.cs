// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Turtle.Model;

/// <summary>Why a line was rejected.</summary>
/// <remarks>
/// One value per way a line can be wrong, rather than a single "syntax error":
/// the caller of a recovering parse gets a list of these and needs to be able
/// to tell a damaged file from a file in the wrong format.
/// </remarks>
public enum ParseErrorKind : byte
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The line ended where a term or a <c>.</c> was expected.</summary>
    UnexpectedEnd,

    /// <summary>A subject must be an IRI or a blank node.</summary>
    ExpectedSubject,

    /// <summary>A predicate must be an IRI.</summary>
    ExpectedPredicate,

    /// <summary>An object must be an IRI, a blank node, a literal or a triple term.</summary>
    ExpectedObject,

    /// <summary>A graph label must be an IRI or a blank node.</summary>
    ExpectedGraphLabel,

    /// <summary>The statement did not end with a <c>.</c>.</summary>
    ExpectedDot,

    /// <summary>A fourth term appeared in an N-Triples document.</summary>
    GraphLabelNotAllowed,

    /// <summary>An <c>&lt;</c> was not closed by a <c>&gt;</c> on the same line.</summary>
    UnterminatedIri,

    /// <summary>A <c>"</c> was not closed by a <c>"</c> on the same line.</summary>
    UnterminatedLiteral,

    /// <summary>A triple term was not closed by <c>)&gt;&gt;</c>.</summary>
    UnterminatedTripleTerm,

    /// <summary>A character the IRIREF production excludes appeared raw in an IRI.</summary>
    InvalidIriCharacter,

    /// <summary>The IRI was well-formed as a reference but is not a well-formed IRI.</summary>
    InvalidIri,

    /// <summary>The IRI has no scheme. RDF terms need an absolute IRI.</summary>
    RelativeIri,

    /// <summary>A <c>\</c> was followed by something that is not an ECHAR or a UCHAR.</summary>
    InvalidEscape,

    /// <summary>A <c>\u</c> or <c>\U</c> was not followed by the right number of hex digits.</summary>
    InvalidUnicodeEscape,

    /// <summary>
    /// A <c>\u</c> named a surrogate code point that no second escape completed
    /// into a scalar value, which cannot be encoded as UTF-8.
    /// </summary>
    UnpairedSurrogate,

    /// <summary>A blank node label was empty or contained a character the production excludes.</summary>
    InvalidBlankNodeLabel,

    /// <summary>A language tag did not match the LANGTAG production.</summary>
    InvalidLanguageTag,

    /// <summary>A base direction was neither <c>ltr</c> nor <c>rtl</c>.</summary>
    InvalidBaseDirection,

    /// <summary>
    /// <c>rdf:langString</c> or <c>rdf:dirLangString</c> was written as an
    /// explicit datatype. Both are the datatype a language-tagged literal
    /// already has, and neither describes a literal without one.
    /// </summary>
    DatatypeRequiresLanguage,

    /// <summary>Something other than whitespace or a comment followed the <c>.</c>.</summary>
    TrailingContent,

    /// <summary>The input was not well-formed UTF-8.</summary>
    InvalidUtf8,

    /// <summary>A <c>@</c> directive that is neither <c>@prefix</c> nor <c>@base</c>.</summary>
    UnknownDirective,

    /// <summary>A prefix name that does not match PN_PREFIX.</summary>
    InvalidPrefix,

    /// <summary>A prefixed name whose prefix no directive has bound.</summary>
    UndeclaredPrefix,

    /// <summary>An IRI or a prefixed name was expected.</summary>
    ExpectedIri,

    /// <summary>A <c>%</c> in a local name was not followed by two hex digits.</summary>
    InvalidPercentEncoding,

    /// <summary>A numeric literal that matches none of INTEGER, DECIMAL or DOUBLE.</summary>
    InvalidNumber,

    /// <summary>A <c>[</c> was not closed by a <c>]</c>.</summary>
    UnterminatedBlankNodeList,
}
