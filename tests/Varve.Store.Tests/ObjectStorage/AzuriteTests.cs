// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The adapter against Azurite: <c>VARVE_AZURITE</c> is the account's address,
/// <c>http://127.0.0.1:10000/devstoreaccount1</c> (ADR 0134).
/// </summary>
internal static class Azurite
{
    public static string Account() => ServiceLegs.Require("azurite", "VARVE_AZURITE").TrimEnd('/');

    /// <summary>A fresh container, created with the development key.</summary>
    public static async Task<AzureBlobStore> ContainerAsync() =>
        AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = await NewContainerAsync(), Clock = TimeProvider.System });

    /// <summary>
    /// A fresh container's address, for a test that opens it with a handler
    /// of its own. Returned, never held in a static: test classes run in
    /// parallel, and one's container must not be read as another's.
    /// </summary>
    public static async Task<Uri> NewContainerAsync()
    {
        Uri container = new(Account() + "/" + ServiceLegs.Run());
        using HttpClient http = new();
        using DevelopmentKeySigner signer = new(TimeProvider.System);
        using HttpRequestMessage create = new(HttpMethod.Put, new Uri(container + "?restype=container"));
        create.Headers.Add("x-ms-version", "2025-11-05");
        await signer.SignAsync(create, T.Ct);
        using HttpResponseMessage response = await http.SendAsync(create, T.Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return container;
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteExtentMaintenanceTests
{
    /// <summary>A follower keeps serving across the writer's merges and deletions, on Azurite (ADRs 0158, 0160).</summary>
    [Fact]
    public async Task a_follower_keeps_serving_across_merges_and_deletions_on_azurite()
    {
        using AzureBlobStore store = await Azurite.ContainerAsync();
        await ExtentMaintenanceCases.FollowerAcrossMergesAndDeletionsAsync(store);
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteObjectStoreContractTests
{
    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task azurite_keeps_the_object_store_contract(string name)
    {
        using AzureBlobStore store = await Azurite.ContainerAsync();
        await ObjectStoreContractCases.All().Single(c => c.Name == name).Run(store, "contract/", T.Ct);
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteStorageContractTests : StorageContractTests, IAsyncLifetime
{
    private readonly List<(AzureBlobStore Store, Backend Storage, string Cache)> _opened = [];

    protected override IStorage Create() => throw new NotSupportedException("Opened asynchronously.");

    protected override async ValueTask<IStorage> CreateAsync()
    {
        AzureBlobStore store = await Azurite.ContainerAsync();
        string cache = Directory.CreateTempSubdirectory("varve-azurite-").FullName;
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
        foreach ((AzureBlobStore store, Backend storage, string cache) in _opened)
        {
            await storage.DisposeAsync();
            store.Dispose();
            Directory.Delete(cache, recursive: true);
        }
    }
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteAdapterTests
{
    [Fact]
    public async Task a_create_that_finds_a_blob_is_409_blob_already_exists_and_reads_as_a_lost_race()
    {
        // ADR 0134 states the 409 from Azurite's source; this pins it.
        using AzureBlobStore store = await Azurite.ContainerAsync();
        ObjectName name = new("x");
        Assert.NotNull(await store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
        Assert.Null(await store.CreateIfAbsentAsync(name, new byte[] { 2 }, T.Ct));
    }

    /// <summary>
    /// What Azurite answers the conditional writes, beside what the service
    /// answers (#143, `AzureAccountConditionalWriteTests`): the same three,
    /// a replace of no blob included, which is 412 ConditionNotMet on both
    /// and never the 404 ADR 0134's decision 5 also maps.
    /// </summary>
    [Fact]
    public async Task azurite_answers_409_to_a_create_that_finds_a_blob_and_412_to_a_stale_replace_and_to_a_replace_of_nothing()
    {
        using RecordingHandler recording = new();
        using AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = await Azurite.NewContainerAsync(), Clock = TimeProvider.System, Handler = recording });
        ObjectName name = new("a");
        ObjectVersion v1 = Assert.IsType<ObjectVersion>(await store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
        Assert.Null(await store.CreateIfAbsentAsync(name, new byte[] { 2 }, T.Ct));
        ObjectVersion v2 = Assert.IsType<ObjectVersion>(await store.ReplaceIfUnchangedAsync(name, v1, new byte[] { 3 }, T.Ct));
        Assert.Null(await store.ReplaceIfUnchangedAsync(name, v1, new byte[] { 4 }, T.Ct));
        Assert.Null(await store.ReplaceIfUnchangedAsync(new ObjectName("none"), v2, new byte[] { 5 }, T.Ct));

        var puts = recording.Exchanges.Where(e => e.Method == "PUT").ToList();
        Assert.Equal([201, 409, 201, 412, 412], puts.Select(p => p.Status));
        Assert.Equal(["BlobAlreadyExists", "ConditionNotMet", "ConditionNotMet"], puts.Where(p => p.Code is not null).Select(p => p.Code));
    }

    [Fact]
    public async Task a_list_pages_past_five_thousand_names()
    {
        using AzureBlobStore store = await Azurite.ContainerAsync();
        await Task.WhenAll(Enumerable.Range(0, 5100).Chunk(200).Select(async batch =>
        {
            foreach (int i in batch)
            {
                await store.CreateIfAbsentAsync(new ObjectName("p/" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)), new byte[] { 1 }, T.Ct);
            }
        }));

        IReadOnlyList<ObjectEntry> all = await store.ListAsync(new ObjectName("p/"), T.Ct);
        Assert.Equal(5100, all.Count);
        Assert.Equal(all.Select(e => e.Name.Value).Order(StringComparer.Ordinal), all.Select(e => e.Name.Value));
    }

    [Fact]
    public void the_development_key_signs_only_azurite_on_a_loopback_address()
    {
        Assert.True(AzureBlobStore.IsDevelopmentStorage(new Uri("http://127.0.0.1:10000/devstoreaccount1/c")));
        Assert.True(AzureBlobStore.IsDevelopmentStorage(new Uri("http://localhost:10000/devstoreaccount1/c")));
        Assert.False(AzureBlobStore.IsDevelopmentStorage(new Uri("https://devstoreaccount1.blob.core.windows.net/c")));
        Assert.False(AzureBlobStore.IsDevelopmentStorage(new Uri("http://10.0.0.5:10000/devstoreaccount1/c")));
        Assert.False(AzureBlobStore.IsDevelopmentStorage(new Uri("http://127.0.0.1:10000/someaccount/c")));
    }

    [Fact]
    public void a_container_address_with_a_query_is_refused_without_echoing_it()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => AzureBlobStore.Create(new AzureBlobStoreOptions
        {
            ContainerUrl = new Uri("https://acct.blob.core.windows.net/c?sv=2025-11-05&sig=SECRETSIGNATURE"),
            Clock = TimeProvider.System,
        }));
        Assert.DoesNotContain("SECRETSIGNATURE", refused.Message, StringComparison.Ordinal);
        Assert.Contains("ADR 0137", refused.Message, StringComparison.Ordinal);
    }
}

/// <summary>The writer fence of ADR 0136 on Azurite: the cases every store runs.</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteWriterFenceTests
{
    public static TheoryData<string> Cases() => [.. WriterFenceCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task azurite_fences_a_superseded_writer(string name)
    {
        using AzureBlobStore store = await Azurite.ContainerAsync();
        await WriterFenceCases.All().Single(c => c.Name == name).Run(store, null, TimeProvider.System);
    }
}
