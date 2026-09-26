// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>
/// An algebraic query expression (SPARQL 1.2 §18.2); <c>docs/spec/sparql-algebra.md</c>
/// §2.4. Not <c>GraphPattern</c>, which is <c>Varve.Rdf</c>'s name for the
/// graph half of a match pattern, and which the evaluator uses in the same
/// files as this.
/// </summary>
[Contract(typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords), Role = "a graph pattern node")]
public abstract record QueryPattern : AlgebraNode
{
    private protected QueryPattern()
    {
    }
}

/// <summary><c>BGP(…)</c>. Empty for <c>{}</c>, the identity for <see cref="Join"/>.</summary>
public sealed record Bgp(AlgebraList<TriplePattern> Triples) : QueryPattern;

/// <summary><c>Path(x, ppe, y)</c>.</summary>
public sealed record PathPattern(PatternTerm Subject, PropertyPath Path, PatternTerm Object) : QueryPattern;

/// <summary><c>Join(left, right)</c>.</summary>
public sealed record Join(QueryPattern Left, QueryPattern Right) : QueryPattern;

/// <summary><c>LeftJoin(left, right, expr)</c>; a <see langword="null"/> condition is <c>true</c>.</summary>
public sealed record LeftJoin(QueryPattern Left, QueryPattern Right, Expression? Condition) : QueryPattern;

/// <summary><c>Filter(expr, inner)</c>.</summary>
public sealed record Filter(Expression Condition, QueryPattern Inner) : QueryPattern;

/// <summary><c>Union(left, right)</c>.</summary>
public sealed record Union(QueryPattern Left, QueryPattern Right) : QueryPattern;

/// <summary><c>Graph(name, inner)</c>; the name is a variable or an IRI.</summary>
public sealed record Graph(PatternTerm Name, QueryPattern Inner) : QueryPattern;

/// <summary><c>Extend(inner, var, expr)</c>.</summary>
public sealed record Extend(QueryPattern Inner, Variable Variable, Expression Expression) : QueryPattern;

/// <summary><c>Minus(left, right)</c>.</summary>
public sealed record Minus(QueryPattern Left, QueryPattern Right) : QueryPattern;

/// <summary>
/// A multiset of solution mappings written inline: <c>VALUES</c>. Each row has
/// one entry per variable; <see langword="null"/> is <c>UNDEF</c>.
/// </summary>
public sealed record Values(AlgebraList<Variable> Variables, AlgebraList<AlgebraList<RdfTerm?>> Rows) : QueryPattern;

/// <summary><c>SERVICE</c>: a node an evaluator may refuse (<c>docs/spec/sparql-algebra.md</c> §5).</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record Service(PatternTerm Name, QueryPattern Inner, bool Silent) : QueryPattern;

/// <summary>
/// <c>Group(keys, inner)</c>. The aggregates over it are the
/// <see cref="AggregateExpression"/>s in the <see cref="Extend"/>,
/// <see cref="Filter"/> and <see cref="OrderBy"/> nodes above it
/// (<c>docs/spec/sparql-algebra.md</c> §4.6). Empty keys is implicit grouping.
/// </summary>
public sealed record Group(QueryPattern Inner, AlgebraList<GroupKey> Keys) : QueryPattern;

/// <summary>
/// One <c>GROUP BY</c> condition. <c>?v</c> is <c>(?v, ?v)</c>; <c>(expr AS ?v)</c>
/// is <c>(expr, ?v)</c>; <c>(expr)</c> is <c>(expr, null)</c>, a key not in
/// scope by name.
/// </summary>
public sealed record GroupKey(Expression Expression, Variable? Variable) : AlgebraNode;

/// <summary><c>OrderBy(inner, conditions)</c>.</summary>
public sealed record OrderBy(QueryPattern Inner, AlgebraList<OrderCondition> Conditions) : QueryPattern;

/// <summary>One <c>ORDER BY</c> condition.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.GrammarKeywordsAreBools), Scope = ExceptionScope.Boundary)]
public sealed record OrderCondition(Expression Expression, bool Descending) : AlgebraNode;

/// <summary><c>Project(inner, variables)</c>, in the order named.</summary>
public sealed record Project(QueryPattern Inner, AlgebraList<Variable> Variables) : QueryPattern;

/// <summary><c>Distinct(inner)</c>.</summary>
public sealed record Distinct(QueryPattern Inner) : QueryPattern;

/// <summary><c>Reduced(inner)</c>.</summary>
public sealed record Reduced(QueryPattern Inner) : QueryPattern;

/// <summary><c>Slice(inner, offset, limit)</c>; a <see langword="null"/> limit is unbounded.</summary>
[DesignDecision(typeof(SparqlAlgebraSurfaces.QueryLiteralsKeepTheirTypes), Scope = ExceptionScope.Boundary)]
public sealed record Slice(QueryPattern Inner, long Offset, long? Limit) : QueryPattern;
