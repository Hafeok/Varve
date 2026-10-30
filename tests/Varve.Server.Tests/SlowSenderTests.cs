// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// Every route that reads a request body reads all of it, however the body
/// arrives (#127, guarded after it was found to be CI's image pull, not the
/// server). A raw socket sends each body in segments of one to seven bytes
/// with pauses between them, after <c>Expect: 100-continue</c>, and chunked
/// with chunk boundaries inside a multipart part and inside a UTF-8
/// sequence; then the test reads back what the request should have done.
/// A body over the limit, declared or chunked, is <c>413</c>.
/// </summary>
/// <remarks>
/// Shown to catch the defect class: against a reader that takes the first
/// <c>ReadAsync</c> that returns bytes as the whole body, all four tests
/// failed, each delivery with a <c>400</c> and the chunked body over the
/// limit with a <c>204</c> (the traceability record of #129's step 2). That
/// reader is not kept.
/// </remarks>
public sealed class SlowSenderTests
{
    private const int Limit = 16 << 10;
    private const string Ns = "https://w3id.org/varve/ns#";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public enum Delivery
    {
        Segments,
        ExpectContinue,
        Chunked,
    }

    [Theory]
    [InlineData(Delivery.Segments)]
    [InlineData(Delivery.ExpectContinue)]
    [InlineData(Delivery.Chunked)]
    public async Task Every_route_that_reads_a_body_reads_all_of_it(Delivery delivery)
    {
        await using RunningServer server = await Start();
        Sender send = new(server.Address, delivery, new Random((int)delivery + 127));
        string tag = delivery.ToString().ToLowerInvariant();

        // SPARQL Update, as the body and as a form.
        string g1 = "urn:slow:" + tag + ":update";
        Assert.Equal(204, (await send.Async("POST", "datasets/d/sparql", "application/sparql-update", Utf8("INSERT DATA { GRAPH <" + g1 + "> { " + Triples(40) + " } }"))).Status);
        Assert.Equal(40, await CountAsync(server, g1));

        string g2 = "urn:slow:" + tag + ":form";
        string form = "update=" + Uri.EscapeDataString("INSERT DATA { GRAPH <" + g2 + "> { " + Triples(30) + " } }");
        Assert.Equal(204, (await send.Async("POST", "datasets/d/sparql", "application/x-www-form-urlencoded", Utf8(form))).Status);
        Assert.Equal(30, await CountAsync(server, g2));

        // A SPARQL query whose body is long, its answer at its end.
        string query = "# " + new string('x', 3000) + "\nSELECT (COUNT(*) AS ?n) WHERE { GRAPH <" + g1 + "> { ?s ?p ?o } }";
        Response answered = await send.Async("POST", "datasets/d/sparql", "application/sparql-query", Utf8(query), accept: "application/sparql-results+json");
        Assert.Equal(200, answered.Status);
        Assert.Contains("\"40\"", answered.Body, StringComparison.Ordinal);

        // Graph Store PUT and POST of Turtle, and POST of two multipart parts.
        string g3 = "urn:slow:" + tag + ":put";
        Assert.Equal(201, (await send.Async("PUT", "datasets/d/graphs?graph=" + Uri.EscapeDataString(g3), "text/turtle", Utf8(Triples(50)))).Status);
        Assert.Equal(50, await CountAsync(server, g3));
        Assert.Equal(204, (await send.Async("POST", "datasets/d/graphs?graph=" + Uri.EscapeDataString(g3), "text/turtle", Utf8(Triples(70)))).Status);
        Assert.Equal(70, await CountAsync(server, g3));

        string g4 = "urn:slow:" + tag + ":multipart";
        const string Boundary = "slow-sender-boundary";
        string multipart =
            "--" + Boundary + "\r\nContent-Type: text/turtle\r\nContent-Disposition: form-data; name=\"a\"; filename=\"a.ttl\"\r\n\r\n" + Triples(25, "a") + "\r\n"
            + "--" + Boundary + "\r\nContent-Type: text/turtle\r\nContent-Disposition: form-data; name=\"b\"; filename=\"b.ttl\"\r\n\r\n" + Triples(25, "b") + "\r\n"
            + "--" + Boundary + "--\r\n";
        Assert.Equal(201, (await send.Async("POST", "datasets/d/graphs?graph=" + Uri.EscapeDataString(g4), "multipart/form-data; boundary=" + Boundary, Utf8(multipart))).Status);
        Assert.Equal(50, await CountAsync(server, g4));

        // The management plane: a declaration, the settings, the state, and an apply.
        string declared = "slow" + tag;
        string declaration = "<urn:varve:from-config:" + declared + "> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"" + declared + "\" ; <" + Ns + "storage> <" + Ns + "Memory> .\n# " + new string('é', 800);
        Assert.Equal(201, (await send.Async("PUT", "manage/datasets/" + declared, "application/trig", Utf8(declaration))).Status);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("manage/datasets/" + declared + "/status", UriKind.Relative), Ct)).StatusCode);

        using HttpResponseMessage before = await server.Client.GetAsync(new Uri("manage/datasets/" + declared + "/settings", UriKind.Relative), Ct);
        string scope = "<urn:varve:from-config:" + declared + "> <" + Ns + "defaultAccessScope> <" + Ns + "Current> .\n# " + new string('ø', 800);
        Assert.Equal(204, (await send.Async("PUT", "manage/datasets/" + declared + "/settings", "application/trig", Utf8(scope), ifMatch: before.Headers.ETag!.Tag)).Status);
        using (JsonDocument settings = JsonDocument.Parse(await server.Client.GetStringAsync(new Uri("manage/datasets/" + declared + "/settings", UriKind.Relative), Ct)))
        {
            Assert.Equal("Current", settings.RootElement.GetProperty("defaultAccessScope").GetString());
        }

        Assert.Equal(204, (await send.Async("PUT", "manage/datasets/" + declared + "/state", "application/json", Utf8("{\"state\":\"closed\"" + new string(' ', 600) + "}"))).Status);
        using (JsonDocument state = JsonDocument.Parse(await server.Client.GetStringAsync(new Uri("manage/datasets/" + declared + "/state", UriKind.Relative), Ct)))
        {
            Assert.Equal("closed", state.RootElement.GetProperty("state").GetString());
        }

        string applied = "applied" + tag;
        string graph = Ns + "manage/dataset/" + applied;
        string trig = "<" + graph + "> { <urn:varve:apply:" + applied + "> a <" + Ns + "Dataset> ; <" + Ns + "datasetName> \"" + applied + "\" ; <" + Ns + "storage> <" + Ns + "Memory> . }\n# " + new string('ß', 800);
        Response apply = await send.Async("POST", "manage/apply", "application/trig", Utf8(trig), graphsMatch: "<" + graph + ">=0");
        Assert.True(apply.Status is 200 or 204, apply.Status + ": " + apply.Body);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync(new Uri("manage/datasets/" + applied + "/status", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused_declared_or_chunked()
    {
        await using RunningServer server = await Start();
        byte[] large = Utf8(new string('#', Limit + 1024));

        Response declared = await new Sender(server.Address, Delivery.Segments, new Random(1)).Async("POST", "datasets/d/graphs?graph=urn%3Aslow%3Alarge", "text/turtle", large, sendBodyWhenRefused: false);
        Assert.Equal(413, declared.Status);

        Response chunked = await new Sender(server.Address, Delivery.Chunked, new Random(2)).Async("POST", "datasets/d/graphs?graph=urn%3Aslow%3Alarge", "text/turtle", large);
        Assert.Equal(413, chunked.Status);
        Assert.Equal(0, await CountAsync(server, "urn:slow:large"));
    }

    private static Task<RunningServer> Start() => RunningServer.StartAsync(new Dictionary<string, string>
    {
        ["Varve:Auth:Mode"] = "Anonymous",
        ["Varve:Datasets:d:Storage"] = "Memory",
        ["Varve:Limits:MaxRequestBody"] = Limit.ToString(CultureInfo.InvariantCulture),
    });

    // Triples whose objects carry two- and three-byte UTF-8 sequences, so a
    // cut inside one is a cut a decoder would notice.
    private static string Triples(int count, string prefix = "t")
    {
        StringBuilder text = new();

        for (int i = 0; i < count; i++)
        {
            text.Append("<urn:slow:").Append(prefix).Append(i.ToString(CultureInfo.InvariantCulture)).Append("> <urn:slow:p> \"ünïcødé ✓ ").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\" .\n");
        }

        return text.ToString();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task<int> CountAsync(RunningServer server, string graph)
    {
        string query = "SELECT (COUNT(*) AS ?n) WHERE { GRAPH <" + graph + "> { ?s ?p ?o } }";
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("datasets/d/sparql?query=" + Uri.EscapeDataString(query), UriKind.Relative));
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        using HttpResponseMessage response = await server.Client.SendAsync(request, Ct);
        using JsonDocument results = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return int.Parse(results.RootElement.GetProperty("results").GetProperty("bindings")[0].GetProperty("n").GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
    }

    private readonly record struct Response(int Status, string Body);

    /// <summary>One HTTP/1.1 request a connection, written by hand.</summary>
    private sealed class Sender(Uri server, Delivery delivery, Random random)
    {
        internal async Task<Response> Async(
            string method,
            string target,
            string contentType,
            byte[] body,
            string? accept = null,
            string? ifMatch = null,
            string? graphsMatch = null,
            bool sendBodyWhenRefused = true)
        {
            using TcpClient client = new() { NoDelay = true };
            await client.ConnectAsync(server.Host, server.Port, Ct);
            NetworkStream stream = client.GetStream();

            StringBuilder head = new();
            head.Append(method).Append(" /").Append(target).Append(" HTTP/1.1\r\n");
            head.Append("Host: ").Append(server.Authority).Append("\r\n");
            head.Append("Connection: close\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            Optional(head, "Accept", accept);
            Optional(head, "If-Match", ifMatch);
            Optional(head, "Varve-If-Graphs-Match", graphsMatch);
            head.Append(delivery == Delivery.Chunked ? "Transfer-Encoding: chunked\r\n" : "Content-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n");

            if (delivery == Delivery.ExpectContinue)
            {
                head.Append("Expect: 100-continue\r\n");
            }

            head.Append("\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), Ct);
            ResponseReader reader = new(stream);

            if (delivery == Delivery.ExpectContinue)
            {
                Response interim = await reader.ReadAsync(interimOnly: true);

                if (interim.Status != 100)
                {
                    return interim;
                }
            }

            if (!sendBodyWhenRefused)
            {
                return await reader.ReadAsync(interimOnly: false);
            }

            // The answer is read while the body is written. A server that
            // refuses a body answers and closes with the rest unread, which
            // resets the connection; Windows then discards whatever the
            // client had received and not yet read, the answer included.
            Task<Response> answer = reader.ReadAsync(interimOnly: false);

            try
            {
                if (delivery == Delivery.Chunked)
                {
                    await WriteChunkedAsync(stream, body, answer);
                }
                else
                {
                    await WriteSegmentsAsync(stream, body, answer);
                }
            }
            catch (IOException)
            {
                // The server refused the body and closed: its answer is read below.
            }

            return await answer;
        }

        private static void Optional(StringBuilder head, string name, string? value)
        {
            if (value is not null)
            {
                head.Append(name).Append(": ").Append(value).Append("\r\n");
            }
        }

        // Segments of one to seven bytes, with a pause after about one in
        // forty, so the server's reads see the body arrive in many pieces.
        private async Task WriteSegmentsAsync(NetworkStream stream, byte[] body, Task answered)
        {
            for (int at = 0; at < body.Length && !answered.IsCompleted;)
            {
                int length = Math.Min(random.Next(1, 8), body.Length - at);
                await stream.WriteAsync(body.AsMemory(at, length), Ct);
                await stream.FlushAsync(Ct);
                at += length;

                if (random.Next(40) == 0)
                {
                    await Task.Delay(1, Ct);
                }
            }
        }

        // Chunks of 1 to 97 bytes, cut wherever the random cut falls: inside
        // a UTF-8 sequence, a multipart delimiter or a part's body.
        private async Task WriteChunkedAsync(NetworkStream stream, byte[] body, Task answered)
        {
            for (int at = 0; at < body.Length && !answered.IsCompleted;)
            {
                int length = Math.Min(random.Next(1, 98), body.Length - at);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(length.ToString("x", CultureInfo.InvariantCulture) + "\r\n"), Ct);
                await stream.WriteAsync(body.AsMemory(at, length), Ct);
                await stream.WriteAsync("\r\n"u8.ToArray(), Ct);
                at += length;

                if (random.Next(8) == 0)
                {
                    await Task.Delay(1, Ct);
                }
            }

            await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), Ct);
        }
    }

    /// <summary>Reads a response's status line, headers and body from a connection the server closes.</summary>
    private sealed class ResponseReader(NetworkStream stream)
    {
        private readonly List<byte> _buffer = [];

        internal async Task<Response> ReadAsync(bool interimOnly)
        {
            byte[] chunk = new byte[4096];

            while (true)
            {
                if (TryHead(out int status, out int end))
                {
                    if (interimOnly || status != 100)
                    {
                        if (interimOnly && status == 100)
                        {
                            Consume(end);
                            return new Response(100, string.Empty);
                        }

                        if (!interimOnly)
                        {
                            await DrainAsync(chunk);
                            return new Response(status, Encoding.UTF8.GetString(_buffer.ToArray(), end, _buffer.Count - end));
                        }

                        return new Response(status, string.Empty);
                    }

                    // An interim 100 before the final answer: skip it.
                    Consume(end);
                    continue;
                }

                int read;

                try
                {
                    read = await stream.ReadAsync(chunk, Ct);
                }
                catch (IOException)
                {
                    read = 0;
                }

                if (read == 0)
                {
                    throw new IOException("The connection closed before a response: " + Encoding.ASCII.GetString(_buffer.ToArray()));
                }

                _buffer.AddRange(chunk.AsSpan(0, read));
            }
        }

        private async Task DrainAsync(byte[] chunk)
        {
            while (true)
            {
                int read;

                try
                {
                    read = await stream.ReadAsync(chunk, Ct);
                }
                catch (IOException)
                {
                    return;
                }

                if (read == 0)
                {
                    return;
                }

                _buffer.AddRange(chunk.AsSpan(0, read));
            }
        }

        private bool TryHead(out int status, out int end)
        {
            byte[] bytes = _buffer.ToArray();
            string text = Encoding.ASCII.GetString(bytes);
            int blank = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            status = 0;
            end = 0;

            if (blank < 0)
            {
                return false;
            }

            status = int.Parse(text.AsSpan(9, 3), CultureInfo.InvariantCulture);
            end = blank + 4;
            return true;
        }

        private void Consume(int count)
        {
            _buffer.RemoveRange(0, count);
        }
    }
}
