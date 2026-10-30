// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Varve.WebUi.SignIn;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The Web UI's own OIDC public client (ADR 0201): the PKCE challenge, where
/// its random values come from, and every check a hostile or confused callback
/// must fail.
/// </summary>
public class SignInTests
{
    private const string IssuerAddress = "https://issuer.example/tenant";
    private const string Client = "varve-webui";
    private static readonly Uri Application = new("https://varve.example/ui/");
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    // RFC 7636 Appendix B.
    [Fact]
    public void The_challenge_is_the_rfc_7636_appendix_b_vector() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void The_verifier_state_and_nonce_are_32_bytes_from_the_random_number_generator()
    {
        Assert.Equal(((Filler)RandomNumberGenerator.Fill).Method, OidcRandom.Default.Method);
        Assert.Null(OidcRandom.Default.Target);

        List<int> asked = [];
        string token = OidcRandom.Token(bytes =>
        {
            asked.Add(bytes.Length);
            bytes.Fill(0xAB);
        });

        Assert.Equal([32], asked);
        Assert.Equal(43, token.Length);
        Assert.Equal(Base64Url.EncodeToString(Enumerable.Repeat((byte)0xAB, 32).ToArray()), token);
        Assert.NotEqual(OidcRandom.Token(OidcRandom.Default), OidcRandom.Token(OidcRandom.Default));
    }

    [Fact]
    public async Task The_redirect_asks_for_the_data_scope_alone_with_pkce_and_prompt_none_when_silent()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/health", silent: true, CancellationToken.None);

        Dictionary<string, string> query = Query(harness.Browser.Left.Single());
        PendingSignIn pending = harness.Store.Pending!;
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(Client, query["client_id"]);
        Assert.Equal("https://varve.example/ui/signin", query["redirect_uri"]);
        Assert.Equal("openid offline_access api://varve/read", query["scope"]);
        Assert.Equal(pending.State, query["state"]);
        Assert.Equal(pending.Nonce, query["nonce"]);
        Assert.Equal(Pkce.Challenge(pending.Verifier), query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("none", query["prompt"]);
        Assert.DoesNotContain("api://varve-manage", query["scope"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_good_callback_signs_both_planes_in_each_with_its_own_token()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/health", silent: false, CancellationToken.None);

        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State);

        Assert.True(outcome.SignedIn, outcome.Failure);
        Assert.Equal("/health", outcome.ReturnPath);
        Assert.Equal("data-1", (await harness.Session.Data(CancellationToken.None))!.Value.Value);
        Assert.Equal("manage-1", (await harness.Session.Management(CancellationToken.None))!.Value.Value);
        Assert.Equal("alice", harness.Session.Name);

        // The code was exchanged with the verifier; the management token was
        // redeemed from the refresh token for the management scope alone.
        Assert.Equal(harness.Store.Taken!.Verifier, issuer.Exchanges.Single()["code_verifier"]);
        Assert.Equal("api://varve-manage/operate", issuer.Refreshes.Single()["scope"]);
    }

    [Fact]
    public async Task A_replayed_callback_is_refused() =>
        await Gen.String[Gen.Char.AlphaNumeric, 1, 64].SampleAsync(async code =>
        {
            Issuer issuer = new() { Code = code };
            Harness harness = new(issuer);
            await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
            string state = harness.Store.Pending!.State;

            Assert.True((await harness.CallbackAsync(state)).SignedIn);
            SignInOutcome again = await harness.CallbackAsync(state);
            Assert.False(again.SignedIn);
            Assert.Contains("already used", again.Failure, StringComparison.Ordinal);
            Assert.Single(issuer.Exchanges);
        }, iter: 50);

    [Fact]
    public async Task A_callback_with_another_state_is_refused_and_exchanges_nothing() =>
        await Gen.String[Gen.Char.AlphaNumeric, 0, 64].SampleAsync(async state =>
        {
            Issuer issuer = new();
            Harness harness = new(issuer);
            await harness.Session.StartAsync("/", silent: false, CancellationToken.None);

            if (state == harness.Store.Pending!.State)
            {
                return;
            }

            SignInOutcome outcome = await harness.CallbackAsync(state);
            Assert.False(outcome.SignedIn);
            Assert.Contains("state", outcome.Failure, StringComparison.Ordinal);
            Assert.Empty(issuer.Exchanges);
            Assert.False(harness.Session.DataSignedIn);
        }, iter: 50);

    [Fact]
    public async Task An_id_token_with_another_nonce_is_refused_and_no_token_is_kept() =>
        await Gen.String[Gen.Char.AlphaNumeric, 0, 64].SampleAsync(async nonce =>
        {
            Issuer issuer = new() { Nonce = nonce };
            Harness harness = new(issuer);
            await harness.Session.StartAsync("/", silent: false, CancellationToken.None);

            if (nonce == harness.Store.Pending!.Nonce)
            {
                return;
            }

            SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State);
            Assert.False(outcome.SignedIn);
            Assert.Contains("nonce", outcome.Failure, StringComparison.Ordinal);
            Assert.False(harness.Session.DataSignedIn);
            Assert.Null(await harness.Session.Data(CancellationToken.None));
        }, iter: 50);

    [Theory]
    [InlineData("iss", "The answer names an issuer other than")]
    [InlineData("id-iss", "The ID token's issuer")]
    [InlineData("aud", "not for this client")]
    [InlineData("exp", "has expired")]
    [InlineData("no-code", "no code")]
    [InlineData("exchange", "did not exchange the code")]
    public async Task Each_check_refuses_on_its_own(string broken, string why)
    {
        Issuer issuer = new()
        {
            IssuerInCallback = broken == "iss" ? "https://evil.example/" : IssuerAddress,
            IdIssuer = broken == "id-iss" ? "https://evil.example/" : IssuerAddress,
            Audience = broken == "aud" ? "someone-else" : Client,
            Expires = broken == "exp" ? Now.AddSeconds(-1) : Now.AddMinutes(5),
            RefuseExchange = broken == "exchange",
        };
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);

        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State, code: broken != "no-code");

        Assert.False(outcome.SignedIn);
        Assert.Contains(why, outcome.Failure, StringComparison.Ordinal);
        Assert.False(harness.Session.DataSignedIn);
    }

    [Fact]
    public async Task A_silent_sign_in_the_issuer_needs_the_person_for_becomes_an_interactive_one()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/fleet", silent: true, CancellationToken.None);

        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State, error: "login_required");

        Assert.True(outcome.Redirected);
        Assert.Equal(2, harness.Browser.Left.Count);
        Dictionary<string, string> again = Query(harness.Browser.Left[1]);
        Assert.False(again.ContainsKey("prompt"));
        Assert.Equal("/fleet", harness.Store.Pending!.ReturnPath);
    }

    [Fact]
    public async Task An_interactive_sign_in_the_issuer_refuses_is_a_failure_not_a_loop()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);

        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State, error: "login_required");

        Assert.False(outcome.Redirected);
        Assert.Contains("login_required", outcome.Failure, StringComparison.Ordinal);
        Assert.Single(harness.Browser.Left);
    }

    [Fact]
    public async Task An_issuer_that_will_not_redeem_for_the_management_scope_leaves_that_plane_signed_out()
    {
        Issuer issuer = new() { RefuseRefresh = true };
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);

        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State);

        Assert.True(outcome.SignedIn);
        Assert.True(harness.Session.DataSignedIn);
        Assert.False(harness.Session.ManagementSignedIn);
        Assert.Contains("management scope", harness.Session.ManagementAbsence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_near_expiry_is_renewed_for_its_own_plane_with_the_rotated_refresh_token()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        await harness.CallbackAsync(harness.Store.Pending!.State);

        harness.Clock.Advance(TimeSpan.FromSeconds(300 - 20));
        BearerToken? data = await harness.Session.Data(CancellationToken.None);

        Assert.Equal("data-2", data!.Value.Value);
        Dictionary<string, string> renewal = issuer.Refreshes[1];
        Assert.Equal("api://varve/read", renewal["scope"]);
        Assert.Equal("refresh-2", renewal["refresh_token"]);
    }

    [Fact]
    public async Task With_exchange_the_sign_in_asks_for_both_planes_and_each_token_is_exchanged_from_the_public_client()
    {
        Issuer issuer = new();
        Harness harness = new(issuer, Exchanging("urn:zitadel:iam:org:project:id:42:aud", "urn:zitadel:iam:org:project:id:7:aud"));
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        Assert.Equal("openid offline_access api://varve/read api://varve-manage/operate", Query(harness.Browser.Left.Last())["scope"]);
        await harness.CallbackAsync(harness.Store.Pending!.State);

        Assert.True(harness.Session.DataSignedIn);
        Assert.True(harness.Session.ManagementSignedIn);
        Assert.Equal("data-exchanged-1", (await harness.Session.Data(CancellationToken.None))!.Value.Value);
        Assert.Equal("manage-exchanged-2", (await harness.Session.Management(CancellationToken.None))!.Value.Value);

        // The sign-in's token, data-1, went to the token endpoint and nowhere else.
        Assert.Equal(2, issuer.TokenExchanges.Count);
        Assert.All(issuer.TokenExchanges, exchange =>
        {
            Assert.Equal("data-1", exchange["subject_token"]);
            Assert.Equal("urn:ietf:params:oauth:token-type:access_token", exchange["subject_token_type"]);
            Assert.Equal("urn:ietf:params:oauth:token-type:jwt", exchange["requested_token_type"]);
            Assert.Equal(Client, exchange["client_id"]);
            Assert.DoesNotContain("client_secret", exchange.Keys);
            Assert.DoesNotContain("actor_token", exchange.Keys);
        });
        Assert.Equal(("api://varve/read", "urn:zitadel:iam:org:project:id:7:aud"), (issuer.TokenExchanges[0]["scope"], issuer.TokenExchanges[0]["audience"]));
        Assert.Equal(("api://varve-manage/operate", "urn:zitadel:iam:org:project:id:42:aud"), (issuer.TokenExchanges[1]["scope"], issuer.TokenExchanges[1]["audience"]));
        Assert.Empty(issuer.Refreshes);
    }

    [Fact]
    public async Task With_exchange_a_plane_near_expiry_is_renewed_by_refreshing_the_combined_token_and_exchanging_again()
    {
        Issuer issuer = new();
        Harness harness = new(issuer, Exchanging(null, null));
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        await harness.CallbackAsync(harness.Store.Pending!.State);

        harness.Clock.Advance(TimeSpan.FromSeconds(300 - 20));
        BearerToken? management = await harness.Session.Management(CancellationToken.None);

        // The refresh asks for no scope, so it answers the sign-in's scopes:
        // a token for both planes, exchanged for this plane's and not kept.
        Dictionary<string, string> refresh = Assert.Single(issuer.Refreshes);
        Assert.DoesNotContain("scope", refresh.Keys);
        Assert.Equal("refresh-1", refresh["refresh_token"]);
        Dictionary<string, string> renewal = issuer.TokenExchanges[2];
        Assert.Equal("data-1", renewal["subject_token"]);
        Assert.Equal("api://varve-manage/operate", renewal["scope"]);
        Assert.DoesNotContain("audience", renewal.Keys);
        Assert.Equal("manage-exchanged-3", management!.Value.Value);
    }

    [Fact]
    public async Task With_exchange_a_data_exchange_the_issuer_refuses_is_a_failed_sign_in_that_keeps_nothing()
    {
        Issuer issuer = new() { RefuseTokenExchange = true };
        Harness harness = new(issuer, Exchanging(null, null));
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        SignInOutcome outcome = await harness.CallbackAsync(harness.Store.Pending!.State);

        Assert.False(outcome.SignedIn);
        Assert.Contains("data plane", outcome.Failure, StringComparison.Ordinal);
        Assert.False(harness.Session.DataSignedIn);
        Assert.False(harness.Session.ManagementSignedIn);
    }

    [Theory]
    [InlineData("""{"aud":"api://varve"}""", 1)]
    [InlineData("""{"aud":["api://varve"]}""", 1)]
    [InlineData("""{"aud":["api://varve","api://varve-manage"]}""", 2)]
    [InlineData("""{"sub":"alice"}""", 0)]
    public void The_audiences_are_read_from_the_payload_without_validating(string payload, int audiences) =>
        Assert.Equal(audiences, OneAudience.Audiences(Jwt(payload)));

    [Fact]
    public void An_opaque_token_names_no_audience_the_client_can_read() =>
        Assert.Equal(0, OneAudience.Audiences("Zm9vYmFy-opaque"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_token_naming_both_planes_fails_in_the_client_and_nothing_reaches_the_network(bool management)
    {
        string combined = Jwt("""{"aud":["api://varve","api://varve-manage"]}""");
        Issuer issuer = new() { SignInAccess = combined, ManageAccess = combined };
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        await harness.CallbackAsync(harness.Store.Pending!.State);
        Network network = new();
        using HttpClient http = new(network);
        Uri server = new("https://varve.example/");

        HttpRequestException refused = management
            ? await Assert.ThrowsAsync<HttpRequestException>(() => new VarveManagementClient(http, server, ClientLimits.Default, harness.Session.Management, TimeProvider.System).DatasetsAsync(CancellationToken.None))
            : await Assert.ThrowsAsync<HttpRequestException>(() => new DatasetClients(http, server, harness.Session.Data, TimeProvider.System).For(new DatasetName("notes")).ReadCommitsAsync(new FeedPosition(0), new FeedPosition(1), CancellationToken.None));

        Assert.Contains("more than one audience", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, network.Sent);
    }

    [Fact]
    public async Task A_token_naming_one_audience_is_attached()
    {
        Issuer issuer = new() { SignInAccess = Jwt("""{"aud":"api://varve"}""") };
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        await harness.CallbackAsync(harness.Store.Pending!.State);

        Assert.Equal(issuer.SignInAccess, (await harness.Session.Data(CancellationToken.None))!.Value.Value);
    }

    private static string Jwt(string payload) =>
        "eyJhbGciOiJub25lIn0." + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload)) + ".";

    [Theory]
    [InlineData(null, "Refresh")]
    [InlineData("refresh", "Refresh")]
    [InlineData("exchange", "Exchange")]
    public void The_management_grant_is_read_from_the_settings(string? setting, string expected) =>
        Assert.Equal(expected, OidcOptions.From(IssuerAddress, Client, "d", "m", setting, null).Grant.ToString());

    [Fact]
    public void An_unknown_management_grant_is_refused_by_name() =>
        Assert.Contains("Varve:Auth:ManageGrant", Assert.Throws<InvalidOperationException>(() => OidcOptions.From(IssuerAddress, Client, "d", "m", "password", null)).Message, StringComparison.Ordinal);

    private static OidcOptions Exchanging(string? manageAudience, string? dataAudience) =>
        new(new Uri(IssuerAddress), Client, "api://varve/read", "api://varve-manage/operate", ManagementGrant.Exchange, manageAudience, dataAudience);

    // Counts what would reach the network.
    private sealed class Network : HttpMessageHandler
    {
        internal int Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Sign_out_drops_both_tokens_and_leaves_for_the_end_session_endpoint()
    {
        Issuer issuer = new();
        Harness harness = new(issuer);
        await harness.Session.StartAsync("/", silent: false, CancellationToken.None);
        await harness.CallbackAsync(harness.Store.Pending!.State);

        await harness.Session.SignOutAsync(CancellationToken.None);

        Assert.False(harness.Session.DataSignedIn);
        Assert.False(harness.Session.ManagementSignedIn);
        Assert.Null(await harness.Session.Management(CancellationToken.None));
        Uri left = harness.Browser.Left.Last();
        Assert.StartsWith(IssuerAddress + "/logout?", left.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("https://varve.example/ui/signedout", Query(left)["post_logout_redirect_uri"]);
    }

    private static Dictionary<string, string> Query(Uri address) =>
        address.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

    private sealed class Harness
    {
        internal Harness(Issuer issuer, OidcOptions? options = null)
        {
            Issuer = issuer;
            issuer.Clock = Clock;
            Session = new SignInSession(
                options ?? new OidcOptions(new Uri(IssuerAddress), Client, "api://varve/read", "api://varve-manage/operate"),
                new HttpClient(issuer),
                Store,
                Browser,
                Clock,
                OidcRandom.Default,
                Application);
            issuer.Store = Store;
        }

        internal Issuer Issuer { get; }

        internal Store Store { get; } = new();

        internal Browser Browser { get; } = new();

        internal Clock Clock { get; } = new();

        internal SignInSession Session { get; }

        internal Task<SignInOutcome> CallbackAsync(string state, bool code = true, string? error = null)
        {
            string query = "state=" + Uri.EscapeDataString(state) + "&iss=" + Uri.EscapeDataString(Issuer.IssuerInCallback);
            query += error is not null ? "&error=" + error : code ? "&code=" + Uri.EscapeDataString(Issuer.Code) : string.Empty;
            return Session.CompleteAsync(new Uri(Application, "signin?" + query), CancellationToken.None);
        }
    }

    private sealed class Store : ISignInStore
    {
        internal PendingSignIn? Pending { get; private set; }

        internal PendingSignIn? Taken { get; private set; }

        public Task PutAsync(PendingSignIn pending)
        {
            // Through its JSON, as session storage holds it.
            Pending = PendingSignIn.FromJson(pending.ToJson());
            return Task.CompletedTask;
        }

        public Task<PendingSignIn?> TakeAsync()
        {
            PendingSignIn? pending = Pending;
            Pending = null;
            Taken = pending ?? Taken;
            return Task.FromResult(pending);
        }
    }

    private sealed class Browser : IBrowser
    {
        internal List<Uri> Left { get; } = [];

        public void Leave(Uri address) => Left.Add(address);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = Now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }

    // An issuer with a discovery document, a token endpoint that redeems one
    // code once, and a refresh grant that rotates its token.
    private sealed class Issuer : HttpMessageHandler
    {
        private int _issued;
        private bool _codeUsed;

        internal string Code { get; init; } = "the-code";

        internal string? Nonce { get; init; }

        internal string IssuerInCallback { get; init; } = IssuerAddress;

        internal string IdIssuer { get; init; } = IssuerAddress;

        internal string Audience { get; init; } = Client;

        internal DateTimeOffset Expires { get; init; } = Now.AddMinutes(5);

        internal bool RefuseExchange { get; init; }

        internal bool RefuseRefresh { get; init; }

        internal bool RefuseTokenExchange { get; init; }

        internal string SignInAccess { get; init; } = "data-1";

        internal string? ManageAccess { get; init; }

        internal Store? Store { get; set; }

        internal TimeProvider? Clock { get; set; }

        internal List<Dictionary<string, string>> Exchanges { get; } = [];

        internal List<Dictionary<string, string>> Refreshes { get; } = [];

        internal List<Dictionary<string, string>> TokenExchanges { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == IssuerAddress + "/.well-known/openid-configuration")
            {
                return Json(new Dictionary<string, object>
                {
                    ["issuer"] = IssuerAddress,
                    ["authorization_endpoint"] = IssuerAddress + "/authorize",
                    ["token_endpoint"] = IssuerAddress + "/token",
                    ["end_session_endpoint"] = IssuerAddress + "/logout",
                    ["authorization_response_iss_parameter_supported"] = true,
                });
            }

            Dictionary<string, string> form = (await request.Content!.ReadAsStringAsync(cancellationToken))
                .Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));

            if (form["grant_type"] == "authorization_code")
            {
                if (RefuseExchange || _codeUsed || form["code"] != Code)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                _codeUsed = true;
                Exchanges.Add(form);
                return Json(new Dictionary<string, object>
                {
                    ["access_token"] = SignInAccess,
                    ["expires_in"] = 300,
                    ["refresh_token"] = "refresh-1",
                    ["id_token"] = IdToken(Nonce ?? Store!.Taken!.Nonce),
                });
            }

            if (form["grant_type"] == "urn:ietf:params:oauth:grant-type:token-exchange")
            {
                TokenExchanges.Add(form);

                if (RefuseTokenExchange)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                return Json(new Dictionary<string, object>
                {
                    ["access_token"] = (form["scope"] == "api://varve-manage/operate" ? "manage" : "data") + "-exchanged-" + TokenExchanges.Count,
                    ["issued_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 300,
                });
            }

            Refreshes.Add(form);

            if (RefuseRefresh)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            _issued++;
            string plane = form.GetValueOrDefault("scope") == "api://varve-manage/operate" ? "manage" : "data";
            return Json(new Dictionary<string, object>
            {
                ["access_token"] = plane == "manage" && ManageAccess is { } manage ? manage : plane + "-" + _issued,
                ["expires_in"] = 300,
                ["refresh_token"] = "refresh-" + (_issued + 1),
            });
        }

        private string IdToken(string nonce)
        {
            string payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["iss"] = IdIssuer,
                ["aud"] = new[] { Audience },
                ["exp"] = Expires.ToUnixTimeSeconds(),
                ["nonce"] = nonce,
                ["sub"] = "u-1",
                ["preferred_username"] = "alice",
            });
            return "eyJhbGciOiJub25lIn0." + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload)) + ".unsigned";
        }

        private static HttpResponseMessage Json(Dictionary<string, object> body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
