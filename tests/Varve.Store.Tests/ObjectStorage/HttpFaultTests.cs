// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
internal sealed class FaultingHandler(FaultController faults) : DelegatingHandler(new SocketsHttpHandler())
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

/// <summary>The failure-injection workload over HTTP against Azurite, one fault of each kind at every request.</summary>
public sealed class AzuriteFaultTests
{
    // The HTTP faults alone: a kill between requests is the same event over
    // any transport, and the in-memory gate injects it at every request.
    private static readonly ObjectFault[] Kinds =
    [
        ObjectFault.Http500, ObjectFault.Http503, ObjectFault.Http429, ObjectFault.HttpTimeout, ObjectFault.HttpLostResponse,
    ];

    // Six steps keep the leg to minutes; a deeper run sets the variable.
    private static int Steps => int.TryParse(Environment.GetEnvironmentVariable("VARVE_HTTP_FAULT_STEPS"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int steps) ? steps : 6;

    [Fact]
    public async Task after_a_fault_at_any_request_over_http_the_dataset_reopens_with_nothing_lost_or_doubled()
    {
        int requests;

        await using (Run clean = await Run.StartAsync(null))
        {
            await clean.Workload.RunAsync();
            requests = clean.Workload.Requests;
        }

        int points = 0;

        foreach (ObjectFault kind in Kinds)
        {
            for (int at = 1; at <= requests; at++)
            {
                await using Run run = await Run.StartAsync((at, kind));

                try
                {
                    await run.Workload.RunAsync();
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(kind + " at request " + at + " of " + requests + ": " + error.Message, error);
                }

                points++;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("Azurite: " + requests + " requests, " + points + " injection points");
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly AzureBlobStore _store;
        private readonly FaultingHandler _handler;

        private Run(AzureBlobStore store, FaultingHandler handler, ObjectFaultRun workload)
        {
            _store = store;
            _handler = handler;
            Workload = workload;
        }

        public ObjectFaultRun Workload { get; }

        public static async Task<Run> StartAsync((int At, ObjectFault Kind)? fault)
        {
            FaultController faults = new(fault);
            FaultingHandler handler = new(faults);
            using AzureBlobStore created = await Azurite.ContainerAsync();
            Uri container = Azurite.LastContainer!;
            AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = container, Clock = new InstantClock(DateTimeOffset.UtcNow), Handler = handler });
            return new Run(store, handler, new ObjectFaultRun(9301, Steps, faults, store));
        }

        public async ValueTask DisposeAsync()
        {
            await Workload.DisposeAsync();
            _store.Dispose();
            _handler.Dispose();
        }
    }
}
