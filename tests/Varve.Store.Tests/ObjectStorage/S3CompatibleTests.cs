// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The S3-compatible server CI starts on a loopback address with a test key,
/// RustFS, which the contract suite chose (ADR 0134, decision 6):
/// its variable holds <c>endpoint,accessKeyId,secret</c> (ADR 0134, decision 6;
/// ADR 0137, LoopbackDevelopmentKey).
/// </summary>
internal sealed class S3Server(string leg, string variable)
{
    public static S3Server RustFs { get; } = new("rustfs", "VARVE_RUSTFS");

    /// <summary>A fresh bucket on the server, and the store over it.</summary>
    public async Task<S3ObjectStore> BucketAsync()
    {
        (Uri endpoint, S3Bucket bucket, S3DevelopmentKey key) = await NewBucketAsync();

        return S3ObjectStore.Create(new S3ObjectStoreOptions
        {
            Endpoint = endpoint,
            Bucket = bucket,
            DevelopmentKey = key,
            Clock = TimeProvider.System,
        });
    }

    /// <summary>A fresh bucket on the server, made with its test key.</summary>
    public async Task<(Uri Endpoint, S3Bucket Bucket, S3DevelopmentKey Key)> NewBucketAsync()
    {
        string[] parts = ServiceLegs.Require(leg, variable).Split(',');
        Uri endpoint = new(parts[0]);
        S3Bucket bucket = new(ServiceLegs.Run());

        using (HttpClient http = new())
        using (HttpRequestMessage create = new(HttpMethod.Put, new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/" + bucket.Value)))
        {
            SigV4.Sign(create, [], new SigningCredentials(parts[1], parts[2], null, DateTimeOffset.MaxValue), S3Region.Default.Value, DateTimeOffset.UtcNow);
            using HttpResponseMessage response = await http.SendAsync(create, T.Ct);
            Assert.True(response.IsSuccessStatusCode, leg + " refused CreateBucket with " + (int)response.StatusCode + ": " + await response.Content.ReadAsStringAsync(T.Ct));
        }

        return (endpoint, bucket, new S3DevelopmentKey(parts[1], parts[2]));
    }
}

public abstract class S3CompatibleObjectStoreContractTests
{
    internal abstract S3Server Server { get; }

    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task the_server_keeps_the_object_store_contract(string name)
    {
        using S3ObjectStore store = await Server.BucketAsync();
        await ObjectStoreContractCases.All().Single(c => c.Name == name).Run(store, "contract/", T.Ct);
    }
}

public abstract class S3CompatibleStorageContractTests : StorageContractTests, IAsyncLifetime
{
    private readonly List<(S3ObjectStore Store, Backend Storage, string Cache)> _opened = [];

    internal abstract S3Server Server { get; }

    protected override IStorage Create() => throw new NotSupportedException("Opened asynchronously.");

    protected override async ValueTask<IStorage> CreateAsync()
    {
        S3ObjectStore store = await Server.BucketAsync();
        string cache = Directory.CreateTempSubdirectory("varve-s3-").FullName;
        Backend storage = await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Cache = new CacheDirectory(cache),
            Instance = new WriterInstance("contract"),
            Clock = TimeProvider.System,
            ProbeRandomness = TestRandomness.Instance,
        }, T.Ct);
        _opened.Add((store, storage, cache));
        return storage;
    }

    protected override Durability Expected => Durability.Replicated;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        foreach ((S3ObjectStore store, Backend storage, string cache) in _opened)
        {
            await storage.DisposeAsync();
            store.Dispose();
            Directory.Delete(cache, recursive: true);
        }
    }
}

public sealed class RustFsExtentMaintenanceTests
{
    /// <summary>A follower keeps serving across the writer's merges and deletions, on RustFS (ADRs 0158, 0160).</summary>
    [Fact]
    public async Task a_follower_keeps_serving_across_merges_and_deletions_on_rustfs()
    {
        using S3ObjectStore store = await S3Server.RustFs.BucketAsync();
        await ExtentMaintenanceCases.FollowerAcrossMergesAndDeletionsAsync(store);
    }
}

public sealed class RustFsObjectStoreContractTests : S3CompatibleObjectStoreContractTests
{
    internal override S3Server Server => S3Server.RustFs;
}

public sealed class RustFsStorageContractTests : S3CompatibleStorageContractTests
{
    internal override S3Server Server => S3Server.RustFs;
}
