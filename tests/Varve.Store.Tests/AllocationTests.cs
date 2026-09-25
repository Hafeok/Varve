// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Varve.Rdf;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// Allocation per quad is a defect (constraint 5). Measured as 3a and 3b
/// measured it: the same operation over 500 and over 4,000 quads, in rounds
/// until one reproduces the last, and the difference is what the extra quads
/// cost.
/// </summary>
/// <remarks>
/// <para>
/// A scan must cost <strong>exactly zero</strong> per quad.
/// </para>
/// <para>
/// A commit must cost <c>O(N)</c> bytes once, for the delta, and nothing per
/// quad in the index update. The second half is asserted exactly: the index
/// update's slope is the six sorted key arrays and not one byte more, which
/// no per-quad object — at least 24 bytes of header each — could hide in. The
/// whole commit's slope is asserted against a stated byte budget, because it
/// is a sum of arrays whose growth policy is the runtime's.
/// </para>
/// <para>
/// Readings are <see cref="AllocationMeter"/>'s (issue #32): a collection in
/// the window added up to 8 KB to one side, and a tier-up stack-allocated a
/// result the test discarded.
/// </para>
/// </remarks>
public class AllocationTests
{
    private const int Small = 500;
    private const int Large = 4_000;

    private static long sink;

    private static async Task<Dataset> Loaded(int quads)
    {
        Dataset dataset = await T.Open(new MemoryStorage());
        CommitRequest request = new();

        for (int i = 0; i < quads; i++)
        {
            request.Assert(T.Iri("s" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture)));
        }

        await dataset.CommitAsync(request, T.Ct);

        // A second, smaller run on top, so the scan merges more than one run.
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("extra"), T.Iri("p"), T.Iri("o")), T.Ct);
        return dataset;
    }

    // The cursor is returned so that it escapes at every tier.
    private static Func<object?> Scan(DatasetView source, TermHandle predicate) => () =>
    {
        IQuadCursor cursor = source.Match(TermHandle.None, predicate, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            sink += (long)cursor.Current.Object.Value;
        }

        cursor.Dispose();
        return cursor;
    };

    private static long Difference(Func<object?> small, Func<object?> large)
    {
        (long smallCost, long largeCost) = AllocationMeter.MeasurePair(small, large);
        return largeCost - smallCost;
    }

    [Fact]
    public async Task a_scan_of_a_pinned_read_allocates_nothing_per_quad()
    {
        await using Dataset small = await Loaded(Small);
        await using Dataset large = await Loaded(Large);
        using DatasetView smallView = small.Pin();
        using DatasetView largeView = large.Pin();
        Assert.True(smallView.TryInternalise(T.Iri("p"), out TermHandle ps));
        Assert.True(largeView.TryInternalise(T.Iri("p"), out TermHandle pl));

        Assert.Equal(0, Difference(Scan(smallView, ps), Scan(largeView, pl)));
        Assert.Equal(0, Difference(Scan(smallView, TermHandle.None), Scan(largeView, TermHandle.None)));
        Assert.True(AllocationMeter.Measure(Scan(smallView, ps)) <= 512);
    }

    [Fact]
    public async Task a_scan_of_an_as_of_read_allocates_nothing_per_quad()
    {
        await using Dataset small = await Loaded(Small);
        await using Dataset large = await Loaded(Large);

        // Position 2 with no checkpoint: the whole log overlaid on an empty run.
        using DatasetView smallView = await small.AsOfAsync(2, T.Ct);
        using DatasetView largeView = await large.AsOfAsync(2, T.Ct);

        Assert.Equal(0, Difference(Scan(smallView, TermHandle.None), Scan(largeView, TermHandle.None)));
    }

    /// <summary>
    /// The index update: a run built from the delta is six sorted arrays of
    /// 32-byte keys, so the extra quads cost exactly <c>6 × 32</c> bytes each.
    /// </summary>
    [Fact]
    public void the_index_update_allocates_the_six_key_arrays_and_nothing_per_quad()
    {
        Quad[] small = Quads(Small);
        Quad[] large = Quads(Large);

        (long smallCost, long largeCost) = AllocationMeter.MeasurePair(
            () => IndexVersion.Empty.Apply(small, [], 1),
            () => IndexVersion.Empty.Apply(large, [], 1));

        Assert.Equal((Large - Small) * Orders.Count * QuadKey.Size, largeCost - smallCost);
        TestContext.Current.TestOutputHelper?.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"index update: fixed {smallCost - (Small * 192)} bytes, then 192 bytes per quad"));
    }

    private static Quad[] Quads(int count)
    {
        Quad[] quads = new Quad[count];

        for (int i = 0; i < count; i++)
        {
            quads[i] = new Quad(new TermHandle((ulong)i + 1), new TermHandle(1), new TermHandle((ulong)i + 7));
        }

        return quads;
    }

    /// <summary>
    /// The whole commit path, known terms only: retract and re-assert the same
    /// quads, so the dictionary is not what is measured. The budget per quad is
    /// stated rather than discovered; see the remarks on the class. Read
    /// directly rather than through <see cref="AllocationMeter"/>, because a
    /// commit changes the state it is measured in; what a collection can add,
    /// 8 KB over 3,500 quads, is 2.3 bytes per quad against the budget.
    /// </summary>
    [Fact]
    public async Task a_commit_allocates_linearly_within_its_budget()
    {
        await using Dataset small = await Loaded(Small);
        await using Dataset large = await Loaded(Large);
        CommitRequest smallRetract = Request(Small, assert: false), smallAssert = Request(Small, assert: true);
        CommitRequest largeRetract = Request(Large, assert: false), largeAssert = Request(Large, assert: true);

        for (int i = 0; i < 3; i++)
        {
            await small.CommitAsync(smallRetract, T.Ct);
            await small.CommitAsync(smallAssert, T.Ct);
            await large.CommitAsync(largeRetract, T.Ct);
            await large.CommitAsync(largeAssert, T.Ct);
        }

        long smallCost = await MeasureAsync(small, smallRetract);
        long largeCost = await MeasureAsync(large, largeRetract);
        double perQuad = (largeCost - smallCost) / (double)(Large - Small);

        TestContext.Current.TestOutputHelper?.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"commit: {smallCost} bytes for {Small} quads, {largeCost} for {Large}: {perQuad:F1} bytes per extra quad"));
        Assert.True(perQuad <= Budget, string.Create(CultureInfo.InvariantCulture, $"{perQuad:F1} bytes per quad exceeds the budget of {Budget}"));
    }

    /// <summary>
    /// The per-quad budget of a commit, in bytes, and where it goes: the log
    /// record (the body's 32 bytes, in the body buffer, the record and the
    /// segment's copy), the delta (normalisation's lists, the remapped
    /// arrays, <see cref="QuadDelta"/>'s own sorted copies), the pending set
    /// and id set, and the index update — the new run's six key arrays and a
    /// merge's. Array growth by doubling can double any of them transiently.
    /// </summary>
    private const double Budget = 2_048;

    private static async Task<long> MeasureAsync(Dataset dataset, CommitRequest request)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        CommitResult result = await dataset.CommitAsync(request, T.Ct);
        long cost = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(CommitOutcome.Committed, result.Outcome);
        return cost;
    }

    private static CommitRequest Request(int quads, bool assert)
    {
        CommitRequest request = new();

        for (int i = 0; i < quads; i++)
        {
            RdfTerm s = T.Iri("s" + i.ToString(CultureInfo.InvariantCulture));
            RdfTerm o = T.Integer(i.ToString(CultureInfo.InvariantCulture));

            if (assert)
            {
                request.Assert(s, T.Iri("p"), o);
            }
            else
            {
                request.Retract(s, T.Iri("p"), o);
            }
        }

        return request;
    }
}
