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

    /// <summary>RDF 1.2 N-Triples (ADR 0110): the edition decides how a surrogate escape is read.</summary>
    NTriples12,

    /// <summary>RDF 1.2 N-Quads.</summary>
    NQuads12,

    /// <summary>JSON-LD 1.1 (ADR 0112): the json-ld-api toRdf entries, whose expected datasets are N-Quads.</summary>
    JsonLd,

    /// <summary>RDF 1.2 Turtle.</summary>
    Turtle12,

    /// <summary>RDF 1.2 TriG.</summary>
    TriG12,

    /// <summary>RDF/XML, the rdf11 suite (ADR 0111): its results are RDF 1.1 N-Triples.</summary>
    RdfXml,

    /// <summary>RDF/XML, the rdf12 evaluation suite: its results are RDF 1.2 N-Triples.</summary>
    RdfXml12,
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
/// <param name="ManifestFile">The manifest on disk, under whichever submodule publishes the suite.</param>
internal sealed record ConformanceSuite(string Id, string ManifestPath, string BaseIri, RdfFormat Format, string ManifestFile)
{
    private const string PublishedRoot = "https://w3c.github.io/rdf-tests/";

    /// <summary>Where the json-ld-api suite publishes its tests, and the base of every entry's IRI.</summary>
    internal const string JsonLdPublishedRoot = TestData.JsonLdPublishedRoot;

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
        // entries pass.
        Suite("rdf12/n-triples", "rdf/rdf12/rdf-n-triples/syntax/manifest.ttl", RdfFormat.NTriples12),
        Suite("rdf12/n-quads", "rdf/rdf12/rdf-n-quads/syntax/manifest.ttl", RdfFormat.NQuads12),

        // RDF 1.2's canonical N-Triples and N-Quads (ADR 0061): each input
        // parsed and written canonically must give its expected file byte for
        // byte. The canonical form is the writer's, not RDFC-1.0's, which has
        // a suite of its own (CanonSuite).
        Suite("rdf12/n-triples-c14n", "rdf/rdf12/rdf-n-triples/c14n/manifest.ttl", RdfFormat.NTriples12),
        Suite("rdf12/n-quads-c14n", "rdf/rdf12/rdf-n-quads/c14n/manifest.ttl", RdfFormat.NQuads12),

        // Milestone 3b. Both carry evaluation entries as well as syntax ones,
        // which is why they could not be wired until the dataset comparison
        // existed.
        Suite("rdf11/turtle", "rdf/rdf11/rdf-turtle/manifest.ttl", RdfFormat.Turtle),
        Suite("rdf11/trig", "rdf/rdf11/rdf-trig/manifest.ttl", RdfFormat.TriG),

        // Milestone 6b (ADR 0110). RDF 1.2 Turtle and TriG: reified triples,
        // triple terms, annotations, reifiers, the version directive and
        // LANG_DIR. Each has a syntax and an evaluation manifest, wired as two
        // suites because the top-level manifest only includes them.
        Suite("rdf12/turtle-syntax", "rdf/rdf12/rdf-turtle/syntax/manifest.ttl", RdfFormat.Turtle12),
        Suite("rdf12/turtle-eval", "rdf/rdf12/rdf-turtle/eval/manifest.ttl", RdfFormat.Turtle12),
        Suite("rdf12/trig-syntax", "rdf/rdf12/rdf-trig/syntax/manifest.ttl", RdfFormat.TriG12),
        Suite("rdf12/trig-eval", "rdf/rdf12/rdf-trig/eval/manifest.ttl", RdfFormat.TriG12),

        // Milestone 6b (ADR 0111). RDF/XML: the rdf11 suite, and the rdf12
        // evaluation suite with rdf:parseType="Triple", rdf:annotation and
        // its:dir.
        Suite("rdf11/rdf-xml", "rdf/rdf11/rdf-xml/manifest.ttl", RdfFormat.RdfXml),
        Suite("rdf12/rdf-xml", "rdf/rdf12/rdf-xml/eval/manifest.ttl", RdfFormat.RdfXml12),

        // Milestone 6b (ADR 0112). JSON-LD 1.1's toRdf suite, from the
        // json-ld-api submodule: each entry runs under its stated options,
        // its expected dataset is N-Quads, and a negative entry names the
        // specification's error code.
        JsonLdSuite("json-ld/toRdf", "toRdf-manifest.jsonld", JsonLdOperation.ToRdf),
    ];

    /// <summary>
    /// The json-ld-api suites whose results are JSON documents rather than
    /// datasets: expand and fromRdf (ADR 0112). They are ratcheted by
    /// <c>JsonLdApiConformanceTests</c>, not by the syntax tests, and are
    /// not corpus for the chunk-boundary oracle, which wants quads.
    /// </summary>
    internal static IReadOnlyList<ConformanceSuite> JsonLdApi { get; } =
    [
        JsonLdSuite("json-ld/expand", "expand-manifest.jsonld", JsonLdOperation.Expand),
        JsonLdSuite("json-ld/fromRdf", "fromRdf-manifest.jsonld", JsonLdOperation.FromRdf),
    ];

    /// <summary>The operation a json-ld-api suite exercises; <c>null</c> for an rdf-tests suite.</summary>
    internal JsonLdOperation? Operation { get; init; }

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
        new(id, manifestPath, PublishedRoot + manifestPath, format, TestData.ResolveFromRoot(manifestPath));

    private static ConformanceSuite JsonLdSuite(string id, string manifestFile, JsonLdOperation operation) =>
        new(id, manifestFile, JsonLdPublishedRoot + manifestFile[..^".jsonld".Length], RdfFormat.JsonLd, System.IO.Path.Combine(TestData.JsonLdRoot, "tests", manifestFile))
        {
            Operation = operation,
        };

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
