// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// One dataset's endpoints over HTTP (ADR 0103): queries and updates by the
/// SPARQL 1.1 Protocol, the Graph Store's reads, the commits resource, and
/// the admin calls of ADRs 0106 and 0118, each carrying the bearer token its provider answers (ADR 0127)
/// and the headers of ADRs 0094 and 0096. Every call answers the response
/// with its headers read and its body still to stream; the caller owns it.
/// The limits bound the time to the headers; the body is the caller's to
/// bound.
/// </summary>
public sealed class SparqlHttpClient
{
    /// <summary>
    /// How long before its expiry a token is treated as expired (ADR 0127):
    /// a request takes time to arrive, and a token that expires in flight is a
    /// <c>401</c> the caller cannot tell from a refusal.
    /// </summary>
    public static readonly TimeSpan TokenExpirySkew = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly ClientLimits _limits;
    private readonly BearerTokenProvider? _tokens;
    private readonly TimeProvider? _clock;

    /// <summary>
    /// A client of the dataset at <paramref name="dataset"/> whose every request
    /// carries the token <paramref name="tokens"/> answers for it (ADR 0127), its
    /// expiry measured on <paramref name="clock"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The address is not absolute http or https.</exception>
    public SparqlHttpClient(HttpClient http, Uri dataset, ClientLimits limits, BearerTokenProvider tokens, TimeProvider clock)
        : this(http, dataset, limits)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(clock);
        _tokens = tokens;
        _clock = clock;
    }

    /// <summary>An unauthenticated client of the dataset at <paramref name="dataset"/>, an absolute address ending in <c>/</c>.</summary>
    /// <exception cref="ArgumentException">The address is not absolute http or https.</exception>
    public SparqlHttpClient(HttpClient http, Uri dataset, ClientLimits limits)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(dataset);

        if (!dataset.IsAbsoluteUri || (dataset.Scheme != Uri.UriSchemeHttp && dataset.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A dataset's address is an absolute http or https URL.", nameof(dataset));
        }

        _http = http;
        Dataset = dataset.AbsoluteUri.EndsWith('/') ? dataset : new Uri(dataset.AbsoluteUri + "/");
        _limits = limits;
    }

    /// <summary>The dataset's address, ending in <c>/</c>.</summary>
    public Uri Dataset { get; }

    /// <summary>A query, <c>POST</c>ed as <c>application/sparql-query</c>, asking for <paramref name="accept"/>; <paramref name="asOf"/> is a <c>Varve-As-Of</c> selector.</summary>
    public Task<HttpResponseMessage> QueryAsync(string query, string accept, string? asOf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(Dataset, "sparql")) { Content = new StringContent(query, Encoding.UTF8, "application/sparql-query") };
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (asOf is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>An update, <c>POST</c>ed as <c>application/sparql-update</c>, with <paramref name="ifMatch"/> as the expected position.</summary>
    public Task<HttpResponseMessage> UpdateAsync(string update, long? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(Dataset, "sparql")) { Content = new StringContent(update, Encoding.UTF8, "application/sparql-update") };

        if (ifMatch is long expected)
        {
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + expected.ToString(CultureInfo.InvariantCulture) + "\"");
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>A graph by the Graph Store Protocol: the default graph for a null <paramref name="graph"/>.</summary>
    public Task<HttpResponseMessage> GetGraphAsync(string? graph, string accept, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, graph is null ? "graphs?default" : "graphs?graph=" + Uri.EscapeDataString(graph)));
        request.Headers.TryAddWithoutValidation("Accept", accept);
        return SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The commits from <paramref name="from"/> (exclusive) to <paramref name="to"/>
    /// (inclusive, or a live tail with null), in the line format or as events
    /// (ADRs 0097, 0118).
    /// </summary>
    public Task<HttpResponseMessage> CommitsAsync(long from, long? to, string? graph, bool eventStream, CancellationToken cancellationToken)
    {
        StringBuilder query = new("commits?from=");
        query.Append(from.ToString(CultureInfo.InvariantCulture));

        if (to is long end)
        {
            query.Append("&to=").Append(end.ToString(CultureInfo.InvariantCulture));
        }

        if (graph is not null)
        {
            query.Append("&graph=").Append(Uri.EscapeDataString(graph));
        }

        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, query.ToString()));
        request.Headers.TryAddWithoutValidation("Accept", eventStream ? "text/event-stream" : "application/vnd.varve.delta");
        return SendAsync(request, cancellationToken);
    }

    /// <summary>One commit, as one record of the line format (ADR 0118).</summary>
    public Task<HttpResponseMessage> CommitAsync(long position, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, "commits/" + position.ToString(CultureInfo.InvariantCulture)));
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.varve.delta");
        return SendAsync(request, cancellationToken);
    }

    /// <summary>The dataset's state, <c>open</c>, <c>closed</c> or <c>failed</c>, JSON (ADR 0118); a server admin's call.</summary>
    public Task<HttpResponseMessage> StateAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "state")), cancellationToken);

    /// <summary>Asks for the dataset to be <paramref name="open"/> or closed, idempotently (ADR 0118); a server admin's call.</summary>
    public Task<HttpResponseMessage> SetStateAsync(bool open, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, new Uri(Dataset, "state"))
        {
            Content = new StringContent(open ? "{\"state\":\"open\"}" : "{\"state\":\"closed\"}", Encoding.UTF8, "application/json"),
        }, cancellationToken);

    /// <summary>The settings at the head, JSON, with the position as <c>ETag</c> (ADR 0118).</summary>
    public Task<HttpResponseMessage> SettingsAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "settings")), cancellationToken);

    /// <summary>Replaces the settings with <paramref name="json"/>, the whole of them, under <paramref name="ifMatch"/> (ADR 0118).</summary>
    public Task<HttpResponseMessage> PutSettingsAsync(string json, long? ifMatch, CancellationToken cancellationToken) =>
        SettingsWriteAsync(HttpMethod.Put, json, "application/json", ifMatch, cancellationToken);

    /// <summary>Merges <paramref name="json"/> into the settings (RFC 7396) under <paramref name="ifMatch"/> (ADR 0118).</summary>
    public Task<HttpResponseMessage> PatchSettingsAsync(string json, long? ifMatch, CancellationToken cancellationToken) =>
        SettingsWriteAsync(HttpMethod.Patch, json, "application/merge-patch+json", ifMatch, cancellationToken);

    private Task<HttpResponseMessage> SettingsWriteAsync(HttpMethod method, string json, string mediaType, long? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);
        HttpRequestMessage request = new(method, new Uri(Dataset, "settings")) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

        if (ifMatch is long expected)
        {
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + expected.ToString(CultureInfo.InvariantCulture) + "\"");
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>The dataset's status (ADRs 0101, 0106), JSON.</summary>
    public Task<HttpResponseMessage> StatusAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "status")), cancellationToken);

    /// <summary>A checkpoint at the head, or at <paramref name="at"/> (ADR 0106).</summary>
    public Task<HttpResponseMessage> CheckpointAsync(long? at, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(Dataset, at is long position ? "checkpoints?at=" + position.ToString(CultureInfo.InvariantCulture) : "checkpoints")), cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            if (await TokenAsync(cancellationToken).ConfigureAwait(false) is BearerToken token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            }
        }
        catch
        {
            request.Dispose();
            throw;
        }

        using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_limits.Timeout);

        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("<" + request.RequestUri + "> did not answer within " + _limits.Timeout.ToString() + ".");
        }
        finally
        {
            request.Dispose();
        }
    }

    // The token for one request (ADR 0127): the provider's answer, asked for
    // once more when it expires within the skew, and refused when it still
    // does; the client never sends a token it knows has expired.
    private async ValueTask<BearerToken?> TokenAsync(CancellationToken cancellationToken)
    {
        if (_tokens is null)
        {
            return null;
        }

        BearerToken? token = await _tokens(cancellationToken).ConfigureAwait(false);

        if (token is BearerToken first && Stale(first))
        {
            token = await _tokens(cancellationToken).ConfigureAwait(false);

            if (token is BearerToken again && Stale(again))
            {
                throw new BearerTokenExpiredException(again.ExpiresOn);
            }
        }

        return token;
    }

    private bool Stale(BearerToken token) =>
        token.ExpiresOn != DateTimeOffset.MaxValue && token.ExpiresOn - TokenExpirySkew <= _clock!.GetUtcNow();

    /// <summary>The <c>Varve-Position</c> a response carries, or null.</summary>
    public static long? PositionOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues("Varve-Position", out IEnumerable<string>? values)
            && long.TryParse(string.Join(string.Empty, values), NumberStyles.None, CultureInfo.InvariantCulture, out long position)
            ? position
            : null;
    }
}
