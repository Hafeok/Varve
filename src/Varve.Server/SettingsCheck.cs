// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>
/// Validates the settings before anything starts (ADR 0101): every error is
/// listed, and a server with any refuses to start rather than fall back to a
/// default for a value that was given and is wrong.
/// </summary>
internal static class SettingsCheck
{
    /// <summary>
    /// Every key under <c>Varve:</c> the server knows, as patterns where
    /// <c>*</c> is one segment (ADR 0115): a key the configuration holds
    /// that matches none is a startup error, so a misspelt setting is an
    /// error and not a silently applied default. A test holds this list
    /// complete against the settings classes.
    /// </summary>
    internal static ImmutableArray<string> KnownKeys { get; } =
    [
        "DatasetsRoot",
        "Datasets:*:Storage",
        "ObjectStorage:Provider", "ObjectStorage:ContainerUrl", "ObjectStorage:Endpoint", "ObjectStorage:Bucket", "ObjectStorage:Region",
        "ObjectStorage:CachePath", "ObjectStorage:CacheBytes", "ObjectStorage:Instance", "ObjectStorage:ReaderId",
        "Auth:Mode", "Auth:Authority", "Auth:Audiences:*", "Auth:SubjectClaim", "Auth:ApplicationClaim", "Auth:RoleClaimType", "Auth:RequireHttpsMetadata", "Auth:Production",
        "Auth:Datasets:*:Read:*", "Auth:Datasets:*:Write:*", "Auth:Datasets:*:Admin:*",
        "Auth:Datasets:*:Grants:*:Claim", "Auth:Datasets:*:Grants:*:Permission", "Auth:Datasets:*:Grants:*:Graphs:*", "Auth:Datasets:*:Grants:*:GraphPrefixes:*",
        "Auth:Server:Admin:*",
        "Limits:QueryTimeout", "Limits:ResultSizeCap", "Limits:MaxRequestBody", "Limits:PinnedReadLifetime", "Limits:FeedHeartbeat",
        "Limits:MaxConcurrentReads", "Limits:ReadQueueLength", "Limits:MaxQueryMemory", "Limits:MaxAsOfDistance", "Limits:MaxLiveTailsPerClient", "Limits:CommitsPageSize",
        "ForwardedHeaders:Enabled", "ForwardedHeaders:KnownProxies:*",
        "Federation:AllowedEndpoints:*", "Federation:AllowPrivateAddresses", "Federation:Timeout", "Federation:MaxResponseBytes",
        "Load:AllowedSources:*", "Load:AllowPrivateAddresses", "Load:Timeout", "Load:MaxResponseBytes",
        "Health:ReadyLag", "Health:RateLimit", "Health:StopDelay",
        "Lease:WaitFor",
        "Telemetry:QueryText",
        "Cors:AllowedOrigins:*", "Cors:MaxAge",
        "Store:GroupCommit:MaxRecords", "Store:GroupCommit:MaxBytes",
        "Store:ExtentMerges:Enabled", "Store:ExtentMerges:FanOut", "Store:ExtentMerges:MaxSetBytes",
        "Events:Source", "Events:EventHubs:Host", "Events:EventHubs:Hub",
    ];

    /// <summary>The leaf keys under <paramref name="section"/> that match no known pattern, as <c>Varve:…</c> paths.</summary>
    internal static List<string> UnknownKeys(IConfigurationSection section)
    {
        List<string> unknown = [];
        Walk(section, string.Empty, unknown);
        return unknown;
    }

    private static void Walk(IConfigurationSection section, string prefix, List<string> unknown)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            string path = prefix.Length == 0 ? child.Key : prefix + ":" + child.Key;
            bool leaf = true;

            foreach (IConfigurationSection _ in child.GetChildren())
            {
                leaf = false;
                break;
            }

            if (leaf)
            {
                if (!IsKnown(path))
                {
                    unknown.Add("Varve:" + path);
                }
            }
            else
            {
                Walk(child, path, unknown);
            }
        }
    }

    internal static bool IsKnown(string path) => Canonical(path) is not null;

    // Names a credential goes by in other products' configuration.
    private static ImmutableArray<string> CredentialNames { get; } =
        ["AccountKey", "AccessKey", "ConnectionString", "SasToken", "Sas", "SharedAccessSignature", "AccessKeyId", "SecretAccessKey", "SecretKey", "ClientSecret", "Password"];

    /// <summary>
    /// The error for a key the server does not know. A storage credential under
    /// <c>ObjectStorage</c> or a dataset —
    /// an account key, a SAS token, a connection string, a static access key —
    /// is refused by name, with the rule, rather than as a misspelling: the
    /// store is reached as the workload's identity (ADR 0137).
    /// </summary>
    internal static string UnknownKeyError(string unknown)
    {
        string leaf = unknown[(unknown.LastIndexOf(':') + 1)..];
        bool storage = unknown.StartsWith("Varve:ObjectStorage:", StringComparison.OrdinalIgnoreCase) || unknown.StartsWith("Varve:Datasets:", StringComparison.OrdinalIgnoreCase);

        foreach (string name in storage ? CredentialNames : [])
        {
            if (string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase))
            {
                return unknown + " is a storage credential, and the server takes none: an object store is reached as the workload's identity — managed identity, workload identity or web identity (ADR 0137).";
            }
        }

        return unknown + " is not a setting the server knows; see docs/operator/configure.md for every key.";
    }

    /// <summary>
    /// The path in the casing the settings classes spell it, with the names
    /// the operator chose kept where a pattern has <c>*</c>; null when no
    /// pattern matches. Configuration keys are case-insensitive, so an
    /// environment variable's upper case prints as the setting it set.
    /// </summary>
    internal static string? Canonical(string path)
    {
        string[] segments = path.Split(':');

        foreach (string pattern in KnownKeys)
        {
            string[] expected = pattern.Split(':');

            if (expected.Length != segments.Length)
            {
                continue;
            }

            bool matches = true;

            for (int i = 0; i < segments.Length && matches; i++)
            {
                matches = expected[i] == "*" || string.Equals(expected[i], segments[i], StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    if (expected[i] != "*")
                    {
                        segments[i] = expected[i];
                    }
                }

                return string.Join(':', segments);
            }
        }

        return null;
    }

    internal static List<string> Errors(ServerSettings settings)
    {
        List<string> errors = [];
        AuthSettings auth = settings.Auth;

        switch (auth.Mode)
        {
            case "Oidc":
                if (!Uri.TryCreate(auth.Authority, UriKind.Absolute, out Uri? authority)
                    || (authority.Scheme != Uri.UriSchemeHttps && (auth.RequireHttpsMetadata || authority.Scheme != Uri.UriSchemeHttp)))
                {
                    errors.Add("Varve:Auth:Authority is the issuer's absolute https address (http only with RequireHttpsMetadata false).");
                }

                if (auth.Audiences.Count == 0 || auth.Audiences.Exists(string.IsNullOrWhiteSpace))
                {
                    errors.Add("Varve:Auth:Audiences names at least one audience, none empty.");
                }

                if (string.IsNullOrWhiteSpace(auth.SubjectClaim) || string.IsNullOrWhiteSpace(auth.RoleClaimType))
                {
                    errors.Add("Varve:Auth:SubjectClaim and Varve:Auth:RoleClaimType are claim types.");
                }

                break;

            case "Anonymous":
                if (auth.Production)
                {
                    errors.Add("Varve:Auth:Mode is Anonymous in a configuration marked Production; anonymous mode is for development (ADR 0037).");
                }

                break;

            default:
                errors.Add("Varve:Auth:Mode is Oidc or Anonymous, and has no default (ADR 0037).");
                break;
        }

        if (settings.Health.ReadyLag < 0)
        {
            errors.Add("Varve:Health:ReadyLag is a number of positions, zero or more.");
        }

        if (settings.Health.RateLimit < 1)
        {
            errors.Add("Varve:Health:RateLimit is at least one probe a minute.");
        }

        if (settings.Health.StopDelay < TimeSpan.Zero || settings.Health.StopDelay > TimeSpan.FromMinutes(5))
        {
            errors.Add("Varve:Health:StopDelay is between zero and five minutes.");
        }

        if (settings.Lease.WaitFor < TimeSpan.Zero || settings.Lease.WaitFor > TimeSpan.FromMinutes(10))
        {
            errors.Add("Varve:Lease:WaitFor is between zero and ten minutes (ADR 0116).");
        }

        if (settings.Store.GroupCommit.MaxRecords < 1 || settings.Store.GroupCommit.MaxBytes < 1 || settings.Store.GroupCommit.MaxBytes > 4L << 20)
        {
            errors.Add("Varve:Store:GroupCommit: MaxRecords is at least one, and MaxBytes between one byte and the write window, 4 MiB (ADR 0124).");
        }

        if (settings.Store.ExtentMerges.FanOut is < 2 or > 16 || settings.Store.ExtentMerges.MaxSetBytes < 1L << 20)
        {
            errors.Add("Varve:Store:ExtentMerges: FanOut is 2 to 16, and MaxSetBytes 1 MiB or more (ADR 0159).");
        }

        if (settings.ObjectStorage.ReaderId is { } reader && !Varve.Store.Log.ReaderId.IsValid(reader))
        {
            errors.Add("Varve:ObjectStorage:ReaderId is 1 to 128 ASCII letters, digits, '-', '_' or '.', not starting with '.' (ADR 0160).");
        }

        // A server with no configured dataset serves what the admin API
        // creates under its root (ADR 0106), so a root is enough.
        if (settings.Datasets.Count == 0 && string.IsNullOrWhiteSpace(settings.DatasetsRoot) && string.IsNullOrWhiteSpace(settings.ObjectStorage.Provider))
        {
            errors.Add("Varve:Datasets names at least one dataset, or Varve:DatasetsRoot names the directory datasets are created under, or Varve:ObjectStorage the store they are created in.");
        }

        if (!string.IsNullOrWhiteSpace(settings.DatasetsRoot) && !Path.IsPathFullyQualified(settings.DatasetsRoot))
        {
            errors.Add("Varve:DatasetsRoot is an absolute path.");
        }

        foreach ((string name, DatasetSettings dataset) in settings.Datasets)
        {
            if (!DatasetName.TryParse(name, out _))
            {
                errors.Add("Varve:Datasets:" + name + " is not a dataset name: 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit (ADR 0093).");
            }

            switch (dataset.Storage)
            {
                case "File":
                    if (string.IsNullOrWhiteSpace(settings.DatasetsRoot) || !Path.IsPathFullyQualified(settings.DatasetsRoot))
                    {
                        errors.Add("Varve:Datasets:" + name + " is a File dataset, and Varve:DatasetsRoot is not an absolute path.");
                    }

                    break;
                case "Memory":
                    break;
                case "ObjectStorage":
                    if (string.IsNullOrWhiteSpace(settings.ObjectStorage.Provider))
                    {
                        errors.Add("Varve:Datasets:" + name + " is an ObjectStorage dataset, and Varve:ObjectStorage:Provider is not set.");
                    }

                    break;
                default:
                    errors.Add("Varve:Datasets:" + name + ":Storage is File, Memory or ObjectStorage.");
                    break;
            }
        }

        CheckObjectStorage(settings.ObjectStorage, errors);

        foreach ((string name, PermissionSettings permissions) in auth.Datasets)
        {
            if (!settings.Datasets.ContainsKey(name))
            {
                errors.Add("Varve:Auth:Datasets:" + name + " grants permissions on a dataset that is not configured.");
            }

            for (int i = 0; i < permissions.Grants.Count; i++)
            {
                string at = "Varve:Auth:Datasets:" + name + ":Grants:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                GrantSettings grant = permissions.Grants[i];

                if (string.IsNullOrWhiteSpace(grant.Claim))
                {
                    errors.Add(at + ":Claim names the claim value the grant is for.");
                }

                if (grant.Permission is not ("read" or "write"))
                {
                    errors.Add(at + ":Permission is read or write; admin is dataset-wide (ADR 0107).");
                }

                if (grant.Graphs.Count == 0 && grant.GraphPrefixes.Count == 0)
                {
                    errors.Add(at + " names at least one graph or graph prefix.");
                }

                foreach (string graph in grant.Graphs)
                {
                    if (graph != "default" && !Uri.TryCreate(graph, UriKind.Absolute, out _))
                    {
                        errors.Add(at + ":Graphs holds an absolute IRI or `default`, not " + graph + ".");
                    }
                }

                foreach (string prefix in grant.GraphPrefixes)
                {
                    if (string.IsNullOrEmpty(prefix))
                    {
                        errors.Add(at + ":GraphPrefixes holds no empty prefix.");
                    }
                }
            }
        }

        LimitSettings limits = settings.Limits;

        if (limits.MaxConcurrentReads < 1 || limits.ReadQueueLength < 0 || limits.MaxQueryMemory < 1 || limits.MaxAsOfDistance < 0 || limits.MaxLiveTailsPerClient < 1 || limits.CommitsPageSize < 1)
        {
            errors.Add("Varve:Limits: MaxConcurrentReads, MaxQueryMemory, MaxLiveTailsPerClient and CommitsPageSize are positive, and ReadQueueLength and MaxAsOfDistance are zero or more (ADR 0114).");
        }

        if (limits.QueryTimeout <= TimeSpan.Zero || limits.PinnedReadLifetime <= TimeSpan.Zero || limits.FeedHeartbeat <= TimeSpan.Zero)
        {
            errors.Add("Varve:Limits durations are positive.");
        }

        if (limits.ResultSizeCap <= 0 || limits.MaxRequestBody <= 0)
        {
            errors.Add("Varve:Limits sizes are positive.");
        }

        CheckOutbound(errors, "Varve:Federation:AllowedEndpoints", settings.Federation.AllowedEndpoints, settings.Federation.Timeout, settings.Federation.MaxResponseBytes);
        CheckOutbound(errors, "Varve:Load:AllowedSources", settings.Load.AllowedSources, settings.Load.Timeout, settings.Load.MaxResponseBytes);

        foreach (string proxy in settings.ForwardedHeaders.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                errors.Add("Varve:ForwardedHeaders:KnownProxies has " + proxy + ", which is not an IP address.");
            }
        }

        if (settings.ForwardedHeaders.Enabled && settings.ForwardedHeaders.KnownProxies.Count == 0)
        {
            errors.Add("Varve:ForwardedHeaders is enabled with no known proxy; forwarded headers are honoured only from configured proxies.");
        }

        foreach (string origin in settings.Cors.AllowedOrigins)
        {
            if (!IsOrigin(origin))
            {
                errors.Add("Varve:Cors:AllowedOrigins holds an origin, scheme://host[:port] with no path, or *, not '" + origin + "' (ADR 0132).");
            }
        }

        if (settings.Cors.MaxAge < TimeSpan.Zero)
        {
            errors.Add("Varve:Cors:MaxAge is zero or more.");
        }

        if (settings.Events.Source is { } source
            && (!(source.StartsWith("https://", StringComparison.Ordinal) || source.StartsWith("http://", StringComparison.Ordinal))
                || !source.EndsWith('/')
                || source.IndexOf('/', source.IndexOf("://", StringComparison.Ordinal) + 3) <= source.IndexOf("://", StringComparison.Ordinal) + 3))
        {
            errors.Add("Varve:Events:Source is an absolute http or https URL ending in '/', not '" + source + "' (ADR 0143).");
        }

        if (string.IsNullOrEmpty(settings.Events.EventHubs.Host) != string.IsNullOrEmpty(settings.Events.EventHubs.Hub))
        {
            errors.Add("Varve:Events:EventHubs names a Host and a Hub, both or neither (ADR 0144).");
        }

        return errors;
    }

    // An origin as the Fetch standard serialises one (ADR 0132): http or https,
    // a host, an optional port, and nothing after it, not even a slash; or *.
    private static bool IsOrigin(string origin)
    {
        if (origin == "*")
        {
            return true;
        }

        int authority = origin.StartsWith("https://", StringComparison.Ordinal) ? 8 : origin.StartsWith("http://", StringComparison.Ordinal) ? 7 : -1;

        if (authority < 0 || authority == origin.Length)
        {
            return false;
        }

        foreach (char c in origin.AsSpan(authority))
        {
            if (c is '/' or '?' or '#' or '@' or '*' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    // An endpoint policy's prefixes are absolute http or https IRIs, and its
    // limits positive (ADRs 0103, 0104): checked here so that a wrong prefix is
    // a listed error at start, not an exception at the first SERVICE.
    // The object store (ADRs 0134, 0137): the provider's address, with no
    // credential in it, and a local cache directory.
    private static void CheckObjectStorage(ObjectStorageSettings store, List<string> errors)
    {
        switch (store.Provider)
        {
            case null or "":
                return;
            case "Azure":
                if (!Uri.TryCreate(store.ContainerUrl, UriKind.Absolute, out Uri? container) || !SecureOrLoopback(container) || container.AbsolutePath.Trim('/').Length == 0)
                {
                    errors.Add("Varve:ObjectStorage:ContainerUrl is the container's absolute https address, https://{account}.blob.core.windows.net/{container} (http only on loopback, for Azurite).");
                }
                else if (container.Query.Length > 0 || container.UserInfo.Length > 0)
                {
                    errors.Add("Varve:ObjectStorage:ContainerUrl carries a query or user information: a SAS token or a key is refused; the server reaches the container as the workload's identity (ADR 0137).");
                }

                break;
            case "S3Compatible":
                if (!Uri.TryCreate(store.Endpoint, UriKind.Absolute, out Uri? endpoint) || !SecureOrLoopback(endpoint))
                {
                    errors.Add("Varve:ObjectStorage:Endpoint is the service's absolute https address (http only on loopback).");
                }
                else if (endpoint.Query.Length > 0 || endpoint.UserInfo.Length > 0)
                {
                    errors.Add("Varve:ObjectStorage:Endpoint carries a query or user information: a static key is refused; the server reaches the store by web identity (ADR 0137).");
                }

                if (string.IsNullOrWhiteSpace(store.Bucket))
                {
                    errors.Add("Varve:ObjectStorage:Bucket names the bucket.");
                }

                if (string.IsNullOrWhiteSpace(store.Region))
                {
                    errors.Add("Varve:ObjectStorage:Region names the region the signature is for.");
                }

                break;
            default:
                errors.Add("Varve:ObjectStorage:Provider is Azure or S3Compatible (ADR 0134).");
                return;
        }

        if (string.IsNullOrWhiteSpace(store.CachePath) || !Path.IsPathFullyQualified(store.CachePath))
        {
            errors.Add("Varve:ObjectStorage:CachePath is an absolute path: the local cache, a directory per dataset under it (ADR 0139).");
        }

        if (store.CacheBytes < 64L << 20)
        {
            errors.Add("Varve:ObjectStorage:CacheBytes is at least 64 MiB (ADR 0139).");
        }

        static bool SecureOrLoopback(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static void CheckOutbound(List<string> errors, string section, List<string> prefixes, TimeSpan timeout, long maxResponseBytes)
    {
        foreach (string prefix in prefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix)
                || !(prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(section + " holds an absolute http or https IRI prefix, not '" + prefix + "'.");
            }
        }

        if (timeout <= TimeSpan.Zero || maxResponseBytes <= 0)
        {
            errors.Add(section[..section.LastIndexOf(':')] + " has a positive Timeout and MaxResponseBytes.");
        }
    }
}
