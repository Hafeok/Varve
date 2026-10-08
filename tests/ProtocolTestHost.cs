// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Store;
using Varve.Store;
using Varve.Store.Log;

namespace Varve;

/// <summary>
/// An in-process server for the protocol tests: Kestrel on a loopback port,
/// every policy allowing everyone, one dataset, and the update executor bound
/// to <c>Varve.Sparql.Store</c> as the server binds it (ADR 0091). Linked into
/// the conformance harness and the protocol tests. Real HTTP on a real socket,
/// so trailers, aborts and chunking are what a client sees (ADR 0099).
/// </summary>
internal sealed class ProtocolTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ProtocolTestHost(WebApplication app, HttpClient client, Uri address)
    {
        _app = app;
        Client = client;
        Address = address;
    }

    /// <summary>A client whose base address is the server's.</summary>
    internal HttpClient Client { get; }

    /// <summary><c>http://127.0.0.1:port/</c>.</summary>
    internal Uri Address { get; }

    /// <summary>
    /// Starts a server for <paramref name="dataset"/>. <paramref name="map"/>
    /// mounts the endpoints; the default is the server's own layout for one
    /// dataset, under <c>/datasets/{dataset}</c>.
    /// </summary>
    internal static Task<ProtocolTestHost> StartAsync(
        Dataset dataset,
        Action<WebApplication, ProtocolOptions>? map = null,
        ProtocolLimits? limits = null,
        TimeProvider? clock = null,
        ICallerIdentity? identity = null,
        IServiceHandler? serviceHandler = null,
        IAccessScopes? accessScopes = null,
        ILoadSource? loadSource = null,
        IRandomSource? randomness = null,
        CancellationToken stopping = default) =>
        StartAsync(new OneDataset(dataset), map, limits, clock, identity, serviceHandler, accessScopes, loadSource, randomness, stopping);

    /// <summary>
    /// Starts a server whose datasets <paramref name="datasets"/> names.
    /// <paramref name="serviceHandler"/>, when given, federates the server's own
    /// queries (ADR 0104's suite run); the default refuses.
    /// </summary>
    internal static async Task<ProtocolTestHost> StartAsync(
        IDatasetResolver datasets,
        Action<WebApplication, ProtocolOptions>? map = null,
        ProtocolLimits? limits = null,
        TimeProvider? clock = null,
        ICallerIdentity? identity = null,
        IServiceHandler? serviceHandler = null,
        IAccessScopes? accessScopes = null,
        ILoadSource? loadSource = null,
        IRandomSource? randomness = null,
        CancellationToken stopping = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        ProtocolOptions options = new()
        {
            Datasets = datasets,
            Updates = new StoreUpdates(Evaluation(clock ?? TimeProvider.System, serviceHandler, randomness), loadSource),
            Identity = identity ?? new NoOne(),
            AccessScopes = accessScopes ?? EveryoneEverything.Instance,
            Authorization = new EveryoneMay(),
            Clock = clock ?? TimeProvider.System,
            Limits = limits ?? ProtocolLimits.Default,
            Evaluation = Evaluation(clock ?? TimeProvider.System, serviceHandler, randomness),
            Stopping = stopping,
        };

        if (map is null)
        {
            app.MapGroup("/datasets/{dataset}").MapVarveDataset(options);
        }
        else
        {
            map(app, options);
        }

        await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
        string address = ((IApplicationBuilder)app).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        Uri baseAddress = new(address.TrimEnd('/') + "/");
        HttpClient client = new() { BaseAddress = baseAddress };
        return new ProtocolTestHost(app, client, baseAddress);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class OneDataset(Dataset dataset) : IDatasetResolver
    {
        public bool TryResolve(DatasetName name, [NotNullWhen(true)] out Dataset? resolved)
        {
            resolved = dataset;
            return true;
        }
    }

    /// <summary>A host with no authentication: every policy allows everyone (ADR 0091).</summary>
    internal sealed class EveryoneMay : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, System.Collections.Generic.IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class NoOne : ICallerIdentity
    {
        public RequestTerm AgentOf(ClaimsPrincipal caller) => RequestTerm.None;
    }

    /// <summary>Datasets by name, added while the server runs, each with the scope every caller gets on it (ADR 0107's properties).</summary>
    internal sealed class Datasets : IDatasetResolver, IAsyncDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dataset> _datasets = new(StringComparer.Ordinal);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Varve.Rdf.CallerScope> _scopes = new(StringComparer.Ordinal);

        internal void Add(string name, Dataset dataset) => _datasets[name] = dataset;

        internal void Add(string name, Dataset dataset, Varve.Rdf.CallerScope scope)
        {
            _datasets[name] = dataset;
            _scopes[name] = scope;
        }

        internal void Remove(string name)
        {
            _datasets.TryRemove(name, out _);
            _scopes.TryRemove(name, out _);
        }

        internal Varve.Rdf.CallerScope? ScopeOf(string name) => _scopes.TryGetValue(name, out Varve.Rdf.CallerScope? scope) ? scope : null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public bool TryResolve(DatasetName name, [NotNullWhen(true)] out Dataset? dataset) => _datasets.TryGetValue(name.Value, out dataset);
    }

    /// <summary>The update executor, as the server binds it: no retries, the expected position and the scope passed through.</summary>
    // Randomness is left unset when none is given: the options' own default
    // refuses RAND() and UUID(), as before.
    private static EvaluationOptions Evaluation(TimeProvider clock, IServiceHandler? serviceHandler, IRandomSource? randomness) =>
        randomness is null
            ? new EvaluationOptions { Clock = clock, ServiceHandler = serviceHandler ?? RefusingServiceHandler.Instance }
            : new EvaluationOptions { Clock = clock, ServiceHandler = serviceHandler ?? RefusingServiceHandler.Instance, Randomness = randomness };

    internal sealed class StoreUpdates(EvaluationOptions evaluation, ILoadSource? loadSource) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, Position? expectedPosition, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, loadSource is null
                ? new UpdateOptions
                {
                    Metadata = metadata,
                    ExpectedPosition = expectedPosition,
                    Evaluation = evaluation,
                    ReadScope = scope.Readable,
                    WriteScope = scope.Writable,
                }
                : new UpdateOptions
                {
                    Metadata = metadata,
                    ExpectedPosition = expectedPosition,
                    Evaluation = evaluation,
                    LoadSource = loadSource,
                    ReadScope = scope.Readable,
                    WriteScope = scope.Writable,
                }, cancellationToken);
    }
}
