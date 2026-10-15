// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Varve.Protocol.Client.Hosting;

/// <summary>Varve in an application's services (ADR 0131).</summary>
public static class VarveServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="SparqlHttpClient"/> for the dataset the options
    /// name and, when they carry a setup, the hosted service that applies it as
    /// the host starts (ADR 0130); a setup that cannot be applied fails the
    /// start with its <see cref="SetupException"/>.
    /// </summary>
    public static IServiceCollection AddVarve(this IServiceCollection services, Action<VarveClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        VarveClientOptions options = new();
        configure(options);

        Uri server = options.Server ?? throw new InvalidOperationException("VarveClientOptions.Server is the server's address.");
        string dataset = options.Dataset ?? options.Setup?.Name ?? throw new InvalidOperationException("VarveClientOptions.Dataset names the dataset, or Setup does.");

        if (options.Tokens is not null && options.Clock is null)
        {
            throw new InvalidOperationException("VarveClientOptions.Clock is the clock a token's expiry is measured on, TimeProvider.System in most hosts; the client reads none of its own (ADR 0011).");
        }

        Uri address = new(new Uri(server.AbsoluteUri.EndsWith('/') ? server.AbsoluteUri : server.AbsoluteUri + "/"), "datasets/" + Uri.EscapeDataString(dataset) + "/");
        // One client for the application and the setup alike, made on first
        // use from the provider the container hands each factory: nothing is
        // resolved here, so the container stays the application's to compose.
        Lock gate = new();
        SparqlHttpClient? client = null;

        SparqlHttpClient Client(IServiceProvider provider)
        {
            lock (gate)
            {
                return client ??= options.Tokens is { } tokens
                    ? new SparqlHttpClient(OutboundHttp.CreateClient(), address, options.Limits, tokens(provider), options.Clock!)
                    : new SparqlHttpClient(OutboundHttp.CreateClient(), address, options.Limits);
            }
        }

        services.AddSingleton(Client);

        if (options.Setup is { } setup && options.ApplySetupAtStart)
        {
            services.AddSingleton<IHostedService>(provider => new DatasetSetupService(Client(provider), setup));
        }

        return services;
    }
}

/// <summary>
/// Applies a dataset's setup as the host starts (ADRs 0130, 0131): the same
/// <see cref="SparqlHttpClient.ApplyAsync"/> that <c>varve setup apply</c> and
/// <c>Varve.Aspire</c>'s <c>WithDataset</c> run. A setup that cannot be applied
/// fails the start loudly, with the step, the status and the problem.
/// </summary>
public sealed class DatasetSetupService : IHostedService
{
    private readonly SparqlHttpClient _client;
    private readonly DatasetSetup _setup;

    /// <summary>A service applying <paramref name="setup"/> through <paramref name="client"/>.</summary>
    public DatasetSetupService(SparqlHttpClient client, DatasetSetup setup)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(setup);
        _client = client;
        _setup = setup;
    }

    /// <summary>What the last start applied, or null before it ran.</summary>
    public SetupResult? Result { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Result = await _client.ApplyAsync(_setup, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException unreachable)
        {
            throw new SetupException("dataset", "the server at " + _client.Dataset + " could not be reached: " + unreachable.Message, null, null);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
