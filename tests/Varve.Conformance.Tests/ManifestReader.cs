using System;
using System.Collections.Generic;
using System.IO;
using Varve.Rdf;

namespace Varve.Conformance.Tests;

/// <summary>
/// Reads a W3C test manifest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It reads them with <c>Varve.Turtle</c>.</strong> ADR 0007 opened a
/// circularity deliberately — the manifests are Turtle and there was no Turtle
/// parser — and set an exit criterion: when <c>Varve.Turtle</c> passes
/// <c>rdf/rdf11/rdf-turtle</c> unexempted, manifest reading moves to it and
/// <c>dotNetRdf.Core</c> leaves this project. It passes 313 of 313, and 357 of
/// 357 for TriG, so this is that move.
/// </para>
/// <para>
/// <strong>Why this is not self-certifying.</strong> The parser does not
/// decide whether it is correct; the suites do, and a manifest is not a
/// verdict. What a parser bug could do is change which entries run — drop some
/// and quietly shrink a suite. That is what the per-suite counts in
/// <see cref="SubmoduleGuardTests"/> are for, and they were recorded while
/// dotNetRDF was still reading the manifests: 70, 87, 29, 27, 313, 357. The
/// guard against the new reader was written by the old one.
/// </para>
/// </remarks>
internal static class ManifestReader
{
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    /// <summary>
    /// Reads every entry of <paramref name="suite"/>'s manifest, in the order
    /// the manifest lists them.
    /// </summary>
    /// <remarks>
    /// Entries come from the <c>mf:entries</c> collection rather than from a
    /// type query, so an entry the manifest does not list does not run — which
    /// is what the manifest listing it means.
    /// </remarks>
    internal static IReadOnlyList<ManifestEntry> Read(ConformanceSuite suite)
    {
        string manifestPath = TestData.ResolveFromRoot(suite.ManifestPath);
        string manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidOperationException("Manifest has no directory: " + manifestPath);

        string baseDirectoryIri = suite.BaseIri[..(suite.BaseIri.LastIndexOf('/') + 1)];
        ManifestGraph graph = ManifestGraph.Load(manifestPath, suite.BaseIri);

        List<ManifestEntry> result = [];

        foreach (RdfTerm list in graph.Objects(FindManifest(graph, suite), Mf + "entries"))
        {
            foreach (RdfTerm entry in graph.Collection(list))
            {
                ManifestEntry? parsed = ReadEntry(graph, entry, suite, manifestDirectory, baseDirectoryIri);

                if (parsed is not null)
                {
                    result.Add(parsed);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the node the manifest describes.
    /// </summary>
    /// <remarks>
    /// The RDF 1.1 manifests name themselves <c>&lt;&gt;</c>, which resolves to
    /// the manifest file's own IRI. The RDF 1.2 manifests name themselves
    /// <c>trs:manifest</c>, a fragment of the suite directory. Asking for the
    /// node typed <c>mf:Manifest</c> works for both and does not need a table
    /// of which suite is written which way — and a manifest with no such node,
    /// or more than one, is a manifest this reader should refuse rather than
    /// silently read as empty.
    /// </remarks>
    private static RdfTerm FindManifest(ManifestGraph graph, ConformanceSuite suite)
    {
        IReadOnlyList<RdfTerm> found = graph.Subjects(Rdf + "type", Mf + "Manifest");

        return found.Count == 1
            ? found[0]
            : throw new InvalidOperationException(
                "Found " + found.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " nodes typed mf:Manifest in " + suite.ManifestPath + "; expected exactly one.");
    }

    private static ManifestEntry? ReadEntry(
        ManifestGraph graph,
        RdfTerm entry,
        ConformanceSuite suite,
        string manifestDirectory,
        string baseDirectoryIri)
    {
        if (!ManifestGraph.IsIri(entry))
        {
            return null;
        }

        RdfTerm? type = graph.Object(entry, Rdf + "type");

        if (type is null || !ManifestGraph.IsIri(type))
        {
            return null;
        }

        ExpectedOutcome? expected = ExpectationOf(ManifestGraph.Text(type));

        if (expected is null)
        {
            return null;
        }

        RdfTerm? action = graph.Object(entry, Mf + "action");

        if (action is null || !ManifestGraph.IsIri(action))
        {
            return null;
        }

        string actionIri = ManifestGraph.Text(action);
        RdfTerm? result = graph.Object(entry, Mf + "result");

        return new ManifestEntry(
            TestIri: ManifestGraph.Text(entry),
            Suite: suite.Id,
            Name: Literal(graph, entry, Mf + "name") ?? Fragment(ManifestGraph.Text(entry)),
            Comment: Literal(graph, entry, Rdfs + "comment"),
            ActionPath: ResolveAction(actionIri, baseDirectoryIri, manifestDirectory),
            ActionIri: actionIri,
            Format: suite.Format,
            Expected: expected.Value,
            ResultPath: result is not null && ManifestGraph.IsIri(result)
                ? ResolveAction(ManifestGraph.Text(result), baseDirectoryIri, manifestDirectory)
                : null);
    }

    /// <summary>
    /// The suites use one test type per format and outcome. A type this does
    /// not recognise yields no test case rather than a guessed one — a silently
    /// mis-typed entry would be worse than a missing one, and the count in the
    /// run summary is what makes a missing one visible.
    /// </summary>
    private static ExpectedOutcome? ExpectationOf(string type) => type switch
    {
        "http://www.w3.org/ns/rdftest#TestNTriplesPositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestNTriplesNegativeSyntax" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestNQuadsPositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestNQuadsNegativeSyntax" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestTurtlePositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestTurtleNegativeSyntax" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestTurtleEval" => ExpectedOutcome.Evaluates,
        "http://www.w3.org/ns/rdftest#TestTurtleNegativeEval" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestTrigPositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestTrigNegativeSyntax" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestTrigEval" => ExpectedOutcome.Evaluates,
        "http://www.w3.org/ns/rdftest#TestTrigNegativeEval" => ExpectedOutcome.IsRejected,
        _ => null,
    };

    private static string ResolveAction(string actionIri, string baseDirectoryIri, string manifestDirectory)
    {
        string relative = actionIri.StartsWith(baseDirectoryIri, StringComparison.Ordinal)
            ? actionIri[baseDirectoryIri.Length..]
            : Path.GetFileName(new Uri(actionIri).AbsolutePath);

        return Path.Combine(manifestDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Fragment(string iri)
    {
        int hash = iri.LastIndexOf('#');
        return hash < 0 ? iri : iri[(hash + 1)..];
    }

    private static string? Literal(ManifestGraph graph, RdfTerm subject, string predicate)
    {
        RdfTerm? value = graph.Object(subject, predicate);

        return value is not null && ManifestGraph.IsLiteral(value)
            ? System.Text.Encoding.UTF8.GetString(value.Lexical)
            : null;
    }
}
