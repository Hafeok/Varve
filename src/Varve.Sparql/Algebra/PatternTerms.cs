// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>
/// A term position in a pattern: a variable, a term, a blank node, or a triple
/// term with a variable inside it (<c>docs/spec/sparql-algebra.md</c> §2.4).
/// </summary>
[Contract(typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords), Role = "a term in a pattern: a constant, a variable or a blank node")]
public abstract record PatternTerm : AlgebraNode
{
    private protected PatternTerm()
    {
    }
}

/// <summary>A variable in a pattern.</summary>
public sealed record VariablePattern(Variable Variable) : PatternTerm;

/// <summary>An IRI, a literal, or a ground triple term.</summary>
public sealed record TermPattern(RdfTerm Term) : PatternTerm;

/// <summary>
/// A blank node, by label. Kept in the tree rather than replaced by a variable
/// (<c>docs/spec/sparql-algebra.md</c> §3.1); an evaluator treats it as a
/// variable that is never projected.
/// </summary>
[DesignDecision(typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords), Scope = ExceptionScope.Compatibility)]
public sealed record BlankNodePattern(string Label) : PatternTerm;

/// <summary>A triple term with a variable or blank node somewhere inside it. SPARQL 1.2.</summary>
public sealed record TripleTermPattern(PatternTerm Subject, PatternTerm Predicate, PatternTerm Object) : PatternTerm;

/// <summary>A triple pattern.</summary>
public sealed record TriplePattern(PatternTerm Subject, PatternTerm Predicate, PatternTerm Object) : AlgebraNode;

/// <summary>
/// A quad pattern in an update template: a triple pattern and a graph, where
/// <see langword="null"/> is the default graph and a variable is allowed by
/// <c>GRAPH ?g { }</c>.
/// </summary>
public sealed record QuadPattern(PatternTerm Subject, PatternTerm Predicate, PatternTerm Object, PatternTerm? Graph) : AlgebraNode;
