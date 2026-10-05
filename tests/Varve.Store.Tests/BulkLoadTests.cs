// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Varve.Store.Tests.Faults;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>The bulk loader (ADRs 0076, 0077, 0081).</summary>
public class BulkLoadTests
{
    /// <summary>An operation over a small vocabulary, so that loads meet the dataset's terms and quads.</summary>
    internal sealed record Op(bool Assert, int S, int P, int O, int G);

    private static readonly Gen<Op> Ops =
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 7], Gen.Int[0, 2], Gen.Int[0, 15], Gen.Int[0, 2])
            .Select(x => new Op(x.Item1 != 0, x.Item2, x.Item3, x.Item4, x.Item5));

    private static RdfTerm Subject(int s) => s switch
    {
        < 5 => T.Iri("s" + s.ToString(CultureInfo.InvariantCulture)),
        _ => RdfTerm.BlankNode(Encoding.UTF8.GetBytes("x" + (s - 5).ToString(CultureInfo.InvariantCulture))),
    };

    private static RdfTerm Object(int o) => o switch
    {
        < 3 => T.Iri("s" + o.ToString(CultureInfo.InvariantCulture)),
        < 6 => T.Literal("plain " + o.ToString(CultureInfo.InvariantCulture)),
        < 8 => T.Lang("tagged " + o.ToString(CultureInfo.InvariantCulture), "en-gb"),
        < 10 => T.Integer(o.ToString(CultureInfo.InvariantCulture)),
        10 => T.Integer("0" + o.ToString(CultureInfo.InvariantCulture)),
        11 => RdfTerm.BlankNode("x1"u8),
        12 => RdfTerm.TripleTerm(T.Iri("s1"), T.Iri("p0"), T.Literal("quoted")),
        13 => RdfTerm.TripleTerm(T.Iri("s2"), T.Iri("p1"), RdfTerm.TripleTerm(T.Iri("s0"), T.Iri("p0"), T.Integer("1"))),
        14 => T.Iri("new/" + new string('n', 300)),
        _ => RdfTerm.Literal("2026-10-05"u8, T.Iri("date")),
    };

    private static RdfTerm? Graph(int g) => g == 0 ? null : T.Iri("g" + g.ToString(CultureInfo.InvariantCulture));

    private static RdfTerm Predicate(int p) => T.Iri("p" + p.ToString(CultureInfo.InvariantCulture));

    private static CommitRequest Request(IEnumerable<Op> ops)
    {
        CommitRequest request = new();

        foreach (Op op in ops)
        {
            if (op.Assert)
            {
                request.Assert(Subject(op.S), Predicate(op.P), Object(op.O), Graph(op.G) is { } g ? g : RequestTerm.None);
            }
            else
            {
                request.Retract(Subject(op.S), Predicate(op.P), Object(op.O), Graph(op.G) is { } g ? g : RequestTerm.None);
            }
        }

        return request;
    }

    private static void Feed(BulkLoad load, IEnumerable<Op> ops)
    {
        foreach (Op op in ops)
        {
            if (op.Assert)
            {
                load.Assert(Subject(op.S), Predicate(op.P), Object(op.O), Graph(op.G));
            }
            else
            {
                load.Retract(Subject(op.S), Predicate(op.P), Object(op.O), Graph(op.G));
            }
        }
    }

    private static string Canonical(IQuadSource source) => Encoding.UTF8.GetString(RdfCanonicaliser.Canonicalise(source).NQuads.Span);

    private static DatasetOptions Options(IReadOnlyList<ICommitValidator>? validators = null) => new()
    {
        Clock = ManualClock.Epoch(),
        MaxRecordBytes = new ByteCount(64),
        SegmentBytes = new ByteCount(1024),
        MemtableLimit = new QuadCount(8),
        Maintenance = MaintenanceMode.Off,
        Validators = validators ?? [],
    };

    // Small enough that every load spills many runs, and merges more than 64 of them in passes.
    private static BulkLoadOptions Tiny => new() { SortRecords = 3, TermBytes = 64 };

    private static async Task<Dataset> PriorAsync(IStorage storage, Op[][] prior, IReadOnlyList<ICommitValidator>? validators = null)
    {
        Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(validators), T.Ct);

        foreach (Op[] commit in prior)
        {
            await dataset.CommitAsync(Request(commit), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        return dataset;
    }

    /// <summary>
    /// A bulk load is one commit equal to the same operations committed the
    /// ordinary way (ADR 0076): the same outcome, the same quads up to blank
    /// node naming, and the same dictionary counters — so the same terms
    /// allocated and no others (I3) — on top of any prior state, and again
    /// after reopening.
    /// </summary>
    [Fact]
    public async Task a_bulk_load_equals_the_same_operations_committed_the_ordinary_way()
    {
        Gen<Op[][]> prior = Ops.Array[0, 12].Array[0, 4];

        await Gen.Select(prior, Ops.Array[0, 300]).SampleAsync(
            async (history, load) =>
            {
                MemoryStorage ordinaryStorage = new();
                MemoryStorage bulkStorage = new();
                await using Dataset ordinary = await PriorAsync(ordinaryStorage, history);
                await using Dataset bulk = await PriorAsync(bulkStorage, history);

                CommitResult expected = await ordinary.CommitAsync(Request(load), T.Ct);

                await using (BulkLoad loading = await bulk.BeginBulkLoadAsync(Tiny, T.Ct))
                {
                    Feed(loading, load);
                    CommitResult actual = await loading.CommitAsync(new CommitMetadata(), T.Ct);
                    Assert.Equal(expected.Outcome, actual.Outcome);
                    Assert.Equal(expected.Position, actual.Position);
                }

                Assert.Equal(ordinary.CountersForTests(), bulk.CountersForTests());

                using (DatasetView want = ordinary.Pin())
                using (DatasetView have = bulk.Pin())
                {
                    Assert.Equal(Canonical(want), Canonical(have));
                }

                Assert.DoesNotContain(await bulkStorage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith("bulk/", StringComparison.Ordinal));

                // The commit after it, and a reopen, read what was loaded.
                await bulk.CommitAsync(Request([new Op(true, 0, 0, 0, 0)]), T.Ct);
                await ordinary.CommitAsync(Request([new Op(true, 0, 0, 0, 0)]), T.Ct);
                await using Dataset reopened = await Dataset.OpenAsync(bulkStorage, Options(), T.Ct);
                using DatasetView again = reopened.Pin();
                using DatasetView reference = ordinary.Pin();
                Assert.Equal(Canonical(reference), Canonical(again));
            },
            iter: 150);
    }

    [Fact]
    public async Task language_tags_in_either_case_are_one_term_and_find_the_datasets_own()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(), T.Ct);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Lang("hello", "EN")), T.Ct);
        (long canonical, _) = dataset.CountersForTests();

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct))
        {
            load.Assert(T.Iri("s"), T.Iri("p"), T.Lang("hello", "en"));
            load.Assert(T.Iri("s"), T.Iri("q"), T.Lang("bye", "DA"));
            load.Assert(T.Iri("s"), T.Iri("q"), T.Lang("bye", "da"));
            Assert.Equal(CommitOutcome.Committed, (await load.CommitAsync(new CommitMetadata(), T.Ct)).Outcome);
        }

        using DatasetView view = dataset.Pin();
        Assert.Equal(2, view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value);
        Assert.Equal(canonical + 2, dataset.CountersForTests().Canonical);
    }

    /// <summary>
    /// Validators see the bulk delta on disk (ADR 0077): one that reads the
    /// sources gets the delta's quads; one that rejects leaves no trace in the
    /// log, the dictionary, or <c>derived/</c>.
    /// </summary>
    [Fact]
    public async Task validators_read_the_delta_where_it_lies_and_a_rejection_leaves_no_trace()
    {
        CountingValidator counting = new();
        MemoryStorage storage = new();
        await using Dataset dataset = await PriorAsync(storage, [[new Op(true, 0, 0, 3, 0), new Op(true, 1, 1, 4, 1)]], [counting]);

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct))
        {
            Feed(load, [new Op(true, 2, 2, 5, 0), new Op(true, 3, 2, 12, 2), new Op(false, 0, 0, 3, 0), new Op(true, 0, 0, 3, 0), new Op(false, 1, 1, 4, 1)]);
            Assert.Equal(CommitOutcome.Committed, (await load.CommitAsync(new CommitMetadata(), T.Ct)).Outcome);
        }

        Assert.Equal((2, 1), (counting.Asserted, counting.Retracted));

        long head = dataset.Head.Value;
        long log = (await storage.Log.ListSegmentsAsync(T.Ct)).Sum(s => s.Length.Value);
        (long, long) counters = dataset.CountersForTests();
        counting.Reject = true;

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct))
        {
            Feed(load, [new Op(true, 4, 0, 14, 1)]);
            Assert.Equal(CommitOutcome.Rejected, (await load.CommitAsync(new CommitMetadata(), T.Ct)).Outcome);
        }

        Assert.Equal(head, dataset.Head.Value);
        Assert.Equal(log, (await storage.Log.ListSegmentsAsync(T.Ct)).Sum(s => s.Length.Value));
        Assert.Equal(counters, dataset.CountersForTests());
        Assert.DoesNotContain(await storage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith("bulk/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task an_abandoned_load_writes_nothing_and_releases_the_sequencer()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await PriorAsync(storage, [[new Op(true, 0, 0, 3, 0)]]);

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct))
        {
            Feed(load, Enumerable.Range(0, 50).Select(i => new Op(true, i % 5, i % 3, i % 16, i % 3)));
        }

        Assert.Equal(1, dataset.Head.Value);
        Assert.DoesNotContain(await storage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith("bulk/", StringComparison.Ordinal));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(Request([new Op(true, 4, 1, 1, 0)]), T.Ct)).Outcome);
    }

    /// <summary>
    /// The crash gate of the bulk loader: the file backend over the simulated
    /// file system crashes at every operation of a load — every spill, every
    /// run write, and every byte boundary of every record of the commit — and
    /// the dataset reopens at the head before the load with exactly its state,
    /// no partial commit visible, the load's spills deleted, and the log
    /// continuing; a load that completed reopens with all of it.
    /// </summary>
    [Fact]
    public async Task a_crash_at_every_operation_of_a_load_leaves_the_dataset_at_the_previous_head()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-simulated", "bulk"));
        Op[][] prior = [[new Op(true, 0, 0, 3, 0), new Op(true, 1, 1, 4, 1), new Op(true, 2, 2, 12, 2)], [new Op(false, 1, 1, 4, 1), new Op(true, 3, 0, 13, 0)]];
        Op[] load = [.. Enumerable.Range(0, 40).Select(i => new Op(i % 7 != 0, i % 9, i % 3, i % 16, i % 3))];

        // The reference: the state before, and after a load that completes.
        SimulatedFileSystem clean = new();
        (string before, string after, int startOperation, int operations) = await RunAsync(clean, root, prior, load);
        Assert.NotEqual(before, after);
        int points = 0, beforeClose = 0, afterClose = 0;
        int records = clean.Writes.Count(w => w.Operation > startOperation && w.Path.EndsWith(".seg", StringComparison.Ordinal));
        Assert.True(records > 10, "the load wrote " + records + " records");

        // Every operation, cut before it; and every write to a segment cut
        // inside it too, at its first byte, its middle and its last.
        List<(int Operation, int Keep)> crashes = [.. Enumerable.Range(startOperation + 1, operations - startOperation).Select(o => (o, 0))];

        foreach ((int operation, int length, string path) in clean.Writes)
        {
            if (operation > startOperation && path.EndsWith(".seg", StringComparison.Ordinal) && length > 2)
            {
                crashes.AddRange([(operation, 1), (operation, length / 2), (operation, length - 1)]);
            }
        }

        foreach ((int crashAt, int keep) in crashes)
        {
            int target = crashAt;
            SimulatedFileSystem files = new() { Injector = op => op.Number == target ? keep : null };

            try
            {
                await RunAsync(files, root, prior, load);
                Assert.Fail("operation " + target + " did not crash");
            }
            catch (SimulatedCrash)
            {
            }

            foreach (CrashKind kind in new[] { CrashKind.Process, CrashKind.PowerLoss })
            {
                SimulatedFileSystem image = files.Image(kind, new Random(target));
                await using FileStorage storage = FileStorage.Open(image, new DatasetDirectory(root), new FileStorageOptions { Clock = ManualClock.Epoch() });
                await using Dataset reopened = await Dataset.OpenAsync(storage, Options(), T.Ct);
                // Before the closing record is durable the load is invisible;
                // after it, the whole load is there. Nothing in between.
                long head = reopened.Head.Value;
                Assert.True(head == prior.Length || head == prior.Length + 1, "opened at " + head + " after a crash at operation " + target);
                using (DatasetView view = reopened.Pin())
                {
                    Assert.Equal(head == prior.Length ? before : after, Canonical(view));
                }

                (head == prior.Length ? ref beforeClose : ref afterClose)++;

                Assert.DoesNotContain(await storage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith("bulk/", StringComparison.Ordinal));
                Assert.Equal(CommitOutcome.Committed, (await reopened.CommitAsync(Request([new Op(true, 4, 2, 2, 0)]), T.Ct)).Outcome);
                points++;
            }
        }

        Assert.True(beforeClose > records, "the commit closed too early to test");
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{operations - startOperation} operations of the load, {crashes.Count} crash points, {records} record writes, {points} images reopened: {beforeClose} at the previous head, {afterClose} with the load"));

        // And the load that completed, reopened.
        SimulatedFileSystem done = clean.Image(CrashKind.Process, new Random(0));
        await using FileStorage finished = FileStorage.Open(done, new DatasetDirectory(root), new FileStorageOptions { Clock = ManualClock.Epoch() });
        await using Dataset loaded = await Dataset.OpenAsync(finished, Options(), T.Ct);
        Assert.Equal(prior.Length + 1, loaded.Head.Value);
        using DatasetView all = loaded.Pin();
        Assert.Equal(after, Canonical(all));
    }

    /// <summary>
    /// A crash after a load's commit is durable and before the state naming
    /// its delta run is: the run, written before the commit, is adopted on
    /// open — it starts where the loaded index ends and its end hash is the
    /// log's — so the load is not replayed from the log in memory (ADR 0081).
    /// Defect found by the 100-million-quad gate: a crash in that window
    /// replayed the whole load into a memtable.
    /// </summary>
    [Fact]
    public async Task a_load_whose_state_was_lost_reopens_from_its_delta_run()
    {
        MemoryStorage storage = new();
        Op[][] prior = [[new Op(true, 0, 0, 3, 0), new Op(true, 1, 1, 4, 1)], [new Op(true, 2, 2, 12, 2)]];
        Op[] ops = [.. Enumerable.Range(0, 60).Select(i => new Op(i % 5 != 0, i % 9, i % 3, i % 16, i % 3))];
        BlobName state = new("index/state");
        string after;
        ReadOnlyMemory<byte> stateBefore;

        await using (Dataset dataset = await PriorAsync(storage, prior))
        {
            await using BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct);
            stateBefore = await T.ReadBlobAsync(storage, state);
            Feed(load, ops);
            Assert.Equal(CommitOutcome.Committed, (await load.CommitAsync(new CommitMetadata(), T.Ct)).Outcome);
            using DatasetView view = dataset.Pin();
            after = Canonical(view);
        }

        // The state as the crash left it: as it was before the load.
        await using (IBlobWriter writer = await storage.Derived.CreateAsync(state, T.Ct))
        {
            await writer.WriteAsync(stateBefore, T.Ct);
            await writer.PublishAsync(T.Ct);
        }

        string run = "index/runs/" + prior.Length.ToString("D20", CultureInfo.InvariantCulture) + "-" + (prior.Length + 1).ToString("D20", CultureInfo.InvariantCulture) + ".";
        Assert.Contains(await storage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith(run, StringComparison.Ordinal));

        await using Dataset reopened = await Dataset.OpenAsync(storage, Options(), T.Ct);
        Assert.Equal(prior.Length + 1, reopened.Head.Value);

        using (DatasetView view = reopened.Pin())
        {
            Assert.Equal(after, Canonical(view));
        }

        // Kept, so adopted: a replay would have left it unnamed, and deleted.
        Assert.Contains(await storage.Derived.ListAsync(T.Ct), n => n.Value.StartsWith(run, StringComparison.Ordinal));
    }

    /// <summary>
    /// The open scan holds at most a bound of any one commit's body, whatever
    /// the commit's size: a commit past it is verified as it passes — every
    /// record's hash and the body's content hash — and let go, to be read
    /// again if replay needs it (ADR 0081). The scan with any bound finds the
    /// same commits, with the same headers, as the scan that holds them all;
    /// and a dataset opened over a torn tail opens the same either way.
    /// Defect found by the 100-million-quad gate: the scan held a torn bulk
    /// commit's every record, and recovery ran out of memory.
    /// </summary>
    [Fact]
    public void the_open_scan_holds_a_bounded_part_of_any_commit() =>
        Gen.Select(Gen.Select(Ops.Array[1, 40], Gen.Bool).Array[1, 6], Gen.Long[0, 400], Gen.Int[0, 100])
            .Sample(sample =>
            {
                ((Op[] Ops, bool Bulk)[] commits, long retain, int cut) = sample;
                MemoryStorage storage = new();

                Task.Run(async () =>
                {
                    await using Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options(), T.Ct);

                    foreach ((Op[] ops, bool bulk) in commits)
                    {
                        if (bulk)
                        {
                            await using BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct);
                            Feed(load, ops);
                            await load.CommitAsync(new CommitMetadata(), T.Ct);
                        }
                        else
                        {
                            await dataset.CommitAsync(Request(ops), T.Ct);
                        }
                    }
                }).GetAwaiter().GetResult();

                LogScan whole = LogReader.ScanAsync(storage.Log, T.Id, 0, long.MaxValue, T.Ct).AsTask().GetAwaiter().GetResult();
                LogScan bounded = LogReader.ScanAsync(storage.Log, T.Id, 0, retain, T.Ct).AsTask().GetAwaiter().GetResult();
                Assert.Equal(whole.Head, bounded.Head);

                for (int i = 0; i < whole.Commits.Count; i++)
                {
                    Assert.Equal(whole.Commits[i].HeaderHash, bounded.Commits[i].HeaderHash);
                    Assert.Equal(whole.Commits[i].Bytes, bounded.Commits[i].Bytes);
                    Assert.NotNull(whole.Commits[i].Full);

                    if (bounded.Commits[i].Full is { } full)
                    {
                        Assert.Equal(whole.Commits[i].Full!.Asserted, full.Asserted);
                        Assert.Equal(whole.Commits[i].Full!.Retracted, full.Retracted);
                    }
                    else
                    {
                        Assert.True(whole.Commits[i].Bytes > retain, "a commit of " + whole.Commits[i].Bytes + " bytes was let go under a bound of " + retain);
                    }
                }

                // Torn: the newest segment cut short, as a crash leaves it.
                (ReadOnlyMemory<byte> manifest, List<byte[]> segments) = T.CopyLogAsync(storage).GetAwaiter().GetResult();

                // A load that changes nothing commits nothing, and a log with no commit has no segment.
                if (segments.Count == 0)
                {
                    return;
                }

                byte[] last = segments[^1];
                segments[^1] = last[..(int)(LogFormat.SegmentHeaderLength + ((last.Length - LogFormat.SegmentHeaderLength) * (long)cut / 100))];
                MemoryStorage torn = MemoryStorage.FromLog(manifest, segments.Select(b => (ReadOnlyMemory<byte>)b));
                LogScan tornWhole = LogReader.ScanAsync(torn.Log, T.Id, 0, long.MaxValue, T.Ct).AsTask().GetAwaiter().GetResult();
                LogScan tornBounded = LogReader.ScanAsync(torn.Log, T.Id, 0, retain, T.Ct).AsTask().GetAwaiter().GetResult();
                Assert.Equal(tornWhole.Head, tornBounded.Head);
                Assert.Equal(tornWhole.DiscardedTail, tornBounded.DiscardedTail);
            }, iter: 100);

    // The prior commits, then the load; the canonical state before and after,
    // and the operation numbers the load spans.
    private static async Task<(string Before, string After, int Start, int End)> RunAsync(SimulatedFileSystem files, string root, Op[][] prior, Op[] ops)
    {
        FileStorage storage = FileStorage.Open(files, new DatasetDirectory(root), new FileStorageOptions { Clock = ManualClock.Epoch() });

        try
        {
            await using Dataset dataset = await PriorAsync(storage, prior);
            string before;

            using (DatasetView view = dataset.Pin())
            {
                before = Canonical(view);
            }

            int start = files.Operations;

            await using (BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct))
            {
                Feed(load, ops);
                await load.CommitAsync(new CommitMetadata(), T.Ct);
            }

            int end = files.Operations;
            using DatasetView after = dataset.Pin();
            return (before, Canonical(after), start, end);
        }
        finally
        {
            await storage.DisposeAsync();
        }
    }

    private sealed class CountingValidator : ICommitValidator
    {
        public long Asserted { get; private set; }

        public long Retracted { get; private set; }

        public bool Reject { get; set; }

        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) =>
            Reject ? ValidationVerdict.Reject([T.Iri("no")]) : ValidationVerdict.Accept();

        public ValidationVerdict Validate(IQuadSource proposed, BulkDelta delta)
        {
            if (Reject)
            {
                return ValidationVerdict.Reject([T.Iri("no")]);
            }

            Asserted = T.All(delta.Asserted).Count;
            Retracted = T.All(delta.Retracted).Count;

            foreach (Quad quad in T.All(delta.Asserted))
            {
                Assert.True(proposed.Contains(in quad));
                Assert.True(proposed.TryExternalise(quad.Object, out _));
            }

            return ValidationVerdict.Accept();
        }
    }
}
