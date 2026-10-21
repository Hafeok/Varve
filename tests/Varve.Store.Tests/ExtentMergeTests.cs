// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Background merges of extent sets (ADR 0158) under the size-tiered policy
/// (ADR 0159): over generated histories with retractions and any schedule of
/// checkpoints, drops and merges, the view at every position and the head
/// equal the reference model's, before and after the dataset is opened again
/// from the log and the extents alone; and the policy keeps its bound on sets.
/// </summary>
public sealed class ExtentMergeTests
{
    // How many schedules the land gate runs; VARVE_EXTENT_SCHEDULES raises it
    // for the release gate (docs/testing.md).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_EXTENT_SCHEDULES"), out int n) ? n : 40;

    private enum Step
    {
        Checkpoint,
        Drop,
        Merge,
    }

    private static readonly Gen<(Step Step, int Choice)[]> Operations =
        Gen.Select(Gen.Frequency((4, Gen.Const(Step.Checkpoint)), (1, Gen.Const(Step.Drop)), (3, Gen.Const(Step.Merge))), Gen.Int[0, 1000]).Array[1, 14];

    /// <summary>
    /// Whatever sizes the sets have and whatever order they arrive in, the
    /// policy merged to exhaustion keeps at most (F − 1) sets per tier below
    /// the cap, plus the sets past half of it (ADR 0159, BoundOnSetsPerPermutation).
    /// </summary>
    [Fact]
    public void the_policy_keeps_its_bound_over_any_sequence_of_sets()
    {
        Gen.Select(Gen.Int[2, 8], Gen.Long[1, 64L << 20].Array[1, 300]).Sample(
            sample =>
            {
                (int fanOut, long[] arrivals) = sample;
                ExtentMergePolicy policy = new() { FanOut = fanOut, MaxSetBytes = new ByteCount(256L << 20) };
                List<long> sets = [];

                foreach (long bytes in arrivals)
                {
                    sets.Add(bytes);

                    while (policy.Choose(sets.ToArray()) is { } merge)
                    {
                        Assert.InRange(merge.Count, 2, fanOut);
                        long merged = sets.GetRange(merge.First, merge.Count).Sum();
                        Assert.True(merged <= policy.MaxSetBytes.Value, "a merge made a set past the cap");
                        sets.RemoveRange(merge.First, merge.Count);
                        sets.Insert(merge.First, merged);
                    }

                    // At most the bound below half the cap, and the sets past it
                    // fewer than twice the total over the cap (ADR 0159).
                    int full = sets.Count(policy.Full);
                    Assert.True(sets.Count - full <= policy.Bound, "the sets below half the cap pass the bound of " + policy.Bound + ": " + string.Join(",", sets));
                    Assert.True(full <= (2 * sets.Sum() / policy.MaxSetBytes.Value) + 1, "more sets past half the cap than the total allows");
                }
            },
            iter: 500);
    }

    /// <summary>
    /// The size tiers: 1 MiB and below is tier 0, and each tier up is F times larger.
    /// </summary>
    [Fact]
    public void a_tier_is_the_fan_out_logarithm_of_a_sets_bytes()
    {
        ExtentMergePolicy policy = new() { FanOut = 4 };
        Assert.Equal(0, policy.TierOf(1));
        Assert.Equal(0, policy.TierOf(1L << 20));
        Assert.Equal(1, policy.TierOf((1L << 20) + 1));
        Assert.Equal(1, policy.TierOf(4L << 20));
        Assert.Equal(2, policy.TierOf((4L << 20) + 1));
        Assert.Null(policy.Choose([1, 1, 1]));
        Assert.Equal((0, 4), policy.Choose([1, 1, 1, 1]));
        Assert.Equal((1, 4), policy.Choose([5L << 20, 1, 1, 1, 1]));

        // Sizes either side of a tier's edge are still one size.
        Assert.Equal((0, 4), policy.Choose([900_000, 1_100_000, 900_000, 1_100_000]));
        Assert.Null(ExtentMergePolicy.Off.Choose([1, 1, 1, 1]));
        Assert.Equal(15, policy.Bound);
    }

    [Fact]
    public async Task a_merged_projection_equals_the_model_at_every_position_across_any_schedule_of_checkpoints_drops_and_merges()
    {
        await Gen.Select(Generators.CommitsOnly, Operations, Gen.Int[2, 4]).SampleAsync(
            async sample =>
            {
                (Script script, (Step Step, int Choice)[] operations, int fanOut) = sample;
                await using Harness harness = await Harness.StartAsync(configure: options => Merging(options, fanOut));
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                if (head == 0)
                {
                    return;
                }

                foreach ((Step step, int choice) in operations)
                {
                    IReadOnlyList<Position> checkpoints = harness.Dataset.Checkpoints;

                    switch (step)
                    {
                        case Step.Drop when checkpoints.Count > 0:
                            await harness.Dataset.DropCheckpointAsync(checkpoints[choice % checkpoints.Count], T.Ct);
                            break;
                        case Step.Merge:
                            await harness.Dataset.MergeOnceForTestsAsync(T.Ct);
                            break;
                        default:
                            await harness.Dataset.CheckpointAsync(new Position(1 + (choice % head)), T.Ct);
                            break;
                    }

                    await ExtentTests.EveryPositionAsync(harness, harness.Dataset, "after " + step);
                    await HeadAsync(harness, harness.Dataset, "after " + step);
                }

                // Merged to exhaustion, the chain keeps the policy's bound.
                while (await harness.Dataset.MergeOnceForTestsAsync(T.Ct))
                {
                }

                Assert.Null(new ExtentMergePolicy { FanOut = fanOut, MaxSetBytes = new ByteCount(1L << 20) }.Choose(harness.Dataset.SetsForTests().Select(s => s.Bytes).ToArray()));
                await ExtentTests.EveryPositionAsync(harness, harness.Dataset, "merged to exhaustion");

                // The log and the extents alone.
                List<byte[]> log = await LogPropertyTests.SegmentsAsync(harness.Storage);
                List<(BlobName Name, ReadOnlyMemory<byte> Bytes)> blobs = await T.CheckpointBlobsAsync(harness.Storage);
                MemoryStorage alone = MemoryStorage.FromLog(T.Manifest, log.Select(s => (ReadOnlyMemory<byte>)s), blobs.Select(b => new KeyValuePair<BlobName, ReadOnlyMemory<byte>>(b.Name, b.Bytes)));
                await using Dataset opened = await T.OpenOrCreate(alone, harness.Options);
                Assert.Equal(harness.Dataset.Checkpoints, opened.Checkpoints);
                await ExtentTests.EveryPositionAsync(harness, opened, "opened from the merged extents");

                // A storage with one reader deletes what no catalog names at once.
                HashSet<string> named = [.. harness.Dataset.SetsForTests().Select(s => s.From + "-" + s.To)];
                Assert.Equal(named.Count, (await harness.Storage.Derived.ListAsync(T.Ct)).Count(b => b.Value.StartsWith(ExtentSet.Prefix, StringComparison.Ordinal) && b.Value.EndsWith("/terms", StringComparison.Ordinal)));
            },
            iter: Schedules,
            print: sample => sample.Item1 + " then " + string.Join(", ", sample.Item2.Select(o => o.Step + " " + o.Choice)) + " at fan-out " + sample.Item3);
    }

    /// <summary>
    /// The rebase (ADR 0158, MergeRebasesOnTheCatalog): a checkpoint that
    /// lands above the chain while a merge runs is kept, and the merge's swap
    /// keeps it; a checkpoint that splits an input, or a drop of one, abandons
    /// the merge, and its set is never named.
    /// </summary>
    [Theory]
    [InlineData("above", true)]
    [InlineData("split", false)]
    [InlineData("drop", false)]
    public async Task a_checkpoint_during_a_merge_is_kept_and_one_that_touches_an_input_abandons_it(string meanwhile, bool merges)
    {
        Dataset? dataset = null;
        int paused = 0;

        await using Harness harness = await Harness.StartAsync(configure: options => new DatasetOptions
        {
            Clock = options.Clock,
            MaxRecordBytes = options.MaxRecordBytes,
            SegmentBytes = options.SegmentBytes,
            CommitCache = options.CommitCache,
            Maintenance = MaintenanceMode.Off,
            ExtentMerges = new ExtentMergePolicy { FanOut = 2, MaxSetBytes = new ByteCount(1L << 20) },
            BeforeMergeSwap = async token =>
            {
                if (Interlocked.Increment(ref paused) != 1)
                {
                    return;
                }

                switch (meanwhile)
                {
                    case "above":
                        await dataset!.CheckpointAsync(dataset.Head, token);
                        break;
                    case "split":
                        await dataset!.CheckpointAsync(new Position(1), token);
                        break;
                    default:
                        await dataset!.DropCheckpointAsync(dataset.Checkpoints[0], token);
                        break;
                }
            },
        });
        dataset = harness.Dataset;

        for (int i = 0; i < 6; i++)
        {
            await harness.StepAsync(new CommitStep([new OpSpec(true, new IriSpec("s" + i), new IriSpec("p0"), new IriSpec("o" + i), 0, -1)], 1, Expect.None, ValidatorKind.None, Pin: false));
        }

        // Two sets of one tier, (0, 2] and (2, 4]; the head, 6, is past them.
        await dataset.CheckpointAsync(new Position(2), T.Ct);
        await dataset.CheckpointAsync(new Position(4), T.Ct);
        (long From, long To, long Bytes)[] before = dataset.SetsForTests();

        bool swapped = await dataset.MergeOnceForTestsAsync(T.Ct);

        Assert.Equal(merges, swapped);
        (long From, long To, long Bytes)[] after = dataset.SetsForTests();

        // Kept: the checkpoint above the chain; split: (0, 1], (1, 2],
        // (2, 4]; the drop of 2 merges (0, 2] into (2, 4] itself.
        (long, long)[] expected = meanwhile switch
        {
            "above" => [(0, 4), (4, 6)],
            "split" => [(0, 1), (1, 2), (2, 4)],
            _ => [(0, 4)],
        };
        Assert.Equal(expected, after.Select(s => (s.From, s.To)));
        Assert.Equal([(0L, 2L), (2L, 4L)], before.Select(s => (s.From, s.To)));

        await ExtentTests.EveryPositionAsync(harness, dataset, "after the merge and " + meanwhile);
        await HeadAsync(harness, dataset, "after the merge and " + meanwhile);

        // What an abandoned merge wrote is named by no catalog, and a storage
        // with one reader has deleted it once nothing holds it.
        int sets = (await harness.Storage.Derived.ListAsync(T.Ct)).Count(b => b.Value.StartsWith(ExtentSet.Prefix, StringComparison.Ordinal) && b.Value.EndsWith("/terms", StringComparison.Ordinal));
        Assert.Equal(after.Length, sets);
    }

    /// <summary>
    /// A merge replaces the projection's base runs when they are its inputs
    /// (ADR 0158): a read of the present then merges the fewer runs at once,
    /// and still equals the model.
    /// </summary>
    [Fact]
    public async Task a_merge_replaces_the_inputs_in_the_projections_runs()
    {
        await using Harness harness = await Harness.StartAsync(configure: options => Merging(options, 2));
        Dataset dataset = harness.Dataset;

        for (int i = 0; i < 4; i++)
        {
            await harness.StepAsync(new CommitStep([new OpSpec(true, new IriSpec("s" + i), new IriSpec("p0"), new IriSpec("o" + i), 0, -1)], 1, Expect.None, ValidatorKind.None, Pin: false));
        }

        await dataset.CheckpointAsync(new Position(2), T.Ct);
        await dataset.CheckpointAsync(new Position(4), T.Ct);

        // Opened again, the chain is the projection's base.
        await using Dataset reopened = await T.OpenOrCreate(harness.Storage, harness.Options);
        await harness.Dataset.DisposeAsync();
        Assert.True(await reopened.MergeOnceForTestsAsync(T.Ct));
        Assert.Equal([(0L, 4L)], reopened.SetsForTests().Select(s => (s.From, s.To)));
        await HeadAsync(harness, reopened, "after the merge");
        await ExtentTests.EveryPositionAsync(harness, reopened, "after the merge");
    }

    internal static DatasetOptions Merging(DatasetOptions options, int fanOut) => new()
    {
        Clock = options.Clock,
        MaxRecordBytes = options.MaxRecordBytes,
        SegmentBytes = options.SegmentBytes,
        CommitCache = options.CommitCache,
        Maintenance = MaintenanceMode.Off,
        ExtentMerges = new ExtentMergePolicy { FanOut = fanOut, MaxSetBytes = new ByteCount(1L << 20) },
    };

    internal static Task HeadAsync(Harness harness, Dataset dataset, string when)
    {
        using DatasetView view = dataset.Pin();
        Harness.Same(Harness.Rendered(harness.Model.History[(int)harness.Model.Head]), harness.Rendered(view), when + ", at the head");
        return Task.CompletedTask;
    }
}
