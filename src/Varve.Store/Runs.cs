// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// One sorted list of keys of a run: an array in memory, or a section of a
/// derived blob read in blocks through the synchronous blob read (ADR 0071).
/// </summary>
/// <remarks>
/// A section on a blob holds its fences — the first key of every block — in
/// memory, so a seek is a binary search of the fences and one block read.
/// The scan code is the same for both: a cursor walks a block at a time, and
/// an array is one block.
/// </remarks>
internal sealed class KeySection
{
    internal const int BlockKeys = 128;

    private readonly IReadableBlob? _blob;
    private readonly long _offset;
    private readonly QuadKey[] _fences;

    private KeySection(ReadOnlyMemory<QuadKey> memory, IReadableBlob? blob, long offset, long count, QuadKey[] fences)
    {
        Memory = memory;
        _blob = blob;
        _offset = offset;
        _fences = fences;
        Count = count;
    }

    internal static KeySection Empty { get; } = new(ReadOnlyMemory<QuadKey>.Empty, null, 0, 0, []);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long Count { get; }

    /// <summary>The keys, when they are in memory; empty for a section on a blob.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ReadOnlyMemory<QuadKey> Memory { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool OnBlob => _blob is not null;

    internal static KeySection Of(ReadOnlyMemory<QuadKey> keys) => keys.IsEmpty ? Empty : new(keys, null, 0, keys.Length, []);

    internal static KeySection On(IReadableBlob blob, long offset, long count, QuadKey[] fences) =>
        count == 0 ? Empty : new(ReadOnlyMemory<QuadKey>.Empty, blob, offset, count, fences);

    /// <summary>Reads block <paramref name="block"/> into the buffer; returns how many keys it holds.</summary>
    /// <exception cref="IOException">The blob is shorter than its directory says.</exception>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal int ReadBlock(long block, Span<QuadKey> buffer)
    {
        long start = block * BlockKeys;
        int count = (int)Math.Min(BlockKeys, Count - start);
        Span<byte> bytes = MemoryMarshal.AsBytes(buffer[..count]);

        if (_blob!.Read(new ByteOffset(_offset + (start * QuadKey.Size)), bytes) != bytes.Length)
        {
            throw new IOException("A derived run is shorter than its directory says.");
        }

        return count;
    }

    /// <summary>The first index whose key is at or above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long LowerBound(in QuadKey bound)
    {
        if (_blob is null)
        {
            return Run.LowerBound(Memory.Span, in bound);
        }

        long block = Run.LowerBound(_fences, in bound) - 1;

        if (block < 0)
        {
            return 0;
        }

        Span<QuadKey> keys = stackalloc QuadKey[BlockKeys];
        int count = ReadBlock(block, keys);
        return (block * BlockKeys) + Run.LowerBound(keys[..count], in bound);
    }

    /// <summary>The first index whose key is above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal long UpperBound(in QuadKey bound)
    {
        if (_blob is null)
        {
            return Run.UpperBound(Memory.Span, in bound);
        }

        long block = Run.UpperBound(_fences, in bound) - 1;

        if (block < 0)
        {
            return 0;
        }

        Span<QuadKey> keys = stackalloc QuadKey[BlockKeys];
        int count = ReadBlock(block, keys);
        return (block * BlockKeys) + Run.UpperBound(keys[..count], in bound);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Contains(in QuadKey key)
    {
        if (_blob is null)
        {
            return Run.Search(Memory.Span, in key) >= 0;
        }

        long block = Run.UpperBound(_fences, in key) - 1;

        if (block < 0)
        {
            return false;
        }

        Span<QuadKey> keys = stackalloc QuadKey[BlockKeys];
        int count = ReadBlock(block, keys);
        return Run.Search(keys[..count], in key) >= 0;
    }
}

/// <summary>
/// A derived blob that runs read, held open while anything reads it: the
/// index version or checkpoint list that owns it, and every pinned or as-of
/// view that captured it. Closed when the last of them lets go.
/// </summary>
internal sealed class RunBlob
{
    private int _references = 1;

    internal RunBlob(BlobName name, IReadableBlob blob)
    {
        Name = name;
        Blob = blob;
    }

    internal BlobName Name { get; }

    internal IReadableBlob Blob { get; }

    /// <summary>Takes a reference, unless the blob has already been closed.</summary>
    internal bool TryAcquire()
    {
        while (true)
        {
            int current = Volatile.Read(ref _references);

            if (current == 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            Blob.Dispose();
        }
    }
}

/// <summary>
/// An immutable sorted run: for each of the six orders, the keys it asserts and
/// the keys it retracts (ADR 0041), covering the commits after
/// <see cref="From"/> up to <see cref="To"/>.
/// </summary>
/// <remarks>
/// A commit's run is built from its effective delta in a constant number of
/// arrays — twelve at most — and nothing per quad. A disk run and a
/// checkpoint are the same shape over a derived blob (ADR 0070).
/// </remarks>
internal sealed class Run
{
    private readonly KeySection[] _asserted;
    private readonly KeySection[] _retracted;

    internal Run(KeySection[] asserted, KeySection[] retracted, long from, long to, RunBlob? blob = null)
    {
        _asserted = asserted;
        _retracted = retracted;
        From = from;
        To = to;
        Blob = blob;
    }

    /// <summary>The run covers the commits after this position.</summary>
    internal long From { get; }

    /// <summary>The run covers the commits up to this position.</summary>
    internal long To { get; }

    /// <summary>The blob the run is read from, or null for a run in memory.</summary>
    internal RunBlob? Blob { get; }

    internal bool InMemory => Blob is null;

    /// <summary>How many keys it holds in each order, asserted and retracted.</summary>
    internal long Count => _asserted[0].Count + _retracted[0].Count;

    internal long AssertedCount => _asserted[0].Count;

    internal bool HasRetractions => _retracted[0].Count != 0;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal KeySection Asserted(IndexOrder order) => _asserted[(int)order];

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal KeySection Retracted(IndexOrder order) => _retracted[(int)order];

    private static KeySection[] NoRetractions()
    {
        KeySection[] sections = new KeySection[Orders.Count];
        Array.Fill(sections, KeySection.Empty);
        return sections;
    }

    /// <summary>A run from a delta's two halves: one array per order per half.</summary>
    internal static Run FromDelta(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted, long from, long to) =>
        new(Build(asserted), retracted.IsEmpty ? NoRetractions() : Build(retracted), from, to);

    /// <summary>A run of assertions only, in memory, already sorted in every order.</summary>
    internal static Run FromSorted(ReadOnlyMemory<QuadKey>[] asserted, long from, long to)
    {
        KeySection[] sections = new KeySection[Orders.Count];

        for (int order = 0; order < Orders.Count; order++)
        {
            sections[order] = KeySection.Of(asserted[order]);
        }

        return new Run(sections, NoRetractions(), from, to);
    }

    // The arrays are the run: one per order, kept for as long as the version
    // that holds it. The loop inside is per quad and allocates nothing.
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    [DesignDecision(typeof(StoreHotPathScope.RunBuildAllocatesTheRunItReturns), Scope = ExceptionScope.HotPath)]
    private static KeySection[] Build(ReadOnlySpan<Quad> quads)
    {
        KeySection[] orders = new KeySection[Orders.Count];

        for (int order = 0; order < Orders.Count; order++)
        {
            QuadKey[] keys = new QuadKey[quads.Length];

            for (int i = 0; i < quads.Length; i++)
            {
                keys[i] = Orders.Key((IndexOrder)order, in quads[i]);
            }

            keys.AsSpan().Sort();
            orders[order] = KeySection.Of(keys);
        }

        return orders;
    }

    /// <summary>
    /// What this run says about a quad: asserted, retracted, or nothing (null).
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool? Lookup(in QuadKey spog)
    {
        if (_asserted[0].Contains(in spog))
        {
            return true;
        }

        return _retracted[0].Contains(in spog) ? false : null;
    }

    /// <summary>
    /// Two runs in memory as one, still an exact delta against the runs older
    /// than both (I2). A key one run asserts and the other retracts is back
    /// where it was before the older run and drops out; the newer decides
    /// wherever both say the same thing, which I2 makes impossible anyway.
    /// Merging into the oldest run drops retractions, because there is
    /// nothing older for them to cancel.
    /// </summary>
    /// <remarks>
    /// Keeping the newer verdict for a key the older asserted and the newer
    /// retracted reads the same through a lookup, because the retraction just
    /// hides a key nothing older holds; it counts differently, because
    /// <see cref="IndexVersion.Estimate"/> subtracts a retraction it assumes
    /// cancels an older assertion. The estimate property found the case. Runs
    /// on disk are merged by <see cref="RunMerge"/>, by the same rule.
    /// </remarks>
    internal static Run Merge(Run older, Run newer, bool dropRetractions)
    {
        KeySection[] asserted = new KeySection[Orders.Count];
        KeySection[] retracted = NoRetractions();

        for (int order = 0; order < Orders.Count; order++)
        {
            IndexOrder o = (IndexOrder)order;
            ReadOnlySpan<QuadKey> oa = older.Asserted(o).Memory.Span;
            ReadOnlySpan<QuadKey> or = older.Retracted(o).Memory.Span;
            ReadOnlySpan<QuadKey> na = newer.Asserted(o).Memory.Span;
            ReadOnlySpan<QuadKey> nr = newer.Retracted(o).Memory.Span;

            (int assertCount, int retractCount) = MergeInto(oa, or, na, nr, [], [], count: true);

            QuadKey[] a = assertCount == 0 ? [] : new QuadKey[assertCount];
            QuadKey[] r = dropRetractions || retractCount == 0 ? [] : new QuadKey[retractCount];
            MergeInto(oa, or, na, nr, a, dropRetractions ? [] : r, count: false);

            asserted[order] = KeySection.Of(a);

            if (!dropRetractions)
            {
                retracted[order] = KeySection.Of(r);
            }
        }

        return new Run(asserted, retracted, older.From, newer.To);
    }

    // One pass that either counts or writes. A key both runs mention with
    // opposite verdicts cancels; one they agree on, or one run alone mentions,
    // is kept.
    private static (int Asserted, int Retracted) MergeInto(
        ReadOnlySpan<QuadKey> olderAsserted,
        ReadOnlySpan<QuadKey> olderRetracted,
        ReadOnlySpan<QuadKey> newerAsserted,
        ReadOnlySpan<QuadKey> newerRetracted,
        Span<QuadKey> asserted,
        Span<QuadKey> retracted,
        bool count)
    {
        int ia = 0, ir = 0, na = 0, nr = 0, outA = 0, outR = 0;

        while (ia < olderAsserted.Length || ir < olderRetracted.Length || na < newerAsserted.Length || nr < newerRetracted.Length)
        {
            QuadKey min = default;
            bool any = false;
            Pick(olderAsserted, ia, ref min, ref any);
            Pick(olderRetracted, ir, ref min, ref any);
            Pick(newerAsserted, na, ref min, ref any);
            Pick(newerRetracted, nr, ref min, ref any);

            bool olderA = Take(olderAsserted, ref ia, in min);
            bool olderR = Take(olderRetracted, ref ir, in min);
            bool newerA = Take(newerAsserted, ref na, in min);
            bool newerR = Take(newerRetracted, ref nr, in min);

            if (RunMerge.Asserts(olderA, olderR, newerA, newerR))
            {
                if (!count)
                {
                    asserted[outA] = min;
                }

                outA++;
            }
            else if (RunMerge.Retracts(olderA, olderR, newerA, newerR))
            {
                if (!count && !retracted.IsEmpty)
                {
                    retracted[outR] = min;
                }

                outR++;
            }
        }

        return (outA, outR);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static void Pick(ReadOnlySpan<QuadKey> keys, int at, ref QuadKey min, ref bool any)
    {
        if (at < keys.Length && (!any || keys[at].CompareTo(min) < 0))
        {
            min = keys[at];
            any = true;
        }
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static bool Take(ReadOnlySpan<QuadKey> keys, ref int at, in QuadKey key)
    {
        if (at < keys.Length && keys[at].Equals(key))
        {
            at++;
            return true;
        }

        return false;
    }

    /// <summary>Binary search: the index of the key, or the complement of where it would go.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Search(ReadOnlySpan<QuadKey> keys, in QuadKey key)
    {
        int low = 0;
        int high = keys.Length - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int c = keys[middle].CompareTo(key);

            if (c == 0)
            {
                return middle;
            }

            if (c < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    /// <summary>The first index whose key is at or above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int LowerBound(ReadOnlySpan<QuadKey> keys, in QuadKey bound)
    {
        int at = Search(keys, in bound);
        return at >= 0 ? at : ~at;
    }

    /// <summary>The first index whose key is above the bound.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int UpperBound(ReadOnlySpan<QuadKey> keys, in QuadKey bound)
    {
        int low = 0;
        int high = keys.Length;

        while (low < high)
        {
            int middle = low + ((high - low) >> 1);

            if (keys[middle].CompareTo(bound) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}

/// <summary>The merge rule of ADR 0041, shared by runs merged in memory and on disk.</summary>
internal static class RunMerge
{
    /// <summary>Whether the merged run asserts a key, given what each side said of it.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool Asserts(bool olderAsserted, bool olderRetracted, bool newerAsserted, bool newerRetracted) =>
        (newerAsserted && !olderRetracted) || (olderAsserted && !newerRetracted);

    /// <summary>Whether the merged run retracts a key, given what each side said of it.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool Retracts(bool olderAsserted, bool olderRetracted, bool newerAsserted, bool newerRetracted) =>
        (newerRetracted && !olderAsserted) || (olderRetracted && !newerAsserted);
}

/// <summary>
/// The default projection's state at one position: an immutable list of runs,
/// oldest first — disk runs, then the memtable's runs in memory. Publishing a
/// new version is one reference write, which is what "position persisted
/// atomically with state" means in memory; pinning is taking the current one
/// (ADR 0041, ADR 0070).
/// </summary>
internal sealed class IndexVersion
{
    internal IndexVersion(long position, Run[] runs, int frozen)
    {
        Position = position;
        Runs = runs;
        Frozen = frozen;
    }

    internal static IndexVersion Empty { get; } = new(0, [], 0);

    internal long Position { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal Run[] Runs { get; }

    /// <summary>
    /// How many runs, from the oldest, a commit's tiered merge leaves alone:
    /// disk runs, and memtable runs maintenance is writing to disk.
    /// </summary>
    internal int Frozen { get; }

    /// <summary>The quads, asserted and retracted, held in memory: the memtable's size.</summary>
    internal long MemtableCount
    {
        get
        {
            long count = 0;

            foreach (Run run in Runs)
            {
                if (run.InMemory)
                {
                    count += run.Count;
                }
            }

            return count;
        }
    }

    /// <summary>A version standing on one run, such as a checkpoint's.</summary>
    internal static IndexVersion FromBase(long position, Run run) => new(position, [run], 1);

    /// <summary>
    /// This version with one more commit applied: its delta as a new run,
    /// then tiered merges of the memtable while the newest run is at least a
    /// quarter the size of the one below it.
    /// </summary>
    internal IndexVersion Apply(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted, long position)
    {
        if (asserted.IsEmpty && retracted.IsEmpty)
        {
            return new IndexVersion(position, Runs, Frozen);
        }

        Run[] runs = new Run[Runs.Length + 1];
        Runs.CopyTo(runs, 0);
        runs[^1] = Run.FromDelta(asserted, retracted, position - 1, position);
        int count = runs.Length;

        while (count >= 2 && count - 2 >= Frozen && runs[count - 1].Count * 4 >= runs[count - 2].Count)
        {
            runs[count - 2] = Run.Merge(runs[count - 2], runs[count - 1], dropRetractions: count == 2);
            count--;
        }

        if (count == 1 && Frozen == 0 && runs[0].HasRetractions)
        {
            runs[0] = Run.Merge(runs[0], Run.FromDelta([], [], runs[0].To, runs[0].To), dropRetractions: true);
        }

        return new IndexVersion(position, count == runs.Length ? runs : runs.AsSpan(0, count).ToArray(), Frozen);
    }

    /// <summary>This version with its runs replaced and the frozen boundary moved; the position stays.</summary>
    internal IndexVersion With(Run[] runs, int frozen) => new(Position, runs, frozen);

    /// <summary>
    /// Takes a reference to every blob the version reads, for a view that
    /// will hold it; false when one has been closed already, and the caller
    /// takes the current version again.
    /// </summary>
    internal bool TryAcquire()
    {
        for (int i = 0; i < Runs.Length; i++)
        {
            if (Runs[i].Blob is { } blob && !blob.TryAcquire())
            {
                for (int j = 0; j < i; j++)
                {
                    Runs[j].Blob?.Release();
                }

                return false;
            }
        }

        return true;
    }

    internal void Release()
    {
        foreach (Run run in Runs)
        {
            run.Blob?.Release();
        }
    }

    /// <summary>Whether the quad is in the graph at this version.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Contains(in Quad quad)
    {
        QuadKey key = Orders.Key(IndexOrder.Spog, in quad);

        for (int i = Runs.Length - 1; i >= 0; i--)
        {
            bool? said = Runs[i].Lookup(in key);

            if (said.HasValue)
            {
                return said.Value;
            }
        }

        return false;
    }

    /// <summary>The quads matching a pattern.</summary>
    internal IQuadCursor Match(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph) =>
        new RunCursor(Runs, subject.Value, predicate.Value, @object.Value, graph);

    /// <summary>
    /// How many quads match a pattern, exactly, at <c>O(runs × log n)</c>
    /// (ADR 0049). Every combination of bound positions is a prefix range in
    /// one of the six orders (ADR 0041), so a run's contribution is two
    /// searches per key list; and each run is a delta exact against the state
    /// the older runs produce (I2), so the sum over runs of asserted keys in
    /// range minus retracted keys in range is the count. <see cref="GraphMatch.AnyNamed"/>
    /// is every graph minus the default graph, both prefix ranges.
    /// </summary>
    internal CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph)
    {
        if (graph.Match == GraphMatch.AnyNamed)
        {
            return CardinalityEstimate.Exact(new QuadCount(
                CountRange(subject.Value, predicate.Value, @object.Value, GraphPattern.Any)
                - CountRange(subject.Value, predicate.Value, @object.Value, GraphPattern.DefaultGraph)));
        }

        return CardinalityEstimate.Exact(new QuadCount(CountRange(subject.Value, predicate.Value, @object.Value, graph)));
    }

    private long CountRange(ulong subject, ulong predicate, ulong @object, GraphPattern graph)
    {
        (IndexOrder order, QuadKey low, QuadKey high) = RunCursor.Plan(subject, predicate, @object, graph);
        long count = 0;

        foreach (Run run in Runs)
        {
            KeySection asserted = run.Asserted(order);
            KeySection retracted = run.Retracted(order);
            count += asserted.UpperBound(in high) - asserted.LowerBound(in low);
            count -= retracted.UpperBound(in high) - retracted.LowerBound(in low);
        }

        return count;
    }

    /// <summary>How many quads the version holds. A full scan; for tests and checkpoints.</summary>
    internal long CountQuads()
    {
        long count = 0;

        using IQuadCursor cursor = Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            count++;
        }

        return count;
    }
}

/// <summary>
/// A merging scan over a version's runs in one order and one key range. For
/// equal keys the newest run decides, and the quad is emitted when that run
/// asserted it.
/// </summary>
/// <remarks>
/// One object, one stream array and, for each section on a blob, one block
/// buffer per scan; nothing per quad. A section in memory is one block.
/// </remarks>
internal sealed class RunCursor : IQuadCursor
{
    private readonly Stream[] _streams;
    private readonly IndexOrder _order;
    private readonly ulong _subject;
    private readonly ulong _predicate;
    private readonly ulong _object;
    private readonly GraphPattern _graph;

    internal RunCursor(Run[] runs, ulong subject, ulong predicate, ulong @object, GraphPattern graph)
    {
        _subject = subject;
        _predicate = predicate;
        _object = @object;
        _graph = graph;

        (_order, QuadKey low, QuadKey high) = Plan(subject, predicate, @object, graph);

        _streams = new Stream[runs.Length * 2];

        for (int i = 0; i < runs.Length; i++)
        {
            _streams[2 * i] = Stream.Over(runs[i].Asserted(_order), i, true, in low, in high);
            _streams[(2 * i) + 1] = Stream.Over(runs[i].Retracted(_order), i, false, in low, in high);
        }
    }

    public Quad Current { get; private set; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool MoveNext()
    {
        Stream[] streams = _streams;

        while (true)
        {
            QuadKey min = default;
            bool any = false;

            for (int i = 0; i < streams.Length; i++)
            {
                ref Stream stream = ref streams[i];

                if (stream.Next < stream.End)
                {
                    QuadKey key = stream.Key();

                    if (!any || key.CompareTo(min) < 0)
                    {
                        min = key;
                        any = true;
                    }
                }
            }

            if (!any)
            {
                Current = default;
                return false;
            }

            int decidingRun = -1;
            bool asserted = false;

            for (int i = 0; i < streams.Length; i++)
            {
                ref Stream stream = ref streams[i];

                if (stream.Next < stream.End && stream.Key().Equals(min))
                {
                    stream.Next++;

                    if (stream.Run > decidingRun)
                    {
                        decidingRun = stream.Run;
                        asserted = stream.Asserts;
                    }
                }
            }

            if (!asserted)
            {
                continue;
            }

            Quad quad = Orders.Quad(_order, in min);

            if (Matches(in quad))
            {
                Current = quad;
                return true;
            }
        }
    }

    public void Dispose()
    {
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private bool Matches(in Quad quad) =>
        (_subject == 0 || _subject == quad.Subject.Value)
        && (_predicate == 0 || _predicate == quad.Predicate.Value)
        && (_object == 0 || _object == quad.Object.Value)
        && _graph.Matches(quad.Graph);

    /// <summary>
    /// The order whose longest prefix is bound, and the key range it gives. A
    /// graph position is bound by <see cref="GraphPattern.DefaultGraph"/> (id 0)
    /// and by a named graph; <see cref="GraphPattern.AnyNamed"/> starts the
    /// range at graph 1 when the graph comes first. With six orders every
    /// subset of the four positions is some order's prefix, so the range holds
    /// exactly the matching keys; the cursor still filters, the estimate
    /// counts the range and the property test holds the two to each other.
    /// </summary>
    internal static (IndexOrder Order, QuadKey Low, QuadKey High) Plan(
        ulong subject, ulong predicate, ulong @object, GraphPattern graph)
    {
        Span<ulong> value = [subject, predicate, @object, graph.Graph.Value];
        Span<bool> bound =
        [
            subject != 0,
            predicate != 0,
            @object != 0,
            graph.Match is GraphMatch.Named or GraphMatch.DefaultGraph,
        ];

        IndexOrder best = IndexOrder.Spog;
        int bestPrefix = -1;

        for (int order = 0; order < Orders.Count; order++)
        {
            ReadOnlySpan<byte> positions = Orders.Positions((IndexOrder)order);
            int prefix = 0;

            while (prefix < 4 && bound[positions[prefix]])
            {
                prefix++;
            }

            if (prefix > bestPrefix)
            {
                best = (IndexOrder)order;
                bestPrefix = prefix;
            }
        }

        ReadOnlySpan<byte> chosen = Orders.Positions(best);
        Span<ulong> low = stackalloc ulong[4];
        Span<ulong> high = stackalloc ulong[4];

        for (int i = 0; i < 4; i++)
        {
            if (i < bestPrefix)
            {
                low[i] = high[i] = value[chosen[i]];
            }
            else
            {
                low[i] = i == bestPrefix && chosen[i] == 3 && graph.Match == GraphMatch.AnyNamed ? 1UL : 0UL;
                high[i] = ulong.MaxValue;
            }
        }

        return (best, new QuadKey(low[0], low[1], low[2], low[3]), new QuadKey(high[0], high[1], high[2], high[3]));
    }

    /// <summary>One section's keys in range, a block at a time.</summary>
    private struct Stream
    {
        internal KeySection Section;
        internal ReadOnlyMemory<QuadKey> Block;
        internal QuadKey[]? Buffer;
        internal long BlockStart;
        internal long Next;
        internal long End;
        internal int Run;
        internal bool Asserts;

        internal static Stream Over(KeySection section, int run, bool asserts, in QuadKey low, in QuadKey high)
        {
            long next = section.LowerBound(in low);
            long end = next < section.Count ? section.UpperBound(in high) : next;

            return new Stream
            {
                Section = section,
                Block = section.Memory,
                Buffer = section.OnBlob && next < end ? new QuadKey[KeySection.BlockKeys] : null,
                BlockStart = section.OnBlob ? long.MinValue / 2 : 0,
                Next = next,
                End = end,
                Run = run,
                Asserts = asserts,
            };
        }

        /// <summary>The key at <see cref="Next"/>, reading its block when it is not the one held.</summary>
        [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
        internal QuadKey Key()
        {
            long at = Next - BlockStart;

            if (at < 0 || at >= Block.Length)
            {
                long block = Next / KeySection.BlockKeys;
                int count = Section.ReadBlock(block, Buffer);
                Block = new ReadOnlyMemory<QuadKey>(Buffer, 0, count);
                BlockStart = block * KeySection.BlockKeys;
                at = Next - BlockStart;
            }

            return Block.Span[(int)at];
        }
    }
}
