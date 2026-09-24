// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RepoStandard.GitHub;

/// <summary>A response: the status and the parsed body, if any.</summary>
internal sealed record ApiResponse(int Status, JsonNode? Body, string? NextPage);

/// <summary>Waits. Injected so tests of the retry policy do not sleep.</summary>
internal interface IDelay
{
    Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>The real wait.</summary>
internal sealed class RealDelay : IDelay
{
    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}

/// <summary>Where the API is and how to authenticate to it.</summary>
/// <param name="ApiBase">The REST root, <c>https://api.github.com</c> unless on GitHub Enterprise Server.</param>
/// <param name="GraphQlUrl">The GraphQL endpoint.</param>
/// <param name="Token">The token. Placed on the Authorization header and nowhere else.</param>
/// <param name="UserAgent">The User-Agent, which GitHub requires.</param>
internal sealed record GitHubOptions(Uri ApiBase, Uri GraphQlUrl, string Token, string UserAgent)
{
    /// <inheritdoc />
    /// <remarks>Overridden so that no formatting of the options can print the token.</remarks>
    public override string ToString() => $"GitHubOptions {{ ApiBase = {ApiBase}, GraphQlUrl = {GraphQlUrl} }}";
}

/// <summary>
/// GitHub's REST and GraphQL APIs over <see cref="HttpClient"/>, with
/// GitHub's guidance on rate limits built in.
/// </summary>
/// <remarks>
/// <para>
/// The token is attached to each request's Authorization header and is never
/// part of a message, an exception, a log line or a file. Errors carry the
/// method, the path, the status and GitHub's own message, and nothing else from
/// the request.
/// </para>
/// <para>
/// Rate limits, per GitHub's "Best practices for using the REST API": writes
/// are sent one at a time with at least one second between them. A primary or
/// secondary limit (a 403 or 429 that says so) is retried: after
/// <c>retry-after</c> seconds when the header is present; otherwise, when
/// <c>x-ratelimit-remaining</c> is 0, after <c>x-ratelimit-reset</c>;
/// otherwise after at least a minute, doubling on each further attempt. After
/// <see cref="MaxRetries"/> retries the request fails.
/// </para>
/// </remarks>
internal sealed class GitHubClient
{
    /// <summary>The REST API version this tool is written against.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>How many times a rate-limited request is retried.</summary>
    public const int MaxRetries = 5;

    private static readonly TimeSpan WriteSpacing = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SecondaryBackoff = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly GitHubOptions _options;
    private readonly IDelay _delay;
    private readonly TimeProvider _time;
    private readonly TextWriter _log;
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;

    public GitHubClient(HttpClient http, GitHubOptions options, IDelay delay, TimeProvider time, TextWriter log)
    {
        _http = http;
        _options = options;
        _delay = delay;
        _time = time;
        _log = log;
    }

    /// <summary>GETs a resource; any status other than 2xx is an error.</summary>
    public async Task<JsonNode?> GetAsync(Endpoint endpoint, string[] values, CancellationToken cancellationToken)
    {
        ApiResponse response = await SendAsync(endpoint, values, null, cancellationToken).ConfigureAwait(false);
        return response.Body;
    }

    /// <summary>GETs a resource and returns the status, for endpoints whose status is the answer.</summary>
    public Task<ApiResponse> GetStatusAsync(Endpoint endpoint, string[] values, CancellationToken cancellationToken) =>
        SendAsync(endpoint, values, null, static status => status is 404, cancellationToken);

    /// <summary>GETs every page of a list, following the Link header.</summary>
    /// <param name="endpoint">The list endpoint.</param>
    /// <param name="values">Its path values.</param>
    /// <param name="select">The array in a page's body: the body itself, or a member of it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<List<JsonNode>> GetAllAsync(
        Endpoint endpoint, string[] values, Func<JsonNode?, JsonArray?> select, CancellationToken cancellationToken)
    {
        List<JsonNode> all = [];
        ApiResponse response = await SendAsync(endpoint, values, null, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            if (select(response.Body) is JsonArray page)
            {
                all.AddRange(page.Where(static item => item is not null).Select(static item => item!.DeepClone()));
            }

            if (response.NextPage is null)
            {
                return all;
            }

            // The token goes with every request, so a page is followed only on
            // the API's own scheme, host and port, whatever the Link header says.
            if (!Uri.TryCreate(response.NextPage, UriKind.Absolute, out Uri? next)
                || Uri.Compare(next, _options.ApiBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new GitHubException(endpoint, $"GET {endpoint.Template}", 0,
                    $"the next page is at {next?.GetLeftPart(UriPartial.Authority) ?? "an unreadable URL"}, not the API's host; not followed");
            }

            response = await SendRawAsync(endpoint, HttpMethod.Get, response.NextPage, null, null, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Sends a write. Any status other than 2xx is an error.</summary>
    public async Task<JsonNode?> WriteAsync(Endpoint endpoint, string[] values, JsonNode? body, CancellationToken cancellationToken)
    {
        ApiResponse response = await SendAsync(endpoint, values, body, cancellationToken).ConfigureAwait(false);
        return response.Body;
    }

    /// <summary>Runs a GraphQL operation and returns its <c>data</c>.</summary>
    public async Task<JsonNode> GraphQlAsync(GraphQlOperation operation, JsonObject variables, CancellationToken cancellationToken)
    {
        string request = JsonSerializer.Serialize(
            new GraphQlRequest(operation.Text, variables), GitHubJsonContext.Default.GraphQlRequest);

        ApiResponse response = await SendRawAsync(
            Endpoints.GraphQl, HttpMethod.Post, _options.GraphQlUrl.ToString(), request, null, operation.IsMutation,
            cancellationToken).ConfigureAwait(false);

        if (response.Body?["errors"] is JsonArray errors && errors.Count > 0)
        {
            string messages = string.Join("; ", errors.Select(static error => (string?)error?["message"] ?? "unknown error"));
            throw new GitHubException(Endpoints.GraphQl, $"GraphQL {operation.Name}", response.Status, messages);
        }

        return response.Body?["data"] ?? throw new GitHubException(
            Endpoints.GraphQl, $"GraphQL {operation.Name}", response.Status, "the response carried no data");
    }

    private Task<ApiResponse> SendAsync(
        Endpoint endpoint, string[] values, JsonNode? body, CancellationToken cancellationToken) =>
        SendAsync(endpoint, values, body, null, cancellationToken);

    private Task<ApiResponse> SendAsync(
        Endpoint endpoint, string[] values, JsonNode? body, Func<int, bool>? acceptStatus, CancellationToken cancellationToken)
    {
        string url = _options.ApiBase.ToString().TrimEnd('/') + endpoint.Path(values);
        return SendRawAsync(endpoint, endpoint.HttpMethod, url, body?.ToJsonString(), acceptStatus, null, cancellationToken);
    }

    private async Task<ApiResponse> SendRawAsync(
        Endpoint endpoint, HttpMethod method, string url, string? body,
        Func<int, bool>? acceptStatus, bool? isWrite, CancellationToken cancellationToken)
    {
        bool write = isWrite ?? method != HttpMethod.Get;
        string display = $"{method} {PathOf(url)}";

        for (int attempt = 0; ; attempt++)
        {
            if (write)
            {
                TimeSpan since = _time.GetUtcNow() - _lastWrite;
                if (since < WriteSpacing)
                {
                    await _delay.WaitAsync(WriteSpacing - since, cancellationToken).ConfigureAwait(false);
                }

                _lastWrite = _time.GetUtcNow();
            }

            using HttpRequestMessage request = new(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                // The inner exception's message names the host at most; the
                // headers are never part of it.
                throw new GitHubException(endpoint, display, 0, exception.Message);
            }

            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                JsonNode? parsed = ParseBody(text);
                int status = (int)response.StatusCode;

                if (IsRateLimited(response, parsed))
                {
                    if (attempt >= MaxRetries)
                    {
                        throw new GitHubException(endpoint, display, status,
                            $"rate limited, and still after {MaxRetries} retries");
                    }

                    TimeSpan wait = RetryAfter(response, attempt);
                    await _log.WriteLineAsync(
                        $"rate limited on {display}; waiting {wait.TotalSeconds:F0}s (retry {attempt + 1} of {MaxRetries})")
                        .ConfigureAwait(false);
                    await _delay.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode && acceptStatus?.Invoke(status) != true)
                {
                    string message = (string?)parsed?["message"] ?? response.ReasonPhrase ?? "request failed";
                    if (parsed?["errors"] is JsonArray details && details.Count > 0)
                    {
                        message += " (" + string.Join("; ", details.Select(Detail)) + ")";
                    }

                    throw new GitHubException(endpoint, display, status, message);
                }

                return new ApiResponse(status, parsed, NextLink(response));
            }
        }
    }

    /// <summary>One entry of a 422's errors: "Label name already_exists", or its message.</summary>
    private static string Detail(JsonNode? error) =>
        error is JsonObject obj
            ? (string?)obj["message"]
                ?? string.Join(' ', new[] { (string?)obj["resource"], (string?)obj["field"], (string?)obj["code"] }.Where(static s => s is not null))
            : error?.ToString() ?? string.Empty;

    private static JsonNode? ParseBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response, JsonNode? body)
    {
        int status = (int)response.StatusCode;
        if (status is not (403 or 429))
        {
            return false;
        }

        if (status is 429 || response.Headers.Contains("retry-after") || Header(response, "x-ratelimit-remaining") == "0")
        {
            return true;
        }

        string? message = (string?)body?["message"];
        return message is not null && message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan RetryAfter(HttpResponseMessage response, int attempt)
    {
        if (int.TryParse(Header(response, "retry-after"), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            return TimeSpan.FromSeconds(Math.Max(seconds, 1));
        }

        if (Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long reset))
        {
            TimeSpan until = DateTimeOffset.FromUnixTimeSeconds(reset) - _time.GetUtcNow();
            return until > TimeSpan.FromSeconds(1) ? until : TimeSpan.FromSeconds(1);
        }

        return SecondaryBackoff * Math.Pow(2, attempt);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    private static string? NextLink(HttpResponseMessage response)
    {
        string? link = Header(response, "link");
        if (link is null)
        {
            return null;
        }

        foreach (string part in link.Split(','))
        {
            string[] pieces = part.Split(';');
            if (pieces.Length >= 2 && pieces.Skip(1).Any(static p => p.Trim() == "rel=\"next\""))
            {
                return pieces[0].Trim().TrimStart('<').TrimEnd('>');
            }
        }

        return null;
    }

    private static string PathOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.PathAndQuery : url;
}
