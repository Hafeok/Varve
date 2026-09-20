namespace Varve.Conformance.Tests;

/// <summary>What a manifest entry asserts about one input file.</summary>
internal enum ExpectedOutcome
{
    /// <summary>The file is well-formed and must parse.</summary>
    Parses,

    /// <summary>The file is ill-formed and must be rejected.</summary>
    IsRejected,
}

/// <summary>
/// One entry from a W3C manifest, resolved to a file on disk.
/// </summary>
/// <param name="TestIri">
/// The entry's IRI, resolved against the suite's published base. This is the
/// test's identity: it names the case in the run, and it is what a line in
/// <c>baseline/passing.txt</c> refers to.
/// </param>
/// <param name="Name">The entry's <c>mf:name</c>.</param>
/// <param name="Comment">The entry's <c>rdfs:comment</c>, if it has one.</param>
/// <param name="ActionPath">The absolute path of the file to parse.</param>
/// <param name="Format">The syntax to parse it as.</param>
/// <param name="Expected">Whether parsing must succeed or must fail.</param>
internal sealed record ManifestEntry(
    string TestIri,
    string Name,
    string? Comment,
    string ActionPath,
    RdfFormat Format,
    ExpectedOutcome Expected);
