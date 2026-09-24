// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Varve.Sparql;

namespace Varve.Conformance.Tests;

/// <summary>
/// One W3C SPARQL syntax suite: where its manifest is, and the version its
/// cases are parsed at.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="ConformanceSuite"/> on purpose: that list is
/// about streaming document readers, with a format, a chunk-boundary oracle and
/// a pull-reader agreement test; a SPARQL suite is parsed whole, at a version,
/// and what stands in for the oracle is the UTF-8 / UTF-16 agreement
/// (<c>docs/spec/sparql-grammar.md</c> §8).
/// </remarks>
/// <param name="Id">A short, stable name used in the run summary.</param>
/// <param name="ManifestPath">The manifest, relative to the <c>rdf-tests</c> submodule root.</param>
/// <param name="BaseIri">The IRI the manifest is published at; entry IRIs resolve against it.</param>
/// <param name="Version">
/// The version the suite's cases are parsed at (<c>docs/spec/sparql-grammar.md</c>
/// §5). The 1.0 suites are parsed under 1.1, which has no separate 1.0 grammar
/// to offer; the 1.2 default is for API callers only.
/// </param>
internal sealed record SparqlSuite(string Id, string ManifestPath, string BaseIri, SparqlVersion Version)
{
    private const string PublishedRoot = "https://w3c.github.io/rdf-tests/";

    /// <summary>Every SPARQL suite wired in, each parsed at its own version.</summary>
    internal static IReadOnlyList<SparqlSuite> All { get; } =
    [
        // SPARQL 1.0 (data-r2), parsed under 1.1. A 1.0 negative case that 1.1
        // relaxed would be an exemption citing the 1.1 production; none is.
        Suite("sparql10/syntax-sparql1", "sparql/sparql10/syntax-sparql1/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql10/syntax-sparql2", "sparql/sparql10/syntax-sparql2/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql10/syntax-sparql3", "sparql/sparql10/syntax-sparql3/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql10/syntax-sparql4", "sparql/sparql10/syntax-sparql4/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql10/syntax-sparql5", "sparql/sparql10/syntax-sparql5/manifest.ttl", SparqlVersion.Sparql11),

        // SPARQL 1.1 Query, Update and Federated Query syntax.
        Suite("sparql11/syntax-query", "sparql/sparql11/syntax-query/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql11/syntax-update-1", "sparql/sparql11/syntax-update-1/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql11/syntax-update-2", "sparql/sparql11/syntax-update-2/manifest.ttl", SparqlVersion.Sparql11),
        Suite("sparql11/syntax-fed", "sparql/sparql11/syntax-fed/manifest.ttl", SparqlVersion.Sparql11),

        // SPARQL 1.2, Working Drafts of 2026 (docs/spec/sparql-grammar.md §1).
        // Wired and ratcheted, unlike the RDF 1.2 Turtle suites, because the
        // algebra carries 1.2 from the start and the ratchet absorbs churn.
        // Syntax entries only: the mixed manifests' evaluation entries wait
        // for the evaluator at milestone 5b.
        Suite("sparql12/syntax-triple-terms-positive", "sparql/sparql12/syntax-triple-terms-positive/manifest.ttl", SparqlVersion.Sparql12),
        Suite("sparql12/syntax-triple-terms-negative", "sparql/sparql12/syntax-triple-terms-negative/manifest.ttl", SparqlVersion.Sparql12),
        Suite("sparql12/syntax", "sparql/sparql12/syntax/manifest.ttl", SparqlVersion.Sparql12),
        Suite("sparql12/version", "sparql/sparql12/version/manifest.ttl", SparqlVersion.Sparql12),
        Suite("sparql12/codepoint-escapes", "sparql/sparql12/codepoint-escapes/manifest.ttl", SparqlVersion.Sparql12),
        Suite("sparql12/lang-basedir", "sparql/sparql12/lang-basedir/manifest.ttl", SparqlVersion.Sparql12),
    ];

    private static SparqlSuite Suite(string id, string manifestPath, SparqlVersion version) =>
        new(id, manifestPath, PublishedRoot + manifestPath, version);
}

/// <summary>One syntax case of a SPARQL suite.</summary>
internal sealed record SparqlManifestEntry(
    string TestIri,
    string Suite,
    string Name,
    string? Comment,
    string ActionPath,
    string ActionIri,
    bool IsUpdate,
    bool MustParse,
    SparqlVersion Version);

/// <summary>Every case of every wired SPARQL suite, read once.</summary>
internal static class SparqlCatalogue
{
    internal static IReadOnlyList<SparqlManifestEntry> Entries { get; } = ReadAll();

    internal static IReadOnlyDictionary<string, SparqlManifestEntry> ByIri { get; } = Index(Entries);

    internal static IReadOnlyList<SparqlManifestEntry> Of(SparqlSuite suite)
    {
        List<SparqlManifestEntry> entries = [];

        foreach (SparqlManifestEntry entry in Entries)
        {
            if (string.Equals(entry.Suite, suite.Id, StringComparison.Ordinal))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static List<SparqlManifestEntry> ReadAll()
    {
        if (!TestData.IsCheckedOut)
        {
            return [];
        }

        List<SparqlManifestEntry> all = [];

        foreach (SparqlSuite suite in SparqlSuite.All)
        {
            all.AddRange(ManifestReader.Read(suite));
        }

        return all;
    }

    private static ReadOnlyDictionary<string, SparqlManifestEntry> Index(IReadOnlyList<SparqlManifestEntry> entries)
    {
        Dictionary<string, SparqlManifestEntry> byIri = new(StringComparer.Ordinal);

        foreach (SparqlManifestEntry entry in entries)
        {
            byIri[entry.TestIri] = entry;
        }

        return new ReadOnlyDictionary<string, SparqlManifestEntry>(byIri);
    }
}
