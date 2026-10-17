// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Checkpoints as extent sets (ADR 0138, ADR 0139): over generated histories
/// with retractions, checkpoints written at any position — above the newest,
/// or inside a set, which splits it — and dropped in any order, which merges
/// sets, the view at every position equals the reference model's, before and
/// after the dataset is opened again from the log and the extents alone.
/// </summary>
public sealed class ExtentTests
{
    private static readonly Gen<(bool Drop, int Choice)[]> Operations =
        Gen.Select(Gen.Frequency((3, Gen.Const(false)), (2, Gen.Const(true))), Gen.Int[0, 1000]).Array[1, 8];

    [Fact]
    public async Task an_extent_backed_view_equals_the_model_at_every_position_through_checkpoints_splits_and_merges()
    {
        await Gen.Select(Generators.CommitsOnly, Operations).SampleAsync(
            async sample =>
            {
                (Script script, (bool Drop, int Choice)[] operations) = sample;
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                if (head == 0)
                {
                    return;
                }

                foreach ((bool drop, int choice) in operations)
                {
                    IReadOnlyList<Position> checkpoints = harness.Dataset.Checkpoints;

                    if (drop && checkpoints.Count > 0)
                    {
                        await harness.Dataset.DropCheckpointAsync(checkpoints[choice % checkpoints.Count], T.Ct);
                    }
                    else
                    {
                        await harness.Dataset.CheckpointAsync(new Position(1 + (choice % head)), T.Ct);
                    }

                    await EveryPositionAsync(harness, harness.Dataset, "after " + (drop ? "a drop" : "a checkpoint"));
                }

                // The log and the extents alone: the catalog's chain is the checkpoints.
                List<byte[]> log = await LogPropertyTests.SegmentsAsync(harness.Storage);
                List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs = await T.CheckpointBlobsAsync(harness.Storage);
                MemoryStorage alone = MemoryStorage.FromLog(T.Manifest, log.Select(s => (ReadOnlyMemory<byte>)s), blobs.Select(b => new KeyValuePair<BlobName, ReadOnlyMemory<byte>>(b.Name, b.Bytes)));
                await using Dataset opened = await T.OpenOrCreate(alone, harness.Options);
                Assert.Equal(harness.Dataset.Checkpoints, opened.Checkpoints);
                await EveryPositionAsync(harness, opened, "opened from the extents");
            },
            iter: 60,
            print: sample => sample.Item1 + " then " + string.Join(", ", sample.Item2.Select(o => (o.Drop ? "drop " : "checkpoint ") + o.Choice)));
    }

    /// <summary>
    /// A set merged into the next by a drop reads, at every position of both
    /// windows, what the two sets read apart (ADR 0138): the older set's open
    /// intervals and the newer's closing ones meet.
    /// </summary>
    [Fact]
    public async Task dropping_every_checkpoint_but_the_newest_merges_the_chain_into_one_set_that_reads_the_same()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using Harness harness = await Harness.StartAsync();
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                for (long p = 1; p <= head; p++)
                {
                    await harness.Dataset.CheckpointAsync(new Position(p), T.Ct);
                }

                while (harness.Dataset.Checkpoints.Count > 1)
                {
                    await harness.Dataset.DropCheckpointAsync(harness.Dataset.Checkpoints[0], T.Ct);
                    await EveryPositionAsync(harness, harness.Dataset, "after merging " + harness.Dataset.Checkpoints.Count);
                }

                Assert.Equal(head == 0 ? [] : [new Position(head)], harness.Dataset.Checkpoints);
                Assert.Equal(head == 0 ? 0 : 1, (await harness.Storage.Derived.ListAsync(T.Ct)).Count(n => n.Value.StartsWith(ExtentSet.Prefix, StringComparison.Ordinal) && n.Value.EndsWith("/terms", StringComparison.Ordinal)));
            },
            iter: 60,
            print: script => script.ToString());
    }

    internal static async Task EveryPositionAsync(Harness harness, Dataset dataset, string when)
    {
        for (long q = 0; q <= harness.Model.Head; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            Harness.Same(Harness.Rendered(harness.Model.History[(int)q]), harness.Rendered(view), when + ", as of " + q);
        }
    }
}
