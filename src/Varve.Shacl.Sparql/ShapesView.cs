// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;

namespace Varve.Shacl.Sparql;

/// <summary>
/// The shapes graph read by the extension: by handle, so a blank node — a
/// SPARQL-based constraint, a validator, a prefix declaration — is followed by
/// the handle it came with (ADR 0044), and as terms where a term is needed.
/// </summary>
internal sealed class ShapesView(IQuadSource source, GraphPattern graph)
{
    private readonly Dictionary<TermHandle, RdfTerm> _terms = new(source.TermComparer);
    private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);

    internal IQuadSource Source => source;

    internal List<TermHandle> Objects(TermHandle subject, RdfTerm predicate)
    {
        List<TermHandle> found = [];
        if (subject.IsNone || !TryHandle(predicate, out TermHandle p))
        {
            return found;
        }

        HashSet<TermHandle> seen = new(source.TermComparer);
        using IQuadCursor cursor = source.Match(subject, p, TermHandle.None, graph);
        while (cursor.MoveNext())
        {
            if (seen.Add(cursor.Current.Object))
            {
                found.Add(cursor.Current.Object);
            }
        }

        return found;
    }

    internal List<TermHandle> Subjects(RdfTerm predicate, TermHandle obj)
    {
        List<TermHandle> found = [];
        if (obj.IsNone || !TryHandle(predicate, out TermHandle p))
        {
            return found;
        }

        HashSet<TermHandle> seen = new(source.TermComparer);
        using IQuadCursor cursor = source.Match(TermHandle.None, p, obj, graph);
        while (cursor.MoveNext())
        {
            if (seen.Add(cursor.Current.Subject))
            {
                found.Add(cursor.Current.Subject);
            }
        }

        return found;
    }

    internal List<TermHandle> SubjectsOf(RdfTerm predicate)
    {
        List<TermHandle> found = [];
        if (!TryHandle(predicate, out TermHandle p))
        {
            return found;
        }

        HashSet<TermHandle> seen = new(source.TermComparer);
        using IQuadCursor cursor = source.Match(TermHandle.None, p, TermHandle.None, graph);
        while (cursor.MoveNext())
        {
            if (seen.Add(cursor.Current.Subject))
            {
                found.Add(cursor.Current.Subject);
            }
        }

        return found;
    }

    /// <summary>The one object as a term, or null; <paramref name="count"/> says how many there were.</summary>
    internal RdfTerm? Object(TermHandle subject, RdfTerm predicate, out int count)
    {
        List<TermHandle> found = Objects(subject, predicate);
        count = found.Count;
        return found.Count == 0 ? null : Term(found[0]);
    }

    /// <summary>The members of a SHACL list, or null when it is not one.</summary>
    internal List<TermHandle>? List(TermHandle head)
    {
        List<TermHandle> members = [];
        HashSet<TermHandle> visited = new(source.TermComparer);
        TryHandle(Vocabulary.RdfNil, out TermHandle nil);
        TermHandle cursor = head;
        while (nil.IsNone || !source.TermComparer.Equals(cursor, nil))
        {
            if (!visited.Add(cursor))
            {
                return null;
            }

            List<TermHandle> first = Objects(cursor, Vocabulary.RdfFirst);
            List<TermHandle> rest = Objects(cursor, Vocabulary.RdfRest);
            if (first.Count != 1 || rest.Count != 1)
            {
                return null;
            }

            members.Add(first[0]);
            cursor = rest[0];
        }

        return members;
    }

    /// <summary>Whether the node is a SHACL instance of the class in the shapes graph.</summary>
    internal bool IsInstanceOf(TermHandle node, RdfTerm cls)
    {
        Stack<TermHandle> pending = new(Objects(node, Vocabulary.RdfType));
        HashSet<TermHandle> visited = new(source.TermComparer);
        while (pending.Count > 0)
        {
            TermHandle current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (Term(current).Equals(cls))
            {
                return true;
            }

            foreach (TermHandle parent in Objects(current, Vocabulary.RdfsSubClassOf))
            {
                pending.Push(parent);
            }
        }

        return false;
    }

    internal bool TryHandle(RdfTerm term, out TermHandle handle)
    {
        if (_handles.TryGetValue(term, out handle))
        {
            return true;
        }

        if (term.Kind != RdfTermKind.BlankNode && source.TryInternalise(term, out handle))
        {
            _handles[term] = handle;
            return true;
        }

        handle = TermHandle.None;
        return false;
    }

    internal RdfTerm Term(TermHandle handle)
    {
        if (!_terms.TryGetValue(handle, out RdfTerm? term))
        {
            if (!source.TryExternalise(handle, out term))
            {
                throw new InvalidOperationException("The shapes source gave a handle it cannot externalise.");
            }

            _terms[handle] = term;
            _handles.TryAdd(term, handle);
        }

        return term;
    }

    /// <summary>A literal's lexical form, or null when the node is not a literal.</summary>
    internal string? Text(TermHandle handle)
    {
        RdfTerm term = Term(handle);
        return term.Kind == RdfTermKind.Literal ? Encoding.UTF8.GetString(term.Lexical) : null;
    }
}

/// <summary>The SHACL and other terms SHACL-SPARQL reads.</summary>
internal static class Vocabulary
{
    internal const string Shacl = "http://www.w3.org/ns/shacl#";

    internal static readonly RdfTerm Sparql = Sh("sparql");
    internal static readonly RdfTerm Select = Sh("select");
    internal static readonly RdfTerm Ask = Sh("ask");
    internal static readonly RdfTerm Message = Sh("message");
    internal static readonly RdfTerm Deactivated = Sh("deactivated");
    internal static readonly RdfTerm Severity = Sh("severity");
    internal static readonly RdfTerm Prefixes = Sh("prefixes");
    internal static readonly RdfTerm Declare = Sh("declare");
    internal static readonly RdfTerm Prefix = Sh("prefix");
    internal static readonly RdfTerm Namespace = Sh("namespace");
    internal static readonly RdfTerm Path = Sh("path");
    internal static readonly RdfTerm Parameter = Sh("parameter");
    internal static readonly RdfTerm Optional = Sh("optional");
    internal static readonly RdfTerm Validator = Sh("validator");
    internal static readonly RdfTerm NodeValidator = Sh("nodeValidator");
    internal static readonly RdfTerm PropertyValidator = Sh("propertyValidator");
    internal static readonly RdfTerm ConstraintComponent = Sh("ConstraintComponent");
    internal static readonly RdfTerm SparqlConstraintComponent = Sh("SPARQLConstraintComponent");
    internal static readonly RdfTerm ShapesGraph = Sh("ShapesGraph");
    internal static readonly RdfTerm DataGraph = Sh("DataGraph");
    internal static readonly RdfTerm RulesGraph = Sh("RulesGraph");
    internal static readonly RdfTerm InversePath = Sh("inversePath");
    internal static readonly RdfTerm AlternativePath = Sh("alternativePath");
    internal static readonly RdfTerm ZeroOrMorePath = Sh("zeroOrMorePath");
    internal static readonly RdfTerm OneOrMorePath = Sh("oneOrMorePath");
    internal static readonly RdfTerm ZeroOrOnePath = Sh("zeroOrOnePath");
    internal static readonly RdfTerm RdfType = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    internal static readonly RdfTerm RdfFirst = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#first");
    internal static readonly RdfTerm RdfRest = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#rest");
    internal static readonly RdfTerm RdfNil = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#nil");
    internal static readonly RdfTerm RdfsSubClassOf = Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf");
    internal static readonly RdfTerm OwlOntology = Iri("http://www.w3.org/2002/07/owl#Ontology");
    internal static readonly RdfTerm OwlImports = Iri("http://www.w3.org/2002/07/owl#imports");
    internal static readonly RdfTerm OwlVersionIri = Iri("http://www.w3.org/2002/07/owl#versionIRI");

    private static RdfTerm Sh(string local) => Iri(Shacl + local);

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
