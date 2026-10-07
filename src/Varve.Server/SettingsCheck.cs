// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Varve.Protocol.Model;

namespace Varve.Server;

/// <summary>
/// Validates the settings before anything starts (ADR 0101): every error is
/// listed, and a server with any refuses to start rather than fall back to a
/// default for a value that was given and is wrong.
/// </summary>
internal static class SettingsCheck
{
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

        if (settings.Datasets.Count == 0)
        {
            errors.Add("Varve:Datasets names at least one dataset.");
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

        foreach (string name in auth.Datasets.Keys)
        {
            if (!settings.Datasets.ContainsKey(name))
            {
                errors.Add("Varve:Auth:Datasets:" + name + " grants permissions on a dataset that is not configured.");
            }
        }

        LimitSettings limits = settings.Limits;

        if (limits.QueryTimeout <= TimeSpan.Zero || limits.PinnedReadLifetime <= TimeSpan.Zero || limits.FeedHeartbeat <= TimeSpan.Zero)
        {
            errors.Add("Varve:Limits durations are positive.");
        }

        if (limits.ResultSizeCap <= 0 || limits.MaxRequestBody <= 0)
        {
            errors.Add("Varve:Limits sizes are positive.");
        }

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
}
