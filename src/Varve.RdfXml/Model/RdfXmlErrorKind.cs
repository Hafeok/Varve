// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.RdfXml.Model;

/// <summary>Why an RDF/XML document was rejected.</summary>
/// <remarks>
/// One value per way a document can be wrong at the RDF level, and one for
/// the XML level beneath it: a document the XML reader cannot read has no
/// RDF to be wrong about, and the two are different repairs.
/// </remarks>
public enum RdfXmlErrorKind : byte
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The XML itself is not well-formed, or uses a DTD, which is refused (ADR 0122).</summary>
    MalformedXml,

    /// <summary>The document ended before its RDF did, or holds no element at all.</summary>
    UnexpectedEnd,

    /// <summary>
    /// An element whose name RDF/XML forbids in that position: a core syntax
    /// term (<c>rdf:RDF</c>, <c>rdf:ID</c>, …) or <c>rdf:li</c> as a node
    /// element, <c>rdf:Description</c> as a property element, or one of the
    /// withdrawn terms <c>rdf:aboutEach</c>, <c>rdf:aboutEachPrefix</c> and
    /// <c>rdf:bagID</c> (§6.2.4 and §6.2.5 of RDF 1.2 XML).
    /// </summary>
    ForbiddenElementName,

    /// <summary>
    /// An attribute RDF/XML forbids where it stands: a core syntax term as a
    /// property attribute, <c>rdf:li</c> as an attribute, a withdrawn term, or
    /// an attribute the property element's form does not admit.
    /// </summary>
    ForbiddenAttribute,

    /// <summary>More than one of <c>rdf:ID</c>, <c>rdf:about</c> and <c>rdf:nodeID</c> on one element, or <c>rdf:resource</c> beside <c>rdf:nodeID</c>.</summary>
    ConflictingAttributes,

    /// <summary>An <c>rdf:ID</c> or <c>rdf:nodeID</c> value that is not an XML <c>NCName</c>.</summary>
    InvalidName,

    /// <summary>The same <c>rdf:ID</c> resolved against the same base twice in one document (§5.3 of RDF 1.1 XML).</summary>
    DuplicateId,

    /// <summary>A relative IRI with no base in scope.</summary>
    RelativeIri,

    /// <summary>An IRI that is not an RFC 3987 IRI.</summary>
    InvalidIri,

    /// <summary>Text that is not whitespace where only elements may stand: inside <c>rdf:RDF</c>, a node element, or <c>rdf:parseType="Resource"</c> content.</summary>
    UnexpectedText,

    /// <summary>A property element with a node element child and other content, or with more than one node element.</summary>
    UnexpectedContent,

    /// <summary>A language tag that is not well-formed (BCP 47).</summary>
    InvalidLanguageTag,

    /// <summary>An <c>its:dir</c> value other than <c>ltr</c> or <c>rtl</c>.</summary>
    InvalidBaseDirection,

    /// <summary>An <c>rdf:parseType="Triple"</c> element whose content is not exactly one node element describing exactly one triple (RDF 1.2 XML §6.2.19).</summary>
    InvalidTripleTerm,
}
