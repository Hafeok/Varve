// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// What the soak runs: every part on by default, as milestone 6a ran it; each
/// switch turns one part off, so a run without it measures what it costs.
/// </summary>
internal sealed record SoakOptions(
    bool Checkpoints,
    bool AsOf,
    bool Pins,
    bool Maintenance,
    bool Policy,
    int Subjects,
    long MemtableLimit)
{
    internal static SoakOptions Parse(ReadOnlySpan<string> args)
    {
        SoakOptions options = new(Checkpoints: true, AsOf: true, Pins: true, Maintenance: true, Policy: false, Subjects: 20_000, MemtableLimit: 20_000);

        for (int i = 0; i < args.Length; i++)
        {
            options = args[i] switch
            {
                "--no-checkpoints" => options with { Checkpoints = false },
                "--no-asof" => options with { AsOf = false },
                "--no-pins" => options with { Pins = false },
                "--no-maintenance" => options with { Maintenance = false },
                "--policy" => options with { Policy = true, Checkpoints = false },
                "--subjects" => options with { Subjects = int.Parse(args[++i], CultureInfo.InvariantCulture) },
                "--memtable" => options with { MemtableLimit = long.Parse(args[++i], CultureInfo.InvariantCulture) },
                _ => throw new ArgumentException("Unknown soak option " + args[i] + "."),
            };
        }

        return options;
    }
}

/// <summary>
/// The one-hour soak of milestones 6a and 6c: commits, pins, as-of reads, tier
/// merges and checkpoints at once, on the file backend with background
/// maintenance. Every 30 seconds it collects garbage and prints a row: the
/// managed heap and how the collector holds it, the process's working set and
/// private bytes, and the highest working set a one-second sampler saw since
/// the last row — so that a peak is attributed to the window it happened in.
/// Run once and reported; not in CI.
/// </summary>
internal static class Soak
{
    internal static async Task RunAsync(TimeSpan duration, SoakOptions options)
    {
        // The process's own handles, before the dataset opens: the base of the
        // handles bound (ADR 0215), whatever the platform counts as a handle.
        int handlesBefore = Handles();
        string directory = Directory.CreateTempSubdirectory("varve-soak-").FullName;
        FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        DatasetOptions datasetOptions = new()
        {
            Clock = TimeProvider.System,
            MemtableLimit = new QuadCount(options.MemtableLimit),
            Maintenance = options.Maintenance ? MaintenanceMode.Background : MaintenanceMode.Off,
            Checkpoints = options.Policy ? new CheckpointPolicy { EveryCommits = 1_000, Keep = SoakBounds.KeepCheckpoints } : CheckpointPolicy.Never,
        };
        StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), datasetOptions);
        using CancellationTokenSource stop = new(duration);
        long commits = 0, reads = 0, asOf = 0, checkpoints = 0, checkpointsInWindow = 0;
        Console.WriteLine("# soak " + duration.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " min, " + options);
        Console.WriteLine(GarbageCollection());
        Console.WriteLine("# handles before the dataset " + handlesBefore.ToString(CultureInfo.InvariantCulture));

        // The writer: batches of 50 over a vocabulary of ten million possible
        // quads, a third of them retractions, pausing 25 ms between commits; the
        // dataset grows toward that bound for the whole hour.
        Task writer = Task.Run(async () =>
        {
            Random random = new(1);

            while (!stop.IsCancellationRequested)
            {
                CommitRequestBuilder request = new();

                for (int i = 0; i < 50; i++)
                {
                    RdfTerm s = RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/s" + random.Next(options.Subjects)));
                    RdfTerm p = RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/p" + random.Next(10)));
                    RdfTerm o = RdfTerm.Literal(System.Text.Encoding.UTF8.GetBytes("v" + random.Next(50)));
                    _ = random.Next(3) == 0 ? request.Retract(s, p, o) : request.Assert(s, p, o);
                }

                await dataset.CommitAsync(request);
                Interlocked.Increment(ref commits);
                await Task.Delay(25);
            }
        });

        // Readers: pinned scans of one predicate, and as-of reads at random positions.
        Task[] readers = [.. Enumerable.Range(0, 2).Select(r => Task.Run(async () =>
        {
            Random random = new(100 + r);

            while (!stop.IsCancellationRequested)
            {
                if (options.Pins)
                {
                    using (DatasetView view = dataset.Pin())
                    {
                        view.TryInternalise(RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("http://example.org/p" + random.Next(10))), out TermHandle predicate);
                        using IQuadCursor cursor = view.Match(TermHandle.None, predicate, TermHandle.None, GraphPattern.Any);

                        while (cursor.MoveNext())
                        {
                        }
                    }

                    Interlocked.Increment(ref reads);
                }

                long head = dataset.Head.Value;

                if (options.AsOf && head > 0)
                {
                    // The last 10,000 positions: as-of cost is the distance to the
                    // nearest checkpoint (R2), and this measures leaks, not that.
                    using DatasetView then = await dataset.AsOfAsync(new Position(Math.Max(1, head - random.Next(10_000))));
                    _ = then.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
                    Interlocked.Increment(ref asOf);
                }

                if (!options.Pins && !options.AsOf)
                {
                    await Task.Delay(100);
                }
            }
        }))];

        // Checkpoints every two minutes, keeping the newest three.
        Task checkpointer = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested && options.Checkpoints)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await dataset.CheckpointAsync(dataset.Head);
                Interlocked.Increment(ref checkpoints);
                Interlocked.Increment(ref checkpointsInWindow);

                while (dataset.Checkpoints.Count > 3)
                {
                    await dataset.DropCheckpointAsync(dataset.Checkpoints[0]);
                }
            }
        });

        // The one-second sampler: the highest working set and managed heap
        // since the last row, without collecting.
        long peakWorkingSet = 0, peakHeap = 0;
        Task sampler = Task.Run(async () =>
        {
            using Process self = Process.GetCurrentProcess();

            while (!stop.IsCancellationRequested)
            {
                self.Refresh();
                InterlockedMax(ref peakWorkingSet, self.WorkingSet64);
                InterlockedMax(ref peakHeap, GC.GetTotalMemory(false));

                try
                {
                    await Task.Delay(1000, stop.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });

        Stopwatch clock = Stopwatch.StartNew();
        Console.WriteLine(Header);

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stop.Token);
            }
            catch (OperationCanceledException)
            {
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GCMemoryInfo gc = GC.GetGCMemoryInfo(GCKind.Any);
            using Process self = Process.GetCurrentProcess();
            int handles = Handles();
            int segments = Directory.GetFiles(Path.Combine(directory, "log")).Length;
            string[] derivedFiles = Directory.GetFiles(Path.Combine(directory, "derived"), "*", SearchOption.AllDirectories);
            long derivedBytes = derivedFiles.Sum(SizeOf);
            long directories = derivedFiles.Where(f => !IsCheckpoint(f)).Sum(DirectoryLength);
            long checkpointDirectories = derivedFiles.Where(IsCheckpoint).Sum(DirectoryLength);
            long quads = 0;

            try
            {
                using DatasetView view = dataset.Pin();
                quads = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
            }
            catch (DatasetUnavailableException)
            {
            }

            Console.WriteLine(string.Join(
                ",",
                clock.Elapsed.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture),
                Interlocked.Read(ref commits).ToString(CultureInfo.InvariantCulture),
                dataset.Head.Value.ToString(CultureInfo.InvariantCulture),
                quads.ToString(CultureInfo.InvariantCulture),
                Mb(GC.GetTotalMemory(false)),
                Mb(gc.HeapSizeBytes),
                Mb(gc.FragmentedBytes),
                Mb(gc.TotalCommittedBytes),
                Mb(gc.GenerationInfo[3].SizeAfterBytes),
                Mb(self.WorkingSet64),
                Mb(self.PrivateMemorySize64),
                Mb(Interlocked.Exchange(ref peakWorkingSet, 0)),
                Mb(Interlocked.Exchange(ref peakHeap, 0)),
                handles.ToString(CultureInfo.InvariantCulture),
                derivedFiles.Length.ToString(CultureInfo.InvariantCulture),
                Mb(derivedBytes),
                Interlocked.Read(ref reads).ToString(CultureInfo.InvariantCulture),
                Interlocked.Read(ref asOf).ToString(CultureInfo.InvariantCulture),
                Interlocked.Read(ref checkpoints).ToString(CultureInfo.InvariantCulture),
                Interlocked.Exchange(ref checkpointsInWindow, 0).ToString(CultureInfo.InvariantCulture),
                (GC.GetTotalAllocatedBytes() / 1e9).ToString("F1", CultureInfo.InvariantCulture),
                Mb(directories),
                Mb(checkpointDirectories),
                Mb(gc.PromotedBytes),
                Mb(Math.Max(0, self.PrivateMemorySize64 - gc.TotalCommittedBytes)),
                Mb(DatasetsOwn(directories, checkpointDirectories, dataset.Head.Value)),
                segments.ToString(CultureInfo.InvariantCulture)));
        }

        await writer;
        await Task.WhenAll(readers);
        await checkpointer;
        await sampler;
        await dataset.DisposeAsync();

        // The soak's own end-to-end check: what it wrote reopens.
        Stopwatch reopening = Stopwatch.StartNew();
        StoreDatasetType reopened = await StoreDatasetType.OpenAsync(storage, datasetOptions);
        Console.WriteLine("reopened at " + reopened.Head.Value.ToString(CultureInfo.InvariantCulture) + " of " + Interlocked.Read(ref commits).ToString(CultureInfo.InvariantCulture)
            + " commits in " + reopening.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
        await reopened.DisposeAsync();
        await storage.DisposeAsync();

        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// The CSV's columns, which <see cref="SoakJudge"/> reads by name. The
    /// live heap is <c>managed MB</c>, taken after a full collection; native
    /// memory is the process's private bytes less the collector's committed
    /// memory; <c>own MB</c> is the dataset's own (ADR 0082).
    /// </summary>
    internal const string Header =
        "minutes,commits,head,quads,managed MB,heap MB,fragmented MB,committed MB,LOH MB,working set MB,private MB,peak working set MB,peak heap MB,"
        + "open handles,derived files,derived MB,pins,as-of reads,checkpoints,checkpoint in window,allocated GB,directories MB,checkpoint directories MB,"
        + "promoted MB,native MB,own MB,log segments";

    /// <summary>
    /// The dataset's own (ADR 0082): what grows with the dataset and nothing
    /// else. The runs' directories and filters whole, a twentieth of the
    /// checkpoints' and extent sets', which are held sparsely, and the table
    /// of commits, 144 bytes a commit.
    /// </summary>
    internal static long DatasetsOwn(long directories, long checkpointDirectories, long commits) =>
        directories + (checkpointDirectories / 20) + (commits * 144);

    // The directory of a derived run or checkpoint: what its reader holds in
    // memory, the fences and where each block begins (ADR 0080), read from
    // the header at the file's end (storage-format.md §7), and, in a
    // format-3 file, its term filter, eight bits a term (ADR 0109), whose
    // length is the directory's last word. What ADR 0082 counts as the
    // dataset's own; zero for a file that is not a run.
    private static bool IsCheckpoint(string path) =>
        path.Replace('\\', '/').Contains("/derived/checkpoints/", StringComparison.Ordinal)
        || path.Replace('\\', '/').Contains("/derived/extents/", StringComparison.Ordinal);

    // The process's open handles: file descriptors where /proc has them, the
    // kernel's handle count elsewhere.
    private static int Handles()
    {
        using Process self = Process.GetCurrentProcess();
        return Directory.Exists("/proc/self/fd") ? Directory.GetFiles("/proc/self/fd").Length : self.HandleCount;
    }

    /// <summary>
    /// The collector the run had, as the runtime reports it, on the line
    /// <see cref="SoakJudge"/> reads: the mode, whether collections are
    /// concurrent, and ConserveMemory, from the runtimeconfig the shipped
    /// props give or from the environment (ADR 0110, ADR 0215).
    /// </summary>
    internal static string GarbageCollection()
    {
        string conserve = AppContext.GetData("System.GC.ConserveMemory") as string
            ?? Environment.GetEnvironmentVariable("DOTNET_GCConserveMemory")
            ?? "0";
        bool concurrent = System.Runtime.GCSettings.LatencyMode != System.Runtime.GCLatencyMode.Batch;
        return "# gc " + (System.Runtime.GCSettings.IsServerGC ? "server" : "workstation") + ", " + (concurrent ? "concurrent" : "not concurrent") + ", ConserveMemory " + conserve;
    }

    // A file listed a moment ago may be gone: a temporary file renamed into
    // place, or a file maintenance deleted. It counts as nothing.
    private static long SizeOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
    }

    private static long DirectoryLength(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);

            if (file.Length < 160)
            {
                return 0;
            }

            Span<byte> header = stackalloc byte[160];
            file.Seek(-160, SeekOrigin.End);
            file.ReadExactly(header);

            if (!header[..4].SequenceEqual("VRVD"u8))
            {
                return 0;
            }

            long directory = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[80..]);
            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);

            // Version 6 (ADR 0213) puts the term pages' checks after the
            // directory's words, so the filter's length is found by walking
            // the directory from its start.
            if (version >= 6 && kind is 1 or 2)
            {
                long at = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);
                byte[] words = new byte[directory];
                file.Seek(at, SeekOrigin.Begin);
                file.ReadExactly(words);
                return directory + FilterLengthOf(words);
            }

            // Only a run or a checkpoint (kinds 1 and 2) carries a filter; the
            // projection state and the commit index are version 3 too, and
            // their directory's last word is something else.
            if (BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) < 3 || kind > 2 || directory < 8)
            {
                return directory;
            }

            Span<byte> last = stackalloc byte[8];
            file.Seek(-160 - 8, SeekOrigin.End);
            file.ReadExactly(last);
            return directory + (long)BinaryPrimitives.ReadUInt64LittleEndian(last);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // A version-6 run's directory (storage format §7.2): the keys per block,
    // the section count, each section's offset, count and length, then per
    // block a fence of 32 bytes, a start and a check of 8 each, then the
    // term section's words, of which the ninth is the filter's length.
    private static long FilterLengthOf(ReadOnlySpan<byte> directory)
    {
        int sections = BinaryPrimitives.ReadInt32LittleEndian(directory[4..]);
        long blocks = 0;

        for (int i = 0; i < sections; i++)
        {
            long count = BinaryPrimitives.ReadInt64LittleEndian(directory[(8 + (i * 24) + 8)..]);
            blocks += (count + 127) / 128;
        }

        int words = 8 + (sections * 24) + (int)(blocks * (32 + 8 + 8));
        return BinaryPrimitives.ReadInt64LittleEndian(directory[(words + (8 * 8))..]);
    }

    private static string Mb(long bytes) => (bytes / 1e6).ToString("F1", CultureInfo.InvariantCulture);

    private static void InterlockedMax(ref long target, long value)
    {
        long current = Interlocked.Read(ref target);

        while (value > current)
        {
            long seen = Interlocked.CompareExchange(ref target, value, current);

            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
