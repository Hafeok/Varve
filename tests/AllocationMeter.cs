// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime;

namespace Varve;

/// <summary>
/// Bytes allocated on the current thread, read so that nothing but the code
/// under test can move the reading. Linked into every test project that
/// asserts allocation as a difference (<c>docs/testing.md</c> §4).
/// </summary>
/// <remarks>
/// <para>
/// Issue #32 found two things that move a single reading of
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/>, neither of them in the
/// code under test, and milestone 7a's Windows run found a third.
/// </para>
/// <para>
/// <strong>A collection during the window adds bytes.</strong> The counter is
/// "bytes handed to this thread, less the unused tail of its allocation
/// context". A collection — started by this thread's own large-object
/// allocation or by any other thread's — retires every thread's context, and
/// the tail it retires is then counted as allocated: up to one 8 KB
/// allocation quantum. A reading taken inside a no-GC region that is still
/// intact at its end had no collection in it; a reading whose region broke is
/// discarded.
/// </para>
/// <para>
/// <strong>Tiered compilation can subtract bytes.</strong> Once a measured
/// call is rejitted with PGO and inlined into its caller, an object the
/// caller never lets escape is allocated on the stack. The measured action
/// therefore returns what it made, and the meter keeps it alive, so that what
/// is allocated does not depend on the tier.
/// </para>
/// <para>
/// <strong>Everything else only ever adds.</strong> A pool miss, a cache that
/// fills on the first call, a lazily built table: each adds bytes to one
/// reading and not to the next. So every figure the meter answers is the
/// <strong>minimum of <see cref="Readings"/> readings</strong>, the one that
/// repeats, and no two readings have to agree. The loop this replaced waited
/// for two consecutive rounds to agree exactly on both sides of a pair, and
/// on Windows a parse whose arena pool missed on alternate readings never
/// did (7d43295, the 7a run of
/// <c>parsing_allocates_the_tree_and_nothing_else</c>).
/// </para>
/// </remarks>
internal static class AllocationMeter
{
    /// <summary>
    /// How many readings a figure is the minimum of. Ten: a disturbance adds
    /// to one reading, not to ten in a row, and ten readings of the largest
    /// measured action — a commit of 4,000 quads — cost milliseconds. It is
    /// not tuned to any test; raising it buys nothing once the minimum
    /// repeats, which it does by the second reading in every run observed.
    /// </summary>
    internal const int Readings = 10;

    // Enough for the largest measured action — a commit of 4,000 quads — and
    // within what TryStartNoGCRegion accepts.
    private const long RegionBytes = 64L << 20;

    private const int MaxAttempts = 200;

    /// <summary>
    /// What <paramref name="action"/> allocates on this thread: the minimum of
    /// <see cref="Readings"/> readings, each inside an intact no-GC region.
    /// The action's results are kept alive.
    /// </summary>
    /// <exception cref="InvalidOperationException">No attempt ran without a collection.</exception>
    internal static long Measure(Func<object?> action)
    {
        long minimum = long.MaxValue;

        for (int reading = 0; reading < Readings; reading++)
        {
            minimum = Math.Min(minimum, ReadOnce(action));
        }

        return minimum;
    }

    /// <summary>
    /// Readings of two actions — the small and the large side of a difference
    /// — interleaved, each side the minimum of <see cref="Readings"/>
    /// readings. Interleaved, so that whatever the process does over time
    /// (tiering, a neighbour's allocations) falls on both sides alike.
    /// </summary>
    internal static (long Small, long Large) MeasurePair(Func<object?> small, Func<object?> large)
    {
        long minimumSmall = long.MaxValue;
        long minimumLarge = long.MaxValue;

        for (int reading = 0; reading < Readings; reading++)
        {
            minimumSmall = Math.Min(minimumSmall, ReadOnce(small));
            minimumLarge = Math.Min(minimumLarge, ReadOnce(large));
        }

        return (minimumSmall, minimumLarge);
    }

    private static long ReadOnce(Func<object?> action)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (TryMeasure(action, out long bytes))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException(
            "Every attempt to read an allocation was interrupted by a collection; the reading would not be the code's.");
    }

    private static bool TryMeasure(Func<object?> action, out long bytes)
    {
        bool started;

        try
        {
            started = GC.TryStartNoGCRegion(RegionBytes);
        }
        catch (InvalidOperationException)
        {
            // Another test in this process holds a region.
            started = false;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        object? result = action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        bool intact = started && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion;

        if (intact)
        {
            try
            {
                GC.EndNoGCRegion();
            }
            catch (InvalidOperationException)
            {
                // Another thread's allocation ended the region after the check.
                intact = false;
            }
        }

        GC.KeepAlive(result);
        bytes = after - before;
        return intact;
    }
}
