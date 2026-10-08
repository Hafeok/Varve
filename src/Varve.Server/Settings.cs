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

    /// <summary><c>SERVICE</c> over HTTP (ADR 0103): where a query may federate to.</summary>
    public FederationSettings Federation { get; set; } = new();

    /// <summary><c>LOAD</c> over HTTP (ADR 0103): where an update may fetch a document from.</summary>
    public LoadSettings Load { get; set; } = new();
}

/// <summary>
/// The endpoint policy and limits of <c>SERVICE</c> (ADRs 0102, 0103). With
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

/// <summary>The endpoint policy and limits of <c>LOAD</c> (ADRs 0102, 0103). With no allowed source, every <c>LOAD</c> is refused.</summary>
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
}

/// <summary>
/// The claim values that grant a dataset's permissions. Permissions are
/// cumulative: a value that grants write grants read, and one that grants
/// admin grants both.
/// </summary>
internal sealed class PermissionSettings
{
    public List<string> Read { get; set; } = [];

    public List<string> Write { get; set; } = [];

    public List<string> Admin { get; set; } = [];
}

/// <summary>The limits (ADR 0095); an absent one takes its documented default.</summary>
internal sealed class LimitSettings
{
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public long ResultSizeCap { get; set; } = 1L << 30;

    public long MaxRequestBody { get; set; } = 100L << 20;

    public TimeSpan PinnedReadLifetime { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan FeedHeartbeat { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Forwarded headers, honoured only when enabled and only from the listed proxies (ADR 0101).</summary>
internal sealed class ForwardedSettings
{
    public bool Enabled { get; set; }

    public List<string> KnownProxies { get; set; } = [];
}
