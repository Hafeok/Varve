// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Seek, don't narrow (ADR 0177): an as-of read inside a format-5 extent
/// set's window looks its keys up in the set and classifies only their
/// rows, writes nothing, and answers what narrowing answered — by scan, by
/// lookup and by count — at every position, across any schedule of
/// checkpoints, drops and merges. A format-4 set is still narrowed, and
/// maintenance rewrites it as format 5.
/// </summary>
public sealed class ExtentSeekTests
{
    // How many schedules the land gate runs; VARVE_EXTENT_SCHEDULES raises it
    // for the release gate (docs/testing.md), as for the merge property.
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_EXTENT_SCHEDULES"), out int n) ? n : 40;

    private static readonly Gen<(int Step, int Choice)[]> Operations =
        Gen.Select(Gen.Frequency((4, Gen.Const(0)), (1, Gen.Const(1)), (3, Gen.Const(2))), Gen.Int[0, 1000]).Array[1, 10];

    [Fact]
    public async Task a_sought_set_answers_scans_lookups_and_counts_as_the_model_at_every_position_and_writes_nothing()
    {
        await Gen.Select(Generators.CommitsOnly, Operations, Gen.Int[2, 4]).SampleAsync(
            async sample =>
            {
                (Script script, (int Step, int Choice)[] operations, int fanOut) = sample;
                await using Harness harness = await Harness.StartAsync(configure: options => ExtentMergeTests.Merging(options, fanOut));
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

                    Assert.All(harness.Dataset.SeekableSetsForTests(), Assert.True);
                    await EveryPositionAsync(harness, harness.Dataset, "after step " + step);
                }
            },
            iter: Schedules,
            print: sample => sample.Item1 + " then " + string.Join(", ", sample.Item2.Select(o => o.Step + " " + o.Choice)) + " at fan-out " + sample.Item3);
    }

    /// <summary>
    /// A dataset an earlier release wrote, its sets in format 4: as-of reads
    /// inside them narrow, as they did, and equal the model; maintenance then
    /// rewrites each set alone as format 5, the oldest first, and the reads
    /// are unchanged (ADR 0177's upgrade).
    /// </summary>
    [Fact]
    public async Task a_format_4_set_is_narrowed_until_maintenance_rewrites_it_as_format_5()
    {
        await Gen.Select(Generators.CommitsOnly, Gen.Int[1, 1000].Array[1, 4]).SampleAsync(
            async sample =>
            {
                (Script script, int[] choices) = sample;
                await using Harness harness = await Harness.StartAsync(configure: options =>
                {
                    DatasetOptions merging = ExtentMergeTests.Merging(options, 4);
                    merging.ExtentVersion = 4;
                    return merging;
                });
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                if (head == 0)
                {
                    return;
                }

                foreach (int choice in choices)
                {
                    await harness.Dataset.CheckpointAsync(new Position(1 + (choice % head)), T.Ct);
                }

                Assert.All(harness.Dataset.SeekableSetsForTests(), Assert.False);
                await EveryPositionAsync(harness, harness.Dataset, "format 4", sought: false);

                // This release opens it: the sets are read as they are, then rewritten.
                DatasetOptions upgraded = ExtentMergeTests.Merging(harness.Options, 4);
                await harness.Dataset.DisposeAsync();
                await using Dataset opened = await T.OpenOrCreate(harness.Storage, upgraded);
                await EveryPositionAsync(harness, opened, "opened by this release", sought: false);
                int rewrites = 0;

                while (await opened.MergeOnceForTestsAsync(T.Ct))
                {
                    rewrites++;
                }

                Assert.True(rewrites >= 1);
                Assert.All(opened.SeekableSetsForTests(), Assert.True);
                await EveryPositionAsync(harness, opened, "rewritten as format 5");
            },
            iter: 20,
            print: sample => sample.Item1 + " checkpoints " + string.Join(",", sample.Item2));
    }

    /// <summary>
    /// The pages a point lookup touches do not grow with the set: one quad
    /// read as of a position inside a set of 2,000 quads and inside one of
    /// 16,000 reads the same few blocks and column entries (ADR 0177's
    /// O(matches)). Narrowing read every row of the set.
    /// </summary>
    [Fact]
    public async Task a_point_lookup_inside_a_set_reads_the_same_bytes_whatever_the_sets_size()
    {
        long small = await LookupBytesAsync(2_000);
        long large = await LookupBytesAsync(16_000);

        Assert.InRange(small, 1, 64 * 1024);
        Assert.InRange(large, 1, 64 * 1024);
        Assert.True(large <= small * 2, "a lookup inside the larger set read " + large + " bytes against " + small);
    }

    /// <summary>
    /// Found by <c>ExtentDamageTests</c>' undamaged check: quads asserted at a
    /// set's start, retracted inside it and asserted again have two rows
    /// within under one key, and a lookup's lower bound found the second of
    /// them by a search that stops at any equal key, so it never saw that the
    /// quad was held at the start, and the older set answered for it. Twenty
    /// such quads put the duplicates where a search meets the second.
    /// </summary>
    [Fact]
    public async Task a_quad_retracted_and_asserted_again_inside_a_set_is_looked_up_as_the_scan_finds_it()
    {
        await using Dataset dataset = await T.OpenOrCreate(new MemoryStorage(), ExtentMergeTests.Merging(T.Options(), 4));
        CommitRequestBuilder asserted = new();
        CommitRequestBuilder retracted = new();

        for (int i = 0; i < 20; i++)
        {
            asserted.Assert(T.Iri("s" + i), T.Iri("p"), T.Iri("o"));
            retracted.Retract(T.Iri("s" + i), T.Iri("p"), T.Iri("o"));
        }

        await dataset.CommitAsync(asserted, T.Ct);
        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("x"), T.Iri("p"), T.Iri("o")), T.Ct);
        await dataset.CommitAsync(retracted, T.Ct);
        await dataset.CommitAsync(asserted, T.Ct);
        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        Assert.All(dataset.SeekableSetsForTests(), Assert.True);

        // At 3 the twenty are retracted: inside the set (1, 4].
        using DatasetView view = await dataset.AsOfAsync(new Position(3), T.Ct);
        Assert.Single(T.All(view));
        Assert.True(view.TryInternalise(T.Iri("p"), out TermHandle p));
        Assert.True(view.TryInternalise(T.Iri("o"), out TermHandle o));

        for (int i = 0; i < 20; i++)
        {
            Assert.True(view.TryInternalise(T.Iri("s" + i), out TermHandle s));
            Quad quad = new(s, p, o);
            Assert.False(view.Contains(in quad), "s" + i + " is retracted at 3");
        }
    }

    /// <summary>
    /// A count of a range holding more of a sought set's rows than it reads
    /// one by one is estimated, and says so; the scan is still exact.
    /// </summary>
    [Fact]
    public async Task a_count_over_many_of_a_sought_sets_rows_is_estimated_and_its_scan_is_exact()
    {
        await using Dataset dataset = await T.OpenOrCreate(new MemoryStorage(), ExtentMergeTests.Merging(T.Options(), 4));
        int quads = (int)ExtentSeek.ExactCountRows + 1000;
        await CommitQuadsAsync(dataset, quads, batches: 4, retractEvery: 7);
        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        long inside = dataset.Head.Value / 2;

        using DatasetView view = await dataset.AsOfAsync(new Position(inside), T.Ct);
        CardinalityEstimate all = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        int scanned = T.Drain(view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any)).Count;

        Assert.False(all.IsExact);
        Assert.False(all.IsUnknown);
        Assert.Equal(await NarrowedCountAsync(dataset, inside), scanned);

        // A subject's few rows are still counted exactly.
        Assert.True(view.TryInternalise(T.Iri("s1"), out TermHandle s1));
        CardinalityEstimate one = view.Estimate(s1, TermHandle.None, TermHandle.None, GraphPattern.Any);
        Assert.True(one.IsExact);
        Assert.Equal(T.Drain(view.Match(s1, TermHandle.None, TermHandle.None, GraphPattern.Any)).Count, one.Count.Value);
    }

    /// <summary>
    /// Every view of every position equals the model's by scan; every quad
    /// any view holds is looked up in every view and found exactly where the
    /// scan found it; and the estimate of each quad's subject is exact and
    /// is the scan's count. With <paramref name="sought"/>, no read wrote a
    /// narrowed run.
    /// </summary>
    internal static async Task EveryPositionAsync(Harness harness, Dataset dataset, string when, bool sought = true)
    {
        int runsBefore = await NarrowedRunsAsync(harness.Storage);
        List<(DatasetView View, HashSet<Quad> Quads)> views = [];

        try
        {
            for (long q = 0; q <= harness.Model.Head; q++)
            {
                DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
                views.Add((view, [.. T.All(view)]));
                Harness.Same(Harness.Rendered(harness.Model.History[(int)q]), harness.Rendered(view), when + ", as of " + q);
            }

            if (sought)
            {
                Assert.Equal(runsBefore, await NarrowedRunsAsync(harness.Storage));
            }

            HashSet<Quad> every = [.. views.SelectMany(v => v.Quads)];

            foreach ((DatasetView view, HashSet<Quad> quads) in views)
            {
                foreach (Quad quad in every)
                {
                    Assert.True(quads.Contains(quad) == view.Contains(in quad), when + ", as of " + view.Position + ": the lookup disagrees with the scan");
                }

                foreach (TermHandle subject in every.Select(q => q.Subject).Distinct())
                {
                    CardinalityEstimate estimate = view.Estimate(subject, TermHandle.None, TermHandle.None, GraphPattern.Any);
                    Assert.True(estimate.IsExact, when + ", as of " + view.Position + ": a subject's estimate is not exact");
                    Assert.Equal(quads.Count(q => q.Subject == subject), estimate.Count.Value);
                }
            }
        }
        finally
        {
            foreach ((DatasetView view, _) in views)
            {
                view.Dispose();
            }
        }
    }

    // The narrowed runs an as-of read wrote beside the projection's runs, still there.
    private static async Task<int> NarrowedRunsAsync(IStorage storage) =>
        (await storage.Derived.ListAsync(T.Ct)).Count(b => b.Value.Contains("asof-", StringComparison.Ordinal));

    private static async Task<long> LookupBytesAsync(int quads)
    {
        CountingStorage storage = new(new MemoryStorage());
        await using Dataset dataset = await T.OpenOrCreate(storage, ExtentMergeTests.Merging(T.Options(), 4));
        await CommitQuadsAsync(dataset, quads, batches: 8, retractEvery: 5);
        await dataset.CheckpointAsync(dataset.Head, T.Ct);
        long inside = dataset.Head.Value / 2;

        using DatasetView view = await dataset.AsOfAsync(new Position(inside), T.Ct);
        Assert.True(view.TryInternalise(T.Iri("s" + (quads / 3)), out TermHandle s));
        Assert.True(view.TryInternalise(T.Iri("p"), out TermHandle p));
        Assert.True(view.TryInternalise(T.Iri("o"), out TermHandle o));
        Quad quad = new(s, p, o);
        _ = view.Contains(in quad);

        long before = storage.BytesRead;
        _ = view.Contains(in quad);
        return storage.BytesRead - before;
    }

    // Quads s0..s(n-1) p o, asserted in batches; every retractEvery-th of a
    // batch is retracted in the next commit, so the set has rows within.
    private static async Task CommitQuadsAsync(Dataset dataset, int quads, int batches, int retractEvery)
    {
        int per = quads / batches;

        for (int b = 0; b < batches; b++)
        {
            CommitRequestBuilder asserted = new();

            for (int i = b * per; i < (b + 1) * per; i++)
            {
                asserted.Assert(T.Iri("s" + i), T.Iri("p"), T.Iri("o"));
            }

            await dataset.CommitAsync(asserted, T.Ct);
            CommitRequestBuilder retracted = new();

            for (int i = b * per; i < (b + 1) * per; i += retractEvery)
            {
                retracted.Retract(T.Iri("s" + i), T.Iri("p"), T.Iri("o"));
            }

            await dataset.CommitAsync(retracted, T.Ct);
        }
    }

    // The quads at a position, from the log alone.
    private static async Task<int> NarrowedCountAsync(Dataset dataset, long position)
    {
        QuadDelta delta = await dataset.DiffAsync(new Position(0), new Position(position), T.Ct);
        return delta.Asserted.Length;
    }

    /// <summary>A storage whose derived blobs count the bytes read from them.</summary>
    private sealed class CountingStorage(IStorage inner) : IStorage
    {
        private readonly long[] _read = new long[1];

        public ISegmentStore Log => inner.Log;

        public IDerivedStore Derived => new Counted(inner.Derived, this);

        internal long BytesRead => Interlocked.Read(ref _read[0]);

        private sealed class Counted(IDerivedStore store, CountingStorage counter) : IDerivedStore
        {
            public ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken) => store.CreateAsync(name, cancellationToken);

            public async ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken) =>
                new Blob(await store.OpenAsync(name, cancellationToken).ConfigureAwait(false), counter);

            public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) => store.DeleteAsync(name, cancellationToken);

            public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);
        }

        private sealed class Blob(IReadableBlob inner, CountingStorage counter) : IReadableBlob
        {
            public ByteCount Length => inner.Length;

            public int Read(ByteOffset offset, Span<byte> destination)
            {
                int read = inner.Read(offset, destination);
                Interlocked.Add(ref counter._read[0], read);
                return read;
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
