// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Varve.Rdf;
using Varve.Sparql;

namespace Varve.Conformance.Tests;

/// <summary>
/// A SPARQL query evaluation suite (<c>sparql-evaluation.md</c> §12.1): the
/// <c>QueryEvaluationTest</c> entries of one manifest, each run over both
/// subjects.
/// </summary>
internal sealed record EvaluationSuite(string Id, string ManifestPath, string BaseIri, SparqlVersion Version)
{
    internal const string PublishedRoot = "https://w3c.github.io/rdf-tests/";

    /// <summary>
    /// Every evaluation suite wired in. The SPARQL 1.0 directories the 1.1
    /// suite extends, parsed under 1.1; the 1.1 query directories of the
    /// official query manifest, the two result-format directories that hold
    /// query evaluation entries, and <c>service</c> through a test handler; and
    /// the SPARQL 1.2 directories, whose cases with RDF 1.2 Turtle data are
    /// blocked (<see cref="EvaluationCatalogue.IsBlocked"/>).
    /// </summary>
    internal static IReadOnlyList<EvaluationSuite> All { get; } =
    [
        .. Sparql10(
            "basic", "triple-match", "open-world", "algebra", "bnode-coreference", "optional", "optional-filter",
            "graph", "dataset", "type-promotion", "cast", "boolean-effective-value", "bound", "expr-builtin",
            "expr-ops", "expr-equals", "regex", "i18n", "construct", "ask", "distinct", "sort", "solution-seq",
            "reduced"),
        .. Sparql11(
            "aggregates", "bind", "bindings", "cast", "construct", "csv-tsv-res", "exists", "functions",
            "grouping", "json-res", "negation", "project-expression", "property-path", "service", "subquery"),
        .. Sparql12("codepoint-escapes", "eval-triple-terms", "expression", "grouping", "lang-basedir", "rdf11"),
    ];

    private static IEnumerable<EvaluationSuite> Sparql10(params string[] directories) =>
        directories.Select(d => Suite("sparql10/" + d, SparqlVersion.Sparql11));

    private static IEnumerable<EvaluationSuite> Sparql11(params string[] directories) =>
        directories.Select(d => Suite("sparql11/" + d, SparqlVersion.Sparql11));

    private static IEnumerable<EvaluationSuite> Sparql12(params string[] directories) =>
        directories.Select(d => Suite("sparql12/" + d, SparqlVersion.Sparql12));

    private static EvaluationSuite Suite(string id, SparqlVersion version)
    {
        string manifest = "sparql/" + id + "/manifest.ttl";
        return new EvaluationSuite(id, manifest, PublishedRoot + manifest, version);
    }

    /// <summary>A published IRI under the suites' root, as a path in the submodule.</summary>
    internal static string PathOf(string iri) =>
        iri.StartsWith(PublishedRoot, StringComparison.Ordinal)
            ? TestData.ResolveFromRoot(iri[PublishedRoot.Length..])
            : throw new InvalidOperationException("Not an IRI of the test suites: " + iri);
}

/// <summary>One query evaluation case.</summary>
internal sealed record EvaluationEntry(
    string TestIri,
    string Suite,
    string Name,
    SparqlVersion Version,
    string QueryIri,
    IReadOnlyList<string> Data,
    IReadOnlyList<string> GraphData,
    IReadOnlyList<(string Endpoint, IReadOnlyList<string> Data)> ServiceData,
    string? ResultIri,
    bool LaxCardinality);

/// <summary>The evaluation cases of every wired suite, read once.</summary>
internal static class EvaluationCatalogue
{
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Qt = "http://www.w3.org/2001/sw/DataAccess/tests/test-query#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly Lazy<IReadOnlyList<EvaluationEntry>> AllEntries = new(ReadAll);
    private static readonly Lazy<IReadOnlyDictionary<string, EvaluationEntry>> Index = new(() => AllEntries.Value.ToDictionary(e => e.TestIri, StringComparer.Ordinal));

    internal static IReadOnlyList<EvaluationEntry> Entries => AllEntries.Value;

    internal static IReadOnlyDictionary<string, EvaluationEntry> ByIri => Index.Value;

    internal static IReadOnlyList<EvaluationEntry> Of(EvaluationSuite suite) =>
        [.. Entries.Where(e => string.Equals(e.Suite, suite.Id, StringComparison.Ordinal))];

    /// <summary>
    /// A case is blocked when one of its data files is RDF 1.2 Turtle or TriG,
    /// which <c>turtle.md</c> §9 refuses: the loader reports it, and the guard
    /// pins how many there are and names the slice that unblocks them.
    /// </summary>
    internal static bool IsBlocked(EvaluationEntry entry) => EvaluationData.RefusedFiles(entry).Count > 0;

    /// <summary>The blocked cases, which the guard pins by count and by suite.</summary>
    internal static IReadOnlyList<EvaluationEntry> Blocked => [.. Entries.Where(IsBlocked)];

    private static List<EvaluationEntry> ReadAll()
    {
        List<EvaluationEntry> all = [];
        foreach (EvaluationSuite suite in EvaluationSuite.All)
        {
            all.AddRange(Read(suite));
        }

        return all;
    }

    private static List<EvaluationEntry> Read(EvaluationSuite suite)
    {
        string path = TestData.ResolveFromRoot(suite.ManifestPath);
        ManifestGraph graph = ManifestGraph.Load(path, suite.BaseIri);
        List<EvaluationEntry> result = [];
        RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();
        foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
        {
            foreach (RdfTerm entry in graph.Collection(list))
            {
                if (graph.Object(entry, Rdf + "type") is not { } type
                    || !string.Equals(ManifestGraph.Text(type), Mf + "QueryEvaluationTest", StringComparison.Ordinal)
                    || graph.Object(entry, Mf + "action") is not { } action)
                {
                    continue;
                }

                List<(string, IReadOnlyList<string>)> services = [];
                foreach (RdfTerm service in graph.Objects(action, Qt + "serviceData"))
                {
                    services.Add((
                        ManifestGraph.Text(graph.Object(service, Qt + "endpoint")!),
                        [.. graph.Objects(service, Qt + "data").Select(ManifestGraph.Text)]));
                }

                RdfTerm? cardinality = graph.Object(entry, Mf + "resultCardinality");
                string testIri = ManifestGraph.Text(entry);
                result.Add(new EvaluationEntry(
                    testIri,
                    suite.Id,
                    graph.Object(entry, Mf + "name") is { } name ? ManifestGraph.Text(name) : testIri,
                    suite.Version,
                    ManifestGraph.Text(graph.Object(action, Qt + "query")!),
                    [.. graph.Objects(action, Qt + "data").Select(ManifestGraph.Text)],
                    [.. graph.Objects(action, Qt + "graphData").Select(ManifestGraph.Text)],
                    services,
                    graph.Object(entry, Mf + "result") is { } expected ? ManifestGraph.Text(expected) : null,
                    cardinality is not null && ManifestGraph.Text(cardinality).EndsWith("LaxCardinality", StringComparison.Ordinal)));
            }
        }

        return result;
    }

    internal static bool Exists(string iri) => File.Exists(EvaluationSuite.PathOf(iri));
}
