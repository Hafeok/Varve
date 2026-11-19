// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Varve.WebUi.SignIn;

/// <summary>
/// Who a plane's token says the person is, for display only (#187, review
/// F02): the agent IRI the server names a caller by, <c>iss#sub</c> with the
/// subject percent-encoded as the server encodes it, and the roles the token
/// carries in the configured claim. Read from the payload without
/// validating: it authorises nothing, the server decides.
/// </summary>
/// <param name="Agent">The agent IRI.</param>
/// <param name="Roles">The roles, in the token's order.</param>
internal sealed record Caller(string Agent, IReadOnlyList<string> Roles)
{
    /// <summary>The caller a JWT names, or null when the token is not one, or names no issuer or subject.</summary>
    internal static Caller? Of(string token, string roleClaim)
    {
        if (Jwt.Payload(token) is not { } payload)
        {
            return null;
        }

        using (payload)
        {
            JsonElement root = payload.RootElement;

            if (!root.TryGetProperty("iss", out JsonElement iss) || iss.ValueKind != JsonValueKind.String || iss.GetString() is not { Length: > 0 } issuer
                || !root.TryGetProperty("sub", out JsonElement sub) || sub.ValueKind != JsonValueKind.String || sub.GetString() is not { Length: > 0 } subject
                || issuer.Contains('#', StringComparison.Ordinal))
            {
                return null;
            }

            List<string> roles = [];

            if (root.TryGetProperty(roleClaim, out JsonElement claim))
            {
                switch (claim.ValueKind)
                {
                    case JsonValueKind.String:
                        roles.Add(claim.GetString()!);
                        break;
                    case JsonValueKind.Array:
                        foreach (JsonElement role in claim.EnumerateArray())
                        {
                            if (role.ValueKind == JsonValueKind.String)
                            {
                                roles.Add(role.GetString()!);
                            }
                        }

                        break;
                    case JsonValueKind.Object:
                        // Zitadel's per-project claim: each role a member.
                        foreach (JsonProperty role in claim.EnumerateObject())
                        {
                            roles.Add(role.Name);
                        }

                        break;
                    default:
                        break;
                }
            }

            return new Caller(issuer + "#" + Encode(subject), roles);
        }
    }

    // The server's encoding of a subject in an agent IRI (Varve.Server's
    // TokenIdentity.Encode): unreserved characters as they are, every other
    // UTF-8 byte percent-encoded in upper case.
    private static string Encode(string subject)
    {
        StringBuilder encoded = new(subject.Length * 3);

        foreach (byte b in Encoding.UTF8.GetBytes(subject))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                encoded.Append((char)b);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }
}

/// <summary>
/// Who each plane's token names, as the layout passes it down to the parts
/// that say it (a 403's "Signed in as"): values only, so a part reads who is
/// signed in without reaching the sign-in session, which holds the client's
/// token providers (ADR 0206 point 4, amended 2026-10-30).
/// </summary>
/// <param name="Data">Who the data plane's token names.</param>
/// <param name="Management">Who the management plane's token names.</param>
internal sealed record SignedIn(Caller? Data, Caller? Management);

/// <summary>A JWT's payload, read without validating.</summary>
internal static class Jwt
{
    /// <summary>The payload of a token with three parts whose middle one is a JSON object, or null.</summary>
    internal static JsonDocument? Payload(string token)
    {
        string[] parts = token.Split('.');

        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            JsonDocument payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));

            if (payload.RootElement.ValueKind == JsonValueKind.Object)
            {
                return payload;
            }

            payload.Dispose();
            return null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }
}
