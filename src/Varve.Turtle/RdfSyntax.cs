namespace Varve.Turtle;

/// <summary>Which syntax is being read or written.</summary>
/// <remarks>
/// <para>
/// N-Triples and N-Quads differ only in whether a fourth term is permitted
/// before the <c>.</c>, so one parser reads both and finding a graph label in
/// N-Triples is an error rather than an unknown production. Turtle and TriG
/// stand in the same relation to each other, and are read by a second parser
/// because their grammar is not line-based.
/// </para>
/// </remarks>
public enum RdfSyntax : byte
{
    /// <summary>RDF 1.1 N-Triples.</summary>
    NTriples,

    /// <summary>RDF 1.1 N-Quads.</summary>
    NQuads,

    /// <summary>RDF 1.1 Turtle.</summary>
    Turtle,

    /// <summary>RDF 1.1 TriG.</summary>
    TriG,
}

/// <summary>What the parser should do after an error has been reported.</summary>
public enum ErrorAction : byte
{
    /// <summary>End the parse. This is what happens with no error handler.</summary>
    Stop,

    /// <summary>Resume at the byte after the next end of line.</summary>
    Continue,
}
