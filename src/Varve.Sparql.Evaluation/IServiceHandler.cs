// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Sparql.Evaluation.Model;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// Evaluates a <c>SERVICE</c> pattern somewhere else (ADR 0055). The evaluator
/// hands it the pattern and the solutions it will join the answer with, and
/// joins what comes back. The HTTP implementation is the server's, at layer 5;
/// the default refuses.
/// </summary>
[Contract(typeof(ServiceThroughAHandlerTheDefaultRefuses.ServiceHandlerContract), Role = "what executes a SERVICE request")]
public interface IServiceHandler
{
    /// <summary>
    /// Evaluates the request's pattern at its endpoint. A failure — returned, or
    /// thrown — fails the query unless the pattern is <c>SILENT</c>, in which
    /// case it is one empty solution (Federated Query §2.3).
    /// </summary>
    ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken);
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
