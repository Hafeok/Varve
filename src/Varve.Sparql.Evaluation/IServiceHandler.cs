// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql.Algebra;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// Evaluates a <c>SERVICE</c> pattern somewhere else (ADR 0055). The evaluator
/// hands it the pattern and the solutions it will join the answer with, and
/// joins what comes back. The HTTP implementation is the server's, at layer 5;
/// the default refuses.
/// </summary>
public interface IServiceHandler
{
    /// <summary>
    /// Evaluates the request's pattern at its endpoint. A failure — returned, or
    /// thrown — fails the query unless the pattern is <c>SILENT</c>, in which
    /// case it is one empty solution (Federated Query §2.3).
    /// </summary>
    ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken);
}

/// <summary>What the evaluator asks a <see cref="IServiceHandler"/>.</summary>
public sealed class ServiceRequest
{
    /// <summary>A request.</summary>
    public ServiceRequest(RdfTerm endpoint, Service pattern, IReadOnlyList<Variable> variables, IReadOnlyList<IReadOnlyList<RdfTerm?>> incoming)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(incoming);
        Endpoint = endpoint;
        Pattern = pattern;
        Variables = variables;
        Incoming = incoming;
    }

    /// <summary>The endpoint's IRI.</summary>
    public RdfTerm Endpoint { get; }

    /// <summary>The <c>SERVICE</c> node as written: its pattern and its <c>SILENT</c> flag.</summary>
    public Service Pattern { get; }

    /// <summary>The variables of the pattern, the columns of <see cref="Incoming"/>.</summary>
    public IReadOnlyList<Variable> Variables { get; }

    /// <summary>
    /// The solutions the answer will be joined with, as terms over
    /// <see cref="Variables"/>, null where unbound. A handler may use them to
    /// narrow what it asks (Federated Query §2.4) or ignore them: the evaluator
    /// joins either way.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RdfTerm?>> Incoming { get; }
}

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

/// <summary>
/// The default <see cref="IServiceHandler"/>: every invocation fails, naming the
/// endpoint. Nothing in the default configuration opens a connection (ADR 0055).
/// </summary>
public sealed class RefusingServiceHandler : IServiceHandler
{
    private RefusingServiceHandler()
    {
    }

    /// <summary>The one instance; it has no state.</summary>
    public static RefusingServiceHandler Instance { get; } = new();

    /// <inheritdoc />
    public ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ServiceResult.Failed(
            "No service handler is configured, so SERVICE is refused. Set EvaluationOptions.ServiceHandler to federate.");
    }
}
