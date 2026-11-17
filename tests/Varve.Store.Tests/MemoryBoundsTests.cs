// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// What a dataset holds while it writes, checkpoints and reopens is bounded
/// by its memtable's limit (ADR 0244, #227), by the store's own count and
/// with no collector involved: a writer cannot outrun the flush, an open
/// replays through the same limit and keeps nothing of its scan, a
/// checkpoint spills its window's terms, and a checkpoint has the
/// memtable flushed.
/// </summary>
public sealed class MemoryBoundsTests
{
    private const int Limit = 400;

    // Fifty quads on fifty new subjects: a hundred memtable entries.
    private static CommitRequestBuilder Batch(int number)
    {
        CommitRequestBuilder request = new();

        for (int i = 0; i < 50; i++)
        {
            request.Assert(T.Iri("s" + number.ToString(CultureInfo.InvariantCulture) + "-" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("o"));
        }

        return request;
    }

    private const int BatchEntries = 100;

    private static DatasetOptions Options(MaintenanceMode maintenance, int limit = Limit) => new()
    {
        Clock = ManualClock.Epoch(),
        Maintenance = maintenance,
        MemtableLimit = new QuadCount(limit),
    };

    /// <summary>
    /// With the flush held, commits are taken until the memtable holds twice
    /// its limit, and the next waits; it is written once the flush has made
    /// room. The memtable never holds more than twice its limit and a commit.
    /// </summary>
    [Fact]
    public async Task a_commit_waits_for_the_flush_once_the_memtable_holds_twice_its_limit()
    {
        GatedDerived storage = new(new MemoryStorage());
        await using Dataset dataset = await T.OpenOrCreate(storage, Options(MaintenanceMode.Background));
        storage.Close();
        int number = 0;

        while (dataset.MemtableForTests() < Dataset.WaitBoundFactor * Limit)
        {
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Batch(number++), T.Ct)).Outcome);
        }

        long held = dataset.MemtableForTests();
        Assert.InRange(held, Dataset.WaitBoundFactor * Limit, (Dataset.WaitBoundFactor * Limit) + BatchEntries);
        await storage.Held.Task.WaitAsync(TimeSpan.FromSeconds(30), T.Ct);

        Task<CommitResult> waiting = dataset.CommitAsync(Batch(number++), T.Ct).AsTask();
        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(300), T.Ct)));
        Assert.Equal(held, dataset.MemtableForTests());

        storage.Open();
        Assert.Equal(CommitOutcome.Committed, (await waiting.WaitAsync(TimeSpan.FromSeconds(30), T.Ct)).Outcome);
        Assert.True(dataset.MemtableForTests() < held, "the flush the commit waited for did not shrink the memtable");
    }

    /// <summary>
    /// A writer that commits as fast as it can, with every flush slow: the
    /// memtable never holds more than twice its limit and one commit.
    /// </summary>
    [Fact]
    public async Task a_writer_faster_than_maintenance_never_holds_more_than_twice_the_limit_and_a_commit()
    {
        GatedDerived storage = new(new MemoryStorage()) { Delay = TimeSpan.FromMilliseconds(15) };
        await using Dataset dataset = await T.OpenOrCreate(storage, Options(MaintenanceMode.Background));
        long peak = 0;

        for (int number = 0; number < 120; number++)
        {
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Batch(number), T.Ct)).Outcome);
            peak = Math.Max(peak, dataset.MemtableForTests());
        }

        Assert.InRange(peak, Limit, (Dataset.WaitBoundFactor * Limit) + BatchEntries);
        using DatasetView view = dataset.Pin();
        Assert.Equal(120 * 50, T.All(view).Count);
    }

    /// <summary>
    /// Nothing waits where waiting could not end: with the host running
    /// maintenance itself, and while spills fail, a commit is taken whatever
    /// the memtable holds, as before.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_commit_does_not_wait_when_no_flush_can_come(bool spillsFail)
    {
        DatasetOptions options = Options(spillsFail ? MaintenanceMode.Background : MaintenanceMode.Off);
        options.SpillFault = () => spillsFail;
        Dataset dataset = await T.OpenOrCreate(new MemoryStorage(), options);

        try
        {
            for (int number = 0; number < 20; number++)
            {
                Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Batch(number), T.Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(30), T.Ct)).Outcome);
            }

            Assert.True(dataset.MemtableForTests() >= 3 * Limit, "the memtable holds " + dataset.MemtableForTests() + ": it was flushed, so the test waits for nothing");
        }
        finally
        {
            // Closing reports the spills that failed, as it always has.
            if (spillsFail)
            {
                await Assert.ThrowsAsync<AggregateException>(async () => await dataset.DisposeAsync());
            }
            else
            {
                await dataset.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Opening a log with several memtables' worth above derived/ replays it
    /// through the limit: the memtable never holds more than its limit and a
    /// commit, the runs it flushed are in derived/, and the dataset answers
    /// what it held. With the bodies' budget at one commit, the rest are read
    /// from the log again, and the answer is the same.
    /// </summary>
    [Theory]
    [InlineData(8L << 20)]
    [InlineData(1L)]
    public async Task an_open_replays_through_the_memtables_limit_and_answers_the_same(long bodiesBudget)
    {
        MemoryStorage storage = new();

        await using (Dataset written = await T.OpenOrCreate(storage, Options(MaintenanceMode.Off)))
        {
            for (int number = 0; number < 40; number++)
            {
                await written.CommitAsync(Batch(number), T.Ct);
            }

            Assert.InRange(written.MemtableForTests(), 40 * BatchEntries, (40 * BatchEntries) + 8);
        }

        DatasetOptions options = Options(MaintenanceMode.Background);
        options.OpenBodiesBudget = bodiesBudget;
        await using Dataset dataset = await Dataset.OpenAsync(storage, options, T.Ct);

        Assert.InRange(dataset.OpenMemtablePeakForTests(), Limit, Limit + BatchEntries);
        Assert.True(dataset.MemtableForTests() <= Limit + BatchEntries, "the open left " + dataset.MemtableForTests() + " entries in the memtable");
        Assert.Contains(await storage.Derived.ListAsync(T.Ct), name => name.Value.StartsWith("index/runs/", StringComparison.Ordinal));
        Assert.Equal(40, dataset.Head.Value);

        using DatasetView view = dataset.Pin();
        Assert.Equal(40 * 50, T.All(view).Count);
    }

    /// <summary>
    /// The open flushes only where the dataset runs its own maintenance: with
    /// it off the host flushes, and the open writes no run, as before.
    /// </summary>
    [Fact]
    public async Task an_open_with_maintenance_off_writes_no_run()
    {
        MemoryStorage storage = new();

        await using (Dataset written = await T.OpenOrCreate(storage, Options(MaintenanceMode.Off)))
        {
            for (int number = 0; number < 20; number++)
            {
                await written.CommitAsync(Batch(number), T.Ct);
            }
        }

        await using Dataset dataset = await Dataset.OpenAsync(storage, Options(MaintenanceMode.Off), T.Ct);
        Assert.InRange(dataset.MemtableForTests(), 20 * BatchEntries, (20 * BatchEntries) + 8);
        Assert.DoesNotContain(await storage.Derived.ListAsync(T.Ct), name => name.Value.StartsWith("index/runs/", StringComparison.Ordinal));
    }

    /// <summary>
    /// A checkpoint spills its window's terms once it holds its budget of
    /// them, and writes the set's dictionary from the spills: every term
    /// resolves, at the head, as of any position, and after the dataset is
    /// opened again from derived/, and no spill is left behind.
    /// </summary>
    [Fact]
    public async Task a_checkpoint_that_spills_its_windows_terms_writes_the_same_dictionary()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                MemoryStorage storage = new();
                await using Harness harness = await Harness.StartAsync(storage: storage, configure: options => Spilling(options, termSpillBytes: 512));
                await harness.RunAsync(script);

                if (harness.Model.Head == 0)
                {
                    return;
                }

                await harness.Dataset.CheckpointAsync(harness.Dataset.Head, T.Ct);
                await ExtentSeekTests.EveryPositionAsync(harness, harness.Dataset, "after a checkpoint whose terms were spilled");
                Assert.DoesNotContain(await storage.Derived.ListAsync(T.Ct), name => name.Value.StartsWith("bulk/", StringComparison.Ordinal));

                await harness.Dataset.DisposeAsync();
                await using Dataset reopened = await T.OpenOrCreate(harness.Storage, harness.Options);
                await ExtentSeekTests.EveryPositionAsync(harness, reopened, "after reopening on the checkpoint");
            },
            iter: 40,
            print: script => script.ToString());
    }

    /// <summary>
    /// A checkpoint releases what it makes unnecessary: where the dataset
    /// runs its own maintenance, the memtable is flushed once the
    /// checkpoint is published, though it holds less than its limit, and the
    /// dataset answers what it did. With maintenance off nothing is flushed.
    /// </summary>
    [Theory]
    [InlineData(MaintenanceMode.Background, true)]
    [InlineData(MaintenanceMode.Off, false)]
    public async Task a_checkpoint_has_the_datasets_own_maintenance_flush_the_memtable(MaintenanceMode maintenance, bool flushed)
    {
        await using Dataset dataset = await T.OpenOrCreate(new MemoryStorage(), Options(maintenance, limit: 100_000));

        for (int number = 0; number < 10; number++)
        {
            await dataset.CommitAsync(Batch(number), T.Ct);
        }

        long held = dataset.MemtableForTests();
        Assert.InRange(held, 10 * BatchEntries, 100_000 - 1);
        await dataset.CheckpointAsync(dataset.Head, T.Ct);

        // The background round the checkpoint started, and whatever is still due.
        await dataset.MaintainAsync(T.Ct);
        Assert.Equal(flushed ? 0 : held, dataset.MemtableForTests());

        using DatasetView view = dataset.Pin();
        Assert.Equal(10 * 50, T.All(view).Count);
        using DatasetView then = await dataset.AsOfAsync(new Position(4), T.Ct);
        Assert.Equal(4 * 50, T.All(then).Count);
    }

    private static DatasetOptions Spilling(DatasetOptions options, long termSpillBytes)
    {
        DatasetOptions spilling = new()
        {
            Clock = options.Clock,
            MaxRecordBytes = options.MaxRecordBytes,
            SegmentBytes = options.SegmentBytes,
            CommitCache = options.CommitCache,
            Maintenance = MaintenanceMode.Off,
        };
        spilling.CheckpointTermSpillBytes = termSpillBytes;
        return spilling;
    }

    /// <summary>A storage whose derived writes wait for the test, or take a while.</summary>
    private sealed class GatedDerived(MemoryStorage inner) : IStorage, IDerivedStore
    {
        private TaskCompletionSource _gate = Opened();

        /// <summary>Completed when a write is being held at the gate.</summary>
        public TaskCompletionSource Held { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>What every write of a run waits, with the gate open.</summary>
        public TimeSpan Delay { get; init; }

        public ISegmentStore Log => inner.Log;

        public IDerivedStore Derived => this;

        public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _gate.TrySetResult();

        public async ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken)
        {
            if (name.Value.StartsWith("index/runs/", StringComparison.Ordinal))
            {
                if (!_gate.Task.IsCompleted)
                {
                    Held.TrySetResult();
                }

                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                }
            }

            return await inner.Derived.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken) => inner.Derived.OpenAsync(name, cancellationToken);

        public ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken) => inner.Derived.DeleteAsync(name, cancellationToken);

        public ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken) => inner.Derived.ListAsync(cancellationToken);

        private static TaskCompletionSource Opened()
        {
            TaskCompletionSource open = new(TaskCreationOptions.RunContinuationsAsynchronously);
            open.SetResult();
            return open;
        }
    }
}
