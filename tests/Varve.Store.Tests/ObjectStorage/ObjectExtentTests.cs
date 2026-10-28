// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Extents and the catalog in an object store (ADR 0139): read through a page
/// cache under any eviction schedule, swapped only by the epoch's writer,
/// attached by a follower, and never named by a catalog before they are whole.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectExtentTests
{
    /// <summary>
    /// With a cache too small to hold any page beside the one being read,
    /// every read evicts and fetches again; with one that holds everything,
    /// nothing does. The views are the model's at every position either way.
    /// </summary>
    [Fact]
    public async Task eviction_never_changes_a_result()
    {
        await Gen.Select(Generators.CommitsOnly, Gen.OneOfConst(1L, 1L << 20, 8L << 30), Gen.Int[1, 1000].Array[1, 4]).SampleAsync(
            async sample =>
            {
                (Script script, long cacheBytes, int[] checkpoints) = sample;
                await using ObjectHarness objects = new();
                Backend storage = await objects.OpenAsync(cacheBytes: cacheBytes);
                await using Harness harness = await Harness.StartAsync(storage: storage);
                await harness.RunAsync(script);

                if (harness.Model.Head == 0)
                {
                    return;
                }

                foreach (int choice in checkpoints)
                {
                    await harness.Dataset.CheckpointAsync(new Position(1 + (choice % harness.Model.Head)), T.Ct);
                }

                await ExtentTests.EveryPositionAsync(harness, harness.Dataset, "cache of " + cacheBytes + " bytes");
            },
            iter: 30,
            print: sample => sample.Item1 + " cache " + sample.Item2 + " checkpoints " + string.Join(",", sample.Item3));
    }

    /// <summary>
    /// A writer superseded by a newer claim writes its extents, which nothing
    /// names, and its catalog swap is refused; what it reads is unchanged, and
    /// the new writer opens on the catalog as it was.
    /// </summary>
    [Fact]
    public async Task a_superseded_writers_catalog_swap_is_refused_and_its_reads_are_unchanged()
    {
        await using ObjectHarness objects = new();
        Backend first = await objects.OpenAsync(instance: "first");
        await using Dataset writer = await T.OpenOrCreate(first, T.Options());
        await CommitHistoryAsync(writer, 9);
        await writer.CheckpointAsync(new Position(4), T.Ct);
        List<SortedSet<string>> before = await ViewsAsync(writer);

        Backend second = await objects.OpenAsync(instance: "second");
        await Assert.ThrowsAsync<WriterSupersededException>(async () => await writer.CheckpointAsync(new Position(8), T.Ct));
        Assert.True(first.IsSuperseded);
        Assert.Equal([new Position(4)], writer.Checkpoints);
        Assert.Equal(before, await ViewsAsync(writer));

        await using Dataset current = await T.OpenOrCreate(second, T.Options());
        Assert.Equal([new Position(4)], current.Checkpoints);
        Assert.Equal(before, await ViewsAsync(current));
    }

    /// <summary>A read-only open attaches the writer's catalog and serves what the writer serves, at every position.</summary>
    [Fact]
    public async Task a_follower_attaching_the_writers_catalog_serves_the_same_results()
    {
        await using ObjectHarness objects = new();
        Backend storage = await objects.OpenAsync(instance: "writer");
        await using Dataset writer = await T.OpenOrCreate(storage, T.Options());
        await CommitHistoryAsync(writer, 14);
        await writer.CheckpointAsync(new Position(5), T.Ct);
        await writer.CheckpointAsync(new Position(11), T.Ct);
        await writer.CheckpointAsync(new Position(8), T.Ct);

        Backend read = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset follower = await Dataset.OpenAsync(read, T.Options(), T.Ct);
        Assert.Equal(writer.Checkpoints, follower.Checkpoints);
        Assert.Equal(await ViewsAsync(writer), await ViewsAsync(follower));
        await Assert.ThrowsAsync<WriterSupersededException>(async () => await follower.CheckpointAsync(new Position(13), T.Ct));
    }

    /// <summary>
    /// A fault at every write under <c>derived/</c> — every extent's create
    /// and every catalog swap — after which the store refuses every write, as
    /// a writer that died there: the next writer opens on a catalog whose every
    /// set is whole, and serves the same results at every position.
    /// </summary>
    [Fact]
    public async Task a_fault_at_any_extent_write_or_catalog_swap_never_leaves_a_catalog_naming_a_missing_extent()
    {
        int writes = await DerivedWritesAsync(null, null);
        Assert.True(writes > 20, writes + " writes");
        List<SortedSet<string>>? expected = null;

        for (int at = 1; at <= writes; at++)
        {
            foreach (bool afterApply in new[] { false, true })
            {
                MemoryObjectStore store = new();
                await DerivedWritesAsync(store, (at, afterApply));
                store.Hook = null;

                await using ObjectHarness objects = new(store);
                Backend next = await objects.OpenAsync(instance: "next");
                AssertCatalogWhole(store);
                await using Dataset reopened = await T.OpenOrCreate(next, T.Options());
                List<SortedSet<string>> views = await ViewsAsync(reopened);
                expected ??= views;
                Assert.Equal(expected, views);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("derived/ writes: " + writes + ", injection points: " + (writes * 2));
    }

    // The workload: a history, then checkpoints above, inside and by drops.
    // With a fault, the store refuses every derived/ write from the faulted
    // one on. Returns the derived/ writes a clean run makes.
    private static async Task<int> DerivedWritesAsync(MemoryObjectStore? store, (int At, bool AfterApply)? fault)
    {
        store ??= new MemoryObjectStore();
        int count = 0;
        bool failing = false;

        ObjectStorageOptions options = new()
        {
            Store = store,
            Cache = new CacheDirectory(Directory.CreateTempSubdirectory("varve-extent-faults-").FullName),
            Instance = new WriterInstance("faulted"),
            Clock = new InstantClock(DateTimeOffset.UnixEpoch),
            ProbeRandomness = TestRandomness.Instance,
        };

        try
        {
            Backend storage = await Backend.OpenAsync(options, T.Ct);

            try
            {
                await using Dataset dataset = await T.OpenOrCreate(storage, T.Options());
                await CommitHistoryAsync(dataset, 12);

                store.Hook = (operation, name) =>
                {
                    if (operation is not (ObjectOperation.Create or ObjectOperation.Replace) || !name.Value.StartsWith("derived/", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    if (failing)
                    {
                        throw new IOException("injected: the writer is gone");
                    }

                    if (fault is { } f && ++count == f.At)
                    {
                        failing = true;

                        if (f.AfterApply)
                        {
                            return () => throw new IOException("injected: lost after the store applied it");
                        }

                        throw new IOException("injected: before the store applied it");
                    }

                    if (fault is null)
                    {
                        count++;
                    }

                    return null;
                };

                try
                {
                    await dataset.CheckpointAsync(new Position(4), T.Ct);
                    await dataset.CheckpointAsync(new Position(9), T.Ct);
                    await dataset.CheckpointAsync(new Position(7), T.Ct);
                    await dataset.DropCheckpointAsync(new Position(4), T.Ct);
                    await dataset.CheckpointAsync(new Position(12), T.Ct);
                    await dataset.DropCheckpointAsync(new Position(12), T.Ct);
                }
                catch (IOException) when (fault is not null)
                {
                }
            }
            finally
            {
                store.Hook = null;
                await storage.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(options.Cache.Value, recursive: true);
        }

        return count;
    }

    // Every set the catalog names has every extent in the store.
    private static void AssertCatalogWhole(MemoryObjectStore store)
    {
        HashSet<string> names = [.. store.Names()];

        if (!names.Contains("derived/catalog"))
        {
            return;
        }

        byte[] wrapped = store.Get("derived/catalog")!;

        if (wrapped.Length == 12)
        {
            return;
        }

        // Set names are UTF-8 in the catalog's body, behind the epoch wrapper.
        string text = System.Text.Encoding.UTF8.GetString(wrapped);

        foreach (string set in names.Where(n => n.StartsWith("derived/extents/", StringComparison.Ordinal)).Select(n => n[..(n.LastIndexOf('/') + 1)]).Distinct())
        {
            string name = set["derived/".Length..];

            if (!text.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string part in new[] { "order0", "order1", "order2", "order3", "order4", "order5", "terms" })
            {
                Assert.True(names.Contains(set + part), "the catalog names " + name + " but " + part + " is missing");
            }
        }
    }

    // A history with retractions: each commit asserts two quads and retracts
    // one an earlier commit asserted.
    internal static async Task CommitHistoryAsync(Dataset dataset, int commits)
    {
        for (int i = 1; i <= commits; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);
            CommitRequestBuilder request = new CommitRequestBuilder()
                .Assert(T.Iri("s" + (i % 5)), T.Iri("p"), T.Literal("v" + n))
                .Assert(T.Iri("s" + (i % 3)), T.Iri("q"), T.Iri("o" + (i % 4)));

            if (i > 2)
            {
                request.Retract(T.Iri("s" + ((i - 2) % 5)), T.Iri("p"), T.Literal("v" + (i - 2).ToString(CultureInfo.InvariantCulture)));
            }

            await dataset.CommitAsync(request, T.Ct);
        }
    }

    internal static async Task<List<SortedSet<string>>> ViewsAsync(Dataset dataset)
    {
        List<SortedSet<string>> views = [];

        for (long q = 0; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(T.Terms(view));
        }

        return views;
    }
}
