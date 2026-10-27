// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Extent maintenance in an object store (ADRs 0158, 0160): a follower keeps
/// serving across the writer's merges and deletions because its record holds
/// what it reads; a dead reader's record holds deletion back until it
/// restarts or is forgotten; a forgotten reader and an unannounced read fail
/// rather than answer wrong; and an identity that may not write its record
/// fails the open, naming the role.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectExtentMaintenanceTests
{
    [Fact]
    public Task a_follower_keeps_serving_across_merges_and_deletions() =>
        ExtentMaintenanceCases.FollowerAcrossMergesAndDeletionsAsync(new MemoryObjectStore());

    /// <summary>
    /// A follower writes one object, its reader record, and deletes it when it
    /// closes; an unannounced one writes nothing at all (ADR 0134 amended,
    /// ADR 0160).
    /// </summary>
    [Theory]
    [InlineData(ReaderAnnouncing.Required)]
    [InlineData(ReaderAnnouncing.Unannounced)]
    public async Task a_follower_writes_only_its_reader_record_and_an_unannounced_one_nothing(ReaderAnnouncing announcing)
    {
        await using ObjectHarness harness = new();
        Backend writer = await harness.OpenAsync();
        await using Dataset dataset = await T.OpenOrCreate(writer, T.Options());
        await ObjectExtentTests.CommitHistoryAsync(dataset, 6);
        await dataset.CheckpointAsync(new Position(4), T.Ct);
        IReadOnlyList<string> names = harness.Store.Names();
        List<string> written = [];
        harness.Store.Hook = (operation, name) =>
        {
            if (operation is ObjectOperation.Create or ObjectOperation.Replace or ObjectOperation.Delete)
            {
                written.Add(name.Value);
            }

            return null;
        };

        Backend reader = await Backend.OpenAsync(harness.Options(ObjectStorageAccess.ReadOnly, announcing: announcing), T.Ct);

        await using (Dataset read = await Dataset.OpenAsync(reader, T.Options(), T.Ct))
        {
            Assert.Equal(await ObjectExtentTests.ViewsAsync(dataset), await ObjectExtentTests.ViewsAsync(read));
        }

        await reader.DisposeAsync();
        harness.Store.Hook = null;

        // The writer may replace its own record meanwhile; the follower's
        // writes are its record's alone, or none.
        List<string> follower = [.. written.Where(name => name != "readers/" + writer.Reader)];

        if (announcing == ReaderAnnouncing.Required)
        {
            Assert.NotEmpty(follower);
            Assert.All(follower, name => Assert.Equal("readers/" + reader.Reader, name));
        }
        else
        {
            Assert.Empty(follower);
        }

        Assert.Equal(names, harness.Store.Names());
    }

    /// <summary>
    /// A reader that dies holds what its record announces until an event
    /// removes the record — the same id opening again, or the operator's
    /// forget — and never by a timer; then the writer deletes it (ADR 0160).
    /// </summary>
    [Theory]
    [InlineData("restart")]
    [InlineData("forget")]
    public async Task a_dead_readers_record_holds_deletion_until_it_restarts_or_is_forgotten(string removedBy)
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);
        await writer.CheckpointAsync(new Position(6), T.Ct);

        // Attached, and never closed: a crash.
        ReaderId dead = new("dead-reader");
        Backend crashed = await Backend.OpenAsync(harness.Options(ObjectStorageAccess.ReadOnly, reader: dead), T.Ct);
        Dataset held = await Dataset.OpenAsync(crashed, T.Options(), T.Ct);
        Assert.Equal(new CatalogVersion(writer.CatalogVersion.Value), (await Backend.ReadReadersAsync(harness.Store, null, T.Ct)).Single(r => r.Reader == dead).Oldest);

        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        int before = ExtentMaintenanceCases.UnnamedSets(harness.Store, writer);
        Assert.True(before > 0, "the dead reader's record holds nothing back");
        Assert.Equal(before, writer.UnnamedSetsForTests());
        ExtentRetention retention = writer.Retention!;
        Assert.True(retention.RetainedBytes > 0);
        Assert.True(retention.Readers.Single(r => r.Announcement.Reader == dead).RetainedBytes > 0);

        // However many rounds pass, nothing is decided from time.
        harness.Clock.Now += TimeSpan.FromDays(400);

        for (int i = 0; i < 40; i++)
        {
            await writer.MaintainAsync(T.Ct);
        }

        Assert.Equal(before, ExtentMaintenanceCases.UnnamedSets(harness.Store, writer));

        if (removedBy == "restart")
        {
            Backend restarted = await Backend.OpenAsync(harness.Options(ObjectStorageAccess.ReadOnly, reader: dead), T.Ct);
            await using Dataset again = await Dataset.OpenAsync(restarted, T.Options(), T.Ct);
            await writer.DeleteUnnamedForTestsAsync(T.Ct);
            Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(harness.Store, writer));
            Assert.Equal(await ObjectExtentTests.ViewsAsync(writer), await ObjectExtentTests.ViewsAsync(again));
            await again.DisposeAsync();
            await restarted.DisposeAsync();
        }
        else
        {
            ReaderRecord record = (await Backend.ReadReadersAsync(harness.Store, null, T.Ct)).Single(r => r.Reader == dead);
            Assert.False(await Backend.ForgetReaderAsync(harness.Store, null, dead, record.Oldest, new CatalogVersion(record.Newest.Value + 1), harness.Clock, T.Ct));
            Assert.True(await Backend.ForgetReaderAsync(harness.Store, null, dead, record.Oldest, record.Newest, harness.Clock, T.Ct));
            Assert.DoesNotContain(await Backend.ReadReadersAsync(harness.Store, null, T.Ct), r => r.Reader == dead);
            await writer.DeleteUnnamedForTestsAsync(T.Ct);
            Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(harness.Store, writer));
        }

        GC.KeepAlive(held);
    }

    /// <summary>
    /// A reader forgotten while it still reads fails the reads that find an
    /// extent gone with <see cref="ReaderForgottenException"/>, and never
    /// answers wrong; opened again, it serves (ADR 0160).
    /// </summary>
    [Fact]
    public async Task a_forgotten_reader_fails_with_reader_forgotten_and_never_returns_a_wrong_result()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);
        await writer.CheckpointAsync(new Position(6), T.Ct);
        List<SortedSet<string>> expected = await ObjectExtentTests.ViewsAsync(writer);

        Backend reading = await harness.OpenAsync(ObjectStorageAccess.ReadOnly, cacheBytes: 1);
        await using Dataset follower = await Dataset.OpenAsync(reading, T.Options(), T.Ct);
        ReaderRecord record = (await Backend.ReadReadersAsync(harness.Store, null, T.Ct)).Single(r => r.Reader == reading.Reader);
        Assert.True(await Backend.ForgetReaderAsync(harness.Store, null, reading.Reader, record.Oldest, record.Newest, harness.Clock, T.Ct));

        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(harness.Store, writer));

        int failed = await ExtentMaintenanceCases.ModelOrFailureAsync<ReaderForgottenException>(follower, expected);
        Assert.True(failed > 0, "no read of the forgotten reader found an extent gone");

        Backend attached = await harness.OpenAsync(ObjectStorageAccess.ReadOnly, cacheBytes: 1);
        await using Dataset again = await Dataset.OpenAsync(attached, T.Options(), T.Ct);
        Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(again));
    }

    /// <summary>
    /// An unannounced read, which writes no record, may find an extent gone:
    /// it fails with <see cref="UnannouncedExtentGoneException"/>, never the
    /// damage error, and never a wrong result (ADR 0160).
    /// </summary>
    [Fact]
    public async Task an_unannounced_read_fails_on_a_gone_extent_and_never_returns_a_wrong_result()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);
        await writer.CheckpointAsync(new Position(6), T.Ct);
        List<SortedSet<string>> expected = await ObjectExtentTests.ViewsAsync(writer);

        Backend reading = await Backend.OpenAsync(harness.Options(ObjectStorageAccess.ReadOnly, cacheBytes: 1, announcing: ReaderAnnouncing.Unannounced), T.Ct);
        await using Dataset follower = await Dataset.OpenAsync(reading, T.Options(), T.Ct);
        Assert.DoesNotContain(await Backend.ReadReadersAsync(harness.Store, null, T.Ct), r => r.Reader == reading.Reader);

        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);

        int failed = await ExtentMaintenanceCases.ModelOrFailureAsync<UnannouncedExtentGoneException>(follower, expected);
        Assert.True(failed > 0, "no unannounced read found an extent gone");
        await follower.DisposeAsync();
        await reading.DisposeAsync();
    }

    /// <summary>
    /// An identity the store refuses a reader record for fails the open, and
    /// the error names the role it needs; it is never read unannounced
    /// without being asked (ADR 0160, AnnouncingIsRequired).
    /// </summary>
    [Fact]
    public async Task an_identity_that_may_not_write_its_record_fails_the_open_naming_the_role()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, T.Options());
        await ObjectExtentTests.CommitHistoryAsync(writer, 4);
        await writer.CheckpointAsync(new Position(2), T.Ct);

        harness.Store.Hook = (operation, name) =>
            name.Value.StartsWith("readers/", StringComparison.Ordinal) && operation is ObjectOperation.Create or ObjectOperation.Replace
                ? throw new ObjectStoreAccessDeniedException("injected: 403 AuthorizationPermissionMismatch")
                : null;

        Backend reading = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
        ObjectStoreAccessDeniedException denied = await Assert.ThrowsAsync<ObjectStoreAccessDeniedException>(async () => await Dataset.OpenAsync(reading, T.Options(), T.Ct));
        Assert.Contains("readers/", denied.Message, StringComparison.Ordinal);
        Assert.Contains("Storage Blob Data Contributor", denied.Message, StringComparison.Ordinal);
        Assert.Contains("--unannounced", denied.Message, StringComparison.Ordinal);
        harness.Store.Hook = null;
    }

    /// <summary>
    /// A reader record of a format this build does not read holds every
    /// catalog version: it is never ignored (ADR 0160).
    /// </summary>
    [Fact]
    public async Task a_record_of_an_unknown_format_holds_every_version_until_forgotten()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);
        await writer.CheckpointAsync(new Position(6), T.Ct);
        await harness.Store.CreateIfAbsentAsync(new ObjectName("readers/from-the-future"), """{"format":2,"reader":"from-the-future"}"""u8.ToArray(), T.Ct);

        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.True(ExtentMaintenanceCases.UnnamedSets(harness.Store, writer) > 0);
        ReaderRecord unknown = (await Backend.ReadReadersAsync(harness.Store, null, T.Ct)).Single(r => !r.Understood);
        Assert.False(unknown.Understood);

        Assert.True(await Backend.ForgetReaderAsync(harness.Store, null, unknown.Reader, unknown.Oldest, unknown.Newest, harness.Clock, T.Ct));
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(harness.Store, writer));
    }

    /// <summary>
    /// What a superseded writer creates never fails the writer's checkpoint
    /// or merge, and is never named (ADR 0158): a name a create finds taken
    /// with other bytes is written again under the next sequence.
    /// </summary>
    [Fact]
    public async Task a_name_taken_with_other_bytes_takes_the_next_sequence_and_is_never_named()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, 10);
        await writer.CheckpointAsync(new Position(3), T.Ct);

        // What a superseded writer would leave: other bytes under every name
        // the next sets could take, for the next checkpoint's window and the
        // merge's.
        long next = writer.CatalogVersion.Value;
        List<string> planted = [];

        foreach ((long from, long to, long first) in new[] { (3L, 6L, next), (0L, 6L, next + 5) })
        {
            for (long sequence = first; sequence < first + 3; sequence++)
            {
                string name = "derived/" + ExtentSet.NameOf(from, to, sequence) + "order0";
                await harness.Store.CreateIfAbsentAsync(new ObjectName(name), "not this writer's"u8.ToArray(), T.Ct);
                planted.Add(name);
            }
        }

        await writer.CheckpointAsync(new Position(6), T.Ct);
        Assert.True(await writer.MergeOnceForTestsAsync(T.Ct));
        await using (Backend reading = await harness.OpenAsync(ObjectStorageAccess.ReadOnly))
        await using (Dataset follower = await Dataset.OpenAsync(reading, T.Options(), T.Ct))
        {
            Assert.Equal(await ObjectExtentTests.ViewsAsync(writer), await ObjectExtentTests.ViewsAsync(follower));
        }

        CatalogSummary catalog = CatalogSummary.TryRead(harness.Store.Get("derived/catalog").AsMemory(12))!;
        Assert.DoesNotContain(catalog.Sets, set => planted.Any(p => p.StartsWith("derived/" + set.Name.Value, StringComparison.Ordinal)));

        // Never named, and held by no record: deleted.
        await writer.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.DoesNotContain(harness.Store.Names(), name => planted.Contains(name));
    }
}

/// <summary>Cases shared by the in-memory store and the service legs, Azurite and RustFS.</summary>
internal static class ExtentMaintenanceCases
{
    /// <summary>Merges driven by hand, two sets of one size at a time.</summary>
    internal static DatasetOptions Merging() => new()
    {
        Clock = ManualClock.Epoch(),
        CommitCache = T.CommitCache,
        Maintenance = MaintenanceMode.Off,
        ExtentMerges = new ExtentMergePolicy { FanOut = 2, MaxSetBytes = new ByteCount(1L << 20) },
    };

    /// <summary>
    /// A follower attached to the writer's catalog serves, at every position,
    /// what the writer serves, across the writer's merges and its deletions:
    /// its record holds what it reads. Closed, it lets the writer delete it.
    /// </summary>
    internal static async Task FollowerAcrossMergesAndDeletionsAsync(IObjectStore store)
    {
        string writerCache = Directory.CreateTempSubdirectory("varve-maintenance-").FullName;
        string followerCache = Directory.CreateTempSubdirectory("varve-maintenance-").FullName;

        try
        {
            Backend storage = await Backend.OpenAsync(new ObjectStorageOptions
            {
                Store = store,
                Cache = new CacheDirectory(writerCache),
                Instance = new WriterInstance("writer"),
                Clock = TimeProvider.System,
                ProbeRandomness = TestRandomness.Instance,
            }, T.Ct);

            await using (storage)
            {
                await using Dataset writer = await T.OpenOrCreate(storage, Merging());
                await ObjectExtentTests.CommitHistoryAsync(writer, 12);

                foreach (long at in new long[] { 2, 4, 6, 8 })
                {
                    await writer.CheckpointAsync(new Position(at), T.Ct);
                }

                List<SortedSet<string>> expected = await ObjectExtentTests.ViewsAsync(writer);

                // A cache of one byte: every read of the follower fetches from the store.
                Backend reading = await Backend.OpenAsync(new ObjectStorageOptions
                {
                    Store = store,
                    Cache = new CacheDirectory(followerCache),
                    Access = ObjectStorageAccess.ReadOnly,
                    Reader = new ReaderId("follower"),
                    CacheBytes = new ByteCount(1),
                    Clock = TimeProvider.System,
                }, T.Ct);

                await using (reading)
                {
                    await using Dataset follower = await Dataset.OpenAsync(reading, T.Options(), T.Ct);
                    Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(follower));
                    int merges = 0;

                    while (await writer.MergeOnceForTestsAsync(T.Ct))
                    {
                        merges++;
                        await writer.DeleteUnnamedForTestsAsync(T.Ct);
                        Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(follower));
                        Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(writer));
                    }

                    Assert.True(merges >= 2, merges + " merges");
                    Assert.True(UnnamedSets(store, writer) > 0, "the follower's record holds nothing back");
                }

                // The follower closed, and deleted its record: the writer deletes.
                await writer.DeleteUnnamedForTestsAsync(T.Ct);
                Assert.Equal(0, UnnamedSets(store, writer));
                Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(writer));
            }
        }
        finally
        {
            Directory.Delete(writerCache, recursive: true);
            Directory.Delete(followerCache, recursive: true);
        }
    }

    /// <summary>The sets the store holds that the writer's catalog does not name.</summary>
    internal static int UnnamedSets(IObjectStore store, Dataset writer)
    {
        IReadOnlyList<ObjectEntry> entries = store.ListAsync(new ObjectName("derived/extents/"), T.Ct).AsTask().GetAwaiter().GetResult();
        HashSet<string> sets = [.. entries.Select(e => e.Name.Value["derived/".Length..]).Select(n => n[..(n.IndexOf('/', ExtentSet.Prefix.Length) + 1)])];
        SetsNamed(writer, out HashSet<string> names);
        return sets.Count(s => !names.Contains(s));
    }

    private static void SetsNamed(Dataset writer, out HashSet<string> names)
    {
        names = [];
        IReadableBlob blob;

        try
        {
            blob = writer.StorageForTests().Derived.OpenAsync(new BlobName("catalog"), T.Ct).AsTask().GetAwaiter().GetResult();
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        {
            using (blob)
            {
                byte[] bytes = new byte[blob.Length.Value];
                blob.Read(new ByteOffset(0), bytes);

                foreach (CatalogSet set in CatalogSummary.TryRead(bytes)?.Sets ?? [])
                {
                    names.Add(set.Name.Value);
                }
            }
        }
    }

    /// <summary>Every blob of every set the dataset's chain names is in the store: no catalog it reads names a missing extent.</summary>
    internal static void AssertChainWhole(IObjectStore store, Dataset dataset, string what)
    {
        HashSet<string> present = [.. store.ListAsync(new ObjectName("derived/extents/"), T.Ct).AsTask().GetAwaiter().GetResult().Select(e => e.Name.Value)];

        foreach (string set in dataset.SetNamesForTests())
        {
            foreach (string part in new[] { "order0", "order1", "order2", "order3", "order4", "order5", "terms" })
            {
                Assert.True(present.Contains("derived/" + set + part), what + ": " + set + " lost its " + part);
            }
        }
    }

    /// <summary>
    /// Every position read through the follower: each the model's, or failed
    /// with <typeparamref name="TFailure"/>, never anything else. Returns how
    /// many failed.
    /// </summary>
    internal static async Task<int> ModelOrFailureAsync<TFailure>(Dataset follower, List<SortedSet<string>> expected)
        where TFailure : Exception
    {
        int failed = 0;

        for (long q = 0; q <= follower.Head.Value; q++)
        {
            try
            {
                using DatasetView view = await follower.AsOfAsync(new Position(q), T.Ct);
                Assert.Equal(expected[(int)q], T.Terms(view));
            }
            catch (TFailure)
            {
                failed++;
            }
        }

        return failed;
    }
}
