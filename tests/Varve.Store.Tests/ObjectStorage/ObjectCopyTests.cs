// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// An object-store copy, as docs/operator/backup-and-restore.md tells an
/// operator to take one (#129): every object under <c>log/</c> and
/// <c>derived/</c> — the chunks, the extent sets and the catalog — copied to
/// another prefix, and neither the writer record nor <c>readers/</c>. Opened
/// there it has the source's id and head, answers at every position as the
/// source did, claims an epoch of its own and takes writes. On the in-memory
/// store wherever Varve.Store.Tests runs, and on Azurite in the services job.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class ObjectCopyTests
{
    [Fact]
    public Task a_copy_of_the_log_and_derived_objects_without_the_writer_record_or_readers_opens_elsewhere() =>
        CopyCases.CopiedToAnotherPrefixAsync(new MemoryObjectStore());

    /// <summary>
    /// What an asynchronous or geo-replicated copy can hold, since it copies
    /// objects in no order the writer chose: a catalog whose newest set has
    /// not arrived, or a log without its newest chunk. The first opens with
    /// every answer, the missing set a cache miss read from the log (ADR
    /// 0139); the second opens at the last commit the copy holds whole.
    /// </summary>
    [Theory]
    [InlineData(Missing.NewestSet)]
    [InlineData(Missing.NewestChunk)]
    public Task a_copy_that_caught_the_store_mid_swap_opens_at_what_it_holds(Missing missing) =>
        CopyCases.CopiedToAnotherPrefixAsync(new MemoryObjectStore(), missing);
}

/// <summary>What a copy lacks that the source held.</summary>
public enum Missing
{
    /// <summary>Nothing: every object under log/ and derived/.</summary>
    Nothing,

    /// <summary>The newest extent set's objects, which the catalog names.</summary>
    NewestSet,

    /// <summary>The newest log chunk.</summary>
    NewestChunk,
}

[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzuriteObjectCopyTests
{
    [Fact]
    public async Task a_copy_of_the_log_and_derived_objects_opens_elsewhere_on_azurite()
    {
        using AzureBlobStore store = await Azurite.ContainerAsync();
        await CopyCases.CopiedToAnotherPrefixAsync(store);
    }
}

internal static class CopyCases
{
    private static readonly ObjectName Source = new("source/");
    private static readonly ObjectName Target = new("copy/");

    internal static async Task CopiedToAnotherPrefixAsync(IObjectStore store, Missing missing = Missing.Nothing)
    {
        List<string> caches = [];

        try
        {
            List<SortedSet<string>> expected;
            Position head;
            List<Position> checkpoints;

            Backend writer = await Backend.OpenAsync(Options(store, Source, "writer", caches), T.Ct);

            await using (writer)
            {
                await using Dataset dataset = await Dataset.CreateAsync(writer, T.Id, DatasetOptions(), T.Ct);
                await CommitsAsync(dataset, 12, every: 4);
                expected = await ObjectExtentTests.ViewsAsync(dataset);
                head = dataset.Head;
                checkpoints = [.. dataset.Checkpoints];
            }

            Assert.Equal(new Position(12), head);
            Assert.NotEmpty(checkpoints);

            // A follower still runs against the source, so readers/ is not empty while the copy is taken.
            Backend follower = await Backend.OpenAsync(Options(store, Source, "follower", caches, ObjectStorageAccess.ReadOnly, new ReaderId("follower")), T.Ct);

            await using (follower)
            {
                await using Dataset following = await Dataset.OpenAsync(follower, DatasetOptions(), T.Ct);
                Assert.Equal(head, following.Head);

                IReadOnlyList<ObjectEntry> entries = await store.ListAsync(Source, T.Ct);
                List<string> names = [.. entries.Select(e => e.Name.Value)];
                Assert.Contains("source/writer", names);
                Assert.Contains(names, n => n.StartsWith("source/readers/", StringComparison.Ordinal));
                Assert.Contains("source/derived/catalog", names);
                Assert.Contains(names, n => n.StartsWith("source/derived/extents/", StringComparison.Ordinal));
                Assert.Contains(names, n => n.StartsWith("source/log/", StringComparison.Ordinal) && n.Contains(".chunks/", StringComparison.Ordinal));

                // The copy: log/ and derived/, nothing else, less what it missed.
                string? omitted = missing switch
                {
                    Missing.NewestSet => names.Where(n => n.StartsWith("source/derived/extents/", StringComparison.Ordinal))
                        .Select(n => n[..(n.IndexOf('/', "source/derived/extents/".Length) + 1)]).Max(StringComparer.Ordinal),
                    Missing.NewestChunk => names.Where(n => n.StartsWith("source/log/", StringComparison.Ordinal) && n.Contains(".chunks/", StringComparison.Ordinal)).Max(StringComparer.Ordinal),
                    _ => null,
                };

                foreach (ObjectEntry entry in entries)
                {
                    string name = entry.Name.Value;

                    if (omitted is not null && name.StartsWith(omitted, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (name.StartsWith("source/log/", StringComparison.Ordinal) || name.StartsWith("source/derived/", StringComparison.Ordinal))
                    {
                        ObjectRead read = Assert.IsType<ObjectRead>(await store.ReadAsync(entry.Name, new ByteOffset(0), entry.Length, T.Ct));
                        Assert.NotNull(await store.CreateIfAbsentAsync(new ObjectName(Target.Value + name[Source.Value.Length..]), read.Bytes, T.Ct));
                    }
                }

                List<string> copied = [.. (await store.ListAsync(Target, T.Ct)).Select(e => e.Name.Value)];
                Assert.DoesNotContain("copy/writer", copied);
                Assert.DoesNotContain(copied, n => n.StartsWith("copy/readers/", StringComparison.Ordinal));
            }

            // Opened at its own prefix: the source's id, head, checkpoints and every answer.
            Backend restored = await Backend.OpenAsync(Options(store, Target, "restored", caches), T.Ct);

            await using (restored)
            {
                Assert.False(restored.IsSuperseded);
                await using Dataset reopened = await Dataset.OpenAsync(restored, DatasetOptions(), T.Ct);
                Assert.Equal(T.Id, reopened.Id);
                Position at = reopened.Head;

                if (missing == Missing.NewestChunk)
                {
                    Assert.True(at.Value < head.Value, "the copy without the newest chunk opened at " + at + ", the source's head");
                }
                else
                {
                    Assert.Equal(head, at);
                }

                if (missing == Missing.Nothing)
                {
                    Assert.Equal(checkpoints, reopened.Checkpoints);
                }

                Assert.Equal(expected.Take((int)at.Value + 1), await ObjectExtentTests.ViewsAsync(reopened));

                // It writes on: a commit after the copy's head, which the source never sees.
                await reopened.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("after"), T.Iri("p"), T.Iri("o")), T.Ct);
                Assert.Equal(new Position(at.Value + 1), reopened.Head);
            }

            WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, Target, T.Ct));
            Assert.Equal(new WriterInstance("restored"), record.Holder);
            WriterRecord original = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(store, Source, T.Ct));
            Assert.Equal(new WriterInstance("writer"), original.Holder);
        }
        finally
        {
            foreach (string cache in caches)
            {
                try
                {
                    Directory.Delete(cache, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static ObjectStorageOptions Options(
        IObjectStore store, ObjectName prefix, string instance, List<string> caches, ObjectStorageAccess access = ObjectStorageAccess.Write, ReaderId? reader = null)
    {
        string cache = Directory.CreateTempSubdirectory("varve-copy-").FullName;
        caches.Add(cache);
        return new ObjectStorageOptions
        {
            Store = store,
            Prefix = prefix,
            Cache = new CacheDirectory(cache),
            Access = access,
            Instance = new WriterInstance(instance),
            Clock = TimeProvider.System,
            ProbeRandomness = TestRandomness.Instance,
            Reader = reader,
        };
    }

    private static DatasetOptions DatasetOptions() => new()
    {
        Clock = TimeProvider.System,
        CommitCache = T.CommitCache,
        Maintenance = MaintenanceMode.Off,
    };

    // Commits with retractions, a checkpoint every few, so the copy holds extent sets and a catalog.
    private static async Task CommitsAsync(Dataset dataset, int count, int every)
    {
        for (int i = 0; i < count; i++)
        {
            CommitRequestBuilder request = new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Iri("o" + (i % 3)));

            if (i % 3 == 2)
            {
                request.Retract(T.Iri("s" + (i - 2)), T.Iri("p"), T.Iri("o" + ((i - 2) % 3)));
            }

            await dataset.CommitAsync(request, T.Ct);

            if ((i + 1) % every == 0)
            {
                await dataset.CheckpointAsync(dataset.Head, T.Ct);
            }
        }
    }
}
