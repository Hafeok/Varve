// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
    /// <summary>The request header a write's key travels in (ADR 0246).</summary>
    internal const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>The request header a retry names the head it knew when it made its key in (ADR 0246).</summary>
    internal const string KeyPositionHeader = "Varve-Key-Position";

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

    /// <summary>
    /// Sends a write (ADR 0246): with a fresh <c>Idempotency-Key</c> when the
    /// limits name a source of keys, and, keyed, again with the same key after a
    /// lost response, a <c>503</c> or <c>409 idempotency-key-in-flight</c>,
    /// up to <see cref="ClientLimits.WriteRetries"/> times. The writer answers
    /// a retry whose original committed from that commit, so the write
    /// commits once. <paramref name="authorize"/> sets each attempt's token.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendWriteAsync(
        HttpClient http, HttpRequestMessage request, ClientLimits limits, Func<HttpRequestMessage, CancellationToken, ValueTask> authorize, KnownPosition known, CancellationToken cancellationToken, bool keyed = true)
    {
        if (!keyed || limits.IdempotencyKeys is not { } keys || request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
        {
            await PrepareAsync(request, authorize, cancellationToken).ConfigureAwait(false);
            HttpResponseMessage once = await SendAsync(http, request, limits, cancellationToken).ConfigureAwait(false);
            known.Observe(once);
            return once;
        }

        // One key for every attempt, as a Structured Field string, and the
        // head this client knew when it made it: 0 when it knew none, which
        // only ever refuses more.
        string key = "\"" + keys().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        string made = known.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Attempt template;

        using (request)
        {
            template = await Attempt.OfAsync(request, cancellationToken).ConfigureAwait(false);
        }

        for (int attempt = 0; ; attempt++)
        {
            // A retry says when its key was made, so a retry past the window
            // is refused rather than committed twice; a first attempt is not
            // a retry, and an idle client's is never refused for it.
            HttpRequestMessage sent = template.Build(key, attempt == 0 ? null : made);
            bool last = attempt >= limits.WriteRetries;
            HttpResponseMessage response;

            try
            {
                await PrepareAsync(sent, authorize, cancellationToken).ConfigureAwait(false);
                response = await SendAsync(http, sent, limits, cancellationToken).ConfigureAwait(false);
                known.Observe(response);
            }
            catch (HttpRequestException) when (!last && !cancellationToken.IsCancellationRequested)
            {
                // The response was lost, or never came: the key finds the
                // commit if there was one.
                await Task.Delay(Backoff(attempt, null), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (last || !await RetriedAsync(response, cancellationToken).ConfigureAwait(false))
            {
                return response;
            }

            TimeSpan wait = Backoff(attempt, response.Headers.RetryAfter?.Delta);
            response.Dispose();
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask PrepareAsync(HttpRequestMessage request, Func<HttpRequestMessage, CancellationToken, ValueTask> authorize, CancellationToken cancellationToken)
    {
        try
        {
            await authorize(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    // A 503 left nothing committed or settles by the key; a 409 whose key is
    // in flight is answered once the first request has been.
    private static async ValueTask<bool> RetriedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.Conflict)
        {
            return false;
        }

        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using JsonDocument problem = JsonDocument.Parse(body);
            return problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString()!.EndsWith("/idempotency-key-in-flight", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The server's Retry-After, at most five seconds; otherwise 100 ms doubled each time.
    private static TimeSpan Backoff(int attempt, TimeSpan? retryAfter) =>
        retryAfter is { } after && after > TimeSpan.Zero
            ? (after > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : after)
            : TimeSpan.FromMilliseconds(100 * (1 << Math.Min(attempt, 6)));

    // One write, held so that it can be sent again: its method, address,
    // headers and body bytes with their content headers.
    private sealed class Attempt
    {
        private readonly HttpMethod _method;
        private readonly Uri? _uri;
        private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
        private readonly byte[]? _body;
        private readonly List<KeyValuePair<string, IEnumerable<string>>> _contentHeaders;

        private Attempt(HttpMethod method, Uri? uri, List<KeyValuePair<string, IEnumerable<string>>> headers, byte[]? body, List<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
        {
            _method = method;
            _uri = uri;
            _headers = headers;
            _body = body;
            _contentHeaders = contentHeaders;
        }

        internal static async Task<Attempt> OfAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[]? body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new Attempt(request.Method, request.RequestUri, [.. request.Headers], body, request.Content is null ? [] : [.. request.Content.Headers]);
        }

        internal HttpRequestMessage Build(string key, string? made)
        {
            HttpRequestMessage request = new(_method, _uri);

            foreach (KeyValuePair<string, IEnumerable<string>> header in _headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Remove(IdempotencyKeyHeader);
            request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, key);
            request.Headers.Remove(KeyPositionHeader);

            if (made is not null)
            {
                request.Headers.TryAddWithoutValidation(KeyPositionHeader, made);
            }

            if (_body is not null)
            {
                ByteArrayContent content = new(_body);

                foreach (KeyValuePair<string, IEnumerable<string>> header in _contentHeaders)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                request.Content = content;
            }

            return request;
        }
    }
}

/// <summary>
/// The newest position a client's responses named in <c>Varve-Position</c>:
/// what a write's key says it was made after (ADR 0246). It only rises.
/// </summary>
internal sealed class KnownPosition
{
    private long _value;

    /// <summary>The newest position seen, or 0.</summary>
    internal long Value => Volatile.Read(ref _value);

    internal void Observe(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Varve-Position", out IEnumerable<string>? values))
        {
            return;
        }

        foreach (string value in values)
        {
            if (long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long position))
            {
                long seen = Volatile.Read(ref _value);

                while (position > seen && Interlocked.CompareExchange(ref _value, position, seen) is long found && found != seen)
                {
                    seen = found;
                }
            }
        }
    }
}
