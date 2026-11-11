// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Varve.Classification.Model;
using Varve.Rdf;

namespace Varve.Classification;

/// <summary>
/// A binding's classifier configuration, read from its shapes graph as RDF
/// (ADR 0232 points 2 and 3): the targets of its data-subject shapes, its
/// link predicates and its public links, with what it refuses. A function of
/// the shapes graph as of one state.
/// </summary>
internal sealed class Annotations
{
    internal const string Varve = "https://w3id.org/varve/ns#";
    internal const string Sh = "http://www.w3.org/ns/shacl#";
    internal const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    internal static readonly RdfTerm RdfType = Term(Rdf + "type");
    private static readonly RdfTerm DataSubject = Term(Varve + "dataSubject");
    private static readonly RdfTerm SubjectLink = Term(Varve + "subjectLink");
    private static readonly RdfTerm PublicLink = Term(Varve + "publicLink");
    private static readonly RdfTerm Privacy = Term(Varve + "privacy");
    private static readonly RdfTerm Path = Term(Sh + "path");
    private static readonly RdfTerm TargetClass = Term(Sh + "targetClass");
    private static readonly RdfTerm TargetNode = Term(Sh + "targetNode");
    private static readonly RdfTerm TargetSubjectsOf = Term(Sh + "targetSubjectsOf");
    private static readonly FrozenSet<RdfTerm> RefusedTargets = new[] { Term(Sh + "targetObjectsOf"), Term(Sh + "target"), Term(Sh + "targetWhere") }.ToFrozenSet(RdfTerm.Comparer);
    private static readonly FrozenSet<RdfTerm> Classes = new[] { Term("http://www.w3.org/2000/01/rdf-schema#Class"), Term("http://www.w3.org/2002/07/owl#Class") }.ToFrozenSet(RdfTerm.Comparer);

    private Annotations()
    {
    }

    /// <summary>The classes whose instances are data subjects.</summary>
    internal List<RdfTerm> SubjectClasses { get; } = [];

    /// <summary>The nodes that are data subjects by <c>sh:targetNode</c>.</summary>
    internal List<RdfTerm> SubjectNodes { get; } = [];

    /// <summary>The predicates whose subjects are data subjects by <c>sh:targetSubjectsOf</c>.</summary>
    internal List<RdfTerm> SubjectsOf { get; } = [];

    /// <summary>The link predicates: ownership flows from object to subject.</summary>
    internal List<RdfTerm> Links { get; } = [];

    /// <summary>The public links, <c>rdf:type</c> always among them.</summary>
    internal List<RdfTerm> Public { get; } = [RdfType];

    /// <summary>Why the shapes graph does not classify; empty when it does.</summary>
    internal List<ClassifierProblem> Problems { get; } = [];

    /// <summary>No annotations: what a stored generation is read back into.</summary>
    internal static Annotations Empty() => new();

    /// <summary>The annotations of <paramref name="binding"/>'s shapes graph in <paramref name="source"/>.</summary>
    internal static Annotations Read(IQuadSource source, ClassifierBinding binding)
    {
        Annotations read = new();

        if (!source.TryInternalise(binding.ShapesGraph, out TermHandle graph))
        {
            return read;
        }

        List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples = [];
        using (IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph)))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;

                if (source.TryExternalise(quad.Subject, out RdfTerm? s) && source.TryExternalise(quad.Predicate, out RdfTerm? p) && source.TryExternalise(quad.Object, out RdfTerm? o))
                {
                    triples.Add((s, p, o));
                }
            }
        }

        foreach ((RdfTerm shape, RdfTerm p, RdfTerm o) in triples)
        {
            if (p.Equals(DataSubject) && IsTrue(o))
            {
                read.Subject(shape, triples);
            }
            else if (p.Equals(SubjectLink) && IsTrue(o))
            {
                read.Predicate(shape, triples, read.Links, "varve:subjectLink");
            }
            else if (p.Equals(PublicLink) && IsTrue(o))
            {
                read.Predicate(shape, triples, read.Public, "varve:publicLink");
            }
            else if (p.Equals(Privacy))
            {
                read.Problems.Add(new ClassifierProblem("varve:privacy is reserved for erasure mode (#203)."));
            }
        }

        foreach (RdfTerm link in read.Links)
        {
            if (read.Public.Contains(link))
            {
                read.Problems.Add(new ClassifierProblem($"<{Text(link)}> is both a link and a public link."));
            }
        }

        return read;
    }

    internal static RdfTerm Term(string iri) => RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes(iri));

    internal static string Text(RdfTerm term) => System.Text.Encoding.UTF8.GetString(term.Lexical);

    private void Subject(RdfTerm shape, List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples)
    {
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            if (!s.Equals(shape))
            {
                continue;
            }

            if ((p.Equals(TargetClass) || p.Equals(TargetNode) || p.Equals(TargetSubjectsOf)) && o.Kind != RdfTermKind.Iri)
            {
                Problems.Add(new ClassifierProblem($"The data-subject shape {Label(shape)} has a target that is not an IRI."));
            }
            else if (p.Equals(TargetClass))
            {
                SubjectClasses.Add(o);
            }
            else if (p.Equals(TargetNode))
            {
                SubjectNodes.Add(o);
            }
            else if (p.Equals(TargetSubjectsOf))
            {
                SubjectsOf.Add(o);
            }
            else if (p.Equals(RdfType) && Classes.Contains(o) && shape.Kind == RdfTermKind.Iri && !SubjectClasses.Contains(shape))
            {
                // An implicit class target (SHACL §2.1.3.3).
                SubjectClasses.Add(shape);
            }
            else if (RefusedTargets.Contains(p))
            {
                Problems.Add(new ClassifierProblem($"The data-subject shape {Label(shape)} has a target that reads beyond its focus's row (ADR 0232 point 3)."));
            }
        }
    }

    private void Predicate(RdfTerm shape, List<(RdfTerm S, RdfTerm P, RdfTerm O)> triples, List<RdfTerm> into, string term)
    {
        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in triples)
        {
            if (s.Equals(shape) && p.Equals(Path))
            {
                if (o.Kind == RdfTermKind.Iri)
                {
                    if (!into.Contains(o))
                    {
                        into.Add(o);
                    }
                }
                else
                {
                    Problems.Add(new ClassifierProblem($"{term} on {Label(shape)} needs a predicate path (ADR 0232 point 2)."));
                }
            }
        }
    }

    private static string Label(RdfTerm shape) => shape.Kind == RdfTermKind.Iri ? "<" + Text(shape) + ">" : "a blank node shape";

    private static bool IsTrue(RdfTerm term) =>
        term.Kind == RdfTermKind.Literal && (term.Lexical.SequenceEqual("true"u8) || term.Lexical.SequenceEqual("1"u8));
}
