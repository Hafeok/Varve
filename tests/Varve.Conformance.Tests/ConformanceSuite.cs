// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Conformance.Tests;

/// <summary>The syntax a manifest's entries are written in.</summary>
internal enum RdfFormat
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

        // RDF 1.2. The reader and writer carry base direction and triple terms,
        // so by our own rule those features are not done until their manifest
        // entries pass. Syntax only: the sibling c14n manifests are RDFC-1.0,
        // which is milestone 3b's.
        Suite("rdf12/n-triples", "rdf/rdf12/rdf-n-triples/syntax/manifest.ttl", RdfFormat.NTriples),
        Suite("rdf12/n-quads", "rdf/rdf12/rdf-n-quads/syntax/manifest.ttl", RdfFormat.NQuads),

        // Milestone 3b. Both carry evaluation entries as well as syntax ones,
        // which is why they could not be wired until the dataset comparison
        // existed.
        Suite("rdf11/turtle", "rdf/rdf11/rdf-turtle/manifest.ttl", RdfFormat.Turtle),
        Suite("rdf11/trig", "rdf/rdf11/rdf-trig/manifest.ttl", RdfFormat.TriG),

        // Later: rdf/rdf11/rdf-xml, the RDF 1.2 Turtle and TriG suites, and the
        // c14n manifests with RDFC-1.0. Each is one line.
    ];

    /// <summary>
    /// Suites whose inputs are read but whose results are not yet ratcheted.
    /// </summary>
    /// <remarks>
    /// The chunk-boundary oracle asks whether a subject gives the same answer
    /// however the input arrives. That is a question about self-consistency, so
    /// it needs a corpus and not a verdict, and it can therefore run over a
    /// suite before <see cref="All"/> claims anything about conformance to it.
    /// When a suite moves into <see cref="All"/> its line here is deleted;
    /// <see cref="OracleCorpus"/> unions the two and keeps the first of a
    /// duplicated id, so moving it is one edit and not two.
    /// </remarks>
    internal static IReadOnlyList<ConformanceSuite> NotYetRatcheted { get; } = [];

    /// <summary>Every suite the oracle reads: the ratcheted ones and the rest.</summary>
    internal static IReadOnlyList<ConformanceSuite> OracleCorpus { get; } = Union(All, NotYetRatcheted);

    private static ConformanceSuite Suite(string id, string manifestPath, RdfFormat format) =>
        new(id, manifestPath, PublishedRoot + manifestPath, format);

    private static List<ConformanceSuite> Union(
        IReadOnlyList<ConformanceSuite> first, IReadOnlyList<ConformanceSuite> second)
    {
        List<ConformanceSuite> all = [.. first];

        foreach (ConformanceSuite suite in second)
        {
            if (!all.Exists(s => string.Equals(s.Id, suite.Id, System.StringComparison.Ordinal)))
            {
                all.Add(suite);
            }
        }

        return all;
    }
}
