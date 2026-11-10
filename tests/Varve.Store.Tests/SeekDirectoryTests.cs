// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Nothing is resident per commit (ADR 0240, #222): an extent's seek
/// directory, which grows with its set's history, is read when an as-of
/// read inside the set's window needs it and kept in a cache bounded in
/// bytes, never held with the set. So what the dataset holds for its sets
/// is the same after twice the commits, a read answers the same whatever
/// the cache holds, and a commit index blob's timestamps are found without
/// its fences in memory.
/// </summary>
public sealed class SeekDirectoryTests
{
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_EXTENT_SCHEDULES"), out int n) ? n : 40;

    private static readonly Gen<(int Step, int Choice)[]> Operations =
        Gen.Select(Gen.Frequency((4, Gen.Const(0)), (1, Gen.Const(1)), (3, Gen.Const(2))), Gen.Int[0, 1000]).Array[1, 10];

    /// <summary>
    /// The property of ADR 0177, with the cache keeping nothing, keeping
    /// about one small directory, and keeping a few: scans, lookups and
    /// counts equal the model at every position across any schedule of
    /// checkpoints, drops and merges, and the cache never holds more than
    /// its ceiling.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(96L)]
    [InlineData(1024L)]
    public async Task a_sought_set_answers_as_the_model_whatever_the_seek_directory_cache_keeps(long ceiling)
    {
        await Gen.Select(Generators.CommitsOnly, Operations, Gen.Int[2, 4]).SampleAsync(
            async sample =>
            {
                (Script script, (int Step, int Choice)[] operations, int fanOut) = sample;
                await using Harness harness = await Harness.StartAsync(configure: options => Caching(ExtentMergeTests.Merging(options, fanOut), ceiling));
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                if (head == 0)
                {
                    return;
                }

                foreach ((int step, int choice) in operations)
                {
                    IReadOnlyList<Position> checkpoints = harness.Dataset.Checkpoints;

                    if (step == 1 && checkpoints.Count > 0)
                    {
                        await harness.Dataset.DropCheckpointAsync(checkpoints[choice % checkpoints.Count], T.Ct);
                    }
                    else if (step == 2)
                    {
                        await harness.Dataset.MergeOnceForTestsAsync(T.Ct);
                    }
                    else
                    {
                        await harness.Dataset.CheckpointAsync(new Position(1 + (choice % head)), T.Ct);
                    }

                    await ExtentSeekTests.EveryPositionAsync(harness, harness.Dataset, "after step " + step + " with a cache of " + ceiling);
                    Assert.InRange(harness.Dataset.SeekDirectoryBytes, 0, ceiling);
                    Assert.False(harness.Dataset.AnySetHoldsItsSeekDirectoryForTests(), "a set of the chain holds its seek directory outside the cache");
                }
            },
            iter: Math.Max(1, Schedules / 4),
            print: sample => sample.Item1 + " then " + string.Join(", ", sample.Item2.Select(o => o.Step + " " + o.Choice)) + " at fan-out " + sample.Item3);
    }

    /// <summary>
    /// The gate of #222 by the store's own accounting: on a dataset that
    /// stays the same size, the seek directories of the chain grow with the
    /// commits, and what the dataset holds of them does not: after N commits
    /// and after 2N it is under the ceiling, with as-of reads at every
    /// checkpointed window in between.
    /// </summary>
    [Fact]
    public async Task what_is_held_of_the_seek_directories_is_bounded_by_the_ceiling_while_the_chain_s_grow_with_the_commits()
    {
        const long Ceiling = 2048;
        const int Commits = 600;
        MemoryStorage storage = new();
        DatasetOptions options = Caching(
            new DatasetOptions
            {
                Clock = ManualClock.Epoch(),
                Maintenance = MaintenanceMode.Off,
                MemtableLimit = new QuadCount(200),
                ExtentMerges = new ExtentMergePolicy { Enabled = false },
            },
            Ceiling);
        await using Dataset dataset = await T.OpenOrCreate(storage, options);

        async Task<(long Held, long Chain)> AfterAsync(int from, int to)
        {
            for (int c = from; c < to; c++)
            {
                // Forty quads that are there every other commit: the dataset
                // is steady, and every change is a row within some window.
                CommitRequestBuilder request = new();

                for (int i = 0; i < 40; i++)
                {
                    RdfTerm subject = T.Iri("s" + i.ToString(CultureInfo.InvariantCulture));
                    _ = c % 2 == 0 ? request.Assert(subject, T.Iri("p"), T.Literal("o")) : request.Retract(subject, T.Iri("p"), T.Literal("o"));
                }

                await dataset.CommitAsync(request, T.Ct);
                await dataset.MaintainAsync(T.Ct);

                if ((c + 1) % 100 == 0)
                {
                    await dataset.CheckpointAsync(dataset.Head, T.Ct);
                }
            }

            // A read inside every set's window, in two orders.
            for (long position = 1; position < dataset.Head.Value; position += 37)
            {
                using DatasetView view = await dataset.AsOfAsync(new Position(position), T.Ct);
                _ = T.All(view).Count;
                view.TryInternalise(T.Iri("p"), out TermHandle predicate);
                _ = view.Estimate(TermHandle.None, predicate, TermHandle.None, GraphPattern.Any);
                Assert.InRange(dataset.SeekDirectoryBytes, 0, Ceiling);
            }

            return (dataset.SeekDirectoryBytes, dataset.SeekDirectoryBytesOfTheChainForTests());
        }

        (long heldAtN, long chainAtN) = await AfterAsync(0, Commits);
        (long heldAt2N, long chainAt2N) = await AfterAsync(Commits, 2 * Commits);

        Assert.True(chainAtN > 2 * Ceiling, "the chain's seek directories, " + chainAtN + " bytes, do not exceed the ceiling: the test bounds nothing");
        Assert.True(chainAt2N >= chainAtN + (chainAtN / 2), "the chain's seek directories did not grow with the commits: " + chainAtN + " then " + chainAt2N);
        Assert.InRange(heldAtN, 0, Ceiling);
        Assert.InRange(heldAt2N, 0, Ceiling);
        Assert.False(dataset.AnySetHoldsItsSeekDirectoryForTests(), "a set of the chain holds its seek directory outside the cache");
    }

    /// <summary>
    /// A directory is read from its extent once and then found in the
    /// cache; with a ceiling of zero every read reads it again.
    /// </summary>
    [Theory]
    [InlineData(32L << 20, true)]
    [InlineData(0L, false)]
    public async Task a_directory_is_read_once_when_the_cache_keeps_it_and_at_every_read_when_it_keeps_none(long ceiling, bool kept)
    {
        MemoryStorage storage = new();
        DatasetOptions options = Caching(new DatasetOptions { Clock = ManualClock.Epoch(), Maintenance = MaintenanceMode.Off }, ceiling);
        await using Dataset dataset = await T.OpenOrCreate(storage, options);

        for (int c = 0; c < 6; c++)
        {
            CommitRequestBuilder request = new();
            _ = c % 2 == 0 ? request.Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")) : request.Retract(T.Iri("s"), T.Iri("p"), T.Literal("o"));
            request.Assert(T.Iri("s" + c.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("o"));
            await dataset.CommitAsync(request, T.Ct);
        }

        await dataset.CheckpointAsync(dataset.Head, T.Ct);

        long LoadsOfARead()
        {
            long before = dataset.SeekDirectoryLoadsForTests();
            using DatasetView view = dataset.AsOfAsync(new Position(3), T.Ct).AsTask().GetAwaiter().GetResult();
            Assert.Equal(4, T.All(view).Count);
            return dataset.SeekDirectoryLoadsForTests() - before;
        }

        long first = LoadsOfARead();
        long second = LoadsOfARead();
        Assert.True(first > 0, "a read inside a set's window read no seek directory");
        Assert.Equal(kept ? 0 : first, second);
        Assert.Equal(kept, dataset.SeekDirectoryBytes > 0);
    }

    /// <summary>
    /// The cache itself: the most recently used directories up to the
    /// ceiling, the least recently used out first, and one extent's larger
    /// than the ceiling given to its read and not kept.
    /// </summary>
    [Fact]
    public async Task the_cache_keeps_the_most_recently_used_directories_up_to_its_ceiling()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await T.OpenOrCreate(storage, new DatasetOptions { Clock = ManualClock.Epoch(), Maintenance = MaintenanceMode.Off });

        for (int c = 0; c < 4; c++)
        {
            CommitRequestBuilder request = new();
            _ = c % 2 == 0 ? request.Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")) : request.Retract(T.Iri("s"), T.Iri("p"), T.Literal("o"));
            await dataset.CommitAsync(request, T.Ct);
        }

        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        ExtentInfo[] extents = [.. dataset.ExtentsOfTheChainForTests()];
        long one = extents[0].SeekDirectoryBytes;
        Assert.All(extents, e => Assert.Equal(one, e.SeekDirectoryBytes));

        SeekDirectoryCache two = new(new ByteCount(2 * one));
        ExtentInfo first = two.Get(extents[0]);
        _ = two.Get(extents[1]);
        Assert.Same(first, two.Get(extents[0]));
        Assert.Equal(2, two.Loads);

        // A third pushes out the least recently used, which is the second.
        _ = two.Get(extents[2]);
        Assert.Equal(2 * one, two.Bytes.Value);
        Assert.Same(first, two.Get(extents[0]));
        Assert.Equal(3, two.Loads);
        _ = two.Get(extents[1]);
        Assert.Equal(4, two.Loads);

        two.Forget(extents[1]);
        Assert.Equal(one, two.Bytes.Value);

        SeekDirectoryCache tooSmall = new(new ByteCount(one - 1));
        Assert.True(tooSmall.Get(extents[0]).HoldsSeekDirectory);
        Assert.Equal(0, tooSmall.Bytes.Value);
    }

    /// <summary>
    /// A seek directory that no longer matches the check its extent's open
    /// took is damage, never a different answer (ADR 0213): the read that
    /// asks for it is refused with the damage, which a dataset's read
    /// answers from the log.
    /// </summary>
    [Fact]
    public async Task a_seek_directory_changed_since_its_extent_was_opened_is_damage()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await T.OpenOrCreate(storage, new DatasetOptions { Clock = ManualClock.Epoch(), Maintenance = MaintenanceMode.Off });

        for (int c = 0; c < 4; c++)
        {
            CommitRequestBuilder request = new();
            _ = c % 2 == 0 ? request.Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")) : request.Retract(T.Iri("s"), T.Iri("p"), T.Literal("o"));
            await dataset.CommitAsync(request, T.Ct);
        }

        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        ExtentInfo extent = dataset.ExtentsOfTheChainForTests()[0];
        Assert.True(extent.WithSeekDirectory().HoldsSeekDirectory);

        foreach (ExtentInfo changed in dataset.ExtentsOfTheChainForTests().Select(e => e.WithSeekDirectoryChecksOffByOneForTests()))
        {
            Assert.Throws<DerivedDamageException>(() => changed.WithSeekDirectory());
        }
    }

    /// <summary>
    /// A commit index blob's fences are not in memory (ADR 0240): the
    /// position at a timestamp is found in the entries, and is what it was.
    /// </summary>
    [Fact]
    public async Task a_position_at_a_timestamp_is_found_in_the_paged_entries_without_their_fences()
    {
        MemoryStorage storage = new();
        ManualClock clock = ManualClock.Epoch();
        DatasetOptions options = new() { Clock = clock, Maintenance = MaintenanceMode.Off, CommitCache = 8 };
        await using Dataset dataset = await T.OpenOrCreate(storage, options);
        List<CommitTimestamp> stamps = [];

        // A thousand commits a second apart: several blocks of 128 entries, paged out as they go.
        for (int c = 0; c < 1000; c++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            CommitRequestBuilder request = new();
            request.Assert(T.Iri("s" + c.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("o"));
            await dataset.CommitAsync(request, T.Ct);
            stamps.Add(dataset.TimestampAt(dataset.Head));

            if (c % 50 == 49)
            {
                await dataset.MaintainAsync(T.Ct);
            }
        }

        Assert.True(dataset.CommitsForTests().InMemory < 1000, "no entry was paged out");

        for (int c = 0; c < stamps.Count; c += 7)
        {
            Assert.Equal(c + 1, dataset.PositionAt(stamps[c]).Value);
            Assert.Equal(c + 1, dataset.PositionAt(new CommitTimestamp(stamps[c].Value.AddMilliseconds(500))).Value);
        }

        Assert.Equal(0, dataset.PositionAt(new CommitTimestamp(stamps[0].Value.AddSeconds(-1))).Value);
        Assert.Equal(1000, dataset.PositionAt(new CommitTimestamp(stamps[^1].Value.AddDays(1))).Value);
    }

    private static DatasetOptions Caching(DatasetOptions options, long ceiling) => new()
    {
        Clock = options.Clock,
        MaxRecordBytes = options.MaxRecordBytes,
        SegmentBytes = options.SegmentBytes,
        CommitCache = options.CommitCache,
        Maintenance = options.Maintenance,
        MemtableLimit = options.MemtableLimit,
        ExtentMerges = options.ExtentMerges,
        SeekDirectoryCache = new ByteCount(ceiling),
    };
}
