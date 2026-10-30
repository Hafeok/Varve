// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Varve.Protocol.Client;

/// <summary>
/// Sends one request under the limits (ADR 0103): the response with its
/// headers read and its body still to stream, or an
/// <see cref="HttpRequestException"/> when the headers do not arrive within
/// the timeout. The request is disposed either way.
/// </summary>
internal static class Requests
{
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, ClientLimits limits, CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(limits.Timeout);

        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("<" + request.RequestUri + "> did not answer within " + limits.Timeout.ToString() + ".");
        }
        finally
        {
            request.Dispose();
        }
    }
}
