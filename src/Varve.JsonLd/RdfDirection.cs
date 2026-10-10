// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.JsonLd;

/// <summary>
/// How a value's <c>@direction</c> crosses into RDF and back (JSON-LD 1.1 API
/// §6.1 <c>rdfDirection</c>; ADR 0123, <c>DirectionDefaultIsNative</c>).
/// </summary>
public enum RdfDirection
{
    /// <summary>
    /// RDF 1.2's own form, the default: a value with <c>@language</c> and
    /// <c>@direction</c> is a directional language-tagged string
    /// (<c>"x"@en--rtl</c>), and one with a direction and no language is a
    /// plain string, because RDF 1.2 Concepts has no direction without a
    /// language. The specification's default is <see cref="None"/>; the
    /// departure is stated in <c>json-ld.md</c> §5.
    /// </summary>
    Native = 0,

    /// <summary>The specification's <c>i18n-datatype</c>: a literal typed <c>https://www.w3.org/ns/i18n#&lt;lang&gt;_&lt;dir&gt;</c>.</summary>
    I18nDatatype,

    /// <summary>The specification's <c>compound-literal</c>: a blank node with <c>rdf:value</c>, <c>rdf:language</c> and <c>rdf:direction</c>.</summary>
    CompoundLiteral,

    /// <summary>The specification's default, <c>null</c>: the direction is dropped on the way to RDF and never read on the way back.</summary>
    None,
}
