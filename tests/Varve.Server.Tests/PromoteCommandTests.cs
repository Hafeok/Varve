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
        Assert.Contains("orders: promoted at epoch 4. It is written from position 120 on", output.ToString(), StringComparison.Ordinal);
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
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };
        }
    }
}
