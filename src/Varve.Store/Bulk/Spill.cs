// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>An order a record knows itself: the comparison a hot path may call (VARVE0003).</summary>
internal interface IOrdered<T>
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    int CompareTo(T other);
}

/// <summary>Calls <see cref="IOrdered{T}.CompareTo"/> without boxing.</summary>
internal static class Ordered
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Compare<T>(in T left, in T right)
        where T : IOrdered<T> => left.CompareTo(right);
}

/// <summary>Where a bulk load keeps its temporary blobs, and their names (ADR 0081).</summary>
internal sealed class SpillSpace
{
    internal const string Prefix = "bulk/";

    private readonly string _root;
    private long _next;

    internal SpillSpace(IDerivedStore store, string load)
    {
        Store = store;
        _root = Prefix + load + "/";
    }

    internal IDerivedStore Store { get; }

    internal List<BlobName> Created { get; } = [];

    internal BlobName Next(string kind)
    {
        BlobName name = new(_root + kind + "." + Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture));
        Created.Add(name);
        return name;
    }

    /// <summary>Deletes one blob as soon as nothing needs it, so the load's disk stays what one pass needs.</summary>
    internal async ValueTask DeleteAsync(BlobName name, CancellationToken cancellationToken)
    {
        await Store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        Created.Remove(name);
    }

    /// <summary>Deletes every blob the load made; also what open does to a crashed load's.</summary>
    internal async ValueTask DeleteAllAsync(CancellationToken cancellationToken)
    {
        foreach (BlobName name in Created)
        {
            await Store.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }

        Created.Clear();
    }
}

/// <summary>
/// Fixed-size records sorted outside memory: a buffer of them sorted and
/// written as a run when it fills, and the runs merged by a heap, at most
/// <see cref="FanIn"/> at a time — more are merged in passes first — so what
/// it holds is the buffer, or one read buffer per run merged.
/// </summary>
internal sealed class ExternalSort<T>
    where T : unmanaged, IComparable<T>, IOrdered<T>
{
    internal const int FanIn = 64;
    internal const int ReadBuffer = 1 << 16;

    private readonly SpillSpace _space;
    private readonly string _kind;
    private readonly List<BlobName> _runs = [];
    private List<BlobName>? _final;
    private (int Start, int End)[] _parts = [];
    private T[] _buffer;
    private int _count;

    internal ExternalSort(SpillSpace space, string kind, int bufferRecords)
    {
        _space = space;
        _kind = kind;
        _buffer = new T[Math.Max(1, bufferRecords)];
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Total { get; private set; }

    /// <summary>Adds a record; true when the buffer is full and must be spilled before the next.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Add(in T record)
    {
        _buffer[_count++] = record;
        Total++;
        return _count == _buffer.Length;
    }

    /// <summary>Sorts the buffer and writes it as a run.</summary>
    internal async ValueTask SpillAsync(CancellationToken cancellationToken)
    {
        if (_count == 0)
        {
            return;
        }

        foreach ((int start, int end) in SortInParts(_buffer, _count))
        {
            BlobName name = _space.Next(_kind);
            await WriteAsync(_space.Store, name, _buffer.AsMemory(start, end - start), cancellationToken).ConfigureAwait(false);
            _runs.Add(name);
        }

        _count = 0;
    }

    /// <summary>
    /// Spills what is left, frees the buffer, and returns the records in order.
    /// The caller disposes the reader, which deletes nothing: the load's space does.
    /// </summary>
    internal async ValueTask<SortedReader<T>> FinishAsync(CancellationToken cancellationToken)
    {
        // Everything in one buffer: read in place, nothing spilled.
        if (_runs.Count == 0)
        {
            _parts = SortInParts(_buffer, _count);
            _final = [];
            return SortedReader<T>.Over(_buffer, _parts);
        }

        await SpillAsync(cancellationToken).ConfigureAwait(false);
        _buffer = [];

        List<BlobName> runs = _runs;

        while (runs.Count > FanIn)
        {
            List<BlobName> next = [];

            for (int start = 0; start < runs.Count; start += FanIn)
            {
                List<BlobName> group = runs.GetRange(start, Math.Min(FanIn, runs.Count - start));
                using SortedReader<T> merged = await SortedReader<T>.OpenAsync(_space.Store, group, cancellationToken).ConfigureAwait(false);
                BlobName name = _space.Next(_kind);
                await WriteAsync(_space.Store, name, merged, cancellationToken).ConfigureAwait(false);
                next.Add(name);

                foreach (BlobName done in group)
                {
                    await _space.DeleteAsync(done, cancellationToken).ConfigureAwait(false);
                }
            }

            runs = next;
        }

        _final = runs;
        return await SortedReader<T>.OpenAsync(_space.Store, runs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>After <see cref="FinishAsync"/>: the records in order again, from the start.</summary>
    internal ValueTask<SortedReader<T>> ReopenAsync(CancellationToken cancellationToken) =>
        _final is null ? throw new InvalidOperationException("Not finished.")
        : _final.Count == 0 ? new ValueTask<SortedReader<T>>(SortedReader<T>.Over(_buffer, _parts))
        : SortedReader<T>.OpenAsync(_space.Store, _final, cancellationToken);

    /// <summary>
    /// Sorts the first <paramref name="count"/> records in as many parts as
    /// there are processors, the parts at once; returns the parts, each a
    /// sorted run for the merge.
    /// </summary>
    private static (int Start, int End)[] SortInParts(T[] records, int count)
    {
        int parts = count < (1 << 16) ? 1 : Math.Min(Environment.ProcessorCount, 16);
        (int Start, int End)[] ranges = new (int, int)[parts];

        for (int i = 0; i < parts; i++)
        {
            ranges[i] = ((int)((long)count * i / parts), (int)((long)count * (i + 1) / parts));
        }

        Parallel.For(0, parts, i => records.AsSpan(ranges[i].Start, ranges[i].End - ranges[i].Start).Sort());
        return ranges;
    }

    /// <summary>Drops the records, the buffer and every run spilled, read or not.</summary>
    internal async ValueTask DropAsync(CancellationToken cancellationToken)
    {
        _buffer = [];
        _count = 0;

        foreach (BlobName run in (IEnumerable<BlobName>)[.. _runs, .. _final ?? []])
        {
            await _space.DeleteAsync(run, cancellationToken).ConfigureAwait(false);
        }

        _runs.Clear();
        _final = [];
    }

    internal static async ValueTask WriteAsync(IDerivedStore store, BlobName name, ReadOnlyMemory<T> records, CancellationToken cancellationToken)
    {
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        int chunk = ReadBuffer / Marshal.SizeOf<T>();

        for (int at = 0; at < records.Length; at += chunk)
        {
            ReadOnlyMemory<T> part = records.Slice(at, Math.Min(chunk, records.Length - at));
            await writer.WriteAsync(MemoryMarshal.AsBytes(part.Span).ToArray(), cancellationToken).ConfigureAwait(false);
        }

        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteAsync(IDerivedStore store, BlobName name, SortedReader<T> records, CancellationToken cancellationToken)
    {
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        T[] buffer = new T[ReadBuffer / Marshal.SizeOf<T>()];
        byte[] bytes = new byte[buffer.Length * Marshal.SizeOf<T>()];
        int filled;

        while ((filled = records.Read(buffer)) > 0)
        {
            MemoryMarshal.AsBytes(buffer.AsSpan(0, filled)).CopyTo(bytes);
            await writer.WriteAsync(bytes.AsMemory(0, filled * Marshal.SizeOf<T>()), cancellationToken).ConfigureAwait(false);
        }

        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Sorted runs of fixed-size records merged into one stream by a heap, read synchronously.</summary>
internal sealed class SortedReader<T> : IDisposable
    where T : unmanaged, IComparable<T>, IOrdered<T>
{
    private readonly IReadableBlob[] _blobs;
    private readonly T[][] _buffers;
    private readonly long[] _read;
    private readonly int[] _at;
    private readonly int[] _filled;
    private readonly int[] _heap;
    private readonly long[] _records;
    private readonly int _size = Marshal.SizeOf<T>();
    private int _heapCount;

    /// <summary>A reader over parts of one array, each already sorted, merged.</summary>
    internal static SortedReader<T> Over(T[] records, (int Start, int End)[] parts)
    {
        SortedReader<T> reader = new([], parts.Length);

        for (int i = 0; i < parts.Length; i++)
        {
            reader._buffers[i] = records;
            reader._at[i] = parts[i].Start;
            reader._filled[i] = parts[i].End;

            if (parts[i].End > parts[i].Start)
            {
                reader.Push(i);
            }
        }

        return reader;
    }

    private SortedReader(IReadableBlob[] blobs, int memoryRuns = 0)
    {
        _blobs = blobs;
        int slots = Math.Max(Math.Max(1, memoryRuns), blobs.Length);
        _records = new long[slots];

        for (int i = 0; i < blobs.Length; i++)
        {
            _records[i] = blobs[i].Length.Value / _size;
        }

        _buffers = new T[slots][];
        _read = new long[slots];
        _at = new int[slots];
        _filled = new int[slots];
        _heap = new int[slots];

        for (int i = 0; i < blobs.Length; i++)
        {
            _buffers[i] = new T[ExternalSort<T>.ReadBuffer / Marshal.SizeOf<T>()];

            if (Refill(i))
            {
                Push(i);
            }
        }
    }

    internal static async ValueTask<SortedReader<T>> OpenAsync(IDerivedStore store, IReadOnlyList<BlobName> runs, CancellationToken cancellationToken)
    {
        IReadableBlob[] blobs = new IReadableBlob[runs.Count];

        for (int i = 0; i < runs.Count; i++)
        {
            blobs[i] = await store.OpenAsync(runs[i], cancellationToken).ConfigureAwait(false);
        }

        return new SortedReader<T>(blobs);
    }

    /// <summary>The next record, or false at the end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryNext(out T record)
    {
        if (_heapCount == 0)
        {
            record = default;
            return false;
        }

        int run = _heap[0];
        record = _buffers[run][_at[run]++];

        if (_at[run] < _filled[run] || Refill(run))
        {
            SiftDown(0);
        }
        else
        {
            _heap[0] = _heap[--_heapCount];
            SiftDown(0);
        }

        return true;
    }

    /// <summary>Fills a buffer with the next records; returns how many.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int Read(Span<T> buffer)
    {
        int count = 0;

        while (count < buffer.Length && TryNext(out T record))
        {
            buffer[count++] = record;
        }

        return count;
    }

    public void Dispose()
    {
        foreach (IReadableBlob blob in _blobs)
        {
            blob.Dispose();
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Refill(int run)
    {
        if (_blobs.Length == 0)
        {
            return false;
        }

        int size = _size;
        long remaining = _records[run] - _read[run];

        if (remaining <= 0)
        {
            return false;
        }

        int count = (int)Math.Min(_buffers[run].Length, remaining);
        Span<byte> bytes = MemoryMarshal.AsBytes(_buffers[run].AsSpan(0, count));

        if (_blobs[run].Read(new ByteOffset(_read[run] * size), bytes) != bytes.Length)
        {
            throw new IOException("A bulk load's spill is shorter than was written.");
        }

        _read[run] += count;
        _at[run] = 0;
        _filled[run] = count;
        return true;
    }

    private void Push(int run)
    {
        _heap[_heapCount] = run;
        int i = _heapCount++;

        while (i > 0)
        {
            int parent = (i - 1) / 2;

            if (Less(_heap[parent], _heap[i]))
            {
                break;
            }

            (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
            i = parent;
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void SiftDown(int i)
    {
        while (true)
        {
            int left = (2 * i) + 1;
            int smallest = i;

            if (left < _heapCount && Less(_heap[left], _heap[smallest]))
            {
                smallest = left;
            }

            if (left + 1 < _heapCount && Less(_heap[left + 1], _heap[smallest]))
            {
                smallest = left + 1;
            }

            if (smallest == i)
            {
                return;
            }

            (_heap[smallest], _heap[i]) = (_heap[i], _heap[smallest]);
            i = smallest;
        }
    }

    // Ties broken by run index, so equal records come out in the order their runs were written.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Less(int a, int b)
    {
        int c = Ordered.Compare(in _buffers[a][_at[a]], in _buffers[b][_at[b]]);
        return c < 0 || (c == 0 && a < b);
    }
}

/// <summary>Fixed-size records written to a blob in order, a buffer at a time.</summary>
internal sealed class RecordWriter<T> : IAsyncDisposable
    where T : unmanaged
{
    private readonly IBlobWriter _writer;
    private readonly T[] _buffer = new T[ExternalSort<QuadKey>.ReadBuffer / Marshal.SizeOf<T>()];
    private readonly byte[] _bytes = new byte[ExternalSort<QuadKey>.ReadBuffer];
    private int _count;

    private RecordWriter(IBlobWriter writer) => _writer = writer;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Written { get; private set; }

    internal static async ValueTask<RecordWriter<T>> CreateAsync(IDerivedStore store, BlobName name, CancellationToken cancellationToken) =>
        new(await store.CreateAsync(name, cancellationToken).ConfigureAwait(false));

    /// <summary>Adds a record; true when the buffer is full and must be written before the next.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Add(in T record)
    {
        _buffer[_count++] = record;
        Written++;
        return _count == _buffer.Length;
    }

    internal async ValueTask WriteBufferAsync(CancellationToken cancellationToken)
    {
        int length = _count * Marshal.SizeOf<T>();
        MemoryMarshal.AsBytes(_buffer.AsSpan(0, _count)).CopyTo(_bytes);
        await _writer.WriteAsync(_bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        _count = 0;
    }

    internal async ValueTask PublishAsync(CancellationToken cancellationToken)
    {
        await WriteBufferAsync(cancellationToken).ConfigureAwait(false);
        await _writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

/// <summary>A blob of fixed-size records read in order, a buffer at a time.</summary>
internal sealed class RecordReader<T> : IDisposable
    where T : unmanaged
{
    private readonly IReadableBlob _blob;
    private readonly T[] _buffer = new T[ExternalSort<QuadKey>.ReadBuffer / Marshal.SizeOf<T>()];
    private long _read;
    private int _at;
    private int _filled;

    internal RecordReader(IReadableBlob blob)
    {
        _blob = blob;
        Count = blob.Length.Value / Marshal.SizeOf<T>();
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Count { get; }

    internal static async ValueTask<RecordReader<T>> OpenAsync(IDerivedStore store, BlobName name, CancellationToken cancellationToken) =>
        new(await store.OpenAsync(name, cancellationToken).ConfigureAwait(false));

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryNext(out T record)
    {
        if (_at == _filled)
        {
            long remaining = Count - _read;

            if (remaining <= 0)
            {
                record = default;
                return false;
            }

            int count = (int)Math.Min(_buffer.Length, remaining);
            Span<byte> bytes = MemoryMarshal.AsBytes(_buffer.AsSpan(0, count));

            if (_blob.Read(new ByteOffset(_read * (bytes.Length / count)), bytes) != bytes.Length)
            {
                throw new IOException("A bulk load's spill is shorter than was written.");
            }

            _read += count;
            _at = 0;
            _filled = count;
        }

        record = _buffer[_at++];
        return true;
    }

    public void Dispose() => _blob.Dispose();
}

/// <summary>A section's keys for a run being written, from a sorted spill.</summary>
internal sealed class SpillKeySource : IKeySource, IDisposable
{
    private readonly SortedReader<QuadKey>? _sorted;
    private readonly RecordReader<QuadKey>? _records;

    internal SpillKeySource(SortedReader<QuadKey> sorted) => _sorted = sorted;

    internal SpillKeySource(RecordReader<QuadKey> records) => _records = records;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<QuadKey> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length && (_sorted is not null ? _sorted.TryNext(out QuadKey key) : _records!.TryNext(out key)))
        {
            buffer[produced++] = key;
        }

        return produced;
    }

    public void Dispose()
    {
        _sorted?.Dispose();
        _records?.Dispose();
    }
}
