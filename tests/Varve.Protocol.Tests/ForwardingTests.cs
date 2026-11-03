// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// Write routing by forwarding (ADR 0168) over real loopback connections:
/// two hosts, each serving the dataset when it is the writer and forwarding
/// to the other when it is not. A write's body arrives in small delayed
/// chunks and is forwarded whole; the writer's answer comes back unchanged; a
/// forwarded request is never forwarded again; an unreachable writer is
/// answered, not retried; and over generated schedules that move the writer
/// while writes are in flight, every write is committed once, by the instance
/// that was its writer, or refused.
/// </summary>
public sealed class ForwardingTests
{
    // Sized for the land gate: each schedule is up to twelve requests over loopback.
    private const int Schedules = 100;

    [Fact]
    public async Task A_write_to_the_other_instance_is_forwarded_whole_and_answered_by_the_writer()
    {
        await using Pair pair = await Pair.StartAsync();
        pair.Writer = 0;

        // From the non-writer, with a chunked body that arrives slowly.
        using HttpRequestMessage update = new(HttpMethod.Post, new Uri("datasets/d/sparql", UriKind.Relative))
        {
            Content = new SlowContent("INSERT DATA { <http://ex/a> <http://ex/p> \"" + new string('x', 4096) + "\" }", "application/sparql-update"),
        };
        using HttpResponseMessage response = await pair.Hosts[1].Client.SendAsync(update, P.Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("1", P.Header(response, "Varve-Position"));
        Assert.Equal(1L, pair.Datasets[0].Head.Value);
        Assert.Equal(0L, pair.Datasets[1].Head.Value);

        // A form-encoded update is forwarded whole too; a refusal comes back as the writer wrote it.
        using HttpRequestMessage form = new(HttpMethod.Post, new Uri("datasets/d/sparql", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["update"] = "INSERT DATA { <http://ex/b> <http://ex/p> 2 }" }),
        };
        Assert.Equal(HttpStatusCode.NoContent, (await pair.Hosts[1].Client.SendAsync(form, P.Ct)).StatusCode);
        using HttpResponseMessage stale = await pair.Hosts[1].Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/c> <http://ex/p> 3 }", "\"1\""), P.Ct);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/precondition-failed\"", await stale.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
        Assert.Equal(2L, pair.Datasets[0].Head.Value);

        // A read is served where it arrives.
        Assert.Equal(HttpStatusCode.OK, (await pair.Hosts[1].Client.SendAsync(P.Query("datasets/d/sparql", "ASK {}"), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task A_forwarded_write_that_reaches_a_non_writer_is_refused_and_never_forwarded_again()
    {
        await using Pair pair = await Pair.StartAsync();

        // Each instance thinks the other writes: a request forwarded once
        // reaches an instance that would forward it again, and refuses.
        pair.Writer = Pair.Nobody;
        using HttpResponseMessage moved = await pair.Hosts[0].Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        string body = await moved.Content.ReadAsStringAsync(P.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, moved.StatusCode);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/writer-moved\"", body, StringComparison.Ordinal);
        Assert.Contains("\"epoch\":7", body, StringComparison.Ordinal);
        Assert.Equal(0L, pair.Datasets[0].Head.Value + pair.Datasets[1].Head.Value);
        Assert.Equal(2, pair.Forwards);
        Assert.True(pair.Stale > 0);
    }

    [Fact]
    public async Task An_unreachable_writer_is_answered_once_and_marked_stale()
    {
        await using Pair pair = await Pair.StartAsync();
        pair.Writer = Pair.Unreachable;

        using HttpResponseMessage response = await pair.Hosts[0].Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        // Whether the writer received it is not known: the outcome is unknown, with the request id and no position (ADR 0214).
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/commit-outcome-unknown\"", body, StringComparison.Ordinal);
        Assert.Contains("\"requestId\":", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"position\"", body, StringComparison.Ordinal);
        Assert.Equal(1, pair.Stale);
    }

    /// <summary>
    /// A writer that refuses a forwarded batch as superseded or stepped down
    /// committed nothing (ADR 0179 point 12): the forwarding instance answers
    /// writer-moved, with the writer's answer as its reason, marks the route
    /// stale, and sends the body once. The writer is another process, so it
    /// is played here by a loopback endpoint that answers as one does.
    /// </summary>
    [Theory]
    [InlineData("writer-superseded")]
    [InlineData("writer-stepped-down")]
    public async Task A_forward_refused_across_a_handover_is_answered_writer_moved(string refusal)
    {
        int received = 0;
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        await using WebApplication stepped = builder.Build();
        stepped.Run(async context =>
        {
            Interlocked.Increment(ref received);
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync("{\"type\":\"https://w3id.org/varve/problems/" + refusal + "\",\"title\":\"t\",\"status\":503}", P.Ct);
        });
        await stepped.StartAsync(P.Ct);

        await using Pair pair = await Pair.StartAsync();
        pair.Elsewhere = stepped.Urls.First();
        pair.Writer = Pair.Other;
        using HttpResponseMessage moved = await pair.Hosts[0].Client.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { <http://ex/a> <http://ex/p> 1 }"), P.Ct);
        string body = await moved.Content.ReadAsStringAsync(P.Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, moved.StatusCode);
        Assert.Contains("\"type\":\"https://w3id.org/varve/problems/writer-moved\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"" + refusal + "\"", body, StringComparison.Ordinal);
        Assert.Contains("\"epoch\":7", body, StringComparison.Ordinal);
        Assert.Equal(1, received);
        Assert.True(pair.Stale > 0);
        Assert.Equal(0L, pair.Datasets[0].Head.Value + pair.Datasets[1].Head.Value);

        // Any other 503 from the writer comes back as the writer wrote it.
        using HttpResponseMessage busy = await pair.Hosts[0].Client.SendAsync(P.Query("datasets/d/sparql", "ASK {}"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, busy.StatusCode);
    }

    /// <summary>
    /// For generated schedules of writes to either instance, the writer moved
    /// between them while writes are in flight: each write is committed
    /// exactly once, in the dataset of the instance that served it as its
    /// writer, or refused with 503; none is committed twice or lost after a 2xx.
    /// </summary>
    [Fact]
    public async Task Over_generated_schedules_each_write_is_committed_once_or_refused()
    {
        await using Pair pair = await Pair.StartAsync();
        int serial = 0;
        Gen<int[]> schedules = Gen.Int[0, 2].Array[1, 12];

        await schedules.SampleAsync(async schedule =>
        {
            List<Task<(string Triple, HttpStatusCode Status, string Body)>> writes = [];

            foreach (int step in schedule)
            {
                if (step == 2)
                {
                    pair.Writer = 1 - pair.Writer;
                    await Task.Yield();
                    continue;
                }

                string triple = "<http://ex/w" + Interlocked.Increment(ref serial) + "> <http://ex/p> 1";
                HttpClient via = pair.Hosts[step].Client;
                writes.Add(Task.Run(async () =>
                {
                    using HttpResponseMessage response = await via.SendAsync(P.Update("datasets/d/sparql", "INSERT DATA { " + triple + " }"), P.Ct);
                    return (triple, response.StatusCode, await response.Content.ReadAsStringAsync(P.Ct));
                }));
            }

            foreach ((string triple, HttpStatusCode status, string body) in await Task.WhenAll(writes))
            {
                int held = 0;

                foreach (Dataset dataset in pair.Datasets)
                {
                    held += await HoldsAsync(dataset, triple) ? 1 : 0;
                }

                if (status == HttpStatusCode.NoContent)
                {
                    Assert.True(held == 1, triple + " answered 204 and is held " + held + " times");
                }
                else
                {
                    Assert.True(status == HttpStatusCode.ServiceUnavailable && body.Contains("writer-moved", StringComparison.Ordinal), triple + " answered " + (int)status + " " + body);
                    Assert.Equal(0, held);
                }
            }
        }, iter: Schedules, threads: 1);
    }

    private static async Task<bool> HoldsAsync(Dataset dataset, string triple)
    {
        using DatasetView view = dataset.Pin();
        await Task.CompletedTask;
        string[] parts = triple.Split(' ');
        RdfTerm subject = RdfTerm.Iri(Encoding.UTF8.GetBytes(parts[0].Trim('<', '>')));
        return view.TryInternalise(subject, out TermHandle handle) && Any(view.Match(handle, TermHandle.None, TermHandle.None, GraphPattern.Any));
    }

    private static bool Any(IQuadCursor cursor)
    {
        using (cursor)
        {
            return cursor.MoveNext();
        }
    }

    /// <summary>Two hosts, each with its own dataset "d", and a directory both read: which one writes.</summary>
    private sealed class Pair : IAsyncDisposable
    {
        internal const int Nobody = -1;
        internal const int Unreachable = -2;
        internal const int Other = -3;
        // The forwards routed, then the routes marked stale.
        private readonly int[] _counts = new int[2];

        private Pair()
        {
        }

        internal ProtocolTestHost[] Hosts { get; } = new ProtocolTestHost[2];

        internal Dataset[] Datasets { get; } = new Dataset[2];

        internal volatile int Writer;

        // The address of a writer that is neither host, for Other.
        internal string? Elsewhere;

        internal int Forwards => Volatile.Read(ref _counts[0]);

        internal int Stale => Volatile.Read(ref _counts[1]);

        internal static async Task<Pair> StartAsync()
        {
            Pair pair = new();

            for (int i = 0; i < 2; i++)
            {
                pair.Datasets[i] = await P.NewDatasetAsync();
                ProtocolTestHost.Datasets datasets = new();
                datasets.Add("d", pair.Datasets[i]);
                pair.Hosts[i] = await ProtocolTestHost.StartAsync(datasets, writers: new Directory(pair, i), stopping: P.Ct);
            }

            return pair;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ProtocolTestHost host in Hosts)
            {
                await host.DisposeAsync();
            }

            foreach (Dataset dataset in Datasets)
            {
                await dataset.DisposeAsync();
            }
        }

        private sealed class Directory(Pair pair, int self) : IWriterDirectory
        {
            public WriterName Instance { get; } = new("instance-" + self);

            public ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken)
            {
                int writer = pair.Writer;

                if (writer == self)
                {
                    return ValueTask.FromResult(WriterRoute.Local);
                }

                Interlocked.Increment(ref pair._counts[0]);
                string address = writer switch
                {
                    Unreachable => "http://127.0.0.1:9/",
                    Other => pair.Elsewhere!,
                    Nobody => pair.Hosts[1 - self].Address.AbsoluteUri,
                    _ => pair.Hosts[writer].Address.AbsoluteUri,
                };
                return ValueTask.FromResult(new WriterRoute(WriterRouteKind.Remote, address, 7));
            }

            public void Stale(WriterTarget target) => Interlocked.Increment(ref pair._counts[1]);
        }
    }

    /// <summary>A body sent in 64-byte chunks, 5 ms apart, with no length: chunked transfer.</summary>
    private sealed class SlowContent : HttpContent
    {
        private readonly byte[] _bytes;

        internal SlowContent(string text, string mediaType)
        {
            _bytes = Encoding.UTF8.GetBytes(text);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (int at = 0; at < _bytes.Length; at += 64)
            {
                await stream.WriteAsync(_bytes.AsMemory(at, Math.Min(64, _bytes.Length - at)));
                await stream.FlushAsync();
                await Task.Delay(5);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
