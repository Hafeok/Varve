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

    /// <summary>
    /// The object store each <c>ObjectStorage</c> dataset is in, under the
    /// prefix <c>{name}/</c> (ADRs 0134, 0139).
    /// </summary>
    public ObjectStorageSettings ObjectStorage { get; set; } = new();

    /// <summary>The fleet this server is one of (ADRs 0163, 0164, 0168).</summary>
    public FleetSettings Fleet { get; set; } = new();

    /// <summary>The management plane (ADR 0163): its bootstrap operators and its optional listener.</summary>
    public ManageSettings Manage { get; set; } = new();

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

    /// <summary>Cross-origin requests from a browser (ADR 0132); none allowed by default.</summary>
    public CorsSettings Cors { get; set; } = new();

    /// <summary>How every dataset's store writes (ADR 0124).</summary>
    public StoreSettings Store { get; set; } = new();

    /// <summary>Domain events (ADRs 0143, 0144): the base their URLs are built on, and the Event Hubs server the AsyncAPI document names.</summary>
    public EventsSettings Events { get; set; } = new();
}

/// <summary>
/// Domain events (ADRs 0143, 0144). <see cref="Source"/> is the base of every
/// event's <c>source</c> and every schema URL, an absolute http or https URL
/// ending in <c>/</c>; unset, the request's own base is used, and byte
/// identity then holds per base. <see cref="EventHubs"/> names the Event Hubs
/// server the AsyncAPI document lists, when events are published there.
/// </summary>
internal sealed class EventsSettings
{
    public string? Source { get; set; }

    public EventHubsSettings EventHubs { get; set; } = new();
}

/// <summary>An Event Hubs namespace's host and an event hub's name, both or neither.</summary>
internal sealed class EventHubsSettings
{
    public string? Host { get; set; }

    public string? Hub { get; set; }
}

/// <summary>
/// CORS (ADR 0132): the origins a browser application may call from, and how
/// long a preflight may be cached. Credentials mode is never allowed: a
/// bearer token travels in <c>Authorization</c>.
/// </summary>
internal sealed class CorsSettings
{
    /// <summary>Origins, scheme, host and optional port, or <c>*</c> for any; empty adds no CORS at all.</summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>The preflight's <c>Access-Control-Max-Age</c>.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>How every dataset's store writes (ADR 0124).</summary>
internal sealed class StoreSettings
{
    public GroupCommitSettings GroupCommit { get; set; } = new();

    public ExtentMergeSettings ExtentMerges { get; set; } = new();

    /// <summary>
    /// The memtable's bound, in entries: a quad or a new term each (ADR 0070).
    /// It bounds a writer's memory before a spill, and a follower's before it
    /// is opened again (ADR 0164 point 5).
    /// </summary>
    public long MemtableLimit { get; set; } = 1_000_000;
}

/// <summary>
/// The writer's background merges of extent sets (ADRs 0158, 0159): on by
/// default, fan-out 4, merged sets capped at 256 MiB.
/// </summary>
internal sealed class ExtentMergeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How many adjacent sets of one size tier merge into one: 2 to 16.</summary>
    public int FanOut { get; set; } = 4;

    /// <summary>The bytes no merge makes a set exceed: 1 MiB or more.</summary>
    public long MaxSetBytes { get; set; } = Varve.Store.ExtentMergePolicy.DefaultMaxSetBytes.Value;
}

/// <summary>
/// Group commit (ADR 0124): the bounds of one write of the log. A batch takes
/// no more requests past <see cref="MaxRecords"/> records, and never more than
/// <see cref="MaxBytes"/> bytes of records once it holds two commits; at most
/// the format's write window, 4 MiB.
/// </summary>
internal sealed class GroupCommitSettings
{
    public int MaxRecords { get; set; } = 1024;

    public long MaxBytes { get; set; } = 4L << 20;
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

/// <summary>
/// The fleet (ADRs 0163, 0164, 0168): who writes the system dataset, the
/// address the writer is forwarded to, how often a follower advances, and the
/// management lag readiness tolerates.
/// </summary>
internal sealed class FleetSettings
{
    /// <summary>
    /// <c>true</c>, <c>false</c>, or an instance name compared with
    /// <see cref="ObjectStorageSettings.Instance"/>: whether this instance
    /// writes the system dataset and claims the datasets placed on it. A fleet
    /// of one, with no object store, is its own writer whatever this says.
    /// </summary>
    public string? Writer { get; set; }

    /// <summary>The address other instances forward writes to, recorded at claim; required on an instance that may claim in an object-store fleet.</summary>
    public string? InternalUrl { get; set; }

    /// <summary>How old a follower's last advance may be before a read advances it, and how often the system dataset is advanced (ADR 0164).</summary>
    public TimeSpan FollowInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How many system positions this instance may be behind the system tail and be ready (ADR 0163 point 13).</summary>
    public long AllowedLag { get; set; } = 100;
}

/// <summary>The management plane (ADR 0163).</summary>
internal sealed class ManageSettings
{
    /// <summary>The bootstrap operators: agent IRIs or role values, always operators, recorded by the first writer.</summary>
    public List<string> BootstrapOperators { get; set; } = [];

    /// <summary>A second listener for <c>/manage/</c> alone, so network policy can isolate it; unset, one listener serves both planes.</summary>
    public string? Urls { get; set; }
}

/// <summary>
/// The object store the server's <c>ObjectStorage</c> datasets are in (ADRs
/// 0134–0139): one container or bucket, a dataset per prefix. Credentials are
/// the workload's identity, never a key in configuration (ADR 0137).
/// </summary>
internal sealed class ObjectStorageSettings
{
    /// <summary><c>Azure</c> or <c>S3Compatible</c>; unset when no dataset is in an object store.</summary>
    public string? Provider { get; set; }

    /// <summary>Azure: the container's URL, <c>https://{account}.blob.core.windows.net/{container}</c>.</summary>
    public string? ContainerUrl { get; set; }

    /// <summary>S3-compatible: the service's endpoint.</summary>
    public string? Endpoint { get; set; }

    /// <summary>S3-compatible: the bucket.</summary>
    public string? Bucket { get; set; }

    /// <summary>S3-compatible: the region the signature names.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// S3-compatible: the STS endpoint the workload's web identity is
    /// exchanged at, a self-hosted server's own; Amazon's when unset (ADR 0137).
    /// </summary>
    public string? StsEndpoint { get; set; }

    /// <summary>The local directory datasets cache in, a directory per dataset under it.</summary>
    public string? CachePath { get; set; }

    /// <summary>The most each dataset's extent page cache holds (ADR 0139).</summary>
    public long CacheBytes { get; set; } = 8L << 30;

    /// <summary>Who this server is in the writer record (ADR 0136); the machine name when unset.</summary>
    public string? Instance { get; set; }

    /// <summary>
    /// This server's reader id, the name of its reader record in each
    /// dataset (ADR 0160). Set it where the cache does not survive a restart —
    /// a container's — so a restart replaces the record its previous run left;
    /// unset, a random id is kept in each dataset's cache directory.
    /// </summary>
    public string? ReaderId { get; set; }
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

    /// <summary>The claim that names the acting application, recorded on the request span as <c>varve.azp</c> and never in the log: <c>azp</c>, or <c>appid</c> for an Entra v1 token (ADR 0127).</summary>
    public string ApplicationClaim { get; set; } = "azp";

    /// <summary>The claim whose values are mapped to permissions.</summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>Refuses an authority whose metadata is not served over HTTPS. Off only for a loopback test issuer.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>A production deployment: anonymous mode is refused.</summary>
    public bool Production { get; set; }

    /// <summary>The management plane's token (ADR 0163): its own audience, which the data plane refuses.</summary>
    public ManagementAuthSettings Management { get; set; } = new();
}

/// <summary>The management plane's token (ADR 0163 point 1).</summary>
internal sealed class ManagementAuthSettings
{
    /// <summary>The audience a management token is for; a token for both planes' audiences is refused by both.</summary>
    public string Audience { get; set; } = "api://varve-manage";
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
