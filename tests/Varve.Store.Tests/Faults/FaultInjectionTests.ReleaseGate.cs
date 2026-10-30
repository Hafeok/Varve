// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests.Faults;

/// <summary>
/// The file backend's four faults at every point (ADR 0214), on the workload
/// and the checks the land gate's suites share: a torn write at every byte of
/// every write, every sector cut of every unflushed write, and a full disk at
/// every allocation. The release gate and the nightly run all of each; a pull
/// request runs a share a seed chooses (docs/testing.md section 8).
/// </summary>
public static partial class FaultInjectionTests
{
    /// <summary>A process crash inside every write the backend issues, at every byte.</summary>
    public sealed class TornWrites
    {
        /// <summary>
        /// Every write, to the log, to derived/ and to the small files, cut at
        /// every byte under the process model (ADR 0214 point 1). The land
        /// gate's <see cref="ProcessCrash"/> cuts the log's writes at every byte
        /// and the others at three places; this cuts them all everywhere.
        /// <c>VARVE_FAULT_TORN_STRIDE=1</c> runs every cut.
        /// </summary>
        [Theory]
        [InlineData(6401, 16)]
        public async Task a_write_torn_at_any_byte_recovers_to_the_last_closed_commit(int seed, int steps)
        {
            Sweep sweep = Depth.SweepOf("FAULT_TORN", 64);
            Report(sweep.Replay);
            Reference reference = await ReferenceAsync(seed, steps);
            List<(int Operation, int Keep, string Path)> cuts = [];

            foreach ((int operation, int length, string path) in reference.Writes)
            {
                for (int keep = 0; keep < Math.Max(1, length); keep++)
                {
                    cuts.Add((operation, keep, path));
                }
            }

            int points = 0;
            (int At, SimulatedFileSystem Files, long Acknowledged)? crashed = null;

            for (int i = sweep.First - 1; i < cuts.Count; i += sweep.Stride)
            {
                (int at, int keep, string path) = cuts[i];

                // One run crashed at the write's start serves every cut of it.
                if (crashed?.At != at)
                {
                    (SimulatedFileSystem files, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);
                    crashed = (at, files, acknowledged);
                }

                await VerifyAsync(crashed.Value.Files.Image(CrashKind.Process, new Random(at), keep), reference, crashed.Value.Acknowledged, sweep.Replay + ": write " + at + " to " + path + " torn after byte " + keep);
                points++;
            }

            Report(sweep.Replay + ": torn writes, seed " + seed + ": " + reference.Writes.Count + " writes, " + points + " of " + cuts.Count + " cuts");
        }
    }

    /// <summary>A power loss with every unflushed write cut at every sector, in turn.</summary>
    public sealed class SectorCuts
    {
        /// <summary>
        /// At every operation, power is lost with each write since its file's
        /// last flush cut at every 512-byte sector boundary in turn, the other
        /// unflushed writes once kept whole and once dropped, and the metadata
        /// once kept to the last flush and once whole (ADR 0214 point 1). The
        /// land gate's <see cref="PowerLoss"/> samples these images; this
        /// enumerates them. <c>VARVE_FAULT_SECTOR_STRIDE=1</c> runs every
        /// operation.
        /// </summary>
        [Theory]
        [InlineData(6501, 16)]
        public async Task every_sector_cut_of_every_unflushed_write_recovers(int seed, int steps)
        {
            Sweep sweep = Depth.SweepOf("FAULT_SECTOR", 16);
            Report(sweep.Replay);
            Reference reference = await ReferenceAsync(seed, steps);
            int points = 0;

            for (int operation = sweep.First; operation <= reference.Operations; operation += sweep.Stride)
            {
                int at = operation;
                (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == at ? 0 : null);

                foreach ((string path, int write, int length) in crashed.Unflushed())
                {
                    for (int cut = 0; cut <= length; cut += 512)
                    {
                        foreach (bool othersWhole in new[] { true, false })
                        {
                            foreach (bool metadataToEnd in new[] { false, true })
                            {
                                (string p, int w, int c) = (path, write, Math.Min(cut, length));
                                SimulatedFileSystem image = crashed.Image(
                                    (file, index, size) => file == p && index == w ? c : othersWhole ? size : -1,
                                    metadataToEnd);
                                await VerifyAsync(image, reference, acknowledged, sweep.Replay + ": power lost at operation " + at + ", write " + w + " to " + p + " cut at byte " + c
                                    + (othersWhole ? ", the others kept" : ", the others dropped") + (metadataToEnd ? ", metadata whole" : ", metadata to the flush"));
                                points++;
                            }
                        }
                    }
                }
            }

            Report(sweep.Replay + ": sector cuts, seed " + seed + ": " + reference.Operations + " operations, " + points + " images");
        }
    }

    /// <summary>A full disk at every allocation the backend makes, and at every allocation of an open.</summary>
    public sealed class DiskFull
    {
        /// <summary>
        /// The disk fills at each allocation of the workload in turn: a create,
        /// a write past a file's end, a rename to a new name (ADR 0214 point 2).
        /// The operation that met it is refused, and so is every allocation
        /// after it until the workload sees the failure; then the space
        /// returns, or, in the second case, only after the workload ends.
        /// <list type="bullet">
        /// <item>No commit is acknowledged that was not made durable.</item>
        /// <item>While the dataset is open, it reads what it acknowledged.</item>
        /// <item>A failed derived write does not stop later commits once the space is back.</item>
        /// <item>Reopened with space, it recovers as after a crash: every acknowledged position, and the log continues.</item>
        /// </list>
        /// <c>VARVE_FAULT_DISKFULL_STRIDE=1</c> runs every allocation.
        /// </summary>
        [Theory]
        [InlineData(6601, 16, false)]
        [InlineData(6602, 16, true)]
        public async Task a_full_disk_at_any_allocation_fails_what_it_must_and_reopens_exactly(int seed, int steps, bool staysFull)
        {
            Sweep sweep = Depth.SweepOf("FAULT_DISKFULL", 4);
            Report(sweep.Replay);
            int allocations = await AllocationsAsync(seed, steps);
            int points = 0, commitsFailed = 0, derivedFailed = 0;

            for (int at = sweep.First; at <= allocations; at += sweep.Stride)
            {
                string where = sweep.Replay + ": the disk full at allocation " + at + (staysFull ? ", until the end" : ", until the failure is seen");
                FullDiskRun run = await RunFullAsync(seed, steps, at, staysFull, where);
                commitsFailed += run.CommitFailed ? 1 : 0;
                derivedFailed += run.DerivedFailed ? 1 : 0;

                run.Files.Refuser = null;
                await VerifyAsync(run.Files.Image(CrashKind.Process, new Random(at)), run.History, run.Acknowledged, where);
                points++;
            }

            Report(sweep.Replay + ": full disk, seed " + seed + ": " + allocations + " allocations, " + points + " points, " + commitsFailed + " with a commit refused, " + derivedFailed + " with a derived write refused");
        }

        /// <summary>
        /// The disk fills at each allocation an open makes as it recovers a
        /// crashed workload (ADR 0214 point 2): the open either succeeds or
        /// fails with the disk's <see cref="IOException"/>, and the next open,
        /// with space, recovers every acknowledged commit.
        /// </summary>
        [Fact]
        public async Task a_full_disk_during_recovery_fails_the_open_and_the_next_open_recovers()
        {
            const int seed = 6603, steps = 16;
            Reference reference = await ReferenceAsync(seed, steps);
            int crashAt = reference.Operations * 2 / 3;
            (SimulatedFileSystem crashed, long acknowledged) = await CrashAsync(seed, steps, op => op.Number == crashAt ? 0 : null);

            // How many allocations a clean recovery of this image makes.
            SimulatedFileSystem counted = crashed.Image(CrashKind.Process, new Random(crashAt));
            counted.Refuser = _ => false;
            await OpenAndCloseAsync(counted);
            int allocations = counted.Allocations;
            Assert.True(allocations > 0, "the recovery allocates nothing");
            int failedOpens = 0;

            for (int at = 1; at <= allocations; at++)
            {
                int refused = at;
                SimulatedFileSystem image = crashed.Image(CrashKind.Process, new Random(crashAt));
                image.Refuser = n => n >= refused;

                try
                {
                    await OpenAndCloseAsync(image);
                }
                catch (IOException)
                {
                    failedOpens++;
                }

                image.Refuser = null;
                await VerifyAsync(image.Image(CrashKind.Process, new Random(at)), reference, acknowledged, "the disk full at allocation " + at + " of the recovery");
            }

            Report("full disk in recovery: " + allocations + " allocations, " + failedOpens + " opens failed and recovered on the next");
        }

        private static async Task OpenAndCloseAsync(SimulatedFileSystem files)
        {
            await using FileStorage storage = FileStorage.Open(files, new DatasetDirectory(Root), StorageOptions);
            await using Dataset dataset = await Dataset.OpenAsync(storage, Options, T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        private static async Task<int> AllocationsAsync(int seed, int steps)
        {
            SimulatedFileSystem files = new() { Refuser = _ => false };
            await RunAsync(files, seed, steps, _ => { }, null);
            return files.Allocations;
        }

        // The run's own history: after a refusal, its commits are not the
        // reference's, since the workload goes on from what was committed.
        private sealed record FullDiskRun(SimulatedFileSystem Files, Reference History, long Acknowledged, bool CommitFailed, bool DerivedFailed);

        // The workload of RunAsync, unbatched, going on through every failure
        // the full disk causes, and checking what an open dataset must do.
        private static async Task<FullDiskRun> RunFullAsync(int seed, int steps, int fullAt, bool staysFull, string where)
        {
            SimulatedFileSystem files = new();
            bool full = true, seen = false, commitFailed = false, derivedFailed = false;
            files.Refuser = n => full && n >= fullAt;
            Random random = new(seed);
            SortedSet<string> state = [];
            long acknowledged = 0;
            Reference history = new();

            void Saw()
            {
                seen = true;
                full = full && staysFull;
            }

            FileStorage? storage = null;
            Dataset? dataset = null;

            try
            {
                try
                {
                    storage = FileStorage.Open(files, new DatasetDirectory(Root), StorageOptions);
                    dataset = await Dataset.CreateAsync(storage, T.Id, Options, T.Ct);
                }
                catch (IOException)
                {
                    // The disk filled while the dataset was created: nothing
                    // was acknowledged, and recovery starts from nothing.
                    Saw();
                    return new FullDiskRun(files, history, 0, false, false);
                }

                for (int step = 0; step < steps; step++)
                {
                    (CommitRequestBuilder request, SortedSet<string> next) = Generate(random, state);
                    CommitResult result;

                    bool hadSpace = !full;
                    result = await dataset.CommitAsync(request, T.Ct);

                    if (result is { Outcome: CommitOutcome.Committed } committed)
                    {
                        state = next;
                        acknowledged = committed.Position.Value;
                        history.History.Add(state);
                        Check(history.History.Count - 1 == acknowledged, where + ": committed " + acknowledged + " after " + (history.History.Count - 2));
                    }
                    else if (result is { Outcome: CommitOutcome.Unavailable } refused)
                    {
                        // A full disk is answered as such, and only while the
                        // disk is full: once the space is back, the next write
                        // recovers the writer by itself and commits (ADR 0214).
                        Check(refused.StorageFull, where + ": a commit was refused, step " + step + ", other than for space: " + refused.Reason);
                        Check(!hadSpace, where + ": a commit was refused for space, step " + step + ", after the space came back: " + refused.Reason);
                        commitFailed = true;
                        Check(dataset.StorageFull, where + ": refused for space, and the dataset does not say so");
                        Saw();
                    }

                    // A refused write gives way to the next one once there is space.
                    Check(full || !dataset.StorageFull || result is { StorageFull: true }, where + ": the dataset still says it is full, step " + step);

                    // An open dataset reads what it acknowledged, whatever failed.
                    using (DatasetView pinned = dataset.Pin())
                    {
                        Same(history.History[(int)dataset.Head.Value], T.Terms(pinned), where + ": pinned while open, step " + step);
                        Check(dataset.Head.Value == acknowledged, where + ": open at " + dataset.Head.Value + " where " + acknowledged + " was acknowledged");
                    }

                    if (step % 7 == 6 && dataset.Head.Value > 0)
                    {
                        Position at = new(1 + random.Next((int)dataset.Head.Value));

                        try
                        {
                            await dataset.CheckpointAsync(at, T.Ct);
                        }
                        catch (IOException)
                        {
                            derivedFailed = true;
                            Saw();
                        }
                    }

                    try
                    {
                        await dataset.MaintainAsync(T.Ct);
                    }
                    catch (IOException)
                    {
                        derivedFailed = true;
                        Saw();
                    }

                }

                Check(seen || files.Allocations < fullAt, where + ": the disk filled and nothing saw it");
                return new FullDiskRun(files, history, acknowledged, commitFailed, derivedFailed);
            }
            finally
            {
                full = false;

                try
                {
                    if (dataset is not null)
                    {
                        await dataset.DisposeAsync();
                    }

                    if (storage is not null)
                    {
                        await storage.DisposeAsync();
                    }
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
