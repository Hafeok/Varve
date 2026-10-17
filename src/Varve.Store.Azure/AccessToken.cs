// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Store.Azure;

/// <summary>
/// A bearer token and when it expires. It renders as a placeholder, so that
/// no log line, message or span can carry it by accident (ADR 0137,
/// TokensStayInTheAdapter).
/// </summary>
internal readonly struct AccessToken(string value, DateTimeOffset expiresOn)
{
    /// <summary>The token itself; read only to put it in an <c>Authorization</c> header.</summary>
    public string Value { get; } = value;

    public DateTimeOffset ExpiresOn { get; } = expiresOn;

    public override string ToString() => "(a token, expiring " + ExpiresOn.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + ")";
}

/// <summary>Where a token comes from.</summary>
internal interface ITokenSource
{
    ValueTask<AccessToken> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A token kept until five minutes before it expires, then fetched again
/// (ADR 0137). The expiry decides when to fetch a credential, never anything
/// about the store.
/// </summary>
internal sealed class CachedToken(ITokenSource source, TimeProvider clock) : IDisposable
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _fetching = new(1, 1);
    private AccessToken? _token;

    public async ValueTask<AccessToken> GetAsync(CancellationToken cancellationToken)
    {
        if (_token is { } held && held.ExpiresOn - Margin > clock.GetUtcNow())
        {
            return held;
        }

        await _fetching.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_token is { } again && again.ExpiresOn - Margin > clock.GetUtcNow())
            {
                return again;
            }

            AccessToken fetched = await source.FetchAsync(cancellationToken).ConfigureAwait(false);
            _token = fetched;
            return fetched;
        }
        finally
        {
            _fetching.Release();
        }
    }

    public void Dispose() => _fetching.Dispose();
}
