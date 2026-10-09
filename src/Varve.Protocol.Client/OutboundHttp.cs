// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Net.Http;
using System.Threading;

namespace Varve.Protocol.Client;

/// <summary>
/// An <see cref="HttpClient"/> set up as the endpoint policy expects (ADR
/// 0103): no automatic redirects, no cookies, and no timeout of its own, since
/// <see cref="ClientLimits"/> bounds each call. The host owns the client it
/// gets back.
/// </summary>
public static class OutboundHttp
{
    /// <summary>A client over a <see cref="SocketsHttpHandler"/> that follows no redirect and keeps no cookie.</summary>
    public static HttpClient CreateClient() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
}
