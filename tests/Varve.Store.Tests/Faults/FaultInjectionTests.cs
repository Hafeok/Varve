// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests.Faults;

/// <summary>
/// The failure-injection gate of milestone 6a: the file backend, unchanged,
/// over a file system that crashes at every operation — part way through
/// every byte of the log's writes — loses power with writes torn and
/// reordered up to the flush barrier, loses directory entries, and is copied
/// while it is being written. After every fault the dataset opens at a closed
/// commit no earlier than the last one acknowledged, every pinned and as-of
/// read equals the history of a run that never crashed, the derived state is
/// rebuilt, and the log can be continued and reopened.
/// </summary>
public sealed class FaultInjectionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "varve-simulated", "dataset"));

    private static DatasetOptions Options => new()
    {
        Clock = ManualClock.Epoch(),
        MaxRecordBytes = new ByteCount(64),
        SegmentBytes = new ByteCount(1024),
        MemtableLimit = new QuadCount(4),
        Maintenance = MaintenanceMode.Off,
    };

    private static FileStorageOptions StorageOptions => new() { Clock = ManualClock.Epoch() };

    /// <summary>One run of the workload: its history by position, and its operations.</summary>
    private sealed class Reference
    {
        public List<SortedSet<string>> History { get; } = [[]];

        public SimulatedFileSystem Final { get; set; } = null!;

        public int Operations { get; set; }

        public List<(int Operation, int Length, string Path)> Writes { get; set; } = [];
    }

    private static string Render(int s, int p, int o) =>
        T.Render(T.Iri("s" + s)) + " " + T.Render(T.Iri("p" + p)) + " " + T.Render(T.Integer(o.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The workload: commits of a few asserts and retracts over a small
    /// vocabulary — so that most retract something real — with 64-byte records,
    /// 1 KiB segments, a memtable of four quads flushed and merged after every
    /// commit, and a checkpoint every seventh step. The last acknowledged
    /// position is reported as it happens, so a crash knows what must survive.
    /// </summary>
    private static async Task RunAsync(SimulatedFileSystem files, int seed, int steps, Action<long> acknowledged, Reference? reference)
    {
        Random random = new(seed);
        SortedSet<string> state = [];
        FileStorage storage = FileStorage.Open(files, new DatasetDirectory(Root), StorageOptions);
        Dataset dataset = await Dataset.CreateAsync(storage, T.Id, Options, T.Ct);

        for (int step = 0; step < steps; step++)
        {
            CommitRequest request = new();
            SortedSet<string> next = [.. state];

            for (int op = random.Next(1, 6); op > 0; op--)
            {
                (int s, int p, int o) = (random.Next(4), random.Next(3), random.Next(4));
                bool assert = random.Next(3) > 0;
                RdfTerm subject = T.Iri("s" + s), predicate = T.Iri("p" + p), @object = T.Integer(o.ToString(CultureInfo.InvariantCulture));

                if (assert)
                {
                    request.Assert(subject, predicate, @object);
                    next.Add(Render(s, p, o));
                }
                else
                {
                    request.Retract(subject, predicate, @object);
                    next.Remove(Render(s, p, o));
                }
            }

            CommitResult result = await dataset.CommitAsync(request, T.Ct);

            if (result.Outcome == CommitOutcome.Committed)
            {
                state = next;
                reference?.History.Add(state);
                acknowledged(result.Position.Value);
            }

            if (step % 7 == 6 && dataset.Head.Value > 0)
            {
                await dataset.CheckpointAsync(new Position(1 + random.Next((int)dataset.Head.Value)), T.Ct);
            }

            await dataset.MaintainAsync(T.Ct);
        }

        await dataset.DisposeAsync();
        await storage.DisposeAsync();
    }

    private static async Task<Reference> ReferenceAsync(int seed, int steps)
    {
        Reference reference = new();
        SimulatedFileSystem files = new();
        await RunAsync(files, seed, steps, _ => { }, reference);
        reference.Final = files;
        reference.Operations = files.Operations;
        reference.Writes = files.Writes;
        return reference;
    }

    /// <summary>Runs the workload until it crashes where the injector says; returns the last acknowledged position.</summary>
    private static Task<(SimulatedFileSystem Files, long Acknowledged)> CrashAsync(int seed, int steps, Func<SimulatedFileSystem.Operation, int?> injector) =>
        CrashAsync(seed, steps, _ => injector);

    private static async Task<(SimulatedFileSystem Files, long Acknowledged)> CrashAsync(int seed, int steps, Func<SimulatedFileSystem, Func<SimulatedFileSystem.Operation, int?>> injectorFor)
    {
        SimulatedFileSystem files = new();
        files.Injector = injectorFor(files);
        long acknowledged = 0;

        try
        {
            await RunAsync(files, seed, steps, p => acknowledged = p, null);
        }
        catch (SimulatedCrash)
        {
        }

        return (files, acknowledged);
    }

    /// <summary>
    /// Opens a crash image and checks it: a head no earlier than
    /// <paramref name="atLeast"/>, every read equal to the reference at its
    /// position, then a commit continues the log and a reopen finds it.
    /// </summary>
    private static async Task VerifyAsync(SimulatedFileSystem image, Reference reference, long atLeast, string where, Continuation? continued = null)
    {
        try
        {
            await VerifyCoreAsync(image, reference, atLeast, where, continued, "after");
        }
        catch (Exception error) when (error is not InvalidOperationException { Message: var m } || !m.StartsWith(where, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(where + ": " + error.GetType().Name + ": " + error.Message, error);
        }
    }

    /// <summary>A commit a first recovery made after position <see cref="At"/>, which a second crash may have kept.</summary>
    private sealed record Continuation(long At, string Marker);

    private static string Continued(string marker) =>
        T.Render(T.Iri(marker)) + " " + T.Render(T.Iri("crash")) + " " + T.Render(T.Integer("1"));

    private static SortedSet<string> Expected(Reference reference, long position, Continuation? continued) =>
        continued is not null && position == continued.At + 1
            ? [.. reference.History[(int)continued.At], Continued(continued.Marker)]
            : reference.History[(int)position];

    private static async Task<long> VerifyCoreAsync(SimulatedFileSystem image, Reference reference, long atLeast, string where, Continuation? continued, string marker)
    {
        FileStorage storage = FileStorage.Open(image, new DatasetDirectory(Root), StorageOptions);
        long head;

        try
        {
            Dataset dataset;

            if ((await storage.Log.ReadManifestAsync(T.Ct)).IsEmpty && (await storage.Log.ListSegmentsAsync(T.Ct)).Count == 0)
            {
                Check(atLeast == 0, where + ": the dataset is gone and position " + atLeast + " was acknowledged");
                dataset = await Dataset.CreateAsync(storage, T.Id, Options, T.Ct);
            }
            else
            {
                dataset = await Dataset.OpenAsync(storage, Options, T.Ct);
            }

            await using (dataset)
            {
                head = dataset.Head.Value;
                Check(head >= atLeast, where + ": opened at " + head + " where " + atLeast + " was acknowledged");
                Check(head < reference.History.Count || (continued is not null && head == continued.At + 1), where + ": opened at " + head + ", beyond any commit");

                if (continued is not null && head > continued.At + 1)
                {
                    throw new InvalidOperationException(where + ": opened at " + head + ", past the continuation at " + (continued.At + 1));
                }

                using (DatasetView pinned = dataset.Pin())
                {
                    Same(Expected(reference, head, continued), T.Terms(pinned), where + ": pinned at " + head);
                }

                for (long p = 0; p <= head; p++)
                {
                    using DatasetView asOf = await dataset.AsOfAsync(new Position(p), T.Ct);
                    Same(Expected(reference, p, continued), T.Terms(asOf), where + ": as-of " + p);
                }

                CommitResult next = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri(marker), T.Iri("crash"), T.Integer("1")), T.Ct);
                Check(next.Outcome == CommitOutcome.Committed && next.Position.Value == head + 1, where + ": the recovered log does not continue");
                await dataset.MaintainAsync(T.Ct);
            }
        }
        finally
        {
            await storage.DisposeAsync();
        }

        await using FileStorage again = FileStorage.Open(image, new DatasetDirectory(Root), StorageOptions);
        await using Dataset reopened = await Dataset.OpenAsync(again, Options, T.Ct);
        Check(reopened.Head.Value == head + 1, where + ": a reopen after recovery lost the continuation");
        using DatasetView view = reopened.Pin();
        SortedSet<string> expected = [.. Expected(reference, head, continued), Continued(marker)];
        Same(expected, T.Terms(view), where + ": reopened after continuing");
        return head;
    }

    private static void Same(SortedSet<string> expected, SortedSet<string> actual, string where)
    {
        if (!expected.SetEquals(actual))
        {
            throw new InvalidOperationException(where + ": the store and the reference disagree.\n  only in the reference: "
                + string.Join(" | ", expected.Except(actual)) + "\n  only in the store: " + string.Join(" | ", actual.Except(expected)));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Report(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    /// <summary>
    /// A process crash at every operation of the workload, and inside every
    /// write at every byte offset of the log's records and segment headers and
    /// trailers, and at the start, middle and end of every write to derived/.
    /// What a crash keeps is everything written before it: every acknowledged
    /// commit survives.
    /// </summary>
    [Theory]
    [InlineData(6101, 24)]
    [InlineData(6102, 24)]
    public async Task a_process_crash_anywhere_recovers_to_the_last_closed_commit(int seed, int steps)
    {
        Reference reference = await ReferenceAsync(seed, steps);
        Dictionary<int, (int Length, string Path)> writes = reference.Writes.ToDictionary(w => w.Operation, w => (w.Length, w.Path));
        int points = 0;

        for (int operation = 1; operation <= reference.Operations; operation++)
        {
            List<int> keeps = [];

            if (writes.TryGetValue(operation, out (int Length, string Path) write))
            {
                if (write.Path.Contains(Path.DirectorySeparatorChar + "log" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    keeps.AddRange(Enumerable.Range(0, write.Length));
                }
                else
                {
                    keeps.AddRange(new[] { 0, write.Length / 2, Math.Max(0, write.Length - 1) }.Distinct());
                }
            }
            else
            {
                keeps.Add(0);
            }

            foreach (int keep in keeps)
            {
                int at = operation;
                (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? keep : null);
                await VerifyAsync(crashed.Image(CrashKind.Process, new Random(at)), reference, acknowledged, "process crash at operation " + at + ", byte " + keep);
                points++;
            }
        }

        Report("process crash, seed " + seed + ": " + reference.Operations + " operations, " + points + " injection points, head " + (reference.History.Count - 1));
    }

    /// <summary>
    /// Power lost at every operation: each write since its file's last flush
    /// kept, dropped or torn at a sector, in every combination the seed draws,
    /// and metadata cut anywhere after the last flush. Every acknowledged
    /// commit survives, because a commit is acknowledged only after its flush
    /// (ADR 0013, ADR 0073).
    /// </summary>
    [Theory]
    [InlineData(6201, 24, 6)]
    [InlineData(6202, 24, 6)]
    public async Task a_power_loss_with_writes_reordered_up_to_the_flush_recovers(int seed, int steps, int imagesPerPoint)
    {
        Reference reference = await ReferenceAsync(seed, steps);
        int points = 0;

        for (int operation = 1; operation <= reference.Operations; operation++)
        {
            int at = operation;
            (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);

            for (int image = 0; image < imagesPerPoint; image++)
            {
                await VerifyAsync(crashed.Image(CrashKind.PowerLoss, new Random((at * 31) + image)), reference, acknowledged, "power loss at operation " + at + ", image " + image);
                points++;
            }
        }

        Report("power loss, seed " + seed + ": " + reference.Operations + " operations, " + points + " injection points");
    }

    /// <summary>
    /// Power lost on a file system that may drop a new file's directory entry
    /// even after the file was flushed (ADR 0073's undocumented rows):
    /// acknowledged commits in a lost segment are lost, which ADR 0073 states,
    /// but the dataset always opens at a closed commit, consistently.
    /// </summary>
    [Fact]
    public async Task a_lost_directory_entry_is_a_segment_never_created()
    {
        const int seed = 6301, steps = 24;
        Reference reference = await ReferenceAsync(seed, steps);
        int points = 0;

        for (int operation = 1; operation <= reference.Operations; operation += 2)
        {
            int at = operation;
            (SimulatedFileSystem crashed, _) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);

            for (int image = 0; image < 4; image++)
            {
                await VerifyAsync(crashed.Image(CrashKind.LostDirectoryEntries, new Random((at * 17) + image)), reference, 0, "lost entries at operation " + at + ", image " + image);
                points++;
            }
        }

        Report("lost directory entries: " + points + " injection points");
    }

    /// <summary>
    /// After a crash, derived/ deleted, or replaced by the derived/ of the run
    /// that did not crash — a longer log's, so its state and checkpoints name
    /// positions this log does not have. Both open, rebuild, and read right.
    /// </summary>
    [Fact]
    public async Task derived_data_deleted_or_stale_after_a_crash_is_rebuilt()
    {
        const int seed = 6401, steps = 24;
        Reference reference = await ReferenceAsync(seed, steps);
        string derived = Path.Combine(Root, "derived");
        int points = 0;

        for (int operation = 1; operation <= reference.Operations; operation += 3)
        {
            int at = operation;
            (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);

            SimulatedFileSystem deleted = crashed.Image(CrashKind.Process, new Random(at));
            deleted.RemoveTree(derived);
            await VerifyAsync(deleted, reference, acknowledged, "derived/ deleted after operation " + at);

            SimulatedFileSystem stale = crashed.Image(CrashKind.Process, new Random(at));
            stale.RemoveTree(derived);

            foreach (string file in reference.Final.Files(derived))
            {
                stale.Place(file, reference.Final.Read(file), readOnly: false);
            }

            await VerifyAsync(stale, reference, acknowledged, "derived/ of a longer log after operation " + at);
            points += 2;
        }

        Report("derived/ deleted or stale: " + points + " injection points");
    }

    /// <summary>
    /// The dataset directory copied while it is being written, as `git add` or
    /// a backup would: the copier lists the directory once, then copies one
    /// file at a time, each as it reads at that moment. The copy opens at a
    /// closed commit with no corruption; the copy without derived/ too.
    /// </summary>
    [Fact]
    public async Task a_directory_copied_mid_write_opens_on_the_copy()
    {
        const int seed = 6501, steps = 30;
        Reference reference = await ReferenceAsync(seed, steps);
        int points = 0;

        for (int trial = 0; trial < 300; trial++)
        {
            Random random = new(trial);
            int listAt = random.Next(1, reference.Operations);
            List<int> copyAt = [];
            List<string>? listed = null;
            Dictionary<string, byte[]> copied = new(StringComparer.Ordinal);
            // The copier lists at one operation and copies file i at a later
            // one, in listing order, while the writer carries on.
            int copied_ = 0;

            (SimulatedFileSystem live, _) = await CrashAsync(seed, steps, files => op =>
            {
                if (op.Number == listAt)
                {
                    listed = [.. files.Files(Root).Where(f => !f.EndsWith(".tmp", StringComparison.Ordinal))];
                    int next = listAt;

                    foreach (string _ in listed)
                    {
                        next += random.Next(0, 8);
                        copyAt.Add(next);
                    }
                }

                while (listed is not null && copied_ < listed.Count && copyAt[copied_] <= op.Number)
                {
                    if (files.FileExists(listed[copied_]))
                    {
                        copied[listed[copied_]] = files.Read(listed[copied_]);
                    }

                    copied_++;
                }

                return null;
            });

            // Files the writer had finished with before the copier reached them.
            while (listed is not null && copied_ < listed.Count)
            {
                if (live.FileExists(listed[copied_]))
                {
                    copied[listed[copied_]] = live.Read(listed[copied_]);
                }

                copied_++;
            }

            foreach (bool withDerived in new[] { true, false })
            {
                SimulatedFileSystem copy = new();

                foreach ((string path, byte[] bytes) in copied)
                {
                    bool isDerived = path.Contains(Path.DirectorySeparatorChar + "derived" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

                    if (!withDerived && isDerived)
                    {
                        continue;
                    }

                    if (!path.EndsWith("LOCK", StringComparison.Ordinal))
                    {
                        copy.Place(path, bytes, readOnly: false);
                    }
                }

                await VerifyAsync(copy, reference, 0, "copy listed at operation " + listAt + (withDerived ? string.Empty : " without derived/"));
                points++;
            }
        }

        Report("copies mid-write: " + points + " copies opened");
    }

    /// <summary>
    /// Defect 1 of milestone 6a, kept as a named case: power lost at operation
    /// 21 of seed 6201, the first seal, where the closed trailer reached the
    /// disk and a record of the commit in flight before it did not. The next
    /// open refused the log as damaged. The writer now flushes the segment
    /// before the trailer that vouches for it.
    /// </summary>
    [Fact]
    public async Task regression_a_closed_trailer_never_outlives_the_records_before_it()
    {
        Reference reference = await ReferenceAsync(6201, 24);
        (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(6201, 24, op => op.Number == 21 ? 0 : null);

        for (int image = 0; image < 64; image++)
        {
            await VerifyAsync(crashed.Image(CrashKind.PowerLoss, new Random((21 * 31) + image)), reference, acknowledged, "power loss at operation 21, image " + image);
        }
    }

    /// <summary>
    /// A crash during recovery itself — while it seals an abandoned tail,
    /// deletes stale derived files, or continues the log — at every operation
    /// recovery performs, after a process crash at every fifth operation of
    /// the workload. The second restart recovers as the first would have.
    /// </summary>
    [Fact]
    public async Task a_crash_during_recovery_recovers()
    {
        const int seed = 6601, steps = 24;
        Reference reference = await ReferenceAsync(seed, steps);
        int points = 0;

        for (int operation = 1; operation <= reference.Operations; operation += 5)
        {
            int at = operation;
            (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);

            // How many operations a whole recovery takes from this image, and
            // where it continues the log.
            SimulatedFileSystem counting = crashed.Image(CrashKind.Process, new Random(at));
            long first = await VerifyCoreAsync(counting, reference, acknowledged, "recovery after operation " + at, null, "after");
            int recoveryOperations = counting.Operations;
            Continuation continued = new(first, "after");

            for (int second = 1; second <= recoveryOperations; second++)
            {
                int during = second;
                SimulatedFileSystem image = crashed.Image(CrashKind.Process, new Random(at));
                image.Injector = op => op.Number == during ? 0 : null;

                try
                {
                    await VerifyCoreAsync(image, reference, acknowledged, "recovery after operation " + at, null, "after");
                }
                catch (SimulatedCrash)
                {
                }
                catch (InvalidOperationException error) when (error.InnerException is SimulatedCrash || error.Message.Contains("Simulated", StringComparison.Ordinal))
                {
                }

                SimulatedFileSystem restarted = image.Image(CrashKind.Process, new Random(during));

                try
                {
                    await VerifyCoreAsync(restarted, reference, acknowledged, "a crash at operation " + during + " of the recovery after operation " + at, continued, "again");
                }
                catch (Exception error) when (error is not InvalidOperationException)
                {
                    throw new InvalidOperationException("a crash at operation " + during + " of the recovery after operation " + at + ": " + error.GetType().Name + ": " + error.Message, error);
                }
                points++;
            }
        }

        Report("crash during recovery: " + points + " injection points");
    }

    /// <summary>
    /// The same suites over many seeds and longer workloads, for hunting
    /// defects rather than for the gate: set VARVE_FAULT_SEEDS to a count to
    /// run it. Not in CI.
    /// </summary>
    [Fact]
    public async Task deep_exploration_over_many_seeds()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("VARVE_FAULT_SEEDS"), CultureInfo.InvariantCulture, out int seeds))
        {
            Assert.Skip("Set VARVE_FAULT_SEEDS to run the deep exploration.");
        }

        long points = 0;

        for (int seed = 7000; seed < 7000 + seeds; seed++)
        {
            int steps = 20 + (seed % 25);
            Reference reference = await ReferenceAsync(seed, steps);
            Random choose = new(seed);

            for (int operation = 1; operation <= reference.Operations; operation++)
            {
                int at = operation;
                (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? choose.Next(0, Math.Max(1, op.Length)) : null);
                await VerifyAsync(crashed.Image(CrashKind.Process, new Random(at)), reference, acknowledged, "seed " + seed + ", process crash at operation " + at);

                for (int image = 0; image < 3; image++)
                {
                    await VerifyAsync(crashed.Image(CrashKind.PowerLoss, new Random((at * 31) + image)), reference, acknowledged, "seed " + seed + ", power loss at operation " + at + ", image " + image);
                }

                await VerifyAsync(crashed.Image(CrashKind.LostDirectoryEntries, new Random(at)), reference, 0, "seed " + seed + ", lost entries at operation " + at);
                points += 5;
            }
        }

        Report("deep exploration: " + seeds + " seeds, " + points + " injection points");
    }
}
