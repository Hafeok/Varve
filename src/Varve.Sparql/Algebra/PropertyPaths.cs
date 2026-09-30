// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Sparql.Algebra;

/// <summary>An algebraic property path expression (SPARQL 1.2 §18.2).</summary>
[Contract(typeof(OptimiserAndEvaluatorOnePackageAlgebraInAlgebraOut.AlgebraNodesAreSealedRecords), Role = "a property path node")]
public abstract record PropertyPath : AlgebraNode
{
    private protected PropertyPath()
    {
    }
}

/// <summary><c>Link(iri)</c>: a single predicate.</summary>
public sealed record PredicatePath(RdfTerm Predicate) : PropertyPath;

/// <summary><c>Inv(path)</c>: <c>^path</c>.</summary>
public sealed record InversePath(PropertyPath Inner) : PropertyPath;

/// <summary><c>Seq(left, right)</c>: <c>left / right</c>.</summary>
public sealed record SequencePath(PropertyPath Left, PropertyPath Right) : PropertyPath;

/// <summary><c>Alt(left, right)</c>: <c>left | right</c>.</summary>
public sealed record AlternativePath(PropertyPath Left, PropertyPath Right) : PropertyPath;

/// <summary><c>ZeroOrMorePath(path)</c>: <c>path*</c>.</summary>
public sealed record ZeroOrMorePath(PropertyPath Inner) : PropertyPath;

/// <summary><c>OneOrMorePath(path)</c>: <c>path+</c>.</summary>
public sealed record OneOrMorePath(PropertyPath Inner) : PropertyPath;

/// <summary><c>ZeroOrOnePath(path)</c>: <c>path?</c>.</summary>
public sealed record ZeroOrOnePath(PropertyPath Inner) : PropertyPath;

/// <summary>
/// A negated property set, <c>!(:p | ^:q)</c>: <c>NPS</c>, <c>Inv(NPS)</c>, or
/// the <c>Alt</c> of both, in one node (<c>docs/spec/sparql-algebra.md</c> §4.4).
/// </summary>
public sealed record NegatedPropertySet(AlgebraList<RdfTerm> Forward, AlgebraList<RdfTerm> Inverse) : PropertyPath;
