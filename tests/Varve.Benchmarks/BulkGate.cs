// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>
/// The bulk loader's gate of milestone 6c: generated N-Quads, parsed by the
/// N-Quads parser and fed to a bulk load as the parser hands each quad over,
/// into an empty dataset or a populated one, with the peak memory recorded
/// and the managed heap capped by the runtime (run under
/// <c>DOTNET_GCHeapHardLimit</c>), then checked: the count of quads, and a
/// sample of generated quads present and of others absent.
/// </summary>
/// <remarks>
/// Quad <c>i</c> has subject <c>s{i / 10}</c> and predicate <c>p{i % 10}</c>,
/// so no two generated quads are the same, and its object and graph come from
/// a hash of <c>i</c>: an IRI of another subject, a plain literal from a
/// vocabulary of a fifth as many values as quads, a language-tagged one, an
/// integer, in the default graph or one of ten named ones. A load into a
/// populated dataset overlaps it by half of what it held, so half of that is
/// redundant and the merge-join must find it.
/// </remarks>
internal static class BulkGate
{
    private const int Chunk = 1 << 16;

    internal static async Task RunAsync(string[] args)
    {
        long quads = long.Parse(args[1], CultureInfo.InvariantCulture);
        long populated = 0;
        long memory = 1L << 30;
        string? file = null;
        string? write = null;
        bool crashes = false;
        bool closedOnly = false;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--populated":
                    populated = long.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--memory-mib":
                    memory = long.Parse(args[++i], CultureInfo.InvariantCulture) << 20;
                    break;
                case "--from-file":
                    file = args[++i];
                    break;
                case "--write-file":
                    write = args[++i];
                    break;
                case "--crash-every-record":
                    crashes = true;
                    break;
                case "--crash-closed-only":
                    crashes = true;
                    closedOnly = true;
                    break;
                default:
                    throw new ArgumentException("Unknown option " + args[i]);
            }
        }

        if (write is not null)
        {
            using FileStream output = File.Create(write);
            byte[] buffer = new byte[1 << 22];

            for (long at = 0; at < quads; at += Chunk)
            {
                int length = Generate(at, Math.Min(Chunk, quads - at), quads, buffer, out buffer);
                output.Write(buffer, 0, length);
            }

            Console.WriteLine("wrote " + quads.ToString("N0", CultureInfo.InvariantCulture) + " quads to " + write + ": " + (output.Length / 1e9).ToString("F2", CultureInfo.InvariantCulture) + " GB");
            return;
        }

        string directory = Directory.CreateTempSubdirectory("varve-bulk-").FullName;
        Console.WriteLine("# bulk gate: " + quads.ToString("N0", CultureInfo.InvariantCulture) + " quads"
            + (populated > 0 ? " into a dataset of " + populated.ToString("N0", CultureInfo.InvariantCulture) : " into an empty dataset")
            + ", BulkLoadOptions.MemoryBytes " + (memory >> 20).ToString(CultureInfo.InvariantCulture) + " MiB, heap limit "
            + (Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimit") ?? "none") + ", " + directory);

        FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        DatasetOptions options = new() { Clock = TimeProvider.System, Maintenance = MaintenanceMode.Off };
        StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), options);
        BulkLoadOptions bulk = new() { MemoryBytes = new ByteCount(memory) };
        long start = 0;

        if (populated > 0)
        {
            (TimeSpan baseTime, _, _) = await LoadAsync(dataset, bulk, 0, populated, populated, null);
            Console.WriteLine("base: " + populated.ToString("N0", CultureInfo.InvariantCulture) + " quads in " + baseTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
            start = populated / 2;
        }

        // The state before the load, for the crash check: derived/ copied,
        // and the head and count it holds.
        string before = directory + "-before";
        long headBefore = dataset.Head.Value;
        long countBefore;

        using (DatasetView view = dataset.Pin())
        {
            countBefore = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
        }

        if (crashes)
        {
            await dataset.DisposeAsync();
            Directory.CreateDirectory(Path.Combine(before, "derived"));

            foreach (string path in Directory.GetFiles(Path.Combine(directory, "derived"), "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);

                if (name is not "LOCK" and not "LOCK.owner")
                {
                    string target = Path.Combine(before, "derived", Path.GetRelativePath(Path.Combine(directory, "derived"), path));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(path, target);
                }
            }

            dataset = await StoreDatasetType.OpenAsync(storage, options);
        }

        (TimeSpan time, long peakHeap, long peakWorkingSet) = await LoadAsync(dataset, bulk, start, quads, populated > 0 ? populated : quads, file);
        long expected = populated > 0 ? populated + quads - (populated - start) : quads;

        using (DatasetView view = dataset.Pin())
        {
            long count = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
            Console.WriteLine("loaded: " + count.ToString("N0", CultureInfo.InvariantCulture) + " quads, expected " + expected.ToString("N0", CultureInfo.InvariantCulture));
            Check(count == expected, "the count is " + count + " where " + expected + " was expected");
            Sample(view, start, quads, populated > 0 ? populated : quads, populated);
        }

        long log = Size(Path.Combine(directory, "log"));
        long derived = Size(Path.Combine(directory, "derived"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"bulk load: {time.TotalSeconds:F1} s, {quads / time.TotalSeconds:N0} quads/s; peak managed heap {peakHeap / 1e6:F0} MB, peak working set {peakWorkingSet / 1e6:F0} MB; log/ {log / 1e9:F2} GB ({(double)log / expected:F1} B/quad), derived/ {derived / 1e9:F2} GB ({(double)derived / expected:F1} B/quad)"));

        await dataset.DisposeAsync();

        if (crashes)
        {
            await CrashEveryRecordAsync(storage, before, options, headBefore, countBefore, expected, closedOnly);
            Directory.Delete(before, recursive: true);
        }

        Stopwatch reopening = Stopwatch.StartNew();
        StoreDatasetType reopened = await StoreDatasetType.OpenAsync(storage, options);
        Console.WriteLine("reopened at " + reopened.Head.Value + " in " + reopening.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
        await reopened.DisposeAsync();
        await storage.DisposeAsync();

        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// The crash check at scale: the log cut at the start of every record of
    /// the load's commit and one byte into it, opened with derived/ as it was
    /// before the load, must open at the head before the load with its count;
    /// </summary>
    private static async Task CrashEveryRecordAsync(FileStorage storage, string before, DatasetOptions options, long headBefore, long countBefore, long countAfter, bool closedOnly)
    {
        Stopwatch clock = Stopwatch.StartNew();
        await using FileStorage earlier = await FileStorage.OpenAsync(new DatasetDirectory(before), new FileStorageOptions { Clock = TimeProvider.System });
        List<(int Segment, long Offset)> cuts = [];

        foreach (SegmentInfo segment in await storage.Log.ListSegmentsAsync(default))
        {
            long at = 104;

            while (at + 128 <= segment.Length.Value)
            {
                ReadOnlyMemory<byte> header = await storage.Log.ReadRangeAsync(segment.Id, new ByteOffset(at), new ByteCount(128), default);
                long position = BinaryPrimitives.ReadInt64LittleEndian(header.Span[8..]);
                uint body = BinaryPrimitives.ReadUInt32LittleEndian(header.Span);

                if (header.Span[..4].SequenceEqual("VRVT"u8) && at + 104 == segment.Length.Value)
                {
                    break;
                }

                if (position == headBefore + 1)
                {
                    cuts.Add((segment.Id.Value, at));
                }

                at += 128 + body;
            }
        }

        // One byte into a record too, for the first four, the last four and
        // eight between: each cut reads the unclosed commit up to it, so
        // every record twice over would cost hours, not what it shows.
        List<(int Segment, long Offset)> torn = [];

        for (int i = 0; i < cuts.Count; i++)
        {
            if (i < 4 || i >= cuts.Count - 4 || i % Math.Max(1, cuts.Count / 8) == 0)
            {
                torn.Add((cuts[i].Segment, cuts[i].Offset + 1));
            }
        }

        int done = 0;

        foreach ((int segment, long offset) in closedOnly ? [] : (IEnumerable<(int, long)>)[.. cuts, .. torn])
        {
            await CheckAsync(new CutStorage(storage, earlier, segment, offset), headBefore, countBefore);

            if (++done % 100 == 0)
            {
                Console.WriteLine("  crashes: " + done + " cuts opened, " + clock.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");
            }
        }

        Console.WriteLine("crashes: the log cut at the start of each of the load's " + cuts.Count + " records, and " + torn.Count
            + " one byte into one, opened at the head before the load with its quads, in " + clock.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");

        // And after the commit closed, before the state naming its delta run
        // was written: the whole log, derived/ as the load left it, and the
        // state as it was before. The run is adopted, not the load replayed.
        IReadOnlyList<SegmentInfo> all = await storage.Log.ListSegmentsAsync(default);
        CutStorage closed = new(storage, storage, all[^1].Id.Value, all[^1].Length.Value);
        BlobName stateName = new("index/state");

        // An empty dataset has no state before the load: then none is left.
        if (System.Linq.Enumerable.Contains(await earlier.Derived.ListAsync(default), stateName))
        {
            using IReadableBlob state = await earlier.Derived.OpenAsync(stateName, default);
            byte[] bytes = new byte[state.Length.Value];
            state.Read(new ByteOffset(0), bytes);
            closed.Replace(stateName, bytes);
        }
        else
        {
            closed.Remove(stateName);
        }

        GC.Collect();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch opening = Stopwatch.StartNew();
        await CheckAsync(closed, headBefore + 1, countAfter);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"crashes: the commit closed and its state lost, opened at the load's head with its quads in {opening.Elapsed.TotalSeconds:F1} s, the managed heap {heapBefore / 1e6:F0} MB before and {GC.GetTotalMemory(false) / 1e6:F0} MB after"));

        async Task CheckAsync(CutStorage cut, long head, long count)
        {
            await using StoreDatasetType opened = await StoreDatasetType.OpenAsync(cut, options);
            using DatasetView view = opened.Pin();
            long found = view.Estimate(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any).Count.Value;
            Check(opened.Head.Value == head && found == count, "a cut log opened at " + opened.Head.Value + " with " + found + " quads, where " + head + " and " + count + " were due");
        }
    }

    private static async Task<(TimeSpan Time, long PeakHeap, long PeakWorkingSet)> LoadAsync(
        StoreDatasetType dataset, BulkLoadOptions options, long from, long count, long vocabulary, string? file)
    {
        long peakHeap = 0, peakWorkingSet = 0;
        using CancellationTokenSource done = new();
        Task sampler = Task.Run(async () =>
        {
            using Process self = Process.GetCurrentProcess();

            while (!done.IsCancellationRequested)
            {
                self.Refresh();
                peakWorkingSet = Math.Max(peakWorkingSet, self.WorkingSet64);
                peakHeap = Math.Max(peakHeap, GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes);

                try
                {
                    await Task.Delay(100, done.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });

        Stopwatch clock = Stopwatch.StartNew();
        Stopwatch phase = Stopwatch.StartNew();

        await using (BulkLoad load = await dataset.BeginBulkLoadAsync(options))
        {
            ParseOptions parse = new() { Syntax = RdfSyntax.NQuads };

            if (file is not null)
            {
                using FileStream input = File.OpenRead(file);
                ParseResult result = NQuadsParser.Parse(input, load.Assert, in parse);
                Check(result.Succeeded, "the input did not parse");
            }
            else
            {
                byte[] buffer = new byte[1 << 22];

                for (long at = from; at < from + count; at += Chunk)
                {
                    int length = Generate(at, Math.Min(Chunk, from + count - at), vocabulary, buffer, out buffer);
                    ParseResult result = NQuadsParser.Parse(buffer.AsSpan(0, length), load.Assert, in parse);
                    Check(result.Succeeded, "a generated chunk did not parse");
                }
            }

            Console.WriteLine("  input: " + load.Operations.ToString("N0", CultureInfo.InvariantCulture) + " operations parsed, resolved and sorted in " + phase.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
            phase.Restart();
            CommitResult committed = await load.CommitAsync(new CommitMetadata());
            Console.WriteLine("  commit: " + committed + " in " + phase.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");
            Check(committed.Outcome == CommitOutcome.Committed, "the load did not commit");
        }

        TimeSpan elapsed = clock.Elapsed;
        await done.CancelAsync();
        await sampler;
        return (elapsed, peakHeap, peakWorkingSet);
    }

    /// <summary>The N-Quads of quads <paramref name="from"/> onward, into the buffer; returns its length.</summary>
    private static int Generate(long from, long count, long vocabulary, byte[] buffer, out byte[] used)
    {
        if (buffer.Length < count * 200)
        {
            buffer = new byte[count * 200];
        }

        used = buffer;
        int at = 0;

        for (long i = from; i < from + count; i++)
        {
            at += Write(buffer.AsSpan(at), i, vocabulary);
        }

        return at;
    }

    /// <summary>Quad <paramref name="i"/> as one line of N-Quads.</summary>
    private static int Write(Span<byte> line, long i, long vocabulary)
    {
        ulong h = Mix((ulong)i);
        long subjects = Math.Max(1, vocabulary / 10);
        int at = 0;
        Put(line, ref at, "<http://example.org/s"u8);
        Number(line, ref at, i / 10);
        Put(line, ref at, "> <http://example.org/p"u8);
        Number(line, ref at, i % 10);
        Put(line, ref at, "> "u8);

        switch (h % 10)
        {
            case < 4:
                Put(line, ref at, "<http://example.org/s"u8);
                Number(line, ref at, (long)((h >> 8) % (ulong)subjects));
                Put(line, ref at, ">"u8);
                break;
            case < 8:
                Put(line, ref at, "\"value "u8);
                Number(line, ref at, (long)((h >> 8) % (ulong)Math.Max(1, vocabulary / 5)));
                Put(line, ref at, "\""u8);
                break;
            case 8:
                Put(line, ref at, "\"text "u8);
                Number(line, ref at, (long)((h >> 8) % 1000));
                Put(line, ref at, "\"@en"u8);
                break;
            default:
                Put(line, ref at, "\""u8);
                Number(line, ref at, (long)((h >> 8) % 100_000));
                Put(line, ref at, "\"^^<http://www.w3.org/2001/XMLSchema#integer>"u8);
                break;
        }

        long graph = (long)((h >> 40) % 11);

        if (graph > 0)
        {
            Put(line, ref at, " <http://example.org/g"u8);
            Number(line, ref at, graph);
            Put(line, ref at, ">"u8);
        }

        Put(line, ref at, " .\n"u8);
        return at;
    }

    private static void Put(Span<byte> line, ref int at, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(line[at..]);
        at += bytes.Length;
    }

    private static void Number(Span<byte> line, ref int at, long value)
    {
        value.TryFormat(line[at..], out int written, default, CultureInfo.InvariantCulture);
        at += written;
    }

    /// <summary>Generated quads, at random, are in the dataset, and quads past the load are not.</summary>
    private static void Sample(DatasetView view, long from, long count, long vocabulary, long populated)
    {
        Random random = new(6);
        int checkedIn = 0, checkedOut = 0;

        for (int n = 0; n < 10_000; n++)
        {
            long i = populated > 0 && n % 2 == 0 ? random.NextInt64(0, populated) : from + random.NextInt64(0, count);
            Check(Contains(view, i, vocabulary), "generated quad " + i + " is missing");
            checkedIn++;
        }

        for (int n = 0; n < 1_000; n++)
        {
            long i = from + count + random.NextInt64(0, 1_000_000);
            Check(!Contains(view, i, vocabulary), "quad " + i + ", never generated, is present");
            checkedOut++;
        }

        Console.WriteLine("checked: " + checkedIn + " generated quads present, " + checkedOut + " others absent");
    }

    private static bool Contains(DatasetView view, long i, long vocabulary)
    {
        byte[] text = new byte[400];
        int length = Write(text, i, vocabulary);
        bool found = false;
        NQuadsParser.Parse(text.AsSpan(0, length), (in QuadView quad) =>
        {
            TermHandle g = TermHandle.None;
            found = view.TryInternalise(quad.Subject.Materialise(), out TermHandle s)
                && view.TryInternalise(quad.Predicate.Materialise(), out TermHandle p)
                && view.TryInternalise(quad.Object.Materialise(), out TermHandle o)
                && (!quad.HasGraph || view.TryInternalise(quad.Graph.Materialise(), out g))
                && view.Contains(new Quad(s, p, o, g));
        }, new ParseOptions { Syntax = RdfSyntax.NQuads });
        return found;
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static long Size(string directory)
    {
        long total = 0;

        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(path).Length;
        }

        return total;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("bulk gate: " + message);
        }
    }
}
