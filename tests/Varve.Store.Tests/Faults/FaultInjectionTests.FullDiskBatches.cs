// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests.Faults;

public static partial class FaultInjectionTests
{
    /// <summary>A failed append at every write and every flush, inside generated batches.</summary>
    public sealed class FullDiskBatches
    {
        /// <summary>
        /// Committed means closed and flushed (ADR 0214 point 2). The storage
        /// fails at each write, and in a second sweep at each flush, that the
        /// workload makes after the dataset is created. A refused write keeps a
        /// prefix of its bytes, as a short write does. Three ways it fails and
        /// goes on:
        /// <list type="bullet">
        /// <item><c>recover</c>: a full disk, refusing every write or flush until
        /// the failure is seen and <paramref name="fullBatches"/> batches more;
        /// then the space returns, and the next write recovers, with no reopen;</item>
        /// <item><c>reopen</c>: a full disk, and the dataset reopened at once,
        /// before any write recovers it;</item>
        /// <item><c>io</c>: an I/O error, not a full disk, once; the dataset
        /// waits for a reopen (ADR 0124 point 3), and is reopened.</item>
        /// </list>
        /// Each member of a batch is answered committed, refused or outcome
        /// unknown, and:
        /// <list type="bullet">
        /// <item>the committed members are a prefix of the batch, in order, at the positions after the head;</item>
        /// <item>after a reopen, every member answered committed is in the log at its position, by its cause;</item>
        /// <item>no member refused, <c>storage-full</c> or with the error, is in the log;</item>
        /// <item>every outcome-unknown member is settled by the commits once the head passes its position, either way, and the reopened log agrees with what the open dataset said;</item>
        /// <item>every term committed before the failure and after it reads back as itself: the dataset as of every position holds the quads committed there, and the commit its cause.</item>
        /// </list>
        /// The images reopened at the end keep every byte written (a process
        /// crash), and only what was flushed and what the power-loss model keeps
        /// (ADR 0073). <c>VARVE_FAULT_DISKFULL_STRIDE=1</c> runs every write and
        /// every flush.
        /// </summary>
        [Theory]
        [InlineData(6611, 24, 4, false, "recover", 0)]
        [InlineData(6612, 24, 4, true, "recover", 0)]
        [InlineData(6613, 24, 3, false, "recover", 1)]
        [InlineData(6614, 24, 3, true, "recover", 1)]
        [InlineData(6615, 24, 4, false, "reopen", 0)]
        [InlineData(6616, 24, 4, true, "reopen", 0)]
        [InlineData(6617, 24, 4, false, "io", 0)]
        [InlineData(6618, 24, 4, true, "io", 0)]
        public async Task each_member_of_a_failed_batch_is_answered_by_the_log(int seed, int steps, int batch, bool flushes, string mode, int fullBatches)
        {
            SimulatedFileSystem.OperationKind kind = flushes ? SimulatedFileSystem.OperationKind.Flush : SimulatedFileSystem.OperationKind.Write;
            Sweep sweep = Depth.SweepOf("FAULT_DISKFULL", 4);
            Report(sweep.Replay);
            int operations = (await RunBatchesAsync(seed, steps, batch, kind, mode, int.MaxValue, fullBatches, "the count")).Seen;
            Assert.True(operations > 0, "the workload makes no " + kind);
            int points = 0, unknown = 0, refused = 0, goneOn = 0;

            for (int at = sweep.First; at <= operations; at += sweep.Stride)
            {
                string where = sweep.Replay + ": " + mode + " at " + kind + " " + at + " of " + operations + ", seed " + seed;
                BatchRun run = await RunBatchesAsync(seed, steps, batch, kind, mode, at, fullBatches, where);
                where += " (" + run.FirstRefused + ")";
                unknown += run.Unknown.Count;
                refused += run.Refused.Count;
                goneOn += run.CommittedAfterFailure ? 1 : 0;

                run.Files.Refusal = null;
                await SettleAsync(run.Files.Image(CrashKind.Process, new Random(at)), run, everything: true, where + ", every byte kept");
                await SettleAsync(run.Files.Image(CrashKind.PowerLoss, new Random(at)), run, everything: false, where + ", a power loss");
                points++;
            }

            Report(sweep.Replay + ": " + mode + " at every " + kind + ", seed " + seed + ": " + operations + " operations, " + points + " points, "
                + refused + " members refused, " + unknown + " outcome unknown, " + goneOn + " runs committing again after the failure");
        }

        private sealed class BatchRun
        {
            public SimulatedFileSystem Files { get; init; } = null!;

            /// <summary>Every member answered committed: its cause and position.</summary>
            public Dictionary<string, long> Committed { get; } = [];

            /// <summary>Every member refused, for space or with the error, by cause.</summary>
            public HashSet<string> Refused { get; } = [];

            /// <summary>Every member answered outcome unknown: its cause and the position it names.</summary>
            public Dictionary<string, long> Unknown { get; } = [];

            /// <summary>The cause at each position, as the open dataset had it at the end.</summary>
            public Dictionary<long, string> Causes { get; set; } = [];

            /// <summary>The quads at each committed position, as the run committed them.</summary>
            public Dictionary<long, SortedSet<string>> History { get; } = [];

            public bool CommittedAfterFailure { get; set; }

            public int Seen { get; set; }

            public int Batch { get; init; }

            /// <summary>The operation the failure began at.</summary>
            public string FirstRefused { get; set; } = "";
        }

        // The batched workload of RunAsync, each member with its own cause, the
        // storage failing from the at-th write or flush after the dataset is created.
        private static async Task<BatchRun> RunBatchesAsync(int seed, int steps, int batch, SimulatedFileSystem.OperationKind kind, string mode, int at, int fullBatches, string where)
        {
            SimulatedFileSystem files = new();
            BatchRun run = new() { Files = files, Batch = batch };
            bool counting = false, failing = false, failed = false, full = mode != "io";
            int stillFailing = 0;
            Random random = new(seed);
            Random cuts = new(seed ^ at);

            files.Refusal = operation =>
            {
                if (!counting || operation.Kind != kind)
                {
                    return null;
                }

                if (++run.Seen == at)
                {
                    failing = true;
                    run.FirstRefused = operation.Kind + " of " + operation.Path;
                }

                // A short write keeps less than the whole: the system writes
                // what it can and reports the rest refused.
                return failing ? cuts.Next(Math.Max(1, operation.Length)) : null;
            };

            files.RefusalError = path => full ? SimulatedFileSystem.DiskFull(path) : new IOException("Input/output error: '" + path + "'.", 5);
            FileStorage storage = FileStorage.Open(files, new DatasetDirectory(Root), StorageOptions);
            Dataset dataset = await Dataset.CreateAsync(storage, T.Id, OptionsFor(batch), T.Ct);
            counting = true;
            SortedSet<string> state = [];
            int request = 0;

            try
            {
                // Past the steps, the workload goes on until every refusal has
                // given way and every unknown outcome is passed by the head.
                for (int step = 0; step < steps || (!failing && (dataset.StorageFull || dataset.Head.Value < MaxUnknown(run))); step += batch)
                {
                    Check(step < steps * 4, where + ": the dataset does not take writes again");
                    List<(CommitRequestBuilder Request, SortedSet<string> Next, string Cause)> group = [];

                    for (int member = 0; member < batch; member++)
                    {
                        string cause = "request " + request++;
                        (CommitRequestBuilder generated, SortedSet<string> next) = Generate(random, group.Count == 0 ? state : group[^1].Next, new CommitMetadata { Cause = T.Literal(cause) });

                        // A quad of its own, so that no member changes nothing.
                        RdfTerm own = T.Iri("c" + request);
                        generated.Assert(own, T.Iri("p0"), T.Integer("0"));
                        next.Add(T.Render(own) + " " + T.Render(T.Iri("p0")) + " " + T.Render(T.Integer("0")));
                        group.Add((generated, next, cause));
                    }

                    long head = dataset.Head.Value;
                    List<Task<CommitResult>> answers = [];

                    using (await dataset.HoldSequencerForTestsAsync(T.Ct))
                    {
                        foreach ((CommitRequestBuilder generated, _, _) in group)
                        {
                            answers.Add(dataset.CommitAsync(generated, T.Ct).AsTask());
                        }
                    }

                    bool prefix = true, refusedHere = false, failedHere = false;
                    Dictionary<string, SortedSet<string>> unknownNext = [];

                    for (int member = 0; member < group.Count; member++)
                    {
                        string cause = group[member].Cause;
                        CommitResult result;

                        try
                        {
                            result = await answers[member];
                        }
                        catch (IOException) when (!full)
                        {
                            // An I/O error is the member's answer, never a full disk's.
                            prefix = false;
                            run.Refused.Add(cause);
                            refusedHere = failedHere = true;
                            continue;
                        }
                        catch (Exception error)
                        {
                            throw new InvalidOperationException(where + ": member " + member + " of a batch threw rather than being answered", error);
                        }

                        if (result.Outcome == CommitOutcome.Committed)
                        {
                            Check(prefix, where + ": " + cause + " committed after a member that was not");
                            Check(result.Position.Value == head + member + 1, where + ": " + cause + " committed at " + result.Position.Value + ", not " + (head + member + 1));
                            run.Committed[cause] = result.Position.Value;
                            run.History[result.Position.Value] = group[member].Next;
                            state = group[member].Next;
                            run.CommittedAfterFailure |= failed;
                            continue;
                        }

                        prefix = false;
                        failedHere = true;
                        Check(result.Outcome == CommitOutcome.Unavailable, where + ": " + cause + " answered " + result.Outcome);

                        if (result.OutcomeUnknownAt is { } unknownAt)
                        {
                            Check(!refusedHere, where + ": " + cause + " outcome unknown after a member refused");
                            Check(!result.StorageFull, where + ": " + cause + " both outcome unknown and storage-full");
                            Check(unknownAt.Value == head + member + 1, where + ": " + cause + " outcome unknown at " + unknownAt.Value + ", not " + (head + member + 1));
                            run.Unknown[cause] = unknownAt.Value;
                            unknownNext[cause] = group[member].Next;
                        }
                        else
                        {
                            Check(result.StorageFull == full, where + ": " + cause + " refused " + (full ? "other than for space" : "for space after an I/O error") + ": " + result.Reason);
                            run.Refused.Add(cause);
                            refusedHere = true;
                        }
                    }

                    failed |= failedHere;

                    // The open dataset reads what it committed, and nothing else.
                    using (DatasetView pinned = dataset.Pin())
                    {
                        Same(state, T.Terms(pinned), where + ": the open dataset after the batch from step " + step);
                    }

                    if (failedHere && mode != "recover")
                    {
                        // Reopened, as an operator would, or a restart: what the
                        // log holds decides each unknown member, either way.
                        failing = false;
                        (storage, dataset) = await ReopenAsync(files, storage, dataset, batch);
                        Dictionary<long, string> causes = await CausesAsync(dataset);

                        foreach ((string cause, SortedSet<string> next) in unknownNext.OrderBy(u => run.Unknown[u.Key]))
                        {
                            long position = run.Unknown[cause];

                            if (causes.GetValueOrDefault(position) == cause)
                            {
                                run.History[position] = next;
                                state = next;
                            }
                        }

                        using (DatasetView pinned = dataset.Pin())
                        {
                            Same(state, T.Terms(pinned), where + ": reopened after the batch from step " + step);
                        }
                    }

                    try
                    {
                        if (step % 7 == 6 && dataset.Head.Value > 0)
                        {
                            await dataset.CheckpointAsync(new Position(1 + random.Next((int)dataset.Head.Value)), T.Ct);
                        }

                        await dataset.MaintainAsync(T.Ct);
                    }
                    catch (IOException)
                    {
                        // A derived write that met the failure fails on its own.
                        failed = true;
                    }

                    // The storage works again after the batches the case keeps it failing.
                    if (failed && failing && stillFailing++ >= fullBatches)
                    {
                        failing = false;
                    }
                }

                Check(at == int.MaxValue || run.Seen < at || failed, where + ": the storage failed and nothing saw it");
                Check(!failed || failing || !dataset.StorageFull, where + ": the space is back and the dataset still says it is full");
                run.Causes = await CausesAsync(dataset);
                await ReadsBackAsync(dataset, run, everything: true, where + ", the open dataset at the end");
                return run;
            }
            finally
            {
                failing = false;
                counting = false;

                try
                {
                    await dataset.DisposeAsync();
                    await storage.DisposeAsync();
                }
                catch (IOException)
                {
                }
            }
        }

        private static async Task<(FileStorage Storage, Dataset Dataset)> ReopenAsync(SimulatedFileSystem files, FileStorage storage, Dataset dataset, int batch)
        {
            try
            {
                await dataset.DisposeAsync();
                await storage.DisposeAsync();
            }
            catch (IOException)
            {
            }

            FileStorage reopened = FileStorage.Open(files, new DatasetDirectory(Root), StorageOptions);
            return (reopened, await Dataset.OpenAsync(reopened, OptionsFor(batch), T.Ct));
        }

        private static long MaxUnknown(BatchRun run) => run.Unknown.Count == 0 ? 0 : run.Unknown.Values.Max();

        // Every term committed reads back as itself: the dataset as of every
        // committed position it holds is the run's history there, and every
        // cause is the run's. Positions past the head are checked only when
        // everything must be there.
        private static async Task ReadsBackAsync(Dataset dataset, BatchRun run, bool everything, string where)
        {
            long head = dataset.Head.Value;
            Dictionary<long, string> causes = await CausesAsync(dataset);

            foreach ((long position, SortedSet<string> quads) in run.History.OrderBy(h => h.Key))
            {
                if (position > head)
                {
                    Check(!everything, where + ": " + position + " was committed and the head is " + head);
                    continue;
                }

                using DatasetView asOf = await dataset.AsOfAsync(new Position(position), T.Ct);
                Same(quads, T.Terms(asOf), where + ": as of " + position);
            }

            foreach ((string cause, long position) in run.Committed)
            {
                if (position <= head)
                {
                    Check(causes.GetValueOrDefault(position) == cause, where + ": the cause at " + position + " reads back as " + causes.GetValueOrDefault(position) + ", not " + cause);
                }
            }
        }

        // Reopens an image of the run and settles every answer against it.
        private static async Task SettleAsync(SimulatedFileSystem image, BatchRun run, bool everything, string where)
        {
            await using FileStorage storage = FileStorage.Open(image, new DatasetDirectory(Root), StorageOptions);
            Dataset opened;

            try
            {
                opened = await Dataset.OpenAsync(storage, OptionsFor(run.Batch), T.Ct);
            }
            catch (LogVerificationException error)
            {
                throw new InvalidOperationException(where + ": the log does not open", error);
            }

            await using Dataset dataset = opened;
            Dictionary<long, string> causes = await CausesAsync(dataset);
            Dictionary<string, List<long>> found = [];

            foreach ((long position, string cause) in causes)
            {
                (found.TryGetValue(cause, out List<long>? at) ? at : found[cause] = []).Add(position);
            }

            foreach ((string cause, List<long> positions) in found)
            {
                Check(positions.Count == 1, where + ": " + cause + " is in the log " + positions.Count + " times");
            }

            foreach ((string cause, long position) in run.Committed)
            {
                Check(causes.TryGetValue(position, out string? there) && there == cause, where + ": " + cause + ", answered committed at " + position + ", is not there after a reopen");
            }

            foreach (string cause in run.Refused)
            {
                Check(!found.ContainsKey(cause), where + ": " + cause + ", refused, is in the log at " + string.Join(", ", found.GetValueOrDefault(cause) ?? []));
            }

            foreach ((string cause, long position) in run.Unknown)
            {
                // Settled by the commits once the head passes the position.
                if (run.Causes.TryGetValue(position, out string? said) && (everything || causes.ContainsKey(position)))
                {
                    Check(causes.GetValueOrDefault(position) == said, where + ": at " + position + " the open dataset had " + said + " and the reopened log has " + causes.GetValueOrDefault(position));
                }

                Check(!found.TryGetValue(cause, out List<long>? at) || at.SequenceEqual([position]), where + ": " + cause + ", outcome unknown at " + position + ", is in the log elsewhere");
            }

            if (everything)
            {
                // What the open dataset committed, the reopened log holds.
                foreach ((long position, string cause) in run.Causes)
                {
                    Check(causes.GetValueOrDefault(position) == cause, where + ": at " + position + " the open dataset had " + cause + " and the reopened log has " + causes.GetValueOrDefault(position));
                }
            }

            await ReadsBackAsync(dataset, run, everything, where);
        }

        // The cause of every commit, by position.
        private static async Task<Dictionary<long, string>> CausesAsync(Dataset dataset)
        {
            Dictionary<long, string> causes = [];
            long head = dataset.Head.Value;

            if (head == 0)
            {
                return causes;
            }

            await foreach (Commit commit in dataset.Subscribe(new Position(0), SubscriptionFilter.All, T.Ct))
            {
                causes[commit.Position.Value] = commit.TryExternalise(commit.Cause, out RdfTerm? cause) ? Encoding.UTF8.GetString(cause.Lexical) : "";

                if (commit.Position.Value >= head)
                {
                    break;
                }
            }

            return causes;
        }
    }
}
