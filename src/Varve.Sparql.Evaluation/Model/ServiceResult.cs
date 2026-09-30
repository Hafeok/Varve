// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Evaluation.Model;

/// <summary>What a <see cref="IServiceHandler"/> answers: solutions, or a failure.</summary>
public sealed class ServiceResult
{
    private ServiceResult(IReadOnlyList<Variable> variables, IEnumerable<IReadOnlyList<RdfTerm?>> solutions, string? failure)
    {
        Variables = variables;
        Solutions = solutions;
        Failure = failure;
    }

    /// <summary>The columns of <see cref="Solutions"/>.</summary>
    public IReadOnlyList<Variable> Variables { get; }

    /// <summary>The solutions, each a row of terms over <see cref="Variables"/>, null where unbound.</summary>
    public IEnumerable<IReadOnlyList<RdfTerm?>> Solutions { get; }

    /// <summary>Why the invocation failed, or null when it did not.</summary>
    [DesignDecision(typeof(ModelNamespacesForLayers3To5.FailureTextIsDisplayText), Scope = ExceptionScope.Boundary)]
    public string? Failure { get; }

    /// <summary>Whether the invocation failed.</summary>
    public bool IsFailure => Failure is not null;

    /// <summary>A successful answer.</summary>
    public static ServiceResult FromSolutions(IReadOnlyList<Variable> variables, IEnumerable<IReadOnlyList<RdfTerm?>> solutions)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(solutions);
        return new ServiceResult(variables, solutions, null);
    }

    /// <summary>A failed invocation, and why.</summary>
    public static ServiceResult Failed(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new ServiceResult([], [], reason);
    }
}
