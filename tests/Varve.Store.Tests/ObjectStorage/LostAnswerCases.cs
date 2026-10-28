// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
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
/// A conditional write the store applied and whose answer the caller never
/// heard (#145; ADR 0136's amendment of 2026-10-23): the reader record's
/// replace, cancelled by its own caller or timed out by the client after the
/// service applied it. The instance's next announcement must recognise its
/// own write and go on, never declare itself forgotten. For a named writer
/// and for a holder-less follower, the server's read-only open, which writes
/// its reader record and nothing else. Over the real HTTP adapter, so that
/// the fault is the one the account showed.
/// </summary>
internal static class LostAnswerCases
{
    public static (string Name, Func<Func<FaultingHandler, (IObjectStore Store, IDisposable Owner)>, Task> Run)[] All() =>
    [
        ("a writer whose reader-record replace was applied and then cancelled announces again as itself", (open) => AppliedThenCancelled(open, follower: false)),
        ("a holder-less follower whose reader-record replace was applied and then cancelled announces again as itself", (open) => AppliedThenCancelled(open, follower: true)),
        ("a writer whose reader-record replace was applied and then timed out announces within the same call", (open) => AppliedThenTimedOut(open, follower: false)),
        ("a holder-less follower whose reader-record replace was applied and then timed out announces within the same call", (open) => AppliedThenTimedOut(open, follower: true)),
    ];

    /// <summary>The reader record's replace: a PUT with If-Match under readers/.</summary>
    public static bool IsReaderRecordReplace(HttpRequestMessage request) =>
        request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath.Contains("/readers/", StringComparison.Ordinal) && request.Headers.Contains("If-Match");

    private static async Task AppliedThenCancelled(Func<FaultingHandler, (IObjectStore Store, IDisposable Owner)> open, bool follower)
    {
        using FaultController faults = new((1, ObjectFault.HttpAppliedThenCancelled)) { Target = IsReaderRecordReplace };
        using FaultingHandler handler = new(faults);
        (IObjectStore store, IDisposable owner) = open(handler);
        using IDisposable _ = owner;
        string cache = Directory.CreateTempSubdirectory("varve-lost-").FullName;

        try
        {
            if (follower)
            {
                await (await Backend.OpenAsync(Options(store, "creator", cache + "-creator", follower: false), T.Ct)).DisposeAsync();
            }

            Backend storage = await Backend.OpenAsync(Options(store, follower ? null : "writer", cache, follower), T.Ct);
            await storage.AnnounceAsync(new CatalogVersion(1), new CatalogVersion(1), T.Ct);

            faults.Armed = true;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await storage.AnnounceAsync(new CatalogVersion(2), new CatalogVersion(2), faults.Token));
            Assert.True(faults.Fired);
            faults.Armed = false;
            Assert.Equal(new CatalogVersion(2), Assert.Single(await Backend.ReadReadersAsync(store, null, T.Ct)).Newest);

            // Before the fix: ReaderForgottenException, over a record nobody touched.
            await storage.AnnounceAsync(new CatalogVersion(3), new CatalogVersion(3), T.Ct);
            ReaderRecord record = Assert.Single(await Backend.ReadReadersAsync(store, null, T.Ct));
            Assert.Equal(new CatalogVersion(3), record.Newest);
            Assert.Equal(follower ? null : new WriterInstance("writer"), record.Holder);
            await storage.DisposeAsync();
            Assert.Empty(await Backend.ReadReadersAsync(store, null, T.Ct));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task AppliedThenTimedOut(Func<FaultingHandler, (IObjectStore Store, IDisposable Owner)> open, bool follower)
    {
        using FaultController faults = new((1, ObjectFault.HttpAppliedThenTimedOut)) { Target = IsReaderRecordReplace };
        using FaultingHandler handler = new(faults);
        (IObjectStore store, IDisposable owner) = open(handler);
        using IDisposable _ = owner;
        string cache = Directory.CreateTempSubdirectory("varve-lost-").FullName;

        try
        {
            if (follower)
            {
                await (await Backend.OpenAsync(Options(store, "creator", cache + "-creator", follower: false), T.Ct)).DisposeAsync();
            }

            Backend storage = await Backend.OpenAsync(Options(store, follower ? null : "writer", cache, follower), T.Ct);
            await storage.AnnounceAsync(new CatalogVersion(1), new CatalogVersion(1), T.Ct);

            // A timeout is not the caller's cancellation: the replace helper retries, reads the record back and recognises its own write.
            faults.Armed = true;
            await storage.AnnounceAsync(new CatalogVersion(2), new CatalogVersion(2), T.Ct);
            Assert.True(faults.Fired);
            faults.Armed = false;
            Assert.Equal(new CatalogVersion(2), Assert.Single(await Backend.ReadReadersAsync(store, null, T.Ct)).Newest);
            await storage.AnnounceAsync(new CatalogVersion(3), new CatalogVersion(3), T.Ct);
            Assert.Equal(new CatalogVersion(3), Assert.Single(await Backend.ReadReadersAsync(store, null, T.Ct)).Newest);
            await storage.DisposeAsync();
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    private static ObjectStorageOptions Options(IObjectStore store, string? instance, string cache, bool follower) => new()
    {
        Store = store,
        Cache = new CacheDirectory(Directory.CreateDirectory(cache).FullName),
        Access = follower ? ObjectStorageAccess.ReadOnly : ObjectStorageAccess.Write,
        Instance = instance is null ? null : new WriterInstance(instance),
        Clock = new InstantClock(DateTimeOffset.UtcNow),
        ProbeRandomness = TestRandomness.Instance,
    };
}

/// <summary>The lost-answer cases on Azurite: the deterministic reproduction of #145.</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class AzuriteLostAnswerTests
{
    public static TheoryData<string> Cases() => [.. LostAnswerCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task azurite_applies_a_write_whose_answer_is_lost_and_the_instance_recognises_it(string name)
    {
        Uri container = await Azurite.NewContainerAsync();
        await LostAnswerCases.All().Single(c => c.Name == name).Run(handler =>
        {
            AzureBlobStore store = AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = container, Clock = new InstantClock(DateTimeOffset.UtcNow), Handler = handler });
            return (store, store);
        });
    }
}

/// <summary>The lost-answer cases on the storage account, where #145 was found.</summary>
[Trait(ObjectTraits.Name, ObjectTraits.Faults)]
public sealed class AzureAccountLostAnswerTests
{
    public static TheoryData<string> Cases() => [.. LostAnswerCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task the_service_applies_a_write_whose_answer_is_lost_and_the_instance_recognises_it(string name)
    {
        using AzureBlobStore cleaner = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("lost-" + ServiceLegs.Run());

        try
        {
            await LostAnswerCases.All().Single(c => c.Name == name).Run(handler =>
            {
                AzureBlobStore store = AzureAccount.Open(handler);
                return (new PrefixedObjectStore(store, prefix), store);
            });
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(cleaner, prefix);
        }
    }
}
