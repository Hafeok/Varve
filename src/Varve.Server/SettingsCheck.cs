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
        "Auth:Mode", "Auth:Authority", "Auth:Audiences:*", "Auth:SubjectClaim", "Auth:RoleClaimType", "Auth:RequireHttpsMetadata", "Auth:Production",
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

        // A server with no configured dataset serves what the admin API
        // creates under its root (ADR 0106), so a root is enough.
        if (settings.Datasets.Count == 0 && string.IsNullOrWhiteSpace(settings.DatasetsRoot))
        {
            errors.Add("Varve:Datasets names at least one dataset, or Varve:DatasetsRoot names the directory datasets are created under.");
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
                default:
                    errors.Add("Varve:Datasets:" + name + ":Storage is File or Memory.");
                    break;
            }
        }

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

        return errors;
    }

    // An endpoint policy's prefixes are absolute http or https IRIs, and its
    // limits positive (ADRs 0103, 0104): checked here so that a wrong prefix is
    // a listed error at start, not an exception at the first SERVICE.
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
