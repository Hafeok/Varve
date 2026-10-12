// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Varve.Server.Tests;

/// <summary>
/// The exporter (ADR 0112): with <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set the
/// server builds the SDK and posts traces, metrics and logs over OTLP/HTTP to
/// a collector, here a listener that keeps what it receives. The bodies are
/// protobuf, whose strings are UTF-8 in the clear, so the span, the
/// instrument and the log line are found by name. Environment variables are
/// process-wide, so the class runs alone.
/// </summary>
[Collection(nameof(TelemetryTests))]
public class TelemetryTests
{
    [Fact]
    public async Task with_an_otlp_endpoint_the_server_exports_the_span_the_metrics_and_the_log_line()
    {
        using Collector collector = await Collector.StartAsync();
        Dictionary<string, string?> environment = new(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Address,
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_METRIC_EXPORT_INTERVAL"] = "300",
            ["OTEL_BSP_SCHEDULE_DELAY"] = "100",
            ["OTEL_BLRP_SCHEDULE_DELAY"] = "100",
            ["OTEL_SERVICE_NAME"] = "varve-under-test",
        };

        using (new EnvironmentScope(environment))
        {
            Assert.True(Telemetry.Enabled);
            await using RunningServer server = await RunningServer.StartAsync(new Dictionary<string, string>
            {
                ["Varve:Auth:Mode"] = "Anonymous",
                ["Varve:Datasets:d:Storage"] = "Memory",
                ["Varve:Telemetry:QueryText"] = "true",
                ["Logging:LogLevel:Varve.Protocol"] = "Information",
            });

            using HttpRequestMessage update = new(HttpMethod.Post, "datasets/d/sparql") { Content = new StringContent("INSERT DATA { <http://ex/s> <http://ex/p> 1 }", Encoding.UTF8, "application/sparql-update") };
            using HttpResponseMessage committed = await server.Client.SendAsync(update, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, committed.StatusCode);
            using HttpResponseMessage answered = await server.Client.GetAsync("datasets/d/sparql?query=" + Uri.EscapeDataString("SELECT ?o { ?s ?p ?o }"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);

            await collector.WaitForAsync("/v1/traces", "SELECT ?o { ?s ?p ?o }", TestContext.Current.CancellationToken);
            await collector.WaitForAsync("/v1/traces", "varve-under-test", TestContext.Current.CancellationToken);
            await collector.WaitForAsync("/v1/metrics", "varve.store.commit.duration", TestContext.Current.CancellationToken);
            // The log exporter sends the template as the body and the parameters as attributes.
            await collector.WaitForAsync("/v1/logs", "committed position {Position}", TestContext.Current.CancellationToken);
            Assert.Equal(0, await server.StopAsync());
        }
    }

    [Fact]
    public void without_an_endpoint_nothing_is_built()
    {
        using EnvironmentScope scope = new(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = null,
            ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = null,
            ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = null,
            ["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"] = null,
        });
        Assert.False(Telemetry.Enabled);
    }

    /// <summary>An OTLP/HTTP receiver that keeps every body by path.</summary>
    private sealed class Collector : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly List<(string Path, byte[] Body)> _received = [];
        private readonly SemaphoreSlim _arrived = new(0);

        private Collector(HttpListener listener, string address)
        {
            _listener = listener;
            Address = address;
        }

        internal string Address { get; }

        internal static async Task<Collector> StartAsync()
        {
            int port = FreePort();
            HttpListener listener = new();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            Collector collector = new(listener, "http://127.0.0.1:" + port);
            _ = Task.Run(collector.ServeAsync);
            await Task.Yield();
            return collector;
        }

        internal async Task WaitForAsync(string path, string text, CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            byte[] needle = Encoding.UTF8.GetBytes(text);

            while (true)
            {
                lock (_received)
                {
                    foreach ((string receivedPath, byte[] body) in _received)
                    {
                        if (receivedPath == path && body.AsSpan().IndexOf(needle) >= 0)
                        {
                            return;
                        }
                    }
                }

                try
                {
                    await _arrived.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    string paths;

                    lock (_received)
                    {
                        paths = string.Join(", ", _received.ConvertAll(r => r.Path + " (" + r.Body.Length + " bytes)"));
                    }

                    Assert.Fail("No " + path + " body carrying '" + text + "' arrived in 30 s; received: " + paths);
                }
            }
        }

        public void Dispose()
        {
            _listener.Close();
            _arrived.Dispose();
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                using MemoryStream body = new();
                await context.Request.InputStream.CopyToAsync(body);

                lock (_received)
                {
                    _received.Add((context.Request.Url!.AbsolutePath, body.ToArray()));
                }

                _arrived.Release();
                context.Response.StatusCode = 200;
                context.Response.Close();
            }
        }

        private static int FreePort()
        {
            using TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
    }

    /// <summary>Environment variables set for the scope and restored after it.</summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _before = new(StringComparer.Ordinal);

        internal EnvironmentScope(Dictionary<string, string?> variables)
        {
            foreach ((string name, string? value) in variables)
            {
                _before[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}

/// <summary>Environment variables are process-wide, so these tests run alone.</summary>
[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public sealed class TelemetryTestsRunAlone;
