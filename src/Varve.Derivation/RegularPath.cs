// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Derivation;

/// <summary>The forms of a regular path (ADR 0172), each a factory of <see cref="RegularPath{TLabel}"/>.</summary>
[Contract(typeof(VarveDerivation.PathsAreRegularPaths), Role = "the forms of a path expression")]
public static class RegularPath
{
    /// <summary>One edge labelled <paramref name="label"/>, forwards.</summary>
    public static RegularPath<TLabel> Edge<TLabel>(TLabel label) => new EdgePath<TLabel>(label, forward: true);

    /// <summary>The inverse of <paramref name="path"/>: its walks backwards.</summary>
    public static RegularPath<TLabel> Inverse<TLabel>(RegularPath<TLabel> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Inverted();
    }

    /// <summary>The steps one after the other.</summary>
    public static RegularPath<TLabel> Sequence<TLabel>(IEnumerable<RegularPath<TLabel>> steps) => new CompoundPath<TLabel>(PathKind.Sequence, [.. steps]);

    /// <summary>Any one of the alternatives.</summary>
    public static RegularPath<TLabel> Alternative<TLabel>(IEnumerable<RegularPath<TLabel>> alternatives) => new CompoundPath<TLabel>(PathKind.Alternative, [.. alternatives]);

    /// <summary>Zero or more repetitions.</summary>
    public static RegularPath<TLabel> ZeroOrMore<TLabel>(RegularPath<TLabel> path) => new CompoundPath<TLabel>(PathKind.ZeroOrMore, [path]);

    /// <summary>One or more repetitions.</summary>
    public static RegularPath<TLabel> OneOrMore<TLabel>(RegularPath<TLabel> path) => new CompoundPath<TLabel>(PathKind.OneOrMore, [path]);

    /// <summary>Zero repetitions or one.</summary>
    public static RegularPath<TLabel> ZeroOrOne<TLabel>(RegularPath<TLabel> path) => new CompoundPath<TLabel>(PathKind.ZeroOrOne, [path]);
}

/// <summary>
/// A regular path over labelled edges (ADR 0172): SHACL's paths (SHACL 1.2
/// Core §4) and SPARQL's property paths without negated sets. Denotes, from a
/// start node, the <em>set</em> of nodes some walk matching it reaches
/// (ADR 0152). Built by <see cref="RegularPath"/>; compile it once with <see cref="Compile"/>.
/// </summary>
/// <typeparam name="TLabel">The caller's edge label, such as a predicate.</typeparam>
[Contract(typeof(VarveDerivation.PathsAreRegularPaths), Role = "a path expression, compiled to an automaton")]
public abstract class RegularPath<TLabel>
{
    private protected RegularPath()
    {
    }

    /// <summary>The path's automaton, built once and safe to share.</summary>
    public PathAutomaton<TLabel> Compile() => PathAutomaton<TLabel>.Build(this);

    /// <summary>The same path with every edge reversed and every sequence read backwards.</summary>
    internal abstract RegularPath<TLabel> Inverted();
}

internal enum PathKind
{
    Sequence,
    Alternative,
    ZeroOrMore,
    OneOrMore,
    ZeroOrOne,
}

internal sealed class EdgePath<TLabel>(TLabel label, bool forward) : RegularPath<TLabel>
{
    internal TLabel Label { get; } = label;

    internal bool Forward { get; } = forward;

    internal override RegularPath<TLabel> Inverted() => new EdgePath<TLabel>(Label, !Forward);
}

internal sealed class CompoundPath<TLabel> : RegularPath<TLabel>
{
    internal CompoundPath(PathKind kind, ImmutableArray<RegularPath<TLabel>> parts)
    {
        foreach (RegularPath<TLabel> part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
        }

        if (parts.IsEmpty)
        {
            throw new ArgumentException("A path needs at least one part.", nameof(parts));
        }

        Kind = kind;
        Parts = parts;
    }

    internal PathKind Kind { get; }

    internal ImmutableArray<RegularPath<TLabel>> Parts { get; }

    internal override RegularPath<TLabel> Inverted()
    {
        ImmutableArray<RegularPath<TLabel>>.Builder parts = ImmutableArray.CreateBuilder<RegularPath<TLabel>>(Parts.Length);
        for (int i = 0; i < Parts.Length; i++)
        {
            // ^(a/b) = ^b/^a; every other kind keeps its order.
            parts.Add(Parts[Kind == PathKind.Sequence ? Parts.Length - 1 - i : i].Inverted());
        }

        return new CompoundPath<TLabel>(Kind, parts.MoveToImmutable());
    }
}
