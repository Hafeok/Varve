// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Rdf;

/// <summary>A forward-only walk over distinct terms, disposed when the caller is done with it.</summary>
[Contract(typeof(RdfsRdfAndDByRewriting.DistinctTermsBySeek), Role = "the walk a distinct-term enumeration answers with")]
public interface ITermCursor : IDisposable
{
    /// <summary>Advances to the next distinct term, or returns false at the end.</summary>
    bool MoveNext();

    /// <summary>The term at the current position.</summary>
    TermHandle Current { get; }
}

/// <summary>
/// A quad source that enumerates the distinct terms in one position of one
/// graph's quads by seeking past each, one seek per distinct term, where a
/// scan would read every quad (ADR 0193). Optional: a source that implements
/// it may still refuse a pattern it cannot seek, and
/// <see cref="DistinctTerms"/> then reads the graph whole.
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.DistinctTermsBySeek), Role = "a quad source that seeks to the next distinct term")]
public interface IDistinctTermSource
{
    /// <summary>
    /// The distinct predicates of the quads of <paramref name="graph"/>, a
    /// single graph (<see cref="GraphMatch.DefaultGraph"/> or
    /// <see cref="GraphMatch.Named"/>); false when this source cannot seek them.
    /// </summary>
    bool TryDistinctPredicates(GraphPattern graph, [NotNullWhen(true)] out ITermCursor? predicates);

    /// <summary>
    /// The distinct objects of the quads of <paramref name="graph"/>, a single
    /// graph, under <paramref name="predicate"/>; false when this source cannot
    /// seek them.
    /// </summary>
    bool TryDistinctObjects(TermHandle predicate, GraphPattern graph, [NotNullWhen(true)] out ITermCursor? objects);
}

/// <summary>
/// Distinct terms of one graph's quads: by a source's seek where it has one,
/// otherwise by reading the quads and keeping each term once (ADR 0193).
/// </summary>
[Contract(typeof(RdfsRdfAndDByRewriting.DistinctTermsBySeek), Role = "distinct terms by seek, or by a read where a source cannot seek")]
public static class DistinctTerms
{
    /// <summary>The distinct predicates of <paramref name="graph"/>'s quads in <paramref name="source"/>.</summary>
    public static List<TermHandle> Predicates(IQuadSource source, GraphPattern graph)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is IDistinctTermSource seeking && Single(graph) && seeking.TryDistinctPredicates(graph, out ITermCursor? cursor))
        {
            return Drain(cursor);
        }

        return Read(source, TermHandle.None, graph, static quad => quad.Predicate);
    }

    /// <summary>The distinct objects of <paramref name="graph"/>'s quads under <paramref name="predicate"/> in <paramref name="source"/>.</summary>
    public static List<TermHandle> Objects(IQuadSource source, TermHandle predicate, GraphPattern graph)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (predicate.IsNone)
        {
            throw new ArgumentException("The predicate is bound: the objects of one predicate are enumerated.", nameof(predicate));
        }

        if (source is IDistinctTermSource seeking && Single(graph) && seeking.TryDistinctObjects(predicate, graph, out ITermCursor? cursor))
        {
            return Drain(cursor);
        }

        return Read(source, predicate, graph, static quad => quad.Object);
    }

    private static bool Single(GraphPattern graph) => graph.Match is GraphMatch.DefaultGraph or GraphMatch.Named;

    private static List<TermHandle> Drain(ITermCursor cursor)
    {
        using (cursor)
        {
            List<TermHandle> terms = [];
            while (cursor.MoveNext())
            {
                terms.Add(cursor.Current);
            }

            return terms;
        }
    }

    private static List<TermHandle> Read(IQuadSource source, TermHandle predicate, GraphPattern graph, Func<Quad, TermHandle> of)
    {
        HashSet<TermHandle> seen = new(source.TermComparer);
        List<TermHandle> terms = [];
        using IQuadCursor cursor = source.Match(TermHandle.None, predicate, TermHandle.None, graph);
        while (cursor.MoveNext())
        {
            TermHandle term = of(cursor.Current);
            if (seen.Add(term))
            {
                terms.Add(term);
            }
        }

        return terms;
    }
}

/// <summary>A term cursor over a list already held.</summary>
internal sealed class ListTermCursor(List<TermHandle> terms) : ITermCursor
{
    private int _at = -1;

    public TermHandle Current => terms[_at];

    public bool MoveNext() => ++_at < terms.Count;

    public void Dispose()
    {
    }
}
