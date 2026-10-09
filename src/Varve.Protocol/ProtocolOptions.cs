// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Varve.Sparql.Evaluation;

namespace Varve.Protocol;

/// <summary>
/// What the host wires into the endpoints (ADRs 0060, 0091). Immutable, and
/// nothing is ambient: the clock every limit is measured by, the evaluation
/// options every query runs with, and the four seams.
/// </summary>
public sealed class ProtocolOptions
{
    /// <summary>Which dataset a request names.</summary>
    public required IDatasetResolver Datasets { get; init; }

    /// <summary>How an update executes.</summary>
    public required ISparqlUpdateExecutor Updates { get; init; }

    /// <summary>Who a commit names as its agent.</summary>
    public required ICallerIdentity Identity { get; init; }

    /// <summary>
    /// What a caller may see and change of a dataset, by graph (ADR 0107),
    /// asked once per request after the policy allowed it. Required: a host
    /// without graph-level grants answers <see cref="Varve.Rdf.CallerScope.Everything"/>.
    /// </summary>
    public required IAccessScopes AccessScopes { get; init; }

    /// <summary>
    /// The host's authorisation, asked for <see cref="DatasetPermissions"/>
    /// on every request before anything is read (ADR 0091). Required: a host
    /// without authentication registers the three policies as allowing everyone.
    /// </summary>
    public required IAuthorizationService Authorization { get; init; }

    /// <summary>The clock the limits are measured by.</summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>
    /// Cancelled when the host begins to stop (ADR 0101): a live feed then ends
    /// with a <c>shutdown</c> event, or an error record, rather than holding
    /// the drain open.
    /// </summary>
    public CancellationToken Stopping { get; init; }

    /// <summary>What bounds a request.</summary>
    public ProtocolLimits Limits { get; init; } = ProtocolLimits.Default;

    /// <summary>What the request span records beyond its defaults (ADR 0112).</summary>
    public TelemetryOptions Telemetry { get; init; } = TelemetryOptions.Default;

    /// <summary>
    /// Where a commit and a refusal are logged, with the request id and the
    /// position (ADR 0112). Silent by default.
    /// </summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;

    /// <summary>The live tails open per client, bounded by <see cref="ProtocolLimits.MaxLiveTailsPerClient"/> (ADR 0114); one registry per host.</summary>
    internal Endpoints.LiveTails Tails { get; } = new();

    /// <summary>
    /// The write side of the dataset map (ADR 0106), for the admin API's
    /// <c>GET /datasets</c>, <c>PUT</c>, <c>DELETE</c>, <c>open</c> and
    /// <c>close</c>. Null in a host that administers no dataset: those
    /// endpoints then answer <c>404</c>, and the per-dataset admin endpoints
    /// still serve.
    /// </summary>
    public IDatasetAdministration? Administration { get; init; }

    /// <summary>
    /// The options every query is evaluated with: the clock and random source
    /// <c>NOW()</c> and <c>RAND()</c> read, and the <c>SERVICE</c> handler.
    /// </summary>
    public EvaluationOptions Evaluation { get; init; } = new();
}
