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
/// so trailers, aborts and chunking are what a client sees (ADR 0099); an
/// in-memory transport beside it, on request, for a measurement that must not
/// count a socket.
/// </summary>
internal sealed class ProtocolTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly InMemoryTransport? _memory;

    private ProtocolTestHost(WebApplication app, HttpClient client, Uri address, InMemoryTransport? memory)
    {
        _app = app;
        Client = client;
        Address = address;
        _memory = memory;
    }

    /// <summary>A client whose base address is the server's.</summary>
    internal HttpClient Client { get; }

    /// <summary><c>http://127.0.0.1:port/</c>.</summary>
    internal Uri Address { get; }

    /// <summary>
    /// A connection to the server over the in-memory transport, the
    /// application's end of it: write a request to its output, read the
    /// response from its input. Only on a host started with <c>inMemory</c>.
    /// </summary>
    internal ValueTask<System.IO.Pipelines.IDuplexPipe> ConnectInMemoryAsync() =>
        (_memory ?? throw new InvalidOperationException("The host was started without the in-memory transport.")).ConnectAsync();

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
        bool http2 = false,
        TelemetryOptions? telemetry = null,
        CancellationToken stopping = default) =>
        StartAsync(new OneDataset(dataset), map, limits, clock, identity, serviceHandler, accessScopes, loadSource, randomness, null, null, http2, telemetry, stopping: stopping);

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
        IDatasetAdministration? administration = null,
        Func<ISparqlUpdateExecutor, ISparqlUpdateExecutor>? updates = null,
        bool http2 = false,
        TelemetryOptions? telemetry = null,
        bool inMemory = false,
        string? eventSource = null,
        IWriterDirectory? writers = null,
        CancellationToken stopping = default)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        // HTTP/2 over cleartext, for the tests of what only HTTP/2 carries:
        // response trailers (ADR 0095). Kestrel sends none over HTTP/1.1.
        InMemoryTransport? memory = inMemory ? new InMemoryTransport() : null;
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = http2 ? Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2 : Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);

            if (memory is not null)
            {
                kestrel.Listen(memory.EndPoint);
            }
        });

        if (memory is not null)
        {
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Microsoft.AspNetCore.Connections.IConnectionListenerFactory>(builder.Services, memory);
        }

        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        ProtocolLimits bounds = limits ?? ProtocolLimits.Default;
        ISparqlUpdateExecutor executor = new StoreUpdates(Evaluation(clock ?? TimeProvider.System, serviceHandler, randomness, bounds), loadSource);
        ProtocolOptions options = new()
        {
            Datasets = datasets,
            Administration = administration,
            Updates = updates is null ? executor : updates(executor),
            Identity = identity ?? new NoOne(),
            AccessScopes = accessScopes ?? EveryoneEverything.Instance,
            Authorization = new EveryoneMay(),
            Clock = clock ?? TimeProvider.System,
            Limits = bounds,
            Evaluation = Evaluation(clock ?? TimeProvider.System, serviceHandler, randomness, bounds),
            Stopping = stopping,
            Telemetry = telemetry ?? TelemetryOptions.Default,
            EventSource = eventSource,
            Writers = writers,
            Forwarder = writers is null ? null : new System.Net.Http.HttpMessageInvoker(new System.Net.Http.SocketsHttpHandler()),
        };

        if (map is null)
        {
            app.MapGroup("/datasets/{dataset}").MapVarveDataset(options);
            app.MapVarveManagement(options, null);
        }
        else
        {
            map(app, options);
        }

        await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
        string address = ((IApplicationBuilder)app).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        Uri baseAddress = new(address.TrimEnd('/') + "/");
        HttpClient client = new() { BaseAddress = baseAddress };
        return new ProtocolTestHost(app, client, baseAddress, memory);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _memory?.Dispose();
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
    private static EvaluationOptions Evaluation(TimeProvider clock, IServiceHandler? serviceHandler, IRandomSource? randomness, ProtocolLimits limits) =>
        randomness is null
            ? new EvaluationOptions { Clock = clock, ServiceHandler = serviceHandler ?? RefusingServiceHandler.Instance, MemoryBudget = limits.MaxQueryMemory }
            : new EvaluationOptions { Clock = clock, ServiceHandler = serviceHandler ?? RefusingServiceHandler.Instance, Randomness = randomness, MemoryBudget = limits.MaxQueryMemory };

    internal sealed class StoreUpdates(EvaluationOptions evaluation, ILoadSource? loadSource) : ISparqlUpdateExecutor
    {
        // As the server binds it: the client's preconditions passed through and
        // never retried; an inferred one retried three times (ADR 0126).
        public ValueTask<CommitResult> ExecuteAsync(Dataset dataset, Update update, CommitMetadata metadata, UpdatePreconditions preconditions, Varve.Rdf.CallerScope scope, CancellationToken cancellationToken) =>
            SparqlUpdate.ExecuteAsync(dataset, update, loadSource is null
                ? new UpdateOptions
                {
                    Metadata = metadata,
                    ExpectedPosition = preconditions.ExpectedPosition,
                    ExpectedGraphs = Expected(preconditions),
                    ConflictRetries = preconditions.IsImplicit ? 3 : 0,
                    Evaluation = evaluation,
                    ReadScope = scope.Readable,
                    WriteScope = scope.Writable,
                }
                : new UpdateOptions
                {
                    Metadata = metadata,
                    ExpectedPosition = preconditions.ExpectedPosition,
                    ExpectedGraphs = Expected(preconditions),
                    ConflictRetries = preconditions.IsImplicit ? 3 : 0,
                    Evaluation = evaluation,
                    LoadSource = loadSource,
                    ReadScope = scope.Readable,
                    WriteScope = scope.Writable,
                }, cancellationToken);

        private static ExpectedGraphVersion[]? Expected(UpdatePreconditions preconditions) =>
            preconditions.ExpectedGraphs?.Select(g => g.Graph is { } graph ? new ExpectedGraphVersion(graph, g.Version) : new ExpectedGraphVersion(g.Version)).ToArray();
    }
}

/// <summary>
/// A Kestrel transport of in-memory pipes, for a measurement that must see
/// the server's code and not a socket's: its pipes never pause a writer, so a
/// flush completes where it is called whatever the reader does, and its
/// buffers come from a pool that keeps every block it hands out, so how much
/// is in flight never decides what is allocated.
/// </summary>
internal sealed class InMemoryTransport : Microsoft.AspNetCore.Connections.IConnectionListenerFactory, Microsoft.AspNetCore.Connections.IConnectionListenerFactorySelector, IDisposable
{
    private readonly System.Threading.Channels.Channel<Microsoft.AspNetCore.Connections.ConnectionContext> _accepted = System.Threading.Channels.Channel.CreateUnbounded<Microsoft.AspNetCore.Connections.ConnectionContext>();
    private readonly RetainingPool _pool = new();
    private int _connections;

    internal System.Net.EndPoint EndPoint { get; } = new MemoryEndPoint();

    public bool CanBind(System.Net.EndPoint endpoint) => endpoint is MemoryEndPoint;

    public void Dispose() => _pool.Dispose();

    public ValueTask<Microsoft.AspNetCore.Connections.IConnectionListener> BindAsync(System.Net.EndPoint endpoint, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Microsoft.AspNetCore.Connections.IConnectionListener>(new Listener(this, endpoint));

    internal async ValueTask<System.IO.Pipelines.IDuplexPipe> ConnectAsync()
    {
        System.IO.Pipelines.PipeOptions options = new(pool: _pool, readerScheduler: System.IO.Pipelines.PipeScheduler.Inline, writerScheduler: System.IO.Pipelines.PipeScheduler.Inline, pauseWriterThreshold: 0, resumeWriterThreshold: 0, useSynchronizationContext: false);
        System.IO.Pipelines.Pipe toServer = new(options), toClient = new(options);
        Microsoft.AspNetCore.Connections.DefaultConnectionContext connection = new("memory-" + Interlocked.Increment(ref _connections).ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Transport = new Duplex(toServer.Reader, toClient.Writer),
            Application = new Duplex(toClient.Reader, toServer.Writer),
        };
        await _accepted.Writer.WriteAsync(connection).ConfigureAwait(false);
        return connection.Application;
    }

    private sealed class MemoryEndPoint : System.Net.EndPoint
    {
        public override string ToString() => "memory";
    }

    private sealed class Duplex(System.IO.Pipelines.PipeReader input, System.IO.Pipelines.PipeWriter output) : System.IO.Pipelines.IDuplexPipe
    {
        public System.IO.Pipelines.PipeReader Input => input;

        public System.IO.Pipelines.PipeWriter Output => output;
    }

    private sealed class Listener(InMemoryTransport transport, System.Net.EndPoint endpoint) : Microsoft.AspNetCore.Connections.IConnectionListener
    {
        public System.Net.EndPoint EndPoint => endpoint;

        public async ValueTask<Microsoft.AspNetCore.Connections.ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await transport._accepted.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or System.Threading.Channels.ChannelClosedException)
            {
                return null;
            }
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            transport._accepted.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => UnbindAsync();
    }

    // Blocks of 4 KiB, every one kept for reuse once returned.
    private sealed class RetainingPool : System.Buffers.MemoryPool<byte>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Block> _free = new();

        public override int MaxBufferSize => 4096;

        public override System.Buffers.IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(minBufferSize, 4096);
            return _free.TryDequeue(out Block? block) ? block : new Block(this);
        }

        protected override void Dispose(bool disposing)
        {
        }

        private sealed class Block(RetainingPool pool) : System.Buffers.IMemoryOwner<byte>
        {
            private readonly byte[] _array = new byte[4096];

            public Memory<byte> Memory => _array;

            public void Dispose() => pool._free.Enqueue(this);
        }
    }
}
