// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// R2's cost, measured (ADR 0078): an as-of read at a given log distance
/// from the nearest checkpoint below it, on datasets of two sizes, on files.
/// The claim is that the time follows the distance and not the dataset.
/// </summary>
internal static class AsOfLatency
{
    internal static async Task RunAsync()
    {
        Console.WriteLine("| Dataset | Distance (commits of 50 quads) | As-of read, median |");
        Console.WriteLine("|---:|---:|---:|");

        foreach (int baseCommits in new[] { 2_000, 40_000 })
        {
            string directory = Directory.CreateTempSubdirectory("varve-asof-").FullName;
            FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
            DatasetOptions options = new() { Clock = TimeProvider.System, Maintenance = MaintenanceMode.Off };
            StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), options);

            await using (BulkLoad load = await dataset.BeginBulkLoadAsync())
            {
                for (int i = 0; i < baseCommits * 50; i++)
                {
                    load.Assert(Term("s", i / 10), Term("p", i % 10), Term("o", i));
                }

                await load.CommitAsync(new CommitMetadata());
            }

            await dataset.CheckpointAsync(dataset.Head);
            long checkpoint = dataset.Head.Value;

            for (int c = 0; c < 10_000; c++)
            {
                CommitRequest request = new();

                for (int i = 0; i < 50; i++)
                {
                    request.Assert(Term("t", c), Term("p", i % 10), Term("o", (c * 50) + i));
                }

                await dataset.CommitAsync(request);
            }

            await dataset.MaintainAsync();

            foreach (int distance in new[] { 0, 10, 100, 1_000, 10_000 })
            {
                Position at = new(checkpoint + distance);
                double[] times = new double[distance >= 10_000 ? 5 : 21];

                for (int r = 0; r < times.Length; r++)
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    using DatasetView view = await dataset.AsOfAsync(at);
                    _ = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
                    times[r] = clock.Elapsed.TotalMilliseconds;
                }

                Array.Sort(times);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"| {baseCommits * 50:N0} quads | {distance:N0} | {times[times.Length / 2]:F2} ms |"));
            }

            await dataset.DisposeAsync();
            await storage.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RdfTerm Term(string prefix, int i) =>
        RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/" + prefix + i.ToString(CultureInfo.InvariantCulture)));
}
