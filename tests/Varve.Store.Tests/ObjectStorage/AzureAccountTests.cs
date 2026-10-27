// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A real storage account (ADR 0134's optional leg; #143): the suites that
/// run against Azurite run here too, unchanged, each run under a prefix of
/// its own in the container, and nothing outside that prefix is listed,
/// written or deleted.
/// </summary>
/// <remarks>
/// <para>
/// The container is <c>VARVE_AZURE_CONTAINER</c>, a container URL, or else
/// <c>VARVE_AZURE_BLOB_ENDPOINT</c> with <c>VARVE_AZURE_BLOB_CONTAINER</c>
/// (<c>varve-test</c> unless set). Without either the leg is skipped. Every
/// class here carries <c>Category=AzureObjects</c>, or
/// <c>Category=AzureObjectsFaults</c> for the failure-injection workload
/// (<see cref="ObjectTraits"/>), so one command runs the leg and nothing
/// else does: <c>dotnet test --project
/// tests/Varve.Store.Tests/Varve.Store.Tests.csproj -- --filter-trait
/// Category=AzureObjects</c>, and the same with <c>AzureObjectsFaults</c>.
/// </para>
/// <para>
/// The identity is the workload's and never a key (ADR 0137). On a
/// workstation, <c>VARVE_AZURE_TENANT_ID</c> pins the Azure CLI's token to
/// one tenant: the fixture runs <c>az account get-access-token --tenant</c>
/// itself, so a sign-in to another tenant in the same profile is never
/// picked up, and the CLI honours <c>AZURE_CONFIG_DIR</c>. Without it the
/// adapter's own sources apply, which is how CI reaches the account as a
/// federated credential (<c>AZURE_FEDERATED_TOKEN_FILE</c>). The production
/// path is untouched: only this fixture pins.
/// </para>
/// </remarks>
internal static class AzureAccount
{
    /// <summary>This run's prefix in the container: the UTC time it started and a short random part.</summary>
    public static string RunPrefix { get; } =
        DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + "/";

    private static readonly Lock Gate = new();
    private static readonly HttpClient TokenHttp = new();
    private static CachedToken? _token;

    /// <summary>The container's address, or a skip when the leg is not configured.</summary>
    public static Uri Container()
    {
        string? whole = Environment.GetEnvironmentVariable("VARVE_AZURE_CONTAINER");

        if (!string.IsNullOrEmpty(whole))
        {
            return new Uri(whole.TrimEnd('/'));
        }

        string endpoint = ServiceLegs.Require("azure", "VARVE_AZURE_BLOB_ENDPOINT").TrimEnd('/');
        string container = Environment.GetEnvironmentVariable("VARVE_AZURE_BLOB_CONTAINER") is { Length: > 0 } named ? named : "varve-test";
        return new Uri(endpoint + "/" + container);
    }

    /// <summary>A test's own prefix under the run's.</summary>
    public static ObjectName Prefix(string test) => new(RunPrefix + test + "/");

    /// <summary>
    /// The adapter over the container, through <paramref name="handler"/>
    /// when a test puts one in the pipeline (the caller disposes it), signed
    /// with the process's one cached token.
    /// </summary>
    public static AzureBlobStore Open(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        Uri container = Container();
        clock ??= TimeProvider.System;
        HttpClient http = new(Traced(handler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }), disposeHandler: handler is null)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        return new AzureBlobStore(new AzureBlobStoreOptions { ContainerUrl = container, Clock = clock }, new SharedBearerSigner(Token()), http);
    }

    /// <summary>
    /// The handler a store's requests go through: the caller's, under a
    /// tracing one when <c>VARVE_AZURE_TRACE</c> names a file, which then
    /// gets one line per exchange (time, method, path under the container,
    /// condition, status, error code, ETag; never a token).
    /// </summary>
    private static HttpMessageHandler Traced(HttpMessageHandler handler) =>
        Environment.GetEnvironmentVariable("VARVE_AZURE_TRACE") is { Length: > 0 } file ? new TracingHandler(handler, file) : handler;

    private sealed class TracingHandler(HttpMessageHandler inner, string file) : DelegatingHandler(inner)
    {
        private static readonly Lock Writing = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string condition = request.Headers.TryGetValues("If-Match", out IEnumerable<string>? match) ? "If-Match:" + string.Join(",", match)
                : request.Headers.TryGetValues("If-None-Match", out IEnumerable<string>? none) ? "If-None-Match:" + string.Join(",", none) : "-";
            long started = Stopwatch.GetTimestamp();
            string line;

            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                string code = response.Headers.TryGetValues("x-ms-error-code", out IEnumerable<string>? codes) ? string.Join(",", codes) : "-";
                line = string.Join("\t", DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), request.Method.Method, request.RequestUri!.PathAndQuery, condition, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), code, response.Headers.ETag?.Tag ?? "-", Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + "ms");
                Write(line);
                return response;
            }
            catch (Exception error)
            {
                Write(string.Join("\t", DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), request.Method.Method, request.RequestUri!.PathAndQuery, condition, "threw", error.GetType().Name));
                throw;
            }
        }

        private void Write(string line)
        {
            lock (Writing)
            {
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
    }

    /// <summary>Deletes every blob under <paramref name="prefix"/>, and nothing else.</summary>
    public static async Task DeleteUnderAsync(IObjectStore store, ObjectName prefix)
    {
        foreach (ObjectEntry entry in await store.ListAsync(prefix, T.Ct))
        {
            await store.DeleteAsync(entry.Name, T.Ct);
        }
    }

    /// <summary>A writer's or follower's options over the container, each open with a cache directory of its own.</summary>
    public static ObjectStorageOptions Options(IObjectStore store, ObjectName? prefix, string instance, ObjectStorageAccess access = ObjectStorageAccess.Write)
    {
        return new ObjectStorageOptions
        {
            Store = store,
            Prefix = prefix,
            Cache = new CacheDirectory(Directory.CreateTempSubdirectory("varve-azure-" + instance + "-").FullName),
            Access = access,
            Instance = new WriterInstance(instance),
            Clock = TimeProvider.System,
            ProbeRandomness = TestRandomness.Instance,
        };
    }

    private static CachedToken Token()
    {
        lock (Gate)
        {
            if (_token is null)
            {
                string? tenant = Environment.GetEnvironmentVariable("VARVE_AZURE_TENANT_ID");
                ITokenSource source = string.IsNullOrEmpty(tenant)
                    ? TokenSources.FromEnvironment(Environment.GetEnvironmentVariable, TokenHttp, TimeProvider.System)
                    : new TokenSources.AzureCli(cancellationToken => AzureCliAsync(tenant, cancellationToken));
                _token = new CachedToken(source, TimeProvider.System);
            }

            return _token;
        }
    }

    // The adapter's own CLI source, with the tenant named: a profile signed
    // in to several tenants answers for this one only.
    private static async ValueTask<string> AzureCliAsync(string tenant, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("az")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in new[] { "account", "get-access-token", "--resource", TokenSources.Resource, "--tenant", tenant, "--output", "json" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new IOException("az could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new IOException("az account get-access-token failed (" + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "): " + (await error).Trim());
        }

        return await output;
    }

    /// <summary>The bearer signer over the process's one token, which no store's disposal releases.</summary>
    private sealed class SharedBearerSigner(CachedToken token) : IRequestSigner
    {
        public async ValueTask SignAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AccessToken held = await token.GetAsync(cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", held.Value);
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// A store seen under a prefix: every name a caller gives is put under it,
/// every name listed has it taken off. It is how a suite written for a
/// container of its own runs under a run's prefix in a shared one, through
/// the real adapter beneath.
/// </summary>
internal sealed class PrefixedObjectStore(IObjectStore inner, ObjectName prefix) : IObjectStore
{
    public ObjectName Prefix => prefix;

    public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        inner.CreateIfAbsentAsync(Under(name), content, cancellationToken);

    public ValueTask<ObjectVersion?> CreateIfAbsentAsync(ObjectName name, ObjectContent content, CancellationToken cancellationToken) =>
        inner.CreateIfAbsentAsync(Under(name), content, cancellationToken);

    public ValueTask<ObjectVersion?> ReplaceIfUnchangedAsync(ObjectName name, ObjectVersion expected, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
        inner.ReplaceIfUnchangedAsync(Under(name), expected, content, cancellationToken);

    public ValueTask<ObjectRead?> ReadAsync(ObjectName name, ByteOffset offset, ByteCount length, CancellationToken cancellationToken) =>
        inner.ReadAsync(Under(name), offset, length, cancellationToken);

    public async ValueTask<IReadOnlyList<ObjectEntry>> ListAsync(ObjectName under, CancellationToken cancellationToken)
    {
        IReadOnlyList<ObjectEntry> listed = await inner.ListAsync(Under(under), cancellationToken);
        return [.. listed.Select(entry => new ObjectEntry(new ObjectName(entry.Name.Value[prefix.Value.Length..]), entry.Length, entry.Version))];
    }

    public ValueTask<bool> DeleteAsync(ObjectName name, CancellationToken cancellationToken) => inner.DeleteAsync(Under(name), cancellationToken);

    public override string ToString() => inner + "/" + prefix.Value;

    private ObjectName Under(ObjectName name) => new(prefix.Value + name.Value);
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzureAccountObjectStoreContractTests
{
    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task a_storage_account_keeps_the_object_store_contract(string name)
    {
        using AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("contract-" + ServiceLegs.Run());

        try
        {
            await ObjectStoreContractCases.All().Single(c => c.Name == name).Run(store, prefix.Value, T.Ct);
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzureAccountStorageContractTests : StorageContractTests, IAsyncLifetime
{
    private readonly List<(AzureBlobStore Store, Backend Storage, string Cache, ObjectName Prefix)> _opened = [];

    protected override IStorage Create() => throw new NotSupportedException("Opened asynchronously.");

    protected override async ValueTask<IStorage> CreateAsync()
    {
        AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("storage-" + ServiceLegs.Run());
        ObjectStorageOptions options = AzureAccount.Options(store, prefix, "contract");
        Backend storage = await Backend.OpenAsync(options, T.Ct);
        _opened.Add((store, storage, options.Cache.Value, prefix));
        return storage;
    }

    protected override Durability Expected => Durability.Replicated;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach ((AzureBlobStore store, Backend storage, string cache, ObjectName prefix) in _opened)
        {
            await storage.DisposeAsync();
            await AzureAccount.DeleteUnderAsync(store, prefix);
            store.Dispose();
            Directory.Delete(cache, recursive: true);
        }
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzureAccountExtentMaintenanceTests
{
    /// <summary>A follower keeps serving across the writer's merges and deletions, on the account (ADRs 0158, 0160).</summary>
    [Fact]
    public async Task a_follower_keeps_serving_across_merges_and_deletions_on_a_storage_account()
    {
        using AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("maintenance");

        try
        {
            await ExtentMaintenanceCases.FollowerAcrossMergesAndDeletionsAsync(new PrefixedObjectStore(store, prefix));
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }
}

/// <summary>The writer fence of ADR 0136 against the account: the cases every store runs.</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzureAccountWriterFenceTests
{
    public static TheoryData<string> Cases() => [.. WriterFenceCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task a_storage_account_fences_a_superseded_writer(string name)
    {
        using AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("fence-" + ServiceLegs.Run());

        try
        {
            await WriterFenceCases.All().Single(c => c.Name == name).Run(store, prefix, TimeProvider.System);
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }
}

/// <summary>
/// The failure-injection workload over HTTP against the account: a 500, a
/// 503, a 429, a timeout and a lost response, each at every
/// <c>VARVE_AZURE_FAULT_STRIDE</c>-th request (every seventh unless set; 1 is
/// every request, as the Azurite gate runs it). A round trip to the account
/// is tens of milliseconds where Azurite's is one, and the suite's cost is
/// quadratic in the workload's requests.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class AzureAccountFaultTests
{
    private static int Stride =>
        int.TryParse(Environment.GetEnvironmentVariable("VARVE_AZURE_FAULT_STRIDE"), NumberStyles.None, CultureInfo.InvariantCulture, out int stride) && stride > 0 ? stride : 7;

    [Fact]
    public async Task after_a_fault_at_a_request_to_the_account_the_dataset_reopens_with_nothing_lost_or_doubled()
    {
        using AzureBlobStore cleaner = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("faults");

        try
        {
            await HttpFaultSuite.EveryRequestAsync("storage account", Stride, (handler, run) =>
            {
                AzureBlobStore store = AzureAccount.Open(handler);
                IObjectStore under = new PrefixedObjectStore(store, new ObjectName(prefix.Value + run.ToString(CultureInfo.InvariantCulture) + "/"));
                return Task.FromResult((under, (IDisposable)store));
            });
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(cleaner, prefix);
        }
    }
}
