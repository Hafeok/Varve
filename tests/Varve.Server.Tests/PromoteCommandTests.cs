// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Server.Commands;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// <c>varve promote</c> (ADR 0185 decision 4): one request to the instance
/// named, with the source and the position the operator states, and its
/// answer reported as it came.
/// </summary>
public sealed class PromoteCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task promote_sends_one_request_with_the_source_and_position_and_reports_the_epoch()
    {
        Answering handler = new(HttpStatusCode.OK, """{"epoch":4,"position":120}""");
        using HttpClient http = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();

        bool promoted = await Promote.AskAsync(http, "a-token", "https://varve-2.example", "orders", "https://acct.blob.core.windows.net/primary", 120, new Io(output, error, Stream.Null), Ct);

        Assert.True(promoted);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("https://varve-2.example/manage/datasets/orders/state", handler.Address);
        Assert.Equal("Bearer a-token", handler.Authorization);
        Assert.Equal("""{"promote":{"source":"https://acct.blob.core.windows.net/primary","position":120}}""", handler.Body);
        Assert.Contains("orders: promoted at epoch 4. The promotion finished at position 120. It is written from there on", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    /// <summary>
    /// ADR 0185 decision 4: a promotion that finished above the position
    /// named says so. A shipper mid-round created chunks before the fence.
    /// </summary>
    [Fact]
    public async Task promote_says_where_the_promotion_finished_when_that_is_above_the_position_named()
    {
        Answering handler = new(HttpStatusCode.OK, """{"epoch":4,"position":123,"asked":120,"keptAboveAsked":3}""");
        using HttpClient http = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();

        bool promoted = await Promote.AskAsync(http, null, "https://varve-2.example", "orders", "s3://primary", 120, new Io(output, error, Stream.Null), Ct);

        Assert.True(promoted);
        Assert.Contains(
            "orders: promoted at epoch 4. The promotion finished at position 123, 3 above the 120 you named: a shipper created those commits on the copy before the fence. "
            + "The source acknowledged them, and the copy keeps them. It is written from there on",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task a_refusal_is_reported_with_its_reason_and_the_command_does_not_ask_again()
    {
        Answering handler = new(HttpStatusCode.Conflict,
            """{"type":"https://w3id.org/varve/problems/takeover-refused","title":"This instance refuses to take the dataset over.","status":409,"detail":"The copy's last position is 118, not the 120 asked for. Nothing was changed; ask again with the position you mean to keep.","reason":"position-mismatch","epoch":3}""");
        using HttpClient http = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();

        bool promoted = await Promote.AskAsync(http, null, "https://varve-2.example", "orders", "s3://primary", 120, new Io(output, error, Stream.Null), Ct);

        Assert.False(promoted);
        Assert.Equal(1, handler.Requests);
        Assert.Null(handler.Authorization);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("orders: not promoted. position-mismatch: The copy's last position is 118, not the 120 asked for.", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>varve status</c>: each dataset, and each shipping target with its
    /// state and its detail, as readiness reports them (ADR 0184 decision
    /// 11). A copy that waits for its first shipping round says why.
    /// </summary>
    [Fact]
    public async Task status_shows_each_shipping_target_with_its_state_and_its_detail()
    {
        Answering handler = new(HttpStatusCode.OK,
            """{"status":"ready","datasets":{"copy":{"state":"awaiting-shipping","role":"follower","reason":"It is declared a copy, and waits for the first shipping round."},"orders":{"state":"ready","role":"writer","position":42,"lag":0,"shipping":{"https://a.invalid/copies/orders":{"state":"shipping","shippedThrough":40,"lag":2,"catalogPosition":32},"s3://replicas/orders":{"state":"shipping-diverged","failures":1,"detail":"The shipping target has diverged from its source at log slot 00000003/00000002."}}}}}""");
        using HttpClient http = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();

        bool ready = await Status.ShowAsync(http, "https://varve-1.example", new Io(output, error, Stream.Null), Ct);

        Assert.True(ready);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://varve-1.example/health/ready", handler.Address);
        Assert.Null(handler.Authorization);
        Assert.Equal(
            """
            https://varve-1.example: ready
              copy: awaiting-shipping, follower
                It is declared a copy, and waits for the first shipping round.
              orders: ready, writer, at position 42
                ships to https://a.invalid/copies/orders: shipping, through position 40 (2 behind), checkpoint at 32
                ships to s3://replicas/orders: shipping-diverged, 1 failed round
                  The shipping target has diverged from its source at log slot 00000003/00000002.

            """.ReplaceLineEndings(Environment.NewLine),
            output.ToString());
    }

    [Fact]
    public async Task status_of_an_instance_that_is_not_ready_lists_what_is_not_and_says_so()
    {
        Answering handler = new(HttpStatusCode.ServiceUnavailable,
            """{"type":"https://w3id.org/varve/problems/not-ready","title":"The server is not ready.","status":503,"datasets":[{"name":"orders","state":"failed","role":"writer","reason":"The store did not answer."}]}""");
        using HttpClient http = new(handler);
        using StringWriter output = new();
        using StringWriter error = new();

        bool ready = await Status.ShowAsync(http, "https://varve-1.example", new Io(output, error, Stream.Null), Ct);

        Assert.False(ready);
        Assert.Contains("https://varve-1.example: not ready", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("  orders: failed, writer", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("    The store did not answer.", output.ToString(), StringComparison.Ordinal);
    }

    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Address { get; private set; }

        public string? Authorization { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Method = request.Method;
            Address = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };
        }
    }
}
