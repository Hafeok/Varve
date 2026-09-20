using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>The syntax a manifest's entries are written in.</summary>
internal enum RdfFormat
{
    /// <summary>RDF 1.1 N-Triples.</summary>
    NTriples,

    /// <summary>RDF 1.1 N-Quads.</summary>
    NQuads,
}

/// <summary>
/// One W3C test suite: where its manifest is, and what format its entries are.
/// </summary>
/// <param name="Id">A short, stable name used in the run summary.</param>
/// <param name="ManifestPath">
/// The manifest, relative to the <c>rdf-tests</c> submodule root, with forward
/// slashes.
/// </param>
/// <param name="BaseIri">
/// The IRI the manifest is published at. Entry IRIs resolve against it, so it
/// is what gives a baseline line its identity, and it must not vary with where
/// the repository happens to be checked out.
/// </param>
/// <param name="Format">The syntax the entries are written in.</param>
internal sealed record ConformanceSuite(string Id, string ManifestPath, string BaseIri, RdfFormat Format)
{
    private const string PublishedRoot = "https://w3c.github.io/rdf-tests/";

    /// <summary>
    /// Every suite wired in. Adding one is a line here — which is the point of
    /// the shape, since the suites arrive over several milestones.
    /// </summary>
    internal static IReadOnlyList<ConformanceSuite> All { get; } =
    [
        Suite("rdf11/n-triples", "rdf/rdf11/rdf-n-triples/manifest.ttl", RdfFormat.NTriples),
        Suite("rdf11/n-quads", "rdf/rdf11/rdf-n-quads/manifest.ttl", RdfFormat.NQuads),

        // Milestone 5: rdf/rdf11/rdf-turtle, rdf/rdf11/rdf-trig.
        // Milestone 3+: rdf/rdf11/rdf-xml, and the RDF 1.2 suites as they
        // stabilise. Each is one line.
    ];

    private static ConformanceSuite Suite(string id, string manifestPath, RdfFormat format) =>
        new(id, manifestPath, PublishedRoot + manifestPath, format);
}
