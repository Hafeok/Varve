// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http;
using System.Text.Json;
using Varve.Protocol.Client.Model;

namespace Varve.WebUi.SignIn;

/// <summary>
/// The client's half of "a token naming both planes is sent only to the
/// issuer's token endpoint, never to the server" (ADR 0201, amended
/// 2026-10-28): before a token is attached to a request, its <c>aud</c> is
/// read, without validating it, and a token naming more than one audience is
/// refused, so nothing reaches the network. The server's refusal of a token
/// naming both planes (ADR 0163 point 1) stays the guarantee; this keeps the
/// Web UI from ever offering one.
/// </summary>
internal static class OneAudience
{
    /// <summary>The token, unless it names more than one audience.</summary>
    /// <exception cref="HttpRequestException">The token names more than one audience.</exception>
    internal static BearerToken? Checked(BearerToken? token) =>
        token is { } bearer && Audiences(bearer.Value) > 1
            ? throw new HttpRequestException("The Web UI refused to send a token naming more than one audience: no plane accepts one, and a token naming both planes goes only to the issuer's token endpoint (ADR 0201).")
            : token;

    // The number of audiences a JWT names, or 0 when the token is not a JWT
    // whose payload can be read: an opaque token names none the client can
    // see, and the server, which validates JWTs alone, refuses it anyway.
    internal static int Audiences(string token)
    {
        using JsonDocument? payload = Jwt.Payload(token);
        return payload is not null && payload.RootElement.TryGetProperty("aud", out JsonElement aud)
            ? aud.ValueKind switch
            {
                JsonValueKind.Array => aud.GetArrayLength(),
                JsonValueKind.String => 1,
                _ => 0,
            }
            : 0;
    }
}
