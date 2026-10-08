// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Varve.Server.Tests;

/// <summary>How a minted token differs from a good one: the failures a validator must catch.</summary>
public enum TokenFlaw
{
    None,
    Expired,
    NotYetValid,
    WrongAudience,
    WrongIssuer,
    BadSignature,
    UnknownKey,
    Unsigned,
}

/// <summary>
/// Test layer (a) of ADR 0100: an OIDC issuer in the test, on a loopback
/// port, serving its discovery document and a JWKS, with a fresh RSA key per
/// run. <see cref="Mint"/> signs RS256 tokens with the BCL alone — no JWT
/// library — so what the server validates is exactly what a provider sends.
/// </summary>
internal sealed class TestIssuer : IAsyncDisposable
{
    internal const string Audience = "api://varve-tests";

    private readonly WebApplication _app;
    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _stranger = RSA.Create(2048);
    private readonly string _kid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private TestIssuer(WebApplication app) => _app = app;

    private readonly Dictionary<string, (string ClientId, bool Approved)> _deviceCodes = [];
    private readonly HashSet<string> _refreshTokens = [];

    /// <summary>The issuer's identifier and authority: <c>http://127.0.0.1:port/</c>.</summary>
    internal string Issuer { get; private set; } = string.Empty;

    /// <summary>The one confidential client the token endpoint knows, and its secret.</summary>
    internal const string ClientId = "varve-cli";

    internal const string ClientSecret = "s3cret";

    /// <summary>The claims a token minted by the token endpoint carries: the roles the test wants the CLI's caller to have.</summary>
    internal IReadOnlyDictionary<string, object> IssuedClaims { get; set; } = new Dictionary<string, object> { ["sub"] = "cli", ["roles"] = new[] { "writer" } };

    /// <summary>How many device codes were approved: the test "enters the code" by calling <see cref="Approve"/>.</summary>
    internal int DeviceCodesIssued { get; private set; }

    /// <summary>Approves every pending device code, as the person at the verification page would.</summary>
    internal void Approve()
    {
        lock (_deviceCodes)
        {
            foreach (string code in _deviceCodes.Keys.ToArray())
            {
                _deviceCodes[code] = (_deviceCodes[code].ClientId, true);
            }
        }
    }

    /// <summary>Whether the token endpoint has issued this refresh token and not seen it revoked.</summary>
    internal bool Knows(string refreshToken)
    {
        lock (_refreshTokens)
        {
            return _refreshTokens.Contains(refreshToken);
        }
    }

    internal static async Task<TestIssuer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        TestIssuer issuer = new(app);
        app.MapGet("/.well-known/openid-configuration", (RequestDelegate)(context => WriteJson(context, json =>
        {
            json.WriteString("issuer", issuer.Issuer);
            json.WriteString("jwks_uri", issuer.Issuer + "jwks");
            json.WriteString("token_endpoint", issuer.Issuer + "token");
            json.WriteString("device_authorization_endpoint", issuer.Issuer + "device");
            json.WriteStartArray("id_token_signing_alg_values_supported");
            json.WriteStringValue("RS256");
            json.WriteEndArray();
        })));
        app.MapGet("/jwks", (RequestDelegate)(context => WriteJson(context, json =>
        {
            RSAParameters key = issuer._key.ExportParameters(false);
            json.WriteStartArray("keys");
            json.WriteStartObject();
            json.WriteString("kty", "RSA");
            json.WriteString("use", "sig");
            json.WriteString("alg", "RS256");
            json.WriteString("kid", issuer._kid);
            json.WriteString("n", Base64Url(key.Modulus!));
            json.WriteString("e", Base64Url(key.Exponent!));
            json.WriteEndObject();
            json.WriteEndArray();
        })));
        app.MapPost("/device", (RequestDelegate)(async context =>
        {
            Microsoft.AspNetCore.Http.IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

            lock (issuer._deviceCodes)
            {
                issuer._deviceCodes[code] = (form["client_id"].ToString(), false);
                issuer.DeviceCodesIssued++;
            }

            await WriteJson(context, json =>
            {
                json.WriteString("device_code", code);
                json.WriteString("user_code", "ABCD-EFGH");
                json.WriteString("verification_uri", issuer.Issuer + "activate");
                json.WriteNumber("interval", 1);
                json.WriteNumber("expires_in", 300);
            });
        }));
        app.MapPost("/token", (RequestDelegate)(async context =>
        {
            Microsoft.AspNetCore.Http.IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted);
            string grant = form["grant_type"].ToString();
            string? error = null;

            switch (grant)
            {
                case "client_credentials":
                    error = form["client_id"] == ClientId && form["client_secret"] == ClientSecret ? null : "invalid_client";
                    break;
                case "refresh_token":
                    lock (issuer._refreshTokens)
                    {
                        error = issuer._refreshTokens.Remove(form["refresh_token"].ToString()) ? null : "invalid_grant";
                    }

                    break;
                case "urn:ietf:params:oauth:grant-type:device_code":
                    lock (issuer._deviceCodes)
                    {
                        error = issuer._deviceCodes.TryGetValue(form["device_code"].ToString(), out (string ClientId, bool Approved) pending)
                            ? pending.Approved ? null : "authorization_pending"
                            : "invalid_grant";

                        if (error is null)
                        {
                            issuer._deviceCodes.Remove(form["device_code"].ToString());
                        }
                    }

                    break;
                default:
                    error = "unsupported_grant_type";
                    break;
            }

            if (error is not null)
            {
                context.Response.StatusCode = 400;
                await WriteJson(context, json => json.WriteString("error", error));
                return;
            }

            string refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            lock (issuer._refreshTokens)
            {
                issuer._refreshTokens.Add(refresh);
            }

            await WriteJson(context, json =>
            {
                json.WriteString("access_token", issuer.Mint(issuer.IssuedClaims));
                json.WriteString("token_type", "Bearer");
                json.WriteNumber("expires_in", 300);

                if (grant != "client_credentials")
                {
                    json.WriteString("refresh_token", refresh);
                }
            });
        }));
        await app.StartAsync(CancellationToken.None);
        string address = ((IApplicationBuilder)app).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        issuer.Issuer = address.TrimEnd('/') + "/";
        return issuer;
    }

    /// <summary>A token with these claims, valid for five minutes unless it carries a flaw.</summary>
    internal string Mint(IReadOnlyDictionary<string, object> claims, TokenFlaw flaw = TokenFlaw.None)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Json(json =>
        {
            json.WriteString("alg", flaw == TokenFlaw.Unsigned ? "none" : "RS256");
            json.WriteString("typ", "JWT");
            json.WriteString("kid", flaw == TokenFlaw.UnknownKey ? "not-a-key" : _kid);
        });
        string payload = Json(json =>
        {
            json.WriteString("iss", flaw == TokenFlaw.WrongIssuer ? "https://elsewhere.example/" : Issuer);
            json.WriteString("aud", flaw == TokenFlaw.WrongAudience ? "api://someone-else" : Audience);
            json.WriteNumber("iat", now - 60);
            json.WriteNumber("nbf", flaw == TokenFlaw.NotYetValid ? now + 3600 : now - 60);
            json.WriteNumber("exp", flaw == TokenFlaw.Expired ? now - 3600 : now + 300);

            foreach ((string name, object value) in claims)
            {
                switch (value)
                {
                    case string text:
                        json.WriteString(name, text);
                        break;
                    case string[] values:
                        json.WriteStartArray(name);
                        foreach (string item in values)
                        {
                            json.WriteStringValue(item);
                        }

                        json.WriteEndArray();
                        break;
                    case System.Text.Json.Nodes.JsonNode node:
                        json.WritePropertyName(name);
                        node.WriteTo(json);
                        break;
                }
            }
        });

        string unsigned = Base64Url(Encoding.UTF8.GetBytes(header)) + "." + Base64Url(Encoding.UTF8.GetBytes(payload));

        if (flaw == TokenFlaw.Unsigned)
        {
            return unsigned + ".";
        }

        // A bad signature claims the issuer's key and is signed by another; an
        // unknown key is one the JWKS does not publish, under a kid it does not
        // know. (The issuer's own key under a strange kid is a genuine
        // signature, and validators rightly try every published key.)
        RSA signer = flaw is TokenFlaw.BadSignature or TokenFlaw.UnknownKey ? _stranger : _key;
        byte[] signature = signer.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return unsigned + "." + Base64Url(signature);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
        _key.Dispose();
        _stranger.Dispose();
    }

    private static Task WriteJson(HttpContext context, Action<Utf8JsonWriter> write)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(Json(write));
    }

    private static string Json(Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
