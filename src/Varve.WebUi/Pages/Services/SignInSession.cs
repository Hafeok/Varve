// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;
using Varve.WebUi.SignIn;

namespace Varve.WebUi.Pages.Services;

/// <summary>Where a sign-in in flight is kept across the redirect: session storage in the browser.</summary>
internal interface ISignInStore
{
    Task PutAsync(PendingSignIn pending);

    /// <summary>The sign-in in flight, removed as it is read, so a callback is honoured once.</summary>
    Task<PendingSignIn?> TakeAsync();
}

/// <summary>A top-level navigation away from the application: to the issuer, and back.</summary>
internal interface IBrowser
{
    void Leave(Uri address);
}

/// <summary>How a callback ended.</summary>
internal sealed record SignInOutcome(bool SignedIn, bool Redirected, string? ReturnPath, string? Failure)
{
    internal static SignInOutcome Failed(string why) => new(false, false, null, why);
}

/// <summary>
/// The Web UI's own OIDC public client (ADR 0201): authorisation code with
/// PKCE, one token per plane, held in the WebAssembly heap for the tab's life.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>The sign-in asks for <c>openid offline_access</c> and the data
///   plane's scope. The code exchange gives the data plane's token and a
///   refresh token.</item>
///   <item>The management plane's token comes from the refresh-token grant
///   with the management scope alone. An issuer that will not redeem a
///   refresh token for another scope is not supported (ADR 0201); its plane
///   shows signed out.</item>
///   <item>With <see cref="ManagementGrant.Exchange"/> (ADR 0201, amended
///   2026-10-28), the sign-in asks for both planes' scopes. The token it
///   answers names both planes, so it is sent only to the token endpoint:
///   each plane's token is exchanged from it (RFC 8693), and it is dropped
///   once both exchanges are done. A renewal refreshes it, exchanges again,
///   and drops it again.</item>
///   <item>Whatever the grant, a token naming more than one audience is never
///   attached to a request (<see cref="OneAudience"/>).</item>
///   <item>A token within the client's skew of expiry is renewed by the same
///   grant, with that plane's scope. A rotated refresh token replaces the
///   old one.</item>
///   <item>A reload loses the heap. The layout then starts a sign-in with
///   <c>prompt=none</c>, which an issuer session makes invisible, and an
///   interactive one if the issuer says it needs the person.</item>
/// </list>
/// </remarks>
internal sealed class SignInSession : IDisposable
{
    private static readonly FrozenSet<string> NeedsThePerson =
        new[] { "login_required", "interaction_required", "consent_required", "account_selection_required" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly OidcOptions _options;
    private readonly HttpClient _http;
    private readonly ISignInStore _store;
    private readonly IBrowser _browser;
    private readonly TimeProvider _clock;
    private readonly Filler _fill;
    private readonly Uri _redirect;
    private readonly Uri _signedOut;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private Discovery? _discovery;
    private string? _refreshToken;
    private string? _idToken;
    private BearerToken? _data;
    private BearerToken? _management;

    internal SignInSession(OidcOptions options, HttpClient http, ISignInStore store, IBrowser browser, TimeProvider clock, Filler fill, Uri application)
    {
        _options = options;
        _http = http;
        _store = store;
        _browser = browser;
        _clock = clock;
        _fill = fill;
        _redirect = new Uri(application, "signin");
        _signedOut = new Uri(application, "signedout");
    }

    /// <inheritdoc />
    public void Dispose() => _refreshing.Dispose();

    /// <summary>Raised when either plane signs in or out.</summary>
    internal event Action? Changed;

    internal bool DataSignedIn => _data is not null;

    internal bool ManagementSignedIn => _management is not null;

    /// <summary>Why the management plane has no token, when the data plane has one.</summary>
    internal string? ManagementAbsence { get; private set; }

    /// <summary>The person's name, from the ID token, for display only.</summary>
    internal string? Name { get; private set; }

    /// <summary>The data plane's token, renewed when it is within the skew of expiry.</summary>
    /// <summary>Who the data plane's token names, for display (review F02).</summary>
    internal Caller? DataCaller => _data is { } token ? Caller.Of(token.Value, _options.RoleClaimType) : null;

    /// <summary>Who the management plane's token names, for display (review F02).</summary>
    internal Caller? ManagementCaller => _management is { } token ? Caller.Of(token.Value, _options.RoleClaimType) : null;

    internal BearerTokenProvider Data => async cancellationToken => OneAudience.Checked(await CurrentAsync(isManagement: false, cancellationToken).ConfigureAwait(false));

    /// <summary>The management plane's token, likewise.</summary>
    internal BearerTokenProvider Management => async cancellationToken => OneAudience.Checked(await CurrentAsync(isManagement: true, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Leaves for the issuer's authorisation endpoint, keeping the state, the
    /// nonce, the verifier and where to come back to.
    /// </summary>
    internal async Task StartAsync(string returnPath, bool silent, CancellationToken cancellationToken)
    {
        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        PendingSignIn pending = new(OidcRandom.Token(_fill), OidcRandom.Token(_fill), OidcRandom.Token(_fill), returnPath, silent);
        await _store.PutAsync(pending).ConfigureAwait(false);

        List<KeyValuePair<string, string>> query =
        [
            new("response_type", "code"),
            new("client_id", _options.ClientId),
            new("redirect_uri", _redirect.AbsoluteUri),
            new("scope", "openid offline_access " + _options.DataScope + (Exchanging ? " " + _options.ManageScope : string.Empty)),
            new("state", pending.State),
            new("nonce", pending.Nonce),
            new("code_challenge", Pkce.Challenge(pending.Verifier)),
            new("code_challenge_method", "S256"),
        ];

        if (silent)
        {
            query.Add(new("prompt", "none"));
        }

        _browser.Leave(With(discovery.AuthorizationEndpoint, query));
    }

    /// <summary>
    /// The callback: checks <c>state</c> and <c>iss</c>, exchanges the code with
    /// the verifier, checks the ID token, and takes the management plane's
    /// token. Anything that does not hold is a failure, and nothing is kept.
    /// </summary>
    internal async Task<SignInOutcome> CompleteAsync(Uri callback, CancellationToken cancellationToken)
    {
        PendingSignIn? pending = await _store.TakeAsync().ConfigureAwait(false);

        if (pending is null)
        {
            return SignInOutcome.Failed("No sign-in was in progress in this tab: the answer is stale, or was already used.");
        }

        Dictionary<string, string> query = Query(callback);

        if (!query.TryGetValue("state", out string? state) || !string.Equals(state, pending.State, StringComparison.Ordinal))
        {
            return SignInOutcome.Failed("The answer's state is not the one this sign-in sent.");
        }

        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);

        // RFC 9207: an issuer that says it sends iss is held to it, and one
        // that sends it unasked is checked all the same.
        query.TryGetValue("iss", out string? iss);

        if ((iss is not null || discovery.IssParameterSupported) && !string.Equals(iss, discovery.Issuer, StringComparison.Ordinal))
        {
            return SignInOutcome.Failed("The answer names an issuer other than '" + discovery.Issuer + "'.");
        }

        if (query.TryGetValue("error", out string? error))
        {
            if (pending.Silent && NeedsThePerson.Contains(error))
            {
                await StartAsync(pending.ReturnPath, silent: false, cancellationToken).ConfigureAwait(false);
                return new SignInOutcome(false, true, null, null);
            }

            return SignInOutcome.Failed("The issuer refused the sign-in: " + error + (query.TryGetValue("error_description", out string? description) ? " (" + description + ")" : string.Empty) + ".");
        }

        if (!query.TryGetValue("code", out string? code))
        {
            return SignInOutcome.Failed("The answer carries no code.");
        }

        using JsonDocument? answer = await TokenAsync(discovery,
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", _redirect.AbsoluteUri),
            new("client_id", _options.ClientId),
            new("code_verifier", pending.Verifier),
        ], cancellationToken).ConfigureAwait(false);

        if (answer is null || Text(answer, "access_token") is not { } access || Text(answer, "id_token") is not { } id)
        {
            return SignInOutcome.Failed("The issuer did not exchange the code for the tokens sign-in needs.");
        }

        IdToken claims;

        try
        {
            claims = IdToken.Parse(id);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return SignInOutcome.Failed("The ID token cannot be read.");
        }

        if (!string.Equals(claims.Issuer, discovery.Issuer, StringComparison.Ordinal))
        {
            return SignInOutcome.Failed("The ID token's issuer is not '" + discovery.Issuer + "'.");
        }

        if (!System.Linq.Enumerable.Contains(claims.Audiences, _options.ClientId, StringComparer.Ordinal))
        {
            return SignInOutcome.Failed("The ID token is not for this client.");
        }

        if (claims.Expires <= _clock.GetUtcNow())
        {
            return SignInOutcome.Failed("The ID token has expired.");
        }

        if (!string.Equals(claims.Nonce, pending.Nonce, StringComparison.Ordinal))
        {
            return SignInOutcome.Failed("The ID token's nonce is not the one this sign-in sent.");
        }

        _refreshToken = Text(answer, "refresh_token");
        _idToken = id;
        Name = claims.Name;
        _management = null;
        ManagementAbsence = null;

        if (Exchanging)
        {
            // The sign-in's token names both planes: it goes to the token
            // endpoint alone, and is not kept past these two exchanges.
            _data = await ExchangeAsync(access, _options.DataScope, _options.DataAudience, cancellationToken).ConfigureAwait(false);

            if (_data is null)
            {
                _refreshToken = null;
                _idToken = null;
                Name = null;
                return SignInOutcome.Failed("The issuer did not exchange the sign-in's token for the data plane's.");
            }

            _management = await ExchangeAsync(access, _options.ManageScope, _options.ManageAudience, cancellationToken).ConfigureAwait(false);
            ManagementAbsence = _management is null ? "The issuer did not exchange the sign-in's token for the management plane's." : null;
            Changed?.Invoke();
            return new SignInOutcome(true, false, pending.ReturnPath, null);
        }

        _data = Bearer(answer, access);

        if (_refreshToken is null)
        {
            ManagementAbsence = "The issuer gave no refresh token, so the management plane's token cannot be asked for.";
        }
        else
        {
            _management = await RedeemAsync(_options.ManageScope, cancellationToken).ConfigureAwait(false);
            ManagementAbsence = _management is null ? "The issuer did not redeem the refresh token for the management scope." : null;
        }

        Changed?.Invoke();
        return new SignInOutcome(true, false, pending.ReturnPath, null);
    }

    /// <summary>Drops both tokens and leaves for the issuer's end-session endpoint, if it has one.</summary>
    internal async Task SignOutAsync(CancellationToken cancellationToken)
    {
        string? hint = _idToken;
        _data = null;
        _management = null;
        _refreshToken = null;
        _idToken = null;
        Name = null;
        Changed?.Invoke();

        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (discovery.EndSessionEndpoint is { } end)
        {
            List<KeyValuePair<string, string>> query = [new("client_id", _options.ClientId), new("post_logout_redirect_uri", _signedOut.AbsoluteUri)];

            if (hint is not null)
            {
                query.Add(new("id_token_hint", hint));
            }

            _browser.Leave(With(end, query));
        }
        else
        {
            _browser.Leave(_signedOut);
        }
    }

    private async ValueTask<BearerToken?> CurrentAsync(bool isManagement, CancellationToken cancellationToken)
    {
        BearerToken? token = isManagement ? _management : _data;

        if (token is null || token.Value.ExpiresOn - SparqlHttpClient.TokenExpirySkew > _clock.GetUtcNow())
        {
            return token;
        }

        await _refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            token = isManagement ? _management : _data;

            if (token is null || token.Value.ExpiresOn - SparqlHttpClient.TokenExpirySkew > _clock.GetUtcNow())
            {
                return token;
            }

            BearerToken? renewed = _refreshToken is null ? null
                : Exchanging ? await RenewByExchangeAsync(isManagement, cancellationToken).ConfigureAwait(false)
                : await RedeemAsync(isManagement ? _options.ManageScope : _options.DataScope, cancellationToken).ConfigureAwait(false);

            if (isManagement)
            {
                _management = renewed;
            }
            else
            {
                _data = renewed;
            }

            if (renewed is null)
            {
                Changed?.Invoke();
            }

            return renewed;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    private bool Exchanging => _options.Grant == ManagementGrant.Exchange;

    // The refresh-token grant for one plane's scope, or, with no scope, for
    // the scopes of the sign-in. A rotated refresh token replaces the old one.
    private async Task<BearerToken?> RedeemAsync(string? scope, CancellationToken cancellationToken)
    {
        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        List<KeyValuePair<string, string>> form =
        [
            new("grant_type", "refresh_token"),
            new("refresh_token", _refreshToken!),
            new("client_id", _options.ClientId),
        ];

        if (scope is not null)
        {
            form.Add(new("scope", scope));
        }

        using JsonDocument? answer = await TokenAsync(discovery, [.. form], cancellationToken).ConfigureAwait(false);

        if (answer is null || Text(answer, "access_token") is not { } access)
        {
            return null;
        }

        _refreshToken = Text(answer, "refresh_token") ?? _refreshToken;
        return Bearer(answer, access);
    }

    // A plane's token renewed under exchange: the refresh grant answers a
    // fresh token for both planes, which is exchanged for this plane's and
    // dropped. The refresh token stays the one long-lived credential.
    private async Task<BearerToken?> RenewByExchangeAsync(bool isManagement, CancellationToken cancellationToken)
    {
        BearerToken? combined = await RedeemAsync(null, cancellationToken).ConfigureAwait(false);

        return combined is { } subject
            ? await ExchangeAsync(subject.Value, isManagement ? _options.ManageScope : _options.DataScope, isManagement ? _options.ManageAudience : _options.DataAudience, cancellationToken).ConfigureAwait(false)
            : null;
    }

    // Token exchange (RFC 8693 §2.1) of the token naming both planes for one
    // plane's scope, from the public client: no client secret, no actor
    // token. It asks for a JWT, the only token the server validates; an
    // issuer asked for an access token may answer an opaque one (Zitadel
    // does). The audience is named when the settings name one. A token that
    // still names more than one audience is never attached to a request
    // (OneAudience), and the server refuses one naming both planes (ADR 0163).
    private async Task<BearerToken?> ExchangeAsync(string subject, string scope, string? audience, CancellationToken cancellationToken)
    {
        Discovery discovery = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        List<KeyValuePair<string, string>> form =
        [
            new("grant_type", "urn:ietf:params:oauth:grant-type:token-exchange"),
            new("client_id", _options.ClientId),
            new("subject_token", subject),
            new("subject_token_type", "urn:ietf:params:oauth:token-type:access_token"),
            new("requested_token_type", "urn:ietf:params:oauth:token-type:jwt"),
            new("scope", scope),
        ];

        if (audience is not null)
        {
            form.Add(new("audience", audience));
        }

        using JsonDocument? answer = await TokenAsync(discovery, [.. form], cancellationToken).ConfigureAwait(false);
        return answer is not null && Text(answer, "access_token") is { } access ? Bearer(answer, access) : null;
    }

    private BearerToken Bearer(JsonDocument answer, string access) =>
        new(access, answer.RootElement.TryGetProperty("expires_in", out JsonElement expires) && expires.TryGetInt64(out long seconds)
            ? _clock.GetUtcNow().AddSeconds(seconds)
            : _clock.GetUtcNow().AddMinutes(5));

    private async Task<JsonDocument?> TokenAsync(Discovery discovery, KeyValuePair<string, string>[] form, CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent content = new(form);
        using HttpResponseMessage response = await _http.PostAsync(discovery.TokenEndpoint, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Discovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (_discovery is null)
        {
            Uri document = new(_options.Authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
            _discovery = Discovery.Parse(await _http.GetStringAsync(document, cancellationToken).ConfigureAwait(false));
        }

        return _discovery;
    }

    private static string? Text(JsonDocument document, string name) =>
        document.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Uri With(Uri endpoint, List<KeyValuePair<string, string>> query)
    {
        string separator = endpoint.Query.Length == 0 ? "?" : "&";
        List<string> pairs = query.ConvertAll(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
        return new Uri(endpoint.AbsoluteUri + separator + string.Join("&", pairs));
    }

    private static Dictionary<string, string> Query(Uri address)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        foreach (string pair in address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            values.TryAdd(key, value);
        }

        return values;
    }
}
