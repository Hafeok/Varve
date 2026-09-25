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
/// code under test.
/// </para>
/// <para>
/// <strong>A collection during the window adds bytes.</strong> The counter is
/// "bytes handed to this thread, less the unused tail of its allocation
/// context". A collection — started by this thread's own large-object
/// allocation or by any other thread's — retires every thread's context, and
/// the tail it retires is then counted as allocated: up to one 8 KB
/// allocation quantum. Under a thread allocating 200 KB arrays, the store's
/// index update read 7,960 bytes over in 248 windows of 3,000, on either side
/// of the difference; the flake that opened the issue was 7,920. It only ever
/// adds: of 12,000 scan readings under the same noise, none was below the
/// true value. A reading taken inside a no-GC region that is still intact at
/// its end had no collection in it, and under the same noise no such reading
/// deviated in 2,000 pairs; a reading whose region broke is discarded.
/// </para>
/// <para>
/// <strong>Tiered compilation can subtract bytes.</strong> Once a measured
/// call is rejitted with PGO and inlined into its caller, an object the
/// caller never lets escape is allocated on the stack: the index version the
/// update test used to discard, 32 bytes, on whichever side was measured
/// after the tier-up. The measured action therefore returns what it made,
/// and the meter keeps it alive, so that what is allocated does not depend
/// on the tier. And a pair of readings is accepted only when the next round
/// reproduces it exactly, so that a tier-up landing between the two sides of
/// one round is a round that is not used.
/// </para>
/// </remarks>
internal static class AllocationMeter
{
    // Enough for the largest measured action — a commit of 4,000 quads — and
    // within what TryStartNoGCRegion accepts.
    private const long RegionBytes = 64L << 20;

    private const int MaxAttempts = 200;

    /// <summary>
    /// What <paramref name="action"/> allocates on this thread, read once,
    /// inside an intact no-GC region. The action's result is kept alive.
    /// </summary>
    /// <exception cref="InvalidOperationException">No attempt ran without a collection.</exception>
    internal static long Measure(Func<object?> action)
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

    /// <summary>
    /// Readings of two actions — the small and the large side of a difference
    /// — taken in rounds until one round reproduces the one before it on both
    /// sides. Each reading is <see cref="Measure"/>'s.
    /// </summary>
    /// <exception cref="InvalidOperationException">No two rounds agreed.</exception>
    internal static (long Small, long Large) MeasurePair(Func<object?> small, Func<object?> large)
    {
        long lastSmall = Measure(small);
        long lastLarge = Measure(large);

        for (int round = 0; round < MaxAttempts; round++)
        {
            long nextSmall = Measure(small);
            long nextLarge = Measure(large);

            if (nextSmall == lastSmall && nextLarge == lastLarge)
            {
                return (nextSmall, nextLarge);
            }

            (lastSmall, lastLarge) = (nextSmall, nextLarge);
        }

        throw new InvalidOperationException("No two rounds of allocation readings agreed.");
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
