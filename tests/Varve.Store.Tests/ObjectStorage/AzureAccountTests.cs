// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A real storage account (ADR 0142's optional leg): the contract suite and
/// the storage contract against the container <c>VARVE_AZURE_CONTAINER</c>
/// names, each run under a prefix of its own, reached as the workload's
/// identity — in CI, GitHub's OIDC token as a federated credential through
/// <c>AZURE_FEDERATED_TOKEN_FILE</c> (ADR 0137). No key is involved.
/// </summary>
internal static class AzureAccount
{
    public static AzureBlobStore Open() =>
        AzureBlobStore.Create(new AzureBlobStoreOptions
        {
            ContainerUrl = new Uri(ServiceLegs.Require("azure", "VARVE_AZURE_CONTAINER")),
            Clock = TimeProvider.System,
        });
}

public sealed class AzureAccountObjectStoreContractTests
{
    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task a_storage_account_keeps_the_object_store_contract(string name)
    {
        using AzureBlobStore store = AzureAccount.Open();
        string prefix = ServiceLegs.Run() + "/";

        try
        {
            await ObjectStoreContractCases.All().Single(c => c.Name == name).Run(store, prefix, T.Ct);
        }
        finally
        {
            foreach (ObjectEntry entry in await store.ListAsync(new ObjectName(prefix), T.Ct))
            {
                await store.DeleteAsync(entry.Name, T.Ct);
            }
        }
    }
}

public sealed class AzureAccountStorageContractTests : StorageContractTests, IAsyncLifetime
{
    private readonly List<(AzureBlobStore Store, Backend Storage, string Cache, ObjectName Prefix)> _opened = [];

    protected override IStorage Create() => throw new NotSupportedException("Opened asynchronously.");

    protected override async ValueTask<IStorage> CreateAsync()
    {
        AzureBlobStore store = AzureAccount.Open();
        string cache = Directory.CreateTempSubdirectory("varve-azure-").FullName;
        ObjectName prefix = new(ServiceLegs.Run() + "/");
        Backend storage = await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Prefix = prefix,
            Cache = new CacheDirectory(cache),
            Instance = new WriterInstance("contract"),
            Clock = TimeProvider.System,
        }, T.Ct);
        _opened.Add((store, storage, cache, prefix));
        return storage;
    }

    protected override Durability Expected => Durability.Replicated;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach ((AzureBlobStore store, Backend storage, string cache, ObjectName prefix) in _opened)
        {
            await storage.DisposeAsync();

            foreach (ObjectEntry entry in await store.ListAsync(prefix, T.Ct))
            {
                await store.DeleteAsync(entry.Name, T.Ct);
            }

            store.Dispose();
            Directory.Delete(cache, recursive: true);
        }
    }
}
