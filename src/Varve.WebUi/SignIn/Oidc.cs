// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Varve.WebUi.SignIn;

/// <summary>How each plane's token is asked for (ADR 0201, amended 2026-10-28).</summary>
internal enum ManagementGrant
{
    /// <summary>The data token from the sign-in; the management token by the refresh grant with its scope alone.</summary>
    Refresh,

    /// <summary>
    /// The sign-in asks for both planes; each plane's token is exchanged (RFC
    /// 8693) from the token it answers, from the public client.
    /// </summary>
    Exchange,
}

/// <summary>
/// The Web UI's sign-in settings (ADR 0201), from <c>appsettings.json</c>'s
/// <c>Varve:Auth</c>: the issuer, the Web UI's own public client, the two
/// planes' scopes, how each plane's token is asked for, with the audience an
/// exchange names, and the claim whose roles the shell shows (the server's
/// <c>Auth:RoleClaimType</c>, <c>roles</c> by default).
/// </summary>
internal sealed record OidcOptions(Uri Authority, string ClientId, string DataScope, string ManageScope, ManagementGrant Grant = ManagementGrant.Refresh, string? ManageAudience = null, string? DataAudience = null, string RoleClaimType = "roles")
{
    /// <summary>The settings, or a message naming what is missing.</summary>
    internal static OidcOptions From(string? authority, string? clientId, string? dataScope, string? manageScope, string? manageGrant = null, string? manageAudience = null, string? dataAudience = null, string? roleClaimType = null)
    {
        List<string> missing = [];
        ManagementGrant? grant = manageGrant switch
        {
            null or "" or "refresh" => ManagementGrant.Refresh,
            "exchange" => ManagementGrant.Exchange,
            _ => null,
        };

        if (grant is null)
        {
            missing.Add("Varve:Auth:ManageGrant as refresh or exchange");
        }

        if (!Uri.TryCreate(authority, UriKind.Absolute, out Uri? issuer) || (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp))
        {
            missing.Add("Varve:Auth:Authority (the issuer's absolute URL)");
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            missing.Add("Varve:Auth:ClientId");
        }

        if (string.IsNullOrWhiteSpace(dataScope))
        {
            missing.Add("Varve:Auth:DataScope");
        }

        if (string.IsNullOrWhiteSpace(manageScope))
        {
            missing.Add("Varve:Auth:ManageScope");
        }

        return missing.Count == 0
            ? new OidcOptions(issuer!, clientId!, dataScope!, manageScope!, grant!.Value, string.IsNullOrWhiteSpace(manageAudience) ? null : manageAudience, string.IsNullOrWhiteSpace(dataAudience) ? null : dataAudience, string.IsNullOrWhiteSpace(roleClaimType) ? "roles" : roleClaimType)
            : throw new InvalidOperationException("Sign-in is configured (Varve:Auth:Mode is Oidc) but these are not set: " + string.Join(", ", missing) + ".");
    }
}

/// <summary>Fills a span with random bytes.</summary>
internal delegate void Filler(Span<byte> bytes);

/// <summary>
/// The random values of a sign-in: the PKCE verifier, the <c>state</c> and the
/// <c>nonce</c>, each 32 bytes from <see cref="RandomNumberGenerator"/>,
/// base64url without padding, 43 characters (RFC 7636 §4.1, ADR 0201).
/// </summary>
internal static class OidcRandom
{
    internal const int Bytes = 32;

    /// <summary>The source every sign-in uses.</summary>
    internal static Filler Default => RandomNumberGenerator.Fill;

    internal static string Token(Filler fill)
    {
        Span<byte> bytes = stackalloc byte[Bytes];
        fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }
}

/// <summary>PKCE's <c>S256</c> challenge (RFC 7636 §4.2).</summary>
internal static class Pkce
{
    internal static string Challenge(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

/// <summary>What the issuer's discovery document says that sign-in uses.</summary>
internal sealed record Discovery(string Issuer, Uri AuthorizationEndpoint, Uri TokenEndpoint, Uri? EndSessionEndpoint, bool IssParameterSupported)
{
    internal static Discovery Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return new Discovery(
            Text(root, "issuer") ?? throw new FormatException("The discovery document names no issuer."),
            Address(root, "authorization_endpoint") ?? throw new FormatException("The discovery document names no authorization_endpoint."),
            Address(root, "token_endpoint") ?? throw new FormatException("The discovery document names no token_endpoint."),
            Address(root, "end_session_endpoint"),
            root.TryGetProperty("authorization_response_iss_parameter_supported", out JsonElement iss) && iss.ValueKind == JsonValueKind.True);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Uri? Address(JsonElement root, string name) =>
        Uri.TryCreate(Text(root, name), UriKind.Absolute, out Uri? address) ? address : null;
}

/// <summary>
/// The ID token's claims that sign-in checks (OIDC Core §3.1.3.7): <c>iss</c>,
/// <c>aud</c>, <c>exp</c> and <c>nonce</c>, and the name it shows.
/// </summary>
/// <remarks>
/// The signature is not checked. The token came straight from the token
/// endpoint over TLS, which §3.1.3.7 point 6 allows in place of it, and it
/// authorises nothing: it is read for the display name, and the planes' access
/// tokens are what the server checks (ADR 0201).
/// </remarks>
internal sealed record IdToken(string Issuer, IReadOnlyList<string> Audiences, DateTimeOffset Expires, string? Nonce, string? Name)
{
    internal static IdToken Parse(string jwt)
    {
        string[] parts = jwt.Split('.');

        if (parts.Length != 3)
        {
            throw new FormatException("The ID token is not a JWT.");
        }

        using JsonDocument document = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        JsonElement claims = document.RootElement;
        List<string> audiences = [];

        if (claims.TryGetProperty("aud", out JsonElement aud))
        {
            if (aud.ValueKind == JsonValueKind.String)
            {
                audiences.Add(aud.GetString()!);
            }
            else if (aud.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement one in aud.EnumerateArray())
                {
                    audiences.Add(one.GetString() ?? string.Empty);
                }
            }
        }

        return new IdToken(
            Claim(claims, "iss") ?? string.Empty,
            audiences,
            claims.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue,
            Claim(claims, "nonce"),
            Claim(claims, "preferred_username") ?? Claim(claims, "name") ?? Claim(claims, "sub"));
    }

    private static string? Claim(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// A sign-in in flight, kept in session storage across the redirect and taken
/// out, once, by the callback (ADR 0201). None of it is a token.
/// </summary>
internal sealed record PendingSignIn(string State, string Nonce, string Verifier, string ReturnPath, bool Silent)
{
    internal string ToJson()
    {
        using System.IO.MemoryStream buffer = new();

        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("state", State);
            json.WriteString("nonce", Nonce);
            json.WriteString("verifier", Verifier);
            json.WriteString("return", ReturnPath);
            json.WriteBoolean("silent", Silent);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static PendingSignIn? FromJson(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            return new PendingSignIn(
                root.GetProperty("state").GetString()!,
                root.GetProperty("nonce").GetString()!,
                root.GetProperty("verifier").GetString()!,
                root.GetProperty("return").GetString()!,
                root.GetProperty("silent").GetBoolean());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
