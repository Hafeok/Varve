// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

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
/// A graph's governor (ADR 0107, amended 2026-10-15): when
/// <paramref name="graph"/> is governed by another graph, answers
/// <see langword="true"/> with that graph in <paramref name="governor"/>,
/// <see langword="null"/> for the default graph. A scope allows a governed graph
/// exactly when it allows its governor, and a grant naming the governed graph
/// itself has no effect. <see cref="Varve.Rdf"/> knows no rule that names a
/// governor; the host composes one into a caller's scope.
/// </summary>
[Contract(typeof(GraphLevelAuthorisation.GoverningGraphSeam), Role = "the rule a host composes into a scope that names a graph's governor")]
public delegate bool GraphGovernor(RdfTerm graph, out RdfTerm? governor);

/// <summary>
/// A set of graphs (ADR 0107): every graph, or the default graph and named
/// graphs listed by IRI or by IRI prefix. The unit of a grant's scope, and
/// what <see cref="GraphScopedQuadSource"/> filters by. Immutable; two scopes
/// combine by <see cref="Union"/>.
/// </summary>
public sealed class GraphScope
{
    private readonly RdfTerm[] _graphs;
    private readonly byte[][] _prefixes;

    private GraphScope(RdfTerm[] graphs, byte[][] prefixes, bool includesDefault, bool all, GraphGovernor? governor = null)
    {
        _graphs = graphs;
        _prefixes = prefixes;
        IncludesDefault = includesDefault;
        IsAll = all;
        Governor = governor;
    }

    /// <summary>The rule that names a graph's governor, when the scope has one.</summary>
    public GraphGovernor? Governor { get; }

    /// <summary>
    /// The same set, with <paramref name="governor"/> deciding governed graphs:
    /// such a graph is allowed exactly when its governor is. The scope of every
    /// graph stays itself, since it allows every governor too.
    /// </summary>
    public GraphScope WithGovernor(GraphGovernor governor)
    {
        ArgumentNullException.ThrowIfNull(governor);
        return IsAll ? this : new GraphScope(_graphs, _prefixes, IncludesDefault, all: false, governor);
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

    /// <summary>The IRI prefixes the set names graphs by, in the order given: what identifies the set beside <see cref="Graphs"/>, as a store that keeps something per scope needs.</summary>
    [DesignDecision(typeof(TheValidationProjection.SnapshotIdentifiesItsBinding), Scope = ExceptionScope.Boundary)]
    public IReadOnlyList<string> Prefixes => Array.ConvertAll(_prefixes, static prefix => Encoding.UTF8.GetString(prefix));

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

        if (Governor is not null && Governor(graph, out RdfTerm? governor))
        {
            // A governed graph's own grant has no effect; its governor's does.
            // A chain of governors ends, since each is a different graph.
            return !graph.Equals(governor) && Allows(governor);
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

        return new GraphScope([.. graphs], [.. prefixes], IncludesDefault || other.IncludesDefault, all: false, Governor ?? other.Governor);
    }
}
