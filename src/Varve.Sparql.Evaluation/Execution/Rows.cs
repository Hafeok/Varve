// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Sparql.Evaluation.Execution;

/// <summary>
/// The layout of a solution (<c>sparql-evaluation.md</c> §4.1): <c>width</c>
/// slots of 64 bits, then one mask word per 64 slots whose bit says the slot
/// holds a local term rather than a source handle. Zero is unbound.
/// </summary>
internal static class Rows
{
    internal static int Length(int width) => width + ((width + 63) >> 6);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static TermRef Get(ulong[] row, int width, int slot) =>
        new(row[slot], ((row[width + (slot >> 6)] >> (slot & 63)) & 1) != 0);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool IsBound(ulong[] row, int slot) => row[slot] != 0;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void Set(ulong[] row, int width, int slot, TermRef value)
    {
        row[slot] = value.Raw;
        ulong bit = 1UL << (slot & 63);
        ref ulong mask = ref row[width + (slot >> 6)];
        mask = value.IsLocal ? mask | bit : mask & ~bit;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static void Clear(ulong[] row, int width, int slot) => Set(row, width, slot, TermRef.Unbound);

    internal static ulong[] Copy(ulong[] row)
    {
        ulong[] copy = new ulong[row.Length];
        Array.Copy(row, copy, row.Length);
        return copy;
    }

    /// <summary>Whether two solutions agree on every slot both bind (§18.5, compatible mappings).</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool Compatible(Exec exec, ulong[] left, ulong[] right)
    {
        int width = exec.Width;
        for (int slot = 0; slot < width; slot++)
        {
            if (left[slot] != 0 && right[slot] != 0
                && !exec.TermEquals(Get(left, width, slot), Get(right, width, slot)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two solutions bind some variable in common: MINUS's condition.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static bool ShareVariable(int width, ulong[] left, ulong[] right)
    {
        for (int slot = 0; slot < width; slot++)
        {
            if (left[slot] != 0 && right[slot] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The merge of two compatible solutions: each slot from whichever binds it.</summary>
    internal static ulong[] Merge(int width, ulong[] left, ulong[] right)
    {
        ulong[] merged = Copy(left);
        for (int slot = 0; slot < width; slot++)
        {
            if (merged[slot] == 0 && right[slot] != 0)
            {
                Set(merged, width, slot, Get(right, width, slot));
            }
        }

        return merged;
    }
}
