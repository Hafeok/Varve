// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

/// <summary>
/// A server's health probes, <c>/health/live</c> and <c>/health/ready</c>
/// (ADR 0113), which answer without a token on either listener.
/// </summary>
/// <remarks>
/// Every answer is a <see cref="VarveResult{T}"/>: a <c>503 not-ready</c> is a
/// readiness with its problem beside it, and any other problem is the problem.
/// </remarks>
public sealed class VarveHealthClient
{
    private readonly HttpClient _http;
    private readonly Uri _server;
    private readonly ClientLimits _limits;

    /// <summary>A client of the server at <paramref name="server"/>, an absolute http or https address.</summary>
    /// <exception cref="ArgumentException">The address is not absolute http or https.</exception>
    public VarveHealthClient(HttpClient http, Uri server, ClientLimits limits)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(server);

        if (!server.IsAbsoluteUri || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A server's address is an absolute http or https URL.", nameof(server));
        }

        _http = http;
        _server = server.AbsoluteUri.EndsWith('/') ? server : new Uri(server.AbsoluteUri + "/");
        _limits = limits;
    }

    /// <summary>Whether the process is live: true on <c>200</c>.</summary>
    public async Task<bool> LiveAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync("health/live", cancellationToken).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.OK;
    }

    /// <summary>The instance's readiness, ready or not, or the problem when the answer is neither.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    public async Task<VarveResult<Readiness>> ReadyAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync("health/ready", cancellationToken).ConfigureAwait(false);
        ResponseFacts facts = ResponseFacts.Of(response);
        (byte[]? body, string? failure) = await ResponseBodies.ReadAsync(response.Content, _limits.MaxResponseBytes, cancellationToken).ConfigureAwait(false);

        if (body is null)
        {
            throw new HttpRequestException(failure);
        }

        if (response.StatusCode == HttpStatusCode.OK && Parse(body) is JsonDocument document)
        {
            using (document)
            {
                return document.RootElement.ValueKind == JsonValueKind.Object
                    ? new VarveResult<Readiness>(Readiness.Ready(document.RootElement), null, facts)
                    : new VarveResult<Readiness>(null, null, facts);
            }
        }

        Problem? problem = Problem.Parse(body, facts.Status);
        Readiness? readiness = problem is not null && problem.Type == ProblemType.NotReady ? Readiness.NotReady(problem) : null;
        return new VarveResult<Readiness>(readiness, problem, facts);
    }

    private Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(_server, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));
        return Requests.SendAsync(_http, request, _limits, cancellationToken);
    }

    private static JsonDocument? Parse(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
