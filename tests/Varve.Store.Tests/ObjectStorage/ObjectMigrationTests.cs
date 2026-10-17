// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A dataset moved from a directory to a container, and back, opens and
/// answers identically at every position (#92, part A point 6): the
/// migration path and the backup path are one, <see cref="Dataset.ShipAsync"/>
/// into the other backend — the log, and the newest checkpoint's extents
/// with a catalog that names them.
/// </summary>
public sealed class ObjectMigrationTests
{
    internal const int Iterations = 40;

    [Fact]
    public async Task a_dataset_moved_from_a_directory_to_a_container_and_back_answers_identically()
    {
        await Gen.Select(Generators.Scripts, Gen.Int[0, 1000]).SampleAsync(
            async sample =>
            {
                (Script script, int choice) = sample;
                await using TemporaryDirectory directory = new(flushes: false);
                await using TemporaryDirectory back = new(flushes: false);
                await using ObjectHarness objects = new();

                await using Harness files = await Harness.StartAsync(maxRecordBytes: 256, segmentBytes: 2048, storage: await directory.OpenAsync());
                await files.RunAsync(script);
                long head = files.Model.Head;
                Position shipped = new(head == 0 ? 0 : choice % (head + 1));
                List<SortedSet<string>> expected = await ViewsAsync(files, files.Dataset, shipped.Value);

                // Directory to container.
                Backend container = await objects.OpenAsync(instance: "migrated");
                Assert.Equal(shipped, await files.Dataset.ShipAsync(container, shipped, T.Ct));
                await using Dataset inContainer = await T.OpenOrCreate(container, files.Options);
                Assert.Equal(shipped, inContainer.Head);
                Assert.Equal(expected, await ViewsAsync(files, inContainer, shipped.Value));

                // And back, as a backup is taken.
                FileStorage restored = await back.OpenAsync();

                await using (restored)
                {
                    Assert.Equal(shipped, await inContainer.ShipAsync(restored, shipped, T.Ct));
                    await using Dataset onFiles = await T.OpenOrCreate(restored, files.Options);
                    Assert.Equal(expected, await ViewsAsync(files, onFiles, shipped.Value));
                    Assert.Equal(inContainer.Checkpoints.Where(p => p <= shipped).TakeLast(1), onFiles.Checkpoints.TakeLast(1));
                }
            },
            iter: Iterations,
            print: sample => sample.Item1 + " shipped at choice " + sample.Item2);
    }

    private static async Task<List<SortedSet<string>>> ViewsAsync(Harness harness, Dataset dataset, long upTo)
    {
        List<SortedSet<string>> views = [];

        for (long q = 0; q <= upTo; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(harness.Rendered(view));
        }

        return views;
    }
}
