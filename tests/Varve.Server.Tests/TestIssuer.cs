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

    /// <summary>The issuer's identifier and authority: <c>http://127.0.0.1:port/</c>.</summary>
    internal string Issuer { get; private set; } = string.Empty;

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
