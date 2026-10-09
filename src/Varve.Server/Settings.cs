// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Server;

/// <summary>
/// The <c>Varve</c> configuration section (ADR 0101), bound by the
/// configuration-binding source generator and validated by
/// <see cref="SettingsCheck"/> before anything starts. Values that name a
/// choice are strings, so that a wrong one is a listed error rather than a
/// binder exception.
/// </summary>
internal sealed class ServerSettings
{
    /// <summary>The directory each file dataset's directory is under.</summary>
    public string? DatasetsRoot { get; set; }

    /// <summary>The datasets, by name.</summary>
    public Dictionary<string, DatasetSettings> Datasets { get; set; } = new(StringComparer.Ordinal);

    public AuthSettings Auth { get; set; } = new();

    public LimitSettings Limits { get; set; } = new();

    public ForwardedSettings ForwardedHeaders { get; set; } = new();

    /// <summary><c>SERVICE</c> over HTTP (ADR 0104): where a query may federate to.</summary>
    public FederationSettings Federation { get; set; } = new();

    /// <summary><c>LOAD</c> over HTTP (ADR 0104): where an update may fetch a document from.</summary>
    public LoadSettings Load { get; set; } = new();

    /// <summary>Health and readiness (ADR 0113).</summary>
    public HealthSettings Health { get; set; } = new();

    /// <summary>The lease at start (ADR 0116).</summary>
    public LeaseSettings Lease { get; set; } = new();

    /// <summary>What the request span records (ADR 0112); the exporter is configured by <c>OTEL_*</c>.</summary>
    public TelemetrySettings Telemetry { get; set; } = new();
}

/// <summary>Telemetry (ADR 0112): what the request span records beyond its defaults. The exporter itself is the standard <c>OTEL_*</c> environment's.</summary>
internal sealed class TelemetrySettings
{
    /// <summary>Record <c>db.query.text</c>, the query or update as sent, on the request span. Off by default: a query can carry data.</summary>
    public bool QueryText { get; set; }
}

/// <summary>The lease at start (ADR 0116): how long a refused lease is waited for before the dataset is failed.</summary>
internal sealed class LeaseSettings
{
    /// <summary>How long to retry a lease another process holds, logging the holder each second; the dataset is then failed with the holder as its reason.</summary>
    public TimeSpan WaitFor { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Health and readiness (ADR 0113): the lag readiness tolerates, the probes' rate limit, and the drain's head start.</summary>
internal sealed class HealthSettings
{
    /// <summary>How many positions a default projection may be behind its head and the server still ready; 0 is at head.</summary>
    public long ReadyLag { get; set; }

    /// <summary>Probes per minute per client address before <c>429</c>.</summary>
    public int RateLimit { get; set; } = 60;

    /// <summary>How long readiness answers 503 before the listener closes on a stop, so a load balancer notices.</summary>
    public TimeSpan StopDelay { get; set; }
}

/// <summary>
/// The endpoint policy and limits of <c>SERVICE</c> (ADRs 0103, 0104). With
/// no allowed endpoint, every <c>SERVICE</c> is refused, as the evaluator's
/// default refuses it.
/// </summary>
internal sealed class FederationSettings
{
    /// <summary>The IRI prefixes an endpoint may start with; empty allows none.</summary>
    public List<string> AllowedEndpoints { get; set; } = [];

    /// <summary>Whether loopback, link-local and private addresses may be reached.</summary>
    public bool AllowPrivateAddresses { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public long MaxResponseBytes { get; set; } = 100L << 20;
}

/// <summary>The endpoint policy and limits of <c>LOAD</c> (ADRs 0103, 0104). With no allowed source, every <c>LOAD</c> is refused.</summary>
internal sealed class LoadSettings
{
    /// <summary>The IRI prefixes a document's address may start with; empty allows none.</summary>
    public List<string> AllowedSources { get; set; } = [];

    /// <summary>Whether loopback, link-local and private addresses may be reached.</summary>
    public bool AllowPrivateAddresses { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

    public long MaxResponseBytes { get; set; } = 1L << 30;
}

/// <summary>One dataset.</summary>
internal sealed class DatasetSettings
{
    /// <summary><c>File</c> (under <see cref="ServerSettings.DatasetsRoot"/>) or <c>Memory</c>.</summary>
    public string? Storage { get; set; }
}

/// <summary>Authentication and authorisation (ADR 0037).</summary>
internal sealed class AuthSettings
{
    /// <summary><c>Oidc</c> or <c>Anonymous</c>. No default: it is stated or the server does not start.</summary>
    public string? Mode { get; set; }

    /// <summary>The OIDC issuer; its discovery document is <c>/.well-known/openid-configuration</c> under it.</summary>
    public string? Authority { get; set; }

    /// <summary>The audiences a token may be for.</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>The claim that names the caller: <c>oid</c> for Entra, <c>sub</c> otherwise (ADR 0037).</summary>
    public string SubjectClaim { get; set; } = "sub";

    /// <summary>The claim whose values are mapped to permissions.</summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>Refuses an authority whose metadata is not served over HTTPS. Off only for a loopback test issuer.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>A production deployment: anonymous mode is refused.</summary>
    public bool Production { get; set; }

    /// <summary>Per dataset, the claim values that grant each permission.</summary>
    public Dictionary<string, PermissionSettings> Datasets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The server-wide grant (ADR 0106): who may create, open, close and delete datasets, and administers every one.</summary>
    public ServerPermissionSettings Server { get; set; } = new();
}

/// <summary>The claim values of the server admin (ADR 0106), cumulative above every dataset's <c>admin</c>.</summary>
internal sealed class ServerPermissionSettings
{
    public List<string> Admin { get; set; } = [];
}

/// <summary>
/// The claim values that grant a dataset's permissions. Permissions are
/// cumulative: a value that grants write grants read, and one that grants
/// admin grants both.
/// </summary>
internal sealed class PermissionSettings
{
    /// <summary>Claim values that read every graph.</summary>
    public List<string> Read { get; set; } = [];

    /// <summary>Claim values that write, and so read, every graph.</summary>
    public List<string> Write { get; set; } = [];

    /// <summary>Claim values that administer the dataset: every permission, every graph.</summary>
    public List<string> Admin { get; set; } = [];

    /// <summary>Grants scoped to graphs (ADR 0107); the lists above are the <c>all</c> case.</summary>
    public List<GrantSettings> Grants { get; set; } = [];
}

/// <summary>One scoped grant (ADR 0107): a claim value, <c>read</c> or <c>write</c>, and the graphs it reaches.</summary>
internal sealed class GrantSettings
{
    /// <summary>The claim value the grant is for.</summary>
    public string? Claim { get; set; }

    /// <summary><c>read</c> or <c>write</c>; write grants read.</summary>
    public string? Permission { get; set; }

    /// <summary>Graph IRIs, and <c>default</c> for the default graph.</summary>
    public List<string> Graphs { get; set; } = [];

    /// <summary>IRI prefixes: every named graph whose IRI starts with one.</summary>
    public List<string> GraphPrefixes { get; set; } = [];
}

/// <summary>The limits (ADR 0095); an absent one takes its documented default.</summary>
internal sealed class LimitSettings
{
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public long ResultSizeCap { get; set; } = 1L << 30;

    public long MaxRequestBody { get; set; } = 100L << 20;

    public TimeSpan PinnedReadLifetime { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan FeedHeartbeat { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Reads in flight at once, server-wide (ADR 0114).</summary>
    public int MaxConcurrentReads { get; set; } = 64;

    /// <summary>Reads waiting for a slot before the next is <c>503 server-busy</c> (ADR 0114).</summary>
    public int ReadQueueLength { get; set; } = 256;

    /// <summary>The most an evaluation's materialising operators may hold, in bytes, counted (ADR 0114).</summary>
    public long MaxQueryMemory { get; set; } = 256L << 20;

    /// <summary>The most commits an as-of position may lie above its nearest checkpoint (ADR 0114).</summary>
    public long MaxAsOfDistance { get; set; } = 10_000;

    /// <summary>The most live tails one client may hold open (ADR 0114).</summary>
    public int MaxLiveTailsPerClient { get; set; } = 16;

    /// <summary>The most commits a bounded commits range serves before <c>Link rel="next"</c> (ADR 0114).</summary>
    public int CommitsPageSize { get; set; } = 1_000;
}

/// <summary>Forwarded headers, honoured only when enabled and only from the listed proxies (ADR 0101).</summary>
internal sealed class ForwardedSettings
{
    public bool Enabled { get; set; }

    public List<string> KnownProxies { get; set; } = [];
}
