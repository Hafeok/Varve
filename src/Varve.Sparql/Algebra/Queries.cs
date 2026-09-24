// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>A query: the prologue, the dataset clause, and one of four forms.</summary>
public abstract record Query(Prologue Prologue, DatasetSpec? Dataset) : AlgebraNode
{
    /// <summary>The graph pattern, with the solution modifiers already applied (<c>docs/spec/sparql-algebra.md</c> §4.7).</summary>
    public abstract QueryPattern Pattern { get; init; }
}

/// <summary><c>SELECT</c>. The pattern ends in the <see cref="Project"/> and whatever modifiers surround it.</summary>
public sealed record SelectQuery(Prologue Prologue, DatasetSpec? Dataset, QueryPattern Pattern) : Query(Prologue, Dataset)
{
    /// <inheritdoc />
    public override QueryPattern Pattern { get; init; } = Pattern;
}

/// <summary><c>CONSTRUCT</c>, with its template.</summary>
public sealed record ConstructQuery(Prologue Prologue, DatasetSpec? Dataset, AlgebraList<TriplePattern> Template, QueryPattern Pattern)
    : Query(Prologue, Dataset)
{
    /// <inheritdoc />
    public override QueryPattern Pattern { get; init; } = Pattern;
}

/// <summary><c>ASK</c>.</summary>
public sealed record AskQuery(Prologue Prologue, DatasetSpec? Dataset, QueryPattern Pattern) : Query(Prologue, Dataset)
{
    /// <inheritdoc />
    public override QueryPattern Pattern { get; init; } = Pattern;
}

/// <summary><c>DESCRIBE</c>: the resources are variables and IRIs; <c>DESCRIBE *</c> names the in-scope variables.</summary>
public sealed record DescribeQuery(Prologue Prologue, DatasetSpec? Dataset, AlgebraList<PatternTerm> Resources, QueryPattern Pattern)
    : Query(Prologue, Dataset)
{
    /// <inheritdoc />
    public override QueryPattern Pattern { get; init; } = Pattern;
}

/// <summary>
/// What was declared before the query: the base, the prefixes in order, and
/// the version label. The tree's terms are already resolved and expanded;
/// this is kept for the serialiser.
/// </summary>
public sealed record Prologue(RdfTerm? Base, AlgebraList<PrefixDeclaration> Prefixes, SparqlVersion? Version) : AlgebraNode
{
    /// <summary>No base, no prefixes, no version.</summary>
    public static Prologue Empty { get; } = new(null, default, null);
}

/// <summary>One <c>PREFIX</c> declaration; the prefix is without its colon.</summary>
public sealed record PrefixDeclaration(string Prefix, RdfTerm Iri) : AlgebraNode;

/// <summary><c>FROM</c> and <c>FROM NAMED</c>; <c>USING</c> and <c>USING NAMED</c> in an update.</summary>
public sealed record DatasetSpec(AlgebraList<RdfTerm> DefaultGraphs, AlgebraList<RdfTerm> NamedGraphs) : AlgebraNode;
