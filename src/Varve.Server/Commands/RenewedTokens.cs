// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;

namespace Varve.Server.Commands;

/// <summary>
/// The CLI's token provider (ADRs 0105, 0127): the access token the issuer
/// gave, renewed a minute before it expires by the refresh token when there
/// is one, by client credentials when a secret was given, so a long command
/// such as <c>feed</c> outlives one token. With neither, the token is kept
/// to its end and the client then refuses to send it.
/// </summary>
internal sealed class RenewedTokens(OidcClient oidc, string clientId, string? secret, string? scope, Action<string>? keep, OidcTokens first)
{
    private static readonly TimeSpan Early = TimeSpan.FromMinutes(1);
    private readonly Lock _sync = new();
    private (OidcTokens Tokens, DateTimeOffset Expires) _current = (first, Expiry(first));
    private Task? _renewal;

    internal async ValueTask<BearerToken?> GetAsync(CancellationToken cancellationToken)
    {
        Task? renewal;

        lock (_sync)
        {
            if (_current.Expires == DateTimeOffset.MaxValue || _current.Expires - Early > TimeProvider.System.GetUtcNow())
            {
                return new BearerToken(_current.Tokens.AccessToken, _current.Expires);
            }

            // One renewal at a time: a rotated refresh token is spent by the
            // first use, so two concurrent requests must not both spend it.
            _renewal ??= RenewAsync();
            renewal = _renewal;
        }

        try
        {
            await renewal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_renewal, renewal) && renewal.IsCompleted)
                {
                    _renewal = null;
                }
            }
        }

        lock (_sync)
        {
            return new BearerToken(_current.Tokens.AccessToken, _current.Expires);
        }
    }

    private async Task RenewAsync()
    {
        OidcTokens tokens;

        lock (_sync)
        {
            tokens = _current.Tokens;
        }

        OidcTokens renewed;

        if (tokens.RefreshToken is { } refresh)
        {
            renewed = await oidc.RefreshAsync(clientId, secret, refresh, CancellationToken.None).ConfigureAwait(false);
            renewed = renewed with { RefreshToken = renewed.RefreshToken ?? refresh };
            keep?.Invoke(renewed.RefreshToken!);
        }
        else if (secret is not null)
        {
            renewed = await oidc.ClientCredentialsAsync(clientId, secret, scope, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            return;
        }

        lock (_sync)
        {
            _current = (renewed, Expiry(renewed));
        }
    }

    // An access token the issuer gave no lifetime for is used until the
    // server refuses it; one with a lifetime, until a minute before its end.
    private static DateTimeOffset Expiry(OidcTokens tokens) =>
        tokens.ExpiresIn is TimeSpan lifetime ? TimeProvider.System.GetUtcNow() + lifetime : DateTimeOffset.MaxValue;
}
