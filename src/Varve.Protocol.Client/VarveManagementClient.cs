// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// The management plane's reads under <c>/manage/</c> (ADRs 0163, 0203): the
/// dataset list, a dataset's lifecycle, status and declaration, the system
/// plane, and the fleet's graphs, each with the management plane's own token.
/// </summary>
/// <remarks>
/// Every answer is a <see cref="VarveResult{T}"/>: the value on success, the
/// problem otherwise, and the response's facts either way. An answer from the
/// server is never an exception; no answer within the limits is.
/// </remarks>
public sealed class VarveManagementClient
{
    private readonly HttpClient _http;
    private readonly Uri _server;
    private readonly ClientLimits _limits;
    private readonly BearerTokenProvider? _tokens;
    private readonly TimeProvider? _clock;
    private readonly FeedPosition? _atLeast;

    /// <summary>
    /// A client of the server at <paramref name="server"/>. With
    /// <paramref name="tokens"/>, each request carries the management plane's
    /// token, renewed on <paramref name="clock"/> as the data plane's is
    /// (ADR 0127); without, requests are anonymous.
    /// </summary>
    /// <exception cref="ArgumentException">The address is not absolute http or https, or a provider is given without a clock.</exception>
    public VarveManagementClient(HttpClient http, Uri server, ClientLimits limits, BearerTokenProvider? tokens, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(server);

        if (!server.IsAbsoluteUri || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A server's address is an absolute http or https URL.", nameof(server));
        }

        if (tokens is not null && clock is null)
        {
            throw new ArgumentException("A token provider needs the clock its tokens' expiry is measured on.", nameof(clock));
        }

        _http = http;
        _server = server.AbsoluteUri.EndsWith('/') ? server : new Uri(server.AbsoluteUri + "/");
        _limits = limits;
        _tokens = tokens;
        _clock = clock;
    }

    private VarveManagementClient(VarveManagementClient client, FeedPosition atLeast)
    {
        _http = client._http;
        _server = client._server;
        _limits = client._limits;
        _tokens = client._tokens;
        _clock = client._clock;
        _atLeast = atLeast;
    }

    /// <summary>
    /// The same client, whose every request asks to be answered at management
    /// position <paramref name="managementPosition"/> or later
    /// (<c>Varve-Min-Management-Position</c>, ADR 0163 point 3): after a
    /// management write, a re-read sees it, or is a <c>management-behind</c>
    /// problem, never a stale answer.
    /// </summary>
    public VarveManagementClient AtLeast(FeedPosition managementPosition) => new(this, managementPosition);

    /// <summary>
    /// Takes a checkpoint of <paramref name="dataset"/> at its head (ADR 0106):
    /// derived data, no change to the log. On success the value is set and the
    /// facts name the position.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<Acknowledgement>> CheckpointAsync(DatasetName dataset, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, DatasetPath(dataset) + "/checkpoints", null, cancellationToken);

    /// <summary>
    /// Opens or closes <paramref name="dataset"/> (ADR 0165): a write to its
    /// declaration graph, which drifts from the declaration files until they
    /// say the same. On success the facts name the management position the
    /// change was committed at. With <paramref name="ifMatch"/>, the request
    /// carries <c>If-Match</c> with that version of the declaration graph, as
    /// its ETag names it, so a newer declaration is never overwritten blind: a
    /// server that honours it answers <c>412 precondition-failed</c> instead.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="state"/> is neither open nor closed.</exception>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<Acknowledgement>> SetStateAsync(DatasetName dataset, DatasetState state, FeedPosition? ifMatch, CancellationToken cancellationToken)
    {
        if (state != DatasetState.Open && state != DatasetState.Closed)
        {
            throw new ArgumentException("A dataset is opened or closed.", nameof(state));
        }

        return SendAsync(HttpMethod.Put, DatasetPath(dataset) + "/state", "{\"state\":\"" + state.Value + "\"}", cancellationToken, ifMatch);
    }

    /// <summary>Every dataset the management plane knows, with this instance's role for each.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<DatasetList>> DatasetsAsync(CancellationToken cancellationToken) =>
        JsonAsync("manage/datasets", DatasetList.Of, cancellationToken);

    /// <summary>A dataset's state and this instance's role for it.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<DatasetLifecycle>> LifecycleAsync(DatasetName dataset, CancellationToken cancellationToken) =>
        JsonAsync(DatasetPath(dataset) + "/state", DatasetLifecycle.Of, cancellationToken);

    /// <summary>A dataset's status: head, durability, projection, settings and checkpoints.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<DatasetStatus>> StatusAsync(DatasetName dataset, CancellationToken cancellationToken) =>
        JsonAsync(DatasetPath(dataset) + "/status", DatasetStatus.Of, cancellationToken);

    /// <summary>The management plane on this instance.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<SystemPlane>> SystemAsync(CancellationToken cancellationToken) =>
        JsonAsync("manage/system", SystemPlane.Of, cancellationToken);

    /// <summary>A dataset's declaration graph, as TriG.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<Declaration>> DeclarationAsync(DatasetName dataset, CancellationToken cancellationToken) =>
        TrigAsync(DatasetPath(dataset), cancellationToken);

    /// <summary>The fleet's graph, as TriG.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<Declaration>> FleetAsync(CancellationToken cancellationToken) =>
        TrigAsync("manage/fleet", cancellationToken);

    /// <summary>Every management graph, as TriG.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<Declaration>> GraphsAsync(CancellationToken cancellationToken) =>
        TrigAsync("manage/graphs", cancellationToken);

    /// <summary>
    /// A dataset's grants, at the management position <paramref name="asOf"/>
    /// when one is given (<c>Varve-As-Of</c> on the management log), else at
    /// the position applied.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<GrantList>> GrantsAsync(DatasetName dataset, FeedPosition? asOf, CancellationToken cancellationToken) =>
        JsonAsync(DatasetPath(dataset) + "/grants", GrantList.Of, cancellationToken, asOf: asOf);

    /// <summary>
    /// Where each dataset is placed, from the declarations, through the
    /// management plane's SPARQL endpoint (ADR 0166).
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<PlacementList>> PlacementsAsync(CancellationToken cancellationToken) =>
        JsonAsync("manage/sparql?query=" + Uri.EscapeDataString(PlacementQuery), PlacementList.Of, cancellationToken, "application/sparql-results+json");

    /// <summary>
    /// A dataset's shapes bindings (ADR 0170): each binding and the shapes
    /// graph it binds, from the dataset's declaration graph, through the
    /// management plane's SPARQL endpoint. The name is escaped into the graph's
    /// IRI, so a name from an address can only name a graph, never add to the
    /// query.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public Task<VarveResult<ShapesBindingList>> ShapesBindingsAsync(DatasetName dataset, CancellationToken cancellationToken) =>
        JsonAsync("manage/sparql?query=" + Uri.EscapeDataString(
            "PREFIX varve: <https://w3id.org/varve/ns#> SELECT ?binding ?graph WHERE { GRAPH <https://w3id.org/varve/ns#manage/dataset/" + Uri.EscapeDataString(dataset.Value)
            + "> { ?binding a varve:ShapesBinding ; varve:boundGraph ?graph } }"), ShapesBindingList.Of, cancellationToken, "application/sparql-results+json");

    private const string PlacementQuery =
        "PREFIX varve: <https://w3id.org/varve/ns#> SELECT ?name ?instance WHERE { GRAPH ?g { ?d varve:datasetName ?name ; varve:placedOn ?instance } }";

    private static string DatasetPath(DatasetName dataset) => "manage/datasets/" + Uri.EscapeDataString(dataset.Value);

    private async Task<VarveResult<T>> JsonAsync<T>(string path, Func<JsonElement, T> read, CancellationToken cancellationToken, string accept = "application/json", FeedPosition? asOf = null)
        where T : class
    {
        using HttpResponseMessage response = await GetAsync(path, accept, asOf, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return new VarveResult<T>(read(document.RootElement), null, facts);
                }
            }
            catch (JsonException)
            {
                // Not JSON: neither the value nor a problem.
            }

            return new VarveResult<T>(null, null, facts);
        }

        Problem? problem = Problem.Parse(body, facts.Status);
        return new VarveResult<T>(null, problem, facts.With(problem));
    }

    private async Task<VarveResult<Declaration>> TrigAsync(string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(path, "application/trig", null, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            FeedPosition? version = response.Headers.ETag is { IsWeak: false } tag
                && long.TryParse(tag.Tag.AsSpan().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out long at)
                ? new FeedPosition(at)
                : null;
            return new VarveResult<Declaration>(new Declaration(body, version), null, facts);
        }

        Problem? problem = Problem.Parse(body, facts.Status);
        return new VarveResult<Declaration>(null, problem, facts.With(problem));
    }

    private async Task<VarveResult<Acknowledgement>> SendAsync(HttpMethod method, string path, string? json, CancellationToken cancellationToken, FeedPosition? ifMatch = null)
    {
        BearerToken? token = await Tokens.ForRequestAsync(_tokens, _clock, cancellationToken).ConfigureAwait(false);
        HttpRequestMessage request = new(method, new Uri(_server, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        if (ifMatch is FeedPosition version)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"" + version.Value.ToString(CultureInfo.InvariantCulture) + "\""));
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        if (token is BearerToken bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Value);
        }

        using HttpResponseMessage response = await Requests.SendAsync(_http, request, _limits, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return new VarveResult<Acknowledgement>(new Acknowledgement(), null, facts);
        }

        Problem? problem = Problem.Parse(body, facts.Status);
        return new VarveResult<Acknowledgement>(null, problem, facts.With(problem));
    }

    private async Task<(ResponseFacts Facts, byte[] Body)> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ResponseFacts facts = ResponseFacts.Of(response);
        (byte[]? body, string? failure) = await ResponseBodies.ReadAsync(response.Content, _limits.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        return body is null ? throw new HttpRequestException(failure) : (facts, body);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string accept, FeedPosition? asOf, CancellationToken cancellationToken)
    {
        BearerToken? token = await Tokens.ForRequestAsync(_tokens, _clock, cancellationToken).ConfigureAwait(false);
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(_server, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        if (_atLeast is FeedPosition least)
        {
            request.Headers.TryAddWithoutValidation("Varve-Min-Management-Position", least.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (asOf is FeedPosition at)
        {
            request.Headers.TryAddWithoutValidation("Varve-As-Of", "position:" + at.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (token is BearerToken bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Value);
        }

        return await Requests.SendAsync(_http, request, _limits, cancellationToken).ConfigureAwait(false);
    }
}
