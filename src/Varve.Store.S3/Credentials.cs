// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Varve.Store.S3;

/// <summary>
/// Credentials a request is signed with. They render without their secret
/// (ADR 0137, TokensStayInTheAdapter).
/// </summary>
internal sealed class SigningCredentials(string accessKeyId, string secretAccessKey, string? sessionToken, DateTimeOffset expiresOn)
{
    public string AccessKeyId { get; } = accessKeyId;

    public string SecretAccessKey { get; } = secretAccessKey;

    public string? SessionToken { get; } = sessionToken;

    public DateTimeOffset ExpiresOn { get; } = expiresOn;

    public override string ToString() => "(credentials " + AccessKeyId + ", expiring " + ExpiresOn.ToString("O", CultureInfo.InvariantCulture) + ")";
}

/// <summary>Where signing credentials come from.</summary>
internal interface ICredentialSource
{
    ValueTask<SigningCredentials> GetAsync(CancellationToken cancellationToken);
}

/// <summary>A static key for a loopback server: it never expires (ADR 0137, LoopbackDevelopmentKey).</summary>
internal sealed class StaticKey(string accessKeyId, string secretAccessKey) : ICredentialSource
{
    private readonly SigningCredentials _credentials = new(accessKeyId, secretAccessKey, null, DateTimeOffset.MaxValue);

    public ValueTask<SigningCredentials> GetAsync(CancellationToken cancellationToken) => new(_credentials);
}

/// <summary>
/// <c>AssumeRoleWithWebIdentity</c> with the OIDC token in
/// <c>AWS_WEB_IDENTITY_TOKEN_FILE</c>, read afresh each time, for the role in
/// <c>AWS_ROLE_ARN</c> (ADR 0137, S3WebIdentityOnly). The temporary
/// credentials are kept until five minutes before they expire.
/// </summary>
internal sealed class WebIdentity(HttpClient http, TimeProvider clock, Uri sts, string tokenFile, string roleArn, string sessionName) : ICredentialSource, IDisposable
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _fetching = new(1, 1);
    private SigningCredentials? _held;

    public static WebIdentity? FromEnvironment(Func<string, string?> environment, HttpClient http, TimeProvider clock, Uri sts)
    {
        if (environment("AWS_WEB_IDENTITY_TOKEN_FILE") is not { Length: > 0 } file || environment("AWS_ROLE_ARN") is not { Length: > 0 } role)
        {
            return null;
        }

        string session = environment("AWS_ROLE_SESSION_NAME") is { Length: > 0 } name ? name : "varve";
        return new WebIdentity(http, clock, sts, file, role, session);
    }

    public async ValueTask<SigningCredentials> GetAsync(CancellationToken cancellationToken)
    {
        if (_held is { } held && held.ExpiresOn - Margin > clock.GetUtcNow())
        {
            return held;
        }

        await _fetching.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_held is { } again && again.ExpiresOn - Margin > clock.GetUtcNow())
            {
                return again;
            }

            string token = (await File.ReadAllTextAsync(tokenFile, cancellationToken).ConfigureAwait(false)).Trim();
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["Action"] = "AssumeRoleWithWebIdentity",
                ["Version"] = "2011-06-15",
                ["RoleArn"] = roleArn,
                ["RoleSessionName"] = sessionName,
                ["WebIdentityToken"] = token,
                ["DurationSeconds"] = "3600",
            });
            using HttpResponseMessage response = await http.PostAsync(sts, form, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new IOException("The STS endpoint refused the web identity with " + (int)response.StatusCode + ". Check the role's trust policy for this issuer and subject (ADR 0137).");
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            _held = Read(body);
            return _held;
        }
        finally
        {
            _fetching.Release();
        }
    }

    public void Dispose() => _fetching.Dispose();

    private static SigningCredentials Read(Stream body)
    {
        using XmlReader xml = XmlReader.Create(body, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        string? id = null;
        string? secret = null;
        string? session = null;
        string? expiration = null;

        // Reading an element's content leaves the reader on the node after
        // it, so the next read only happens when nothing was consumed.
        bool advance = true;

        while (!advance || xml.Read())
        {
            advance = true;

            if (xml.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (xml.LocalName)
            {
                case "AccessKeyId":
                    id = xml.ReadElementContentAsString();
                    advance = false;
                    break;
                case "SecretAccessKey":
                    secret = xml.ReadElementContentAsString();
                    advance = false;
                    break;
                case "SessionToken":
                    session = xml.ReadElementContentAsString();
                    advance = false;
                    break;
                case "Expiration":
                    expiration = xml.ReadElementContentAsString();
                    advance = false;
                    break;
            }
        }

        if (id is null || secret is null || session is null || expiration is null)
        {
            throw new IOException("The STS endpoint answered without credentials.");
        }

        return new SigningCredentials(id, secret, session, DateTimeOffset.Parse(expiration, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
    }
}
