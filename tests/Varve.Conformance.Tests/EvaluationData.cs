// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>One quad of test data, as owned terms; <see cref="Graph"/> null for the default graph.</summary>
internal sealed record DataQuad(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object, RdfTerm? Graph);

/// <summary>
/// Reads the suites' data files: Turtle, TriG, N-Triples and N-Quads with
/// <c>Varve.Turtle</c>, and RDF/XML through the N-Triples translations in
/// <c>tests/fixtures/w3c-rdfxml/</c>, each guarded by the SHA-256 of its original
/// (ADR 0027's dated note). A file is read once per run.
/// </summary>
internal static class EvaluationData
{
    private static readonly ConcurrentDictionary<string, (IReadOnlyList<DataQuad>? Quads, string? Error)> Cache = new(StringComparer.Ordinal);

    internal static string FixtureRoot => Path.Combine(TestData.RdfTestsRoot, "..", "..", "fixtures", "w3c-rdfxml");

    /// <summary>
    /// The Turtle and TriG data files of a case that Varve.Turtle refuses — RDF
    /// 1.2 syntax, which turtle.md §9 does not accept — with why. Any other
    /// file that fails to load is not a block but a failure, and fails its case.
    /// </summary>
    internal static IReadOnlyList<string> RefusedFiles(EvaluationEntry entry)
    {
        List<string> refused = [];
        foreach (string iri in Files(entry))
        {
            if (Path.GetExtension(iri) is ".ttl" or ".trig" && Read(iri).Error is { } error)
            {
                refused.Add(iri + ": " + error);
            }
        }

        return refused;
    }

    internal static IEnumerable<string> Files(EvaluationEntry entry)
    {
        foreach (string iri in entry.Data)
        {
            yield return iri;
        }

        foreach (string iri in entry.GraphData)
        {
            yield return iri;
        }

        foreach ((_, IReadOnlyList<string> data) in entry.ServiceData)
        {
            foreach (string iri in data)
            {
                yield return iri;
            }
        }
    }

    /// <summary>A file's quads, or why it does not load.</summary>
    internal static (IReadOnlyList<DataQuad>? Quads, string? Error) Read(string iri) => Cache.GetOrAdd(iri, Load);

    /// <summary>A file's quads, or an exception naming why not.</summary>
    internal static IReadOnlyList<DataQuad> Quads(string iri) =>
        Read(iri) is { Quads: { } quads } ? quads : throw new InvalidOperationException(Read(iri).Error);

    private static (IReadOnlyList<DataQuad>?, string?) Load(string iri)
    {
        string path = EvaluationSuite.PathOf(iri);
        string extension = Path.GetExtension(path);
        RdfSyntax syntax;
        byte[] bytes;
        if (string.Equals(extension, ".rdf", StringComparison.Ordinal))
        {
            (bytes, string? problem) = Translation(path);
            if (problem is not null)
            {
                return (null, problem);
            }

            syntax = RdfSyntax.NTriples;
        }
        else
        {
            bytes = File.ReadAllBytes(path);
            syntax = extension switch
            {
                ".nt" => RdfSyntax.NTriples,
                ".nq" => RdfSyntax.NQuads,
                ".trig" => RdfSyntax.TriG,
                _ => RdfSyntax.Turtle,
            };
        }

        List<DataQuad> quads = [];
        QuadHandler handler = (in QuadView quad) => quads.Add(new DataQuad(
            quad.Subject.Materialise(),
            quad.Predicate.Materialise(),
            quad.Object.Materialise(),
            quad.HasGraph ? quad.Graph.Materialise() : null));

        ParseResult result = syntax is RdfSyntax.NTriples or RdfSyntax.NQuads
            ? NQuadsParser.Parse(bytes, handler, new ParseOptions { Syntax = syntax })
            : TurtleParser.Parse(bytes, handler, new TurtleOptions { Syntax = syntax, BaseIri = Encoding.UTF8.GetBytes(iri) });
        return result.Succeeded ? (quads, null) : (null, result.FirstError.ToString());
    }

    /// <summary>
    /// An RDF/XML file's N-Triples translation, generated once by dotNetRDF
    /// (ADR 0027), and whether the original it was made from is still the one
    /// in the submodule: a translation of a file that has since changed is a
    /// fixture that lies.
    /// </summary>
    internal static (byte[] Bytes, string? Problem) Translation(string rdfXmlPath)
    {
        string relative = Path.GetRelativePath(TestData.RdfTestsRoot, rdfXmlPath).Replace('\\', '/');
        string translation = Path.Combine(FixtureRoot, relative + ".nt");
        string digestFile = translation + ".sha256";
        if (!File.Exists(translation) || !File.Exists(digestFile))
        {
            return ([], "RDF/XML with no N-Triples translation in tests/fixtures/w3c-rdfxml/: " + relative);
        }

        string expected = File.ReadAllText(digestFile).Trim();
        string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(rdfXmlPath)));
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return ([], "The translation of " + relative + " was made from a different original (SHA-256 " + expected + ", now " + actual + "); regenerate it.");
        }

        return (File.ReadAllBytes(translation), null);
    }

    /// <summary>A term as N-Triples-like text: comparable, and readable in a failure message.</summary>
    internal static string Text(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        RdfTermKind.TripleTerm => "<<( " + Text(term.Subject!) + " " + Text(term.Predicate!) + " " + Text(term.Object!) + " )>>",
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal) + "\""
            + (term.Language.IsEmpty ? "" : "@" + Encoding.UTF8.GetString(term.Language).ToLowerInvariant()
                + (term.Direction == TextDirection.LeftToRight ? "--ltr" : term.Direction == TextDirection.RightToLeft ? "--rtl" : ""))
            + (term.Datatype is null ? "" : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };
}
