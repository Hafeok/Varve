// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using Varve.Rdf;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// A function called by IRI in a query (SPARQL 1.1 §17.6), passed to the
/// evaluator in <see cref="EvaluationOptions.Functions"/> — never registered in
/// a process-wide table (ADR 0056).
/// </summary>
public interface IExtensionFunction
{
    /// <summary>
    /// Computes the function over its evaluated arguments. False is an
    /// expression error (§17.2): a <c>FILTER</c> over it is false, a
    /// <c>BIND</c> of it leaves its variable unbound.
    /// </summary>
    bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result);
}

/// <summary>
/// A custom aggregate (ADR 0053), passed in <see cref="EvaluationOptions.Aggregates"/>
/// by IRI: it makes one accumulator per group.
/// </summary>
public interface IExtensionAggregate
{
    /// <summary>A fresh accumulator for one group. <paramref name="distinct"/> says whether <c>DISTINCT</c> was written; the evaluator has already removed duplicates when it was.</summary>
    IAggregateAccumulator CreateAccumulator(bool distinct);
}

/// <summary>One group's state for a custom aggregate.</summary>
public interface IAggregateAccumulator
{
    /// <summary>
    /// Takes one value of the aggregate's argument. A value whose evaluation
    /// was an error is not passed; an accumulator that wants to fail the group
    /// answers false from <see cref="TryGetResult"/>.
    /// </summary>
    void Add(RdfTerm value);

    /// <summary>The aggregate's value for the group; false is an error, which leaves its binding unbound.</summary>
    bool TryGetResult([NotNullWhen(true)] out RdfTerm? result);
}
