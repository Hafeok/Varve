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
    internal static async Task<ProtocolTestHost> StartAsync(
        Dataset dataset,
        Action<WebApplication, ProtocolOptions>? map = null,
        ProtocolLimits? limits = null,
        TimeProvider? clock = null,
        CancellationToken stopping = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        ProtocolOptions options = new()
        {
            Datasets = new OneDataset(dataset),
            Updates = new StoreUpdates(clock ?? TimeProvider.System),
            Identity = new NoOne(),
            Authorization = new EveryoneMay(),
            Clock = clock ?? TimeProvider.System,
            Limits = limits ?? ProtocolLimits.Default,
            Evaluation = new EvaluationOptions { Clock = clock ?? TimeProvider.System },
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

    /// <summary>The update executor, as the server binds it: no retries, the expected position passed through.</summary>
    internal sealed class StoreUpdates(TimeProvider clock) : ISparqlUpdateExecutor
    {
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, Position? expectedPosition, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, new UpdateOptions
            {
                Metadata = metadata,
                ExpectedPosition = expectedPosition,
                Evaluation = new EvaluationOptions { Clock = clock },
            }, cancellationToken);
    }
}
