using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Varve.Rdf;
using Varve.Turtle;

namespace Varve.Conformance.Tests;

/// <summary>
/// A manifest, parsed with <c>Varve.Turtle</c> and indexed for the four
/// questions <see cref="ManifestReader"/> asks of it.
/// </summary>
/// <remarks>
/// <para>
/// Not a graph API — a lookup table. The reader needs objects of a
/// subject-predicate pair, the subjects of one, a collection walk and a
/// literal's text, and nothing else. A general graph type here would be a
/// second RDF library in a repository that is retiring its first.
/// </para>
/// <para>
/// <strong>The harness now reads its own manifests with the parser under
/// test</strong>, which is the circularity ADR 0007 opened deliberately and
/// closes here. It is not self-certifying: the suites decide whether the
/// parser is right, and the per-suite case counts pinned in
/// <c>SubmoduleGuardTests</c> were recorded while dotNetRDF was still reading
/// them. A parser bug that dropped or invented entries changes a count, and
/// the count was written down by the implementation being replaced.
/// </para>
/// </remarks>
internal sealed class ManifestGraph
{
    private readonly Dictionary<Key, List<RdfTerm>> _objects = [];
    private readonly Dictionary<Key, List<RdfTerm>> _subjects = [];

    private ManifestGraph()
    {
    }

    /// <summary>Parses a manifest, or throws with what the parser said.</summary>
    internal static ManifestGraph Load(string path, string baseIri)
    {
        ManifestGraph graph = new();
        TurtleOptions options = new()
        {
            Syntax = RdfSyntax.Turtle,
            BaseIri = Encoding.UTF8.GetBytes(baseIri),
        };

        ParseResult result = TurtleParser.Parse(
            File.ReadAllBytes(path),
            (in QuadView quad) => graph.Add(
                quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            in options);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The manifest {path} did not parse: {result.FirstError}. A manifest this harness "
                + "cannot read is a harness failure, not an empty suite.");
        }

        return graph;
    }

    /// <summary>The objects of <paramref name="subject"/> and <paramref name="predicate"/>.</summary>
    internal IReadOnlyList<RdfTerm> Objects(RdfTerm subject, string predicate) =>
        _objects.TryGetValue(new Key(Text(subject), predicate), out List<RdfTerm>? found) ? found : [];

    /// <summary>The single object, or null when there is none.</summary>
    internal RdfTerm? Object(RdfTerm subject, string predicate)
    {
        IReadOnlyList<RdfTerm> found = Objects(subject, predicate);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The subjects that have <paramref name="obj"/> for a predicate.</summary>
    internal IReadOnlyList<RdfTerm> Subjects(string predicate, string obj) =>
        _subjects.TryGetValue(new Key(obj, predicate), out List<RdfTerm>? found) ? found : [];

    /// <summary>
    /// Walks an <c>rdf:first</c>/<c>rdf:rest</c> collection to its
    /// <c>rdf:nil</c> terminator.
    /// </summary>
    internal IEnumerable<RdfTerm> Collection(RdfTerm head)
    {
        const string first = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";
        const string rest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";
        const string nil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

        RdfTerm? cursor = head;

        while (cursor is not null && !string.Equals(Text(cursor), nil, StringComparison.Ordinal))
        {
            RdfTerm? item = Object(cursor, first);

            if (item is null)
            {
                yield break;
            }

            yield return item;
            cursor = Object(cursor, rest);
        }
    }

    /// <summary>
    /// A term's identity as a string: the IRI, or the label with its
    /// <c>_:</c>, or the lexical form. Enough to key on, because a manifest's
    /// subjects are IRIs and blank nodes and its predicates are IRIs.
    /// </summary>
    internal static string Text(RdfTerm term) =>
        term.Kind == RdfTermKind.BlankNode
            ? "_:" + Encoding.UTF8.GetString(term.Lexical)
            : Encoding.UTF8.GetString(term.Lexical);

    internal static bool IsIri(RdfTerm term) => term.Kind == RdfTermKind.Iri;

    internal static bool IsLiteral(RdfTerm term) => term.Kind == RdfTermKind.Literal;

    private void Add(RdfTerm subject, RdfTerm predicate, RdfTerm obj)
    {
        Append(_objects, new Key(Text(subject), Text(predicate)), obj);
        Append(_subjects, new Key(Text(obj), Text(predicate)), subject);
    }

    private static void Append(Dictionary<Key, List<RdfTerm>> index, Key key, RdfTerm value)
    {
        if (!index.TryGetValue(key, out List<RdfTerm>? found))
        {
            found = [];
            index[key] = found;
        }

        found.Add(value);
    }

    private readonly record struct Key(string Node, string Predicate);
}
