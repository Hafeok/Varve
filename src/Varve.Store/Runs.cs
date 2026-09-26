// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// An immutable sorted run: for each of the six orders, the keys it asserts and
/// the keys it retracts (ADR 0041).
/// </summary>
/// <remarks>
/// A commit's run is built from its effective delta in a constant number of
/// arrays — twelve at most — and nothing per quad. A checkpoint's run is the
/// same shape over bytes in the derived store.
/// </remarks>
internal sealed class Run
{
    private static readonly ReadOnlyMemory<QuadKey>[] NoRetractions = new ReadOnlyMemory<QuadKey>[Orders.Count];

    private readonly ReadOnlyMemory<QuadKey>[] _asserted;
    private readonly ReadOnlyMemory<QuadKey>[] _retracted;

    internal Run(ReadOnlyMemory<QuadKey>[] asserted, ReadOnlyMemory<QuadKey>[] retracted)
    {
        _asserted = asserted;
        _retracted = retracted;
    }

    /// <summary>How many keys it holds in each order, asserted and retracted.</summary>
    internal int Count => _asserted[0].Length + _retracted[0].Length;

    internal int AssertedCount => _asserted[0].Length;

    internal bool HasRetractions => !_retracted[0].IsEmpty;

    internal ReadOnlyMemory<QuadKey> Asserted(IndexOrder order) => _asserted[(int)order];

    internal ReadOnlyMemory<QuadKey> Retracted(IndexOrder order) => _retracted[(int)order];

    /// <summary>A run from a delta's two halves: one array per order per half.</summary>
    internal static Run FromDelta(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted) =>
        new(Build(asserted), retracted.IsEmpty ? NoRetractions : Build(retracted));

    /// <summary>A run of assertions only, already sorted in every order: a checkpoint's.</summary>
    internal static Run FromSorted(ReadOnlyMemory<QuadKey>[] asserted) => new(asserted, NoRetractions);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static ReadOnlyMemory<QuadKey>[] Build(ReadOnlySpan<Quad> quads)
    {
        ReadOnlyMemory<QuadKey>[] orders = new ReadOnlyMemory<QuadKey>[Orders.Count];

        for (int order = 0; order < Orders.Count; order++)
        {
            QuadKey[] keys = new QuadKey[quads.Length];

            for (int i = 0; i < quads.Length; i++)
            {
                keys[i] = Orders.Key((IndexOrder)order, in quads[i]);
            }

            keys.AsSpan().Sort();
            orders[order] = keys;
        }

        return orders;
    }

    /// <summary>
    /// What this run says about a quad: asserted, retracted, or nothing (null).
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool? Lookup(in QuadKey spog)
    {
        if (Search(_asserted[0].Span, in spog) >= 0)
        {
            return true;
        }

        return Search(_retracted[0].Span, in spog) >= 0 ? false : null;
    }

    /// <summary>
    /// Two runs as one, still an exact delta against the runs older than both
    /// (I2). A key one run asserts and the other retracts is back where it was
    /// before the older run and drops out; the newer decides wherever both say
    /// the same thing, which I2 makes impossible anyway. Merging into the
    /// oldest run drops retractions, because there is nothing older for them
    /// to cancel.
    /// </summary>
    /// <remarks>
    /// Keeping the newer verdict for a key the older asserted and the newer
    /// retracted reads the same through a lookup, because the retraction just
    /// hides a key nothing older holds; it counts differently, because
    /// <see cref="IndexVersion.Estimate"/> subtracts a retraction it assumes
    /// cancels an older assertion. The estimate property found the case.
    /// </remarks>
    internal static Run Merge(Run older, Run newer, bool dropRetractions)
    {
        ReadOnlyMemory<QuadKey>[] asserted = new ReadOnlyMemory<QuadKey>[Orders.Count];
        ReadOnlyMemory<QuadKey>[] retracted = dropRetractions ? NoRetractions : new ReadOnlyMemory<QuadKey>[Orders.Count];

        for (int order = 0; order < Orders.Count; order++)
        {
            IndexOrder o = (IndexOrder)order;
            ReadOnlySpan<QuadKey> oa = older.Asserted(o).Span;
            ReadOnlySpan<QuadKey> or = older.Retracted(o).Span;
            ReadOnlySpan<QuadKey> na = newer.Asserted(o).Span;
            ReadOnlySpan<QuadKey> nr = newer.Retracted(o).Span;

            (int assertCount, int retractCount) = MergeInto(oa, or, na, nr, [], [], count: true);

            QuadKey[] a = assertCount == 0 ? [] : new QuadKey[assertCount];
            QuadKey[] r = dropRetractions || retractCount == 0 ? [] : new QuadKey[retractCount];
            MergeInto(oa, or, na, nr, a, dropRetractions ? [] : r, count: false);

            asserted[order] = a;

            if (!dropRetractions)
            {
                retracted[order] = r;
            }
        }

        return new Run(asserted, retracted);
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

            bool assert = (newerA && !olderR) || (olderA && !newerR);
            bool retract = (newerR && !olderA) || (olderR && !newerA);

            if (assert)
            {
                if (!count)
                {
                    asserted[outA] = min;
                }

                outA++;
            }
            else if (retract)
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

/// <summary>
/// The default projection's state at one position: an immutable list of runs,
/// oldest first. Publishing a new version is one reference write, which is
/// what "position persisted atomically with state" means in memory; pinning
/// is taking the current one (ADR 0041).
/// </summary>
internal sealed class IndexVersion
{
    internal IndexVersion(long position, Run[] runs)
    {
        Position = position;
        Runs = runs;
    }

    internal static IndexVersion Empty { get; } = new(0, []);

    internal long Position { get; }

    internal Run[] Runs { get; }

    /// <summary>A version standing on a checkpoint's run.</summary>
    internal static IndexVersion FromCheckpoint(long position, Run run) => new(position, [run]);

    /// <summary>
    /// This version with one more commit applied: its delta as a new run,
    /// then tiered merges while the newest run is at least a quarter the size
    /// of the one below it.
    /// </summary>
    internal IndexVersion Apply(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted, long position)
    {
        if (asserted.IsEmpty && retracted.IsEmpty)
        {
            return new IndexVersion(position, Runs);
        }

        Run[] runs = new Run[Runs.Length + 1];
        Runs.CopyTo(runs, 0);
        runs[^1] = Run.FromDelta(asserted, retracted);
        int count = runs.Length;

        while (count >= 2 && (long)runs[count - 1].Count * 4 >= runs[count - 2].Count)
        {
            runs[count - 2] = Run.Merge(runs[count - 2], runs[count - 1], dropRetractions: count == 2);
            count--;
        }

        if (count == 1 && runs[0].HasRetractions)
        {
            runs[0] = Run.Merge(runs[0], Run.FromDelta([], []), dropRetractions: true);
        }

        return new IndexVersion(position, count == runs.Length ? runs : runs.AsSpan(0, count).ToArray());
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
    /// one of the six orders (ADR 0041), so a run's contribution is two binary
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
            ReadOnlySpan<QuadKey> asserted = run.Asserted(order).Span;
            ReadOnlySpan<QuadKey> retracted = run.Retracted(order).Span;
            count += Run.UpperBound(asserted, in high) - Run.LowerBound(asserted, in low);
            count -= Run.UpperBound(retracted, in high) - Run.LowerBound(retracted, in low);
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
/// One object and one stream array per scan; nothing per quad.
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
                    QuadKey key = stream.Keys.Span[stream.Next];

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

                if (stream.Next < stream.End && stream.Keys.Span[stream.Next].Equals(min))
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

    private struct Stream
    {
        internal ReadOnlyMemory<QuadKey> Keys;
        internal int Next;
        internal int End;
        internal int Run;
        internal bool Asserts;

        internal static Stream Over(ReadOnlyMemory<QuadKey> keys, int run, bool asserts, in QuadKey low, in QuadKey high)
        {
            ReadOnlySpan<QuadKey> span = keys.Span;

            return new Stream
            {
                Keys = keys,
                Next = global::Varve.Store.Run.LowerBound(span, in low),
                End = global::Varve.Store.Run.UpperBound(span, in high),
                Run = run,
                Asserts = asserts,
            };
        }
    }
}
