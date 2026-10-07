// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The commit table, measured through the public members that read it (issue
/// #61): a dataset of many single-quad commits on files, closed and opened
/// again; the managed heap the open dataset holds; and the time and the bytes
/// allocated per call of the readers that resolve a position or a timestamp
/// — <c>PositionAt</c>, <c>SettingsAtAsync</c>, <c>DiffAsync</c> over ten
/// commits, and a subscription resumed from a position for ten commits.
/// </summary>
internal static class CommitIndexBench
{
    private const int Calls = 20_000;

    internal static async Task RunAsync(int commits)
    {
        string directory = Directory.CreateTempSubdirectory("varve-commits-").FullName;
        Stepping clock = new();
        DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off };
        DatasetId id = new(Guid.NewGuid());

        await using (FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }))
        {
            StoreDatasetType writing = await StoreDatasetType.CreateAsync(storage, id, options);
            Stopwatch building = Stopwatch.StartNew();

            for (int c = 0; c < commits; c++)
            {
                CommitRequest request = new();
                request.Assert(Term("s", c % 1000), Term("p", c % 7), Term("o", c));
                await writing.CommitAsync(request);
            }

            await writing.MaintainAsync();
            await writing.CheckpointAsync(writing.Head);
            await writing.DisposeAsync();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# commit table: {commits:N0} single-quad commits on files, built in {building.Elapsed.TotalSeconds:F0} s"));
        }

        await using FileStorage reopened = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        long heapBefore = Heap();
        Stopwatch opening = Stopwatch.StartNew();
        StoreDatasetType dataset = await StoreDatasetType.OpenAsync(reopened, options);
        double openSeconds = opening.Elapsed.TotalSeconds;
        long heapAfter = Heap();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"open: {openSeconds:F2} s; managed heap held by the open dataset {(heapAfter - heapBefore) / 1e6:F1} MB, {(double)(heapAfter - heapBefore) / commits:F1} B a commit"));

        Random random = new(61);
        long head = dataset.Head.Value;

        await Measure("PositionAt(timestamp)", () =>
        {
            long step = random.NextInt64(0, head + 2);
            Position found = dataset.PositionAt(new CommitTimestamp(Stepping.Start.AddTicks(step * Stepping.Step)));
            return found.Value <= head ? ValueTask.CompletedTask : throw new InvalidOperationException("PositionAt found " + found.Value);
        });

        await Measure("SettingsAtAsync(position)", async () => await dataset.SettingsAtAsync(new Position(random.NextInt64(0, head + 1))));

        await Measure("DiffAsync over 10 commits", async () =>
        {
            long from = random.NextInt64(0, head - 10);
            QuadDelta delta = await dataset.DiffAsync(new Position(from), new Position(from + 10));
            _ = delta.Count;
        });

        await Measure("Subscribe from a position, 10 commits", async () =>
        {
            long from = random.NextInt64(0, head - 10);
            int seen = 0;

            await foreach (Commit commit in dataset.Subscribe(new Position(from), SubscriptionFilter.All))
            {
                if (++seen == 10)
                {
                    break;
                }
            }
        });

        await dataset.DisposeAsync();

        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    private static async Task Measure(string what, Func<ValueTask> call)
    {
        for (int i = 0; i < 1000; i++)
        {
            await call();
        }

        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch clock = Stopwatch.StartNew();

        for (int i = 0; i < Calls; i++)
        {
            await call();
        }

        double micros = clock.Elapsed.TotalMilliseconds * 1000 / Calls;
        long bytes = (GC.GetTotalAllocatedBytes(precise: true) - allocated) / Calls;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {micros:F2} us a call, {bytes:N0} B allocated a call"));
    }

    private static long Heap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static RdfTerm Term(string prefix, long n) =>
        RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/" + prefix + n.ToString(CultureInfo.InvariantCulture)));

    // Each commit a millisecond after the one before, so every timestamp names one position.
    private sealed class Stepping : TimeProvider
    {
        internal const long Step = TimeSpan.TicksPerMillisecond;

        internal static readonly DateTimeOffset Start = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

        private long _ticks = Start.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Add(ref _ticks, Step), TimeSpan.Zero);
    }
}
