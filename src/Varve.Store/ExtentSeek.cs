// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// A format-5 extent set read at a position strictly inside its window,
/// <c>(From, position]</c> (ADR 0177): seek, don't narrow. A read looks its
/// keys up in the set's key sections exactly as a read of the present does,
/// then classifies only the matching rows' intervals against the position.
/// The set is never rewritten, and a read costs what its matches cost.
/// </summary>
/// <remarks>
/// <para>
/// A key's rows are its open row (asserted in the window, never at its
/// start), its closing row (asserted at the start, retracted in the window)
/// and its rows within. The quad is asserted at the window's start when one
/// row begins there, and at the position when one row covers it. The
/// narrowed delta is the difference: asserted when it holds at the position
/// and not at the start, retracted when the reverse — exactly what narrowing
/// the set to <c>(From, position]</c> gives (ADR 0138), without the pass.
/// </para>
/// <para>
/// Each of the three sources is a key section with a fixed-width column
/// beside it, so a key's rows are a binary search of the fences, one block
/// and one read of the column each.
/// </para>
/// </remarks>
internal sealed class ExtentSeek
{
    /// <summary>
    /// The most rows of the set in a pattern's range that an estimate counts
    /// one by one, for an exact count; past it the estimate is the set's
    /// count at its end, and says it is not exact (ADR 0177).
    /// </summary>
    internal const long ExactCountRows = 4096;

    private readonly ExtentInfo[] _infos = new ExtentInfo[Orders.Count];
    private readonly Run[] _keys = new Run[Orders.Count];

    internal ExtentSeek(ExtentSet set, long position)
    {
        for (int o = 0; o < Orders.Count; o++)
        {
            _infos[o] = set.Orders[o].Extent!;
            _keys[o] = set.Orders[o].Run;
        }

        Position = position;
    }

    /// <summary>The position the set is read at, strictly inside its window.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Position { get; }

    /// <summary>
    /// What the set read at the position says about a quad: asserted,
    /// retracted, or nothing (null), against the sets before it.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool? Lookup(in QuadKey spog)
    {
        ExtentInfo info = Info(IndexOrder.Spog);
        Run run = Keys(IndexOrder.Spog);
        Span<QuadKey> buffer = stackalloc QuadKey[KeySection.BlockKeys];
        bool atStart = false;
        bool atPosition = false;

        (long next, long end) = run.Asserted(IndexOrder.Spog).Range(in spog, in spog, buffer, out _, out _);

        if (next < end)
        {
            atPosition |= info.WindowFrom + info.Distance(info.OpenAt, next) <= Position;
        }

        (next, end) = run.Retracted(IndexOrder.Spog).Range(in spog, in spog, buffer, out _, out _);

        if (next < end)
        {
            atStart = true;
            atPosition |= info.WindowFrom + info.Distance(info.CloseAt, next) > Position;
        }

        (next, end) = info.WithinKeys!.Range(in spog, in spog, buffer, out _, out _);

        for (long row = next; row < end; row++)
        {
            long from = info.Distance(info.WithinAt, 2 * row);
            long to = info.Distance(info.WithinAt, (2 * row) + 1);
            atStart |= from == 0;
            atPosition |= Covers(from == 0 ? 0 : info.WindowFrom + from, to == 0 ? 0 : info.WindowFrom + to, Position);
        }

        return Delta(atStart, atPosition);
    }

    /// <summary>The set's narrowed delta in one order and key range, in key order.</summary>
    internal ExtentSeekCursor Delta(IndexOrder order, in QuadKey low, in QuadKey high) =>
        new(Info(order), Keys(order), order, Position, in low, in high);

    /// <summary>
    /// The set's contribution to a count of the range: exact, by reading the
    /// delta, when the set holds at most <see cref="ExactCountRows"/> rows in
    /// it; otherwise its count at its end, and <paramref name="exact"/> false.
    /// </summary>
    internal long Count(IndexOrder order, in QuadKey low, in QuadKey high, out bool exact)
    {
        ExtentInfo info = Info(order);
        Run run = Keys(order);
        KeySection open = run.Asserted(order);
        KeySection close = run.Retracted(order);
        long opened = open.UpperBound(in high) - open.LowerBound(in low);
        long closed = close.UpperBound(in high) - close.LowerBound(in low);
        long within = info.WithinKeys!.UpperBound(in high) - info.WithinKeys.LowerBound(in low);

        if (opened + closed + within > ExactCountRows)
        {
            exact = false;
            return opened - closed;
        }

        exact = true;
        long count = 0;
        ExtentSeekCursor delta = Delta(order, in low, in high);

        try
        {
            while (delta.TryNext(out _, out bool asserts))
            {
                count += asserts ? 1 : -1;
            }
        }
        finally
        {
            delta.Dispose();
        }

        return count;
    }

    /// <summary>Whether an interval covers the position: begun at the window's start or by it, and not ended by it.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool Covers(long from, long to, long position) => from <= position && (to == 0 || to > position);

    /// <summary>The narrowed verdict of a key held at the start and at the position or not.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool? Delta(bool atStart, bool atPosition) =>
        atPosition == atStart ? null : atPosition;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private ExtentInfo Info(IndexOrder order) => _infos[(int)order];

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private Run Keys(IndexOrder order) => _keys[(int)order];
}

/// <summary>
/// One extent's narrowed delta in a key range (ADR 0177): its open, closing
/// and within rows in the range, merged by key, each key's rows classified
/// against the position, and the keys whose verdict differs from the
/// window's start given in order with it. One object and three key blocks a
/// scan; nothing per row.
/// </summary>
internal sealed class ExtentSeekCursor : IDisposable
{
    private readonly ColumnRows _open;
    private readonly ColumnRows _close;
    private readonly ColumnRows _within;
    private readonly long _windowFrom;
    private readonly long _position;

    internal ExtentSeekCursor(ExtentInfo info, Run run, IndexOrder order, long position, in QuadKey low, in QuadKey high)
    {
        _windowFrom = info.WindowFrom;
        _position = position;
        _open = new ColumnRows(info, run.Asserted(order), info.OpenAt, 1, in low, in high);
        _close = new ColumnRows(info, run.Retracted(order), info.CloseAt, 1, in low, in high);
        _within = new ColumnRows(info, info.WithinKeys!, info.WithinAt, 2, in low, in high);
    }

    /// <summary>The next key the delta holds, and whether it asserts or retracts it; false at the range's end.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool TryNext(out QuadKey key, out bool asserts)
    {
        while (true)
        {
            bool any = false;
            key = default;
            Least(_open, ref key, ref any);
            Least(_close, ref key, ref any);
            Least(_within, ref key, ref any);

            if (!any)
            {
                asserts = false;
                return false;
            }

            bool atStart = false;
            bool atPosition = false;

            while (_open.HasCurrent && _open.Key.Equals(key))
            {
                atPosition |= _windowFrom + _open.First <= _position;
                _open.Advance();
            }

            while (_close.HasCurrent && _close.Key.Equals(key))
            {
                atStart = true;
                atPosition |= _windowFrom + _close.First > _position;
                _close.Advance();
            }

            while (_within.HasCurrent && _within.Key.Equals(key))
            {
                long from = _within.First;
                long to = _within.Second;
                atStart |= from == 0;
                atPosition |= ExtentSeek.Covers(from == 0 ? 0 : _windowFrom + from, to == 0 ? 0 : _windowFrom + to, _position);
                _within.Advance();
            }

            if (ExtentSeek.Delta(atStart, atPosition) is { } verdict)
            {
                asserts = verdict;
                return true;
            }
        }
    }

    public void Dispose()
    {
        _open.Release();
        _close.Release();
        _within.Release();
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void Least(ColumnRows rows, ref QuadKey key, ref bool any)
    {
        if (rows.HasCurrent && (!any || rows.Key.CompareTo(key) < 0))
        {
            key = rows.Key;
            any = true;
        }
    }
}

/// <summary>
/// The rows of one key section in a key range, with their column beside them
/// (ADR 0177): one distance a row, or two for the rows within, read forward
/// from the range's first row.
/// </summary>
internal sealed class ColumnRows : ForwardReader
{
    private readonly SectionReader _keys;
    private readonly long _end;
    private readonly int _width;
    private readonly int _values;

    internal ColumnRows(ExtentInfo info, KeySection keys, long columnAt, int values, in QuadKey low, in QuadKey high)
        : this(info, keys, columnAt, values, Bounds(keys, in low, in high))
    {
    }

    private ColumnRows(ExtentInfo info, KeySection keys, long columnAt, int values, (long Next, long End) range)
        : base(info.Blob, columnAt + (range.Next * values * info.Width), (range.End - range.Next) * values * info.Width)
    {
        _keys = new SectionReader(keys);
        _keys.Seek(range.Next);
        _end = range.End;
        _width = info.Width;
        _values = values;
        Load();
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool HasCurrent { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal QuadKey Key { get; private set; }

    /// <summary>The row's first distance after the window's start: its open position, its closing position, or a row within's assertion.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long First { get; private set; }

    /// <summary>A row within's retraction's distance; 0 for the others.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Second { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal void Advance()
    {
        _keys.Advance();
        Load();
    }

    internal void Release() => _keys.Release();

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private void Load()
    {
        HasCurrent = _keys.Next < _end;

        if (!HasCurrent)
        {
            _keys.Release();
            return;
        }

        Key = _keys.Current();
        First = Fixed(_width);
        Second = _values == 2 ? Fixed(_width) : 0;
    }

    // The section's indexes from the low bound through the high one.
    private static (long Next, long End) Bounds(KeySection keys, in QuadKey low, in QuadKey high)
    {
        Span<QuadKey> buffer = stackalloc QuadKey[KeySection.BlockKeys];
        return keys.Range(in low, in high, buffer, out _, out _);
    }
}

/// <summary>
/// A scan of a version whose newest member is a sought set (ADR 0177): the
/// older runs' scan, overlaid in key order by the set's narrowed delta in the
/// same order and range — a key the delta asserts is yielded, one it
/// retracts is not, and every other comes from the older runs.
/// </summary>
internal sealed class ExtentSeekScan : IQuadCursor
{
    private readonly RunCursor _older;
    private readonly ExtentSeekCursor _delta;
    private readonly IndexOrder _order;
    private readonly ulong _subject;
    private readonly ulong _predicate;
    private readonly ulong _object;
    private readonly GraphPattern _graph;
    private bool _olderHeld;
    private bool _olderEnded;
    private QuadKey _olderKey;
    private bool _deltaHeld;
    private bool _deltaEnded;
    private QuadKey _deltaKey;
    private bool _deltaAsserts;

    internal ExtentSeekScan(Run[] older, ExtentSeek seek, ulong subject, ulong predicate, ulong @object, GraphPattern graph)
    {
        _subject = subject;
        _predicate = predicate;
        _object = @object;
        _graph = graph;
        (_order, QuadKey low, QuadKey high) = RunCursor.Plan(subject, predicate, @object, graph);
        _older = new RunCursor(older, subject, predicate, @object, graph);
        _delta = seek.Delta(_order, in low, in high);
    }

    public Quad Current { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool MoveNext()
    {
        while (true)
        {
            if (!_olderHeld && !_olderEnded)
            {
                _olderHeld = _older.MoveNext();
                _olderEnded = !_olderHeld;

                if (_olderHeld)
                {
                    Quad quad = _older.Current;
                    _olderKey = Orders.Key(_order, in quad);
                }
            }

            if (!_deltaHeld && !_deltaEnded)
            {
                _deltaHeld = _delta.TryNext(out _deltaKey, out _deltaAsserts);
                _deltaEnded = !_deltaHeld;
            }

            if (!_olderHeld && !_deltaHeld)
            {
                Current = default;
                return false;
            }

            int c = !_deltaHeld ? -1 : !_olderHeld ? 1 : _olderKey.CompareTo(_deltaKey);

            if (c < 0)
            {
                _olderHeld = false;
                Current = _older.Current;
                return true;
            }

            _deltaHeld = false;

            if (c == 0)
            {
                // The older runs hold it; the delta decides.
                _olderHeld = false;

                if (_deltaAsserts)
                {
                    Current = _older.Current;
                    return true;
                }

                continue;
            }

            if (_deltaAsserts)
            {
                Quad quad = Orders.Quad(_order, in _deltaKey);

                if (Matches(in quad))
                {
                    Current = quad;
                    return true;
                }
            }
        }
    }

    public void Dispose()
    {
        _older.Dispose();
        _delta.Dispose();
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Matches(in Quad quad) =>
        (_subject == 0 || _subject == quad.Subject.Value)
        && (_predicate == 0 || _predicate == quad.Predicate.Value)
        && (_object == 0 || _object == quad.Object.Value)
        && _graph.Matches(quad.Graph);
}
