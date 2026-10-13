// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// The token provider (ADR 0127): asked for every request, so one client
/// serves many users; a token near its expiry asked for once more; one still
/// stale refused before a byte is sent; none sending no header.
/// </summary>
public class TokenProviderTests
{
    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    [Fact]
    public async Task The_provider_is_asked_for_every_request_so_one_client_carries_each_users_token()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        ManualClock clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        AsyncLocal<string> user = new();
        int asked = 0;
        SparqlHttpClient client = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default, _ =>
        {
            Interlocked.Increment(ref asked);
            return ValueTask.FromResult<BearerToken?>(new BearerToken("token-of-" + user.Value, clock.GetUtcNow().AddHours(1)));
        }, clock);

        List<string> seen = [];

        foreach (string name in (string[])["alice", "bob", "alice"])
        {
            user.Value = name;
            seen.Add(await AuthorizationAsync(client));
        }

        Assert.Equal(["Bearer token-of-alice", "Bearer token-of-bob", "Bearer token-of-alice"], seen);
        Assert.Equal(3, asked);
    }

    [Fact]
    public async Task A_token_within_the_skew_of_its_expiry_is_asked_for_once_more()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        ManualClock clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        Queue<BearerToken> answers = new([
            new BearerToken("stale", clock.GetUtcNow() + SparqlHttpClient.TokenExpirySkew - TimeSpan.FromSeconds(1)),
            new BearerToken("fresh", clock.GetUtcNow().AddMinutes(5)),
        ]);
        SparqlHttpClient client = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default, _ => ValueTask.FromResult<BearerToken?>(answers.Dequeue()), clock);

        Assert.Equal("Bearer fresh", await AuthorizationAsync(client));
        Assert.Empty(answers);
    }

    [Fact]
    public async Task A_token_stale_twice_is_refused_before_anything_is_sent()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        ManualClock clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        DateTimeOffset expired = clock.Now.AddMinutes(-1);
        int asked = 0;
        SparqlHttpClient client = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default, _ =>
        {
            asked++;
            return ValueTask.FromResult<BearerToken?>(new BearerToken("expired", expired));
        }, clock);

        await Assert.ThrowsAsync<BearerTokenExpiredException>(() => client.StatusAsync(C.Ct));
        Assert.Equal(2, asked);

        // The clock is the client's: the same token, an hour earlier, is sent.
        clock.Now = clock.Now.AddHours(-1);
        Assert.Equal("Bearer expired", await AuthorizationAsync(client));
    }

    [Fact]
    public async Task No_token_sends_no_header_and_a_fixed_one_is_sent_as_given()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        ManualClock clock = new(DateTimeOffset.UnixEpoch);

        SparqlHttpClient anonymous = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default);
        SparqlHttpClient none = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default, _ => ValueTask.FromResult<BearerToken?>(null), clock);
        SparqlHttpClient given = new(Http, new Uri(host.Address, "echo/"), ClientLimits.Default, BearerToken.Fixed("given"), clock);

        Assert.Equal(string.Empty, await AuthorizationAsync(anonymous));
        Assert.Equal(string.Empty, await AuthorizationAsync(none));
        Assert.Equal("Bearer given", await AuthorizationAsync(given));
    }

    [Fact]
    public void A_token_never_renders_its_value()
    {
        Assert.DoesNotContain("secret", new BearerToken("secret", DateTimeOffset.UnixEpoch).ToString(), StringComparison.Ordinal);
    }

    private static async Task<string> AuthorizationAsync(SparqlHttpClient client)
    {
        using HttpResponseMessage response = await client.StatusAsync(C.Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(C.Ct);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
