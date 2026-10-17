// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>One <c>sht:Validate</c> case of the W3C SHACL test suites (ADR 0149).</summary>
internal sealed record ShaclEntry(
    string Suite,
    string TestIri,
    string Name,
    string DataGraph,
    string ShapesGraph,
    string File,
    bool ExpectsFailure,
    RdfTerm? ExpectedReport);

/// <summary>
/// The SHACL suites the harness runs (ADR 0149): the 2017 suite's
/// <c>core</c> and <c>sparql</c>, and SHACL 1.2's, from the
/// <c>w3c/data-shapes</c> submodule. Each test file is parsed with its
/// published IRI as base, so a case's IRI is the one the suite publishes.
/// </summary>
internal static class ShaclSuite
{
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Sht = "http://www.w3.org/ns/shacl-test#";
    private const string Published = "https://w3c.github.io/data-shapes/";

    internal static string Root { get; } = Path.Combine(TestData.RepositoryRootPath, "tests", "w3c", "data-shapes");

    internal static bool IsCheckedOut => File.Exists(Path.Combine(Root, "data-shapes-test-suite", "tests", "manifest.ttl"));

    /// <summary>The suites: an identifier, the directory under the submodule, and its sub-manifests.</summary>
    internal static readonly (string Id, string Directory, string[] Parts)[] Suites =
    [
        ("shacl10/core", "data-shapes-test-suite/tests", ["core"]),
        ("shacl10/sparql", "data-shapes-test-suite/tests", ["sparql"]),
        ("shacl12/core", "shacl12-test-suite/tests", ["core"]),
        ("shacl12/sparql", "shacl12-test-suite/tests", ["sparql"]),
    ];

    private static readonly Lazy<IReadOnlyList<ShaclEntry>> All = new(Load);

    internal static IReadOnlyList<ShaclEntry> Entries => All.Value;

    internal static readonly Lazy<Dictionary<string, ShaclEntry>> ByIri = new(() =>
    {
        Dictionary<string, ShaclEntry> map = new(StringComparer.Ordinal);
        foreach (ShaclEntry entry in Entries)
        {
            map[entry.TestIri] = entry;
        }

        return map;
    });

    /// <summary>The file behind a published IRI of the suite.</summary>
    internal static string PathOf(string iri)
    {
        if (!iri.StartsWith(Published, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Not a data-shapes IRI: " + iri);
        }

        return Path.Combine(Root, iri[Published.Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>A file's triples, parsed with its published IRI as base.</summary>
    internal static IReadOnlyList<DataQuad> Read(string iri)
    {
        List<DataQuad> quads = [];
        ParseResult result = TurtleParser.Parse(
            System.IO.File.ReadAllBytes(PathOf(iri)),
            (in QuadView quad) => quads.Add(new DataQuad(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), null)),
            new TurtleOptions { Syntax = RdfSyntax.Turtle, BaseIri = Encoding.UTF8.GetBytes(iri) });
        return result.Succeeded ? quads : throw new InvalidOperationException(iri + ": " + result.FirstError);
    }

    private static List<ShaclEntry> Load()
    {
        List<ShaclEntry> entries = [];
        if (!IsCheckedOut)
        {
            return entries;
        }

        foreach ((string id, string directory, string[] parts) in Suites)
        {
            foreach (string part in parts)
            {
                Walk(id, Published + directory + "/" + part + "/manifest.ttl", entries, new HashSet<string>(StringComparer.Ordinal));
            }
        }

        return entries;
    }

    private static void Walk(string suite, string manifestIri, List<ShaclEntry> entries, HashSet<string> visited)
    {
        if (!visited.Add(manifestIri))
        {
            return;
        }

        ManifestGraph graph = ManifestGraph.Load(PathOf(manifestIri), manifestIri);
        RdfTerm manifest = RdfTerm.Iri(Encoding.UTF8.GetBytes(manifestIri));
        foreach (RdfTerm include in graph.Objects(manifest, Mf + "include"))
        {
            Walk(suite, ManifestGraph.Text(include), entries, visited);
        }

        foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
        {
            foreach (RdfTerm item in graph.Collection(list))
            {
                if (graph.Object(item, Mf + "action") is { } action)
                {
                    AddEntry(suite, graph, manifestIri, item, action, entries);
                }
            }
        }
    }

    private static void AddEntry(string suite, ManifestGraph graph, string fileIri, RdfTerm item, RdfTerm action, List<ShaclEntry> entries)
    {
        bool validate = false;
        foreach (RdfTerm type in graph.Objects(item, "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
        {
            validate |= ManifestGraph.Text(type) == Sht + "Validate";
        }

        RdfTerm? status = graph.Object(item, Mf + "status");
        if (!validate || status is null || ManifestGraph.Text(status) != Sht + "approved")
        {
            return;
        }

        RdfTerm? data = graph.Object(action, Sht + "dataGraph");
        RdfTerm? shapes = graph.Object(action, Sht + "shapesGraph");
        RdfTerm? result = graph.Object(item, Mf + "result");
        if (data is null || shapes is null || result is null)
        {
            throw new InvalidOperationException("The SHACL case " + ManifestGraph.Text(item) + " lacks a data graph, shapes graph or result.");
        }

        bool failure = ManifestGraph.IsIri(result) && ManifestGraph.Text(result) == Sht + "Failure";
        string testIri = ManifestGraph.Text(item);
        string name = testIri[(testIri.LastIndexOf('/') + 1)..];
        entries.Add(new ShaclEntry(suite, testIri, name, ManifestGraph.Text(data), ManifestGraph.Text(shapes), fileIri, failure, failure ? null : result));
    }
}
