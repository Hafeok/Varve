// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Varve.Rdf;

namespace Varve.Conformance.Tests;

/// <summary>
/// A hand-written check of <c>tests/fixtures/service-description/shapes.ttl</c>
/// against a service description: <b>not a SHACL validator</b>. It reads the
/// constraints those shapes use and no others — <c>sh:targetClass</c>,
/// <c>sh:property</c> with a predicate <c>sh:path</c>, <c>sh:minCount</c>,
/// <c>sh:maxCount</c>, <c>sh:class</c>, <c>sh:in</c>, <c>sh:nodeKind</c> — and
/// refuses a shape that uses anything else. The SHACL validator is milestone 8,
/// and replaces this when it exists (ADR 0092).
/// </summary>
internal static class ServiceDescriptionShapes
{
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    private static readonly HashSet<string> Understood = new(StringComparer.Ordinal)
    {
        RdfType, Sh + "targetClass", Sh + "property", Sh + "path", Sh + "minCount", Sh + "maxCount",
        Sh + "class", Sh + "in", Sh + "nodeKind",
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#first", "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest",
    };

    /// <summary>The violations, one line each; empty when the description conforms.</summary>
    internal static List<string> Validate(IReadOnlyList<DataQuad> description)
    {
        string path = Path.Combine(TestData.RdfTestsRoot, "..", "..", "fixtures", "service-description", "shapes.ttl");
        ManifestGraph shapes = ManifestGraph.Load(path, "https://w3id.org/varve/shapes/service-description");
        List<string> violations = [];
        Graph data = new(description);

        foreach (RdfTerm shape in shapes.Subjects(RdfType, Sh + "NodeShape"))
        {
            string targetClass = ManifestGraph.Text(shapes.Object(shape, Sh + "targetClass")!);

            foreach (RdfTerm property in shapes.Objects(shape, Sh + "property"))
            {
                foreach (string predicate in shapes.Predicates(property))
                {
                    if (!Understood.Contains(predicate))
                    {
                        throw new InvalidOperationException("The shapes use " + predicate + ", which this hand-written check does not read.");
                    }
                }

                string pathIri = ManifestGraph.Text(shapes.Object(property, Sh + "path")!);
                int? min = Number(shapes.Object(property, Sh + "minCount"));
                int? max = Number(shapes.Object(property, Sh + "maxCount"));
                string? cls = shapes.Object(property, Sh + "class") is { } c ? ManifestGraph.Text(c) : null;
                string? kind = shapes.Object(property, Sh + "nodeKind") is { } k ? ManifestGraph.Text(k) : null;
                HashSet<string>? allowed = shapes.Object(property, Sh + "in") is { } list
                    ? [.. shapes.Collection(list).Select(ManifestGraph.Text)]
                    : null;

                foreach (string focus in data.SubjectsOf(RdfType, targetClass))
                {
                    List<RdfTerm> values = data.Objects(focus, pathIri);

                    if (min is int least && values.Count < least)
                    {
                        violations.Add(focus + " has " + values.Count + " " + pathIri + ", at least " + least);
                    }

                    if (max is int most && values.Count > most)
                    {
                        violations.Add(focus + " has " + values.Count + " " + pathIri + ", at most " + most);
                    }

                    foreach (RdfTerm value in values)
                    {
                        string text = Key(value);

                        if (cls is not null && !data.Objects(text, RdfType).Any(t => Key(t) == cls))
                        {
                            violations.Add(focus + " " + pathIri + " " + text + " is not a " + cls);
                        }

                        if (allowed is not null && !allowed.Contains(ManifestGraph.Text(value)))
                        {
                            violations.Add(focus + " " + pathIri + " " + text + " is not in the allowed list");
                        }

                        if (kind is not null && !KindMatches(kind, value))
                        {
                            violations.Add(focus + " " + pathIri + " " + text + " is not a " + kind);
                        }
                    }
                }
            }
        }

        return violations;
    }

    private static bool KindMatches(string kind, RdfTerm value) => kind[Sh.Length..] switch
    {
        "IRI" => value.Kind == RdfTermKind.Iri,
        "Literal" => value.Kind == RdfTermKind.Literal,
        "BlankNodeOrIRI" => value.Kind is RdfTermKind.Iri or RdfTermKind.BlankNode,
        string other => throw new InvalidOperationException("sh:nodeKind " + other + " is not read by this check."),
    };

    private static int? Number(RdfTerm? term) => term is null ? null : int.Parse(Encoding.UTF8.GetString(term.Lexical), System.Globalization.CultureInfo.InvariantCulture);

    private static string Key(RdfTerm term) => ManifestGraph.Text(term);

    private sealed class Graph
    {
        private readonly List<DataQuad> _quads;

        internal Graph(IReadOnlyList<DataQuad> quads) => _quads = [.. quads];

        internal IEnumerable<string> SubjectsOf(string predicate, string obj) =>
            _quads.Where(q => Key(q.Predicate) == predicate && Key(q.Object) == obj).Select(q => Key(q.Subject)).Distinct();

        internal List<RdfTerm> Objects(string subject, string predicate) =>
            [.. _quads.Where(q => Key(q.Subject) == subject && Key(q.Predicate) == predicate).Select(q => q.Object)];
    }
}
