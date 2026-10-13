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
/// The graph-version index's size (ADR 0125), for a dataset with a graph per
/// entity: N quads, each in a graph of its own, against a control of the same
/// N terms in the default graph. Measured through the public surface: the
/// managed heap the opened dataset holds, the time to open it with the
/// persisted index and with the index rebuilt from the log, the size of
/// <c>derived/index/graphs</c>, and the cost of one graph's version at the
/// head and as of the checkpoint below it.
/// </summary>
internal static class GraphIndexBench
{
    private const int Lookups = 100_000;

    internal static async Task RunAsync()
    {
        Console.WriteLine("| Graphs | Heap, graphs | Heap, control | Index on the heap | index/graphs | Open | Open, rebuilt from the log | Version at the head | Version as of the checkpoint |");
        Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (int n in (int[])[100_000, 1_000_000])
        {
            string graphs = await BuildAsync(n, perGraph: true);
            string control = await BuildAsync(n, perGraph: false);

            (long heapGraphs, double open, double headLookup, double asOfLookup) = await OpenAsync(graphs, n, measureLookups: true);
            (long heapControl, _, _, _) = await OpenAsync(control, n, measureLookups: false);
            long blob = new FileInfo(Path.Combine(graphs, "derived", "index", "graphs")).Length;
            File.Delete(Path.Combine(graphs, "derived", "index", "graphs"));
            (_, double rebuilt, _, _) = await OpenAsync(graphs, n, measureLookups: false);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {n:N0} | {heapGraphs / 1048576.0:F1} MiB | {heapControl / 1048576.0:F1} MiB | {(heapGraphs - heapControl) / 1048576.0:F1} MiB ({(heapGraphs - heapControl) / (double)n:F0} B a graph) | {blob / 1048576.0:F1} MiB | {open * 1000:F0} ms | {rebuilt * 1000:F0} ms | {headLookup:F2} µs | {asOfLookup:F2} µs |"));
            Directory.Delete(graphs, recursive: true);
            Directory.Delete(control, recursive: true);
        }
    }

    // N quads in one bulk commit — each in its own graph, or all in the
    // default graph with the graph's IRI as the object — then a second commit
    // and a checkpoint below it, and maintenance, which writes index/graphs.
    private static async Task<string> BuildAsync(int n, bool perGraph)
    {
        string directory = Directory.CreateTempSubdirectory("varve-graphs-").FullName;
        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Maintenance = MaintenanceMode.Off });

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(new BulkLoadOptions()))
        {
            for (int i = 0; i < n; i++)
            {
                RdfTerm graph = Iri("g", i);

                if (perGraph)
                {
                    load.Assert(Iri("s", 0), Iri("p", 0), Iri("o", 0), graph);
                }
                else
                {
                    load.Assert(Iri("s", 0), Iri("p", 0), graph);
                }
            }

            await load.CommitAsync(new CommitMetadata());
        }

        await dataset.CheckpointAsync(dataset.Head);
        await dataset.CommitAsync(new CommitRequest().Assert(Iri("s", 1), Iri("p", 1), Iri("o", 1), Iri("g", 1)));
        await dataset.MaintainAsync();
        await dataset.DisposeAsync();
        return directory;
    }

    private static async Task<(long Heap, double OpenSeconds, double HeadMicroseconds, double AsOfMicroseconds)> OpenAsync(string directory, int n, bool measureLookups)
    {
        await using FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        long before = Heap();
        Stopwatch opening = Stopwatch.StartNew();
        StoreDatasetType dataset = await StoreDatasetType.OpenAsync(storage, new DatasetOptions { Clock = TimeProvider.System, Maintenance = MaintenanceMode.Off });
        double open = opening.Elapsed.TotalSeconds;
        long heap = Heap() - before;
        double head = 0, asOf = 0;

        if (measureLookups)
        {
            Random random = new(1);
            Stopwatch lookups = Stopwatch.StartNew();

            for (int i = 0; i < Lookups; i++)
            {
                _ = dataset.GraphVersion(Iri("g", random.Next(n)));
            }

            head = lookups.Elapsed.TotalMilliseconds * 1000 / Lookups;

            // Graph 1 was touched after the checkpoint: as of the checkpoint,
            // its version is read from the checkpoint's index.
            lookups.Restart();
            const int AsOfLookups = 2_000;

            for (int i = 0; i < AsOfLookups; i++)
            {
                _ = await dataset.GraphVersionAsync(Iri("g", 1), new Position(1));
            }

            asOf = lookups.Elapsed.TotalMilliseconds * 1000 / AsOfLookups;
        }

        GC.KeepAlive(dataset);
        await dataset.DisposeAsync();
        return (heap, open, head, asOf);
    }

    private static long Heap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static RdfTerm Iri(string prefix, int n) =>
        RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/" + prefix + n.ToString(CultureInfo.InvariantCulture)));
}
