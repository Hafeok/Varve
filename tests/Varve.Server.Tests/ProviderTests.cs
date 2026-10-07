// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Auth test layers (b) and (c) and the real-provider legs (ADR 0100): tokens
/// from an issuer the test did not write, through real OAuth 2.0 flows, against
/// the real middleware with discovery over HTTP. Each leg reads its provider
/// from the environment and is skipped without it — unless
/// <c>VARVE_AUTH_LEGS_REQUIRED</c> names the leg, which the workflow sets on the
/// job that runs it, so that a broken container fails rather than skips.
/// </summary>
public sealed class ProviderTests
{
    private const string Update = "INSERT DATA { <http://ex/a> <http://ex/p> \"from a provider\" }";

    // ---- (b) mock-oauth2-server: VARVE_MOCK_OAUTH2 is its address, e.g. http://localhost:8080.

    [Fact]
    public async Task Mock_oauth2_client_credentials_token_writes_and_names_its_agent()
    {
        string issuer = Leg("mock-oauth2", "VARVE_MOCK_OAUTH2").TrimEnd('/') + "/varve";
        using HttpClient provider = Loopback();
        string token = await TokenAsync(provider, issuer + "/token", new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "varve-writer",
            ["client_secret"] = "not-checked-by-the-mock",
            ["scope"] = "api://varve",
        });

        await using RunningServer server = await StartAsync(issuer, "api://varve", "roles", "sub", read: [], write: ["varve.write"]);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, token)).StatusCode);
        string feed = await (await SendAsync(server, HttpMethod.Get, "datasets/d/feed?to=1", token)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("agent <" + issuer + "#varve-writer>", feed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mock_oauth2_authorization_code_with_pkce_reads_and_may_not_write()
    {
        string issuer = Leg("mock-oauth2", "VARVE_MOCK_OAUTH2").TrimEnd('/') + "/varve";
        using HttpClient provider = Loopback();
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        const string Redirect = "http://127.0.0.1:9/callback";
        string authorize = issuer + "/authorize?client_id=varve-cli&response_type=code&scope=openid&state=s"
            + "&redirect_uri=" + Uri.EscapeDataString(Redirect)
            + "&code_challenge=" + challenge + "&code_challenge_method=S256";

        // The mock's login form, posted as a person would post it.
        using HttpResponseMessage login = await provider.PostAsync(new Uri(authorize), new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "alice" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        string code = Query(login.Headers.Location!)["code"];

        string token = await TokenAsync(provider, issuer + "/token", new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = "varve-cli",
            ["redirect_uri"] = Redirect,
            ["code_verifier"] = verifier,
        });

        await using RunningServer server = await StartAsync(issuer, "api://varve", "roles", "sub", read: ["varve.read"], write: []);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(server, HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateAsync(server, token)).StatusCode);
    }

    // ---- (c) Zitadel: VARVE_ZITADEL_SEED is the file eng/zitadel-seed.cs wrote.

    [Fact]
    public async Task Zitadel_client_credentials_token_writes_with_its_project_role()
    {
        JsonNode seed = JsonNode.Parse(await File.ReadAllTextAsync(Leg("zitadel", "VARVE_ZITADEL_SEED"), TestContext.Current.CancellationToken))!;
        string issuer = (string)seed["issuer"]!;
        using HttpClient provider = Loopback();
        string token = await TokenAsync(provider, issuer + "/oauth/v2/token", new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = (string)seed["writer"]!["clientId"]!,
            ["client_secret"] = (string)seed["writer"]!["clientSecret"]!,
            ["scope"] = (string)seed["scope"]!,
        });

        await using RunningServer server = await StartAsync(issuer, (string)seed["audience"]!, (string)seed["roleClaim"]!, "sub", read: [], write: ["write"]);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, token)).StatusCode);
        string answer = await (await SendAsync(server, HttpMethod.Get, "datasets/d/sparql?query=" + Uri.EscapeDataString("ASK { ?s ?p \"from a provider\" }"), token)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"boolean\":true", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zitadel_device_code_token_reads_and_may_not_write()
    {
        JsonNode seed = JsonNode.Parse(await File.ReadAllTextAsync(Leg("zitadel", "VARVE_ZITADEL_SEED"), TestContext.Current.CancellationToken))!;
        string issuer = (string)seed["issuer"]!;
        string client = (string)seed["deviceClientId"]!;
        using HttpClient provider = Loopback();

        // The device starts the flow.
        using HttpResponseMessage started = await provider.PostAsync(new Uri(issuer + "/oauth/v2/device_authorization"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = client,
            ["scope"] = (string)seed["scope"]!,
        }), TestContext.Current.CancellationToken);
        JsonNode authorization = await JsonAsync(started);

        // The person signs in and enters the code, through the API a login UI uses.
        using HttpClient login = Loopback();
        login.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)seed["loginClientToken"]!);
        JsonNode session = await JsonAsync(await login.PostAsync(new Uri(issuer + "/v2/sessions"), Json(new JsonObject
        {
            ["checks"] = new JsonObject
            {
                ["user"] = new JsonObject { ["loginName"] = (string)seed["reader"]!["loginName"]! },
                ["password"] = new JsonObject { ["password"] = (string)seed["reader"]!["password"]! },
            },
        }), TestContext.Current.CancellationToken));
        JsonNode request = await JsonAsync(await login.GetAsync(new Uri(issuer + "/v2/oidc/device_authorization/" + (string)authorization["user_code"]!), TestContext.Current.CancellationToken));
        using HttpResponseMessage approved = await login.PostAsync(new Uri(issuer + "/v2/oidc/device_authorization/" + (string)request["deviceAuthorizationRequest"]!["id"]!), Json(new JsonObject
        {
            ["session"] = new JsonObject { ["sessionId"] = (string)session["sessionId"]!, ["sessionToken"] = (string)session["sessionToken"]! },
        }), TestContext.Current.CancellationToken);
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The device polls, honouring the interval it was given.
        string? token = null;
        TimeSpan interval = TimeSpan.FromSeconds((int?)authorization["interval"] ?? 5);

        for (int attempt = 0; token is null && attempt < 12; attempt++)
        {
            using HttpResponseMessage polled = await provider.PostAsync(new Uri(issuer + "/oauth/v2/token"), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = (string)authorization["device_code"]!,
                ["client_id"] = client,
            }), TestContext.Current.CancellationToken);
            JsonNode answer = JsonNode.Parse(await polled.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            token = (string?)answer["access_token"];

            if (token is null)
            {
                Assert.Contains((string)answer["error"]!, (string[])["authorization_pending", "slow_down"]);
                await Task.Delay(interval, TestContext.Current.CancellationToken);
            }
        }

        Assert.NotNull(token);
        await using RunningServer server = await StartAsync(issuer, (string)seed["audience"]!, (string)seed["roleClaim"]!, "sub", read: ["read"], write: ["write"]);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(server, HttpMethod.Get, "datasets/d/sparql?query=ASK%7B%7D", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UpdateAsync(server, token)).StatusCode);
    }

    // ---- Entra ID and Google: secrets from a main-only environment, absent elsewhere.

    /// <summary>
    /// Entra's client credentials. The API's application registration issues
    /// v2 tokens (<c>accessTokenAcceptedVersion</c> 2) and grants the client
    /// the app role in <c>VARVE_ENTRA_ROLE</c> (default <c>Varve.Write</c>).
    /// </summary>
    [Fact]
    public async Task Entra_client_credentials_token_writes()
    {
        string tenant = Leg("entra", "VARVE_ENTRA_TENANT");
        string audience = Environment.GetEnvironmentVariable("VARVE_ENTRA_AUDIENCE") ?? throw new InvalidOperationException("VARVE_ENTRA_AUDIENCE");
        string role = Environment.GetEnvironmentVariable("VARVE_ENTRA_ROLE") ?? "Varve.Write";
        using HttpClient provider = new();
        string token = await TokenAsync(provider, "https://login.microsoftonline.com/" + tenant + "/oauth2/v2.0/token", new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Environment.GetEnvironmentVariable("VARVE_ENTRA_CLIENT_ID") ?? throw new InvalidOperationException("VARVE_ENTRA_CLIENT_ID"),
            ["client_secret"] = Environment.GetEnvironmentVariable("VARVE_ENTRA_CLIENT_SECRET") ?? throw new InvalidOperationException("VARVE_ENTRA_CLIENT_SECRET"),
            ["scope"] = audience + "/.default",
        });

        await using RunningServer server = await StartAsync("https://login.microsoftonline.com/" + tenant + "/v2.0", audience, "roles", "oid", read: [], write: [role]);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, token)).StatusCode);
    }

    /// <summary>
    /// Google: a service account's ID token for an audience of our choosing,
    /// by the JWT-bearer grant with a self-signed assertion. Google issues no
    /// roles, so the permission is granted to the account's e-mail claim.
    /// <c>VARVE_GOOGLE_SERVICE_ACCOUNT</c> is the key file's JSON.
    /// </summary>
    [Fact]
    public async Task Google_service_account_id_token_writes()
    {
        JsonNode account = JsonNode.Parse(Leg("google", "VARVE_GOOGLE_SERVICE_ACCOUNT"))!;
        string email = (string)account["client_email"]!;
        const string Audience = "https://w3id.org/varve/tests";
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string unsigned = Base64Url(Encoding.UTF8.GetBytes(new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = (string)account["private_key_id"]! }.ToJsonString()))
            + "." + Base64Url(Encoding.UTF8.GetBytes(new JsonObject
            {
                ["iss"] = email,
                ["sub"] = email,
                ["aud"] = "https://oauth2.googleapis.com/token",
                ["iat"] = now,
                ["exp"] = now + 300,
                ["target_audience"] = Audience,
            }.ToJsonString()));
        using RSA key = RSA.Create();
        key.ImportFromPem((string)account["private_key"]!);
        string assertion = unsigned + "." + Base64Url(key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using HttpClient provider = new();
        using HttpResponseMessage response = await provider.PostAsync(new Uri("https://oauth2.googleapis.com/token"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        }), TestContext.Current.CancellationToken);
        string token = (string)(await JsonAsync(response))["id_token"]!;

        await using RunningServer server = await StartAsync("https://accounts.google.com", Audience, "email", "sub", read: [], write: [email]);
        Assert.Equal(HttpStatusCode.NoContent, (await UpdateAsync(server, token)).StatusCode);
    }

    // ----

    /// <summary>The variable's value; or a skip, or a failure when the leg is required.</summary>
    private static string Leg(string leg, string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        string[] required = (Environment.GetEnvironmentVariable("VARVE_AUTH_LEGS_REQUIRED") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (required.Contains(leg, StringComparer.Ordinal))
        {
            Assert.Fail("The " + leg + " leg is required here (VARVE_AUTH_LEGS_REQUIRED) and " + variable + " is not set.");
        }

        Assert.Skip("The " + leg + " leg needs " + variable + " (ADR 0100).");
        return string.Empty;
    }

    private static Task<RunningServer> StartAsync(string issuer, string audience, string roleClaim, string subjectClaim, string[] read, string[] write)
    {
        Dictionary<string, string> settings = new()
        {
            ["Varve:Auth:Mode"] = "Oidc",
            ["Varve:Auth:Authority"] = issuer,
            ["Varve:Auth:Audiences:0"] = audience,
            ["Varve:Auth:RoleClaimType"] = roleClaim,
            ["Varve:Auth:SubjectClaim"] = subjectClaim,
            ["Varve:Auth:RequireHttpsMetadata"] = issuer.StartsWith("https:", StringComparison.Ordinal) ? "true" : "false",
            ["Varve:Datasets:d:Storage"] = "Memory",
        };

        for (int i = 0; i < read.Length; i++)
        {
            settings["Varve:Auth:Datasets:d:Read:" + i] = read[i];
        }

        for (int i = 0; i < write.Length; i++)
        {
            settings["Varve:Auth:Datasets:d:Write:" + i] = write[i];
        }

        return RunningServer.StartAsync(settings);
    }

    private static Task<HttpResponseMessage> UpdateAsync(RunningServer server, string token) =>
        SendAsync(server, HttpMethod.Post, "datasets/d/sparql", token, new StringContent(Update, Encoding.UTF8, "application/sparql-update"));

    private static async Task<HttpResponseMessage> SendAsync(RunningServer server, HttpMethod method, string path, string token, HttpContent? content = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await server.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> TokenAsync(HttpClient provider, string endpoint, Dictionary<string, string> form)
    {
        using HttpResponseMessage response = await provider.PostAsync(new Uri(endpoint), new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
        return (string)(await JsonAsync(response))["access_token"]!;
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " from " + response.RequestMessage?.RequestUri + ": " + body);
        return JsonNode.Parse(body)!;
    }

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    // A provider on loopback, reached directly: a proxy configured for the
    // machine must not carry it, and a redirect is the test's to read.
    private static HttpClient Loopback() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });

    private static Dictionary<string, string> Query(Uri address) =>
        address.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]), StringComparer.Ordinal);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
