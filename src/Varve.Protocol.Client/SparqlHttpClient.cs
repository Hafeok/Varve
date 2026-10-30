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
public sealed partial class SparqlHttpClient
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
    public Task<HttpResponseMessage> QueryAsync(string query, string accept, string? asOf, CancellationToken cancellationToken) =>
        QueryAsync(query, accept, asOf, null, cancellationToken);

    /// <summary>
    /// A query as <see cref="QueryAsync(string, string, string?, CancellationToken)"/> sends it,
    /// answered under <paramref name="entailment"/>, a <c>Varve-Entailment</c>
    /// value: <c>simple</c>, <c>rdf</c>, <c>rdfs</c> or <c>d</c>, or a regime IRI
    /// in angle brackets (ADR 0192). None is the dataset's default.
    /// </summary>
    public Task<HttpResponseMessage> QueryAsync(string query, string accept, string? asOf, string? entailment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(Dataset, "sparql")) { Content = new StringContent(query, Encoding.UTF8, "application/sparql-query") };
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (asOf is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
        }

        if (entailment is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-Entailment", entailment);
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

    /// <summary>
    /// An update whose client read the graphs named in <paramref name="ifGraphsMatch"/>,
    /// each at its version — a null key is the default graph — sent as
    /// <c>Varve-If-Graphs-Match</c> (ADR 0126): <c>412</c> when one has moved,
    /// <c>409</c> when one moves while it is evaluated, and only then.
    /// </summary>
    public Task<HttpResponseMessage> UpdateAsync(string update, IReadOnlyCollection<KeyValuePair<string?, long>> ifGraphsMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(ifGraphsMatch);
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(Dataset, "sparql")) { Content = new StringContent(update, Encoding.UTF8, "application/sparql-update") };
        StringBuilder header = new();

        foreach ((string? graph, long version) in ifGraphsMatch)
        {
            header.Append(header.Length == 0 ? "" : ", ")
                .Append(graph is null ? "default" : "<" + graph + ">")
                .Append('=')
                .Append(version.ToString(CultureInfo.InvariantCulture));
        }

        request.Headers.TryAddWithoutValidation("Varve-If-Graphs-Match", header.ToString());
        return SendAsync(request, cancellationToken);
    }

    /// <summary>A graph by the Graph Store Protocol: the default graph for a null <paramref name="graph"/>.</summary>
    public Task<HttpResponseMessage> GetGraphAsync(string? graph, string accept, CancellationToken cancellationToken) =>
        GetGraphAsync(graph, accept, null, null, cancellationToken);

    /// <summary>
    /// A graph by the Graph Store Protocol, as of <paramref name="asOf"/> when
    /// given; <paramref name="ifNoneMatch"/> is the version the caller holds,
    /// answered <c>304</c> when the graph has not changed since. The response's
    /// version is <see cref="VersionOf"/> (ADR 0126).
    /// </summary>
    public Task<HttpResponseMessage> GetGraphAsync(string? graph, string accept, long? ifNoneMatch, string? asOf, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, GraphAddress(graph));
        request.Headers.TryAddWithoutValidation("Accept", accept);

        if (ifNoneMatch is long held)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", Tag(held));
        }

        if (asOf is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Replaces a graph with <paramref name="content"/> by the Graph Store
    /// Protocol (<c>PUT</c>), the default graph for a null <paramref name="graph"/>,
    /// under <paramref name="condition"/> (ADR 0126): <c>412</c> when the graph
    /// is not at the version named, or holds a quad for <see cref="GraphCondition.IfAbsent"/>.
    /// </summary>
    public Task<HttpResponseMessage> PutGraphAsync(string? graph, GraphContent content, GraphCondition condition, CancellationToken cancellationToken) =>
        SendGraphAsync(HttpMethod.Put, graph, content, condition, cancellationToken);

    /// <summary>Adds <paramref name="content"/> to a graph by the Graph Store Protocol (<c>POST</c>), under <paramref name="condition"/> (ADR 0126).</summary>
    public Task<HttpResponseMessage> PostGraphAsync(string? graph, GraphContent content, GraphCondition condition, CancellationToken cancellationToken) =>
        SendGraphAsync(HttpMethod.Post, graph, content, condition, cancellationToken);

    /// <summary>Empties a graph by the Graph Store Protocol (<c>DELETE</c>), under <paramref name="condition"/> (ADR 0126).</summary>
    public Task<HttpResponseMessage> DeleteGraphAsync(string? graph, GraphCondition condition, CancellationToken cancellationToken) =>
        SendGraphAsync(HttpMethod.Delete, graph, null, condition, cancellationToken);

    private Task<HttpResponseMessage> SendGraphAsync(HttpMethod method, string? graph, GraphContent? content, GraphCondition condition, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(method, GraphAddress(graph));

        if (content is not null)
        {
            ReadOnlyMemoryContent body = new(content.Bytes);
            body.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(content.MediaType);
            request.Content = body;
        }

        if (condition.Version is long version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", Tag(version));
        }
        else if (condition.Absent)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        }

        return SendAsync(request, cancellationToken);
    }

    private Uri GraphAddress(string? graph) => new(Dataset, graph is null ? "graphs?default" : "graphs?graph=" + Uri.EscapeDataString(graph));

    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";

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

    /// <summary>
    /// The events from <paramref name="from"/> (exclusive) to <paramref name="to"/>
    /// (inclusive; a live tail with null, as server-sent events), as a CloudEvents
    /// batch or as events, of one type when <paramref name="type"/> names one
    /// (ADR 0143).
    /// </summary>
    public Task<HttpResponseMessage> EventsAsync(long from, long? to, string? type, bool eventStream, CancellationToken cancellationToken)
    {
        StringBuilder query = new("events?from=");
        query.Append(from.ToString(CultureInfo.InvariantCulture));

        if (to is long end)
        {
            query.Append("&to=").Append(end.ToString(CultureInfo.InvariantCulture));
        }

        if (type is not null)
        {
            query.Append("&type=").Append(Uri.EscapeDataString(type));
        }

        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, query.ToString()));
        request.Headers.TryAddWithoutValidation("Accept", eventStream ? "text/event-stream" : "application/cloudevents-batch+json");
        return SendAsync(request, cancellationToken);
    }

    /// <summary>The dataset's event types, JSON, as of <paramref name="asOf"/> when given (ADR 0144).</summary>
    public Task<HttpResponseMessage> EventTypesAsync(string? asOf, CancellationToken cancellationToken) =>
        SendAsync(AsOf(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "events/types")), asOf), cancellationToken);

    /// <summary>
    /// A type's schema (ADR 0145): its latest revision, of its current version
    /// or of <paramref name="version"/>, or revision <paramref name="revision"/>
    /// of that version, which never changes.
    /// </summary>
    public Task<HttpResponseMessage> EventSchemaAsync(string type, int? version, long? revision, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        StringBuilder path = new("events/types/");
        path.Append(Uri.EscapeDataString(type));

        if (version is int n)
        {
            path.Append("/versions/").Append(n.ToString(CultureInfo.InvariantCulture));

            if (revision is long r)
            {
                path.Append("/revisions/").Append(r.ToString(CultureInfo.InvariantCulture));
            }
        }
        else if (revision is not null)
        {
            throw new ArgumentException("A revision is of a version.", nameof(revision));
        }

        return SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, path.ToString())), cancellationToken);
    }

    /// <summary>The dataset's AsyncAPI 3.0 document, as of <paramref name="asOf"/> when given (ADR 0144).</summary>
    public Task<HttpResponseMessage> AsyncApiAsync(string? asOf, CancellationToken cancellationToken) =>
        SendAsync(AsOf(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "events/asyncapi")), asOf), cancellationToken);

    /// <summary>
    /// The server-wide list of event types (ADR 0144): every dataset's types the
    /// caller reads, each with its dataset, at <c>/events/types</c> on the
    /// server the dataset is on.
    /// </summary>
    public Task<HttpResponseMessage> ServerEventTypesAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(Dataset, "../../events/types")), cancellationToken);

    private static HttpRequestMessage AsOf(HttpRequestMessage request, string? asOf)
    {
        if (asOf is not null)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", asOf);
        }

        return request;
    }

    /// <summary>One commit, as one record of the line format (ADR 0118).</summary>
    public Task<HttpResponseMessage> CommitAsync(long position, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, "commits/" + position.ToString(CultureInfo.InvariantCulture)));
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.varve.delta");
        return SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// The dataset's resource on the management plane (ADR 0163):
    /// <c>/manage/datasets/{name}/{path}</c> on the dataset's server. A
    /// management call needs a token for the management plane's audience,
    /// which the data plane refuses, and the other way round.
    /// </summary>
    public Uri Management(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string name = Dataset.Segments[^1].TrimEnd('/');
        return new Uri(Dataset, "../../manage/datasets/" + name + (path.Length == 0 ? string.Empty : "/" + path));
    }

    /// <summary>This instance's state and role in the dataset, JSON (ADR 0163); its admin's call.</summary>
    public Task<HttpResponseMessage> StateAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Management("state")), cancellationToken);

    /// <summary>Declares the dataset <paramref name="open"/> or closed, idempotently; every instance converges to it (ADR 0163).</summary>
    public Task<HttpResponseMessage> SetStateAsync(bool open, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Put, Management("state"))
        {
            Content = new StringContent(open ? "{\"state\":\"open\"}" : "{\"state\":\"closed\"}", Encoding.UTF8, "application/json"),
        }, cancellationToken);

    /// <summary>The declared settings, JSON, with the declaration graph's version as <c>ETag</c> (ADR 0163).</summary>
    public Task<HttpResponseMessage> SettingsAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Management("settings")), cancellationToken);

    /// <summary>Declares the settings <paramref name="trig"/> states of the dataset's node, replacing the declared ones, under <paramref name="ifMatch"/> (ADR 0163).</summary>
    public Task<HttpResponseMessage> PutSettingsAsync(string trig, long? ifMatch, CancellationToken cancellationToken) =>
        SettingsWriteAsync(HttpMethod.Put, trig, ifMatch, cancellationToken);

    /// <summary>Declares the settings <paramref name="trig"/> states, keeping the others, under <paramref name="ifMatch"/> (ADR 0163).</summary>
    public Task<HttpResponseMessage> PatchSettingsAsync(string trig, long? ifMatch, CancellationToken cancellationToken) =>
        SettingsWriteAsync(HttpMethod.Patch, trig, ifMatch, cancellationToken);

    private Task<HttpResponseMessage> SettingsWriteAsync(HttpMethod method, string trig, long? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trig);
        HttpRequestMessage request = new(method, Management("settings")) { Content = new StringContent(trig, Encoding.UTF8, "application/trig") };

        if (ifMatch is long expected)
        {
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + expected.ToString(CultureInfo.InvariantCulture) + "\"");
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>The dataset's status (ADRs 0101, 0163), JSON; its admin's call.</summary>
    public Task<HttpResponseMessage> StatusAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, Management("status")), cancellationToken);

    /// <summary>A checkpoint at the head, or at <paramref name="at"/>, taken by the dataset's writer (ADRs 0106, 0168).</summary>
    public Task<HttpResponseMessage> CheckpointAsync(long? at, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, Management(at is long position ? "checkpoints?at=" + position.ToString(CultureInfo.InvariantCulture) : "checkpoints")), cancellationToken);

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

    /// <summary>
    /// The version a response's strong <c>ETag</c> names, or null: for a Graph
    /// Store response the graph's version, the position of the last commit
    /// that touched it; for any other, the position it describes (ADR 0126).
    /// </summary>
    public static long? VersionOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.ETag is { IsWeak: false } tag
            && long.TryParse(tag.Tag.AsSpan().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out long version)
            ? version
            : null;
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
