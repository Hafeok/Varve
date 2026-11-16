// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// The token for one request (ADR 0127): the provider's answer, asked for once
/// more when it expires within the skew, and refused when it still does. The
/// client never sends a token it knows has expired.
/// </summary>
internal static class Tokens
{
    internal static async ValueTask<BearerToken?> ForRequestAsync(BearerTokenProvider? tokens, TimeProvider? clock, CancellationToken cancellationToken)
    {
        if (tokens is null)
        {
            return null;
        }

        BearerToken? token = await tokens(cancellationToken).ConfigureAwait(false);

        if (token is BearerToken first && Stale(first, clock!))
        {
            token = await tokens(cancellationToken).ConfigureAwait(false);

            if (token is BearerToken again && Stale(again, clock!))
            {
                throw new BearerTokenExpiredException(again.ExpiresOn);
            }
        }

        return token;
    }

    private static bool Stale(BearerToken token, TimeProvider clock) =>
        token.ExpiresOn != DateTimeOffset.MaxValue && token.ExpiresOn - SparqlHttpClient.TokenExpirySkew <= clock.GetUtcNow();
}
