// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The default projection in <c>derived/</c> (ADR 0070): memtable flushes,
/// disk runs and their tiered merges, the persisted state, and what opening
/// makes of a <c>derived/</c> that is missing, stale or another dataset's.
/// </summary>
public sealed class DerivedTests
{
    private static DatasetOptions Options(long memtableLimit, MaintenanceMode mode = MaintenanceMode.Off) => new()
    {
        Clock = ManualClock.Epoch(),
        MemtableLimit = new QuadCount(memtableLimit),
        CommitCache = T.CommitCache,
        Maintenance = mode,
    };

    private static CommitRequest Quads(int i, int count = 3)
    {
        CommitRequest request = new();

        for (int q = 0; q < count; q++)
        {
            request.Assert(T.Iri("s" + i), T.Iri("p" + q), T.Integer((i * 10 + q).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return request;
    }

    private static async Task<List<BlobName>> RunsAsync(IStorage storage) =>
        [.. (await storage.Derived.ListAsync(T.Ct)).Where(n => n.Value.StartsWith("index/runs/", StringComparison.Ordinal))];

    [Fact]
    public async Task maintenance_flushes_the_memtable_and_merges_disk_runs_in_tiers()
    {
        MemoryStorage storage = new();
        DatasetOptions options = Options(memtableLimit: 6);

        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct))
        {
            for (int i = 0; i < 200; i++)
            {
                await dataset.CommitAsync(Quads(i), T.Ct);
                await dataset.MaintainAsync(T.Ct);
            }

            List<BlobName> runs = await RunsAsync(storage);
            Assert.InRange(runs.Count, 1, 12);
            Assert.Contains(new BlobName("index/state"), await storage.Derived.ListAsync(T.Ct));

            using DatasetView view = dataset.Pin();
            Assert.Equal(600, T.All(view).Count);
            Assert.Equal(600, view.Estimate(Varve.Rdf.TermHandle.None, Varve.Rdf.TermHandle.None, Varve.Rdf.TermHandle.None, Varve.Rdf.GraphPattern.Any).Count.Value);
        }

        await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
        using DatasetView again = reopened.Pin();
        Assert.Equal(600, T.All(again).Count);
        Assert.True(again.Contains(new Varve.Rdf.Quad(
            again.TryInternalise(T.Iri("s7"), out Varve.Rdf.TermHandle s) ? s : default,
            again.TryInternalise(T.Iri("p1"), out Varve.Rdf.TermHandle p) ? p : default,
            again.TryInternalise(T.Integer("71"), out Varve.Rdf.TermHandle o) ? o : default,
            Varve.Rdf.TermHandle.None)));
    }

    [Fact]
    public async Task retractions_through_disk_runs_read_as_the_model_does()
    {
        MemoryStorage storage = new();
        DatasetOptions options = Options(memtableLimit: 2);

        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        for (int i = 0; i < 40; i++)
        {
            await dataset.CommitAsync(Quads(i), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        for (int i = 0; i < 40; i += 2)
        {
            CommitRequest retract = new();

            for (int q = 0; q < 3; q++)
            {
                retract.Retract(T.Iri("s" + i), T.Iri("p" + q), T.Integer((i * 10 + q).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            await dataset.CommitAsync(retract, T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        using DatasetView view = dataset.Pin();
        Assert.Equal(60, T.All(view).Count);
        Assert.Equal(60, view.Estimate(Varve.Rdf.TermHandle.None, Varve.Rdf.TermHandle.None, Varve.Rdf.TermHandle.None, Varve.Rdf.GraphPattern.Any).Count.Value);

        await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
        using DatasetView again = reopened.Pin();
        Assert.Equal(T.Terms(view), T.Terms(again));
    }

    [Fact]
    public async Task a_pin_keeps_reading_runs_a_merge_has_replaced_and_they_are_deleted_once_it_lets_go()
    {
        await using TemporaryDirectory directory = new();
        FileStorage storage = await directory.OpenAsync();
        DatasetOptions options = Options(memtableLimit: 3);

        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct);

        for (int i = 0; i < 10; i++)
        {
            await dataset.CommitAsync(Quads(i), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        DatasetView pinned = dataset.Pin();
        List<BlobName> before = await RunsAsync(storage);

        for (int i = 10; i < 60; i++)
        {
            await dataset.CommitAsync(Quads(i), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        // Replaced, but still read by the pin: deletion waits (ADR 0070).
        Assert.Empty(before.Except(await RunsAsync(storage)));
        Assert.Equal(30, T.All(pinned).Count);
        pinned.Dispose();
        await dataset.MaintainAsync(T.Ct);
        Assert.NotEmpty(before.Except(await RunsAsync(storage)));

        using DatasetView now = dataset.Pin();
        Assert.Equal(180, T.All(now).Count);
    }

    [Fact]
    public async Task background_maintenance_writes_runs_without_being_asked()
    {
        MemoryStorage storage = new();

        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(4, MaintenanceMode.Background), T.Ct))
        {
            for (int i = 0; i < 50; i++)
            {
                await dataset.CommitAsync(Quads(i), T.Ct);
            }

            // Disposing cancels a round in progress; wait for one to land first.
            for (int wait = 0; wait < 200 && (await RunsAsync(storage)).Count == 0; wait++)
            {
                await Task.Delay(25, T.Ct);
            }
        }

        Assert.NotEmpty(await RunsAsync(storage));

        await using Dataset reopened = await Dataset.OpenAsync(storage, Options(4, MaintenanceMode.Background), T.Ct);
        using DatasetView view = reopened.Pin();
        Assert.Equal(150, T.All(view).Count);
    }

    [Fact]
    public async Task a_missing_derived_directory_is_rebuilt_from_the_log()
    {
        await using TemporaryDirectory directory = new();
        DatasetOptions options = Options(memtableLimit: 5);
        SortedSet<string> expected;

        await using (FileStorage storage = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct))
        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct))
        {
            for (int i = 0; i < 30; i++)
            {
                await dataset.CommitAsync(Quads(i), T.Ct);
                await dataset.MaintainAsync(T.Ct);
            }

            await dataset.CheckpointAsync(new Position(20), T.Ct);
            using DatasetView view = dataset.Pin();
            expected = T.Terms(view);
        }

        Directory.Delete(Path.Combine(directory.Path, "derived"), recursive: true);

        await using FileStorage reopenedStorage = await FileStorage.OpenAsync(directory.Directory, TemporaryDirectory.Options, T.Ct);
        Assert.True(File.Exists(Path.Combine(directory.Path, "derived", ".gitignore")));
        await using Dataset reopened = await Dataset.OpenAsync(reopenedStorage, options, T.Ct);
        Assert.Empty(reopened.Checkpoints);
        using DatasetView again = reopened.Pin();
        Assert.Equal(expected, T.Terms(again));
    }

    [Fact]
    public async Task derived_data_of_another_dataset_or_a_longer_log_is_a_cache_miss()
    {
        DatasetOptions options = Options(memtableLimit: 4);

        // Two datasets of one id with different histories, and one of another id.
        MemoryStorage longer = new();
        MemoryStorage shorter = new();
        MemoryStorage other = new();

        foreach ((MemoryStorage storage, DatasetId id, int commits, string prefix) in new[]
        {
            (longer, T.Id, 30, "a"),
            (shorter, T.Id, 12, "a"),
            (other, new DatasetId(Guid.Parse("11111111-2222-3333-4444-555555555555")), 30, "a"),
        })
        {
            await using Dataset dataset = await Dataset.CreateAsync(storage, id, options, T.Ct);

            for (int i = 0; i < commits; i++)
            {
                await dataset.CommitAsync(Quads(i), T.Ct);
                await dataset.MaintainAsync(T.Ct);
            }

            await dataset.CheckpointAsync(new Position(10), T.Ct);
        }

        // The shorter log beside the longer one's derived/, then beside the
        // other dataset's: the state names positions or a dataset this log
        // does not have, and the checkpoints another dataset's id.
        foreach (MemoryStorage donor in new[] { longer, other })
        {
            (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = await T.CopyLogAsync(shorter);
            List<KeyValuePair<BlobName, ReadOnlyMemory<byte>>> derived = [];

            foreach (BlobName name in await donor.Derived.ListAsync(T.Ct))
            {
                derived.Add(new(name, await T.ReadBlobAsync(donor, name)));
            }

            MemoryStorage mixed = MemoryStorage.FromLog(manifest, segments.Select(s => (ReadOnlyMemory<byte>)s), derived);
            await using Dataset opened = await Dataset.OpenAsync(mixed, options, T.Ct);
            Assert.Equal(new Position(12), opened.Head);
            using DatasetView view = opened.Pin();
            Assert.Equal(36, T.All(view).Count);

            if (donor == other)
            {
                Assert.Empty(opened.Checkpoints);
            }
        }
    }
}
