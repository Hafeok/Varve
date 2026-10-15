// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Varve.Protocol.Client;

// A dataset's domain events, published to Azure Event Hubs (ADRs 0143, 0146).
namespace Varve.EventHubs.Sample;

internal static class Program
{
    private static Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddHostedService<BridgeWorker>();
        return builder.Build().RunAsync();
    }
}

/// <summary>
/// The bridge as a hosted service. <c>Varve:Dataset</c> is the dataset's
/// address, <c>EventHubs:Connection</c> and <c>EventHubs:Hub</c> the hub's,
/// and <c>Bridge:Checkpoint</c> the file the position is kept in.
/// </summary>
internal sealed class BridgeWorker(IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Uri dataset = new(configuration["Varve:Dataset"] ?? throw new InvalidOperationException("Varve:Dataset is the dataset's address."));
        string connection = configuration["EventHubs:Connection"] ?? throw new InvalidOperationException("EventHubs:Connection is the hub's connection string.");
        string hub = configuration["EventHubs:Hub"] ?? throw new InvalidOperationException("EventHubs:Hub is the hub's name.");
        string checkpoint = configuration["Bridge:Checkpoint"] ?? "bridge.checkpoint";

        using HttpClient http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        SparqlHttpClient varve = new(http, dataset, ClientLimits.Default);
        await using EventHubProducerClient producer = new(connection, hub);

        // The partition key is the dataset's name: the last segment of its address.
        string name = dataset.AbsolutePath.TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];

        EventBridge bridge = new(varve, producer, name);
        await bridge.RunAsync(new FileFollowCheckpoint(checkpoint), new FollowOptions(TimeProvider.System), stoppingToken).ConfigureAwait(false);
    }
}
