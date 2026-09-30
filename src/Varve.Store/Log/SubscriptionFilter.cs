// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store.Log;

/// <summary>
/// What a subscription wants: every quad, a graph pattern, or a quad pattern.
/// It restricts each delivered delta; <see cref="CommitKind.Settings"/> and
/// <see cref="CommitKind.Erasure"/> commits pass whatever it says
/// (specification 1.2, §8; ADR 0046).
/// </summary>
public readonly struct SubscriptionFilter : IEquatable<SubscriptionFilter>
{
    private readonly bool _restricted;

    private SubscriptionFilter(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        _restricted = true;
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        Graph = graph;
    }

    /// <summary>Everything.</summary>
    public static SubscriptionFilter All => default;

    /// <summary>The subject to match, or <see cref="TermHandle.None"/> for any.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public TermHandle Subject { get; }

    /// <summary>The predicate to match, or <see cref="TermHandle.None"/> for any.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public TermHandle Predicate { get; }

    /// <summary>The object to match, or <see cref="TermHandle.None"/> for any.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public TermHandle Object { get; }

    /// <summary>The graphs to match. Ignored for <see cref="All"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public GraphPattern Graph { get; }

    /// <summary>Quads in the graphs a pattern names.</summary>
    public static SubscriptionFilter ForGraph(GraphPattern graph) =>
        new(TermHandle.None, TermHandle.None, TermHandle.None, graph);

    /// <summary>Quads matching a pattern; <see cref="TermHandle.None"/> is a wildcard.</summary>
    public static SubscriptionFilter ForPattern(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new(subject, predicate, @object, graph);

    /// <summary>Whether a quad passes.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Matches(in Quad quad) =>
        !_restricted
        || ((Subject.IsNone || Subject == quad.Subject)
            && (Predicate.IsNone || Predicate == quad.Predicate)
            && (Object.IsNone || Object == quad.Object)
            && Graph.Matches(quad.Graph));

    /// <inheritdoc />
    public bool Equals(SubscriptionFilter other) =>
        _restricted == other._restricted && Subject == other.Subject && Predicate == other.Predicate
        && Object == other.Object && Graph == other.Graph;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SubscriptionFilter other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_restricted, Subject, Predicate, Object, Graph);

    /// <summary>Compares every field.</summary>
    public static bool operator ==(SubscriptionFilter left, SubscriptionFilter right) => left.Equals(right);

    /// <summary>Compares every field.</summary>
    public static bool operator !=(SubscriptionFilter left, SubscriptionFilter right) => !left.Equals(right);
}
