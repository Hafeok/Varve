// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// A set written while a reader of derived format 5 is announced keeps its
/// shared blobs in version 4, unchecked, as that reader reads them (ADR
/// 0213 point 7). Maintenance rewrites none of them while the reader is
/// there; once it reads 6, it rewrites them, the oldest first, one a round,
/// until every set is checked, and the answers never change.
/// </summary>
public sealed class UncheckedSetTests
{
    [Fact]
    public async Task unchecked_sets_are_rewritten_only_once_no_reader_reads_less_than_6()
    {
        await using TemporaryDirectory directory = new(flushes: false);
        await using FlushRuleTests.Announced storage = new(FileStorage.Open(new UnflushedFileSystem(), directory.Directory, TemporaryDirectory.Options), 5);
        DatasetOptions options = new() { Clock = ManualClock.Epoch(), CommitCache = T.CommitCache, MemtableLimit = new QuadCount(8), Maintenance = MaintenanceMode.Off };
        await using Dataset dataset = await T.OpenOrCreate(storage, options);
        await MidReadDamageTests.WriteHistoryAsync(dataset);
        List<string> expected = await ViewsAsync(dataset);
        int written = dataset.UncheckedSets;
        Assert.True(written >= 2, written + " unchecked sets were written under a reader of 5");

        // The reader reads 5: whatever maintenance merges stays unchecked,
        // and no set is rewritten for its version.
        while (await dataset.MergeOnceForTestsAsync(T.Ct))
        {
        }

        Assert.Equal(dataset.ExtentSetsForTests().Count, dataset.UncheckedSets);
        Assert.Equal(expected, await ViewsAsync(dataset));

        // It restarts on this build: every set is rewritten, one a round.
        storage.MaxDerived = 6;
        int rounds = 0;

        while (await dataset.MergeOnceForTestsAsync(T.Ct))
        {
            rounds++;
            Assert.Equal(expected, await ViewsAsync(dataset));
        }

        Assert.Equal(0, dataset.UncheckedSets);
        Assert.True(rounds >= 1);
        Assert.All(dataset.ExtentSetsForTests(), set => Assert.True(set.Checked));
    }

    private static async Task<List<string>> ViewsAsync(Dataset dataset)
    {
        List<string> views = [];

        for (long q = 1; q <= dataset.Head.Value; q++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(q), T.Ct);
            views.Add(q + ": " + string.Join(" ", T.Terms(view)));
        }

        return views;
    }
}
