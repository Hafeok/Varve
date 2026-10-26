// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Shacl.Graphs;

/// <summary>
/// The shapes graph read as terms: a graph pattern of a quad source, read
/// once at load through the quad source contract. Every term it hands out is
/// remembered with its handle, so a term read from it — a blank node
/// included, which a store never looks up by label (ADR 0044) — can be looked
/// up again.
/// </summary>
internal sealed class ShapesSource
{
    private readonly Dictionary<TermHandle, RdfTerm> _terms;
    private readonly Dictionary<RdfTerm, TermHandle> _handles = new(RdfTerm.Comparer);
    private readonly ConcurrentDictionary<RdfTerm, List<RdfTerm>> _superclasses = new(RdfTerm.Comparer);
    private readonly ConcurrentDictionary<RdfTerm, List<RdfTerm>> _subclasses = new(RdfTerm.Comparer);

    internal ShapesSource(IQuadSource source, GraphPattern graph)
    {
        Source = source;
        Graph = graph;
        _terms = new Dictionary<TermHandle, RdfTerm>(source.TermComparer);
    }

    internal IQuadSource Source { get; }

    internal GraphPattern Graph { get; }

    /// <summary>The objects of <paramref name="subject"/> and <paramref name="predicate"/>, distinct, in the source's order.</summary>
    internal List<RdfTerm> Objects(RdfTerm subject, RdfTerm predicate)
    {
        List<RdfTerm> found = [];
        if (!TryHandle(subject, out TermHandle s) || !TryHandle(predicate, out TermHandle p))
        {
            return found;
        }

        Collect(s, p, TermHandle.None, Position.Object, found);
        return found;
    }

    /// <summary>
    /// The direct superclasses (<paramref name="upward"/>) or subclasses of
    /// <paramref name="term"/> by <c>rdfs:subClassOf</c>, remembered: the
    /// shapes graph does not change once loaded, and a validation under
    /// SHACL 1.2 Core §6.4's parameter asks for the same classes at every
    /// commit (ADR 0169).
    /// </summary>
    internal List<RdfTerm> SubClassOf(RdfTerm term, bool upward) => upward
        ? _superclasses.GetOrAdd(term, t => Objects(t, Vocabulary.RdfsSubClassOf))
        : _subclasses.GetOrAdd(term, t => Subjects(Vocabulary.RdfsSubClassOf, t));

    /// <summary>The one object, or null when there is none; <paramref name="count"/> says how many there were.</summary>
    internal RdfTerm? Object(RdfTerm subject, RdfTerm predicate, out int count)
    {
        List<RdfTerm> found = Objects(subject, predicate);
        count = found.Count;
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The subjects that have <paramref name="obj"/> for <paramref name="predicate"/>, distinct.</summary>
    internal List<RdfTerm> Subjects(RdfTerm predicate, RdfTerm obj)
    {
        List<RdfTerm> found = [];
        if (!TryHandle(predicate, out TermHandle p) || !TryHandle(obj, out TermHandle o))
        {
            return found;
        }

        Collect(TermHandle.None, p, o, Position.Subject, found);
        return found;
    }

    /// <summary>Every subject of a triple with <paramref name="predicate"/>, distinct.</summary>
    internal List<RdfTerm> SubjectsOf(RdfTerm predicate)
    {
        List<RdfTerm> found = [];
        if (TryHandle(predicate, out TermHandle p))
        {
            Collect(TermHandle.None, p, TermHandle.None, Position.Subject, found);
        }

        return found;
    }

    /// <summary>Every (predicate, object) of <paramref name="subject"/>, distinct.</summary>
    internal List<(RdfTerm Predicate, RdfTerm Object)> Triples(RdfTerm subject)
    {
        List<(RdfTerm, RdfTerm)> found = [];
        if (!TryHandle(subject, out TermHandle s))
        {
            return found;
        }

        HashSet<(RdfTerm, RdfTerm)> seen = new(new PairComparer());
        using IQuadCursor cursor = Source.Match(s, TermHandle.None, TermHandle.None, Graph);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            (RdfTerm, RdfTerm) pair = (Term(quad.Predicate), Term(quad.Object));
            if (seen.Add(pair))
            {
                found.Add(pair);
            }
        }

        return found;
    }

    /// <summary>Every (subject, object) of a triple with <paramref name="predicate"/>, distinct.</summary>
    internal List<(RdfTerm Subject, RdfTerm Object)> Pairs(RdfTerm predicate)
    {
        List<(RdfTerm, RdfTerm)> found = [];
        if (!TryHandle(predicate, out TermHandle p))
        {
            return found;
        }

        HashSet<(RdfTerm, RdfTerm)> seen = new(new PairComparer());
        using IQuadCursor cursor = Source.Match(TermHandle.None, p, TermHandle.None, Graph);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            (RdfTerm, RdfTerm) pair = (Term(quad.Subject), Term(quad.Object));
            if (seen.Add(pair))
            {
                found.Add(pair);
            }
        }

        return found;
    }

    /// <summary>Whether the triple is in the graph.</summary>
    internal bool Has(RdfTerm subject, RdfTerm predicate, RdfTerm obj)
    {
        if (!TryHandle(subject, out TermHandle s) || !TryHandle(predicate, out TermHandle p) || !TryHandle(obj, out TermHandle o))
        {
            return false;
        }

        using IQuadCursor cursor = Source.Match(s, p, o, Graph);
        return cursor.MoveNext();
    }

    /// <summary>Whether <paramref name="node"/> appears in any triple of the graph.</summary>
    internal bool Mentions(RdfTerm node)
    {
        if (!TryHandle(node, out TermHandle h))
        {
            return false;
        }

        using IQuadCursor asSubject = Source.Match(h, TermHandle.None, TermHandle.None, Graph);
        if (asSubject.MoveNext())
        {
            return true;
        }

        using IQuadCursor asObject = Source.Match(TermHandle.None, TermHandle.None, h, Graph);
        return asObject.MoveNext();
    }

    /// <summary>
    /// The members of the SHACL list at <paramref name="head"/> (§1.1: every
    /// node has exactly one <c>rdf:first</c> and one <c>rdf:rest</c>, the
    /// list ends in <c>rdf:nil</c> and has no cycle), or null when it is not one.
    /// </summary>
    internal List<RdfTerm>? List(RdfTerm head)
    {
        List<RdfTerm> members = [];
        HashSet<RdfTerm> visited = new(RdfTerm.Comparer);
        RdfTerm cursor = head;
        while (!cursor.Equals(Vocabulary.RdfNil))
        {
            if (cursor.Kind != RdfTermKind.BlankNode && cursor.Kind != RdfTermKind.Iri || !visited.Add(cursor))
            {
                return null;
            }

            RdfTerm? first = Object(cursor, Vocabulary.RdfFirst, out int firsts);
            RdfTerm? rest = Object(cursor, Vocabulary.RdfRest, out int rests);
            if (firsts != 1 || rests != 1)
            {
                return null;
            }

            members.Add(first!);
            cursor = rest!;
        }

        return members;
    }

    /// <summary>Whether <paramref name="node"/> is a SHACL instance of <paramref name="type"/> in this graph: <c>rdf:type/rdfs:subClassOf*</c>.</summary>
    internal bool IsInstanceOf(RdfTerm node, RdfTerm type)
    {
        foreach (RdfTerm direct in Objects(node, Vocabulary.RdfType))
        {
            if (IsSubClassOf(direct, type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="cls"/> is <paramref name="ancestor"/> or a transitive subclass of it here.</summary>
    internal bool IsSubClassOf(RdfTerm cls, RdfTerm ancestor)
    {
        HashSet<RdfTerm> visited = new(RdfTerm.Comparer);
        Stack<RdfTerm> pending = new();
        pending.Push(cls);
        while (pending.Count > 0)
        {
            RdfTerm current = pending.Pop();
            if (current.Equals(ancestor))
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (RdfTerm parent in Objects(current, Vocabulary.RdfsSubClassOf))
            {
                pending.Push(parent);
            }
        }

        return false;
    }

    /// <summary>
    /// The handle of a term this graph handed out, or of any term the source
    /// holds; false for a term it does not hold.
    /// </summary>
    internal bool TryHandle(RdfTerm term, out TermHandle handle)
    {
        if (_handles.TryGetValue(term, out handle))
        {
            return true;
        }

        if (term.Kind == RdfTermKind.BlankNode)
        {
            // A blank node is only ever looked up by the handle it came with (ADR 0044).
            handle = TermHandle.None;
            return false;
        }

        if (term.Kind == RdfTermKind.TripleTerm)
        {
            if (!TryHandle(term.Subject!, out _) || !TryHandle(term.Predicate!, out _) || !TryHandle(term.Object!, out _))
            {
                handle = TermHandle.None;
                return false;
            }
        }

        if (Source.TryInternalise(term, out handle))
        {
            _handles[term] = handle;
            return true;
        }

        return false;
    }

    /// <summary>The term of a handle of this source, remembered so it can be looked up again.</summary>
    internal RdfTerm Term(TermHandle handle)
    {
        if (_terms.TryGetValue(handle, out RdfTerm? term))
        {
            return term;
        }

        if (!Source.TryExternalise(handle, out term))
        {
            throw new InvalidOperationException("The shapes source gave a handle it cannot externalise.");
        }

        _terms[handle] = term;
        _handles.TryAdd(term, handle);
        return term;
    }

    private void Collect(TermHandle s, TermHandle p, TermHandle o, Position position, List<RdfTerm> into)
    {
        HashSet<TermHandle> seen = new(Source.TermComparer);
        using IQuadCursor cursor = Source.Match(s, p, o, Graph);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            TermHandle h = position == Position.Subject ? quad.Subject : quad.Object;
            if (seen.Add(h))
            {
                into.Add(Term(h));
            }
        }
    }

    private enum Position
    {
        Subject,
        Object,
    }

    private sealed class PairComparer : IEqualityComparer<(RdfTerm, RdfTerm)>
    {
        public bool Equals((RdfTerm, RdfTerm) x, (RdfTerm, RdfTerm) y) =>
            RdfTerm.Comparer.Equals(x.Item1, y.Item1) && RdfTerm.Comparer.Equals(x.Item2, y.Item2);

        public int GetHashCode((RdfTerm, RdfTerm) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Item1), RdfTerm.Comparer.GetHashCode(obj.Item2));
    }
}
