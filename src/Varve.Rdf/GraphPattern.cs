// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Rdf;

/// <summary>Which graphs a match ranges over.</summary>
/// <remarks>
/// Four modes, not a wildcard, because "any graph" is two different questions
/// and a single sentinel cannot tell them apart. SPARQL's <c>GRAPH ?g</c>
/// ranges over the named graphs and **not** the default graph
/// (SPARQL 1.1 §13.3); a serialiser writing a whole dataset wants both. Under a
/// wildcard those two differ by a quiet inclusion nobody would notice was
/// wrong.
/// </remarks>
public enum GraphMatch : byte
{
    /// <summary>The default graph, and nothing else.</summary>
    DefaultGraph,

    /// <summary>One named graph, given by <see cref="GraphPattern.Graph"/>.</summary>
    Named,

    /// <summary>Every named graph. The default graph is excluded. SPARQL's <c>GRAPH ?g</c>.</summary>
    AnyNamed,

    /// <summary>Every graph, the default graph included.</summary>
    Any,
}

/// <summary>
/// The graph half of a match pattern.
/// </summary>
/// <remarks>
/// <para>
/// Subject, predicate and object keep <see cref="TermHandle.None"/> as their
/// wildcard, which is unambiguous: a quad always has all three, so "no term
/// given" can only mean "any term". The graph position is different — a quad
/// may have no graph — so one value would have to mean both "the graph that
/// isn't there" and "whichever graph is". This type is what stops it.
/// </para>
/// <para>
/// <see cref="Any"/> is kept beside <see cref="AnyNamed"/> rather than left to
/// the caller because counting a dataset and serialising it are real operations
/// that are not <c>GRAPH ?g</c>, and a caller unioning two cursors to get there
/// is a caller who can get the arithmetic wrong.
/// </para>
/// </remarks>
public readonly struct GraphPattern : IEquatable<GraphPattern>
{
    private GraphPattern(GraphMatch match, TermHandle graph)
    {
        Match = match;
        Graph = graph;
    }

    /// <summary>Which graphs this pattern ranges over.</summary>
    public GraphMatch Match { get; }

    /// <summary>
    /// The graph named, when <see cref="Match"/> is <see cref="GraphMatch.Named"/>.
    /// <see cref="TermHandle.None"/> otherwise.
    /// </summary>
    public TermHandle Graph { get; }

    /// <summary>The default graph, and nothing else.</summary>
    public static GraphPattern DefaultGraph => new(GraphMatch.DefaultGraph, TermHandle.None);

    /// <summary>Every named graph, excluding the default graph. SPARQL 1.1 §13.3.</summary>
    public static GraphPattern AnyNamed => new(GraphMatch.AnyNamed, TermHandle.None);

    /// <summary>Every graph, the default graph included.</summary>
    public static GraphPattern Any => new(GraphMatch.Any, TermHandle.None);

    /// <summary>
    /// One named graph. <see cref="TermHandle.None"/> is refused: it is not the
    /// name of a graph, and accepting it would let the ambiguity this type
    /// exists to remove back in through the front door.
    /// </summary>
    public static GraphPattern Named(TermHandle graph)
    {
        if (graph.IsNone)
        {
            throw new ArgumentException(
                "TermHandle.None is not the name of a graph. Use GraphPattern.DefaultGraph for the "
                + "default graph, or AnyNamed for every named graph.",
                nameof(graph));
        }

        return new GraphPattern(GraphMatch.Named, graph);
    }

    /// <summary>
    /// Whether a quad's graph position satisfies this pattern. The four modes
    /// partition every quad: for any graph handle, exactly one of
    /// <see cref="DefaultGraph"/> and <see cref="AnyNamed"/> accepts it, and
    /// <see cref="Any"/> accepts it either way.
    /// </summary>
    public bool Matches(TermHandle graph) => Match switch
    {
        GraphMatch.DefaultGraph => graph.IsNone,
        GraphMatch.Named => !graph.IsNone && Graph.Equals(graph),
        GraphMatch.AnyNamed => !graph.IsNone,
        _ => true,
    };

    /// <inheritdoc />
    public bool Equals(GraphPattern other) => Match == other.Match && Graph.Equals(other.Graph);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GraphPattern other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Match, Graph);

    /// <summary>Compares the mode and the named graph.</summary>
    public static bool operator ==(GraphPattern left, GraphPattern right) => left.Equals(right);

    /// <summary>Compares the mode and the named graph.</summary>
    public static bool operator !=(GraphPattern left, GraphPattern right) => !left.Equals(right);
}
