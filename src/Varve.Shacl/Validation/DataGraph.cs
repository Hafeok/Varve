// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;

namespace Varve.Shacl.Validation;

/// <summary>
/// The data graph: a graph pattern of a quad source, read through the quad
/// source contract by handle. A constant is looked up and never added
/// (ADR 0151); a term is externalised once per validation.
/// </summary>
internal sealed class DataGraph
{
    private readonly Dictionary<TermHandle, RdfTerm> _terms;
    private readonly Dictionary<RdfTerm, Node> _resolved = new(RdfTerm.Comparer);

    private readonly Graphs.ShapesSource? _sameSource;

    /// <param name="source">The data source.</param>
    /// <param name="graph">The data graph in it.</param>
    /// <param name="sameSource">
    /// The shapes source when it is this very source: then a blank node of the
    /// shapes graph is a node of the data too, found by the handle it came with.
    /// </param>
    internal DataGraph(IQuadSource source, GraphPattern graph, Graphs.ShapesSource? sameSource)
    {
        _sameSource = sameSource;
        Source = source;
        Graph = graph;
        Nodes = new NodeComparer(source.TermComparer);
        _terms = new Dictionary<TermHandle, RdfTerm>(source.TermComparer);
    }

    internal IQuadSource Source { get; }

    internal GraphPattern Graph { get; }

    internal NodeComparer Nodes { get; }

    /// <summary>A term as a node: the source's handle if it holds the term, the term otherwise. Never adds it.</summary>
    internal Node Resolve(RdfTerm term)
    {
        if (_resolved.TryGetValue(term, out Node node))
        {
            return node;
        }

        TermHandle handle = TermHandle.None;
        bool held = _sameSource is not null
            ? _sameSource.TryHandle(term, out handle)
            : term.Kind != RdfTermKind.BlankNode && Source.TryInternalise(term, out handle);
        node = held ? Node.HeldAs(handle, term) : Node.Absent(term);
        _resolved[term] = node;
        return node;
    }

    /// <summary>The handle of a constant, or false when the data does not hold it.</summary>
    internal bool TryHandle(RdfTerm term, out TermHandle handle)
    {
        Node node = Resolve(term);
        handle = node.Handle;
        return node.IsHeld;
    }

    /// <summary>The node's term, externalised once.</summary>
    internal RdfTerm Term(Node node)
    {
        if (node.Term is { } known)
        {
            return known;
        }

        if (_terms.TryGetValue(node.Handle, out RdfTerm? term))
        {
            return term;
        }

        if (!Source.TryExternalise(node.Handle, out term))
        {
            throw new InvalidOperationException("The data source gave a handle it cannot externalise.");
        }

        _terms[node.Handle] = term;
        return term;
    }

    /// <summary>The objects of (<paramref name="subject"/>, <paramref name="predicate"/>), distinct.</summary>
    internal void Objects(Node subject, TermHandle predicate, List<Node> into, HashSet<Node> seen)
    {
        if (!subject.IsHeld)
        {
            return;
        }

        using IQuadCursor cursor = Source.Match(subject.Handle, predicate, TermHandle.None, Graph);
        while (cursor.MoveNext())
        {
            Node o = Node.Held(cursor.Current.Object);
            if (seen.Add(o))
            {
                into.Add(o);
            }
        }
    }

    /// <summary>The subjects of (<paramref name="predicate"/>, <paramref name="obj"/>), distinct.</summary>
    internal void Subjects(TermHandle predicate, Node obj, List<Node> into, HashSet<Node> seen)
    {
        if (!obj.IsHeld)
        {
            return;
        }

        using IQuadCursor cursor = Source.Match(TermHandle.None, predicate, obj.Handle, Graph);
        while (cursor.MoveNext())
        {
            Node s = Node.Held(cursor.Current.Subject);
            if (seen.Add(s))
            {
                into.Add(s);
            }
        }
    }

    /// <summary>Whether the triple is in the data graph.</summary>
    internal bool Has(Node subject, TermHandle predicate, Node obj)
    {
        if (!subject.IsHeld || !obj.IsHeld)
        {
            return false;
        }

        using IQuadCursor cursor = Source.Match(subject.Handle, predicate, obj.Handle, Graph);
        return cursor.MoveNext();
    }

    /// <summary>Every triple with <paramref name="subject"/> as subject, as (predicate, object).</summary>
    internal List<(Node Predicate, Node Object)> Triples(Node subject)
    {
        List<(Node, Node)> found = [];
        if (!subject.IsHeld)
        {
            return found;
        }

        using IQuadCursor cursor = Source.Match(subject.Handle, TermHandle.None, TermHandle.None, Graph);
        while (cursor.MoveNext())
        {
            found.Add((Node.Held(cursor.Current.Predicate), Node.Held(cursor.Current.Object)));
        }

        return found;
    }

    /// <summary>Every node of the data graph — the subjects and objects of its triples (SPARQL's nodes(G)) — distinct.</summary>
    internal List<Node> AllNodes()
    {
        List<Node> found = [];
        HashSet<Node> seen = new(Nodes);
        using IQuadCursor cursor = Source.Match(TermHandle.None, TermHandle.None, TermHandle.None, Graph);
        while (cursor.MoveNext())
        {
            Node s = Node.Held(cursor.Current.Subject);
            Node o = Node.Held(cursor.Current.Object);
            if (seen.Add(s))
            {
                found.Add(s);
            }

            if (seen.Add(o))
            {
                found.Add(o);
            }
        }

        return found;
    }
}
