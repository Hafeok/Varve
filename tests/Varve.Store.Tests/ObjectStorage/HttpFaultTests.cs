// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A handler in the request pipeline that injects one fault at one request
/// (#92's gate): a 500, a 503 or a 429 with Retry-After the store never
/// applied, a timeout before the request leaves, a response lost after the
/// store applied it, or the process killed before or after.
/// </summary>
internal sealed class FaultingHandler(FaultController faults, HttpMessageHandler? inner = null) : DelegatingHandler(inner ?? new SocketsHttpHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        switch (faults.Next())
        {
            case ObjectFault.Http500:
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request };

            case ObjectFault.Http503:
                return Throttled(HttpStatusCode.ServiceUnavailable, request);

            case ObjectFault.Http429:
                return Throttled(HttpStatusCode.TooManyRequests, request);

            case ObjectFault.HttpTimeout:
                throw new TaskCanceledException("injected: the request timed out before it left");

            case ObjectFault.HttpLostResponse:
                (await base.SendAsync(request, cancellationToken)).Dispose();
                throw new HttpRequestException("injected: the response was lost after the store applied the request");

            case ObjectFault.KillBefore:
                faults.Dead = true;
                throw new ProcessKilled();

            case ObjectFault.KillAfter:
                (await base.SendAsync(request, cancellationToken)).Dispose();
                faults.Dead = true;
                throw new ProcessKilled();

            default:
                return await base.SendAsync(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Throttled(HttpStatusCode status, HttpRequestMessage request)
    {
        HttpResponseMessage response = new(status) { RequestMessage = request };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }
}

/// <summary>Records what the service answered each request: method, path, status and <c>x-ms-error-code</c>.</summary>
internal sealed class RecordingHandler(HttpMessageHandler? inner = null) : DelegatingHandler(inner ?? new SocketsHttpHandler())
{
    private readonly List<(string Method, string Path, int Status, string? Code, string? Version)> _exchanges = [];

    public IReadOnlyList<(string Method, string Path, int Status, string? Code, string? Version)> Exchanges
    {
        get
        {
            lock (_exchanges)
            {
                return [.. _exchanges];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        string? code = response.Headers.TryGetValues("x-ms-error-code", out IEnumerable<string>? codes) ? string.Join(",", codes) : null;
        string? version = response.Headers.TryGetValues("x-ms-version", out IEnumerable<string>? versions) ? string.Join(",", versions) : null;

        lock (_exchanges)
        {
            _exchanges.Add((request.Method.Method, request.RequestUri!.AbsolutePath, (int)response.StatusCode, code, version));
        }

        return response;
    }
}

/// <summary>
/// The failure-injection workload over HTTP, one fault of each kind at
/// every request, or every <c>stride</c>-th: the loop Azurite's gate and the
/// account's leg (#143) share, each opening a fresh store for every run.
/// </summary>
internal static class HttpFaultSuite
{
    // The HTTP faults alone: a kill between requests is the same event over
    // any transport, and the in-memory gate injects it at every request.
    public static readonly ObjectFault[] Kinds =
    [
        ObjectFault.Http500, ObjectFault.Http503, ObjectFault.Http429, ObjectFault.HttpTimeout, ObjectFault.HttpLostResponse,
    ];

    // Six steps keep the leg to minutes; a deeper run sets the variable.
    public static int Steps => int.TryParse(Environment.GetEnvironmentVariable("VARVE_HTTP_FAULT_STEPS"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int steps) ? steps : 6;

    /// <summary>
    /// Runs the workload clean to count its requests, then once per fault
    /// kind at every <paramref name="stride"/>-th request. <paramref name="open"/>
    /// gives each run its store through the handler that injects the fault,
    /// numbered from zero, and what to dispose with it.
    /// </summary>
    public static async Task EveryRequestAsync(string label, int stride, Func<FaultingHandler, int, Task<(IObjectStore Store, IDisposable Owner)>> open)
    {
        int runs = 0;
        int requests;
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();

        await using (HttpFaultRun clean = await HttpFaultRun.StartAsync(null, handler => open(handler, runs++)))
        {
            await clean.Workload.RunAsync();
            requests = clean.Workload.Requests;
        }

        int points = 0;
        int crashes = 0;

        foreach (ObjectFault kind in Kinds)
        {
            for (int at = 1; at <= requests; at += stride)
            {
                await using HttpFaultRun run = await HttpFaultRun.StartAsync((at, kind), handler => open(handler, runs++));

                try
                {
                    await run.Workload.RunAsync();
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(kind + " at request " + at + " of " + requests + ": " + error.Message, error);
                }

                points++;
                crashes += run.Workload.Crashes;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + requests + " requests, " + points + " injection points (every " + stride + "), " + crashes + " crashes while reopening, " + elapsed.Elapsed.TotalSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " s");
    }
}

/// <summary>One run of the workload over a store whose requests go through a <see cref="FaultingHandler"/>.</summary>
internal sealed class HttpFaultRun : IAsyncDisposable
{
    private readonly IDisposable _owner;
    private readonly FaultingHandler _handler;

    private HttpFaultRun(IDisposable owner, FaultingHandler handler, ObjectFaultRun workload)
    {
        _owner = owner;
        _handler = handler;
        Workload = workload;
    }

    public ObjectFaultRun Workload { get; }

    public static async Task<HttpFaultRun> StartAsync((int At, ObjectFault Kind)? fault, Func<FaultingHandler, Task<(IObjectStore Store, IDisposable Owner)>> open)
    {
        FaultController faults = new(fault);
        FaultingHandler handler = new(faults);
        (IObjectStore store, IDisposable owner) = await open(handler);
        return new HttpFaultRun(owner, handler, new ObjectFaultRun(9301, HttpFaultSuite.Steps, faults, store));
    }

    public async ValueTask DisposeAsync()
    {
        await Workload.DisposeAsync();
        _owner.Dispose();
        _handler.Dispose();
    }
}

/// <summary>The failure-injection workload over HTTP against Azurite, one fault of each kind at every request.</summary>
public sealed class AzuriteFaultTests
{
    [Fact]
    public Task after_a_fault_at_any_request_over_http_the_dataset_reopens_with_nothing_lost_or_doubled() =>
        HttpFaultSuite.EveryRequestAsync("Azurite", 1, async (handler, _) =>
        {
            Uri container = await Azurite.NewContainerAsync();
            AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = container, Clock = new InstantClock(DateTimeOffset.UtcNow), Handler = handler });
            return ((IObjectStore)store, (IDisposable)store);
        });
}
