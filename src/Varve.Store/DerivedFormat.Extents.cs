// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// An event of a quad in an extent set's window: asserted or retracted at a
/// position, keyed in SPOG (ADR 0138). Sorted by key, then position.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExtentEvent : IComparable<ExtentEvent>, IOrdered<ExtentEvent>
{
    internal ExtentEvent(QuadKey key, long position, bool asserted)
    {
        Key = key;
        Position = position;
        Asserted = asserted ? 1 : 0;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal QuadKey Key { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Position { get; }

    /// <summary>1 asserted, 0 retracted.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Asserted { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(ExtentEvent other)
    {
        int key = Key.CompareTo(other.Key);
        return key != 0 ? key : Position.CompareTo(other.Position);
    }
}

/// <summary>Which part of an extent a row is written to (ADR 0138).</summary>
internal static class ExtentSection
{
    /// <summary>Asserted in the window and still asserted at its end, where it was not at its start: the asserted section.</summary>
    internal const long Open = 0;

    /// <summary>Asserted at the window's start and retracted in it, not asserted at its end: the retracted section.</summary>
    internal const long Close = 1;

    /// <summary>Every other interval: inside the window, or of a quad asserted at both its ends.</summary>
    internal const long Within = 2;
}

/// <summary>
/// One interval of a quad in an extent set's window (ADR 0138): a key in the
/// extent's order, the position that asserted it and the position that
/// retracted it. <see cref="From"/> is 0 when the quad was asserted at the
/// window's start, <see cref="To"/> 0 when it is still asserted at its end; a
/// quad asserted throughout has no row. Sorted by key, then from, then to.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExtentRow : IComparable<ExtentRow>, IOrdered<ExtentRow>
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ExtentRow(QuadKey key, long from, long to, long section)
    {
        Key = key;
        From = from;
        To = to;
        Section = section;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal QuadKey Key { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long From { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long To { get; }

    /// <summary>One of <see cref="ExtentSection"/>'s values, once the rows of its key are classified.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Section { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ExtentRow In(long section) => new(Key, From, To, section);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(ExtentRow other)
    {
        int key = Key.CompareTo(other.Key);

        if (key != 0)
        {
            return key;
        }

        return From != other.From ? From.CompareTo(other.From) : To.CompareTo(other.To);
    }
}

/// <summary>Rows in their extent's order, a buffer at a time.</summary>
internal interface IRowSource : IDisposable
{
    /// <summary>Fills the buffer with the next rows; returns how many, zero at the end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    int Next(Span<ExtentRow> buffer);
}

/// <summary>A row source read a row at a time.</summary>
internal abstract class RowPipe : IRowSource
{
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal abstract bool TryNext(out ExtentRow row);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<ExtentRow> buffer)
    {
        int produced = 0;

        while (produced < buffer.Length && TryNext(out ExtentRow row))
        {
            buffer[produced++] = row;
        }

        return produced;
    }

    public abstract void Dispose();
}

/// <summary>Another source's rows a row at a time, through a buffer.</summary>
internal sealed class RowInput(IRowSource rows) : IDisposable
{
    private readonly ExtentRow[] _buffer = new ExtentRow[256];
    private int _at;
    private int _filled;
    private bool _ended;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryNext(out ExtentRow row)
    {
        if (_at == _filled)
        {
            if (_ended)
            {
                row = default;
                return false;
            }

            _filled = rows.Next(_buffer);
            _at = 0;

            if (_filled == 0)
            {
                _ended = true;
                row = default;
                return false;
            }
        }

        row = _buffer[_at++];
        return true;
    }

    public void Dispose() => rows.Dispose();
}

/// <summary>A source's rows a key at a time, in a buffer from the shared pool.</summary>
internal sealed class KeyGroups(IRowSource rows) : IDisposable
{
    private readonly RowInput _input = new(rows);
    private ExtentRow[] _group = ArrayPool<ExtentRow>.Shared.Rent(16);
    private bool _gathered;
    private int _count;
    private bool _held;
    private ExtentRow _next;

    /// <summary>The next group's key, gathering it if it is not; false at the end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Peek(out QuadKey key)
    {
        if (!_gathered)
        {
            Gather();
        }

        key = _count > 0 ? _group[0].Key : default;
        return _count > 0;
    }

    /// <summary>The gathered group, valid until the next <see cref="Peek"/>.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal Span<ExtentRow> Take()
    {
        _gathered = false;
        return _group.AsSpan(0, _count);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Gather()
    {
        _gathered = true;
        _count = 0;

        if (!_held && !_input.TryNext(out _next))
        {
            return;
        }

        _held = false;
        QuadKey key = _next.Key;
        Add(_next);

        while (_input.TryNext(out ExtentRow row))
        {
            if (!row.Key.Equals(key))
            {
                _next = row;
                _held = true;
                return;
            }

            Add(row);
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Add(in ExtentRow row)
    {
        if (_count == _group.Length)
        {
            ExtentRow[] larger = ArrayPool<ExtentRow>.Shared.Rent(_group.Length * 2);
            _group.AsSpan(0, _count).CopyTo(larger);
            ArrayPool<ExtentRow>.Shared.Return(_group);
            _group = larger;
        }

        _group[_count++] = row;
    }

    public void Dispose()
    {
        _input.Dispose();

        if (_group.Length > 0)
        {
            ArrayPool<ExtentRow>.Shared.Return(_group);
            _group = [];
        }
    }
}

/// <summary>Rows from sorted records: a blob of them, or an external sort's output.</summary>
internal sealed class RecordRowSource : RowPipe
{
    private readonly RecordReader<ExtentRow>? _records;
    private readonly SortedReader<ExtentRow>? _sorted;

    internal RecordRowSource(RecordReader<ExtentRow> records) => _records = records;

    internal RecordRowSource(SortedReader<ExtentRow> sorted) => _sorted = sorted;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row) =>
        _records is not null ? _records.TryNext(out row) : _sorted!.TryNext(out row);

    public override void Dispose()
    {
        _records?.Dispose();
        _sorted?.Dispose();
    }
}

/// <summary>The rows of one section, as their source classified them.</summary>
internal sealed class SectionRows(IRowSource rows, long section) : RowPipe
{
    private readonly RowInput _input = new(rows);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row)
    {
        while (_input.TryNext(out row))
        {
            if (row.Section == section)
            {
                return true;
            }
        }

        return false;
    }

    public override void Dispose() => _input.Dispose();
}

/// <summary>
/// Rows given their section a key at a time (ADR 0138): a quad asserted at
/// both ends of the window has every row within; otherwise its row from the
/// window's start is its closing row, its row to the window's end its open
/// row, and the rest are within.
/// </summary>
internal sealed class ClassifiedRows(IRowSource rows) : RowPipe
{
    private readonly KeyGroups _groups = new(rows);
    private ExtentRow[] _group = [];
    private int _count;
    private int _at;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row)
    {
        if (_at == _count)
        {
            if (!_groups.Peek(out _))
            {
                row = default;
                return false;
            }

            Span<ExtentRow> group = _groups.Take();
            Classify(group);
            Hold(group);
        }

        row = _group[_at++];
        return true;
    }

    /// <summary>Gives each of one key's rows its section, in place.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void Classify(Span<ExtentRow> group)
    {
        bool start = false;
        bool end = false;

        foreach (ExtentRow r in group)
        {
            start |= r.From == 0;
            end |= r.To == 0;
        }

        for (int i = 0; i < group.Length; i++)
        {
            ExtentRow r = group[i];
            group[i] = r.In(start && end ? ExtentSection.Within
                : r.From == 0 ? ExtentSection.Close
                : r.To == 0 ? ExtentSection.Open
                : ExtentSection.Within);
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Hold(Span<ExtentRow> group)
    {
        if (_group.Length < group.Length)
        {
            if (_group.Length > 0)
            {
                ArrayPool<ExtentRow>.Shared.Return(_group);
            }

            _group = ArrayPool<ExtentRow>.Shared.Rent(Math.Max(16, group.Length));
        }

        group.CopyTo(_group);
        _count = group.Length;
        _at = 0;
    }

    public override void Dispose()
    {
        _groups.Dispose();

        if (_group.Length > 0)
        {
            ArrayPool<ExtentRow>.Shared.Return(_group);
            _group = [];
        }
    }
}

/// <summary>
/// An extent's rows narrowed to a window inside its own, <c>(after, upTo]</c>
/// (ADR 0138): a position outside it becomes the window's boundary, and an
/// interval that does not meet it, or spans it, has no row. Unclassified.
/// </summary>
internal sealed class RestrictedRows(IRowSource rows, long after, long upTo) : RowPipe
{
    private readonly RowInput _input = new(rows);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row)
    {
        while (_input.TryNext(out ExtentRow r))
        {
            // Asserted after the window, or retracted before it reaches it.
            if (r.From > upTo || (r.To != 0 && r.To <= after))
            {
                continue;
            }

            long from = r.From > after ? r.From : 0;
            long to = r.To != 0 && r.To <= upTo ? r.To : 0;

            if (from == 0 && to == 0)
            {
                continue;
            }

            row = new ExtentRow(r.Key, from, to, ExtentSection.Within);
            return true;
        }

        row = default;
        return false;
    }

    public override void Dispose() => _input.Dispose();
}

/// <summary>
/// The rows of two adjacent windows, <c>(a, b]</c> then <c>(b, c]</c>, as the
/// rows of <c>(a, c]</c> (ADR 0138): the older window's row to its end and the
/// newer's row from its start are one interval. Unclassified.
/// </summary>
internal sealed class AdjacentRows(IRowSource older, IRowSource newer) : RowPipe
{
    private readonly KeyGroups _older = new(older);
    private readonly KeyGroups _newer = new(newer);
    private ExtentRow[] _out = ArrayPool<ExtentRow>.Shared.Rent(16);
    private int _count;
    private int _at;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row)
    {
        if (_at == _count && !Combine())
        {
            row = default;
            return false;
        }

        row = _out[_at++];
        return true;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Combine()
    {
        _count = 0;
        _at = 0;
        bool hasOlder = _older.Peek(out QuadKey olderKey);
        bool hasNewer = _newer.Peek(out QuadKey newerKey);

        if (!hasOlder && !hasNewer)
        {
            return false;
        }

        int c = !hasOlder ? 1 : !hasNewer ? -1 : olderKey.CompareTo(newerKey);
        QuadKey key = c <= 0 ? olderKey : newerKey;
        long openFrom = 0;
        long closeTo = 0;

        if (c <= 0)
        {
            foreach (ExtentRow r in _older.Take())
            {
                if (r.To == 0)
                {
                    openFrom = r.From;
                }
                else
                {
                    Add(r);
                }
            }
        }

        if (c >= 0)
        {
            foreach (ExtentRow r in _newer.Take())
            {
                if (r.From == 0)
                {
                    closeTo = r.To;
                }
                else
                {
                    Add(r);
                }
            }
        }

        if (openFrom != 0 || closeTo != 0)
        {
            Add(new ExtentRow(key, openFrom, closeTo, ExtentSection.Within));
        }

        _out.AsSpan(0, _count).Sort();
        return true;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Add(in ExtentRow row)
    {
        if (_count == _out.Length)
        {
            ExtentRow[] larger = ArrayPool<ExtentRow>.Shared.Rent(_out.Length * 2);
            _out.AsSpan(0, _count).CopyTo(larger);
            ArrayPool<ExtentRow>.Shared.Return(_out);
            _out = larger;
        }

        _out[_count++] = row.In(ExtentSection.Within);
    }

    public override void Dispose()
    {
        _older.Dispose();
        _newer.Dispose();

        if (_out.Length > 0)
        {
            ArrayPool<ExtentRow>.Shared.Return(_out);
            _out = [];
        }
    }
}

/// <summary>
/// Every row of a loaded extent, classified, in order: its open rows from the
/// asserted section and their positions, its closing rows from the retracted
/// section and theirs, and the rows within, merged.
/// </summary>
internal sealed class ExtentRows : RowPipe
{
    private readonly SectionReader _openKeys;
    private readonly SectionReader _closeKeys;
    private readonly PositionReader _openPositions;
    private readonly PositionReader _closePositions;
    private readonly WithinReader _within;
    private bool _hasOpen;
    private bool _hasClose;
    private bool _hasWithin;
    private ExtentRow _open;
    private ExtentRow _close;
    private ExtentRow _inside;

    internal ExtentRows(LoadedRun extent)
    {
        ExtentInfo info = extent.Extent!;
        _openKeys = new SectionReader(extent.Run.Asserted(info.Order));
        _closeKeys = new SectionReader(extent.Run.Retracted(info.Order));
        _openPositions = info.OpenPositions();
        _closePositions = info.ClosePositions();
        _within = info.Within();
        _hasOpen = NextOpen();
        _hasClose = NextClose();
        _hasWithin = _within.TryNext(out _inside);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal override bool TryNext(out ExtentRow row)
    {
        int pick = -1;
        row = default;

        if (_hasOpen)
        {
            pick = 0;
            row = _open;
        }

        if (_hasClose && (pick < 0 || _close.CompareTo(row) < 0))
        {
            pick = 1;
            row = _close;
        }

        if (_hasWithin && (pick < 0 || _inside.CompareTo(row) < 0))
        {
            pick = 2;
            row = _inside;
        }

        if (pick == 0)
        {
            _hasOpen = NextOpen();
        }
        else if (pick == 1)
        {
            _hasClose = NextClose();
        }
        else if (pick == 2)
        {
            _hasWithin = _within.TryNext(out _inside);
        }

        return pick >= 0;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool NextOpen()
    {
        if (!_openKeys.HasCurrent)
        {
            return false;
        }

        QuadKey key = _openKeys.Current();
        _openKeys.Advance();
        _open = new ExtentRow(key, _openPositions.Next(), 0, ExtentSection.Open);
        return true;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool NextClose()
    {
        if (!_closeKeys.HasCurrent)
        {
            return false;
        }

        QuadKey key = _closeKeys.Current();
        _closeKeys.Advance();
        _close = new ExtentRow(key, 0, _closePositions.Next(), ExtentSection.Close);
        return true;
    }

    public override void Dispose()
    {
        _openKeys.Release();
        _closeKeys.Release();
        _within.Release();
    }
}

/// <summary>The keys of rows, for a run's section.</summary>
internal sealed class RowKeySource(IRowSource rows) : IKeySource, IDisposable
{
    private readonly ExtentRow[] _buffer = new ExtentRow[256];

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int Next(Span<QuadKey> buffer)
    {
        int count = rows.Next(_buffer.AsSpan(0, Math.Min(_buffer.Length, buffer.Length)));

        for (int i = 0; i < count; i++)
        {
            buffer[i] = _buffer[i].Key;
        }

        return count;
    }

    public void Dispose() => rows.Dispose();
}

/// <summary>
/// What an extent holds beyond a run's sections (ADR 0139, ADR 0177, storage
/// format §7.5): its order, the key range of every row, and where its open
/// rows' positions, its closing rows' positions and its rows within are.
/// </summary>
/// <remarks>
/// <para>
/// The directory words after a run's tail: the order; the smallest and the
/// largest key (four words each); the open positions' offset and length; the
/// closing positions' offset and length; the within rows' offset, length and
/// count; and, from version 5, the width of a position.
/// </para>
/// <para>
/// Version 4: a position is ULEB128 of its distance after the window's start,
/// one per open key and one per closing key, in key order, and a row within
/// is its key's 32 bytes, then ULEB128 of the asserting position's distance
/// after the window's start and of the retracting position's, each 0 for the
/// window's boundary. Nothing in them is reachable but by reading from the
/// start, so an as-of read inside the window narrows the whole extent.
/// </para>
/// <para>
/// Version 5 (ADR 0177): the same distances, each in <see cref="Width"/>
/// bytes little-endian, so the <c>i</c>th key's is at <c>i × width</c> — a
/// column beside its key section. The rows within are a thirteenth key
/// section, their keys in blocks with their fences in the directory, and
/// their column holds two distances a row. A read reaches a key's intervals
/// from the key, and a head read, which reads the two key sections a run
/// has, never touches a column.
/// </para>
/// </remarks>
internal sealed class ExtentInfo
{
    /// <summary>The words of a format-4 extent's tail.</summary>
    internal const int TailWords = 16;

    /// <summary>The words of a format-5 extent's tail: format 4's and the width.</summary>
    internal const int TailWords5 = 17;

    private ExtentInfo(IReadableBlob blob, ushort version, long windowFrom, IndexOrder order, QuadKey min, QuadKey max, long openAt, long openLength, long closeAt, long closeLength, long withinAt, long withinLength, long withinCount, int width, KeySection? withinKeys)
    {
        Blob = blob;
        Version = version;
        WindowFrom = windowFrom;
        Order = order;
        Min = min;
        Max = max;
        OpenAt = openAt;
        OpenLength = openLength;
        CloseAt = closeAt;
        CloseLength = closeLength;
        WithinAt = withinAt;
        WithinLength = withinLength;
        WithinCount = withinCount;
        Width = width;
        WithinKeys = withinKeys;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal IReadableBlob Blob { get; }

    /// <summary>The extent's derived version: 4, narrowed to be read inside its window, or 5, sought (ADR 0177).</summary>
    internal ushort Version { get; }

    /// <summary>Whether a read inside the window can reach a key's intervals from the key (ADR 0177).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Seekable => WithinKeys is not null;

    /// <summary>The position the window starts after.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long WindowFrom { get; }

    internal IndexOrder Order { get; }

    /// <summary>The smallest key of any row; the largest is <see cref="Max"/>. Both zero for an extent with no rows.</summary>
    internal QuadKey Min { get; }

    internal QuadKey Max { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long OpenAt { get; }

    internal long OpenLength { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long CloseAt { get; }

    internal long CloseLength { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long WithinAt { get; }

    internal long WithinLength { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long WithinCount { get; }

    /// <summary>The bytes of one distance in a format-5 column; 0 in format 4, whose distances are ULEB128.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int Width { get; }

    /// <summary>The keys of the rows within, one a row, in a format-5 extent; null in format 4.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal KeySection? WithinKeys { get; }

    /// <summary>The words an extent of this version adds to a run's tail.</summary>
    internal static int WordsOf(ushort version) => version >= DerivedFormat.ExtentVersion ? TailWords5 : TailWords;

    /// <summary>The bytes a distance takes in a window of this many positions: enough for the largest, at least one.</summary>
    internal static int WidthFor(long distance)
    {
        int width = 1;

        while (width < 8 && (ulong)distance >> (width * 8) != 0)
        {
            width++;
        }

        return width;
    }

    internal static byte[] Words(IndexOrder order, QuadKey min, QuadKey max, long openAt, long openLength, long closeAt, long closeLength, long withinAt, long withinLength, long withinCount, int width)
    {
        byte[] words = new byte[(width == 0 ? TailWords : TailWords5) * 8];
        Span<byte> span = words;
        BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)order);
        WriteKey(span[8..], min);
        WriteKey(span[40..], max);
        BinaryPrimitives.WriteUInt64LittleEndian(span[72..], (ulong)openAt);
        BinaryPrimitives.WriteUInt64LittleEndian(span[80..], (ulong)openLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[88..], (ulong)closeAt);
        BinaryPrimitives.WriteUInt64LittleEndian(span[96..], (ulong)closeLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[104..], (ulong)withinAt);
        BinaryPrimitives.WriteUInt64LittleEndian(span[112..], (ulong)withinLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[120..], (ulong)withinCount);

        if (width != 0)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[128..], (ulong)width);
        }

        return words;
    }

    /// <summary>
    /// The extent's own words, checked against where the run's sections end
    /// and the directory begins, and in version 5 against its key sections'
    /// counts; null when they do not describe sections that fill the space
    /// between.
    /// </summary>
    internal static ExtentInfo? TryRead(IReadableBlob blob, ReadOnlySpan<byte> words, DerivedHeader header, long sectionsAt, KeySection? withinKeys, long openCount, long closeCount)
    {
        ulong order = BinaryPrimitives.ReadUInt64LittleEndian(words);
        long openAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[72..]);
        long openLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[80..]);
        long closeAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[88..]);
        long closeLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[96..]);
        long withinAt = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[104..]);
        long withinLength = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[112..]);
        long withinCount = (long)BinaryPrimitives.ReadUInt64LittleEndian(words[120..]);

        if (order >= Orders.Count
            || openAt != sectionsAt
            || openLength < 0 || closeLength < 0 || withinLength < 0 || withinCount < 0
            || closeAt != openAt + openLength
            || withinAt != closeAt + closeLength
            || withinAt + withinLength != header.DirectoryOffset)
        {
            return null;
        }

        int width = 0;

        if (header.Version >= DerivedFormat.ExtentVersion)
        {
            // Every column holds exactly its keys' distances (ADR 0177).
            ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(words[128..]);

            if (withinKeys is null || stored is < 1 or > 8)
            {
                return null;
            }

            width = (int)stored;

            if (openLength != openCount * width
                || closeLength != closeCount * width
                || withinCount != withinKeys.Count
                || withinLength != withinCount * 2 * width)
            {
                return null;
            }
        }
        else if (withinCount > withinLength / (QuadKey.Size + 2))
        {
            return null;
        }

        return new ExtentInfo(blob, header.Version, header.From, (IndexOrder)order, ReadKey(words[8..]), ReadKey(words[40..]), openAt, openLength, closeAt, closeLength, withinAt, withinLength, withinCount, width, width == 0 ? null : withinKeys);
    }

    /// <summary>The open rows' asserting positions, in key order.</summary>
    internal PositionReader OpenPositions() => new(Blob, OpenAt, OpenLength, WindowFrom, Width);

    /// <summary>The closing rows' retracting positions, in key order.</summary>
    internal PositionReader ClosePositions() => new(Blob, CloseAt, CloseLength, WindowFrom, Width);

    /// <summary>The rows within, in order.</summary>
    internal WithinReader Within() => new(this);

    /// <summary>
    /// A distance of a format-5 column (ADR 0177): the <paramref name="index"/>th
    /// of the column at <paramref name="at"/>, one small read.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Distance(long at, long index)
    {
        Span<byte> bytes = stackalloc byte[8];
        Span<byte> read = bytes[..Width];

        if (Blob.Read(new ByteOffset(at + (index * Width)), read) != read.Length)
        {
            throw new IOException("An extent is shorter than its directory says.");
        }

        return ColumnValue(read);
    }

    /// <summary>A distance of <c>bytes.Length</c> bytes, little-endian.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static long ColumnValue(ReadOnlySpan<byte> bytes)
    {
        ulong value = 0;

        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            value = (value << 8) | bytes[i];
        }

        return (long)value;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void WriteKey(Span<byte> span, in QuadKey key)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(span, key.K0);
        BinaryPrimitives.WriteUInt64LittleEndian(span[8..], key.K1);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], key.K2);
        BinaryPrimitives.WriteUInt64LittleEndian(span[24..], key.K3);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static QuadKey ReadKey(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(span),
        BinaryPrimitives.ReadUInt64LittleEndian(span[8..]),
        BinaryPrimitives.ReadUInt64LittleEndian(span[16..]),
        BinaryPrimitives.ReadUInt64LittleEndian(span[24..]));
}

/// <summary>A run of bytes of a blob read forward through the synchronous blob read, a buffer at a time.</summary>
internal class ForwardReader
{
    private readonly IReadableBlob _blob;
    private readonly long _end;
    private readonly byte[] _buffer;
    private long _next;
    private int _filled;
    private int _at;

    internal ForwardReader(IReadableBlob blob, long at, long length)
    {
        _blob = blob;
        _next = at;
        _end = at + length;

        // A buffer no larger than what it reads: a seek's few rows read a few bytes (ADR 0177).
        _buffer = new byte[(int)Math.Clamp(length, 1, 1 << 14)];
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    protected byte Byte()
    {
        if (_at == _filled)
        {
            int count = (int)Math.Min(_buffer.Length, _end - _next);

            if (count <= 0 || _blob.Read(new ByteOffset(_next), _buffer.AsSpan(0, count)) != count)
            {
                ThrowShort();
            }

            _next += count;
            _filled = count;
            _at = 0;
        }

        return _buffer[_at++];
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    protected long Uleb()
    {
        ulong value = 0;

        for (int shift = 0; shift < 64; shift += 7)
        {
            byte b = Byte();
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return (long)value;
            }
        }

        ThrowShort();
        return 0;
    }

    /// <summary>A distance of a format-5 column: <paramref name="width"/> bytes, little-endian (ADR 0177).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    protected long Fixed(int width)
    {
        ulong value = 0;

        for (int i = 0; i < width; i++)
        {
            value |= (ulong)Byte() << (i * 8);
        }

        return (long)value;
    }

    /// <summary>A distance in either version: ULEB128 in 4, <paramref name="width"/> bytes in 5.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    protected long Distance(int width) => width == 0 ? Uleb() : Fixed(width);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void ThrowShort() => throw new IOException("An extent is shorter than its directory says.");
}

/// <summary>Positions of an extent's open or closing rows, in key order.</summary>
internal sealed class PositionReader(IReadableBlob blob, long at, long length, long windowFrom, int width) : ForwardReader(blob, at, length)
{
    /// <summary>The next position; the directory's counts make one due for every key read.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Next() => windowFrom + Distance(width);
}

/// <summary>An extent's rows within, in order: in format 4 one stream, in format 5 a key section and its column.</summary>
internal sealed class WithinReader : ForwardReader
{
    private readonly ExtentInfo _info;
    private readonly SectionReader? _keys;
    private long _read;

    internal WithinReader(ExtentInfo info)
        : base(info.Blob, info.WithinAt, info.WithinLength)
    {
        _info = info;
        _keys = info.WithinKeys is { } keys ? new SectionReader(keys) : null;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryNext(out ExtentRow row)
    {
        if (_read == _info.WithinCount)
        {
            _keys?.Release();
            row = default;
            return false;
        }

        QuadKey key;

        if (_keys is not null)
        {
            key = _keys.Current();
            _keys.Advance();
        }
        else
        {
            Span<byte> bytes = stackalloc byte[QuadKey.Size];

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Byte();
            }

            key = ExtentInfo.ReadKey(bytes);
        }

        long from = Distance(_info.Width);
        long to = Distance(_info.Width);
        _read++;
        long windowFrom = _info.WindowFrom;
        row = new ExtentRow(key, from == 0 ? 0 : windowFrom + from, to == 0 ? 0 : windowFrom + to, ExtentSection.Within);
        return true;
    }

    /// <summary>Returns the key block buffer, when the rows stop being read before their end.</summary>
    internal void Release() => _keys?.Release();
}

internal static partial class DerivedFormat
{
    /// <summary>
    /// Writes one order's extent (ADR 0139): the open rows' keys as the
    /// order's asserted section and the closing rows' keys as its retracted
    /// section — so the extent reads as a run, an exact delta against the
    /// extents before it — then their positions and the rows within. The
    /// rows of each section are made twice, keys then positions, so nothing
    /// the size of the extent is held. In format 5 (ADR 0177) the rows
    /// within's keys are a thirteenth key section, and every position is a
    /// fixed-width column beside its keys.
    /// </summary>
    internal static async ValueTask WriteExtentAsync(
        IDerivedStore store,
        BlobName name,
        IndexOrder order,
        DatasetId dataset,
        long from,
        long to,
        byte[] toHash,
        Func<long, CancellationToken, ValueTask<IRowSource>> rows,
        CancellationToken cancellationToken,
        ushort version = ExtentVersion)
    {
        KeyRange range = new();
        int width = version >= ExtentVersion ? ExtentInfo.WidthFor(to - from) : 0;
        long openCount = 0;
        long closeCount = 0;

        async ValueTask<IKeySource> Section(int section, CancellationToken token)
        {
            if (section == (int)order * 2)
            {
                return new RangeKeys(await rows(ExtentSection.Open, token).ConfigureAwait(false), range);
            }

            if (section == ((int)order * 2) + 1)
            {
                return new RangeKeys(await rows(ExtentSection.Close, token).ConfigureAwait(false), range);
            }

            if (section == Sections)
            {
                return new RangeKeys(await rows(ExtentSection.Within, token).ConfigureAwait(false), range);
            }

            return new SectionSource(KeySection.Empty);
        }

        async ValueTask<(long End, byte[] Words)> Extra(IBlobWriter writer, long offset, CancellationToken token)
        {
            long openAt = offset;
            long openLength;
            (openLength, openCount) = await WritePositionsAsync(writer, await rows(ExtentSection.Open, token).ConfigureAwait(false), from, open: true, width, token).ConfigureAwait(false);
            long closeAt = openAt + openLength;
            long closeLength;
            (closeLength, closeCount) = await WritePositionsAsync(writer, await rows(ExtentSection.Close, token).ConfigureAwait(false), from, open: false, width, token).ConfigureAwait(false);
            long withinAt = closeAt + closeLength;
            (long withinLength, long withinCount) = await WriteWithinAsync(writer, await rows(ExtentSection.Within, token).ConfigureAwait(false), from, range, width, token).ConfigureAwait(false);
            return (withinAt + withinLength, ExtentInfo.Words(order, range.Min, range.Max, openAt, openLength, closeAt, closeLength, withinAt, withinLength, withinCount, width));
        }

        await WriteRunAsync(store, name, KindExtent, dataset, from, to, toHash, Section, [], 0, cancellationToken, version, Extra).ConfigureAwait(false);
    }

    private static async ValueTask<(long Length, long Count)> WritePositionsAsync(IBlobWriter writer, IRowSource rows, long from, bool open, int width, CancellationToken cancellationToken)
    {
        using (rows)
        {
            ExtentRow[] buffer = new ExtentRow[1024];
            ArrayBufferWriter<byte> bytes = new(1 << 16);
            long length = 0;
            long total = 0;

            while (true)
            {
                int count = rows.Next(buffer);

                if (count == 0)
                {
                    break;
                }

                for (int i = 0; i < count; i++)
                {
                    WriteDistance(bytes, (open ? buffer[i].From : buffer[i].To) - from, width);
                }

                total += count;

                if (bytes.WrittenCount >= 1 << 15)
                {
                    length += bytes.WrittenCount;
                    await writer.WriteAsync(bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);
                    bytes.ResetWrittenCount();
                }
            }

            length += bytes.WrittenCount;
            await writer.WriteAsync(bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);
            return (length, total);
        }
    }

    private static async ValueTask<(long Length, long Count)> WriteWithinAsync(IBlobWriter writer, IRowSource rows, long from, KeyRange range, int width, CancellationToken cancellationToken)
    {
        using (rows)
        {
            ExtentRow[] buffer = new ExtentRow[1024];
            ArrayBufferWriter<byte> bytes = new(1 << 16);
            long length = 0;
            long total = 0;

            while (true)
            {
                int count = rows.Next(buffer);

                if (count == 0)
                {
                    break;
                }

                for (int i = 0; i < count; i++)
                {
                    // Format 5's keys are the thirteenth key section; format 4's are inline.
                    if (width == 0)
                    {
                        ExtentInfo.WriteKey(bytes.GetSpan(QuadKey.Size), buffer[i].Key);
                        bytes.Advance(QuadKey.Size);
                    }

                    WriteDistance(bytes, buffer[i].From == 0 ? 0 : buffer[i].From - from, width);
                    WriteDistance(bytes, buffer[i].To == 0 ? 0 : buffer[i].To - from, width);
                    range.Add(buffer[i].Key);
                }

                total += count;

                if (bytes.WrittenCount >= 1 << 15)
                {
                    length += bytes.WrittenCount;
                    await writer.WriteAsync(bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);
                    bytes.ResetWrittenCount();
                }
            }

            length += bytes.WrittenCount;
            await writer.WriteAsync(bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);
            return (length, total);
        }
    }

    // A distance: ULEB128 in format 4, width bytes little-endian in 5.
    private static void WriteDistance(ArrayBufferWriter<byte> bytes, long distance, int width)
    {
        if (width == 0)
        {
            LogFormat.WriteUleb(bytes, (ulong)distance);
            return;
        }

        Span<byte> span = bytes.GetSpan(width);

        for (int i = 0; i < width; i++)
        {
            span[i] = (byte)((ulong)distance >> (i * 8));
        }

        bytes.Advance(width);
    }

    /// <summary>The smallest and largest key seen.</summary>
    private sealed class KeyRange
    {
        private bool _any;

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal QuadKey Min { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal QuadKey Max { get; private set; }

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal void Add(in QuadKey key)
        {
            if (!_any || key.CompareTo(Min) < 0)
            {
                Min = key;
            }

            if (!_any || key.CompareTo(Max) > 0)
            {
                Max = key;
            }

            _any = true;
        }
    }

    /// <summary>The keys of rows, for the section a run writer writes, noting their range as they pass.</summary>
    private sealed class RangeKeys(IRowSource rows, KeyRange range) : IKeySource, IDisposable
    {
        private readonly ExtentRow[] _buffer = new ExtentRow[256];

        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        public int Next(Span<QuadKey> buffer)
        {
            int count = rows.Next(_buffer.AsSpan(0, Math.Min(_buffer.Length, buffer.Length)));

            for (int i = 0; i < count; i++)
            {
                buffer[i] = _buffer[i].Key;
                range.Add(buffer[i]);
            }

            return count;
        }

        public void Dispose() => rows.Dispose();
    }
}
