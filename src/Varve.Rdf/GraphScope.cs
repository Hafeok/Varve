// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;

namespace Varve.Rdf;

/// <summary>Whether a graph set takes in the default graph.</summary>
public enum DefaultGraphAccess : byte
{
    /// <summary>The default graph is outside the set.</summary>
    Excluded,

    /// <summary>The default graph is in the set.</summary>
    Included,
}

/// <summary>
/// A set of graphs (ADR 0106): every graph, or the default graph and named
/// graphs listed by IRI or by IRI prefix. The unit of a grant's scope, and
/// what <see cref="GraphScopedQuadSource"/> filters by. Immutable; two scopes
/// combine by <see cref="Union"/>.
/// </summary>
public sealed class GraphScope
{
    private readonly RdfTerm[] _graphs;
    private readonly byte[][] _prefixes;

    private GraphScope(RdfTerm[] graphs, byte[][] prefixes, bool includesDefault, bool all)
    {
        _graphs = graphs;
        _prefixes = prefixes;
        IncludesDefault = includesDefault;
        IsAll = all;
    }

    /// <summary>Every graph, the default graph included: the scope of an unscoped grant.</summary>
    public static GraphScope All { get; } = new([], [], includesDefault: true, all: true);

    /// <summary>No graph at all.</summary>
    public static GraphScope None { get; } = new([], [], includesDefault: false, all: false);

    /// <summary>Whether this is every graph.</summary>
    public bool IsAll { get; }

    /// <summary>Whether the default graph is in the set.</summary>
    public bool IncludesDefault { get; }

    /// <summary>The named graphs listed by IRI; a prefix grant's graphs are not listed.</summary>
    public IReadOnlyList<RdfTerm> Graphs => _graphs;

    /// <summary>Whether the set names graphs by prefix.</summary>
    public bool HasPrefixes => _prefixes.Length > 0;

    /// <summary>
    /// The set of <paramref name="graphs"/>, each an IRI, every named graph
    /// whose IRI starts with one of <paramref name="prefixes"/>, and the
    /// default graph when <paramref name="defaultGraph"/> says so.
    /// </summary>
    /// <exception cref="ArgumentException">A graph is not an IRI, or a prefix is empty.</exception>
    public static GraphScope Of(ReadOnlySpan<RdfTerm> graphs, ReadOnlySpan<string> prefixes, DefaultGraphAccess defaultGraph)
    {
        RdfTerm[] named = new RdfTerm[graphs.Length];

        for (int i = 0; i < graphs.Length; i++)
        {
            RdfTerm graph = graphs[i] ?? throw new ArgumentException("A graph in a scope is an IRI.", nameof(graphs));

            if (graph.Kind != RdfTermKind.Iri)
            {
                throw new ArgumentException("A graph in a scope is an IRI.", nameof(graphs));
            }

            named[i] = graph;
        }

        byte[][] starts = new byte[prefixes.Length][];

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (string.IsNullOrEmpty(prefixes[i]))
            {
                throw new ArgumentException("A prefix in a scope is not empty.", nameof(prefixes));
            }

            starts[i] = Encoding.UTF8.GetBytes(prefixes[i]);
        }

        return new GraphScope(named, starts, defaultGraph == DefaultGraphAccess.Included, all: false);
    }

    /// <summary>Whether <paramref name="graph"/> is in the set; <see langword="null"/> is the default graph.</summary>
    public bool Allows(RdfTerm? graph)
    {
        if (IsAll)
        {
            return true;
        }

        if (graph is null)
        {
            return IncludesDefault;
        }

        foreach (RdfTerm named in _graphs)
        {
            if (named.Equals(graph))
            {
                return true;
            }
        }

        return AllowsByPrefix(graph.Lexical);
    }

    /// <summary>Whether a named graph's IRI starts with one of the prefixes.</summary>
    internal bool AllowsByPrefix(ReadOnlySpan<byte> iri)
    {
        foreach (byte[] prefix in _prefixes)
        {
            if (iri.StartsWith(prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The graphs of both.</summary>
    public GraphScope Union(GraphScope other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (IsAll || other.IsAll)
        {
            return All;
        }

        List<RdfTerm> graphs = [.. _graphs];

        foreach (RdfTerm graph in other._graphs)
        {
            if (!graphs.Contains(graph))
            {
                graphs.Add(graph);
            }
        }

        List<byte[]> prefixes = [.. _prefixes];

        foreach (byte[] prefix in other._prefixes)
        {
            bool known = false;

            foreach (byte[] mine in _prefixes)
            {
                if (mine.AsSpan().SequenceEqual(prefix))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                prefixes.Add(prefix);
            }
        }

        return new GraphScope([.. graphs], [.. prefixes], IncludesDefault || other.IncludesDefault, all: false);
    }
}
