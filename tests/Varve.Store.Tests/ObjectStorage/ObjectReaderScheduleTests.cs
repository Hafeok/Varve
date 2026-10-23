// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The deletion rule of reader announcements (ADR 0160), over generated
/// schedules of readers attaching, pinning, crashing, restarting and being
/// forgotten, of a writer that checkpoints, drops, merges, deletes and holds
/// views of several catalog versions at once, and of a second writer taking
/// the epoch over while the first still reads: no extent a live announced
/// catalog version names is ever deleted, no read of a reader that was not
/// forgotten fails or differs from the model, and every set no catalog names
/// is deleted once no record holds it. Then a fault at every write and delete
/// the workload makes, under <c>derived/</c> and <c>readers/</c>.
/// </summary>
public sealed class ObjectReaderScheduleTests
{
    // The land gate's count; VARVE_READER_SCHEDULES raises it for the release
    // gate (docs/testing.md).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_READER_SCHEDULES"), out int n) ? n : 60;

    private const int Readers = 3;
    private const int Commits = 12;

    private enum Op
    {
        Checkpoint,
        Drop,
        Merge,
        Delete,
        WriterPin,
        WriterRelease,
        Attach,
        Pin,
        Crash,
        Close,
        Forget,
        Supersede,
        Superseded,
    }

    private static readonly Gen<(Op Op, int Choice)[]> Schedule = Gen.Select(
        Gen.Frequency(
            (4, Gen.Const(Op.Checkpoint)), (1, Gen.Const(Op.Drop)), (4, Gen.Const(Op.Merge)), (3, Gen.Const(Op.Delete)),
            (2, Gen.Const(Op.WriterPin)), (2, Gen.Const(Op.WriterRelease)),
            (3, Gen.Const(Op.Attach)), (2, Gen.Const(Op.Pin)), (1, Gen.Const(Op.Crash)), (1, Gen.Const(Op.Close)), (1, Gen.Const(Op.Forget)),
            (1, Gen.Const(Op.Supersede)), (2, Gen.Const(Op.Superseded))),
        Gen.Int[0, 1000]).Array[4, 30];

    [Fact]
    public async Task no_extent_a_live_announced_version_names_is_ever_deleted_and_every_unnamed_one_is_deleted_once_no_record_holds_it()
    {
        await Schedule.SampleAsync(
            async operations =>
            {
                await using World world = await World.StartAsync();

                foreach ((Op op, int choice) in operations)
                {
                    await world.ApplyAsync(op, choice);
                    world.AssertLiveReadersWhole(op);
                }

                await world.FinishAsync();
            },
            iter: Schedules,
            print: operations => string.Join(", ", operations.Select(o => o.Op + " " + o.Choice)));
    }

    /// <summary>
    /// A fault at every write and delete the maintenance workload makes —
    /// extents, catalog swaps, reader records and deletions — before or
    /// after the store applies it, after which the writer can write nothing
    /// more: the follower attached before still reads the model at every
    /// position, the next writer opens on a catalog whose every set is whole,
    /// and once the dead writer's record is forgotten every unnamed set goes.
    /// </summary>
    [Fact]
    public async Task a_fault_at_any_write_or_delete_never_shows_a_reader_a_catalog_naming_a_missing_extent()
    {
        (int writes, List<SortedSet<string>> expected) = await FaultedRunAsync(null, null, full: true);
        Assert.True(writes > 30, writes + " writes");

        // Every injection point, each checked for whole catalogs; every
        // eighth also read at every position, all of them under
        // VARVE_EXTENT_FAULTS_FULL (the release gate's run, docs/testing.md).
        bool everyPoint = Environment.GetEnvironmentVariable("VARVE_EXTENT_FAULTS_FULL") is "1";

        for (int at = 1; at <= writes; at++)
        {
            foreach (bool afterApply in new[] { false, true })
            {
                await FaultedRunAsync((at, afterApply), expected, full: everyPoint || at % 8 == 0);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine("writes and deletes: " + writes + ", injection points: " + (writes * 2));
    }

    // The workload with a follower attached throughout; with a fault, the
    // writer's every write from the faulted one on is refused, as a writer
    // that died there. Returns the writes a clean run makes.
    private static async Task<(int Writes, List<SortedSet<string>> Expected)> FaultedRunAsync((int At, bool AfterApply)? fault, List<SortedSet<string>>? known, bool full)
    {
        MemoryObjectStore store = new();

        // Retries of a refused write wait on a clock that never waits.
        await using ObjectHarness harness = new(store, waits: new InstantClock(DateTimeOffset.UnixEpoch));
        Backend storage = await harness.OpenAsync(instance: "faulted", reader: new ReaderId("faulted-writer"));
        Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
        await ObjectExtentTests.CommitHistoryAsync(writer, Commits);
        await writer.CheckpointAsync(new Position(2), T.Ct);
        await writer.CheckpointAsync(new Position(4), T.Ct);
        List<SortedSet<string>> expected = known ?? await ObjectExtentTests.ViewsAsync(writer);

        Backend reading = await harness.OpenAsync(ObjectStorageAccess.ReadOnly, cacheBytes: 1, reader: new ReaderId("follower"));
        await using Dataset follower = await Dataset.OpenAsync(reading, T.Options(), T.Ct);
        int count = 0;
        bool failing = false;

        store.Hook = (operation, name) =>
        {
            bool counted = operation is ObjectOperation.Create or ObjectOperation.Replace or ObjectOperation.Delete
                && (name.Value.StartsWith("derived/", StringComparison.Ordinal) || name.Value == "readers/faulted-writer");

            if (!counted)
            {
                return null;
            }

            if (failing)
            {
                throw new IOException("injected: the writer is gone");
            }

            if (fault is { } f && ++count == f.At)
            {
                failing = true;

                if (f.AfterApply)
                {
                    return () => throw new IOException("injected: lost after the store applied it");
                }

                throw new IOException("injected: before the store applied it");
            }

            if (fault is null)
            {
                count++;
            }

            return null;
        };

        try
        {
            await writer.CheckpointAsync(new Position(6), T.Ct);
            await writer.CheckpointAsync(new Position(8), T.Ct);

            while (await writer.MergeOnceForTestsAsync(T.Ct))
            {
                await writer.DeleteUnnamedForTestsAsync(T.Ct);
            }

            await writer.CheckpointAsync(new Position(10), T.Ct);
            await writer.DropCheckpointAsync(new Position(10), T.Ct);
            await writer.DeleteUnnamedForTestsAsync(T.Ct);
        }
        catch (IOException) when (fault is not null)
        {
        }
        catch (WriterSupersededException) when (fault is not null)
        {
        }

        // The follower attached before the fault: every set its catalog
        // names is whole; it is read in full once the run is over.
        store.Hook = null;
        ExtentMaintenanceCases.AssertChainWhole(store, follower, "after the fault at " + fault);

        if (fault is null)
        {
            Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(follower));
            await writer.DisposeAsync();
            return (count, expected);
        }

        // The writer died there: its record stays. The next writer opens on a
        // whole catalog and serves the same.
        Backend next = await harness.OpenAsync(instance: "next", reader: new ReaderId("next-writer"));
        await using Dataset current = await T.OpenOrCreate(next, ExtentMaintenanceCases.Merging());
        ExtentMaintenanceCases.AssertChainWhole(store, current, "the next writer's open after the fault at " + fault);

        if (full)
        {
            Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(current));
        }

        while (await current.MergeOnceForTestsAsync(T.Ct))
        {
        }

        await current.DeleteUnnamedForTestsAsync(T.Ct);
        ExtentMaintenanceCases.AssertChainWhole(store, follower, "after the next writer's merges, the fault at " + fault);

        if (full)
        {
            Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(follower));
        }

        // The dead writer forgotten and the follower closed: nothing is held.
        foreach (ReaderRecord record in await Backend.ReadReadersAsync(store, null, T.Ct))
        {
            if (record.Reader.Value == "faulted-writer")
            {
                Assert.True(await Backend.ForgetReaderAsync(store, null, record.Reader, record.Oldest, record.Newest, harness.Clock, T.Ct));
            }
        }

        await follower.DisposeAsync();
        await reading.DisposeAsync();
        await current.DeleteUnnamedForTestsAsync(T.Ct);
        Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(store, current));
        GC.KeepAlive(writer);
        return (count, expected);
    }

    /// <summary>
    /// One schedule's instances over one store: a writer, perhaps a writer it
    /// superseded that still holds views, and readers by id.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private readonly List<(Dataset Dataset, DatasetView View, long Position)> _writerViews = [];
        private readonly Reader?[] _readers = new Reader?[Readers];
        private readonly List<Reader> _crashed = [];
        private readonly List<(Backend Storage, Dataset Dataset)> _superseded = [];
        private Backend _storage;
        private Dataset _writer;
        private int _writers = 1;

        private World(ObjectHarness harness, Backend storage, Dataset writer, List<SortedSet<string>> expected)
        {
            Harness = harness;
            _storage = storage;
            _writer = writer;
            Expected = expected;
        }

        private ObjectHarness Harness { get; }

        private List<SortedSet<string>> Expected { get; }

        internal static async Task<World> StartAsync()
        {
            ObjectHarness harness = new();
            Backend storage = await harness.OpenAsync(instance: "writer-1", reader: new ReaderId("writer-1"));
            Dataset writer = await T.OpenOrCreate(storage, ExtentMaintenanceCases.Merging());
            await ObjectExtentTests.CommitHistoryAsync(writer, Commits);
            List<SortedSet<string>> expected = await ObjectExtentTests.ViewsAsync(writer);
            return new World(harness, storage, writer, expected);
        }

        internal async Task ApplyAsync(Op op, int choice)
        {
            int r = choice % Readers;

            switch (op)
            {
                case Op.Checkpoint:
                    await _writer.CheckpointAsync(new Position(1 + (choice % Commits)), T.Ct);
                    break;
                case Op.Drop when _writer.Checkpoints.Count > 0:
                    await _writer.DropCheckpointAsync(_writer.Checkpoints[choice % _writer.Checkpoints.Count], T.Ct);
                    break;
                case Op.Merge:
                    await _writer.MergeOnceForTestsAsync(T.Ct);
                    break;
                case Op.Delete:
                    await _writer.DeleteUnnamedForTestsAsync(T.Ct);
                    break;
                case Op.WriterPin:
                    long at = choice % (Commits + 1);
                    _writerViews.Add((_writer, await _writer.AsOfAsync(new Position(at), T.Ct), at));
                    break;
                case Op.WriterRelease when _writerViews.Count > 0:
                    (Dataset _, DatasetView view, long position) = _writerViews[choice % _writerViews.Count];
                    _writerViews.RemoveAt(choice % _writerViews.Count);
                    Assert.Equal(Expected[(int)position], T.Terms(view));
                    view.Dispose();
                    break;
                case Op.Attach when _readers[r] is null:
                    _readers[r] = await Reader.AttachAsync(Harness, r);
                    break;
                case Op.Pin when _readers[r] is { } pinning:
                    long q = choice % (Commits + 1);

                    try
                    {
                        pinning.Views.Add((await pinning.Dataset.AsOfAsync(new Position(q), T.Ct), q));
                    }
                    catch (ReaderForgottenException) when (pinning.Forgotten)
                    {
                        // Forgotten, it may find an extent gone: failed, never wrong (ADR 0160).
                    }

                    break;
                case Op.Crash when _readers[r] is { } crashing:
                    // Never disposed: its record stays, as a process that died leaves it.
                    _crashed.Add(crashing);
                    _readers[r] = null;
                    break;
                case Op.Close when _readers[r] is { } closing:
                    await closing.ReadAsync(Expected);
                    await closing.DisposeAsync();
                    _readers[r] = null;
                    break;
                case Op.Forget:
                    await ForgetAsync(r);
                    break;
                case Op.Superseded when _superseded.Count > 0:
                    // A writer that lost the epoch keeps trying: its creates
                    // land, and its swaps are refused (ADR 0158). The writer
                    // that holds the epoch never fails because of them.
                    Dataset old = _superseded[choice % _superseded.Count].Dataset;

                    try
                    {
                        if (choice % 2 == 0)
                        {
                            await old.CheckpointAsync(new Position(1 + (choice % Commits)), T.Ct);
                        }
                        else
                        {
                            await old.MergeOnceForTestsAsync(T.Ct);
                        }
                    }
                    catch (WriterSupersededException)
                    {
                    }

                    break;
                case Op.Supersede:
                    // A second writer claims the epoch; the first stays open,
                    // reading what its views hold (ADR 0136, ADR 0160).
                    _superseded.Add((_storage, _writer));
                    _writers++;
                    _storage = await Harness.OpenAsync(instance: "writer-" + _writers, reader: new ReaderId("writer-" + _writers));
                    _writer = await T.OpenOrCreate(_storage, ExtentMaintenanceCases.Merging());
                    break;
            }
        }

        // The forget of an operator who decided reader r is gone: alive or
        // crashed, its record goes, and an alive one is forgotten.
        private async Task ForgetAsync(int r)
        {
            ReaderId id = Reader.Id(r);

            if ((await Backend.ReadReadersAsync(Harness.Store, null, T.Ct)).FirstOrDefault(x => x.Reader == id) is not { } record)
            {
                return;
            }

            Assert.True(await Backend.ForgetReaderAsync(Harness.Store, null, id, record.Oldest, record.Newest, Harness.Clock, T.Ct));

            if (_readers[r] is { } alive)
            {
                alive.Forgotten = true;
            }
        }

        // No extent a live announced catalog version names is gone: every set
        // a reader that was not forgotten attached, and every set the writers
        // name, is whole in the store.
        internal void AssertLiveReadersWhole(Op after)
        {
            HashSet<string> names = [.. Harness.Store.Names()];

            foreach (Reader? reader in _readers)
            {
                if (reader is { Forgotten: false })
                {
                    foreach (string set in reader.Dataset.SetNamesForTests())
                    {
                        AssertWhole(names, set, "after " + after + ", reader " + reader.Index + "'s set");
                    }
                }
            }

            foreach (string set in _writer.SetNamesForTests())
            {
                AssertWhole(names, set, "after " + after + ", the writer's set");
            }
        }

        private static void AssertWhole(HashSet<string> names, string set, string what)
        {
            foreach (string part in new[] { "order0", "order1", "order2", "order3", "order4", "order5", "terms" })
            {
                Assert.True(names.Contains("derived/" + set + part), what + " " + set + " lost its " + part);
            }
        }

        // Every view still held reads the model; every live reader too; then
        // with every instance closed or forgotten, every set no catalog names
        // is deleted.
        internal async Task FinishAsync()
        {
            foreach ((Dataset _, DatasetView view, long position) in _writerViews)
            {
                Assert.Equal(Expected[(int)position], T.Terms(view));
                view.Dispose();
            }

            _writerViews.Clear();

            for (int r = 0; r < Readers; r++)
            {
                if (_readers[r] is { } reader)
                {
                    await reader.ReadAsync(Expected);
                    await reader.DisposeAsync();
                    _readers[r] = null;
                }
            }

            foreach ((Backend storage, Dataset dataset) in _superseded)
            {
                Assert.Equal(Expected, await ObjectExtentTests.ViewsAsync(dataset));
                await dataset.DisposeAsync();
                await storage.DisposeAsync();
            }

            _superseded.Clear();

            // The crashed readers' records, forgotten by the operator.
            foreach (ReaderRecord record in await Backend.ReadReadersAsync(Harness.Store, null, T.Ct))
            {
                if (record.Reader != _storage.Reader)
                {
                    Assert.True(await Backend.ForgetReaderAsync(Harness.Store, null, record.Reader, record.Oldest, record.Newest, Harness.Clock, T.Ct));
                }
            }

            await _writer.DeleteUnnamedForTestsAsync(T.Ct);

            // The writer's own record still holds what its projection reads —
            // a dropped newest set stays its base until it is merged away. Its
            // restart is the event that lets that go (ADR 0160): the open
            // deletes what no record holds.
            await _writer.DisposeAsync();
            await _storage.DisposeAsync();
            _storage = await Harness.OpenAsync(instance: "writer-" + _writers, reader: new ReaderId("writer-" + _writers));
            _writer = await T.OpenOrCreate(_storage, ExtentMaintenanceCases.Merging());
            Assert.Equal(0, ExtentMaintenanceCases.UnnamedSets(Harness.Store, _writer));
            Assert.Equal(Expected, await ObjectExtentTests.ViewsAsync(_writer));
        }

        public async ValueTask DisposeAsync()
        {
            foreach ((Dataset _, DatasetView view, long _) in _writerViews)
            {
                view.Dispose();
            }

            foreach (Reader? reader in _readers)
            {
                if (reader is not null)
                {
                    await reader.DisposeAsync();
                }
            }

            await _writer.DisposeAsync();
            await Harness.DisposeAsync();
            GC.KeepAlive(_crashed);
        }
    }

    private sealed class Reader : IAsyncDisposable
    {
        private Reader(int index, Backend storage, Dataset dataset)
        {
            Index = index;
            Storage = storage;
            Dataset = dataset;
        }

        internal int Index { get; }

        internal Backend Storage { get; }

        internal Dataset Dataset { get; }

        internal List<(DatasetView View, long Position)> Views { get; } = [];

        internal bool Forgotten { get; set; }

        internal static ReaderId Id(int index) => new("reader-" + index);

        // A restart of the same id replaces the record a crash left.
        internal static async Task<Reader> AttachAsync(ObjectHarness harness, int index)
        {
            Backend storage = await Backend.OpenAsync(harness.Options(ObjectStorageAccess.ReadOnly, cacheBytes: 1, reader: Id(index)), T.Ct);
            return new Reader(index, storage, await Dataset.OpenAsync(storage, T.Options(), T.Ct));
        }

        // Every view it holds and every position: the model's, or — once
        // forgotten — failed with reader-forgotten; never anything else.
        internal async Task ReadAsync(List<SortedSet<string>> expected)
        {
            foreach ((DatasetView view, long position) in Views)
            {
                try
                {
                    Assert.Equal(expected[(int)position], T.Terms(view));
                }
                catch (ReaderForgottenException) when (Forgotten)
                {
                }
            }

            if (Forgotten)
            {
                await ExtentMaintenanceCases.ModelOrFailureAsync<ReaderForgottenException>(Dataset, expected);
            }
            else
            {
                Assert.Equal(expected, await ObjectExtentTests.ViewsAsync(Dataset));
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach ((DatasetView view, long _) in Views)
            {
                view.Dispose();
            }

            Views.Clear();
            await Dataset.DisposeAsync();
            await Storage.DisposeAsync();
        }
    }
}
