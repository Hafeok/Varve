// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Server.Commands;

/// <summary>The tokens an issuer handed out: the access token, and a refresh token when it gave one.</summary>
internal sealed record OidcTokens(string AccessToken, string? RefreshToken);

/// <summary>An issuer's refusal, or an answer that is not a token.</summary>
internal sealed class OidcException(string message) : Exception(message);

/// <summary>
/// The CLI's OIDC client (ADRs 0037, 0105): discovery, the client credentials
/// flow, the device code flow, and refresh, over <see cref="HttpClient"/>
/// and <c>System.Text.Json</c>'s document reader. No package, no reflection.
/// </summary>
internal sealed class OidcClient(HttpClient http, string authority)
{
    private string? _tokenEndpoint;
    private string? _deviceEndpoint;

    internal async Task<OidcTokens> ClientCredentialsAsync(string clientId, string secret, string? scope, CancellationToken cancellationToken)
    {
        await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> form = new() { ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = secret };

        if (scope is not null)
        {
            form["scope"] = scope;
        }

        return await TokenAsync(form, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<OidcTokens> RefreshAsync(string clientId, string? secret, string refreshToken, CancellationToken cancellationToken)
    {
        await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> form = new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = refreshToken };

        if (secret is not null)
        {
            form["client_secret"] = secret;
        }

        return await TokenAsync(form, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>RFC 8628: the issuer names a code and a page; the person enters one at the other; this polls until it is done.</summary>
    internal async Task<OidcTokens> DeviceCodeAsync(string clientId, string? scope, TextWriter prompt, CancellationToken cancellationToken)
    {
        await DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (_deviceEndpoint is null)
        {
            throw new OidcException("The issuer " + authority + " offers no device authorization endpoint; use --client-secret, or --token.");
        }

        Dictionary<string, string> form = new() { ["client_id"] = clientId };

        if (scope is not null)
        {
            form["scope"] = scope;
        }

        using JsonDocument started = await PostAsync(_deviceEndpoint, form, cancellationToken).ConfigureAwait(false);
        JsonElement root = started.RootElement;
        string deviceCode = Required(root, "device_code");
        string userCode = Required(root, "user_code");
        string uri = root.TryGetProperty("verification_uri_complete", out JsonElement complete) && complete.ValueKind == JsonValueKind.String
            ? complete.GetString()!
            : Required(root, "verification_uri");
        int interval = root.TryGetProperty("interval", out JsonElement given) && given.TryGetInt32(out int seconds) ? Math.Max(1, seconds) : 5;
        await prompt.WriteLineAsync("Open " + uri + " and enter the code " + userCode + ".").ConfigureAwait(false);
        await prompt.FlushAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, string> poll = new() { ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = deviceCode, ["client_id"] = clientId };

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
            using JsonDocument polled = await PostAsync(_tokenEndpoint!, poll, cancellationToken, allowError: true).ConfigureAwait(false);

            if (polled.RootElement.TryGetProperty("access_token", out _))
            {
                return Tokens(polled.RootElement);
            }

            switch (polled.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() : null)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                default:
                    throw new OidcException("The device code flow ended: " + Describe(polled.RootElement));
            }
        }
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        if (_tokenEndpoint is not null)
        {
            return;
        }

        string address = authority.TrimEnd('/') + "/.well-known/openid-configuration";
        using HttpResponseMessage response = await http.GetAsync(new Uri(address, UriKind.Absolute), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OidcException("The issuer's discovery document at " + address + " answered " + (int)response.StatusCode + ".");
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        _tokenEndpoint = Required(document.RootElement, "token_endpoint");
        _deviceEndpoint = document.RootElement.TryGetProperty("device_authorization_endpoint", out JsonElement device) && device.ValueKind == JsonValueKind.String
            ? device.GetString()
            : null;
    }

    private async Task<OidcTokens> TokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using JsonDocument answer = await PostAsync(_tokenEndpoint!, form, cancellationToken).ConfigureAwait(false);
        return Tokens(answer.RootElement);
    }

    private static OidcTokens Tokens(JsonElement root) =>
        new(Required(root, "access_token"), root.TryGetProperty("refresh_token", out JsonElement refresh) && refresh.ValueKind == JsonValueKind.String ? refresh.GetString() : null);

    private async Task<JsonDocument> PostAsync(string endpoint, Dictionary<string, string> form, CancellationToken cancellationToken, bool allowError = false)
    {
        using FormUrlEncodedContent content = new(form);
        using HttpResponseMessage response = await http.PostAsync(new Uri(endpoint, UriKind.Absolute), content, cancellationToken).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new OidcException(endpoint + " answered " + (int)response.StatusCode + " with a body that is not JSON.");
        }

        if (!response.IsSuccessStatusCode && !allowError)
        {
            string reason = Describe(document.RootElement);
            document.Dispose();
            throw new OidcException(endpoint + " refused the request: " + reason);
        }

        return document;
    }

    private static string Required(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new OidcException("The issuer's answer has no '" + name + "'.");

    private static string Describe(JsonElement root)
    {
        string error = root.TryGetProperty("error", out JsonElement code) ? code.ToString() : "unknown error";
        return root.TryGetProperty("error_description", out JsonElement description) ? error + " (" + description.ToString() + ")" : error;
    }
}
