// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>What a canonicalisation case asks.</summary>
internal enum CanonKind
{
    /// <summary><c>rdfc:RDFC10EvalTest</c>: the canonical form, byte for byte.</summary>
    Eval,

    /// <summary><c>rdfc:RDFC10MapTest</c>: the issued identifiers map.</summary>
    Map,

    /// <summary><c>rdfc:RDFC10NegativeEvalTest</c>: canonicalisation must stop at its limit.</summary>
    Negative,
}

/// <summary>One case of the <c>w3c/rdf-canon</c> suite.</summary>
internal sealed record CanonEntry(string TestIri, string Name, CanonKind Kind, string Action, string? Result, HashAlgorithmName Hash);

/// <summary>The RDFC-1.0 suite (<c>rdf-canon.md</c> §7), read once from the second submodule.</summary>
internal static class CanonCatalogue
{
    /// <summary>The base the manifest's relative IRIs resolve against: where it is published.</summary>
    internal const string BaseIri = "https://w3c.github.io/rdf-canon/tests/manifest.ttl";

    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Rdfc = "https://w3c.github.io/rdf-canon/tests/vocab#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly Lazy<IReadOnlyList<CanonEntry>> AllEntries = new(ReadAll);
    private static readonly Lazy<IReadOnlyDictionary<string, CanonEntry>> Index = new(() => AllEntries.Value.ToDictionary(e => e.TestIri, StringComparer.Ordinal));

    internal static IReadOnlyList<CanonEntry> Entries => AllEntries.Value;

    internal static IReadOnlyDictionary<string, CanonEntry> ByIri => Index.Value;

    internal static string ManifestPath => Path.Combine(TestData.RdfCanonRoot, "tests", "manifest.ttl");

    /// <summary>A published IRI under the suite's directory, as a path in the submodule.</summary>
    internal static string PathOf(string iri)
    {
        const string root = "https://w3c.github.io/rdf-canon/tests/";
        return iri.StartsWith(root, StringComparison.Ordinal)
            ? Path.Combine(TestData.RdfCanonRoot, "tests", iri[root.Length..].Replace('/', Path.DirectorySeparatorChar))
            : throw new InvalidOperationException("Not an IRI of the canonicalisation suite: " + iri);
    }

    private static List<CanonEntry> ReadAll()
    {
        ManifestGraph graph = ManifestGraph.Load(ManifestPath, BaseIri);
        RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();
        List<CanonEntry> all = [];

        foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
        {
            foreach (RdfTerm entry in graph.Collection(list))
            {
                string type = ManifestGraph.Text(graph.Object(entry, Rdf + "type")!);
                CanonKind kind = type switch
                {
                    Rdfc + "RDFC10EvalTest" => CanonKind.Eval,
                    Rdfc + "RDFC10MapTest" => CanonKind.Map,
                    Rdfc + "RDFC10NegativeEvalTest" => CanonKind.Negative,
                    _ => throw new InvalidOperationException("An rdf-canon test type this harness does not know: " + type),
                };
                string hash = graph.Object(entry, Rdfc + "hashAlgorithm") is { } h ? ManifestGraph.Text(h) : "SHA256";
                string testIri = ManifestGraph.Text(entry);
                all.Add(new CanonEntry(
                    testIri,
                    graph.Object(entry, Mf + "name") is { } name ? ManifestGraph.Text(name) : testIri,
                    kind,
                    ManifestGraph.Text(graph.Object(entry, Mf + "action")!),
                    graph.Object(entry, Mf + "result") is { } result ? ManifestGraph.Text(result) : null,
                    new HashAlgorithmName(hash)));
            }
        }

        return all;
    }

    /// <summary>An N-Quads file as a dataset, its blank node labels as the file writes them.</summary>
    internal static InMemoryDataset Load(string path)
    {
        InMemoryDataset dataset = new();
        ParseResult result = NQuadsParser.Parse(
            File.ReadAllBytes(path),
            (in QuadView quad) => dataset.Add(
                quad.Subject.Materialise(),
                quad.Predicate.Materialise(),
                quad.Object.Materialise(),
                quad.HasGraph ? quad.Graph.Materialise() : null),
            new ParseOptions { Syntax = RdfSyntax.NQuads });

        return result.Succeeded ? dataset : throw new InvalidOperationException("The input does not parse: " + result.FirstError);
    }
}

internal static class CanonRunner
{
    // A positive case that meets the limit reports it as its failure, naming the limit.
    private static string? Limited(InMemoryDataset input, CanonicalisationOptions options)
    {
        try
        {
            RdfCanonicaliser.Canonicalise(input, options);
            return null;
        }
        catch (CanonicalisationLimitException error)
        {
            return "met the work limit: " + error.Message;
        }
    }

    /// <summary>Runs one case; null when it passes, otherwise why not.</summary>
    internal static string? Run(CanonEntry entry, CanonicalisationOptions? options = null)
    {
        InMemoryDataset input = CanonCatalogue.Load(CanonCatalogue.PathOf(entry.Action));
        options ??= new CanonicalisationOptions { HashAlgorithm = entry.Hash };

        switch (entry.Kind)
        {
            case CanonKind.Negative:
                try
                {
                    RdfCanonicaliser.Canonicalise(input, options);
                    return "canonicalised a dataset that should have met the work limit";
                }
                catch (CanonicalisationLimitException)
                {
                    return null;
                }

            case CanonKind.Eval when Limited(input, options) is { } limited:
                return limited;

            case CanonKind.Map when Limited(input, options) is { } limited:
                return limited;

            case CanonKind.Eval:
                {
                    byte[] expected = File.ReadAllBytes(CanonCatalogue.PathOf(entry.Result!));
                    byte[] actual = RdfCanonicaliser.Canonicalise(input, options).NQuads.ToArray();
                    return actual.AsSpan().SequenceEqual(expected)
                        ? null
                        : "the canonical form differs:\n--- actual\n" + Encoding.UTF8.GetString(actual) + "--- expected\n" + Encoding.UTF8.GetString(expected);
                }

            default:
                {
                    Dictionary<string, string> expected = JsonSerializer.Deserialize(
                        File.ReadAllText(CanonCatalogue.PathOf(entry.Result!)), CanonJson.Default.DictionaryStringString)!;
                    IReadOnlyDictionary<string, string> actual = RdfCanonicaliser.Canonicalise(input, options).IssuedIdentifiers;
                    bool same = actual.Count == expected.Count && expected.All(p => actual.TryGetValue(p.Key, out string? v) && v == p.Value);
                    return same
                        ? null
                        : "the issued identifiers differ: " + string.Join(", ", actual.Select(p => p.Key + "→" + p.Value))
                            + "; expected " + string.Join(", ", expected.Select(p => p.Key + "→" + p.Value));
                }
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CanonJson : System.Text.Json.Serialization.JsonSerializerContext;
