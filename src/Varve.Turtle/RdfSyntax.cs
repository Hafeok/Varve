namespace Varve.Turtle;

/// <summary>Which of the two line-based syntaxes is being read or written.</summary>
/// <remarks>
/// One parser reads both. The only difference in the grammar is whether a
/// fourth term is permitted before the <c>.</c>; in N-Triples, finding one is
/// an error rather than an unknown production.
/// </remarks>
public enum RdfSyntax : byte
{
    /// <summary>RDF 1.1 N-Triples.</summary>
    NTriples,

    /// <summary>RDF 1.1 N-Quads.</summary>
    NQuads,
}

/// <summary>What the parser should do after an error has been reported.</summary>
public enum ErrorAction : byte
{
    /// <summary>End the parse. This is what happens with no error handler.</summary>
    Stop,

    /// <summary>Resume at the byte after the next end of line.</summary>
    Continue,
}
