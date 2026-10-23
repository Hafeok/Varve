// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The measurements behind ADR 0177's choice of layout, over a dataset on
/// disk that <c>--asof-seek file M</c> kept (VARVE_SEEK_LAYOUT_DATASET names
/// its directory): how many intervals a key has, which decides whether keys
/// need position-sorted runs (layout 3); and how many rows and 128-row blocks
/// of the largest set a read at a position inside it could skip, which is the
/// most an interval skip index (layout 2) could save. Not a gate: without the
/// variable it does nothing.
/// </summary>
public sealed class ExtentLayoutStatistics
{
    private static readonly long[] Sections = [ExtentSection.Open, ExtentSection.Close, ExtentSection.Within];

    [Fact]
    public async Task the_largest_sets_intervals_per_key_and_the_rows_a_skip_index_could_skip()
    {
        string? directory = Environment.GetEnvironmentVariable("VARVE_SEEK_LAYOUT_DATASET");

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System }, T.Ct);
        await using Dataset dataset = await Dataset.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System, Maintenance = MaintenanceMode.Off, ExtentMerges = ExtentMergePolicy.Off }, T.Ct);
        ExtentSet largest = dataset.ExtentSetsForTests().OrderByDescending(s => s.Bytes).First();
        List<string> lines = [string.Create(CultureInfo.InvariantCulture, $"largest set ({largest.From}, {largest.To}], {largest.Bytes:N0} bytes")];

        // Intervals per key, in SPOG: a key's rows are adjacent.
        List<int> perKey = [];
        long rows = 0;

        using (KeyGroups groups = new(largest.Rows(IndexOrder.Spog)))
        {
            while (groups.Peek(out _))
            {
                int count = groups.Take().Length;
                perKey.Add(count);
                rows += count;
            }
        }

        perKey.Sort();
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"keys {perKey.Count:N0}, rows {rows:N0}; intervals a key: mean {(double)rows / perKey.Count:F3}, p50 {perKey[perKey.Count / 2]}, p99 {perKey[(int)(perKey.Count * 0.99)]}, p99.99 {perKey[(int)(perKey.Count * 0.9999)]}, max {perKey[^1]}; keys of one interval {perKey.Count(c => c == 1) / (double)perKey.Count:P1}"));

        // At a quarter, half and three quarters of the window: the rows whose
        // verdict at the position is no different from the window's start —
        // a skip index could pass them — and the 128-row blocks, in each
        // section's order, made only of such rows.
        foreach (long position in Enumerable.Range(1, 3).Select(q => largest.From + ((largest.To - largest.From) * q / 4)))
        {
            long skippable = 0;
            long blocks = 0;
            long skippableBlocks = 0;

            foreach (long section in Sections)
            {
                using SectionRows sectionRows = new(largest.Rows(IndexOrder.Spog), section);
                int inBlock = 0;
                bool allSkippable = true;

                while (sectionRows.TryNext(out ExtentRow row))
                {
                    bool atStart = row.From == 0;
                    bool covers = ExtentSeek.Covers(row.From, row.To, position);
                    bool skip = atStart == covers;
                    skippable += skip ? 1 : 0;
                    allSkippable &= skip;

                    if (++inBlock == KeySection.BlockKeys)
                    {
                        blocks++;
                        skippableBlocks += allSkippable ? 1 : 0;
                        inBlock = 0;
                        allSkippable = true;
                    }
                }

                if (inBlock > 0)
                {
                    blocks++;
                    skippableBlocks += allSkippable ? 1 : 0;
                }
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"at {position}: rows a skip index could pass {skippable / (double)rows:P1}; blocks made only of them {skippableBlocks:N0} of {blocks:N0}, {skippableBlocks / (double)Math.Max(1, blocks):P1}"));
        }

        string output = Environment.GetEnvironmentVariable("VARVE_SEEK_LAYOUT_OUT") ?? Path.Combine(directory, "layout-statistics.txt");
        await File.WriteAllLinesAsync(output, lines, TestContext.Current.CancellationToken);
    }
}
