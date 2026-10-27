// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Varve.Events.Model;
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

    /// <summary>
    /// The base of every event's <c>source</c> and every schema URL (ADR 0143):
    /// an absolute URL ending in <c>/</c>, under which datasets are
    /// <c>datasets/{name}</c>. Null takes the request's scheme, host and path
    /// base, so that byte identity holds per base; a host reached under more
    /// than one name sets it.
    /// </summary>
    [DesignDecision(typeof(DomainEventsAsCloudevents.SourceIsConfiguredOrTheRequestBase), Scope = ExceptionScope.Boundary)]
    public string? EventSource { get; init; }

    /// <summary>The Event Hubs server the AsyncAPI document names, when the host publishes events there (ADR 0144).</summary>
    public EventHubsServer? EventHubs { get; init; }

    /// <summary>
    /// Who writes each dataset of the host's fleet (ADR 0168). Null in a
    /// fleet of one: every write is then served here.
    /// </summary>
    public IWriterDirectory? Writers { get; init; }

    /// <summary>What forwards a write to its writer (ADR 0168); required with <see cref="Writers"/>.</summary>
    public System.Net.Http.HttpMessageInvoker? Forwarder { get; init; }

    /// <summary>
    /// The management plane's position, which every commit records and a
    /// write's order is checked against (ADRs 0163, 0165). Null in a host
    /// with no management plane: commits then carry no position.
    /// </summary>
    public IManagementAuthority? Management { get; init; }

    /// <summary>
    /// The management plane, whose system dataset holds the event declarations
    /// of a dataset that moved them there (ADR 0167). Null in a host with no
    /// management plane: every dataset's events render from its own catalogue.
    /// </summary>
    public IManagementPlane? Plane { get; init; }

    /// <summary>How a follower is advanced before a read (ADR 0164). Null when the host follows nothing.</summary>
    public IDatasetFollowing? Following { get; init; }

    /// <summary>
    /// The host's validation projections, served as reserved report graphs
    /// (ADR 0174); null when the host keeps none.
    /// </summary>
    public IValidationReports? ValidationReports { get; init; }

    /// <summary>
    /// The host's TBox bindings, which a query under a regime other than simple
    /// is rewritten against (ADRs 0192, 0193); none by default, when every
    /// graph's TBox is empty and the default regime is simple.
    /// </summary>
    public IEntailmentBindings? Entailment { get; init; }

    /// <summary><see cref="Evaluation"/> with the functions a rewritten query calls, composed once.</summary>
    internal EvaluationOptions EntailedEvaluation => field ??= Endpoints.EntailmentFunctions.With(Evaluation);

    /// <summary>The catalogue graphs' history of each dataset, replayed once and kept current.</summary>
    internal Endpoints.EventCatalogues Catalogues { get; } = new();
}
