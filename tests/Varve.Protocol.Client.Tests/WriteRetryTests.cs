// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// Which refusals the client sends a keyed write again for (ADR 0246 point
/// 10): only those the server states as retryable, by problem type, and for
/// <c>unavailable</c> only its routing reasons. Every other answer is final,
/// every other 503 included, whatever its <c>Retry-After</c>; a memory limit
/// is never retried. The server is played by a scripted handler, the HTTP
/// boundary: the first attempt is refused, a second would be answered 204.
/// </summary>
public sealed class WriteRetryTests
{
    public static TheoryData<int, string?, string?, bool> Refusals => new()
    {
        // Retried: nothing was committed, or the key settles it.
        { 503, "writer-moved", null, true },
        { 503, "writer-stepped-down", "shutdown", true },
        { 503, "writer-superseded", null, true },
        { 503, "shutting-down", null, true },
        { 503, "management-behind", null, true },
        { 503, "commit-outcome-unknown", null, true },
        { 503, "unavailable", "no-writer", true },
        { 503, "unavailable", "writer-unreachable", true },
        { 409, "idempotency-key-in-flight", null, true },

        // Final: every other 503, with Retry-After or without.
        { 503, "unavailable", null, false },
        { 503, "unavailable", "failed", false },
        { 503, "unavailable", "handover-off", false },
        { 503, "read-limit-exceeded", null, false },
        { 503, "load-spill-damaged", null, false },
        { 503, "not-ready", null, false },
        { 503, null, null, false },

        // Final: a memory limit, and every other refusal.
        { 422, "memory-limit-exceeded", null, false },
        { 507, "storage-full", null, false },
        { 409, "conflict", null, false },
        { 422, "idempotency-key-expired", null, false },
        { 422, "idempotency-key-reused", null, false },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_keyed_write_is_sent_again_only_for_a_refusal_the_server_states_as_retryable(int status, string? type, string? reason, bool retried)
    {
        Scripted server = new(status, type, reason);
        using HttpClient http = new(server);
        SparqlHttpClient client = new(http, new Uri("http://varve.test/datasets/d/"), ClientLimits.Default with { IdempotencyKeys = () => "k1" });

        using HttpResponseMessage response = await client.UpdateAsync("INSERT DATA { <http://ex/a> <http://ex/p> 1 }", (long?)null, C.Ct);

        Assert.Equal(retried ? 2 : 1, server.Attempts.Count);
        Assert.Equal(retried ? HttpStatusCode.NoContent : (HttpStatusCode)status, response.StatusCode);

        // One key for every attempt; only a retry says when the key was made.
        Assert.All(server.Attempts, attempt => Assert.Equal("\"k1\"", attempt.Key));
        Assert.Null(server.Attempts[0].KeyPosition);
        Assert.All(server.Attempts.Skip(1), attempt => Assert.Equal("0", attempt.KeyPosition));
    }

    [Fact]
    public void Only_the_named_refusals_are_retryable()
    {
        string[] retryable = ["writer-moved", "writer-stepped-down", "writer-superseded", "shutting-down", "management-behind", "commit-outcome-unknown"];

        foreach (string name in retryable)
        {
            Assert.True(Requests.IsRetryable(name, null), name);
        }

        Assert.True(Requests.IsRetryable("unavailable", "no-writer"));
        Assert.True(Requests.IsRetryable("unavailable", "writer-unreachable"));

        foreach (string name in (string[])["unavailable", "memory-limit-exceeded", "read-limit-exceeded", "storage-full", "server-busy", "dataset-behind", "reader-forgotten", "read-unannounced-extent-gone", "load-spill-damaged", "not-ready"])
        {
            Assert.False(Requests.IsRetryable(name, null), name);
        }
    }

    private sealed record Attempt(string? Key, string? KeyPosition);

    /// <summary>The server: the first attempt is refused as scripted, any later one answered 204.</summary>
    private sealed class Scripted(int status, string? type, string? reason) : HttpMessageHandler
    {
        internal List<Attempt> Attempts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts.Add(new Attempt(Header(request, "Idempotency-Key"), Header(request, "Varve-Key-Position")));

            if (Attempts.Count > 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            HttpResponseMessage refused = new((HttpStatusCode)status);

            // A Retry-After on every refusal, as the server's unavailable carries one: it never makes an answer retryable.
            refused.Headers.TryAddWithoutValidation("Retry-After", "0");

            string body = type is null
                ? "<html>Service Unavailable</html>"
                : "{\"type\":\"https://w3id.org/varve/problems/" + type + "\",\"title\":\"t\",\"status\":" + status + (reason is null ? string.Empty : ",\"reason\":\"" + reason + "\"") + "}";
            refused.Content = new StringContent(body, Encoding.UTF8, type is null ? "text/html" : "application/problem+json");
            return Task.FromResult(refused);
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;
    }
}
