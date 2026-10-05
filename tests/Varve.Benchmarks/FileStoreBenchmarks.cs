// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Win32.SafeHandles;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using StoreDatasetType = Varve.Store.Dataset;

namespace Varve.Benchmarks;

/// <summary>A file-backed dataset in a temporary directory, for the milestone 6a benchmarks.</summary>
internal sealed class FileDataset : IAsyncDisposable
{
    private FileDataset(string directory, FileStorage storage, StoreDatasetType dataset)
    {
        Directory = directory;
        Storage = storage;
        Dataset = dataset;
    }

    public string Directory { get; }

    public FileStorage Storage { get; }

    public StoreDatasetType Dataset { get; }

    internal static DatasetOptions Options(long memtableLimit) => new()
    {
        Clock = TimeProvider.System,
        MemtableLimit = new QuadCount(memtableLimit),
        Maintenance = MaintenanceMode.Off,
    };

    internal static async Task<FileDataset> CreateAsync(long memtableLimit = 1_000_000)
    {
        string directory = System.IO.Directory.CreateTempSubdirectory("varve-bench-").FullName;
        FileStorage storage = await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = TimeProvider.System });
        StoreDatasetType dataset = await StoreDatasetType.CreateAsync(storage, new DatasetId(Guid.NewGuid()), Options(memtableLimit));
        return new FileDataset(directory, storage, dataset);
    }

    /// <summary>Loads quads in commits of <paramref name="batch"/>, running maintenance after each, so the projection ends up on disk.</summary>
    internal async Task LoadAsync((RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads, int batch)
    {
        for (int start = 0; start < quads.Length; start += batch)
        {
            await Dataset.CommitAsync(StoreDataset.Request(quads, start, Math.Min(batch, quads.Length - start)));
            await Dataset.MaintainAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Dataset.DisposeAsync();
        await Storage.DisposeAsync();

        foreach (string file in System.IO.Directory.GetFiles(Directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        System.IO.Directory.Delete(Directory, recursive: true);
    }
}

/// <summary>Commit throughput on the file backend: the same three shapes as <see cref="CommitBenchmarks"/>, each commit flushed to the device.</summary>
[MemoryDiagnoser]
public class FileCommitBenchmarks
{
    private const int Loaded = 100_000;
    private (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] _quads = [];
    private CommitRequest _bulk = new();
    private CommitRequest[] _batches = [];
    private CommitRequest[] _singles = [];
    private FileDataset? _dataset;

    [GlobalSetup]
    public void Setup()
    {
        _quads = StoreDataset.Build(Loaded);
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] fresh = StoreDataset.Build(1_000, offset: StoreDataset.Quads);
        _bulk = StoreDataset.Request(_quads, 0, Loaded);
        _batches = [.. Enumerable.Range(0, 100).Select(b => StoreDataset.Request(_quads, b * 1_000, 1_000))];
        _singles = [.. Enumerable.Range(0, 1_000).Select(i => StoreDataset.Request(fresh, i, 1))];
    }

    [IterationSetup(Targets = [nameof(OneCommitOf100000), nameof(HundredCommitsOf1000)])]
    public void Empty() => _dataset = FileDataset.CreateAsync().GetAwaiter().GetResult();

    [IterationSetup(Target = nameof(ThousandCommitsOf1IntoLoaded))]
    public void Preloaded()
    {
        _dataset = FileDataset.CreateAsync().GetAwaiter().GetResult();
        _dataset.LoadAsync(_quads, 10_000).GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void Cleanup() => _dataset!.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>100,000 quads, one commit, into an empty dataset on disk.</summary>
    [Benchmark]
    public async Task<long> OneCommitOf100000() => (await _dataset!.Dataset.CommitAsync(_bulk)).Position.Value;

    /// <summary>100,000 quads as 100 commits of 1,000: 100 flushes.</summary>
    [Benchmark]
    public async Task<long> HundredCommitsOf1000()
    {
        long position = 0;

        foreach (CommitRequest batch in _batches)
        {
            position = (await _dataset!.Dataset.CommitAsync(batch)).Position.Value;
        }

        return position;
    }

    /// <summary>1,000 single-quad commits of new terms, into a dataset of 100,000: 1,000 flushes.</summary>
    [Benchmark]
    public async Task<long> ThousandCommitsOf1IntoLoaded()
    {
        long position = 0;

        foreach (CommitRequest single in _singles)
        {
            position = (await _dataset!.Dataset.CommitAsync(single)).Position.Value;
        }

        return position;
    }
}

/// <summary>
/// Scan throughput over a pinned read of 1,000,000 quads whose default
/// projection lives in disk runs, read a block at a time through the
/// synchronous blob read (ADR 0070, 0071): the same four scans as
/// <see cref="ScanBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class FileScanBenchmarks
{
    private FileDataset? _dataset;
    private DatasetView? _view;
    private TermHandle _predicate;
    private TermHandle[] _subjects = [];

    [GlobalSetup]
    public void Setup()
    {
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads = StoreDataset.Build(StoreDataset.Quads);
        _dataset = FileDataset.CreateAsync(memtableLimit: 10_000).GetAwaiter().GetResult();
        _dataset.LoadAsync(quads, 10_000).GetAwaiter().GetResult();
        _view = _dataset.Dataset.Pin();
        _view.TryInternalise(quads[3].P, out _predicate);
        _subjects = [.. Enumerable.Range(0, 10_000).Select(i => _view.TryInternalise(quads[i * 97].S, out TermHandle h) ? h : TermHandle.None)];

        if (Count(TermHandle.None, TermHandle.None, GraphPattern.Any) != StoreDataset.Quads)
        {
            throw new InvalidOperationException("The dataset does not hold " + StoreDataset.Quads + " quads.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _view?.Dispose();
        _dataset?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private long Count(TermHandle subject, TermHandle predicate, GraphPattern graph)
    {
        long count = 0;

        using IQuadCursor cursor = _view!.Match(subject, predicate, TermHandle.None, graph);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public long FullScan() => Count(TermHandle.None, TermHandle.None, GraphPattern.Any);

    [Benchmark]
    public long BoundPredicate() => Count(TermHandle.None, _predicate, GraphPattern.Any);

    [Benchmark]
    public long DefaultGraph() => Count(TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph);

    [Benchmark]
    public long TenThousandSubjectLookups()
    {
        long count = 0;

        foreach (TermHandle subject in _subjects)
        {
            count += Count(subject, TermHandle.None, GraphPattern.Any);
        }

        return count;
    }
}

/// <summary>
/// The read primitive ADR 0071 leaves to a benchmark: a 4 KiB block — one
/// block of 128 keys — read by <c>RandomAccess.Read</c> on a held handle,
/// against a copy out of a memory-mapped view, sequentially through 64 MiB and
/// at 10,000 random blocks. The file is in the page cache in both, as a run
/// being scanned is.
/// </summary>
public class BlockReadBenchmarks
{
    private const int Block = 4096;
    private const long Size = 64L << 20;
    private readonly byte[] _buffer = new byte[Block];
    private string _path = string.Empty;
    private SafeFileHandle? _handle;
    private MemoryMappedFile? _mapped;
    private MemoryMappedViewAccessor? _view;
    private long[] _random = [];

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "varve-block-read.bin");
        byte[] data = new byte[Size];
        new Random(6).NextBytes(data);
        File.WriteAllBytes(_path, data);
        _handle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _mapped = MemoryMappedFile.CreateFromFile(_path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _mapped.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        Random random = new(7);
        _random = [.. Enumerable.Range(0, 10_000).Select(_ => (long)random.Next((int)(Size / Block)) * Block)];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _view?.Dispose();
        _mapped?.Dispose();
        _handle?.Dispose();
        File.Delete(_path);
    }

    [Benchmark(Baseline = true)]
    public long SequentialRandomAccess()
    {
        long sum = 0;

        for (long at = 0; at < Size; at += Block)
        {
            sum += RandomAccess.Read(_handle!, _buffer, at);
        }

        return sum;
    }

    [Benchmark]
    public unsafe long SequentialMapped()
    {
        long sum = 0;
        byte* pointer = null;
        _view!.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

        try
        {
            for (long at = 0; at < Size; at += Block)
            {
                new ReadOnlySpan<byte>(pointer + at, Block).CopyTo(_buffer);
                sum += Block;
            }
        }
        finally
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        return sum;
    }

    [Benchmark]
    public long RandomBlocksRandomAccess()
    {
        long sum = 0;

        foreach (long at in _random)
        {
            sum += RandomAccess.Read(_handle!, _buffer, at);
        }

        return sum;
    }

    [Benchmark]
    public unsafe long RandomBlocksMapped()
    {
        long sum = 0;
        byte* pointer = null;
        _view!.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

        try
        {
            foreach (long at in _random)
            {
                new ReadOnlySpan<byte>(pointer + at, Block).CopyTo(_buffer);
                sum += Block;
            }
        }
        finally
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        return sum;
    }
}

/// <summary>
/// Sizes on disk for 1,000,000 quads, and ADR 0012's locality hypothesis
/// measured on the runs: how small the sorted keys of a run become when each
/// key is written as the difference from the one before, with the counter ids
/// the store allocates, against the same keys after the ids are scattered as
/// content-derived ids would be.
/// </summary>
internal static class FileSizes
{
    internal static async Task PrintAsync()
    {
        (RdfTerm S, RdfTerm P, RdfTerm O, RdfTerm? G)[] quads = StoreDataset.Build(StoreDataset.Quads);
        await using FileDataset dataset = await FileDataset.CreateAsync(memtableLimit: 10_000);
        Stopwatch load = Stopwatch.StartNew();
        await dataset.LoadAsync(quads, 10_000);
        load.Stop();
        await dataset.Dataset.CheckpointAsync(dataset.Dataset.Head);

        long log = Bytes(Path.Combine(dataset.Directory, "log"));
        string[] runs = Directory.GetFiles(Path.Combine(dataset.Directory, "derived", "index", "runs"));
        long runBytes = runs.Sum(f => new FileInfo(f).Length);
        long checkpoint = Bytes(Path.Combine(dataset.Directory, "derived", "checkpoints"));

        Print("quads", StoreDataset.Quads);
        Print("load, 100 commits of 10,000 with maintenance, ms", load.ElapsedMilliseconds);
        Print("log bytes", log);
        Print("log bytes per quad", log / (double)StoreDataset.Quads);
        Print("disk runs", runs.Length);
        Print("disk run bytes", runBytes);
        Print("disk run bytes per quad", runBytes / (double)StoreDataset.Quads);
        Print("checkpoint bytes (keys and dictionary)", checkpoint);
        Print("checkpoint bytes per quad", checkpoint / (double)StoreDataset.Quads);
        Print("fences held in memory per quad (asserted, six orders)", 6 * 32.0 / 128);

        string largest = runs.OrderByDescending(f => new FileInfo(f).Length).First();
        (ulong[] Keys, long Count)[] sections = ReadSections(largest);

        for (int order = 0; order < 6; order++)
        {
            ulong[] keys = sections[order * 2].Keys;
            Print("order " + order + ": keys", sections[order * 2].Count);
            Print("order " + order + ": delta-encoded bytes per key, counter ids", Delta(keys));
            Print("order " + order + ": delta-encoded bytes per key, scattered ids", Delta(Scatter(keys, order)));
        }
    }

    private static long Bytes(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;

    private static void Print(string what, double value) =>
        Console.WriteLine(what + ": " + value.ToString("N2", CultureInfo.InvariantCulture));

    // A run file's twelve sections, as storage-format.md §7 lays them out.
    private static (ulong[] Keys, long Count)[] ReadSections(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> header = bytes.AsSpan(bytes.Length - 160);
        long directory = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);
        (ulong[] Keys, long Count)[] sections = new (ulong[], long)[12];

        for (int section = 0; section < 12; section++)
        {
            long offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)directory + 8 + (section * 16)));
            long count = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)directory + 16 + (section * 16)));
            ulong[] keys = new ulong[count * 4];

            for (long i = 0; i < keys.Length; i++)
            {
                keys[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan((int)(offset + (i * 8))));
            }

            sections[section] = (keys, count);
        }

        return sections;
    }

    // Each key as: the number of leading components equal to the previous
    // key's, the varint difference of the first that is not, and the rest as
    // varints. Bytes per key.
    private static double Delta(ulong[] keys)
    {
        long bytes = 0;
        long count = keys.Length / 4;

        for (long k = 0; k < count; k++)
        {
            int shared = 0;

            while (k > 0 && shared < 4 && keys[(k * 4) + shared] == keys[((k - 1) * 4) + shared])
            {
                shared++;
            }

            bytes += 1;

            for (int c = shared; c < 4; c++)
            {
                ulong value = keys[(k * 4) + c];
                ulong previous = c == shared && k > 0 ? keys[((k - 1) * 4) + c] : 0;
                bytes += Varint(value - previous);
            }
        }

        return bytes / (double)count;
    }

    private static int Varint(ulong value)
    {
        int length = 1;

        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }

        return length;
    }

    // The same keys with every id replaced by a pseudo-random 64-bit value
    // (a fixed bijection), then sorted again: what content-derived ids would
    // do to the same data. The graph id 0 stays 0.
    private static ulong[] Scatter(ulong[] keys, int order)
    {
        long count = keys.Length / 4;
        ulong[][] scattered = new ulong[count][];

        for (long k = 0; k < count; k++)
        {
            scattered[k] = [Mix(keys[k * 4]), Mix(keys[(k * 4) + 1]), Mix(keys[(k * 4) + 2]), Mix(keys[(k * 4) + 3])];
        }

        Array.Sort(scattered, (a, b) =>
        {
            for (int c = 0; c < 4; c++)
            {
                int compared = a[c].CompareTo(b[c]);

                if (compared != 0)
                {
                    return compared;
                }
            }

            return 0;
        });

        _ = order;
        return [.. scattered.SelectMany(k => k)];
    }

    private static ulong Mix(ulong id)
    {
        if (id == 0)
        {
            return 0;
        }

        ulong z = id + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
