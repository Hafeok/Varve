using System;
using System.Collections.Generic;
using System.IO;
using VDS.RDF;
using VDS.RDF.Parsing;

namespace Varve.Conformance.Tests;

/// <summary>
/// Reads a W3C test manifest.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only class in the repository that touches dotNetRDF,
/// and it is temporary.</strong> The manifests are Turtle and we have no
/// Turtle parser, which is the circularity ADR 0007 has to break. The exit
/// criterion is stated there and is a criterion, not an aspiration: when
/// <c>Varve.Turtle</c> passes <c>rdf/rdf11/rdf-turtle</c> unexempted, manifest
/// reading moves to it and <c>dotNetRdf.Core</c> leaves
/// <c>Directory.Packages.props</c>. Due at milestone 5.
/// </para>
/// <para>
/// Keep the dependency inside this file. Everything else in this project works
/// in terms of <see cref="ManifestEntry"/>.
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

        Graph graph = new() { BaseUri = new Uri(suite.BaseIri) };
        using (StreamReader reader = new(manifestPath))
        {
            new TurtleParser().Load(graph, reader);
        }

        IUriNode entries = graph.CreateUriNode(new Uri(Mf + "entries"));

        List<ManifestEntry> result = [];

        foreach (Triple triple in graph.GetTriplesWithSubjectPredicate(FindManifest(graph, suite), entries))
        {
            foreach (INode entry in WalkCollection(graph, triple.Object))
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
    private static INode FindManifest(Graph graph, ConformanceSuite suite)
    {
        IUriNode type = graph.CreateUriNode(new Uri(Rdf + "type"));
        IUriNode manifestType = graph.CreateUriNode(new Uri(Mf + "Manifest"));

        List<INode> found = [];

        foreach (Triple triple in graph.GetTriplesWithPredicateObject(type, manifestType))
        {
            found.Add(triple.Subject);
        }

        return found.Count == 1
            ? found[0]
            : throw new InvalidOperationException(
                found.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " nodes typed mf:Manifest in " + suite.ManifestPath + "; expected exactly one.");
    }

    /// <summary>
    /// Walks an <c>rdf:first</c>/<c>rdf:rest</c> collection to its
    /// <c>rdf:nil</c> terminator.
    /// </summary>
    private static IEnumerable<INode> WalkCollection(Graph graph, INode head)
    {
        IUriNode first = graph.CreateUriNode(new Uri(Rdf + "first"));
        IUriNode rest = graph.CreateUriNode(new Uri(Rdf + "rest"));
        IUriNode nil = graph.CreateUriNode(new Uri(Rdf + "nil"));

        INode? cursor = head;

        while (cursor is not null && !cursor.Equals(nil))
        {
            INode? item = SingleObject(graph, cursor, first);
            if (item is null)
            {
                yield break;
            }

            yield return item;

            cursor = SingleObject(graph, cursor, rest);
        }
    }

    private static ManifestEntry? ReadEntry(
        Graph graph,
        INode entry,
        ConformanceSuite suite,
        string manifestDirectory,
        string baseDirectoryIri)
    {
        if (entry is not IUriNode entryIri)
        {
            return null;
        }

        INode? type = SingleObject(graph, entry, graph.CreateUriNode(new Uri(Rdf + "type")));
        if (type is not IUriNode typeIri)
        {
            return null;
        }

        ExpectedOutcome? expected = ExpectationOf(typeIri.Uri.AbsoluteUri);
        if (expected is null)
        {
            return null;
        }

        INode? action = SingleObject(graph, entry, graph.CreateUriNode(new Uri(Mf + "action")));
        if (action is not IUriNode actionIri)
        {
            return null;
        }

        string actionPath = ResolveAction(actionIri.Uri.AbsoluteUri, baseDirectoryIri, manifestDirectory);

        return new ManifestEntry(
            TestIri: entryIri.Uri.AbsoluteUri,
            Suite: suite.Id,
            Name: Literal(graph, entry, Mf + "name") ?? entryIri.Uri.Fragment.TrimStart('#'),
            Comment: Literal(graph, entry, Rdfs + "comment"),
            ActionPath: actionPath,
            Format: suite.Format,
            Expected: expected.Value);
    }

    /// <summary>
    /// The RDF 1.1 syntax suites use one test type per format and outcome.
    /// A type this does not recognise yields no test case rather than a
    /// guessed one — a silently mis-typed entry would be worse than a missing
    /// one, and the count in the run summary is what makes a missing one
    /// visible.
    /// </summary>
    private static ExpectedOutcome? ExpectationOf(string typeIri) => typeIri switch
    {
        "http://www.w3.org/ns/rdftest#TestNTriplesPositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestNTriplesNegativeSyntax" => ExpectedOutcome.IsRejected,
        "http://www.w3.org/ns/rdftest#TestNQuadsPositiveSyntax" => ExpectedOutcome.Parses,
        "http://www.w3.org/ns/rdftest#TestNQuadsNegativeSyntax" => ExpectedOutcome.IsRejected,
        _ => null,
    };

    private static string ResolveAction(string actionIri, string baseDirectoryIri, string manifestDirectory)
    {
        string relative = actionIri.StartsWith(baseDirectoryIri, StringComparison.Ordinal)
            ? actionIri[baseDirectoryIri.Length..]
            : Path.GetFileName(new Uri(actionIri).AbsolutePath);

        return Path.Combine(manifestDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? Literal(Graph graph, INode subject, string predicate)
    {
        INode? value = SingleObject(graph, subject, graph.CreateUriNode(new Uri(predicate)));
        return value is ILiteralNode literal ? literal.Value : null;
    }

    private static INode? SingleObject(Graph graph, INode subject, IUriNode predicate)
    {
        foreach (Triple triple in graph.GetTriplesWithSubjectPredicate(subject, predicate))
        {
            return triple.Object;
        }

        return null;
    }
}
