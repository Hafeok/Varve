namespace Varve.Rdf;

/// <summary>The four kinds of RDF term.</summary>
public enum RdfTermKind : byte
{
    /// <summary>An absolute IRI, optionally with a fragment. Concepts §3.2.</summary>
    Iri = 0,

    /// <summary>An identifier with no meaning beyond identity. Concepts §3.3.</summary>
    BlankNode = 1,

    /// <summary>A lexical form with a datatype or a language tag. Concepts §3.3.</summary>
    Literal = 2,

    /// <summary>A triple used as a term. RDF 1.2.</summary>
    TripleTerm = 3,
}

/// <summary>
/// The base direction of a language-tagged string. RDF 1.2's *directional
/// language-tagged string*.
/// </summary>
/// <remarks>
/// RDF 1.2 Concepts is a Candidate Recommendation Snapshot of 07 April 2026 and
/// may still change; see <c>docs/spec/rdf-model.md</c> §1.
/// </remarks>
public enum TextDirection : byte
{
    /// <summary>No direction was given. The ordinary case, and what RDF 1.1 has.</summary>
    None = 0,

    /// <summary>Left to right.</summary>
    LeftToRight = 1,

    /// <summary>Right to left.</summary>
    RightToLeft = 2,
}
