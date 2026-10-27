// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// A bulk load's spill found damaged when the load reads it back (ADR 0213,
/// #146): the one derived kind the log cannot rebuild, since a spill is the
/// load's own input, sorted. Every byte of every spill is damaged in turn,
/// while the load runs. The load either commits exactly what it was given,
/// when the damage is in no byte it reads, or fails with
/// <see cref="BulkLoadSpillDamagedException"/> before its commit, and the
/// dataset, opened again, is what it was before the load.
/// </summary>
public sealed class SpillDamageTests
{
    private static BulkLoadOptions Tiny => new() { SortRecords = 3, TermBytes = 64, Workers = 1 };

    [Fact]
    public async Task a_damaged_spill_fails_its_load_with_nothing_committed()
    {
        await using TemporaryDirectory directory = new(flushes: false);
        MidReadDamageTests.Damage damage = new();
        IFileSystem files = new MidReadDamageTests.DamagingFileSystem(new UnflushedFileSystem(), damage);
        string before;

        await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
        await using (Dataset dataset = await T.OpenOrCreate(storage, T.Options()))
        {
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("before"), T.Iri("p"), T.Literal("0")), T.Ct);
            before = string.Join(" ", T.Terms(dataset.Pin()));
        }

        // Every byte a spill of this load holds, whatever its layout: the
        // checks are the load's, in memory. The largest spill is a few
        // hundred bytes.
        Sweep sweep = Depth.SweepOf("SPILL_DAMAGE", 4);
        List<string> wrong = [];
        int failed = 0;
        int committed = 0;

        for (int at = sweep.First - 1; at < 600; at += sweep.Stride)
        {
            damage.Clear();
            damage.ArmUnder("bulk/", at);

            await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
            await using (Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct))
            {
                damage.Live(record: false);

                try
                {
                    await using BulkLoad load = await dataset.BeginBulkLoadAsync(Tiny, T.Ct);

                    for (int i = 0; i < 40; i++)
                    {
                        load.Assert(T.Iri("s" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("ø " + i.ToString(CultureInfo.InvariantCulture)));
                    }

                    CommitResult result = await load.CommitAsync(new CommitMetadata(), T.Ct);

                    if (result.Outcome != CommitOutcome.Committed || dataset.Head.Value != 2 || T.Terms(dataset.Pin()).Count != 41)
                    {
                        wrong.Add("byte " + at + ": committed something other than the load");
                    }

                    committed++;
                }
                catch (BulkLoadSpillDamagedException)
                {
                    failed++;
                }
                catch (Exception error) when (error is not Xunit.Sdk.XunitException)
                {
                    wrong.Add("byte " + at + ": " + error.GetType().Name + ": " + error.Message);
                }
                finally
                {
                    damage.StopRecording();
                    damage.Disarm();
                }
            }

            // The load committed, or nothing of it is there: undo a commit
            // by starting again from the dataset before it.
            await using (FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options))
            await using (Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct))
            {
                if (dataset.Head.Value == 1 && string.Join(" ", T.Terms(dataset.Pin())) != before)
                {
                    wrong.Add("byte " + at + ": the dataset is not what it was before the load");
                }
            }

            if (committed > 0)
            {
                // A load that committed moved the dataset on; the next case
                // starts from a fresh one with the same commit before it.
                await RestartAsync(directory, files, damage);
                committed = 0;
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(sweep.Replay + ": " + failed + " loads failed on a damaged spill");
        Assert.True(wrong.Count == 0, sweep.Replay + " replays this. " + string.Join("\n  ", wrong));
        Assert.True(failed > 0, sweep.Replay + ": no load met its damage.");
    }

    private static async Task RestartAsync(TemporaryDirectory directory, IFileSystem files, MidReadDamageTests.Damage damage)
    {
        damage.Clear();
        System.IO.Directory.Delete(directory.Path, recursive: true);
        System.IO.Directory.CreateDirectory(directory.Path);

        await using FileStorage storage = FileStorage.Open(files, directory.Directory, TemporaryDirectory.Options);
        await using Dataset dataset = await T.OpenOrCreate(storage, T.Options());
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("before"), T.Iri("p"), T.Literal("0")), T.Ct);
    }
}
