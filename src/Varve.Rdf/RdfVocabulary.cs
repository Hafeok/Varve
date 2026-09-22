using System;

namespace Varve.Rdf;

/// <summary>
/// The three datatype IRIs the term model itself has to know about.
/// </summary>
/// <remarks>
/// Not a general vocabulary: everything else belongs to whoever uses it.
/// These three are here because RDF 1.1 Concepts §3.3 and RDF 1.2 Concepts
/// §3.3 define literals whose datatype is implied by their shape, so the model
/// cannot compare two literals for equality without them.
/// </remarks>
public static class RdfVocabulary
{
    /// <summary>The datatype of a literal written with no datatype and no language tag.</summary>
    public static ReadOnlySpan<byte> XsdString => "http://www.w3.org/2001/XMLSchema#string"u8;

    /// <summary>The datatype of a language-tagged literal with no base direction.</summary>
    public static ReadOnlySpan<byte> RdfLangString => "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8;

    /// <summary>The datatype of a directional language-tagged literal. RDF 1.2.</summary>
    public static ReadOnlySpan<byte> RdfDirLangString => "http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8;
}
