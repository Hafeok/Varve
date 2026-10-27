// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Older readers decide when format-4 sets are rewritten (ADR 0178): while a
/// reader record that reads only derived format 4 is present — one written
/// before #133, without <c>maxDerived</c> — the writer rewrites no format-4
/// set, and its merges and splits of them keep format 4, so that reader's
/// chain ends only where the new format-5 sets begin and it replays only
/// their windows. Once the record is forgotten, or its reader upgraded,
/// rewriting proceeds. Every position reads the same throughout.
/// </summary>
public sealed class ObjectFormatUpgradeTests
{
    [Theory]
    [InlineData("forgotten")]
    [InlineData("upgraded")]
    public async Task no_format_4_set_is_rewritten_while_a_reader_of_format_4_is_present(string then)
    {
        await using ObjectHarness objects = new();
        List<SortedSet<string>> expected;
        long upgradedAt;

        // A writer of an earlier release: four format-4 sets.
        Backend earlier = await objects.OpenAsync(instance: "writer");

        await using (Dataset dataset = await Dataset.CreateAsync(earlier, T.Id, Options(objects, version: 4), T.Ct))
        {
            await CommitsAsync(dataset, 0, 16, every: 4);
            Assert.All(dataset.SeekableSetsForTests(), Assert.False);
            expected = await ObjectExtentTests.ViewsAsync(dataset);
            upgradedAt = dataset.Head.Value;
        }

        // A reader of that release, still present: its record has no maxDerived.
        CatalogVersion version = (await Backend.ReadReadersAsync(objects.Store, objects.Prefix, T.Ct)).Max(r => r.Newest);
        await objects.Store.CreateIfAbsentAsync(
            new ObjectName((objects.Prefix?.Value ?? string.Empty) + "readers/older"),
            Encoding.UTF8.GetBytes("{\"format\":1,\"reader\":\"older\",\"catalogOldest\":" + version.Value + ",\"catalogNewest\":" + version.Value + ",\"state\":\"announced\"}"),
            T.Ct);
        ReaderRecord older = (await Backend.ReadReadersAsync(objects.Store, objects.Prefix, T.Ct)).Single(r => r.Reader.Value == "older");
        Assert.Equal(DerivedVersion.Format4, older.MaxDerived);

        // This release takes over: more commits, two checkpoints, every merge due.
        Backend storage = await objects.OpenAsync(instance: "writer");
        await using Dataset upgraded = await Dataset.OpenAsync(storage, Options(objects, version: 5), T.Ct);
        await CommitsAsync(upgraded, 16, 8, every: 4);
        expected.AddRange((await ObjectExtentTests.ViewsAsync(upgraded)).Skip(expected.Count));

        while (await upgraded.MergeOnceForTestsAsync(T.Ct))
        {
        }

        // Format-4 sets first, then format-5 sets, and the older reader's
        // chain reaches at least as far as it did: it replays only the log
        // above, which only format-5 sets cover.
        (long From, long To, long Bytes)[] sets = upgraded.SetsForTests();
        bool[] seekable = upgraded.SeekableSetsForTests();
        int format4 = Array.IndexOf(seekable, true) is int at and >= 0 ? at : seekable.Length;
        Assert.True(format4 > 0, "the format-4 sets were rewritten under a reader of format 4");
        Assert.All(seekable[format4..], Assert.True);
        Assert.True(sets[format4 - 1].To >= upgradedAt, "a format-4 set the older reader's chain needs was rewritten");
        Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(upgraded));

        // The reader goes: forgotten by the operator, or restarted on this release.
        if (then == "forgotten")
        {
            Assert.True(await Backend.ForgetReaderAsync(objects.Store, objects.Prefix, older.Reader, older.Oldest, older.Newest, objects.Clock, T.Ct));
        }
        else
        {
            Backend follower = await objects.OpenAsync(ObjectStorageAccess.ReadOnly, instance: "older", reader: new ReaderId("older"));
            await using Dataset following = await Dataset.OpenAsync(follower, Options(objects, version: 5), T.Ct);
            Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(following));
            Assert.Equal(DerivedVersion.ThisBuildReads, (await Backend.ReadReadersAsync(objects.Store, objects.Prefix, T.Ct)).Single(r => r.Reader.Value == "older").MaxDerived);
        }

        int rewrites = 0;

        while (await upgraded.MergeOnceForTestsAsync(T.Ct))
        {
            rewrites++;
        }

        Assert.True(rewrites >= 1);
        Assert.All(upgraded.SeekableSetsForTests(), Assert.True);
        Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(upgraded));
    }

    private static DatasetOptions Options(ObjectHarness objects, ushort version)
    {
        DatasetOptions options = new()
        {
            Clock = objects.Clock,
            CommitCache = T.CommitCache,
            Maintenance = MaintenanceMode.Off,
            ExtentMerges = new ExtentMergePolicy { FanOut = 2, MaxSetBytes = new ByteCount(1L << 20) },
        };
        options.ExtentVersion = version;
        return options;
    }

    // Commits with retractions, a checkpoint every few.
    private static async Task CommitsAsync(Dataset dataset, int first, int count, int every)
    {
        for (int i = first; i < first + count; i++)
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
