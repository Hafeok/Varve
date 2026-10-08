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

namespace Varve.Protocol.Client;

/// <summary>
/// One dataset's endpoints over HTTP (ADR 0102): queries and updates by the
/// SPARQL 1.1 Protocol, the Graph Store's reads, the change feed, and the
/// admin calls of ADR 0105, each carrying the bearer token the caller set
/// and the headers of ADRs 0094 and 0096. Every call answers the response
/// with its headers read and its body still to stream; the caller owns it.
/// The limits bound the time to the headers; the body is the caller's to
/// bound.
/// </summary>
public sealed class SparqlHttpClient
{
    private readonly HttpClient _http;
    private readonly ClientLimits _limits;

    /// <summary>A client of the dataset at <paramref name="dataset"/>, an absolute address ending in <c>/</c>.</summary>
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

    /// <summary>The bearer token every call carries, or null for none.</summary>
    public string? AccessToken { get; set; }

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

    /// <summary>The change feed from <paramref name="from"/> (exclusive) to <paramref name="to"/> (inclusive, or live with null), in the line format or as events.</summary>
    public Task<HttpResponseMessage> FeedAsync(long from, long? to, string? graph, bool eventStream, CancellationToken cancellationToken)
    {
        StringBuilder query = new("feed?from=");
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

    /// <summary>The dataset's status (ADRs 0101, 0105), JSON.</summary>
    public Task<HttpResponseMessage> StatusAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "status")), cancellationToken);

    /// <summary>A checkpoint at the head, or at <paramref name="at"/> (ADR 0105).</summary>
    public Task<HttpResponseMessage> CheckpointAsync(long? at, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, new Uri(Dataset, at is long position ? "checkpoints?at=" + position.ToString(CultureInfo.InvariantCulture) : "checkpoints")), cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (AccessToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
