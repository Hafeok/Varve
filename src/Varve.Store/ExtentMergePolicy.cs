// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven.Ledger.Varve;
using DecisionDriven;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// How a dataset's writer merges its extent sets in the background (ADR 0158,
/// ADR 0159), so a read of the present merges few runs however many
/// checkpoints were taken: size-tiered over adjacent sets, with a fan-out
/// and a cap.
/// </summary>
/// <remarks>
/// <para>
/// A set's **tier** is the base-<see cref="FanOut"/> logarithm of its bytes
/// over 1 MiB. Once <see cref="FanOut"/> adjacent sets of one tier stand
/// side by side, they merge into one, which lands a tier up. A set past half
/// of <see cref="MaxSetBytes"/> is not merged again, and no merge makes a set
/// past it, so one merge's work and one extent's upload are bounded. An as-of
/// read inside a format-5 set seeks it, at the cost of its matches whatever
/// the set's size (ADR 0177, ADR 0178).
/// </para>
/// <para>
/// **The bound.** At most <c>(F − 1)</c> sets per tier below the cap, plus the
/// sets past half of it (fewer than <c>2 × total / MaxSetBytes</c>), plus one
/// merge in flight. A set's six order extents merge together, so the bound on
/// sets is the bound per permutation.
/// </para>
/// <para>
/// A merge is considered only when the catalog changes — at a checkpoint, a
/// drop, or another merge — and runs in the dataset's maintenance; nothing is
/// triggered by elapsed time. A merge never changes a result at any position.
/// </para>
/// </remarks>
public sealed class ExtentMergePolicy
{
    /// <summary>The size tiers' base: a set of at most this many bytes is in tier 0.</summary>
    internal const long TierBase = 1L << 20;

    // Declared before Default, which reads it.
    private const long DefaultCap = 256L << 20;

    /// <summary>Fan-out 4 and sets capped at <see cref="DefaultMaxSetBytes"/>.</summary>
    public static ExtentMergePolicy Default { get; } = new();

    /// <summary>No set is merged but those a drop merges (ADR 0138).</summary>
    public static ExtentMergePolicy Off { get; } = new() { Enabled = false };

    /// <summary>The default cap on a merged set's bytes, chosen from the measurement at REC size M (ADR 0159).</summary>
    public static ByteCount DefaultMaxSetBytes { get; } = new(DefaultCap);

    /// <summary>Whether the writer merges in the background. True by default.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How many adjacent sets of one tier merge into one: 2 to 16, 4 by default.</summary>
    public int FanOut { get; init; } = 4;

    /// <summary>The bytes no merge makes a set exceed; 1 MiB or more. It bounds merges, not reads (ADR 0178).</summary>
    [DesignDecision(typeof(TheFormat5UpgradeAndTheSetCap.TheCapBoundsMergesNotReads), Scope = ExceptionScope.Boundary)]
    public ByteCount MaxSetBytes { get; init; } = new(DefaultCap);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(FanOut, 2, nameof(FanOut));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(FanOut, 16, nameof(FanOut));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSetBytes.Value, 1L << 20, nameof(MaxSetBytes));
    }

    /// <summary>A set's tier: the base-<see cref="FanOut"/> logarithm of its bytes over <see cref="TierBase"/>.</summary>
    internal int TierOf(long bytes)
    {
        int tier = 0;
        long bound = TierBase;

        while (bytes > bound && tier < 62)
        {
            tier++;
            bound = bound > long.MaxValue / FanOut ? long.MaxValue : bound * FanOut;
        }

        return tier;
    }

    /// <summary>Whether a set of <paramref name="bytes"/> is past half the cap: never merged again.</summary>
    internal bool Full(long bytes) => bytes > MaxSetBytes.Value / 2;

    /// <summary>
    /// How many sets below half the cap the policy allows before it merges
    /// under pressure: <c>F − 1</c> per tier from tier 0 to half the cap's.
    /// </summary>
    internal int Bound => (FanOut - 1) * (TierOf(MaxSetBytes.Value / 2) + 1);

    /// <summary>
    /// The adjacent sets to merge next, as <c>(first, count)</c> into
    /// <paramref name="bytes"/>, the sets' sizes oldest first; none when
    /// nothing is due. Two rules, both over sets below half the cap, and
    /// neither taking a merge past the cap:
    /// <list type="number">
    /// <item><description>**Similar sizes**: the oldest run of
    /// <see cref="FanOut"/> adjacent sets whose largest is at most
    /// <see cref="FanOut"/> times their smallest — sets of one size, which
    /// arrive side by side when checkpoints are taken on a schedule, and land
    /// one tier up. This rule writes each byte about once per tier.</description></item>
    /// <item><description>**Pressure**: when more sets than <see cref="Bound"/>
    /// stand below half the cap — sizes alternating across a tier's edge,
    /// which the first rule would never pair — the adjacent run of up to
    /// <see cref="FanOut"/> whose bytes together are fewest. This rule keeps
    /// the bound whatever the sizes.</description></item>
    /// </list>
    /// </summary>
    internal (int First, int Count)? Choose(ReadOnlySpan<long> bytes)
    {
        if (!Enabled)
        {
            return null;
        }

        for (int i = 0; i + FanOut <= bytes.Length; i++)
        {
            long smallest = long.MaxValue;
            long largest = 0;
            bool open = true;

            for (int j = i; j < i + FanOut; j++)
            {
                open &= !Full(bytes[j]);
                smallest = Math.Min(smallest, bytes[j]);
                largest = Math.Max(largest, bytes[j]);
            }

            if (open && largest / FanOut <= smallest && Fit(bytes, i, FanOut) is int count and >= 2)
            {
                return (i, count);
            }
        }

        int below = 0;

        foreach (long b in bytes)
        {
            below += Full(b) ? 0 : 1;
        }

        if (below <= Bound)
        {
            return null;
        }

        (int First, int Count)? fewest = null;
        long least = long.MaxValue;

        for (int i = 0; i + 1 < bytes.Length; i++)
        {
            int count = 0;
            long total = 0;

            while (count < FanOut && i + count < bytes.Length && !Full(bytes[i + count]) && total + bytes[i + count] <= MaxSetBytes.Value)
            {
                total += bytes[i + count];
                count++;
            }

            if (count >= 2 && total < least)
            {
                least = total;
                fewest = (i, count);
            }
        }

        return fewest;
    }

    // Of the run from first, the longest prefix of at most count sets that
    // together stay within the cap.
    private int Fit(ReadOnlySpan<long> bytes, int first, int count)
    {
        int fits = 0;
        long total = 0;

        while (fits < count && total + bytes[first + fits] <= MaxSetBytes.Value)
        {
            total += bytes[first + fits];
            fits++;
        }

        return fits;
    }
}
